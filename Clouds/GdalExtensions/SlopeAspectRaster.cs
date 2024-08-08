using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public class SlopeAspectRaster : Raster
    {
        public const string CmmAspect3BandName = "cmmAspect3";
        public const string CmmSlope3BandName = "cmmSlope3";
        public const string DsmAspectBandName = "dsmAspect";
        public const string DsmSlopeBandName = "dsmSlope";

        public AspectBand CmmAspect3 { get; private set; }
        public SlopeBand CmmSlope3 { get; private set; }
        public AspectBand DsmAspect { get; private init; }
        public SlopeBand DsmSlope { get; private init; }

        public SlopeAspectRaster(Raster templateRaster, RasterBandPool? dataBufferPool)
            : base(templateRaster.Crs, templateRaster.Transform, templateRaster.SizeX, templateRaster.SizeY)
        {
            this.CmmAspect3 = new(this, SlopeAspectRaster.CmmAspect3BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            this.CmmSlope3 = new(this, SlopeAspectRaster.CmmSlope3BandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            this.DsmAspect = new(this, SlopeAspectRaster.DsmAspectBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
            this.DsmSlope = new(this, SlopeAspectRaster.DsmSlopeBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData, dataBufferPool);
        }

        public void CalculateSlopeAndAspect(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> cmmNeighborhood)
        {
            this.CalculateSlopeAndAspect(dsmNeighborhood, this.DsmSlope, this.DsmAspect);
            this.CalculateSlopeAndAspect(cmmNeighborhood, this.CmmSlope3, this.CmmAspect3);
        }

        private void CalculateSlopeAndAspect(VirtualRasterNeighborhood8<float> surfaceNeighborhood, RasterBand<float> slopeBand, RasterBand<float> aspectBand)
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
                if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                           hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                           hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                           weightX, weightY, out slope, out aspect))
                {
                    this.DsmSlope[xIndex, 0] = slope;
                    this.DsmAspect[xIndex, 0] = aspect;
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
            if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
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
                    if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
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
                if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
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
                if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
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
            if (SlopeAspectRaster.TryCalculateSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                             hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                             hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                             weightX, weightY, out slope, out aspect))
            {
                slopeBand[maxInteriorIndexX, maxIteriorIndexY] = slope;
                aspectBand[maxInteriorIndexX, maxIteriorIndexY] = aspect;
            }
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.DsmSlope.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.DsmAspect.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(name, this.CmmSlope3.Name, StringComparison.Ordinal))
            {
                return 2;
            }
            if (String.Equals(name, this.CmmAspect3.Name, StringComparison.Ordinal))
            {
                return 3;
            }
            
            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.DsmSlope;
            yield return this.DsmAspect;
            yield return this.CmmSlope3;
            yield return this.CmmAspect3;
        }

        public List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.DsmSlope.GetStatistics(),
                     this.DsmAspect.GetStatistics(),
                     this.CmmSlope3.GetStatistics(),
                     this.CmmAspect3.GetStatistics() ];
        }

        //public void Reset(string filePath, VirtualRasterNeighborhood8<float> surfaceNeighborhood)
        //{
        //    // inherited from Grid
        //    RasterBand<float> surface = surfaceNeighborhood.Center;
        //    if ((this.SizeX != surface.SizeX) || (this.SizeY != surface.SizeY))
        //    {
        //        throw new NotSupportedException(nameof(this.Reset) + " does not currently support changing the DSM's size from " + this.SizeX + " x " + this.SizeY + " cells to " + surface.SizeX + " x " + surface.SizeY + ".");
        //    }

        //    if (SpatialReferenceExtensions.IsSameCrs(this.Crs, surface.Crs) == false)
        //    {
        //        this.SetCrs(surface.Crs);
        //    }

        //    Debug.Assert(Object.ReferenceEquals(this.Transform, this.Aspect.Transform) && Object.ReferenceEquals(this.Transform, this.Slope.Transform) && 
        //                 ((this.Aspect3 == null) || Object.ReferenceEquals(this.Transform, this.Aspect3!.Transform)) && ((this.Slope3 == null) || Object.ReferenceEquals(this.Transform, this.Slope3!.Transform)));
        //    this.Transform.Copy(surface.Transform);

        //    // inherited from Raster
        //    this.FilePath = filePath;

        //    // slope and aspect fields
        //    Array.Fill(this.Aspect.Data, this.Aspect.NoDataValue);
        //    Array.Fill(this.Slope.Data, this.Slope.NoDataValue);
        //    if (this.Aspect3 != null)
        //    {
        //        Array.Fill(this.Aspect3.Data, this.Aspect3.NoDataValue);
        //    }
        //    if (this.Slope3 != null)
        //    {
        //        Array.Fill(this.Slope3.Data, this.Slope3.NoDataValue);
        //    }

        //    this.CalculateSlopeAndAspect(surfaceNeighborhood);
        //}

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBands(RasterBandPool dataBufferPool)
        {
            this.DsmAspect.ReturnData(dataBufferPool);
            this.DsmSlope.ReturnData(dataBufferPool);
            this.CmmAspect3.ReturnData(dataBufferPool);
            this.CmmSlope3.ReturnData(dataBufferPool);
        }

        //private void SetCrs(SpatialReference crs)
        //{
        //    this.Crs = crs;
        //    this.Aspect.Crs = crs;
        //    this.Slope.Crs = crs;

        //    if (this.Aspect3 != null)
        //    {
        //        this.Aspect3.Crs = crs;
        //    }
        //    if (this.Slope3 != null)
        //    {
        //        this.Slope3.Crs = crs;
        //    }
        //}

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
            if ((name == null) || String.Equals(this.DsmSlope.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmSlope;
            }
            else if (String.Equals(this.DsmAspect.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmAspect;
            }
            else if (String.Equals(this.CmmSlope3.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmSlope3;
            }
            else if (String.Equals(this.CmmAspect3.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmAspect3;
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
            Debug.Assert(this.DsmSlope.IsNoData(RasterBand.NoDataDefaultFloat) && this.DsmAspect.IsNoData(RasterBand.NoDataDefaultFloat) && this.CmmSlope3.IsNoData(RasterBand.NoDataDefaultFloat) && this.CmmAspect3.IsNoData(RasterBand.NoDataDefaultFloat));
            using Dataset slopeAspectDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, 4, DataType.GDT_Float32, compress);
            this.DsmSlope.Write(slopeAspectDataset, 1);
            this.DsmAspect.Write(slopeAspectDataset, 2);
            this.CmmSlope3.Write(slopeAspectDataset, 3);
            this.CmmAspect3.Write(slopeAspectDataset, 4);
            
            this.FilePath = rasterPath;
        }

        public class AspectBand(Raster raster, string name, float noDataValue, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool) : RasterBand<float>(raster, name, noDataValue, initialValue, dataBufferPool)
        {
            public override RasterBandStatistics GetStatistics()
            {
                return new(this.Data, this.HasNoDataValue, this.NoDataValue, 0.0F, 360.0F, 1.0F);
            }
        }

        public class SlopeBand(Raster raster, string name, float noDataValue, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool) : RasterBand<float>(raster, name, noDataValue, initialValue, dataBufferPool)
        {
            public override RasterBandStatistics GetStatistics()
            {
                return new(this.Data, this.HasNoDataValue, this.NoDataValue, 0.0F, 90.0F, 1.0F);
            }
        }
    }
}
