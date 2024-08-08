using DocumentFormat.OpenXml.Drawing.Charts;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public class SlopeAspectRaster : Raster
    {
        public const string AspectBandName = "aspect";
        public const string SlopeBandName = "slope";

        public RasterBand<float> Aspect { get; private init; }
        public RasterBand<float> Slope { get; private init; }

        public SlopeAspectRaster(VirtualRasterNeighborhood8<float> surfaceNeighborhood)
            : base(surfaceNeighborhood.Center.Crs, surfaceNeighborhood.Center.Transform, surfaceNeighborhood.Center.SizeX, surfaceNeighborhood.Center.SizeY)
        {
            RasterBand<float> surface = surfaceNeighborhood.Center;
            if ((surface.Transform.CellHeight > 0.0) || (surface.SizeX < 2) || (surface.SizeY < 2))
            {
                throw new NotSupportedException("Surface raster has a positive cell height or is smaller than 2x2 cells.");
            }

            this.Aspect = new(this, SlopeAspectRaster.AspectBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.Slope = new(this, SlopeAspectRaster.SlopeBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);

            // calculate slope and aspect
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
                if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                           hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                           hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                           weightX, weightY, out slope, out aspect))
                {
                    this.Slope[xIndex, 0] = slope;
                    this.Aspect[xIndex, 0] = aspect;
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
            if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                       hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                       hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                       weightX, weightY, out slope, out aspect))
            {
                this.Slope[maxInteriorIndexX, 0] = slope;
                this.Aspect[maxInteriorIndexX, 0] = aspect;
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
                    if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                               hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                               hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                               weightX, weightY, out slope, out aspect))
                    {
                        this.Slope[xIndex, yIndex] = slope;
                        this.Aspect[xIndex, yIndex] = aspect;
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
                if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                           hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                           hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                           weightX, weightY, out slope, out aspect))
                {
                    this.Slope[maxInteriorIndexX, yIndex] = slope;
                    this.Aspect[maxInteriorIndexX, yIndex] = aspect;
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
                if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                           hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                           hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                           weightX, weightY, out slope, out aspect))
                {
                    this.Slope[xIndex, maxIteriorIndexY] = slope;
                    this.Aspect[xIndex, maxIteriorIndexY] = aspect;
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
            if (SlopeAspectRaster.TryGetSlopeAndAspect(hasNorthwest, zNorthwest, hasNorth, zNorth, hasNortheast, zNortheast,
                                                       hasWest, zWest, hasCenter, zCenter, hasEast, zEast,
                                                       hasSouthwest, zSouthwest, hasSouth, zSouth, hasSoutheast, zSoutheast,
                                                       weightX, weightY, out slope, out aspect))
            {
                this.Slope[maxInteriorIndexX, maxIteriorIndexY] = slope;
                this.Aspect[maxInteriorIndexX, maxIteriorIndexY] = aspect;
            }
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.Slope.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.Aspect.Name, StringComparison.Ordinal))
            {
                return 1;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Slope;
            yield return this.Aspect;
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || (String.Equals(this.Slope.Name, name, StringComparison.Ordinal)))
            {
                band = this.Slope;
            }
            else if (String.Equals(this.Aspect.Name, name, StringComparison.Ordinal))
            {
                band = this.Aspect;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetSlopeAndAspect(bool hasNorthwest, float zNorthwest, bool hasNorth, float zNorth, bool hasNortheast, float zNortheast,
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
                fy = weightX * (zSoutheast - zSouthwest + zNorthwest - zNortheast);
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
                // try for simple fx and fy
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
                aspect = 360.0F + aspect;
            }
            return true;
        }

        public override void Write(string rasterPath, bool compress)
        {
            Debug.Assert(this.Slope.IsNoData(RasterBand.NoDataDefaultFloat) && this.Aspect.IsNoData(RasterBand.NoDataDefaultFloat));
            using Dataset slopeAspectDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, 2, DataType.GDT_Float32, compress);
            this.Slope.Write(slopeAspectDataset, 1);
            this.Aspect.Write(slopeAspectDataset, 2);
            this.FilePath = rasterPath;
        }
    }
}
