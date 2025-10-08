using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    /// <summary>
    /// One band of a virtual raster tile and, where extant, its neighboring bands in the eight neighboring tiles (first order queen adjacency).
    /// </summary>
    /// <remarks>
    /// Can't derive from <see cref="GridNeighborhood8"/> as <see cref="RasterBand"/> is <see cref="Grid"/> rather than <see cref="Grid{TCell}"/>.
    /// </remarks>
    public class RasterNeighborhood8<TBand> : Neighborhood8<RasterBand<TBand>> where TBand : struct, IMinMaxValue<TBand>, INumber<TBand>
    {
        public RasterNeighborhood8(RasterBand<TBand> center)
            : base(center)
        {
        }

        public (TBand value, byte mask) GetValueMaskZero(int xIndex, int yIndex)
        {
            if (this.TryGetValue(xIndex, yIndex, out TBand value))
            {
                return (value, 1);
            }

            return (TBand.Zero, 0);
        }

        public void Slice(int xOrigin, int yOrigin, int sizeX, int sizeY, Span<TBand> slice)
        {
            Debug.Assert(sizeX * sizeY == slice.Length);
            // TODO: what if different tiles in the neighborhood have different no data values?

            RasterBand<TBand> center = this.Center;
            if ((sizeX < 1) || (sizeX > center.SizeX) || (sizeY < 1) || (sizeY > center.SizeY))
            {
                throw new ArgumentException($"An {sizeX} by {sizeY} slice is not supported. One or both dimensions is negative, zero, or exceeds the {center.SizeX} by {center.SizeY} size of the neighborhood's center tile.");
            }

            int xMax = xOrigin + sizeX; // exclusive
            int yMax = yOrigin + sizeY; // exclusive
            if ((xMax < 0) || (yMax < 0) || (xOrigin >= center.SizeX) || (yOrigin >= center.SizeY))
            {
                throw new ArgumentException($"Slice with origin ({xOrigin}, {yOrigin}) and size {sizeX} by {sizeY} does not overlap the neighborhood's {center.SizeX} by {center.SizeY} center tile.");
            }

            // slice is entirely within center tile
            // If needed, slices entirely within other parts of the neighborhood can be supported.
            if ((xOrigin >= 0) && (yOrigin >= 0) && (xMax <= center.SizeX) && (yMax <= center.SizeY))
            {
                this.Center.Slice(xOrigin, yOrigin, sizeX, sizeY, slice);
                return;
            }

            // slice extends into at least one neighboring tile
            int centerOriginX = xOrigin < 0 ? 0 : xOrigin;
            int centerOriginY = yOrigin < 0 ? 0 : yOrigin;
            int centerEndX = xMax <= center.SizeX ? xMax : center.SizeX;
            int centerSizeX = centerEndX - centerOriginX;

            // north neighbors, if applicable
            int sliceDestinationIndex;
            int sliceOriginX;
            if (yOrigin < 0)
            {
                // portion of slice overlapping northwest tile
                // If there is northwest overlap there is also north and west overlap. Similar at the center tile's other three
                // corners.
                if ((this.Northwest != null) && (xOrigin < 0))
                {
                    RasterBand<TBand> northwest = this.Northwest;
                    int northwestOriginX = northwest.SizeX + xOrigin;
                    int northwestOriginY = northwest.SizeY + yOrigin;
                    int northwestRowStartIndex = northwest.ToCellIndex(northwestOriginX, northwestOriginY);
                    int northwestSizeX = -xOrigin;
                    sliceDestinationIndex = 0; // sliceOriginX = sliceOriginY = 0
                    for (int yIndex = northwestOriginY; yIndex < northwest.SizeY; ++yIndex)
                    {
                        northwest.Data.AsSpan(northwestRowStartIndex, northwestSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        northwestRowStartIndex += northwest.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }

                // portion of slice overlapping north tile
                if (this.North != null)
                {
                    RasterBand<TBand> north = this.North;
                    int northOriginX = xOrigin;
                    int northSizeX = centerSizeX;
                    sliceOriginX = 0;
                    if (xOrigin < 0)
                    {
                        northOriginX = 0;
                        northSizeX = sizeX + xOrigin;
                        sliceOriginX = -xOrigin;
                    }
                    int northOriginY = north.SizeY + yOrigin;
                    int northRowStartIndex = north.ToCellIndex(northOriginX, northOriginY);
                    sliceDestinationIndex = sliceOriginX; // sliceOriginY = 0
                    for (int yIndex = northOriginY; yIndex < north.SizeY; ++yIndex)
                    {
                        north.Data.AsSpan(northRowStartIndex, northSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        northRowStartIndex += north.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }

                // portion of slice overlapping northeast tile
                if ((this.Northeast != null) && (xMax >= center.SizeX))
                {
                    RasterBand<TBand> northeast = this.Northeast;
                    int northeastOriginX = 0;
                    int northeastOriginY = northeast.SizeY + yOrigin;
                    int northeastRowStartIndex = northeast.ToCellIndex(northeastOriginX, northeastOriginY);
                    int northeastSizeX = sizeX - centerSizeX;
                    sliceOriginX = centerSizeX; // sizeX - northeastSizeX = centerSizeX
                    sliceDestinationIndex = sliceOriginX; // sliceOriginY = 0
                    for (int yIndex = northeastOriginY; yIndex < northeast.SizeY; ++yIndex)
                    {
                        northeast.Data.AsSpan(northeastRowStartIndex, northeastSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        northeastRowStartIndex += northeast.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }
            }

            // portion of slice overlapping west tile
            int centerEndY = yMax <= center.SizeY ? yMax : center.SizeY;
            int sliceOriginY = yOrigin < 0 ? -yOrigin : 0;
            if ((this.West != null) && (xOrigin < 0))
            {
                RasterBand<TBand> west = this.West;
                int westOriginX = west.SizeX + xOrigin;
                int westOriginY = centerOriginY;
                int westRowStartIndex = west.ToCellIndex(westOriginX, westOriginY);
                int westSizeX = -xOrigin;
                sliceDestinationIndex = sizeX * sliceOriginY; // sliceOriginX = 0
                for (int yIndex = westOriginY; yIndex < centerEndY; ++yIndex)
                {
                    west.Data.AsSpan(westRowStartIndex, westSizeX).CopyTo(slice[sliceDestinationIndex..]);
                    westRowStartIndex += west.SizeX;
                    sliceDestinationIndex += sizeX;
                }
            }

            // portion of slice overlapping center tile
            int centerRowStartIndex = center.ToCellIndex(centerOriginX, centerOriginY);
            sliceOriginX = xOrigin < 0 ? -xOrigin : 0;
            sliceDestinationIndex = sizeX * sliceOriginY + sliceOriginX;
            for (int yIndex = centerOriginY; yIndex < centerEndY; ++yIndex)
            {
                center.Data.AsSpan(centerRowStartIndex, centerSizeX).CopyTo(slice[sliceDestinationIndex..]);
                centerRowStartIndex += center.SizeX;
                sliceDestinationIndex += sizeX;
            }

            // portion of slice overlapping east tile
            if ((this.East != null) && (xMax >= this.Center.SizeX))
            {
                RasterBand<TBand> east = this.East;
                int eastOriginX = 0;
                int eastOriginY = centerOriginY;
                int eastRowStartIndex = east.ToCellIndex(eastOriginX, eastOriginY);
                int eastSizeX = sizeX - centerSizeX;
                sliceOriginX = centerSizeX;
                sliceDestinationIndex = sizeX * sliceOriginY + sliceOriginX;
                for (int yIndex = eastOriginY; yIndex < centerEndY; ++yIndex)
                {
                    east.Data.AsSpan(eastRowStartIndex, eastSizeX).CopyTo(slice[sliceDestinationIndex..]);
                    eastRowStartIndex += east.SizeX;
                    sliceDestinationIndex += sizeX;
                }
            }

            // south neighbors, if applicable
            if (yMax >= center.SizeY)
            {
                int southEndY = yMax - center.SizeY;
                sliceOriginY = sizeY - southEndY;

                // portion of slice overlapping southwest tile
                if ((this.Southwest != null) && (xOrigin < 0))
                {
                    RasterBand<TBand> southwest = this.Southwest;
                    int southwestOriginX = southwest.SizeX + xOrigin;
                    int southwestOriginY = 0;
                    int southwestRowStartIndex = southwest.ToCellIndex(southwestOriginX, southwestOriginY);
                    int southwestSizeX = -xOrigin;
                    sliceDestinationIndex = sizeX * sliceOriginY; // sliceOriginX = 0
                    for (int yIndex = southwestOriginY; yIndex < southEndY; ++yIndex)
                    {
                        southwest.Data.AsSpan(southwestRowStartIndex, southwestSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        southwestRowStartIndex += southwest.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }

                // portion of slice overlapping south tile
                if (this.South != null)
                {
                    RasterBand<TBand> south = this.South;
                    int southOriginX = xOrigin;
                    int southOriginY = 0;
                    int southSizeX = centerSizeX;
                    sliceOriginX = 0;
                    if (xOrigin < 0)
                    {
                        southOriginX = 0;
                        sliceOriginX = -xOrigin;
                    }
                    int southRowStartIndex = south.ToCellIndex(southOriginX, southOriginY);
                    sliceDestinationIndex = sizeX * sliceOriginY + sliceOriginX;
                    for (int yIndex = southOriginY; yIndex < southEndY; ++yIndex)
                    {
                        south.Data.AsSpan(southRowStartIndex, southSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        southRowStartIndex += south.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }

                // portion of slice overlapping southeast tile
                if ((this.Southeast != null) && (xMax >= center.SizeX))
                {
                    RasterBand<TBand> southeast = this.Southeast;
                    int southeastOriginX = 0;
                    int southeastOriginY = 0;
                    int southeastRowStartIndex = southeast.ToCellIndex(southeastOriginX, southeastOriginY);
                    int southeastSizeX = sizeX - centerSizeX;
                    sliceOriginX = centerSizeX;
                    sliceDestinationIndex = sizeX * sliceOriginY + sliceOriginX;
                    for (int yIndex = southeastOriginY; yIndex < southEndY; ++yIndex)
                    {
                        southeast.Data.AsSpan(southeastRowStartIndex, southeastSizeX).CopyTo(slice[sliceDestinationIndex..]);
                        southeastRowStartIndex += southeast.SizeX;
                        sliceDestinationIndex += sizeX;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int xIndex, int yIndex, out TBand value)
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
