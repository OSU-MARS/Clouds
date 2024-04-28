using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster, IRasterSerializable<DigitalSurfaceModel>
    {
        private static readonly string DiagnosticDirectoryPointCounts = "nPoints";
        private static readonly string DiagnosticDirectorySourceID = "sourceID";
        private static readonly string DiagnosticDirectoryZ = "z";

        // primary data bands
        public RasterBand<float> Surface { get; private init; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private init; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private init; } // canopy height model obtained from DSM - DTM

        // diagnostic bands in z
        // Digital terrain model is assumed to be stored separately.
        public RasterBand<float>? Layer1 { get; private init; } // top elevation of uppermost layer surface, if identified
        public RasterBand<float>? Layer2 { get; private init; } // top elevation of next layer below layer 1, if present
        public RasterBand<float>? Ground { get; private init; } // mean elevation of ground points in cell

        // diagnostic bands: point counts
        public RasterBand<UInt32>? AerialPoints { get; private init; } // number of points in cell not classified as ground
        public RasterBand<UInt32>? GroundPoints { get; private init; } // number of ground points in cell

        // diagnostic bands: source IDs
        public RasterBand<UInt16>? SourceIDSurface { get; private init; }
        public RasterBand<UInt16>? SourceIDLayer1 { get; private init; }
        public RasterBand<UInt16>? SourceIDLayer2 { get; private init; }

        public DigitalSurfaceModel(string filePath, PointListXyzcs tilePoints, RasterBand<float> dtmTile, PointListGridZs aerialPointZs, float minimumLayerSeparation)
            : base(dtmTile)
        {
            this.FilePath = filePath;

            // tilePoints must be in the same CRS as the DTM but can have any extent equal to or smaller than the DSM and DTM tiles
            // If the points extend beyond the DSM/DTM tile then ToInteriorGridIndices() will throw while points are being read.
            if (SpatialReferenceExtensions.IsSameCrs(tilePoints.Crs, dtmTile.Crs) == false)
            {
                string tileName = Tile.GetName(filePath);
                throw new NotSupportedException(tileName + ": the point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + tilePoints.Crs.GetName() + "' while the DTM CRS is " + dtmTile.Crs.GetName() + ".");
            }
            if (dtmTile.IsSameExtentAndSpatialResolution(aerialPointZs.Z) == false)
            {
                string tileName = Tile.GetName(filePath);
                throw new NotSupportedException(tileName + ": DTM tiles and aerial point list grid are currently required to be aligned. The point list grid extent is (" + aerialPointZs.Z.GetExtentString() + ") while the DTM extent is (" + dtmTile.GetExtentString() + ").");
            }

            this.Surface = new(this, "dsm", RasterBand.NoDataDefaultFloat);
            this.CanopyMaxima3 = new(this, "cmm3", RasterBand.NoDataDefaultFloat);
            this.CanopyHeight = new(this, "chm", RasterBand.NoDataDefaultFloat);
            this.Layer1 = new(this, "layer1", RasterBand.NoDataDefaultFloat);
            this.Layer2 = new(this, "layer2", RasterBand.NoDataDefaultFloat);
            this.Ground = new(this, "ground", RasterBand.NoDataDefaultFloat);
            this.AerialPoints = new(this, "nAerial", RasterBand.NoDataDefaultUInt32); // leave at default of zero
            this.GroundPoints = new(this, "nGround", RasterBand.NoDataDefaultUInt32); // leave at default of zero
            this.SourceIDSurface = new(this, "sourceIDsurface", 0); // leave at default of zero as source IDs 1-65535 are valid
            this.SourceIDLayer1 = new(this, "sourceIDlayer1", 0);
            this.SourceIDLayer2 = new(this, "sourceIDlayer2", 0);

            // build aerial point lists and accumulate ground points
            double xOffset = tilePoints.XOffset;
            double yOffset = tilePoints.YOffset;
            float zOffset = tilePoints.ZOffset;
            double xScale = tilePoints.XScaleFactor;
            double yScale = tilePoints.YScaleFactor;
            float zScale = tilePoints.ZScaleFactor;
            for (int pointIndex = 0; pointIndex < tilePoints.Count; ++pointIndex)
            {
                // TODO: Can x and y be transformed to cell indices relative to the tile origin with integer math?
                double x = xOffset + xScale * tilePoints.X[pointIndex];
                double y = yOffset + yScale * tilePoints.Y[pointIndex];
                (int xIndex, int yIndex) = this.ToInteriorGridIndices(x, y);

                PointClassification classification = tilePoints.Classification[pointIndex];
                float z = zOffset + zScale * tilePoints.Z[pointIndex];
                if (classification == PointClassification.Ground)
                {
                    // TODO: support PointClassification.{ Rail, RoadSurface, IgnoredGround }
                    UInt32 groundPoints = this.GroundPoints[xIndex, yIndex];
                    if (groundPoints == 0)
                    {
                        this.Ground[xIndex, yIndex] = z;
                    }
                    else
                    {
                        this.Ground[xIndex, yIndex] += z;
                    }

                    this.GroundPoints[xIndex, yIndex] = groundPoints + 1;
                }
                else
                {
                    // for now, assume all non-ground point types are aerial (noise and withheld points are excluded above)
                    // PointClassification.{ NeverClassified , Unclassified, LowVegetation, MediumVegetation, HighVegetation, Building,
                    //                       ModelKeyPoint, OverlapPoint, WireGuard, WireConductor, TransmissionTower, WireStructureConnector,
                    //                       BridgeDeck, OverheadStructure, Snow, TemporalExclusion }
                    // PointClassification.Water is currently also treated as non-ground, which is debatable
                    aerialPointZs.Z[xIndex, yIndex].Add(z);

                    UInt16 sourceID = tilePoints.SourceID[pointIndex];
                    aerialPointZs.SourceID[xIndex, yIndex].Add(sourceID);
                }
            }

            // find layers, set default DSM as the highest z in each cell, and set default CHM
            // This loop also sorts each cell's points from highest to lowest z.
            float[] aerialPoints = new float[128];
            int[] aerialPointSortIndices = new int[128];
            List<int> maxIndexByLayer = [];
            List<float> maxZbyLayer = [];
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                float dtmZ = dtmTile[cellIndex];
                if (dtmTile.IsNoData(dtmZ))
                {
                    // flow any DTM no data values
                    // Won't detect collisions between valid DTM data and default no data but it's unlikely a DTM would have NaN as valid data.
                    dtmZ = RasterBand.NoDataDefaultFloat;
                }

                UInt32 nGround = this.GroundPoints[cellIndex];
                if (nGround > 0)
                {
                    this.Ground[cellIndex] /= nGround;
                }
                else
                {
                    this.Ground[cellIndex] = RasterBand.NoDataDefaultFloat;
                }

                List<float> aerialPointsZ = aerialPointZs.Z[cellIndex];
                this.AerialPoints[cellIndex] = (UInt32)aerialPointsZ.Count;
                if (aerialPointsZ.Count == 0)
                {
                    // should the digital surface and canopy maxima models be set to ground height, if available?
                    // should the canopy height model be set to zero?
                    this.Surface[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyMaxima3[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyHeight[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer1[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave aerial point count at zero
                    // leave source IDs at zero
                    continue; // nothing else to do
                }

                if (aerialPoints.Length < aerialPointsZ.Count)
                {
                    int sizeIncreaseFactor = Int32.Max(2, aerialPointsZ.Count / aerialPoints.Length + 1);
                    aerialPoints = new float[sizeIncreaseFactor * aerialPoints.Length];
                    aerialPointSortIndices = new int[sizeIncreaseFactor * aerialPoints.Length];
                }

                aerialPointsZ.CopyTo(0, aerialPoints, 0, aerialPointsZ.Count);
                for (int pointIndex = 0; pointIndex < aerialPointsZ.Count; ++pointIndex)
                {
                    aerialPointSortIndices[pointIndex] = pointIndex;
                }
                Array.Sort(aerialPoints, aerialPointSortIndices, 0, aerialPointsZ.Count); // ascending unstable introsort

                float zMax = aerialPoints[aerialPointsZ.Count - 1];
                float zPrevious = zMax;
                maxIndexByLayer.Clear();
                maxZbyLayer.Clear();
                for (int pointIndex = aerialPointsZ.Count - 2; pointIndex >= 0; --pointIndex)
                {
                    float z = aerialPoints[pointIndex];
                    float zDelta = zPrevious - z;
                    if (zDelta > minimumLayerSeparation)
                    {
                        maxIndexByLayer.Add(pointIndex);
                        maxZbyLayer.Add(z);
                    }

                    zPrevious = z;
                }

                // open questions
                // - How should canopy height be defined when the DTM and mean ground elevation differ substantially?
                // - (niche case) What if DTM is no data but a ground elevation is available?
                List<UInt16> aerialPointsSourceID = aerialPointZs.SourceID[cellIndex];
                this.Surface[cellIndex] = zMax;
                this.SourceIDSurface[cellIndex] = aerialPointsSourceID[aerialPointSortIndices[aerialPointsZ.Count - 1]];
                this.CanopyHeight[cellIndex] = zMax - dtmZ; 

                if (maxZbyLayer.Count > 0)
                {
                    this.Layer1[cellIndex] = maxZbyLayer[0];
                    this.SourceIDLayer1[cellIndex] = aerialPointsSourceID[aerialPointSortIndices[maxIndexByLayer[0]]];

                    if (maxZbyLayer.Count > 1)
                    {
                        this.Layer2[cellIndex] = maxZbyLayer[1];
                        this.SourceIDLayer2[cellIndex] = aerialPointsSourceID[aerialPointSortIndices[maxIndexByLayer[1]]];
                    }
                    else
                    {
                        this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                        // leave layer 2 source ID as zero
                    }
                }
                else
                {
                    this.Layer1[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave both layers' source IDs as zero
                }
            }

            // for now, population of the canopy maxima model is deferred until a virtual raster neighborhood is available
            // Binomial.Smooth3x3(this.Surface, this.CanopyMaxima3);
        }

        public DigitalSurfaceModel(Dataset dsmDataset, bool loadData) :
            base(dsmDataset)
        {
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case "dsm":
                        this.Surface = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "cmm3":
                        this.CanopyMaxima3 = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "chm":
                        this.CanopyHeight = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "layer1":
                        this.Layer1 = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "layer2":
                        this.Layer2 = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "ground":
                        this.Ground = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "nAerial":
                        this.AerialPoints = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "nGround":
                        this.GroundPoints = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "sourceIDsurface":
                        this.SourceIDSurface = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "sourceIDlayer1":
                        this.SourceIDLayer1 = new(dsmDataset, gdalBand, loadData);
                        break;
                    case "sourceIDlayer2":
                        this.SourceIDLayer2 = new(dsmDataset, gdalBand, loadData);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "'.");
                }
            }
            
            if (this.Surface == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'dsm'.");
            }
            if (this.CanopyMaxima3 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'cmm3'.");
            }
            if (this.CanopyHeight == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'chm'.");
            }
            if (this.Layer1 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'layer1'.");
            }
            if (this.Layer2 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'layer2'.");
            }
            if (this.Ground == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'ground'.");
            }
            if (this.AerialPoints == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'nAerial'.");
            }
            if (this.GroundPoints == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'nGround'.");
            }
            if (this.SourceIDSurface == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'sourceIDsurface'.");
            }
            if (this.SourceIDLayer1 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'sourceIDlayer1'.");
            }
            if (this.SourceIDLayer2 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'sourceIDlayer2'.");
            }
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Surface;
            yield return this.CanopyMaxima3;
            yield return this.CanopyHeight;

            if (this.Layer1 != null)
            {
                yield return this.Layer1;
            }
            if (this.Layer2 != null)
            {
                yield return this.Layer2;
            }
            if (this.Ground != null)
            {
                yield return this.Ground;
            }

            if (this.AerialPoints != null)
            {
                yield return this.AerialPoints;
            }
            if (this.GroundPoints != null)
            {
                yield return this.GroundPoints;
            }

            if (this.SourceIDSurface != null)
            {
                yield return this.SourceIDSurface;
            }
            if (this.SourceIDLayer1 != null)
            {
                yield return this.SourceIDLayer1;
            }
            if (this.SourceIDLayer2 != null)
            {
                yield return this.SourceIDLayer2;
            }
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.Surface.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.CanopyMaxima3.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(name, this.CanopyHeight.Name, StringComparison.Ordinal))
            {
                return 2;
            }

            if ((this.Layer1 != null) && String.Equals(name, this.Layer1.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if ((this.Layer2 != null) && String.Equals(name, this.Layer2.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if ((this.Ground != null) && String.Equals(name, this.Ground.Name, StringComparison.Ordinal))
            {
                return 2;
            }

            if ((this.AerialPoints != null) && String.Equals(name, this.AerialPoints.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if ((this.GroundPoints != null) && String.Equals(name, this.GroundPoints.Name, StringComparison.Ordinal))
            {
                return 1;
            }

            if ((this.SourceIDSurface != null) && String.Equals(name, this.SourceIDSurface.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if ((this.SourceIDLayer1 != null) && String.Equals(name, this.SourceIDLayer1.Name, StringComparison.Ordinal))
            {
                return 2;
            }
            if ((this.SourceIDLayer2 != null) && String.Equals(name, this.SourceIDLayer2.Name, StringComparison.Ordinal))
            {
                return 3;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        private static string GetDiagnosticTilePath(string primaryTilePath, string diagnosticDirectoryName, bool createDiagnosticDirectory)
        {
            string? dsmDirectoryPath = Path.GetDirectoryName(primaryTilePath);
            if (dsmDirectoryPath == null)
            {
                throw new ArgumentOutOfRangeException(nameof(primaryTilePath), "DSM primary tile path '" + primaryTilePath + "' does not contain a directory.");
            }
            string? dsmTileName = Path.GetFileName(primaryTilePath);
            if (dsmTileName == null)
            {
                throw new ArgumentOutOfRangeException(nameof(primaryTilePath), "DSM primary tile path '" + primaryTilePath + "' does not contain a file name.");
            }

            string diagnosticDirectoryPath = Path.Combine(dsmDirectoryPath, diagnosticDirectoryName);
            if (Directory.Exists(diagnosticDirectoryPath) == false)
            {
                if (createDiagnosticDirectory)
                {
                    Directory.CreateDirectory(diagnosticDirectoryPath);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(diagnosticDirectoryName), "Directory '" + dsmDirectoryPath + "' does not have a '" + diagnosticDirectoryName + "' subdirectory for diagnostic tiles.");
                }
            }

            string diagnosticTilePath = Path.Combine(diagnosticDirectoryPath, dsmTileName);
            return diagnosticTilePath;
        }

        public static DigitalSurfaceModel Read(string rasterPath, bool loadData)
        {
            using Dataset dsmDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            if (dsmDataset.RasterCount != 12)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterPath), "'" + rasterPath + "' is not a 12 band DSM raster.");
            }

            return new(dsmDataset, loadData);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || (String.Equals(this.Surface.Name, name, StringComparison.Ordinal)))
            {
                band = this.Surface;
            }
            else if (String.Equals(this.CanopyMaxima3.Name, name, StringComparison.Ordinal))
            {
                band = this.CanopyMaxima3;
            }
            else if (String.Equals(this.CanopyHeight.Name, name, StringComparison.Ordinal))
            {
                band = this.CanopyHeight;
            }
            else if ((this.Layer1 != null) && String.Equals(this.Layer1.Name, name, StringComparison.Ordinal))
            {
                band = this.Layer1;
            }
            else if ((this.Layer2 != null) && String.Equals(this.Layer2.Name, name, StringComparison.Ordinal))
            {
                band = this.Layer2;
            }
            else if ((this.Ground != null) && String.Equals(this.Ground.Name, name, StringComparison.Ordinal))
            {
                band = this.Ground;
            }
            else if ((this.AerialPoints != null) && String.Equals(this.AerialPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialPoints;
            }
            else if ((this.GroundPoints != null) && String.Equals(this.GroundPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundPoints;
            }
            else if ((this.SourceIDSurface != null) && String.Equals(this.SourceIDSurface.Name, name, StringComparison.Ordinal))
            {
                band = this.SourceIDSurface;
            }
            else if ((this.SourceIDLayer1 != null) && String.Equals(this.SourceIDLayer1.Name, name, StringComparison.Ordinal))
            {
                band = this.SourceIDLayer1;
            }
            else if ((this.SourceIDLayer2 != null) && String.Equals(this.SourceIDLayer2.Name, name, StringComparison.Ordinal))
            {
                band = this.SourceIDLayer2;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override void Write(string dsmPrimaryTilePath, bool compress)
        {
            // primary data bands
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.Surface.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyMaxima3.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyHeight.IsNoData(RasterBand.NoDataDefaultFloat));
            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(dsmPrimaryTilePath, 3, DataType.GDT_Float32, compress);
            this.WriteBand(dsmDataset, this.Surface, 1);
            this.WriteBand(dsmDataset, this.CanopyMaxima3, 2);
            this.WriteBand(dsmDataset, this.CanopyHeight, 3);
            this.FilePath = dsmPrimaryTilePath;

            // diagnostic bands in z
            // Bands are single precision floating point as that is the minimum supported size.
            if ((this.Layer1 != null) && (this.Layer2 != null) && (this.Ground != null))
            {
                Debug.Assert(this.Layer1.IsNoData(RasterBand.NoDataDefaultFloat) && this.Layer2.IsNoData(RasterBand.NoDataDefaultFloat) && this.Ground.IsNoData(RasterBand.NoDataDefaultFloat));
                string zDiagnosticTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPrimaryTilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);

                using Dataset zDiagnosticDataset = this.CreateGdalRasterAndSetFilePath(zDiagnosticTilePath, 3, DataType.GDT_Float32, compress);
                this.WriteBand(zDiagnosticDataset, this.Layer1, 1);
                this.WriteBand(zDiagnosticDataset, this.Layer2, 2);
                this.WriteBand(zDiagnosticDataset, this.Ground, 3);
            }
            else if ((this.Layer1 != null) || (this.Layer2 != null) || (this.Ground != null))
            {
                throw new NotSupportedException("Diagnostic bands in z were not be written as only one or two of the three layers is available.");
            }

            // diagnostic bands: point counts
            // Bands are unsigned 8, 16, or 32 bit integers depending on the largest value counted. 8 and 16 bit cases could potentially
            // be merged with source ID tiles but are not.
            if ((this.AerialPoints != null) && (this.GroundPoints != null))
            {
                Debug.Assert(this.AerialPoints.IsNoData(RasterBand.NoDataDefaultUInt32) && this.GroundPoints.IsNoData(RasterBand.NoDataDefaultUInt32));
                string pointCountDiagnosticTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPrimaryTilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(this.AerialPoints, this.GroundPoints);

                using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountDiagnosticTilePath, 2, pointCountBandType, compress);
                this.WriteBand(pointCountDataset, this.AerialPoints, 1);
                this.WriteBand(pointCountDataset, this.GroundPoints, 2);
            }
            else if ((this.AerialPoints != null) || (this.GroundPoints != null))
            {
                throw new NotSupportedException("Point count bands were not be written as only one of the two layers is available.");
            }

            // diagnostic bands: source IDs
            // For now, write bands even if the maximum source ID is zero.
            if ((this.SourceIDSurface != null) && (this.SourceIDLayer1 != null) && (this.SourceIDLayer2 != null))
            {
                Debug.Assert(this.SourceIDSurface.IsNoData(0) && this.SourceIDLayer1.IsNoData(0) && this.SourceIDLayer2.IsNoData(0));
                string sourceIDdiagnosticTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPrimaryTilePath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: true);
                DataType sourceIDbandType = DataTypeExtensions.GetMostCompactIntegerType(this.SourceIDSurface, this.SourceIDLayer1, this.SourceIDLayer2);

                using Dataset sourceIDdataset = this.CreateGdalRasterAndSetFilePath(sourceIDdiagnosticTilePath, 3, sourceIDbandType, compress);
                this.WriteBand(sourceIDdataset, this.SourceIDSurface, 1);
                this.WriteBand(sourceIDdataset, this.SourceIDLayer1, 2);
                this.WriteBand(sourceIDdataset, this.SourceIDLayer2, 3);
            }
            else if ((this.SourceIDSurface != null) || (this.SourceIDLayer1 != null) || (this.SourceIDLayer2 != null))
            {
                throw new NotSupportedException("Source ID bands were not be written as only one or two of the three layers is available.");
            }
        }
    }
}