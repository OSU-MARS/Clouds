﻿using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    /// <summary>
    /// One band of a virtual raster tile and, where extant, its neighboring bands in the eight neighboring tiles (first order queen adjacency).
    /// </summary>
    public class VirtualRasterNeighborhood8<TBand> where TBand : IMinMaxValue<TBand>, INumber<TBand>
    {
        public RasterBand<TBand> Center { get; private init; }
        public RasterBand<TBand>? North { get; init; }
        public RasterBand<TBand>? Northeast { get; init; }
        public RasterBand<TBand>? Northwest { get; init; }
        public RasterBand<TBand>? South { get; init; }
        public RasterBand<TBand>? Southeast { get; init; }
        public RasterBand<TBand>? Southwest { get; init; }
        public RasterBand<TBand>? East { get; init; }
        public RasterBand<TBand>? West { get; init; }

        public VirtualRasterNeighborhood8(RasterBand<TBand> center)
        {
            this.Center = center;
        }

        public (TBand value, byte mask) GetValueMaskZero(int xIndex, int yIndex)
        {
            if (this.TryGetValue(xIndex, yIndex, out TBand? value))
            {
                return (value, 1);
            }

            return (TBand.Zero, 0);
        }

        public bool TryGetValue(int xIndex, int yIndex, [NotNullWhen(true)] out TBand? value)
        {
            value = default;
            if (yIndex < 0)
            {
                if (xIndex < 0)
                {
                    if (this.Northwest == null)
                    {
                        return false;
                    }
                    int northwestYindex = this.Northwest.SizeY + yIndex;
                    int northwestXindex = this.Northwest.SizeX + xIndex;
                    value = this.Northwest[northwestXindex, northwestYindex];
                    if (this.Northwest.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.Northeast == null)
                    {
                        return false;
                    }
                    int northeastYindex = this.Northeast.SizeY + yIndex;
                    int northeastXindex = xIndex - this.Center.SizeX;
                    value = this.Northeast[northeastXindex, northeastYindex];
                    if (this.Northeast.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    if (this.North == null)
                    {
                        return false;
                    }
                    int northYindex = this.North.SizeY + yIndex;
                    value = this.North[xIndex, northYindex];
                    if (this.North.IsNoData(value))
                    {
                        return false;
                    }
                }
            }
            else if (yIndex >= this.Center.SizeY)
            {
                int southYindex = yIndex - this.Center.SizeY;
                if (xIndex < 0)
                {
                    if (this.Southwest == null)
                    {
                        return false;
                    }
                    int westXindex = this.Southwest.SizeX + xIndex;
                    value = this.Southwest[westXindex, southYindex];
                    if (this.Southwest.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.Southeast == null)
                    {
                        return false;
                    }
                    int eastXindex = xIndex - this.Center.SizeX;
                    value = this.Southeast[eastXindex, southYindex];
                    if (this.Southeast.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    if (this.South == null)
                    {
                        return false;
                    }
                    value = this.South[xIndex, southYindex];
                    if (this.South.IsNoData(value))
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (xIndex < 0)
                {
                    if (this.West == null)
                    {
                        return false;
                    }
                    int westXindex = this.West.SizeX + xIndex;
                    value = this.West[westXindex, yIndex];
                    if (this.West.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.East == null)
                    {
                        return false;
                    }
                    int eastXindex = xIndex - this.Center.SizeX;
                    value = this.East[eastXindex, yIndex];
                    if (this.East.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    // mainline case
                    value = this.Center[xIndex, yIndex];
                    if (this.Center.IsNoData(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
