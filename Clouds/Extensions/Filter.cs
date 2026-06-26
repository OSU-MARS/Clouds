using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds.Extensions
{
    internal class Filter
    {
        /// <summary>
        /// Smooths a raster from edge to edge with a 3x3 moving average using 1 cell of nearest neighbor padding.
        /// </summary>
        public static void Average3x3(RasterBand<float> input, RasterBand<float> lowpassed, ref RowBuffers? rowBuffers)
        {
            if ((input.SizeX < 3) || (input.SizeY < 2))
            {
                throw new NotImplementedException($"Special casing for small rasters is not currently implemented. Input raster is {input.SizeX} by {input.SizeY} cells.");
            }
            if ((input.SizeX != lowpassed.SizeX) || (input.SizeY != lowpassed.SizeY))
            {
                throw new ArgumentException($"Input and output rasters must be the same size. Input raster is {input.SizeX} by {input.SizeY}, lowpassed output raster is {lowpassed.SizeX} by {lowpassed.SizeY} cells.");
            }
            if ((rowBuffers == null) || (rowBuffers.RowBuffer3.Length < input.SizeX) || (rowBuffers.RowBuffer1.Length < input.SizeX) || (rowBuffers.RowBuffer2.Length < input.SizeX))
            {
                rowBuffers = new RowBuffers(input.SizeX);
            }

            // smooth first input row
            const float individualCellWeight = 1.0F / (3.0F * 3.0F);
            float[] nextRow = rowBuffers.RowBuffer1;
            float nextRowValue;
            float centerCellValue = individualCellWeight * input[0, 0]; // TODO: fill no data
            float previousCellValue = centerCellValue; // value of unsmoothed[-1] is unknown, so default to nearest neighbor interpolation
            for (int xIndex = 0, xIndexNext = 1; xIndexNext < input.SizeX; ++xIndexNext, ++xIndex)
            {
                float nextCellValue = individualCellWeight * input[xIndexNext, 0]; // yIndex is 0
                nextRowValue = previousCellValue + centerCellValue + nextCellValue; // recalculate to avoid numerical error accumulation
                nextRow[xIndex] = nextRowValue;

                previousCellValue = centerCellValue;
                centerCellValue = nextCellValue;
            }
            nextRow[input.SizeX - 1] = previousCellValue + centerCellValue + centerCellValue; // nextRow[^1] will be incorrect if longer buffers have been allocated, nearest neighbor interpolation of unknown value of unsmoothed[xIndexNext, 0]

            // first output row: only center and next rows
            float[] centerRow = nextRow;
            float centerRowValue;
            nextRow = rowBuffers.RowBuffer2;
            centerCellValue = individualCellWeight * input[0, 1]; // TODO: fill no data
            previousCellValue = centerCellValue;
            for (int xIndex = 0, xIndexNext = 1; xIndexNext < input.SizeX; ++xIndexNext, ++xIndex)
            {
                float nextCellValue = individualCellWeight * input[xIndexNext, 1];
                nextRowValue = previousCellValue + centerCellValue + nextCellValue;
                nextRow[xIndex] = nextRowValue;

                centerRowValue = centerRow[xIndex];
                lowpassed[xIndex, 0] = centerRowValue + centerRowValue + nextRowValue; // nearest neighbor interpolation of previous row

                previousCellValue = centerCellValue;
                centerCellValue = nextCellValue;
            }
            nextRowValue = previousCellValue + centerCellValue + centerCellValue;
            nextRow[input.SizeX - 1] = nextRowValue;
            centerRowValue = centerRow[input.SizeX - 1];
            lowpassed[input.SizeX - 1] = centerRowValue + centerRowValue + nextRowValue;

            // all middle output rows: previous, center, and next input rows
            float[] previousRow = centerRow;
            float previousRowValue;
            centerRow = nextRow;
            nextRow = rowBuffers.RowBuffer3;
            int yIndex;
            for (int yIndexNext = 2; yIndexNext < input.SizeY; ++yIndexNext)
            {
                yIndex = yIndexNext - 1;
                centerCellValue = individualCellWeight * input[0, yIndexNext];
                previousCellValue = centerCellValue;
                for (int xIndex = 0, xIndexNext = 1; xIndexNext < lowpassed.SizeX; ++xIndexNext, ++xIndex)
                {
                    float nextCellValue = individualCellWeight * input[xIndexNext, yIndexNext];
                    nextRowValue = previousCellValue + centerCellValue + nextCellValue;
                    nextRow[xIndex] = nextRowValue;

                    previousRowValue = previousRow[xIndex];
                    centerRowValue = centerRow[xIndex];
                    lowpassed[xIndex, yIndex] = previousRowValue + centerRowValue + nextRowValue;

                    previousCellValue = centerCellValue;
                    centerCellValue = nextCellValue;
                }

                nextRowValue = previousCellValue + centerCellValue + centerCellValue;
                nextRow[input.SizeX - 1] = nextRowValue;
                centerRowValue = centerRow[input.SizeX - 1];
                previousRowValue = previousRow[input.SizeX - 1];
                lowpassed[input.SizeX - 1, yIndex] = previousRowValue + centerRowValue + nextRowValue;

                (previousRow, centerRow, nextRow) = (centerRow, nextRow, previousRow); // advance buffers for next iteration
            }

            // last output row: only previous and center rows
            previousRow = centerRow;
            centerRow = nextRow;
            yIndex = lowpassed.SizeY - 1;
            for (int xIndex = 0; xIndex < lowpassed.SizeX; ++xIndex)
            {
                previousRowValue = previousRow[xIndex];
                centerRowValue = centerRow[xIndex];
                lowpassed[xIndex, yIndex] = previousRowValue + centerRowValue + centerRowValue; // nearest neighbor interpolation of next row
            }
        }
    }
}
