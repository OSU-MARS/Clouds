using DocumentFormat.OpenXml.Presentation;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster, IRasterSerializable<DigitalSurfaceModel>
    {
        private const string DiagnosticDirectoryPointCounts = "nPoints";
        private const string DiagnosticDirectorySourceID = "sourceID";
        private const string DiagnosticDirectoryZ = "z";

        public const string AerialPointsBandName = "nAerial";
        public const string CanopyHeightBandName = "chm";
        public const string CanopyMaximaBandName = "cmm3";
        public const string SurfaceBandName = "dsm";
        public const string Layer1BandName = "layer1";
        public const string Layer2BandName = "layer2";
        public const string GroundBandName = "ground";
        public const string GroundPointsBandName = "nGround";
        public const string SourceIDSurfaceBandName = "sourceIDsurface";
        public const string SourceIDLayer1BandName = "sourceIDlayer1";
        public const string SourceIDLayer2BandName = "sourceIDlayer2";

        // primary data bands
        public RasterBand<float> Surface { get; private set; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private set; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private set; } // canopy height model obtained from DSM - DTM

        // diagnostic bands in z
        // Digital terrain model is assumed to be stored separately.
        public RasterBand<float>? Layer1 { get; private set; } // top elevation of uppermost layer surface, if identified
        public RasterBand<float>? Layer2 { get; private set; } // top elevation of next layer below layer 1, if present
        public RasterBand<float>? Ground { get; private set; } // mean elevation of ground points in cell

        // diagnostic bands: point counts
        public RasterBand<UInt32>? AerialPoints { get; private set; } // number of points in cell not classified as ground
        public RasterBand<UInt32>? GroundPoints { get; private set; } // number of ground points in cell

        // diagnostic bands: source IDs
        public RasterBand<UInt16>? SourceIDSurface { get; private set; }
        public RasterBand<UInt16>? SourceIDLayer1 { get; private set; }
        public RasterBand<UInt16>? SourceIDLayer2 { get; private set; }

        public DigitalSurfaceModel(string filePath, PointList<PointBatchXyzcs> tilePoints, RasterBand<float> dtmTile, PointListGridZs aerialPointZs, float minimumLayerSeparation)
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
            if (dtmTile.IsSameExtentAndSpatialResolution(aerialPointZs.ZSourceID) == false)
            {
                string tileName = Tile.GetName(filePath);
                throw new NotSupportedException(tileName + ": DTM tiles and aerial point list grid are currently required to be aligned. The point list grid extent is (" + aerialPointZs.ZSourceID.GetExtentString() + ") while the DTM extent is (" + dtmTile.GetExtentString() + ").");
            }

            this.Surface = new(this, DigitalSurfaceModel.SurfaceBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.CanopyMaxima3 = new(this, DigitalSurfaceModel.CanopyMaximaBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.CanopyHeight = new(this, DigitalSurfaceModel.CanopyHeightBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.Layer1 = new(this, DigitalSurfaceModel.Layer1BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.Layer2 = new(this, DigitalSurfaceModel.Layer2BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.Ground = new(this, DigitalSurfaceModel.GroundBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.AerialPoints = new(this, DigitalSurfaceModel.AerialPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            this.GroundPoints = new(this, DigitalSurfaceModel.GroundPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            this.SourceIDSurface = new(this, DigitalSurfaceModel.SourceIDSurfaceBandName, 0, RasterBandInitialValue.NoData); // set no data to zero and leave at default of zero as LAS spec defines source IDs 1-65535 as valid
            this.SourceIDLayer1 = new(this, DigitalSurfaceModel.SourceIDLayer1BandName, 0, RasterBandInitialValue.NoData);
            this.SourceIDLayer2 = new(this, DigitalSurfaceModel.SourceIDLayer2BandName, 0, RasterBandInitialValue.NoData);

            // build aerial point lists and accumulate ground points
            double xOffset = tilePoints.XOffset;
            double yOffset = tilePoints.YOffset;
            float zOffset = tilePoints.ZOffset;
            double xScale = tilePoints.XScaleFactor;
            double yScale = tilePoints.YScaleFactor;
            float zScale = tilePoints.ZScaleFactor;
            for (int batchIndex = 0; batchIndex < tilePoints.Count; ++batchIndex)
            {
                PointBatchXyzcs pointBatch = tilePoints[batchIndex];
                for (int pointIndex = 0; pointIndex < pointBatch.Count; ++pointIndex)
                {
                    // TODO: Can x and y be transformed to cell indices relative to the tile origin with integer math?
                    double x = xOffset + xScale * pointBatch.X[pointIndex];
                    double y = yOffset + yScale * pointBatch.Y[pointIndex];
                    (int xIndex, int yIndex) = this.ToInteriorGridIndices(x, y);

                    PointClassification classification = pointBatch.Classification[pointIndex];
                    float z = zOffset + zScale * pointBatch.Z[pointIndex];
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
                        aerialPointZs.ZSourceID[xIndex, yIndex].Add((z, pointBatch.SourceID[pointIndex]));
                    }
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

                List<(float Z, UInt16 SourceID)> aerialPointsZs = aerialPointZs.ZSourceID[cellIndex];
                this.AerialPoints[cellIndex] = (UInt32)aerialPointsZs.Count;
                if (aerialPointsZs.Count == 0)
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

                if (aerialPoints.Length < aerialPointsZs.Count)
                {
                    int sizeIncreaseFactor = Int32.Max(2, aerialPointsZs.Count / aerialPoints.Length + 1);
                    aerialPoints = new float[sizeIncreaseFactor * aerialPoints.Length];
                    aerialPointSortIndices = new int[sizeIncreaseFactor * aerialPoints.Length];
                }

                //aerialPointsZ.CopyTo(0, aerialPoints, 0, aerialPointsZ.Count);
                for (int pointIndex = 0; pointIndex < aerialPointsZs.Count; ++pointIndex)
                {
                    aerialPoints[pointIndex] = aerialPointsZs[pointIndex].Z;
                    aerialPointSortIndices[pointIndex] = pointIndex;
                }
                Array.Sort(aerialPoints, aerialPointSortIndices, 0, aerialPointsZs.Count); // ascending unstable introsort

                float zMax = aerialPoints[aerialPointsZs.Count - 1];
                float zPrevious = zMax;
                maxIndexByLayer.Clear();
                maxZbyLayer.Clear();
                for (int pointIndex = aerialPointsZs.Count - 2; pointIndex >= 0; --pointIndex)
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
                this.Surface[cellIndex] = zMax;
                this.SourceIDSurface[cellIndex] = aerialPointsZs[aerialPointSortIndices[aerialPointsZs.Count - 1]].SourceID;
                this.CanopyHeight[cellIndex] = zMax - dtmZ; 

                if (maxZbyLayer.Count > 0)
                {
                    this.Layer1[cellIndex] = maxZbyLayer[0];
                    this.SourceIDLayer1[cellIndex] = aerialPointsZs[aerialPointSortIndices[maxIndexByLayer[0]]].SourceID;

                    if (maxZbyLayer.Count > 1)
                    {
                        this.Layer2[cellIndex] = maxZbyLayer[1];
                        this.SourceIDLayer2[cellIndex] = aerialPointsZs[aerialPointSortIndices[maxIndexByLayer[1]]].SourceID;
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

        public DigitalSurfaceModel(Dataset dsmDataset, bool readData)
            : base(dsmDataset)
        {
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalSurfaceModel.SurfaceBandName:
                        this.Surface = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.CanopyMaximaBandName:
                        this.CanopyMaxima3 = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.CanopyHeightBandName:
                        this.CanopyHeight = new(dsmDataset, gdalBand, readData);
                        break;
                    // only required bands are expected in the primary dataset but diagnostic bands can be supported if needed
                    //case DigitalSurfaceModel.Layer1BandName:
                    //    this.Layer1 = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.Layer2BandName:
                    //    this.Layer2 = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.GroundBandName:
                    //    this.Ground = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.AerialPointsBandName:
                    //    this.AerialPoints = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.GroundPointsBandName:
                    //    this.GroundPoints = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.SourceIDSurfaceBandName:
                    //    this.SourceIDSurface = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.SourceIDLayer1BandName:
                    //    this.SourceIDLayer1 = new(dsmDataset, gdalBand, readData);
                    //    break;
                    //case DigitalSurfaceModel.SourceIDLayer2BandName:
                    //    this.SourceIDLayer2 = new(dsmDataset, gdalBand, readData);
                    //    break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "'.");
                }
            }
            
            // required bands
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

            // other bands are optional and can remain null
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

        /// <summary>
        /// Read specified digital surface model bands. Required bands (<see cref="DigitalSufaceModelBands.Required"/>) must always be requested.
        /// </summary>
        /// <remarks>
        /// For now, does not modify bands whose flags are not set. They will be left as null if never loaded or with previous data if
        /// previously loaded.
        /// </remarks>
        public void Read(DigitalSufaceModelBands bands, ObjectPool<DigitalSurfaceModel> tilePool)
        {
            if ((bands & DigitalSufaceModelBands.Required) != DigitalSufaceModelBands.Required)
            {
                throw new ArgumentNullException(nameof(bands), "Required bands must be set on " + bands + ".");
            }

            // alleviate memory pressure by offloading GC in streaming virtual raster reads
            // If a previously used tile has been returned into the object pool, capture its required band data arrays and any
            // unpopulated diagnostic bands. Not very useful if tile sizes depart from the virtual raster convention of all being
            // identical size.
            if (tilePool.TryGetThreadSafe(out DigitalSurfaceModel? unusedTile))
            {
                this.Surface.TakeOwnershipOfDataArray(unusedTile.Surface);
                this.CanopyMaxima3.TakeOwnershipOfDataArray(unusedTile.CanopyMaxima3);
                this.CanopyHeight.TakeOwnershipOfDataArray(unusedTile.CanopyHeight);

                if ((this.Layer1 == null) && (unusedTile.Layer1 != null))
                {
                    this.Layer1 = unusedTile.Layer1;
                    unusedTile.Layer1 = null;
                }
                if ((this.Layer2 == null) && (unusedTile.Layer2 != null))
                {
                    this.Layer2 = unusedTile.Layer2;
                    unusedTile.Layer2 = null;
                }
                if ((this.Ground == null) && (unusedTile.Ground != null))
                {
                    this.Ground = unusedTile.Ground;
                    unusedTile.Ground = null;
                }

                if ((this.AerialPoints == null) && (unusedTile.AerialPoints != null))
                {
                    this.AerialPoints = unusedTile.AerialPoints;
                    unusedTile.AerialPoints = null;
                }
                if ((this.GroundPoints == null) && (unusedTile.GroundPoints != null))
                {
                    this.GroundPoints = unusedTile.GroundPoints;
                    unusedTile.GroundPoints = null;
                }

                if ((this.SourceIDSurface == null) && (unusedTile.SourceIDSurface != null))
                {
                    this.SourceIDSurface = unusedTile.SourceIDSurface;
                    unusedTile.SourceIDSurface = null;
                }
                if ((this.SourceIDLayer1 == null) && (unusedTile.SourceIDLayer1 != null))
                {
                    this.SourceIDLayer1 = unusedTile.SourceIDLayer1;
                    unusedTile.SourceIDLayer1 = null;
                }
                if ((this.SourceIDLayer2 == null) && (unusedTile.SourceIDLayer2 != null))
                {
                    this.SourceIDLayer2 = unusedTile.SourceIDLayer2;
                    unusedTile.SourceIDLayer2 = null;
                }

                // rest of unused tile is discarded
                // Raster and raster band parts should be well under 85 kB and thus on the GC's small object heaps.
            }

            using Dataset dsmDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            Debug.Assert((this.SizeX == dsmDataset.RasterXSize) && (this.SizeY == dsmDataset.RasterYSize) && SpatialReferenceExtensions.IsSameCrs(this.Crs, dsmDataset.GetSpatialRef()));
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalSurfaceModel.SurfaceBandName:
                        Debug.Assert(this.Surface.IsNoData(gdalBand));
                        this.Surface.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case DigitalSurfaceModel.CanopyMaximaBandName:
                        Debug.Assert(this.CanopyMaxima3.IsNoData(gdalBand));
                        this.CanopyMaxima3.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case DigitalSurfaceModel.CanopyHeightBandName:
                        Debug.Assert(this.CanopyHeight.IsNoData(gdalBand));
                        this.CanopyHeight.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in DSM tile '" + this.FilePath + ".");
                }
            }

            if ((bands & DigitalSufaceModelBands.DiagnosticZ) != DigitalSufaceModelBands.None)
            {
                string zTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);
                using Dataset zDataset = Gdal.Open(zTilePath, Access.GA_ReadOnly);
                SpatialReference crs = zDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= zDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = zDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.Layer1BandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.Layer1))
                            {
                                if (this.Layer1 == null)
                                {
                                    this.Layer1 = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.Layer1.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.Layer2BandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.Layer2))
                            {
                                if (this.Layer2 == null)
                                {
                                    this.Layer2 = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.Layer2.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.GroundBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.Ground))
                            {
                                if (this.Ground == null)
                                {
                                    this.Ground = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.Ground.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in z diagnostics tile '" + zTilePath + "'.");
                    }
                }
            }

            if ((bands & DigitalSufaceModelBands.PointCounts) != DigitalSufaceModelBands.None)
            {
                string pointCountTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                using Dataset pointCountDataset = Gdal.Open(pointCountTilePath, Access.GA_ReadOnly);
                SpatialReference crs = pointCountDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= pointCountDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = pointCountDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.AerialPointsBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.AerialPoints))
                            {
                                if (this.AerialPoints == null)
                                {
                                    this.AerialPoints = new(pointCountDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.AerialPoints.Read(pointCountDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.GroundPointsBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.GroundPoints))
                            {
                                if (this.GroundPoints == null)
                                {
                                    this.GroundPoints = new(pointCountDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.GroundPoints.Read(pointCountDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in point count diagnostics tile '" + pointCountTilePath + "'.");
                    }
                }
            }

            if ((bands & DigitalSufaceModelBands.SourceIDs) != DigitalSufaceModelBands.None)
            {
                string sourceIDtilePath = DigitalSurfaceModel.GetDiagnosticTilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: false);
                using Dataset sourceIDdataset = Gdal.Open(sourceIDtilePath, Access.GA_ReadOnly);
                SpatialReference crs = sourceIDdataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= sourceIDdataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = sourceIDdataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SourceIDLayer1BandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.SourceIDLayer1))
                            {
                                if (this.SourceIDLayer1 == null)
                                {
                                    this.SourceIDLayer1 = new(sourceIDdataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.SourceIDLayer1.Read(sourceIDdataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.SourceIDLayer2BandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.SourceIDLayer2))
                            {
                                if (this.SourceIDLayer2 == null)
                                {
                                    this.SourceIDLayer2 = new(sourceIDdataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.SourceIDLayer2.Read(sourceIDdataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.SourceIDSurfaceBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.SourceIDSurface))
                            {
                                if (this.SourceIDSurface == null)
                                {
                                    this.SourceIDSurface = new(sourceIDdataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.SourceIDSurface.Read(sourceIDdataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in source ID diagnostics tile '" + sourceIDtilePath + "'.");
                    }
                }
            }
        }

        public static DigitalSurfaceModel Read(string rasterPath, bool readData)
        {
            using Dataset dsmDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            return new(dsmDataset, readData);
        }

        public void ResetAllBandsToDefaultValues()
        {
            Debug.Assert(this.Surface.HasNoDataValue && this.CanopyMaxima3.HasNoDataValue && this.CanopyHeight.HasNoDataValue);

            Array.Fill(this.Surface.Data, this.Surface.NoDataValue);
            Array.Fill(this.CanopyMaxima3.Data, this.CanopyMaxima3.NoDataValue);
            Array.Fill(this.CanopyHeight.Data, this.CanopyHeight.NoDataValue);

            if (this.Layer1 != null) 
            {
                Array.Fill(this.Layer1.Data, this.Layer1.NoDataValue);
            }
            if (this.Layer2 != null)
            {
                Array.Fill(this.Layer2.Data, this.Layer2.NoDataValue);
            }
            if (this.Ground != null)
            {
                Array.Fill(this.Ground.Data, this.Ground.NoDataValue);
            }

            if (this.AerialPoints != null)
            {
                Array.Fill(this.AerialPoints.Data, 0U);
            }
            if (this.GroundPoints != null)
            {
                Array.Fill(this.GroundPoints.Data, 0U);
            }

            if (this.SourceIDSurface != null)
            {
                Array.Fill(this.SourceIDSurface.Data, this.SourceIDSurface.NoDataValue);
            }
            if (this.SourceIDLayer1 != null)
            {
                Array.Fill(this.SourceIDLayer1.Data, this.SourceIDLayer1.NoDataValue);
            }
            if (this.SourceIDLayer2 != null)
            {
                Array.Fill(this.SourceIDLayer2.Data, this.SourceIDLayer2.NoDataValue);
            }
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

        public override void Write(string dsmPath, bool compress)
        {
            // primary data bands
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.Surface.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyMaxima3.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyHeight.IsNoData(RasterBand.NoDataDefaultFloat));
            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(dsmPath, 3, DataType.GDT_Float32, compress);
            this.Surface.Write(dsmDataset, 1);
            this.CanopyMaxima3.Write(dsmDataset, 2);
            this.CanopyHeight.Write(dsmDataset, 3);
            this.FilePath = dsmPath;

            // diagnostic bands in z
            // Bands are single precision floating point as that is the minimum supported size.
            if ((this.Layer1 != null) && (this.Layer2 != null) && (this.Ground != null))
            {
                Debug.Assert(this.Layer1.IsNoData(RasterBand.NoDataDefaultFloat) && this.Layer2.IsNoData(RasterBand.NoDataDefaultFloat) && this.Ground.IsNoData(RasterBand.NoDataDefaultFloat));
                string zTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);

                using Dataset zDataset = this.CreateGdalRasterAndSetFilePath(zTilePath, 3, DataType.GDT_Float32, compress);
                this.Layer1.Write(zDataset, 1);
                this.Layer2.Write(zDataset, 2);
                this.Ground.Write(zDataset, 3);
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
                Debug.Assert((this.AerialPoints.HasNoDataValue == false) && (this.GroundPoints.HasNoDataValue == false));
                string pointCountTilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(this.AerialPoints, this.GroundPoints);

                using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountTilePath, 2, pointCountBandType, compress);
                this.AerialPoints.Write(pointCountDataset, 1);
                this.GroundPoints.Write(pointCountDataset, 2);
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
                string sourceIDtilePath = DigitalSurfaceModel.GetDiagnosticTilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: true);
                DataType sourceIDbandType = DataTypeExtensions.GetMostCompactIntegerType(this.SourceIDSurface, this.SourceIDLayer1, this.SourceIDLayer2);

                using Dataset sourceIDdataset = this.CreateGdalRasterAndSetFilePath(sourceIDtilePath, 3, sourceIDbandType, compress);
                this.SourceIDSurface.Write(sourceIDdataset, 1);
                this.SourceIDLayer1.Write(sourceIDdataset, 2);
                this.SourceIDLayer2.Write(sourceIDdataset, 3);
            }
            else if ((this.SourceIDSurface != null) || (this.SourceIDLayer1 != null) || (this.SourceIDLayer2 != null))
            {
                throw new NotSupportedException("Source ID bands were not be written as only one or two of the three layers is available.");
            }
        }
    }
}