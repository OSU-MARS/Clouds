using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster, IFileSerializable<DigitalSurfaceModel>
    {
        public RasterBand<float> Surface { get; private init; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private init; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private init; } // canopy height model obtained from DSM - DTM
        public RasterBand<float> Layer1 { get; private init; } // top elevation of uppermost layer surface, if identified
        public RasterBand<float> Layer2 { get; private init; } // top elevation of next layer below layer 1, if present
        public RasterBand<float> Ground { get; private init; } // mean elevation of ground points in cell
        public RasterBand<float> Terrain { get; private init; } // ground elevation from digital terrain model
        public RasterBand<UInt32> AerialPoints { get; private init; } // number of points in cell not classified as ground
        public RasterBand<UInt32> GroundPoints { get; private init; } // number of ground points in cell
        public RasterBand<UInt16> SourceIDSurface { get; private init; }
        public RasterBand<UInt16> SourceIDLayer1 { get; private init; }
        public RasterBand<UInt16> SourceIDLayer2 { get; private init; }

        public DigitalSurfaceModel(string filePath, PointListXyzcs tilePoints, RasterBand<float> dtmTile, PointListGridZs aerialPointZs, float minimumLayerSeparation)
            : base(dtmTile)
        {
            // tilePoints must be in the same CRS as the DTM but can have any extent equal to or smaller than the DSM and DTM tiles
            // If the points extend beyond the DSM/DTM tile then ToInteriorGridIndices() will throw while points are being read.
            if (SpatialReferenceExtensions.IsSameCrs(tilePoints.Crs, dtmTile.Crs) == false)
            {
                string tileName = Tile.GetName(filePath);
                throw new NotSupportedException(tileName + ": the point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + tilePoints.Crs.GetName() + "' while the DTM CRS is " + dtmTile.Crs.GetName() + ".");
            }
            if (dtmTile.IsSameExtentAndResolution(aerialPointZs.Z) == false)
            {
                string tileName = Tile.GetName(filePath);
                throw new NotSupportedException(tileName + ": DTM tiles and aerial point list grid are currently required to be aligned. The point list grid extent is (" + aerialPointZs.Z.GetExtentString() + ") while the DTM extent is (" + dtmTile.GetExtentString() + ").");
            }

            this.Surface = new("dsm", this, RasterBand.NoDataDefaultFloat);
            this.CanopyMaxima3 = new("cmm3", this, RasterBand.NoDataDefaultFloat);
            this.CanopyHeight = new("chm", this, RasterBand.NoDataDefaultFloat);
            this.Layer1 = new("layer1", this, RasterBand.NoDataDefaultFloat);
            this.Layer2 = new("layer2", this, RasterBand.NoDataDefaultFloat);
            this.Ground = new("ground", this, RasterBand.NoDataDefaultFloat);
            this.Terrain = new("dtm", this, RasterBand.NoDataDefaultFloat);
            this.AerialPoints = new("nAerial", this, RasterBand.NoDataDefaultUInt32); // leave at default of zero
            this.GroundPoints = new("nGround", this, RasterBand.NoDataDefaultUInt32); // leave at default of zero
            this.SourceIDSurface = new("sourceIDsurface", this, 0); // leave at default of zero as source IDs 1-65535 are valid
            this.SourceIDLayer1 = new("sourceIDlayer1", this, 0);
            this.SourceIDLayer2 = new("sourceIDlayer2", this, 0);

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
                this.Terrain[cellIndex] = dtmZ;

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
                    this.Terrain[cellIndex] = RasterBand.NoDataDefaultFloat;
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

        public DigitalSurfaceModel(Dataset dsmDataset) :
            base(dsmDataset)
        {
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case "dsm":
                        this.Surface = new(gdalBand, this);
                        break;
                    case "cmm3":
                        this.CanopyMaxima3 = new(gdalBand, this);
                        break;
                    case "chm":
                        this.CanopyHeight = new(gdalBand, this);
                        break;
                    case "layer1":
                        this.Layer1 = new(gdalBand, this);
                        break;
                    case "layer2":
                        this.Layer2 = new(gdalBand, this);
                        break;
                    case "ground":
                        this.Ground = new(gdalBand, this);
                        break;
                    case "dtm":
                        this.Terrain = new(gdalBand, this);
                        break;
                    case "nAerial":
                        this.AerialPoints = new(gdalBand, this);
                        break;
                    case "nGround":
                        this.GroundPoints = new(gdalBand, this);
                        break;
                    case "sourceIDsurface":
                        this.SourceIDSurface = new(gdalBand, this);
                        break;
                    case "sourceIDlayer1":
                        this.SourceIDLayer1 = new(gdalBand, this);
                        break;
                    case "sourceIDlayer2":
                        this.SourceIDLayer2 = new(gdalBand, this);
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
            if (this.Terrain == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'dtm'.");
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

        public static DigitalSurfaceModel Read(string rasterPath)
        {
            using Dataset dsmDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            if (dsmDataset.RasterCount != 12)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterPath), "'" + rasterPath + "' is not a 12 band DSM raster.");
            }

            return new(dsmDataset);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (String.Equals(this.Surface.Name, name, StringComparison.Ordinal))
            {
                band = this.Surface;
            }
            else if (String.Equals(this.Ground.Name, name, StringComparison.Ordinal))
            {
                band = this.Ground;
            }
            else if (String.Equals(this.Terrain.Name, name, StringComparison.Ordinal))
            {
                band = this.Terrain;
            }
            else if (String.Equals(this.AerialPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialPoints;
            }
            else if (String.Equals(this.GroundPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundPoints;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override void Write(string rasterPath, bool compress)
        {
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.Surface.IsNoData(RasterBand.NoDataDefaultFloat) && this.Ground.IsNoData(RasterBand.NoDataDefaultFloat) && this.Terrain.IsNoData(RasterBand.NoDataDefaultFloat));

            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, 12, DataType.GDT_Float32, compress);
            this.WriteBand(dsmDataset, this.Surface, 1);
            this.WriteBand(dsmDataset, this.CanopyMaxima3, 2);
            this.WriteBand(dsmDataset, this.CanopyHeight, 3);
            this.WriteBand(dsmDataset, this.Layer1, 4);
            this.WriteBand(dsmDataset, this.Layer2, 5);
            this.WriteBand(dsmDataset, this.Ground, 6);
            this.WriteBand(dsmDataset, this.Terrain, 7);
            this.WriteBand(dsmDataset, this.AerialPoints, 8);
            this.WriteBand(dsmDataset, this.GroundPoints, 9);
            this.WriteBand(dsmDataset, this.SourceIDSurface, 10);
            this.WriteBand(dsmDataset, this.SourceIDLayer1, 11);
            this.WriteBand(dsmDataset, this.SourceIDLayer2, 12);
        }
    }
}