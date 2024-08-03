using Mars.Clouds.GdalExtensions;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Convert LAS integer xy coordinates to a corresponding cell indices in raster tile or other grid.
    /// </summary>
    /// <remarks>
    /// LAS points can be binned into grid cells in two ways.
    /// 
    /// 1) Convert the LAS's scaled integer coordinates to CRS doubles and then convert the doubles to integer grid cell indicies.
    /// 2) Reproject LAS coordinates directly to grid cell indices.
    /// 
    /// <see cref="LasToGridTransform"/> implements the latter using scaled 64 bit fixed point, removing conversion overhead and 
    /// reducing rounding error. LAS coordinates are often quantized to 1 mm for metric CRSes, 0.01 foot for CRSes with English units, 
    /// or similar resolutions (e.g. 0.1 mm) which cannot be represented exactly by doubles. Additionally, double implementations are
    /// likely unscaled, leading to underutilization of precision. LAS coordinates contain at most 31 significant bits but nearly
    /// always less since, at 1 mm resolution, ±2³¹ results in a LAS file extending ±2147 km from its origin (more practically, ±2²⁰
    /// = ±1.05 km or, with English at 0.01 ft resolution, ±3197 feet). The fixed point used varies with CRS units and LAS scale, 
    /// typically resulting in fractional numbers of integer and fraction bits, but LAS files with 1 mm and 0.01 ft resolution are 
    /// about Q21.10 and Q24.7, respectively. <see cref="LasToGridTransform"/>'s current implementation boosts these to Q23.40 and 
    /// Q26.37 internally.
    /// </remarks>
    public class LasToGridTransform
    {
        // for now, scale up by a fixed factor of 2³⁰ regardless of grid extents or .las file resolution
        // Since .las coordinates are Q32 and Q64 math is used in projecting to grid coordinates, at minimum an additional 32 bits are
        // available to increase numeric precision. Using 30 of them allows for grids up to 2² times larger than a .las's maximum size
        // (17,180 x 17,180 km at 1 mm resolution, 52,378 x 52x378 km at 0.01 ft) and exceeds double precision at 61 bits, resulting in
        // a numerical epsilon of 931 fm (femtometers) at 1 mm resolution. Precision can be increased (or decreased) if needed by
        // calculating the maximum non-overflowing scale from .las extents, offset, and resolution (some .las files have offsets of zero
        // and thus set more coordinate bits than necessary for the size of the .las). Using 32 bit transform math is not viable as
        // precision drops to 4 mm.
        private const Int64 FixedPointScale = 1024 * 1024 * 1024;

        private readonly Int64 cellSizeXscaled;
        private readonly Int64 cellSizeYscaled;
        private readonly Int64 gridColumnRotationDivisor;
        private readonly Int64 gridColumnRotationMultiplier;
        private readonly Int64 gridOriginXscaled;
        private readonly Int64 gridOriginYscaled;
        private readonly Int64 gridRowRotationScaled;
        private readonly int gridSizeX;
        private readonly int gridSizeY;
        private readonly Int64 lasMaxXscaled;
        private readonly Int64 lasMaxYscaled;

        public LasToGridTransform(LasHeader10 lasHeader, Grid grid)
        {
            if (grid.Transform.CellHeight > 0.0)
            {
                // must be matched with check in ToGridIndices()
                throw new NotSupportedException("Grids with positive cell heights are not currently supported.");
            }

            double gridCellSizeX = grid.Transform.CellWidth;
            double gridCellSizeY = grid.Transform.CellHeight;
            double gridOriginX = grid.Transform.OriginX;
            double gridOriginY = grid.Transform.OriginY;
            double lasScaleX = lasHeader.XScaleFactor;
            double lasScaleY = lasHeader.YScaleFactor;

            this.cellSizeXscaled = (Int64)(LasToGridTransform.FixedPointScale * gridCellSizeX / lasScaleX);
            this.cellSizeYscaled = (Int64)(LasToGridTransform.FixedPointScale * gridCellSizeY / lasScaleY);
            this.gridOriginXscaled = (Int64)(LasToGridTransform.FixedPointScale * (gridOriginX - lasHeader.XOffset) / lasScaleX);
            this.gridOriginYscaled = (Int64)(LasToGridTransform.FixedPointScale * (gridOriginY - lasHeader.YOffset) / lasScaleY);
            
            // hoist yIndex multiplies and divides for rotated grids
            Int64 gridColumnRotationScaled = (Int64)(LasToGridTransform.FixedPointScale * grid.Transform.ColumnRotation);
            this.gridColumnRotationMultiplier = gridColumnRotationScaled / this.cellSizeXscaled;
            this.gridColumnRotationDivisor = this.cellSizeYscaled - this.gridColumnRotationMultiplier * this.gridRowRotationScaled / this.cellSizeXscaled;
            this.gridRowRotationScaled = (Int64)(LasToGridTransform.FixedPointScale * grid.Transform.RowRotation);

            this.gridSizeX = grid.SizeX;
            this.gridSizeY = grid.SizeY;

            this.lasMaxXscaled = (Int64)(LasToGridTransform.FixedPointScale * (gridOriginX + grid.SizeX * gridCellSizeX - lasHeader.XOffset) / lasScaleX);
            this.lasMaxYscaled = (Int64)(LasToGridTransform.FixedPointScale * (gridOriginY + grid.SizeY * gridCellSizeY - lasHeader.YOffset) / lasScaleY);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryToOnGridIndices(ReadOnlySpan<byte> pointBytes, out Int64 xIndex, out Int64 yIndex)
        {
            Debug.Assert(this.cellSizeYscaled < 0);

            Int64 lasXscaled = LasToGridTransform.FixedPointScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]);
            Int64 lasYscaled = LasToGridTransform.FixedPointScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]);

            Int64 gridXscaled = lasXscaled - this.gridOriginXscaled;
            Int64 gridYscaled = lasYscaled - this.gridOriginYscaled;

            if (this.gridColumnRotationMultiplier != 0)
            {
                // yIndex = (yScaled - this.gridOriginYscaled - this.gridColumnRotationScaled / this.cellSizeXscaled * (xScaled - this.gridOriginXscaled)) / (this.cellSizeYscaled - this.gridColumnRotationScaled * this.gridRowRotationScaled / this.cellSizeXscaled);
                // xIndex = (xScaled - this.gridOriginXscaled - yIndexScaled * this.gridRowRotationScaled) / this.cellSizeXscaled;
                Int64 yIndexScaled = gridYscaled - this.gridColumnRotationMultiplier * gridXscaled;
                yIndex = yIndexScaled / this.gridColumnRotationDivisor;
                Int64 xIndexScaled = gridXscaled - yIndex * this.gridRowRotationScaled;
                xIndex = (gridXscaled - yIndex * this.gridRowRotationScaled) / this.cellSizeXscaled;

                // integer truncation rounds towards zero
                // These adjustments are required for accurate checking whether indices are less than zero and return of correct
                // indices. Without adjustment the coordinates are shifted by one cell from the correct position, resulting in incorrect
                // handling of 75% of the coordinate plane and points with indices of (-1, 0) being overlaid with points in [0, 1).
                if (xIndexScaled < 0)
                {
                    xIndex -= 1;
                }
                if (yIndexScaled < 0)
                {
                    yIndex -= 1;
                }

                // numerically exact edge cases likely need to be handled here
                if ((xIndex < 0) || (yIndex > 0) || (xIndex >= this.gridSizeX) || (yIndex >= this.gridSizeY))
                {
                    return false;
                }
            }
            else
            {
                xIndex = gridXscaled / this.cellSizeXscaled;
                yIndex = gridYscaled / this.cellSizeYscaled;

                // if point's x or y coordinate lies exactly on grid edge, consider point part of the grid
                // if point's coordinate lies to -x or +y of the grid, account for integer truncation
                // Subtracting one here saves loading the grid's size for edge cases. Integer truncation rounds towards zero and thus
                // also needs a subtract by one. These adjustments are not needed if the caller discards off grid indices but are 
                // required for correctness if off grid indices are used.
                if ((lasXscaled == this.lasMaxXscaled) || (gridXscaled < 0))
                {
                    xIndex -= 1;
                }
                if ((lasYscaled == this.lasMaxYscaled) || (gridYscaled > 0))
                {
                    yIndex -= 1;
                }

                if ((gridXscaled < 0) || (gridYscaled > 0) || (lasXscaled > this.lasMaxXscaled) || (lasYscaled < this.lasMaxYscaled))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
