using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster
    {
        private const string DiagnosticDirectoryPointCounts = "nPoints";
        private const string DiagnosticDirectoryReturnNumber = "returnNumber";
        private const string DiagnosticDirectorySourceID = "sourceID";
        private const string DiagnosticDirectoryZ = "z";
        public const string DirectorySlopeAspect = "slopeAspect";

        public const string SurfaceBandName = "dsm";
        public const string CanopyMaximaBandName = "cmm3";
        public const string CanopyHeightBandName = "chm";
        public const string CmmAspect3BandName = "cmmAspect3";
        public const string CmmSlope3BandName = "cmmSlope3";
        public const string DsmAspectBandName = "dsmAspect";
        public const string DsmSlopeBandName = "dsmSlope";
        public const string SubsurfaceBandName = "subsurfaceDsm";
        public const string AerialMeanBandName = "aerialMean";
        public const string GroundMeanBandName = "groundMean";
        public const string AerialPointsBandName = "nAerial";
        public const string GroundPointsBandName = "nGround";
        public const string ReturnNumberBandName = "returnNumberSurface";
        public const string SourceIDSurfaceBandName = "sourceIDsurface";
        public const int SubsurfaceBufferDepth = 8; // half a cache line per DSM cell

        // primary data bands
        // Digital terrain model can be calculated as DTM = DSM - CHM = Surface - CanopyHeight or stored separately.
        public RasterBand<float> Surface { get; private set; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private set; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private set; } // canopy height model obtained from DSM - DTM

        public DigitalSurfaceModelBands Bands { get; private set; }

        // slope and aspect bands
        public RasterBandSlope? DsmSlope { get; private set; }
        public RasterBandAspect? DsmAspect { get; private set; }
        public RasterBandSlope? CmmSlope3 { get; private set; }
        public RasterBandAspect? CmmAspect3 { get; private set; }

        // diagnostic bands in z
        public RasterBand<float>? Subsurface { get; private set; } // estimate of next surface layer below digital surface model
        public RasterBand<float>? AerialMean { get; private set; } // mean elevation of aerial points in cell
        public RasterBand<float>? GroundMean { get; private set; } // mean elevation of ground points in cell

        // diagnostic bands: point counts
        public RasterBand<UInt32>? AerialPoints { get; private set; } // number of points in cell not classified as ground
        public RasterBand<UInt32>? GroundPoints { get; private set; } // number of ground points in cell

        // diagnostic bands: return number
        public RasterBand<byte>? ReturnNumberSurface { get; private set; }

        // diagnostic bands: source IDs
        public RasterBand<UInt16>? SourceIDSurface { get; private set; }

        public DigitalSurfaceModel(string dsmFilePath, LasFile lasFile, DigitalSurfaceModelBands bands, RasterBand<float> dtmTile, RasterBandPool? dataBufferPool)
            : base(lasFile.GetSpatialReference(), dtmTile.Transform, dtmTile.SizeX, dtmTile.SizeY)
        {
            if ((bands & DigitalSurfaceModelBands.Primary) != DigitalSurfaceModelBands.Primary)
            {
                throw new ArgumentOutOfRangeException(nameof(bands), nameof(bands) + " must include " + nameof(DigitalSurfaceModelBands) + "." + nameof(DigitalSurfaceModelBands.Primary) + ".");
            }
            if (this.Crs.IsCompound() == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), dsmFilePath + ": point cloud's coordinate reference system (CRS) is not a compound CRS. Both a horizontal and vertical CRS are needed to fully geolocate a digital surface model's elevations.");
            }

            SpatialReference lasTileCrs = lasFile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(lasTileCrs, dtmTile.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtmTile), dsmFilePath + ": point clouds and DTMs are currently required to be in the same CRS. The point cloud CRS is '" + lasTileCrs.GetName() + "' while the DTM CRS is " + dtmTile.Crs.GetName() + ".");
            }

            this.Bands = DigitalSurfaceModelBands.Primary;
            this.FilePath = dsmFilePath;
            this.Surface = new(this, DigitalSurfaceModel.SurfaceBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            this.CanopyMaxima3 = new(this, DigitalSurfaceModel.CanopyMaximaBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            this.CanopyHeight = new(this, DigitalSurfaceModel.CanopyHeightBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);

            this.DsmSlope = null;
            this.DsmAspect = null;
            this.CmmSlope3 = null;
            this.CmmAspect3 = null;

            this.Subsurface = null;
            this.AerialMean = null;
            this.GroundMean = null;
            this.AerialPoints = null;
            this.GroundPoints = null;
            this.ReturnNumberSurface = null;
            this.SourceIDSurface = null;

            this.EnsureSupportingBandsCreated(bands, dataBufferPool);
        }

        private DigitalSurfaceModel(Dataset dsmDataset, DigitalSurfaceModelBands bands, bool readData, RasterBandPool? dataBufferPool)
            : base(dsmDataset)
        {
            if ((bands & DigitalSurfaceModelBands.Primary) != DigitalSurfaceModelBands.Primary)
            {
                throw new ArgumentOutOfRangeException(nameof(bands), nameof(bands) + " must include " + nameof(DigitalSurfaceModelBands) + "." + nameof(DigitalSurfaceModelBands.Primary) + ".");
            }

            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalSurfaceModel.SurfaceBandName:
                        this.Surface = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.Surface;
                        break;
                    case DigitalSurfaceModel.CanopyMaximaBandName:
                        this.CanopyMaxima3 = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.CanopyMaxima3;
                        break;
                    case DigitalSurfaceModel.CanopyHeightBandName:
                        this.CanopyHeight = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.CanopyHeight;
                        break;
                    // only primary bands are expected in the primary dataset but it's possible diagnostic bands have been merged
                    case DigitalSurfaceModel.DsmSlopeBandName:
                        this.DsmSlope = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.DsmSlope;
                        break;
                    case DigitalSurfaceModel.DsmAspectBandName:
                        this.DsmAspect = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.DsmAspect;
                        break;
                    case DigitalSurfaceModel.CmmSlope3BandName:
                        this.CmmSlope3 = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.CmmSlope3;
                        break;
                    case DigitalSurfaceModel.CmmAspect3BandName:
                        this.CmmAspect3 = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.CmmAspect3;
                        break;
                    case DigitalSurfaceModel.SubsurfaceBandName:
                        this.Subsurface = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.Subsurface;
                        break;
                    case DigitalSurfaceModel.AerialMeanBandName:
                        this.AerialMean = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.AerialMean;
                        break;
                    case DigitalSurfaceModel.GroundMeanBandName:
                        this.GroundMean = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.GroundMean;
                        break;
                    case DigitalSurfaceModel.AerialPointsBandName:
                        this.AerialPoints = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.AerialPoints;
                        break;
                    case DigitalSurfaceModel.GroundPointsBandName:
                        this.GroundPoints = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.GroundPoints;
                        break;
                    case DigitalSurfaceModel.ReturnNumberBandName:
                        this.ReturnNumberSurface = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.ReturnNumberSurface;
                        break;
                    case DigitalSurfaceModel.SourceIDSurfaceBandName:
                        this.SourceIDSurface = new(dsmDataset, gdalBand, readData);
                        this.Bands |= DigitalSurfaceModelBands.SourceIDSurface;
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "'.");
                }
            }
            
            // primary bands
            if (this.Surface == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named '" + DigitalSurfaceModel.SurfaceBandName + "'.");
            }
            if (this.CanopyMaxima3 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named '" + DigitalSurfaceModel.CanopyMaximaBandName + "'.");
            }
            if (this.CanopyHeight == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named '" + DigitalSurfaceModel.CanopyHeightBandName + "'.");
            }
            Debug.Assert((this.Bands & DigitalSurfaceModelBands.Primary) == DigitalSurfaceModelBands.Primary);

            // supporting bands
            this.EnsureSupportingBandsCreated(bands, dataBufferPool);
        }

        public void CalculateSlopeAndAspect(RasterNeighborhood8<float> dsmNeighborhood, RasterNeighborhood8<float> cmmNeighborhood)
        {
            if ((this.DsmSlope == null) || (this.DsmAspect == null) || (this.CmmSlope3 == null) || (this.CmmAspect3 == null))
            {
                throw new InvalidOperationException("DSM and CMM slope and aspect cannot be calculated because at least one slope or aspect band was not instantiated. Current bands are " + this.Bands + ". Was the surface model tile created with " + nameof(DigitalSurfaceModelBands) + "." + nameof(DigitalSurfaceModelBands.SlopeAspect) + "?");
            }

            Debug.Assert((this.Bands &= DigitalSurfaceModelBands.SlopeAspect) == DigitalSurfaceModelBands.SlopeAspect);
            this.CalculateSlopeAndAspect(dsmNeighborhood, this.DsmSlope, this.DsmAspect);
            this.CalculateSlopeAndAspect(cmmNeighborhood, this.CmmSlope3, this.CmmAspect3);
        }

        private void CalculateSlopeAndAspect(RasterNeighborhood8<float> surfaceNeighborhood, RasterBand<float> slopeBand, RasterBand<float> aspectBand)
        {
            RasterBand<float> surface = surfaceNeighborhood.Center;
            if ((surface.Transform.CellHeight > 0.0) || (surface.SizeX < 2) || (surface.SizeY < 2))
            {
                throw new NotSupportedException("Surface raster has a positive cell height or is smaller than 2x2 cells.");
            }

            int maxInteriorIndexX = this.SizeX - 1;
            int maxIteriorIndexY = this.SizeY - 1;
            float weightX = (float)(0.25 / surface.Transform.CellWidth);
            float weightY = (float)(0.25 / Math.Abs(surface.Transform.CellHeight)); // accommodate both negative and positive cell heights

            // first row
            bool hasNorthwest = surfaceNeighborhood.TryGetValue(-1, -1, out float zNorthwest);
            bool hasWest = surfaceNeighborhood.TryGetValue(-1, 0, out float zWest);
            bool hasSouthwest = surfaceNeighborhood.TryGetValue(-1, 1, out float zSouthwest);
            bool hasNorth = surfaceNeighborhood.TryGetValue(0, -1, out float zNorth);
            bool hasCenter = surface.TryGetValue(0, 0, out float zCenter);
            bool hasSouth = surface.TryGetValue(0, 1, out float zSouth);
            bool hasNortheast;
            bool hasEast;
            bool hasSoutheast;
            float zNortheast;
            float zEast;
            float zSoutheast;
            float slope;
            float aspect;
            for (int xIndex = 0; xIndex < maxInteriorIndexX; ++xIndex)
            {
                int xIndexNext = xIndex + 1;
                hasNortheast = surfaceNeighborhood.TryGetValue(xIndexNext, -1, out zNortheast);
                hasEast = surface.TryGetValue(xIndexNext, 0, out zEast);
                hasSoutheast = surface.TryGetValue(xIndexNext, 1, out zSoutheast);
                if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                                   hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                                   hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                                   weightX, weightY, out slope, out aspect))
                {
                    slopeBand[xIndex, 0] = slope;
                    aspectBand[xIndex, 0] = aspect;
                }

                hasNorthwest = hasNorth;
                hasWest = hasCenter;
                hasSouthwest = hasSouth;
                hasNorth = hasNortheast;
                hasCenter = hasEast;
                hasSouth = hasSoutheast;

                zNorthwest = zNorth;
                zWest = zCenter;
                zSouthwest = zSouth;
                zNorth = zNortheast;
                zCenter = zEast;
                zSouth = zSoutheast;
            }

            hasNortheast = surfaceNeighborhood.TryGetValue(this.SizeX, -1, out zNortheast);
            hasEast = surfaceNeighborhood.TryGetValue(this.SizeX, 0, out zEast);
            hasSoutheast = surfaceNeighborhood.TryGetValue(this.SizeX, 1, out zSoutheast);
            if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                               hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                               hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                               weightX, weightY, out slope, out aspect))
            {
                slopeBand[maxInteriorIndexX, 0] = slope;
                aspectBand[maxInteriorIndexX, 0] = aspect;
            }

            // rows 1..n-1
            int yIndexPrevious;
            int yIndexNext;
            for (int yIndex = 1; yIndex < maxIteriorIndexY; ++yIndex)
            {
                yIndexPrevious = yIndex - 1;
                yIndexNext = yIndex + 1;
                hasNorthwest = surfaceNeighborhood.TryGetValue(-1, yIndexPrevious, out zNorthwest);
                hasWest = surfaceNeighborhood.TryGetValue(-1, yIndex, out zWest);
                hasSouthwest = surfaceNeighborhood.TryGetValue(-1, yIndexNext, out zSouthwest);
                hasNorth = surfaceNeighborhood.TryGetValue(0, yIndexPrevious, out zNorth);
                hasCenter = surfaceNeighborhood.TryGetValue(0, yIndex, out zCenter);
                hasSouth = surfaceNeighborhood.TryGetValue(0, yIndexNext, out zSouth);
                for (int xIndex = 0; xIndex < maxInteriorIndexX; ++xIndex)
                {
                    int xIndexNext = xIndex + 1;
                    hasNortheast = surfaceNeighborhood.TryGetValue(xIndexNext, yIndexPrevious, out zNortheast);
                    hasEast = surface.TryGetValue(xIndexNext, yIndex, out zEast);
                    hasSoutheast = surface.TryGetValue(xIndexNext, yIndexNext, out zSoutheast);
                    if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                                       hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                                       hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                                       weightX, weightY, out slope, out aspect))
                    {
                        slopeBand[xIndex, yIndex] = slope;
                        aspectBand[xIndex, yIndex] = aspect;
                    }

                    hasNorthwest = hasNorth;
                    hasWest = hasCenter;
                    hasSouthwest = hasSouth;
                    hasNorth = hasNortheast;
                    hasCenter = hasEast;
                    hasSouth = hasSoutheast;

                    zNorthwest = zNorth;
                    zWest = zCenter;
                    zSouthwest = zSouth;
                    zNorth = zNortheast;
                    zCenter = zEast;
                    zSouth = zSoutheast;
                }

                hasNortheast = surfaceNeighborhood.TryGetValue(this.SizeX, yIndexPrevious, out zNortheast);
                hasEast = surfaceNeighborhood.TryGetValue(this.SizeX, yIndex, out zEast);
                hasSoutheast = surfaceNeighborhood.TryGetValue(this.SizeX, yIndexNext, out zSoutheast);
                if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                                   hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                                   hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                                   weightX, weightY, out slope, out aspect))
                {
                    slopeBand[maxInteriorIndexX, yIndex] = slope;
                    aspectBand[maxInteriorIndexX, yIndex] = aspect;
                }
            }

            // last row
            yIndexPrevious = maxIteriorIndexY - 1;
            yIndexNext = maxIteriorIndexY + 1;
            hasNorthwest = surfaceNeighborhood.TryGetValue(-1, yIndexPrevious, out zNorthwest);
            hasWest = surfaceNeighborhood.TryGetValue(-1, maxIteriorIndexY, out zWest);
            hasSouthwest = surfaceNeighborhood.TryGetValue(-1, yIndexNext, out zSouthwest);
            hasNorth = surfaceNeighborhood.TryGetValue(0, yIndexPrevious, out zNorth);
            hasCenter = surfaceNeighborhood.TryGetValue(0, maxIteriorIndexY, out zCenter);
            hasSouth = surfaceNeighborhood.TryGetValue(0, yIndexNext, out zSouth);
            for (int xIndex = 1; xIndex < maxInteriorIndexX; ++xIndex)
            {
                int xIndexNext = xIndex + 1;
                hasNortheast = surface.TryGetValue(xIndexNext, yIndexPrevious, out zNortheast);
                hasEast = surface.TryGetValue(xIndexNext, maxIteriorIndexY, out zEast);
                hasSoutheast = surfaceNeighborhood.TryGetValue(xIndexNext, yIndexNext, out zSoutheast);
                if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                                   hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                                   hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                                   weightX, weightY, out slope, out aspect))
                {
                    slopeBand[xIndex, maxIteriorIndexY] = slope;
                    aspectBand[xIndex, maxIteriorIndexY] = aspect;
                }

                hasNorthwest = hasNorth;
                hasWest = hasCenter;
                hasSouthwest = hasSouth;
                hasNorth = hasNortheast;
                hasCenter = hasEast;
                hasSouth = hasSoutheast;

                zNorthwest = zNorth;
                zWest = zCenter;
                zSouthwest = zSouth;
                zNorth = zNortheast;
                zCenter = zEast;
                zSouth = zSoutheast;
            }

            hasNortheast = surfaceNeighborhood.TryGetValue(this.SizeX, yIndexPrevious, out zNortheast);
            hasEast = surfaceNeighborhood.TryGetValue(this.SizeX, maxIteriorIndexY, out zEast);
            hasSoutheast = surfaceNeighborhood.TryGetValue(this.SizeX, yIndexNext, out zSoutheast);
            if (DigitalSurfaceModel.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                               hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                               hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                               weightX, weightY, out slope, out aspect))
            {
                slopeBand[maxInteriorIndexX, maxIteriorIndexY] = slope;
                aspectBand[maxInteriorIndexX, maxIteriorIndexY] = aspect;
            }
        }

        public static DigitalSurfaceModel CreateFromPrimaryBandMetadata(string dsmPrimaryBandFilePath)
        {
            return DigitalSurfaceModel.CreateFromPrimaryBandMetadata(dsmPrimaryBandFilePath, DigitalSurfaceModelBands.Primary);
        }

        public static DigitalSurfaceModel CreateFromPrimaryBandMetadata(string dsmPrimaryBandFilePath, DigitalSurfaceModelBands bands)
        {
            using Dataset dsmDataset = Gdal.Open(dsmPrimaryBandFilePath, Access.GA_ReadOnly);
            // leave dataset in GDAL's cache on the assumption primary band data will be read later
            return new(dsmDataset, bands, readData: false, dataBufferPool: null);
        }

        public void EnsureSupportingBandsCreated(DigitalSurfaceModelBands bands, RasterBandPool? dataBufferPool)
        {
            this.Bands |= bands;
            if ((bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope)
            {
                this.DsmSlope ??= new(this, DigitalSurfaceModel.DsmSlopeBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect)
            {
                this.DsmAspect ??= new(this, DigitalSurfaceModel.DsmAspectBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3)
            {
                this.CmmSlope3 ??= new(this, DigitalSurfaceModel.CmmSlope3BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3)
            {
                this.CmmAspect3 ??= new(this, DigitalSurfaceModel.CmmAspect3BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface)
            {
                this.Subsurface ??= new(this, DigitalSurfaceModel.SubsurfaceBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean)
            {
                this.AerialMean ??= new(this, DigitalSurfaceModel.AerialMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean)
            {
                this.GroundMean ??= new(this, DigitalSurfaceModel.GroundMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints)
            {
                this.AerialPoints ??= new(this, DigitalSurfaceModel.AerialPointsBandName, RasterBandInitialValue.Default, dataBufferPool); // leave at default of zero, lacks no data value as count of zero is valid
            }
            if ((bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints)
            {
                this.GroundPoints ??= new(this, DigitalSurfaceModel.GroundPointsBandName, RasterBandInitialValue.Default, dataBufferPool); // leave at default of zero, lacks no data value as count of zero is valid
            }
            if ((bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface)
            {
                this.ReturnNumberSurface ??= new(this, DigitalSurfaceModel.ReturnNumberBandName, 0, RasterBandInitialValue.Default, dataBufferPool); // leave at default of zero, which is defined as no data in LAS specification
            }
            if ((bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface)
            {
                this.SourceIDSurface ??= new(this, DigitalSurfaceModel.SourceIDSurfaceBandName, 0, RasterBandInitialValue.NoData, dataBufferPool); // set no data to zero and leave at default of zero as LAS spec defines source IDs 1-65535 as valid
            }
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Surface;
            yield return this.CanopyMaxima3;
            yield return this.CanopyHeight;

            if (((this.Bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope) && (this.DsmSlope != null))
            {
                yield return this.DsmSlope;
            }
            if (((this.Bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect) && (this.DsmAspect != null))
            {
                yield return this.DsmAspect;
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3) && (this.CmmSlope3 != null))
            {
                yield return this.CmmSlope3;
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3) && (this.CmmAspect3 != null))
            {
                yield return this.CmmAspect3;
            }

            if (((this.Bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface) && (this.Subsurface != null))
            {
                yield return this.Subsurface;
            }
            if (((this.Bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean) && (this.AerialMean != null))
            {
                yield return this.AerialMean;
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean) && (this.GroundMean != null))
            {
                yield return this.GroundMean;
            }

            if (((this.Bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints) && (this.AerialPoints != null))
            {
                yield return this.AerialPoints;
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints) && (this.GroundPoints != null))
            {
                yield return this.GroundPoints;
            }

            if (((this.Bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface) && (this.ReturnNumberSurface != null))
            {
                yield return this.ReturnNumberSurface;
            }
            if (((this.Bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface) && (this.SourceIDSurface != null))
            {
                yield return this.SourceIDSurface;
            }
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            List<RasterBandStatistics> bandStatistics = [ this.Surface.GetStatistics(), this.CanopyMaxima3.GetStatistics(), this.CanopyHeight.GetStatistics() ];

            if (((this.Bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope) && (this.DsmSlope != null))
            {
                bandStatistics.Add(this.DsmSlope.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect) && (this.DsmAspect != null))
            {
                bandStatistics.Add(this.DsmAspect.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3) && (this.CmmSlope3 != null))
            {
                bandStatistics.Add(this.CmmSlope3.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3) && (this.CmmAspect3 != null))
            {
                bandStatistics.Add(this.CmmAspect3.GetStatistics());
            }

            if (((this.Bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface) && (this.Subsurface != null))
            {
                bandStatistics.Add(this.Subsurface.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean) && (this.AerialMean != null))
            {
                bandStatistics.Add(this.AerialMean.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean) && (this.GroundMean != null))
            {
                bandStatistics.Add(this.GroundMean.GetStatistics());
            }

            if (((this.Bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints) && (this.AerialPoints != null))
            {
                bandStatistics.Add(this.AerialPoints.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints) && (this.GroundPoints != null))
            {
                bandStatistics.Add(this.GroundPoints.GetStatistics());
            }

            if (((this.Bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface) && (this.ReturnNumberSurface != null))
            {
                bandStatistics.Add(this.ReturnNumberSurface.GetStatistics());
            }
            if (((this.Bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface) && (this.SourceIDSurface != null))
            {
                bandStatistics.Add(this.SourceIDSurface.GetStatistics());
            }

            return bandStatistics;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MaybeInsertSubsurfacePoint(int cellIndex, float dsmZ, int aerialPoints, float z, float[] subsurfaceBuffer)
        {
            float subsurfaceCandidateZ = z;
            if (dsmZ < z)
            {
                // point lifts DSM so current DSM point moves to first subsurface point
                // first subsurface point becuomes buffer candidate point
                subsurfaceCandidateZ = this.Subsurface![cellIndex];
                this.Subsurface![cellIndex] = dsmZ;
            }
            else if (aerialPoints == 1)
            {
                // DSM has been established but first subsurface point hasn't been set
                this.Subsurface![cellIndex] = z;
            }

            if (aerialPoints > 1)
            {
                // both DSM and subsurface points are set; does subsurface candidate point ripple into buffer?
                float subsurfaceZ = this.Subsurface![cellIndex];
                if (subsurfaceZ < subsurfaceCandidateZ)
                {
                    // candidate point becomes first subsurface position and current subsurface becomes buffer candidate
                    this.Subsurface![cellIndex] = subsurfaceCandidateZ;
                    subsurfaceCandidateZ = subsurfaceZ;
                }

                Span<float> cellSubsurfaceBuffer = subsurfaceBuffer.AsSpan().Slice(DigitalSurfaceModel.SubsurfaceBufferDepth * (int)cellIndex, DigitalSurfaceModel.SubsurfaceBufferDepth);
                int aeriaPointsAvailableToCellSubsurfaceBuffer = (int)aerialPoints - 2;
                if (aeriaPointsAvailableToCellSubsurfaceBuffer >= DigitalSurfaceModel.SubsurfaceBufferDepth)
                {
                    // subsurface buffer is full: if point is to be inserted another must be evicted
                    for (int cellSubsurfaceBufferIndex = 0; cellSubsurfaceBufferIndex < DigitalSurfaceModel.SubsurfaceBufferDepth; ++cellSubsurfaceBufferIndex)
                    {
                        float subsurfaceBufferZ = cellSubsurfaceBuffer![cellSubsurfaceBufferIndex];
                        if (subsurfaceBufferZ > subsurfaceCandidateZ)
                        {
                            cellSubsurfaceBuffer![cellSubsurfaceBufferIndex] = subsurfaceCandidateZ;
                            for (int moveDestinationIndex = cellSubsurfaceBufferIndex + 1; moveDestinationIndex < DigitalSurfaceModel.SubsurfaceBufferDepth; ++moveDestinationIndex)
                            {
                                (subsurfaceBufferZ, cellSubsurfaceBuffer[moveDestinationIndex]) = (cellSubsurfaceBuffer[moveDestinationIndex], subsurfaceBufferZ);
                            }

                            return;
                        }
                    }
                }
                else
                {
                    // insert point into subsurface buffer in ascending order
                    int pointsInCellSubsurfaceBuffer = aeriaPointsAvailableToCellSubsurfaceBuffer;
                    for (int cellSubsurfaceBufferIndex = 0; cellSubsurfaceBufferIndex <= pointsInCellSubsurfaceBuffer; ++cellSubsurfaceBufferIndex)
                    {
                        float subsurfaceBufferZ = cellSubsurfaceBuffer![cellSubsurfaceBufferIndex];
                        if (subsurfaceBufferZ < subsurfaceCandidateZ)
                        {
                            cellSubsurfaceBuffer![cellSubsurfaceBufferIndex] = subsurfaceCandidateZ;
                            for (int moveDestinationIndex = cellSubsurfaceBufferIndex + 1; moveDestinationIndex <= pointsInCellSubsurfaceBuffer; ++moveDestinationIndex)
                            {
                                (subsurfaceBufferZ, cellSubsurfaceBuffer[moveDestinationIndex]) = (cellSubsurfaceBuffer[moveDestinationIndex], subsurfaceBufferZ);
                            }

                            return;
                        }
                    }
                }
            }
        }

        // TODO: SIMD
        public void OnPointAdditionComplete(RasterBand<float> dtm, float subsurfaceGapDistance, float[]? subsurfaceBuffer)
        {
            bool hasSubsurface = (this.Subsurface != null) && (subsurfaceBuffer != null);
            if (hasSubsurface && (this.AerialPoints == null))
            {
                throw new NotSupportedException("Aerial point count is required to adjust subsurface z values.");
            }

            if (SpatialReferenceExtensions.IsSameCrs(this.Crs, dtm.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DSMs and DTMs are currently required to be in the same CRS. The DSM CRS is '" + this.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }
            if (this.IsSameExtentAndSpatialResolution(dtm) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DTM extent (" + dtm.GetExtentString() + ") or size (" + dtm.SizeX + " x " + dtm.SizeY + ") does not match DSM extent (" + this.GetExtentString() + ") or size (" + this.SizeX + " by " + this.SizeY + ").");
            }

            // nothing to do for this.Surface
            // for now, population of the canopy maxima model is deferred until a virtual raster neighborhood is available
            // Binomial.Smooth3x3(this.Surface, this.CanopyMaxima3);

            // set canopy height and, if available, adjust subsurface based on subsurface buffer
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                float dsmZ = this.Surface[cellIndex];
                // canopy height is calculated relative to DTM
                // If needed, mean ground elevation can be used instead.
                this.CanopyHeight[cellIndex] = dsmZ - dtm[cellIndex];

                if (hasSubsurface) // if there's no subsurface buffer nothing to do but leave this.Subsurface as is
                {
                    float subsurfaceGappedZ = dsmZ - subsurfaceGapDistance;
                    float subsurfaceZ = this.Subsurface![cellIndex]; // nothing to do if this.Subsurface satisifes gap
                    if (subsurfaceZ > subsurfaceGappedZ)
                    {
                        int cellSubsurfacePoints = (int)this.AerialPoints![cellIndex] - 2;
                        if (cellSubsurfacePoints > DigitalSurfaceModel.SubsurfaceBufferDepth)
                        {
                            cellSubsurfacePoints = DigitalSurfaceModel.SubsurfaceBufferDepth;
                        }

                        Span<float> cellSubsurfaceBuffer = subsurfaceBuffer.AsSpan().Slice(DigitalSurfaceModel.SubsurfaceBufferDepth * (int)cellIndex, DigitalSurfaceModel.SubsurfaceBufferDepth);
                        bool subsurfaceGapFound = false;
                        for (int cellSubsurfaceIndex = cellSubsurfacePoints - 1; cellSubsurfaceIndex > 0; --cellSubsurfaceIndex)
                        {
                            subsurfaceZ = cellSubsurfaceBuffer[cellSubsurfaceIndex];
                            if (subsurfaceZ < subsurfaceGappedZ)
                            {
                                this.Subsurface[cellIndex] = subsurfaceZ;
                                subsurfaceGapFound = true;
                                break;
                            }
                        }

                        if (subsurfaceGapFound == false)
                        {
                            this.Subsurface[cellIndex] = cellSubsurfaceBuffer[0];
                        }
                    }
                }
            }

            // find mean elevations of aerial and ground points
            if (this.AerialMean != null)
            {
                if (this.AerialPoints == null)
                {
                    throw new NotSupportedException("Mean z of aerial points cannot be calculated without the aerial point count.");
                }
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    UInt32 nAerial = this.AerialPoints[cellIndex];
                    if (nAerial > 0)
                    {
                        this.AerialMean[cellIndex] /= nAerial;
                    }
                    // otherwise leave mean aerial elevation as no data
                }
            }
            if (this.GroundMean != null)
            {
                if (this.GroundPoints == null)
                {
                    throw new NotSupportedException("Mean z of ground points cannot be calculated without the ground point count.");
                }
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    UInt32 nGround = this.GroundPoints[cellIndex];
                    if (nGround > 0)
                    {
                        this.GroundMean[cellIndex] /= nGround;
                    }
                    // otherwise leave ground elevation as no data
                }
            }

            // nothing to do for
            // this.ReturnNumber
            // this.SourceIDSurface
        }

        public override void ReadBandData()
        {
            // default to reading all bands
            this.ReadBandData(this.Bands);
        }

        /// <summary>
        /// Read specified digital surface model bands.
        /// </summary>
        /// <remarks>
        /// For now, does not modify bands whose flags are not set. They will be left as null if never loaded or with previous data if
        /// previously loaded.
        /// </remarks>
        public void ReadBandData(DigitalSurfaceModelBands bands)
        {
            if ((bands & DigitalSurfaceModelBands.Primary) != DigitalSurfaceModelBands.None)
            {
                using Dataset dsmDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
                Debug.Assert((this.SizeX == dsmDataset.RasterXSize) && (this.SizeY == dsmDataset.RasterYSize) && SpatialReferenceExtensions.IsSameCrs(this.Crs, dsmDataset.GetSpatialRef()));
                for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SurfaceBandName:
                            if ((bands & DigitalSurfaceModelBands.Surface) == DigitalSurfaceModelBands.Surface)
                            {
                                Debug.Assert(this.Surface.IsNoData(gdalBand));
                                this.Surface.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                            }
                            break;
                        case DigitalSurfaceModel.CanopyMaximaBandName:
                            if ((bands & DigitalSurfaceModelBands.CanopyMaxima3) == DigitalSurfaceModelBands.CanopyMaxima3)
                            {
                                Debug.Assert(this.CanopyMaxima3.IsNoData(gdalBand));
                                this.CanopyMaxima3.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                            }
                            break;
                        case DigitalSurfaceModel.CanopyHeightBandName:
                            if ((bands & DigitalSurfaceModelBands.CanopyHeight) == DigitalSurfaceModelBands.CanopyHeight)
                            {
                                Debug.Assert(this.CanopyHeight.IsNoData(gdalBand));
                                this.CanopyHeight.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in DSM tile '" + this.FilePath + ".");
                    }
                }

                dsmDataset.FlushCache();
            }

            if ((bands & DigitalSurfaceModelBands.SlopeAspect) != DigitalSurfaceModelBands.None)
            {
                string slopeAspectTilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DirectorySlopeAspect, createDiagnosticDirectory: false);
                using Dataset slopeAspectDataset = Gdal.Open(slopeAspectTilePath, Access.GA_ReadOnly);
                SpatialReference crs = slopeAspectDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= slopeAspectDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = slopeAspectDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.DsmSlopeBandName:
                            if ((bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope)
                            {
                                if (this.DsmSlope == null)
                                {
                                    this.DsmSlope = new(slopeAspectDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.DsmSlope.Read(slopeAspectDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.DsmAspectBandName:
                            if ((bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect)
                            {
                                if (this.DsmAspect == null)
                                {
                                    this.DsmAspect = new(slopeAspectDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.DsmAspect.Read(slopeAspectDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.CmmSlope3BandName:
                            if ((bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3)
                            {
                                if (this.CmmSlope3 == null)
                                {
                                    this.CmmSlope3 = new(slopeAspectDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.CmmSlope3.Read(slopeAspectDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.CmmAspect3BandName:
                            if ((bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3)
                            {
                                if (this.CmmAspect3 == null)
                                {
                                    this.CmmAspect3 = new(slopeAspectDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.CmmAspect3.Read(slopeAspectDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in z diagnostics tile '" + slopeAspectTilePath + "'.");
                    }
                }

                slopeAspectDataset.FlushCache();
            }

            if ((bands & DigitalSurfaceModelBands.DiagnosticZ) != DigitalSurfaceModelBands.None)
            {
                string zTilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: false);
                using Dataset zDataset = Gdal.Open(zTilePath, Access.GA_ReadOnly);
                SpatialReference crs = zDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= zDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = zDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SubsurfaceBandName:
                            if ((bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface)
                            {
                                if (this.Subsurface == null)
                                {
                                    this.Subsurface = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.Subsurface.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.AerialMeanBandName:
                            if ((bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean)
                            {
                                if (this.AerialMean == null)
                                {
                                    this.AerialMean = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.AerialMean.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.GroundMeanBandName:
                            if ((bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean)
                            {
                                if (this.GroundMean == null)
                                {
                                    this.GroundMean = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.GroundMean.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in z diagnostics tile '" + zTilePath + "'.");
                    }
                }

                zDataset.FlushCache();
            }

            if ((bands & DigitalSurfaceModelBands.PointCounts) != DigitalSurfaceModelBands.None)
            {
                string pointCountTilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: false);
                using Dataset pointCountDataset = Gdal.Open(pointCountTilePath, Access.GA_ReadOnly);
                SpatialReference crs = pointCountDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= pointCountDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = pointCountDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.AerialPointsBandName:
                            if ((bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints)
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
                            if ((bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints)
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

                pointCountDataset.FlushCache();
            }

            if ((bands & DigitalSurfaceModelBands.ReturnNumberSurface) != DigitalSurfaceModelBands.None)
            {
                string returnNumberTilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, createDiagnosticDirectory: false);
                using Dataset returnNumberDataset = Gdal.Open(returnNumberTilePath, Access.GA_ReadOnly);
                SpatialReference crs = returnNumberDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= returnNumberDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = returnNumberDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.ReturnNumberBandName:
                            if ((bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface)
                            {
                                if (this.ReturnNumberSurface == null)
                                {
                                    this.ReturnNumberSurface = new(returnNumberDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.ReturnNumberSurface.Read(returnNumberDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in source ID diagnostics tile '" + returnNumberTilePath + "'.");
                    }
                }

                returnNumberDataset.FlushCache();
            }

            if ((bands & DigitalSurfaceModelBands.SourceIDSurface) != DigitalSurfaceModelBands.None)
            {
                string sourceIDtilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: false);
                using Dataset sourceIDdataset = Gdal.Open(sourceIDtilePath, Access.GA_ReadOnly);
                SpatialReference crs = sourceIDdataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= sourceIDdataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = sourceIDdataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SourceIDSurfaceBandName:
                            if ((bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface)
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

                sourceIDdataset.FlushCache();
            }
        }

        public void Reset(string filePath, LasFile lasFile, Grid newExtents)
        {
            // inherited from Grid
            if ((this.SizeX != newExtents.SizeX) || (this.SizeY != newExtents.SizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + "() does not currently support changing the DSM's size from " + this.SizeX + " x " + this.SizeY + " cells to " + newExtents.SizeX + " x " + newExtents.SizeY + ".");
            }

            SpatialReference lasFileCrs = lasFile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(this.Crs, lasFileCrs) == false)
            {
                // for now, be tolerant of .las files missing vertical CRSes when testing sameness but maintain vertical CRS requirement for adoption
                if (lasFileCrs.IsCompound() == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(lasFile), filePath + ": point cloud's coordinate reference system (CRS) is not a compound CRS. Both a horizontal and vertical CRS are needed to fully geolocate a digital surface model's elevations.");
                }
                this.SetCrs(lasFileCrs);
            }

            Debug.Assert(Object.ReferenceEquals(this.Transform, this.Surface.Transform) && Object.ReferenceEquals(this.Transform, this.CanopyMaxima3.Transform) && Object.ReferenceEquals(this.Transform, this.CanopyHeight.Transform) &&
                         ((this.DsmSlope == null) || Object.ReferenceEquals(this.Transform, this.DsmSlope!.Transform)) && ((this.DsmAspect == null) || Object.ReferenceEquals(this.Transform, this.DsmAspect!.Transform)) && ((this.CmmSlope3 == null) || Object.ReferenceEquals(this.Transform, this.CmmSlope3!.Transform)) && ((this.CmmAspect3 == null) || Object.ReferenceEquals(this.Transform, this.CmmAspect3!.Transform)) &&
                         ((this.Subsurface == null) || Object.ReferenceEquals(this.Transform, this.Subsurface!.Transform)) && ((this.AerialMean == null) || Object.ReferenceEquals(this.Transform, this.AerialMean!.Transform)) && ((this.GroundMean == null) || Object.ReferenceEquals(this.Transform, this.GroundMean!.Transform)) &&
                         ((this.AerialPoints == null) || Object.ReferenceEquals(this.Transform, this.AerialPoints!.Transform)) && ((this.GroundPoints == null) || Object.ReferenceEquals(this.Transform, this!.GroundPoints.Transform)) && 
                         ((this.ReturnNumberSurface == null) || Object.ReferenceEquals(this.Transform, this.ReturnNumberSurface!.Transform)) && ((this.SourceIDSurface == null) || Object.ReferenceEquals(this.Transform, this.SourceIDSurface!.Transform)));
            this.Transform.Copy(newExtents.Transform);

            // this.Bands remains unchanged as Reset() does not add or remove bands
            // inherited from Raster
            this.FilePath = filePath;

            // digital surface model fields
            // Contents of secondary bands which are instantiated but not flagged in this.Bands are reset in case the band later
            // becomes flagged.
            // Fills are no ops if the band's data hasn't been loaded or if the data buffers have been returned to pool.
            Debug.Assert(this.Surface.HasNoDataValue && this.CanopyMaxima3.HasNoDataValue && this.CanopyHeight.HasNoDataValue);

            this.Surface.FillNoData();
            this.CanopyMaxima3.FillNoData();
            this.CanopyHeight.FillNoData();

            this.DsmSlope?.FillNoData();
            this.DsmAspect?.FillNoData();
            this.CmmSlope3?.FillNoData();
            this.CmmAspect3?.FillNoData();

            this.Subsurface?.FillNoData();
            this.AerialMean?.FillNoData();
            this.GroundMean?.FillNoData();

            this.AerialPoints?.Fill(0U);
            this.GroundPoints?.Fill(0U);

            this.ReturnNumberSurface?.FillNoData();
            this.SourceIDSurface?.FillNoData();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            // default to returning all bands present
            this.ReturnBandData(this.Bands, dataBufferPool);
        }

        public void ReturnBandData(DigitalSurfaceModelBands bands, RasterBandPool dataBufferPool)
        {
            if ((bands & DigitalSurfaceModelBands.Surface) == DigitalSurfaceModelBands.Surface)
            {
                this.Surface.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CanopyMaxima3) == DigitalSurfaceModelBands.CanopyMaxima3)
            {
                this.CanopyMaxima3.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CanopyHeight) == DigitalSurfaceModelBands.CanopyHeight)
            {
                this.CanopyHeight.ReturnData(dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope)
            {
                this.DsmSlope?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect)
            {
                this.DsmAspect?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3)
            {
                this.CmmSlope3?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3)
            {
                this.CmmAspect3?.ReturnData(dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface)
            {
                this.Subsurface?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean)
            {
                this.AerialMean?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean)
            {
                this.GroundMean?.ReturnData(dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints)
            {
                this.AerialPoints?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints)
            {
                this.GroundPoints?.ReturnData(dataBufferPool);
            }

            if ((bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface)
            {
                this.ReturnNumberSurface?.ReturnData(dataBufferPool);
            }
            if ((bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface)
            {
                this.SourceIDSurface?.ReturnData(dataBufferPool);
            }
        }

        private void SetCrs(SpatialReference crs)
        {
            this.Crs = crs;
            this.Surface.Crs = crs;
            this.CanopyMaxima3.Crs = crs;
            this.CanopyHeight.Crs = crs;

            // update secondary bands' CRS regardless of whether the band is flagged in this.Band
            // Bands should default to a consistent CRS if later flagged.
            if (this.DsmSlope != null)
            {
                this.DsmSlope.Crs = crs;
            }
            if (this.DsmAspect != null)
            {
                this.DsmAspect.Crs = crs;
            }
            if (this.CmmSlope3 != null)
            {
                this.CmmSlope3.Crs = crs;
            }
            if (this.CmmAspect3 != null)
            {
                this.CmmAspect3.Crs = crs;
            }

            if (this.Subsurface != null)
            {
                this.Subsurface.Crs = crs;
            }
            if (this.AerialMean != null)
            {
                this.AerialMean.Crs = crs;
            }
            if (this.GroundMean != null)
            {
                this.GroundMean.Crs = crs;
            }

            if (this.AerialPoints != null)
            {
                this.AerialPoints.Crs = crs;
            }
            if (this.GroundPoints != null)
            {
                this.GroundPoints.Crs = crs;
            }

            if (this.ReturnNumberSurface != null)
            {
                this.ReturnNumberSurface.Crs = crs;
            }
            if (this.SourceIDSurface != null)
            {
                this.SourceIDSurface.Crs = crs;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryCalculateSlopeAndAspect(bool hasNorthwest, float zNorthwest, bool hasNorth, float zNorth, bool hasNortheast, float zNortheast,
                                                       bool hasWest, float zWest, bool hasCenter, float zCenter, bool hasEast, float zEast,
                                                       bool hasSouthwest, float zSouthwest, bool hasSouth, float zSouth, bool hasSoutheast, float zSoutheast,
                                                       float weightX, float weightY, out float slope, out float aspect)
        {
            // Zhou et al. 2004 define fx as north-south and fy as east-west
            // Zhou Q, Liu X. 2004. Analysis of errors of derived slope and aspect related to DEM data properties. Computers & Geosciences 30:369–378. doi:10.1016/j.cageo.2003.07.005
            // Probably this is because 0° aspect is north.
            float fx;
            float fy;
            if (hasNorthwest && hasNortheast && hasSouthwest && hasSoutheast)
            {
                // frame finite difference
                fx = weightY * (zNorthwest - zSouthwest + zNortheast - zSoutheast);
                fy = weightX * (zSoutheast - zSouthwest + zNortheast - zNorthwest);
            }
            else if (hasNorth && hasSouth && hasEast && hasWest)
            {
                // second order finite difference
                fx = 2.0F * weightY * (zNorth - zSouth);
                fy = 2.0F * weightX * (zEast - zWest);
            }
            else if (hasCenter == false)
            {
                slope = Single.NaN;
                aspect = Single.NaN;
                return false;
            }
            else
            {
                // try for simple difference fx and fy
                if (hasNorth)
                {
                    fx = 4.0F * weightY * (zNorth - zCenter);
                }
                else if (hasSouth)
                {
                    fx = 4.0F * weightY * (zCenter - zSouth);
                }
                else
                {
                    slope = Single.NaN;
                    aspect = Single.NaN;
                    return false;
                }
                if (hasWest)
                {
                    fy = 4.0F * weightX * (zCenter - zWest);
                }
                else if (hasEast)
                {
                    fy = 4.0F * weightX * (zEast - zCenter);
                }
                else
                {
                    slope = Single.NaN;
                    aspect = Single.NaN;
                    return false;
                }
            }

            slope = 180.0F / MathF.PI * MathF.Atan(fx * fx + fy * fy);
            aspect = -180.0F / MathF.PI * (MathF.Atan2(fx, fy) + 0.5F * MathF.PI);
            if (aspect < 0.0F)
            {
                aspect = 360.0F + aspect; // can yield exactly 360.0F
            }
            if (aspect == 360.0F)
            {
                aspect = 0.0F; // disambiguate numerical edge case
            }
            return true;
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
            else if (((this.Bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope) && (this.DsmSlope != null) && String.Equals(this.DsmSlope.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmSlope;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect) && (this.DsmAspect != null) && String.Equals(this.DsmAspect.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmAspect;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3) && (this.CmmSlope3 != null) && String.Equals(this.CmmSlope3.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmSlope3;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3) && (this.CmmAspect3 != null) && String.Equals(this.CmmAspect3.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmAspect3;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface) && (this.Subsurface != null) && String.Equals(this.Subsurface.Name, name, StringComparison.Ordinal))
            {
                band = this.Subsurface;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean) && (this.AerialMean != null) && String.Equals(this.AerialMean.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialMean;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean) && (this.GroundMean != null) && String.Equals(this.GroundMean.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundMean;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints) && (this.AerialPoints != null) && String.Equals(this.AerialPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialPoints;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints) && (this.GroundPoints != null) && String.Equals(this.GroundPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundPoints;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface) && (this.ReturnNumberSurface != null) && String.Equals(this.ReturnNumberSurface.Name, name, StringComparison.Ordinal))
            {
                band = this.ReturnNumberSurface;
            }
            else if (((this.Bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface) && (this.SourceIDSurface != null) && String.Equals(this.SourceIDSurface.Name, name, StringComparison.Ordinal))
            {
                band = this.SourceIDSurface;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            if (String.Equals(name, this.Surface.Name, StringComparison.Ordinal))
            {
                bandFilePath = this.FilePath;
                bandIndexInFile = 0;
                return true;
            }
            if (String.Equals(name, this.CanopyMaxima3.Name, StringComparison.Ordinal))
            {
                bandFilePath = this.FilePath;
                bandIndexInFile = 1;
                return true;
            }
            if (String.Equals(name, this.CanopyHeight.Name, StringComparison.Ordinal))
            {
                bandFilePath = this.FilePath;
                bandIndexInFile = 2;
                return true;
            }

            if (this.DsmSlope != null)
            {
                if (String.Equals(name, this.DsmSlope.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DirectorySlopeAspect, false);
                    bandIndexInFile = 0;
                    return true;
                }
            }
            if (this.DsmAspect != null)
            {
                if (String.Equals(name, this.DsmAspect.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DirectorySlopeAspect, false);
                    bandIndexInFile = 1;
                    return true;
                }
            }
            if (this.CmmSlope3 != null)
            {
                if (String.Equals(name, this.CmmSlope3.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DirectorySlopeAspect, false);
                    bandIndexInFile = 2;
                    return true;
                }
            }
            if (this.CmmAspect3 != null)
            {
                if (String.Equals(name, this.CmmAspect3.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DirectorySlopeAspect, false);
                    bandIndexInFile = 3;
                    return true;
                }
            }

            if (this.Subsurface != null)
            {
                if (String.Equals(name, this.Subsurface.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, false);
                    bandIndexInFile = 0;
                    return true;
                }
            }
            if (this.AerialMean != null)
            {
                if (String.Equals(name, this.AerialMean.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, false);
                    bandIndexInFile = 1;
                    return true;
                }
            }
            if (this.GroundMean != null)
            {
                if (String.Equals(name, this.GroundMean.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, false);
                    bandIndexInFile = 2;
                    return true;
                }
            }

            if (this.AerialPoints != null)
            {
                if (String.Equals(name, this.AerialPoints.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, false);
                    bandIndexInFile = 0;
                    return true;
                }
            }
            if (this.GroundPoints != null)
            {
                if (String.Equals(name, this.GroundPoints.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, false);
                    bandIndexInFile = 1;
                    return true;
                }
            }

            if (this.ReturnNumberSurface != null)
            {
                if (String.Equals(name, this.ReturnNumberSurface.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, false);
                    bandIndexInFile = 0;
                    return true;
                }
            }
            if (this.SourceIDSurface != null)
            {
                if (String.Equals(name, this.SourceIDSurface.Name, StringComparison.Ordinal))
                {
                    bandFilePath = Raster.GetComponentFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, false);
                    bandIndexInFile = 1;
                    return true;
                }
            }

            bandFilePath = null;
            bandIndexInFile = -1;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            // alleviate memory pressure by offloading GC in streaming virtual raster reads
            // Not useful if tile sizes depart from the virtual raster convention of all being identical size.
            this.Surface.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.CanopyMaxima3.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.CanopyHeight.TryTakeOwnershipOfDataBuffer(dataBufferPool);

            if (((this.Bands & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope) && (this.DsmSlope != null))
            {
                this.DsmSlope.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect) && (this.DsmAspect != null))
            {
                this.DsmAspect.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3) && (this.CmmSlope3 != null))
            {
                this.CmmSlope3.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3) && (this.CmmAspect3 != null))
            {
                this.CmmAspect3.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }

            if (((this.Bands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface) && (this.Subsurface != null))
            {
                this.Subsurface.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean) && (this.AerialMean != null))
            {
                this.AerialMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean) && (this.GroundMean != null))
            {
                this.GroundMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }

            if (((this.Bands & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints) && (this.AerialPoints != null))
            {
                this.AerialPoints.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints) && (this.GroundPoints != null))
            {
                this.GroundPoints.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }

            if (((this.Bands & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface) && (this.ReturnNumberSurface != null))
            {
                this.ReturnNumberSurface.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            if (((this.Bands & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface) && (this.SourceIDSurface != null))
            {
                this.SourceIDSurface.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
        }

        public override void Write(string dsmPath, bool compress)
        {
            // default to (over)writing all bands present
            this.Write(dsmPath, this.Bands, compress);
        }

        public void Write(string dsmPrimaryPath, DigitalSurfaceModelBands bandsToWriteIfPresent, bool compress)
        {
            // primary data bands
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.Primary) != DigitalSurfaceModelBands.None)
            {
                Debug.Assert(this.Surface.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyMaxima3.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyHeight.IsNoData(RasterBand.NoDataDefaultFloat));
                using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(dsmPrimaryPath, 3, DataType.GDT_Float32, compress);
                if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.Surface) == DigitalSurfaceModelBands.Surface)
                {
                    this.Surface.Write(dsmDataset, 1);
                }
                if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.CanopyMaxima3) == DigitalSurfaceModelBands.CanopyMaxima3)
                {
                    this.CanopyMaxima3.Write(dsmDataset, 2);
                }
                if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.CanopyHeight) == DigitalSurfaceModelBands.CanopyHeight)
                {
                    this.CanopyHeight.Write(dsmDataset, 3);
                }
                this.FilePath = dsmPrimaryPath;
            }

            // slope and aspect bands
            if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.SlopeAspect) != DigitalSurfaceModelBands.None)
            {
                bool writeDsmSlope = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.DsmSlope) == DigitalSurfaceModelBands.DsmSlope) && (this.DsmSlope != null);
                bool writeDsmAspect = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.DsmAspect) == DigitalSurfaceModelBands.DsmAspect) && (this.DsmAspect != null);
                bool writeCmmSlope3 = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.CmmSlope3) == DigitalSurfaceModelBands.CmmSlope3) && (this.CmmSlope3 != null);
                bool writeCmmAspect3 = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.CmmAspect3) == DigitalSurfaceModelBands.CmmAspect3) && (this.CmmAspect3 != null);
                int slopeAspectBands = (writeDsmSlope ? 1 : 0) + (writeDsmAspect ? 1 : 0) + (writeCmmSlope3 ? 1 : 0) + (writeCmmAspect3 ? 1 : 0);
                if (slopeAspectBands > 0)
                {
                    string slopeAspectTilePath = Raster.GetComponentFilePath(dsmPrimaryPath, DigitalSurfaceModel.DirectorySlopeAspect, createDiagnosticDirectory: true);

                    using Dataset slopeAspectDataset = this.CreateGdalRasterAndSetFilePath(slopeAspectTilePath, slopeAspectBands, DataType.GDT_Float32, compress);
                    int gdalBand = 1;
                    if (writeDsmSlope)
                    {
                        Debug.Assert(this.DsmSlope!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.DsmSlope.Write(slopeAspectDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeDsmAspect)
                    {
                        Debug.Assert(this.DsmAspect!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.DsmAspect.Write(slopeAspectDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeCmmSlope3)
                    {
                        Debug.Assert(this.CmmSlope3!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.CmmSlope3.Write(slopeAspectDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeCmmAspect3)
                    {
                        Debug.Assert(this.CmmAspect3!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.CmmAspect3.Write(slopeAspectDataset, gdalBand);
                    }
                }
            }

            // diagnostic bands in z
            // Bands are single precision floating pointm sane as z values.
            if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.DiagnosticZ) != DigitalSurfaceModelBands.None)
            {
                bool writeSubsurface = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface) && (this.Subsurface != null);
                bool writeAerialMean = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.AerialMean) == DigitalSurfaceModelBands.AerialMean) && (this.AerialMean != null);
                bool writeGroundMean = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.GroundMean) == DigitalSurfaceModelBands.GroundMean) && (this.GroundMean != null);
                int zDiagnosticBands = (writeSubsurface ? 1 : 0) + (writeAerialMean ? 1 : 0) + (writeGroundMean ? 1 : 0);
                if (zDiagnosticBands > 0)
                {
                    string zTilePath = Raster.GetComponentFilePath(dsmPrimaryPath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);

                    using Dataset zDataset = this.CreateGdalRasterAndSetFilePath(zTilePath, zDiagnosticBands, DataType.GDT_Float32, compress);
                    int gdalBand = 1;
                    if (writeSubsurface)
                    {
                        Debug.Assert(this.Subsurface!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.Subsurface.Write(zDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeAerialMean)
                    {
                        Debug.Assert(this.AerialMean!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.AerialMean.Write(zDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeGroundMean)
                    {
                        Debug.Assert(this.GroundMean!.IsNoData(RasterBand.NoDataDefaultFloat));
                        this.GroundMean.Write(zDataset, gdalBand);
                    }
                }
            }

            // diagnostic bands: point counts
            // Bands are unsigned 8, 16, or 32 bit integers depending on the largest value counted. 8 and 16 bit cases could potentially
            // be merged with source ID tiles but are not.
            if ((bandsToWriteIfPresent & DigitalSurfaceModelBands.PointCounts) != DigitalSurfaceModelBands.None)
            {
                bool writeAerialPoints = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.AerialPoints) == DigitalSurfaceModelBands.AerialPoints) && (this.AerialPoints != null);
                bool writeGroundPoints = ((bandsToWriteIfPresent & DigitalSurfaceModelBands.GroundPoints) == DigitalSurfaceModelBands.GroundPoints) && (this.GroundPoints != null);
                int pointCountBands = (writeAerialPoints ? 1 : 0) + (writeGroundPoints ? 1 : 0);
                if (pointCountBands > 0)
                {
                    string pointCountTilePath = Raster.GetComponentFilePath(dsmPrimaryPath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                    DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(writeAerialPoints, this.AerialPoints, writeGroundPoints, this.GroundPoints);

                    using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountTilePath, pointCountBands, pointCountBandType, compress);
                    int gdalBand = 1;
                    if (writeAerialPoints)
                    {
                        Debug.Assert(this.AerialPoints!.HasNoDataValue == false); // nullability doesn't flow through bool
                        this.AerialPoints.Write(pointCountDataset, gdalBand);
                        ++gdalBand;
                    }
                    if (writeGroundPoints)
                    {
                        Debug.Assert(this.GroundPoints!.HasNoDataValue == false); // nullability doesn't flow through bool
                        this.GroundPoints.Write(pointCountDataset, gdalBand);
                    }
                }
            }

            // diagnostic bands: return number
            // For now, write band even if return numbers aren't defined in the data source.
            if (((bandsToWriteIfPresent & DigitalSurfaceModelBands.ReturnNumberSurface) == DigitalSurfaceModelBands.ReturnNumberSurface) && (this.ReturnNumberSurface != null))
            {
                string returnNumberTilePath = Raster.GetComponentFilePath(dsmPrimaryPath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, createDiagnosticDirectory: true);

                using Dataset returnNumberDataset = this.CreateGdalRasterAndSetFilePath(returnNumberTilePath, 1, DataType.GDT_Byte, compress);
                this.ReturnNumberSurface.Write(returnNumberDataset, 1);
                Debug.Assert(returnNumberDataset.RasterCount == 1);
            }

            // diagnostic bands: source IDs
            // For now, write band even if the maximum source ID is zero.
            if (((bandsToWriteIfPresent & DigitalSurfaceModelBands.SourceIDSurface) == DigitalSurfaceModelBands.SourceIDSurface) && (this.SourceIDSurface != null))
            {
                string sourceIDtilePath = Raster.GetComponentFilePath(dsmPrimaryPath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: true);
                DataType sourceIDbandType = DataTypeExtensions.GetMostCompactIntegerType(this.SourceIDSurface); // could cache this when points are being added

                using Dataset sourceIDdataset = this.CreateGdalRasterAndSetFilePath(sourceIDtilePath, 1, sourceIDbandType, compress);
                this.SourceIDSurface.Write(sourceIDdataset, 1);
                Debug.Assert(sourceIDdataset.RasterCount == 1);
            }
        }
    }
}