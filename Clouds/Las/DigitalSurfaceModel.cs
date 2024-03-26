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

        public DigitalSurfaceModel(Grid<PointListZs> tilePoints, RasterBand<float> dtmTile, float minimumLayerSeparation)
            : base(tilePoints)
        {
            if (tilePoints.IsSameExtentAndResolution(dtmTile) == false)
            {
                throw new NotSupportedException("Point grid and DTM tile extents aren't matched. LAS extent " + tilePoints.GetExtentString() + ", DTM extent " + dtmTile.GetExtentString() + ", DSM extent " + this.GetExtent() + ".");
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

                PointListZs? cellPoints = tilePoints[cellIndex];
                if (cellPoints == null)
                {
                    this.Surface[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyMaxima3[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyHeight[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer1[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Ground[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave point counts at zero
                    // leave source IDs at zero
                    // no cell point list to clear
                    continue;
                }

                if (cellPoints.GroundPoints == 0)
                {
                    this.Ground[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave ground points at zero
                }
                else
                {
                    this.Ground[cellIndex] = cellPoints.GroundMean;
                    this.GroundPoints[cellIndex] = cellPoints.GroundPoints;
                }

                int aerialPointCount = cellPoints.AerialPoints.Count;
                if (aerialPointCount == 0)
                {
                    this.Surface[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyMaxima3[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.CanopyHeight[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer1[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Ground[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Terrain[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave aerial point count at zero
                    continue;
                }
                else
                {
                    // maximum z value for DSM obtained below
                    this.AerialPoints[cellIndex] = (UInt32)aerialPointCount;
                }

                if (aerialPoints.Length < aerialPointCount)
                {
                    int sizeIncreaseFactor = Int32.Max(2, aerialPointCount / aerialPoints.Length + 1);
                    aerialPoints = new float[sizeIncreaseFactor * aerialPoints.Length];
                    aerialPointSortIndices = new int[sizeIncreaseFactor * aerialPoints.Length];
                }
                
                cellPoints.AerialPoints.CopyTo(0, aerialPoints, 0, aerialPointCount);
                for (int pointIndex = 0; pointIndex < aerialPointCount; ++pointIndex)
                {
                    aerialPointSortIndices[pointIndex] = pointIndex;
                }
                Array.Sort(aerialPoints, aerialPointSortIndices, 0, aerialPointCount); // ascending unstable introsort

                float zMax = aerialPoints[aerialPointCount - 1];
                float zPrevious = zMax;
                maxIndexByLayer.Clear();
                maxZbyLayer.Clear();
                for (int pointIndex = aerialPointCount - 2; pointIndex >= 0; --pointIndex)
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

                this.Surface[cellIndex] = zMax;
                this.SourceIDSurface[cellIndex] = cellPoints.AerialSourceIDs[aerialPointSortIndices[aerialPointCount - 1]];
                this.CanopyHeight[cellIndex] = zMax - dtmZ;

                if (maxZbyLayer.Count > 0)
                {
                    this.Layer1[cellIndex] = maxZbyLayer[0];
                    this.SourceIDLayer1[cellIndex] = cellPoints.AerialSourceIDs[aerialPointSortIndices[maxIndexByLayer[0]]];

                    if (maxZbyLayer.Count > 1)
                    {
                        this.Layer2[cellIndex] = maxZbyLayer[1];
                        this.SourceIDLayer2[cellIndex] = cellPoints.AerialSourceIDs[aerialPointSortIndices[maxIndexByLayer[1]]];
                    }
                    else
                    {
                        this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                        // leave source ID as zero
                    }
                }
                else
                {
                    this.Layer1[cellIndex] = RasterBand.NoDataDefaultFloat;
                    this.Layer2[cellIndex] = RasterBand.NoDataDefaultFloat;
                    // leave source IDs as zero
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