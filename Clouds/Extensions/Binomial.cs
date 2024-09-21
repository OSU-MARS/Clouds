using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mars.Clouds.Extensions
{
    public class Binomial
    {
        // https://bartwronski.com/2021/10/31/practical-gaussian-filter-binomial-filter-and-small-sigma-gaussians/
        // https://math.stackexchange.com/questions/2679110/binomial-approximation-of-gaussian-distribution
        // same as approximation of Gaussian smoothing kernel creation via Pascal's triangle
        // Generating R code for kernels. Kernels are normalized so coefficients sum to 1.0.
        // pascal3 = c(1, 2, 1) / sum(c(1, 2, 1))
        // pascal3 * matrix(pascal3, nrow = length(pascal3), ncol = length(pascal3), byrow = TRUE)
        //
        // pascal5 = c(1, 4, 6, 4, 1) / sum(c(1, 4, 6, 4, 1))
        // pascal5 * matrix(pascal5, nrow = length(pascal5), ncol = length(pascal5), byrow = TRUE)
        //
        // pascal7 = c(1, 6, 15, 20, 15, 6, 1) / sum(c(1, 6, 15, 20, 15, 6, 1))
        // pascal7 * matrix(pascal7, nrow = length(pascal7), ncol = length(pascal7), byrow = TRUE)
        //
        // 5x5 smoothing kernel
        // 1/64 * [ 1,  4,  6,  4, 1, = [ 0.00390625F, 0.015625F, 0.0234375F, 0.015625F, 0.00390625F,
        //          4, 16, 24, 16, 4      0.01562500F, 0.062500F, 0.0937500F, 0.062500F, 0.01562500F,
        //          6, 24, 36, 24, 6      0.02343750F, 0.093750F, 0.1406250F, 0.093750F, 0.02343750F,
        //          4, 16, 24, 16, 4      0.01562500F, 0.062500F, 0.0937500F, 0.062500F, 0.01562500F,
        //          1,  4,  6,  4, 1      0.00390625F, 0.015625F, 0.0234375F, 0.015625F, 0.00390625F ];
        // 7x7 smoothing kernel
        // 1/4096 * [  1,  6,  15,  20,  15,   6,  1, = [ 0.0002441406F, 0.001464844F, 0.003662109F, 0.004882812F, 0.003662109F, 0.001464844F, 0.0002441406F,
        //             6, 36,  90, 120,  90,  36,  6      0.0014648438F, 0.008789062F, 0.021972656F, 0.029296875F, 0.021972656F, 0.008789062F, 0.0014648438F,
        //           15,  90, 225, 300, 225,  90, 15      0.0036621094F, 0.021972656F, 0.054931641F, 0.073242188F, 0.054931641F, 0.021972656F, 0.0036621094F,
        //           20, 120, 300, 400, 300, 120, 20      0.0048828125F, 0.029296875F, 0.073242188F, 0.097656250F, 0.073242188F, 0.029296875F, 0.0048828125F,
        //           15,  90, 225, 300, 225,  90, 15      0.0036621094F, 0.021972656F, 0.054931641F, 0.073242188F, 0.054931641F, 0.021972656F, 0.0036621094F,
        //            6,  36,  90, 120,  90,  36,  6      0.0014648438F, 0.008789062F, 0.021972656F, 0.029296875F, 0.021972656F, 0.008789062F, 0.0014648438F,
        //            1,   6,  15,  20,  15,   6,  1      0.0002441406F, 0.001464844F, 0.003662109F, 0.004882812F, 0.003662109F, 0.001464844F, 0.0002441406F ];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector128<float> previousRowValues, Vector128<float> currentRowValues, Vector128<float> nextRowValues, Vector128<byte> maskVector, byte currentRowCurrentMask) AdvanceToLastCellInRow(RasterNeighborhood8<float> neighborhood, int yIndex, Vector128<float> previousRowValues, Vector128<float> currentRowValues, Vector128<float> nextRowValues, Vector128<byte> maskVector, byte currentRowNextMask)
        {
            byte currentRowCenterMask = currentRowNextMask;
            int xIndexNext = neighborhood.Center.SizeX;

            (float previousRowNextValue, byte previousRowNextMask) = neighborhood.GetValueMaskZero(xIndexNext, yIndex - 1);
            previousRowValues = AvxExtensions.ShuffleInAndUp(previousRowNextValue, previousRowValues);

            (float currentRowNextValue, currentRowNextMask) = neighborhood.GetValueMaskZero(xIndexNext, yIndex);
            currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);

            (float nextRowNextValue, byte nextRowNextMask) = neighborhood.GetValueMaskZero(xIndexNext, yIndex + 1);
            nextRowValues = AvxExtensions.ShuffleInAndUp(nextRowNextValue, nextRowValues);
            
            maskVector = AvxExtensions.ShuffleInAndUp(previousRowNextMask, currentRowNextMask, nextRowNextMask, maskVector);

            return (previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowCenterMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Convolve3x3(Vector128<float> kernelEdge, Vector128<float> kernelCenter, Vector128<sbyte> kernelWeights, Vector128<float> previousRowValues, Vector128<float> currentRowValues, Vector128<float> nextRowValues, Vector128<byte> mask)
        {
            Vector128<float> weightedValueSum = Fma.MultiplyAdd(kernelEdge, nextRowValues, Fma.MultiplyAdd(kernelCenter, currentRowValues, Avx.Multiply(kernelEdge, previousRowValues)));
            weightedValueSum = Avx.HorizontalAdd(weightedValueSum, weightedValueSum);
            weightedValueSum = Avx.HorizontalAdd(weightedValueSum, weightedValueSum);
            Vector128<Int16> weightSum = Avx.MultiplyAddAdjacent(mask, kernelWeights); // e0, e1, e2, e3, e4, e5, e6 = 0, e7 = 0
            weightSum = Avx.HorizontalAdd(weightSum, weightSum); // e0 = e0 + e1, e1 = e2 + e3, e2 = e4 + e5, e3 = e6 + e7
            weightSum = Avx.HorizontalAdd(weightSum, weightSum); // e0 = e0 + e1 + e2 + e3, e1 = e4 + e5 + e6 + e7
            weightSum = Avx.HorizontalAdd(weightSum, weightSum); // e0 = e0 + e1 + e2 + e3 + e4 + e5 + e6 + e7
            return weightedValueSum.ToScalar() / (float)weightSum.ToScalar();
        }

        //private static (byte currentRowNextMask, byte currentRowCenterMask) GatherInitial3(RasterBand<float> band, int yIndexNext, out Vector128<float> previousRowValues, out Vector128<float> currentRowValues, out Vector128<float> nextRowValues, out Vector128<byte> maskVector)
        //{
        //    int xIndex = 0;
        //    int xIndexNext = 1;
        //    int yIndexPrevious = yIndexNext - 2;
        //    byte previousRowCenterMask;
        //    byte previousRowNextMask;
        //    if (yIndexPrevious >= 0)
        //    {
        //        (float previousRowCenterValue, previousRowCenterMask) = band.GetValueMaskZero(xIndex, yIndexPrevious);
        //        (float previousRowNextValue, previousRowNextMask) = band.GetValueMaskZero(xIndexNext, yIndexPrevious);
        //        previousRowValues = Vector128.Create(previousRowNextValue, previousRowCenterValue, 0.0F, 0.0F);
        //    }
        //    else
        //    {
        //        previousRowValues = Vector128<float>.Zero;
        //        previousRowCenterMask = 0;
        //        previousRowNextMask = 0;
        //    }

        //    int yIndex = yIndexNext - 1;
        //    (float currentRowCenterValue, byte currentRowCenterMask) = band.GetValueMaskZero(xIndex, yIndex);
        //    (float currentRowNextValue, byte currentRowNextMask) = band.GetValueMaskZero(xIndexNext, yIndex);
        //    currentRowValues = Vector128.Create(currentRowNextValue, currentRowCenterValue, 0.0F, 0.0F);

        //    byte nextRowCenterMask;
        //    byte nextRowNextMask;
        //    if (yIndexNext < band.YSize)
        //    {
        //        (float nextRowCenterValue, nextRowCenterMask) = band.GetValueMaskZero(xIndex, yIndexNext);
        //        (float nextRowNextValue, nextRowNextMask) = band.GetValueMaskZero(xIndexNext, yIndexNext);
        //        nextRowValues = Vector128.Create(nextRowNextValue, nextRowCenterValue, 0.0F, 0.0F);
        //    }
        //    else
        //    {
        //        nextRowValues = Vector128<float>.Zero;
        //        nextRowCenterMask = 0;
        //        nextRowNextMask = 0;
        //    }

        //    maskVector = Vector128.Create(previousRowNextMask, currentRowNextMask, nextRowNextMask, 0,
        //                                  previousRowCenterMask, currentRowCenterMask, nextRowCenterMask, 0,
        //                                  0, 0, 0, 0,
        //                                  0, 0, 0, 0);
        //    return (currentRowNextMask, currentRowCenterMask);
        //}

        private static (byte currentRowNextMask, byte currentRowCenterMask) GatherInitial3(RasterNeighborhood8<float> neighborhood, int yIndexNext, out Vector128<float> previousRowValues, out Vector128<float> currentRowValues, out Vector128<float> nextRowValues, out Vector128<byte> maskVector)
        {
            int yIndexPrevious = yIndexNext - 2;
            (float previousRowPreviousValue, byte previousRowPreviousMask) = neighborhood.GetValueMaskZero(xIndex: -1, yIndexPrevious);
            (float previousRowCenterValue, byte previousRowCenterMask) = neighborhood.GetValueMaskZero(xIndex: 0, yIndexPrevious);
            (float previousRowNextValue, byte previousRowNextMask) = neighborhood.GetValueMaskZero(xIndex: 1, yIndexPrevious);
            previousRowValues = Vector128.Create(previousRowNextValue, previousRowCenterValue, previousRowPreviousValue, 0.0F);

            RasterBand<float> band = neighborhood.Center;
            int yIndex = yIndexNext - 1;
            (float currentRowPreviousValue, byte currentRowPreviousMask) = neighborhood.GetValueMaskZero(xIndex: -1, yIndex);
            (float currentRowCenterValue, byte currentRowCenterMask) = band.GetValueMaskZero(xIndex: 0, yIndex); // only center cell is guaranteed to be within the band
            (float currentRowNextValue, byte currentRowNextMask) = neighborhood.GetValueMaskZero(xIndex: 1, yIndex);
            currentRowValues = Vector128.Create(currentRowNextValue, currentRowCenterValue, currentRowPreviousValue, 0.0F);

            (float nextRowPreviousValue, byte nextRowPreviousMask) = neighborhood.GetValueMaskZero(xIndex: -1, yIndexNext);
            (float nextRowCenterValue, byte nextRowCenterMask) = neighborhood.GetValueMaskZero(xIndex: 0, yIndexNext);
            (float nextRowNextValue, byte nextRowNextMask) = neighborhood.GetValueMaskZero(xIndex: 1, yIndexNext);
            nextRowValues = Vector128.Create(nextRowNextValue, nextRowCenterValue, nextRowPreviousValue, 0.0F);

            maskVector = Vector128.Create(previousRowNextMask, currentRowNextMask, nextRowNextMask, 0,
                                          previousRowCenterMask, currentRowCenterMask, nextRowCenterMask, 0,
                                          previousRowPreviousMask, currentRowPreviousMask, nextRowPreviousMask, 0,
                                          0, 0, 0, 0);
            return (currentRowNextMask, currentRowCenterMask);
        }

        /// <summary>
        /// Virtual raster and no data compatible 3x3 Gaussian smooth with incomplete computations at edges.
        /// </summary>
        /// <remarks>
        /// 
        /// Smoothing kernel is 1/16 * [ 1, 2, 1,  = [ 0.0625, 0.125, 0.0625,
        ///                              2, 4, 2,      0.1250, 0.250, 0.1250,
        ///                              1, 2, 1 ]     0.0625, 0.125, 0.0625 ]
        /// </remarks>
        public static void Smooth3x3(RasterNeighborhood8<float> neighborhood, RasterBand<float> smoothed)
        {
            RasterBand<float> band = neighborhood.Center;
            if ((band.SizeX != smoothed.SizeX) || (band.SizeY != smoothed.SizeY))
            {
                throw new ArgumentOutOfRangeException(nameof(smoothed), "Input and output bands are different sizes. Input band is " + band.SizeX + " x " + band.SizeY + " cells. Output band is " + smoothed.SizeX + " x " + smoothed.SizeY + " cells.");
            }
            if ((band.SizeX < 3) || (band.SizeY < 2))
            {
                throw new NotImplementedException("Special casing for small rasters is not currently implemented. Input raster is " + band.SizeX + " by " + band.SizeY + " cells.");
            }

            // main possibilities for no data handling: omit, interpolate neighbors, fill with lowest neighbor, fill from ground, fill from DTM 
            // If filling is guaranteed then all cells have data and calculations for kernels lying entirely within a raster can be
            // simplified. However, kernels which extend past a raster's edge are unavoidably subject to no data.
            Binomial.SmoothWithNoDataOmitted3x3(neighborhood, smoothed);
            // Gaussian.SmoothWithNoDataFilling3x3(band, smoothed); // currently excluded from build due to minimal testing
        }

        /// <remarks>
        /// Since Gaussian kernels top and bottom rows are the same, other rows are integer multiples of the top and bottom rows, and kernel 
        /// weights are constant when all cells have data, 2D convolution can be implemented in a single pass using 1D convolution and caching
        /// of calculations on previous.
        /// 
        /// Due to a 3x3 kernel's small size it is unclear if SIMD would be helpful in this case. While the multiply-add portion of 
        /// convolution benefits, data shuffling and two horizontal adds for SIMD calculation of each output value require more instructions 
        /// than a scalar 3x3 implementation. This does not appear to hold for 4x4 and larger kernel sizes.
        /// </remarks>
        //private static void SmoothWithNoDataFilling3x3(RasterBand<float> band, RasterBand<float> smoothed)
        //{
        //    if (band.HasNoDataValue)
        //    {
        //        throw new NotImplementedException("No data filling is not implemented");
        //    }

        //    // first input row
        //    float[] nextRow = new float[smoothed.XSize];
        //    float centerCellValue = band[0, 0];
        //    float previousCellValue = 0.0F;
        //    for (int xIndex = 0, xIndexNext = 1; xIndexNext < smoothed.XSize; ++xIndexNext, ++xIndex)
        //    {
        //        float nextCellValue = 0.0625F * band[xIndexNext, 0];
        //        float nextRowValue = previousCellValue + 2.0F * centerCellValue + nextCellValue; // recalculate to avoid numerical error accumulation
        //        nextRow[xIndex] = nextRowValue;

        //        previousCellValue = centerCellValue;
        //        centerCellValue = nextRowValue;
        //    }
        //    nextRow[^1] = previousCellValue + 2.0F * centerCellValue;

        //    // first output row: only center and next rows
        //    float[] centerRow = nextRow;
        //    nextRow = new float[smoothed.XSize];
        //    centerCellValue = band[0, 1];
        //    previousCellValue = 0.0F;
        //    for (int xIndex = 0, xIndexNext = 1; xIndexNext < smoothed.XSize; ++xIndexNext, ++xIndex)
        //    {
        //        float nextCellValue = 0.0625F * band[xIndexNext, 0];
        //        float nextRowValue = previousCellValue + 2.0F * centerCellValue + nextCellValue;
        //        nextRow[xIndex] = nextRowValue;

        //        float centerRowValue = centerRow[xIndex];
        //        smoothed[xIndex] = 2.0F * centerRowValue + nextRowValue;

        //        previousCellValue = centerCellValue;
        //        centerCellValue = nextCellValue;
        //    }
        //    nextRow[^1] = previousCellValue + 2.0F * centerCellValue;

        //    // all middle output rows: previous, center, and next
        //    float[] previousRow;
        //    float[] tempRow = new float[smoothed.XSize];
        //    int yIndex;
        //    for (int yIndexNext = 1; yIndexNext < band.YSize - 1; ++yIndexNext)
        //    {
        //        previousRow = centerRow;
        //        centerRow = nextRow;
        //        nextRow = tempRow;

        //        yIndex = yIndexNext - 1;
        //        for (int xIndex = 0, xIndexNext = 1; xIndexNext < smoothed.XSize; ++xIndexNext, ++xIndex)
        //        {
        //            float nextCellValue = 0.0625F * band[xIndexNext, 0];
        //            float nextRowValue = previousCellValue + 2.0F * centerCellValue + nextCellValue;
        //            nextRow[xIndex] = nextRowValue;

        //            float previousRowValue = previousRow[xIndex];
        //            float centerRowValue = centerRow[xIndex];
        //            smoothed[xIndex, yIndex] = previousRowValue + 2.0F * centerRowValue + nextRowValue;

        //            previousCellValue = centerCellValue;
        //            centerCellValue = nextCellValue;
        //        }

        //        tempRow = nextRow;
        //    }

        //    // last row: only previous and center rows
        //    previousRow = centerRow;
        //    centerRow = nextRow;
        //    yIndex = smoothed.YSize - 1;
        //    for (int xIndex = 0, xIndexNext = 1; xIndexNext < smoothed.XSize; ++xIndexNext, ++xIndex)
        //    {
        //        float previousRowValue = previousRow[xIndex];
        //        float centerRowValue = centerRow[xIndex];
        //        smoothed[xIndex, yIndex] = previousRowValue + 2.0F * centerRowValue;
        //    }
        //}

        // AVX implementation of masked convolution
        // Can be made more efficient once AVX10/256 is more broadly available.
        private static void SmoothWithNoDataOmitted3x3(RasterNeighborhood8<float> neighborhood, RasterBand<float> smoothed)
        {
            Debug.Assert(neighborhood.Center.Transform.CellHeight < 0.0F, "Currently only negative cell heights are supported as assumptions are made as to y indexing of neighboring north and south tiles.");
            RasterBand<float> band = neighborhood.Center;

            // Debug.Assert(band.HasNoDataValue && smoothed.HasNoDataValue); // potentially desirable for checking but not required
            float noDataValue = smoothed.NoDataValue;
            Vector128<float> kernelEdge = Vector128.Create(1.0F, 2.0F, 1.0F, 0.0F);
            Vector128<float> kernelCenter = Vector128.Create(2.0F, 4.0F, 2.0F, 0.0F);
            Vector128<sbyte> kernelWeights = Vector128.Create(1, 2, 1, 0, // defaulting to sbyte integrates with _mm_maddubs_epi16()
                                                              2, 4, 2, 0,
                                                              1, 2, 1, 0,
                                                              0, 0, 0, 0);

            // first row
            (byte currentRowNextMask, byte currentRowCenterMask) = Binomial.GatherInitial3(neighborhood, yIndexNext: 1, out Vector128<float> previousRowValues, out Vector128<float> currentRowValues, out Vector128<float> nextRowValues, out Vector128<byte> maskVector);
            if (neighborhood.North == null)
            {
                // first row: only center and next rows
                for (int xIndexNextNext = 2; xIndexNextNext < smoothed.SizeX; ++xIndexNextNext)
                {
                    // compute convolution at current position
                    int xIndex = xIndexNextNext - 2;
                    smoothed[xIndex, 0] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

                    // advance one cell in x
                    currentRowCenterMask = currentRowNextMask;

                    (float currentRowNextValue, currentRowNextMask) = band.GetValueMaskZero(xIndexNextNext, 0);
                    currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);
                    (float nextRowNextValue, byte nextRowNextMask) = band.GetValueMaskZero(xIndexNextNext, 1);
                    nextRowValues = AvxExtensions.ShuffleInAndUp(nextRowNextValue, nextRowValues);
                    maskVector = AvxExtensions.ShuffleInAndUp(0, currentRowNextMask, nextRowNextMask, maskVector);
                }
            }
            else
            {
                // first row: previous, center, next rows
                for (int xIndexNextNext = 2; xIndexNextNext < smoothed.SizeX; ++xIndexNextNext)
                {
                    // compute convolution at current position
                    int xIndex = xIndexNextNext - 2;
                    smoothed[xIndex, 0] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

                    // advance one cell in x
                    currentRowCenterMask = currentRowNextMask;

                    (float previousRowNextValue, byte previousRowNextMask) = neighborhood.GetValueMaskZero(xIndexNextNext, -1);
                    previousRowValues = AvxExtensions.ShuffleInAndUp(previousRowNextValue, previousRowValues);
                    (float currentRowNextValue, currentRowNextMask) = band.GetValueMaskZero(xIndexNextNext, 0);
                    currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);
                    (float nextRowNextValue, byte nextRowNextMask) = band.GetValueMaskZero(xIndexNextNext, 1);
                    nextRowValues = AvxExtensions.ShuffleInAndUp(nextRowNextValue, nextRowValues);
                    maskVector = AvxExtensions.ShuffleInAndUp(previousRowNextMask, currentRowNextMask, nextRowNextMask, maskVector);
                }
            }

            smoothed[smoothed.SizeX - 2, 0] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);
            (previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowCenterMask) = Binomial.AdvanceToLastCellInRow(neighborhood, yIndex: 0, previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowNextMask);
            smoothed[smoothed.SizeX - 1, 0] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

            // all middle output rows: previous, center, and next
            int yIndexPrevious;
            int yIndex;
            for (int yIndexNext = 2; yIndexNext < smoothed.SizeY; ++yIndexNext)
            {
                (currentRowNextMask, currentRowCenterMask) = Binomial.GatherInitial3(neighborhood, yIndexNext, out previousRowValues, out currentRowValues, out nextRowValues, out maskVector);

                yIndexPrevious = yIndexNext - 2;
                yIndex = yIndexNext - 1;
                for (int xIndexNextNext = 2; xIndexNextNext < smoothed.SizeX; ++xIndexNextNext)
                {
                    // compute convolution at current position
                    int xIndex = xIndexNextNext - 2;
                    smoothed[xIndex, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

                    // advance one cell in x
                    currentRowCenterMask = currentRowNextMask;

                    (float previousRowNextValue, byte previousRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndexPrevious);
                    previousRowValues = AvxExtensions.ShuffleInAndUp(previousRowNextValue, previousRowValues);
                    (float currentRowNextValue, currentRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndex);
                    currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);
                    (float nextRowNextValue, byte nextRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndexNext);
                    nextRowValues = AvxExtensions.ShuffleInAndUp(nextRowNextValue, nextRowValues);
                    maskVector = AvxExtensions.ShuffleInAndUp(previousRowNextMask, currentRowNextMask, nextRowNextMask, maskVector);
                }

                smoothed[smoothed.SizeX - 2, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);
                (previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowCenterMask) = Binomial.AdvanceToLastCellInRow(neighborhood, yIndex, previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowNextMask);
                smoothed[smoothed.SizeX - 1, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);
            }

            // last row
            yIndexPrevious = smoothed.SizeY - 2;
            yIndex = smoothed.SizeY - 1;
            (currentRowNextMask, currentRowCenterMask) = Binomial.GatherInitial3(neighborhood, smoothed.SizeY, out previousRowValues, out currentRowValues, out nextRowValues, out maskVector);
            if (neighborhood.South == null)
            {
                // last row: only previous and center rows
                for (int xIndexNextNext = 2; xIndexNextNext < smoothed.SizeX; ++xIndexNextNext)
                {
                    // compute convolution at current position
                    int xIndex = xIndexNextNext - 2;
                    smoothed[xIndex, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

                    // advance one cell in x
                    currentRowCenterMask = currentRowNextMask;

                    (float previousRowNextValue, byte previousRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndex - 1);
                    previousRowValues = AvxExtensions.ShuffleInAndUp(previousRowNextValue, previousRowValues);
                    (float currentRowNextValue, currentRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndex);
                    currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);
                    maskVector = AvxExtensions.ShuffleInAndUp(previousRowNextMask, currentRowNextMask, 0, maskVector);
                }
            }
            else
            {
                // last row: only previous, center, and next rows
                int yIndexNext = smoothed.SizeY + 1;
                for (int xIndexNextNext = 2; xIndexNextNext < smoothed.SizeX; ++xIndexNextNext)
                {
                    // compute convolution at current position
                    int xIndex = xIndexNextNext - 2;
                    smoothed[xIndex, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);

                    // advance one cell in x
                    currentRowCenterMask = currentRowNextMask;

                    (float previousRowNextValue, byte previousRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndexPrevious);
                    previousRowValues = AvxExtensions.ShuffleInAndUp(previousRowNextValue, previousRowValues);
                    (float currentRowNextValue, currentRowNextMask) = band.GetValueMaskZero(xIndexNextNext, yIndex);
                    currentRowValues = AvxExtensions.ShuffleInAndUp(currentRowNextValue, currentRowValues);
                    (float nextRowNextValue, byte nextRowNextMask) = neighborhood.GetValueMaskZero(xIndexNextNext, yIndexNext);
                    nextRowValues = AvxExtensions.ShuffleInAndUp(nextRowNextValue, nextRowValues);
                    maskVector = AvxExtensions.ShuffleInAndUp(previousRowNextMask, currentRowNextMask, nextRowNextMask, maskVector);
                }
            }

            smoothed[smoothed.SizeX - 2, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);
            (previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowCenterMask) = Binomial.AdvanceToLastCellInRow(neighborhood, yIndex, previousRowValues, currentRowValues, nextRowValues, maskVector, currentRowNextMask);
            smoothed[smoothed.SizeX - 1, yIndex] = currentRowCenterMask == 0 ? noDataValue : Binomial.Convolve3x3(kernelEdge, kernelCenter, kernelWeights, previousRowValues, currentRowValues, nextRowValues, maskVector);
        }
    }
}
