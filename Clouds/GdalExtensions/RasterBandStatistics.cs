using Mars.Clouds.Extensions;
using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mars.Clouds.GdalExtensions
{
    public class RasterBandStatistics
    {
        private double sum;
        private double sumOfSquares;

        public long CellsSampled { get; private set; }
        public bool IsApproximate { get; set; }
        public long NoDataCells { get; private set; }
        public int[] Histogram { get; private set; } // SIMD compatibility permits individual value counts up to max C# array size of 2³¹ elements
        public double HistogramBinWidth { get; private set; }
        public bool HistogramIncludesOutOfRange { get; private set; }
        public double HistogramMaximum { get; private set; }
        public double HistogramMinimum { get; private set; }
        public double Maximum { get; private set; }
        public double Mean { get; private set; }
        public double Minimum { get; private set; }
        public double StandardDeviation { get; private set; }

        public RasterBandStatistics()
        {
            this.sum = 0.0;
            this.sumOfSquares = 0.0;

            this.CellsSampled = 0;
            this.IsApproximate = false;
            this.NoDataCells = 0;
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
            this.Maximum = Double.MinValue;
            this.Mean = Double.NaN;
            this.Minimum = Double.MaxValue;
            this.StandardDeviation = Double.NaN;
        }

        public unsafe RasterBandStatistics(double[] bandData, bool hasNoData, double noDataValue)
        {
            Vector256<double> maximum256 = Vector256.Create(Double.MinValue);
            Vector256<double> minimum256 = Vector256.Create(Double.MaxValue);
            Vector256<double> sum256 = Vector256<double>.Zero;
            Vector256<double> sumOfSquares256 = Vector256<double>.Zero;
            Vector256<double> noData256 = Vector256.Create(noDataValue);
            bool noDataIsNaN = Double.IsNaN(noDataValue);

            const int stride = 256 / 64; // read 4 doubles
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (double* bandStart = &bandData[0])
            {
                double* bandEndAvx = bandStart + endIndexAvx;
                for (double* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<double> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<double> noDataMask = noDataIsNaN ? Avx.CompareUnordered(data, data) : Avx.CompareEqual(data, noData256);
                        maximum256 = Avx.BlendVariable(Avx.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx.BlendVariable(Avx.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx.BlendVariable(data, Vector256<double>.Zero, noDataMask);

                        int noDataFlags = Avx.MoveMask(noDataMask);
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx.Max(data, maximum256);
                        minimum256 = Avx.Min(data, minimum256);
                    }

                    sum256 = Avx.Add(data, sum256);
                    sumOfSquares256 = Fma.MultiplyAdd(data, data, sumOfSquares256);
                }
            }

            double minimum = AvxExtensions.HorizontalMin(minimum256);
            double maximum = AvxExtensions.HorizontalMax(maximum256);
            this.sum = AvxExtensions.HorizontalAdd(sum256);
            this.sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                double value = bandData[cellIndex];
                if (hasNoData && ((noDataIsNaN && Double.IsNaN(value)) || (value == noDataValue)))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(float[] bandData, bool hasNoData, float noDataValue, float histogramMinimumBinEdge = Single.NaN, float histogramMaximumBinEdge = Single.NaN, float histogramBinWidth = Single.NaN)
        {
            // sums are accumulated with doubles as a minor tradeoff of speed for accuracy
            Vector256<float> maximum256 = Vector256.Create(Single.MinValue);
            Vector256<float> minimum256 = Vector256.Create(Single.MaxValue);
            Vector256<double> sum256 = Vector256<double>.Zero;
            Vector256<double> sumOfSquares256 = Vector256<double>.Zero;
            Vector256<float> noData256 = Vector256.Create(noDataValue);
            bool noDataIsNaN = Single.IsNaN(noDataValue);

            bool hasHistogram = Single.IsNaN(histogramBinWidth) == false;
            if (hasHistogram)
            {
                if (histogramBinWidth <= 0.0F)
                {
                    throw new ArgumentOutOfRangeException(nameof(histogramBinWidth), $"Histogram bin width {histogramBinWidth} is zero or negative.");
                }
                if (histogramMinimumBinEdge >= histogramMaximumBinEdge)
                {
                    throw new ArgumentOutOfRangeException(nameof(histogramMaximumBinEdge), $"Minimum histogram bin edge {histogramMinimumBinEdge} is less than or equal to the maximum {histogramMaximumBinEdge}.");
                }

                int histogramBins = (int)((histogramMaximumBinEdge - histogramMinimumBinEdge) / histogramBinWidth + 0.5F);
                if (histogramBins > 1000 * 1000) // sanity upper bound, QGIS readily uses 250k buckets
                {
                    throw new NotSupportedException($"{histogramBins} bins is an unexpectedly large histogram.");
                }
                this.Histogram = new int[histogramBins]; // leave at default of zero
            }
            else
            {
                this.Histogram = [];
            }
            this.HistogramBinWidth = histogramBinWidth;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = histogramMaximumBinEdge;
            this.HistogramMinimum = histogramMinimumBinEdge;
            Vector256<float> histogramBinWidth256 = Vector256.Create(histogramBinWidth); // need to initialize, even if unused
            Vector256<float> histogramMininum256 = Vector256.Create(histogramMinimumBinEdge);

            const int stride = 256 / 32; // read 8 floats
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (float* bandStart = &bandData[0])
            {
                float* bandEndAvx = bandStart + endIndexAvx;
                for (float* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<float> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<float> noDataMask = noDataIsNaN ? Avx.CompareUnordered(data, data) : Avx.CompareEqual(data, noData256);
                        maximum256 = Avx.BlendVariable(Avx.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx.BlendVariable(Avx.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx.BlendVariable(data, Vector256<float>.Zero, noDataMask);

                        int noDataFlags = Avx.MoveMask(noDataMask);
                        if (hasHistogram)
                        {
                            Vector256<int> histogramIndex256 = Avx.ConvertToVector256Int32(Avx.Floor(Avx.Divide(Avx.Subtract(data, histogramMininum256), histogramBinWidth256)));
                            AvxExtensions.HistogramIncrement(this.Histogram, histogramIndex256, noDataFlags);
                        }

                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx.Max(data, maximum256);
                        minimum256 = Avx.Min(data, minimum256);

                        if (hasHistogram)
                        {
                            Vector256<int> histogramIndex256 = Avx.ConvertToVector256Int32(Avx.Floor(Avx.Divide(Avx.Subtract(data, histogramMininum256), histogramBinWidth256)));
                            AvxExtensions.HistogramIncrement(this.Histogram, histogramIndex256);
                        }
                    }

                    Vector256<double> lowerData = Avx.ConvertToVector256Double(data.GetLower());
                    Vector256<double> upperData = Avx.ConvertToVector256Double(data.GetUpper());
                    sum256 = Avx.Add(lowerData, sum256);
                    sum256 = Avx.Add(upperData, sum256);

                    sumOfSquares256 = Fma.MultiplyAdd(lowerData, lowerData, sumOfSquares256);
                    sumOfSquares256 = Fma.MultiplyAdd(upperData, upperData, sumOfSquares256);
                }
            }

            float minimum = AvxExtensions.HorizontalMin(minimum256);
            float maximum = AvxExtensions.HorizontalMax(maximum256);
            this.sum = (double)AvxExtensions.HorizontalAdd(sum256);
            this.sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                float value = bandData[cellIndex];
                if (hasNoData && ((noDataIsNaN && Single.IsNaN(value)) || (value == noDataValue)))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;

                    if (hasHistogram)
                    {
                        int histogramIndex = (int)((value - histogramMinimumBinEdge) / histogramBinWidth + 0.5F);
                        ++this.Histogram[histogramIndex];
                    }
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);
        }

        public unsafe RasterBandStatistics(sbyte[] bandData, bool hasNoData, sbyte noDataValue)
        {
            Vector256<sbyte> maximum256 = Vector256.Create(SByte.MinValue);
            Vector256<sbyte> minimum256 = Vector256.Create(SByte.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<Int64> sumOfSquares256int64 = Vector256<Int64>.Zero;
            Vector256<sbyte> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 8; // read 32 bytes
            int endIndexAvx = stride * (bandData.Length / stride); // assume band is large enough AVX is useful
            fixed (sbyte* bandStart = &bandData[0])
            {
                sbyte* bandEndAvx = bandStart + endIndexAvx;
                int termsInIntermediateSums = 0;
                Vector256<Int16> sumInt16hextet0 = Vector256<Int16>.Zero;
                Vector256<Int16> sumInt16hextet1 = Vector256<Int16>.Zero;
                Vector256<Int32> sumOfSquaresInt32 = Vector256<Int32>.Zero;
                for (sbyte* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<sbyte> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<sbyte> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<sbyte>.Zero, noDataMask);

                        // number no data values is the number of bits set in the packed mask
                        // https://stackoverflow.com/questions/12171584/what-is-the-fastest-way-to-count-set-bits-in-uint32
                        int noDataFlags = Avx2.MoveMask(noDataMask);
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    Vector256<Int16> dataInt16hextet0 = Avx2.ConvertToVector256Int16(data.GetLower());
                    sumInt16hextet0 = Avx2.Add(dataInt16hextet0, sumInt16hextet0);
                    sumOfSquaresInt32 = Avx2.Add(Avx2.MultiplyAddAdjacent(dataInt16hextet0, dataInt16hextet0), sumOfSquaresInt32);

                    Vector256<Int16> dataInt16hextet1 = Avx2.ConvertToVector256Int16(data.GetUpper());
                    sumInt16hextet1 = Avx2.Add(dataInt16hextet1, sumInt16hextet1);
                    sumOfSquaresInt32 = Avx2.Add(Avx2.MultiplyAddAdjacent(dataInt16hextet1, dataInt16hextet1), sumOfSquaresInt32);

                    // clear 16 bit accumulators every 128 * 32 = 4096 cells
                    // 16 signed bits permit 2^7 = 128 8 bit signed additions before integer rollover is possible.
                    ++termsInIntermediateSums;
                    if (termsInIntermediateSums == 122) // allow for horizontal adds in accumulate
                    {
                        // accumulate
                        sum256int64 = AvxExtensions.Accumulate(sumInt16hextet0, sumInt16hextet1, sum256int64);
                        sumOfSquares256int64 = AvxExtensions.Accumulate(sumOfSquaresInt32, sumOfSquares256int64);

                        // reset accumulators
                        termsInIntermediateSums = 0;
                        sumInt16hextet0 = Vector256<Int16>.Zero;
                        sumInt16hextet1 = Vector256<Int16>.Zero;
                        sumOfSquaresInt32 = Vector256<Int32>.Zero;
                    }
                }

                if (termsInIntermediateSums > 0)
                {
                    sum256int64 = AvxExtensions.Accumulate(sumInt16hextet0, sumInt16hextet1, sum256int64);
                    sumOfSquares256int64 = AvxExtensions.Accumulate(sumOfSquaresInt32, sumOfSquares256int64);
                }
            }

            sbyte minimum = AvxExtensions.HorizontalMin(minimum256);
            sbyte maximum = AvxExtensions.HorizontalMax(maximum256);
            Int64 sum = AvxExtensions.HorizontalAdd(sum256int64);
            Int64 sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256int64);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                sbyte value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                    sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.sum = sum;
                this.sumOfSquares = sumOfSquares;

                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(Int16[] bandData, bool hasNoData, Int16 noDataValue)
        {
            Vector256<Int16> maximum256 = Vector256.Create(Int16.MinValue);
            Vector256<Int16> minimum256 = Vector256.Create(Int16.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<Int64> sumOfSquares256int64 = Vector256<Int64>.Zero;
            Vector256<Int16> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 16; // read 16 Int16s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (Int16* bandStart = &bandData[0])
            {
                Int16* bandEndAvx = bandStart + endIndexAvx;
                int termsInIntermediateSums = 0;
                Vector256<Int32> sumInt32 = Vector256<Int32>.Zero;
                for (Int16* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<Int16> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<Int16> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<Int16>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask.AsSByte()) & 0x55555555; // reinterpret as byte can produce up to 16 extra ones
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    Vector256<Int32> dataInt32octet0 = Avx2.ConvertToVector256Int32(data.GetLower());
                    sumInt32 = Avx2.Add(dataInt32octet0, sumInt32);

                    Vector256<Int32> dataInt32octet1 = Avx2.ConvertToVector256Int32(data.GetUpper());
                    sumInt32 = Avx2.Add(dataInt32octet1, sumInt32);

                    Vector256<Int32> sumOfSquares256 = Avx2.MultiplyAddAdjacent(data, data);
                    sumOfSquares256int64 = Avx2.Add(Avx2.ConvertToVector256Int64(sumOfSquares256.GetLower()), sumOfSquares256int64);
                    sumOfSquares256int64 = Avx2.Add(Avx2.ConvertToVector256Int64(sumOfSquares256.GetUpper()), sumOfSquares256int64);

                    // clear 32 bit accumulators every 32768 * 16 = 524288 cells
                    // 32 signed bits permit 2^15 = 128 16 bit signed additions before integer rollover is possible.
                    ++termsInIntermediateSums;
                    if (termsInIntermediateSums == 32764) // allow for horizontal adds in accumulate
                    {
                        // accumulate
                        sum256int64 = AvxExtensions.Accumulate(sumInt32, sum256int64);

                        // reset accumulators
                        termsInIntermediateSums = 0;
                        sumInt32 = Vector256<Int32>.Zero;
                    }
                }

                if (termsInIntermediateSums > 0)
                {
                    sum256int64 = AvxExtensions.Accumulate(sumInt32, sum256int64);
                }
            }

            Int16 minimum = AvxExtensions.HorizontalMin(minimum256);
            Int16 maximum = AvxExtensions.HorizontalMax(maximum256);
            Int64 sum = AvxExtensions.HorizontalAdd(sum256int64);
            Int64 sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256int64);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                Int16 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                    sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.sum = sum;
                this.sumOfSquares = sumOfSquares;

                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(Int32[] bandData, bool hasNoData, Int32 noDataValue)
        {
            // sums are accumulated as 64 bit
            // 64 bit accumulator does not need clearing. 2^31 32 bit sums fit in a 64 bit value and C#'s maximum array length is 2^31
            // elements. Since 256 bit SIMD is used, each element of the accumulator receives at most 2^29 additions and the horizontal
            // add to find the total sum reaches at most 2^31.
            // 
            // sums of squares are accumulated with doubles
            // Any 64 bit integer addition of the product of two 32 bit integers could cause overflow.
            Vector256<Int32> maximum256 = Vector256.Create(Int32.MinValue);
            Vector256<Int32> minimum256 = Vector256.Create(Int32.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<double> sumOfSquares256 = Vector256<double>.Zero;
            Vector256<Int32> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 32; // read 8 Int32s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (Int32* bandStart = &bandData[0])
            {
                Int32* bandEndAvx = bandStart + endIndexAvx;
                for (Int32* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<Int32> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<Int32> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<Int32>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask.AsSByte()) & 0x11111111; // reinterpret as byte can produce up to 24 extra ones
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    sum256int64 = AvxExtensions.Accumulate(data, sum256int64);

                    // _mm256_cvtepi64_pd() is in AVX-512VL
                    Vector256<double> lowerData = Avx.ConvertToVector256Double(data.GetLower());
                    Vector256<double> upperData = Avx.ConvertToVector256Double(data.GetUpper());
                    sumOfSquares256 = Avx2.Add(Avx2.Multiply(lowerData, lowerData), sumOfSquares256);
                    sumOfSquares256 = Avx2.Add(Avx2.Multiply(upperData, upperData), sumOfSquares256);
                }
            }

            Int32 minimum = AvxExtensions.HorizontalMin(minimum256);
            Int32 maximum = AvxExtensions.HorizontalMax(maximum256);
            this.sum = (double)AvxExtensions.HorizontalAdd(sum256int64);
            this.sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                Int32 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(Int64[] bandData, bool hasNoData, Int64 noDataValue)
        {
            // sums are accumulated as doubles as any 64 bit integer addition could overflow
            Vector256<Int64> maximum256 = Vector256.Create(Int64.MinValue);
            Vector256<Int64> minimum256 = Vector256.Create(Int64.MaxValue);
            Vector256<Int64> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 64; // read 4 Int64s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (Int64* bandStart = &bandData[0])
            {
                Int64* bandEndAvx = bandStart + endIndexAvx;
                for (Int64* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<Int64> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<Int64> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(AvxExtensions.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(AvxExtensions.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<Int64>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask.AsSByte()) & 0x01010101; // reinterpret as byte can produce up to 28 extra ones
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = AvxExtensions.Max(data, maximum256);
                        minimum256 = AvxExtensions.Min(data, minimum256);
                    }

                    // _mm256_cvtepi64_pd() is in AVX-512VL
                    double data0 = data.ToScalar();
                    double data1 = data[1];
                    Vector128<Int64> dataUpper = data.GetUpper();
                    double data2 = dataUpper.ToScalar();
                    double data3 = dataUpper[1];
                    this.sum += data0 + data1 + data2 + data3;
                    this.sumOfSquares += data0 * data0 + data1 * data1 + data2 * data2 + data3 * data3;
                }
            }

            Int64 minimum = AvxExtensions.HorizontalMin(minimum256);
            Int64 maximum = AvxExtensions.HorizontalMax(maximum256);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                Int64 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(byte[] bandData, bool hasNoData, byte noDataValue)
        {
            Vector256<byte> maximum256 = Vector256.Create(Byte.MinValue);
            Vector256<byte> minimum256 = Vector256.Create(Byte.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<Int64> sumOfSquares256int64 = Vector256<Int64>.Zero;
            Vector256<byte> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 8; // read 32 bytes
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (byte* bandStart = &bandData[0])
            {
                byte* bandEndAvx = bandStart + endIndexAvx;
                int termsInIntermediateSums = 0;
                Vector256<Int16> sumInt16hextet0 = Vector256<Int16>.Zero;
                Vector256<Int16> sumInt16hextet1 = Vector256<Int16>.Zero;
                Vector256<Int32> sumOfSquaresInt32 = Vector256<Int32>.Zero;
                for (byte* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<byte> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<byte> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<byte>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask); 
                        while (noDataFlags != 0) 
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    Vector256<Int16> dataInt16hextet0 = Avx2.ConvertToVector256Int16(data.GetLower());
                    sumInt16hextet0 = Avx2.Add(dataInt16hextet0, sumInt16hextet0);
                    sumOfSquaresInt32 = Avx2.Add(Avx2.MultiplyAddAdjacent(dataInt16hextet0, dataInt16hextet0), sumOfSquaresInt32);

                    Vector256<Int16> dataInt16hextet1 = Avx2.ConvertToVector256Int16(data.GetUpper());
                    sumInt16hextet1 = Avx2.Add(dataInt16hextet1, sumInt16hextet1);
                    sumOfSquaresInt32 = Avx2.Add(Avx2.MultiplyAddAdjacent(dataInt16hextet1, dataInt16hextet1), sumOfSquaresInt32);

                    // clear 16 bit accumulators every 128 * 32 = 4096 cells
                    // 16 signed bits permit 2^7 = 128 8 bit unsigned additions before integer rollover is possible.
                    ++termsInIntermediateSums;
                    if (termsInIntermediateSums == 122) // allow for horizontal adds in accumulate
                    {
                        // accumulate
                        sum256int64 = AvxExtensions.Accumulate(sumInt16hextet0, sumInt16hextet1, sum256int64);
                        sumOfSquares256int64 = AvxExtensions.Accumulate(sumOfSquaresInt32, sumOfSquares256int64);

                        // reset accumulators
                        termsInIntermediateSums = 0;
                        sumInt16hextet0 = Vector256<Int16>.Zero;
                        sumInt16hextet1 = Vector256<Int16>.Zero;
                        sumOfSquaresInt32 = Vector256<Int32>.Zero;
                    }
                }

                if (termsInIntermediateSums > 0)
                {
                    sum256int64 = AvxExtensions.Accumulate(sumInt16hextet0, sumInt16hextet1, sum256int64);
                    sumOfSquares256int64 = AvxExtensions.Accumulate(sumOfSquaresInt32, sumOfSquares256int64);
                }
            }

            byte minimum = AvxExtensions.HorizontalMin(minimum256);
            byte maximum = AvxExtensions.HorizontalMax(maximum256);
            Int64 sum = AvxExtensions.HorizontalAdd(sum256int64);
            Int64 sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256int64);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                byte value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                    sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.sum = sum;
                this.sumOfSquares = sumOfSquares;

                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(UInt16[] bandData, bool hasNoData, UInt16 noDataValue)
        {
            Vector256<UInt16> maximum256 = Vector256.Create(UInt16.MinValue);
            Vector256<UInt16> minimum256 = Vector256.Create(UInt16.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<Int64> sumOfSquares256int64 = Vector256<Int64>.Zero;
            Vector256<UInt16> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 16; // read 16 UInt16s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (UInt16* bandStart = &bandData[0])
            {
                UInt16* bandEndAvx = bandStart + endIndexAvx;
                int termsInIntermediateSums = 0;
                Vector256<Int32> sumInt32 = Vector256<Int32>.Zero;
                for (UInt16* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<UInt16> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<UInt16> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<UInt16>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask.AsByte()) & 0x55555555; // reinterpret as byte can produce up to 16 extra ones
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    Vector256<Int32> dataInt32octet0 = Avx2.ConvertToVector256Int32(data.GetLower());
                    sumInt32 = Avx2.Add(dataInt32octet0, sumInt32);

                    Vector256<Int32> dataInt32octet0shift64 = Avx2.Shuffle(dataInt32octet0, Constant.Simd128.Circular32Up1); // _mm256_mul_epi32() multiplies low integers in packed 64 bits
                    sumOfSquares256int64 = Avx2.Add(Avx2.Multiply(dataInt32octet0, dataInt32octet0), sumOfSquares256int64);
                    sumOfSquares256int64 = Avx2.Add(Avx2.Multiply(dataInt32octet0shift64, dataInt32octet0shift64), sumOfSquares256int64);

                    Vector256<Int32> dataInt32octet1 = Avx2.ConvertToVector256Int32(data.GetUpper());
                    sumInt32 = Avx2.Add(dataInt32octet1, sumInt32);

                    Vector256<Int32> dataInt32octet1shift32 = Avx2.Shuffle(dataInt32octet1, Constant.Simd128.Circular32Up1);
                    sumOfSquares256int64 = Avx2.Add(Avx2.Multiply(dataInt32octet1, dataInt32octet1), sumOfSquares256int64);
                    sumOfSquares256int64 = Avx2.Add(Avx2.Multiply(dataInt32octet1shift32, dataInt32octet1shift32), sumOfSquares256int64);

                    // clear 16 bit accumulators every 128 * 32 = 4096 cells
                    // 32 signed bits permit 2^15 = 128 16 bit unsigned additions before integer rollover is possible.
                    ++termsInIntermediateSums;
                    if (termsInIntermediateSums == 32764) // allow for horizontal adds in accumulate
                    {
                        // accumulate
                        sum256int64 = AvxExtensions.Accumulate(sumInt32, sum256int64);

                        // reset accumulators
                        termsInIntermediateSums = 0;
                        sumInt32 = Vector256<Int32>.Zero;
                    }
                }

                if (termsInIntermediateSums > 0)
                {
                    sum256int64 = AvxExtensions.Accumulate(sumInt32, sum256int64);
                }
            }

            UInt16 minimum = AvxExtensions.HorizontalMin(minimum256);
            UInt16 maximum = AvxExtensions.HorizontalMax(maximum256);
            Int64 sum = AvxExtensions.HorizontalAdd(sum256int64);
            Int64 sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256int64);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                UInt16 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                    sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.sum = sum;
                this.sumOfSquares = sumOfSquares;

                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(UInt32[] bandData, bool hasNoData, UInt32 noDataValue)
        {
            // sums are accumulated as 64 bit
            // 64 bit accumulator does not need clearing. 2^31 32 bit sums fit in a 64 bit value and C#'s maximum array length is 2^31
            // elements. Since 256 bit SIMD is used, each element of the accumulator receives at most 2^29 additions and the horizontal
            // add to find the total sum reaches at most 2^31.
            // 
            // sums of squares are accumulated with doubles
            // Any 64 bit integer addition of the product of two 32 bit integers could cause overflow.
            Vector256<UInt32> maximum256 = Vector256.Create(UInt32.MinValue);
            Vector256<UInt32> minimum256 = Vector256.Create(UInt32.MaxValue);
            Vector256<Int64> sum256int64 = Vector256<Int64>.Zero;
            Vector256<double> sumOfSquares256 = Vector256<double>.Zero;
            Vector256<UInt32> noData256 = Vector256.Create(noDataValue);

            const int stride = 256 / 32; // read 8 UInt32s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (UInt32* bandStart = &bandData[0])
            {
                UInt32* bandEndAvx = bandStart + endIndexAvx;
                for (UInt32* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<UInt32> data = Avx.LoadVector256(bandAddress);
                    if (hasNoData)
                    {
                        Vector256<UInt32> noDataMask = Avx2.CompareEqual(data, noData256);
                        maximum256 = Avx2.BlendVariable(Avx2.Max(data, maximum256), maximum256, noDataMask);
                        minimum256 = Avx2.BlendVariable(Avx2.Min(data, minimum256), minimum256, noDataMask);
                        data = Avx2.BlendVariable(data, Vector256<UInt32>.Zero, noDataMask);

                        int noDataFlags = Avx2.MoveMask(noDataMask.AsByte()) & 0x11111111; // reinterpret as byte can produce up to 24 extra ones
                        while (noDataFlags != 0)
                        {
                            ++this.NoDataCells;
                            noDataFlags &= noDataFlags - 1;
                        }
                    }
                    else
                    {
                        maximum256 = Avx2.Max(data, maximum256);
                        minimum256 = Avx2.Min(data, minimum256);
                    }

                    sum256int64 = AvxExtensions.Accumulate(data, sum256int64);

                    // _mm256_cvtepu32_pd() is in AVX-512VL
                    // Speed-accuracy tradeoff: could accumulate in eight floats rather than four doubles but double accumulation is
                    // used for consistency with float and Int32 overloads.
                    Vector256<float> dataAsFloat = data.ToFloat();
                    Vector256<double> lowerData = Avx.ConvertToVector256Double(dataAsFloat.GetLower());
                    Vector256<double> upperData = Avx.ConvertToVector256Double(dataAsFloat.GetUpper());
                    sumOfSquares256 = Avx2.Add(Avx2.Multiply(lowerData, lowerData), sumOfSquares256);
                    sumOfSquares256 = Avx2.Add(Avx2.Multiply(upperData, upperData), sumOfSquares256);
                }
            }

            UInt32 minimum = AvxExtensions.HorizontalMin(minimum256);
            UInt32 maximum = AvxExtensions.HorizontalMax(maximum256);
            this.sum = AvxExtensions.HorizontalAdd(sum256int64);
            this.sumOfSquares = AvxExtensions.HorizontalAdd(sumOfSquares256);

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                UInt32 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public unsafe RasterBandStatistics(UInt64[] bandData, bool hasNoData, UInt64 noDataValue)
        {
            // mostly scalar due to lack of AVX2 epu64 support (added in AVX-512VL)
            UInt64 maximum = UInt64.MinValue;
            UInt64 minimum = UInt64.MaxValue;

            const int stride = 256 / 64; // read 4 UInt64s
            int endIndexAvx = stride * (bandData.Length / stride);
            fixed (UInt64* bandStart = &bandData[0])
            {
                UInt64* bandEndAvx = bandStart + endIndexAvx;
                for (UInt64* bandAddress = bandStart; bandAddress < bandEndAvx; bandAddress += stride)
                {
                    Vector256<UInt64> data = Avx.LoadVector256(bandAddress);
                    double sum = 0.0;
                    double sumOfSquares = 0.0;

                    UInt64 data0 = data.ToScalar();
                    if (hasNoData && (data0 == noDataValue))
                    {
                        ++this.NoDataCells;
                    }
                    else
                    {
                        if (data0 > maximum)
                        {
                            maximum = data0;
                        }
                        if (data0 < minimum)
                        {
                            minimum = data0;
                        }

                        double data0asDouble = data0;
                        sum = data0asDouble;
                        sumOfSquares = data0asDouble * data0asDouble;
                    }

                    UInt64 data1 = data[1];
                    if (hasNoData && (data1 == noDataValue))
                    {
                        ++this.NoDataCells;
                    }
                    else
                    {
                        if (data1 > maximum)
                        {
                            maximum = data1;
                        }
                        if (data1 < minimum)
                        {
                            minimum = data1;
                        }

                        double data1asDouble = data1;
                        sum += data1asDouble;
                        sumOfSquares += data1asDouble * data1asDouble;
                    }

                    Vector128<UInt64> dataUpper = data.GetUpper();
                    UInt64 data2 = dataUpper.ToScalar();
                    if (hasNoData && (data2 == noDataValue))
                    {
                        ++this.NoDataCells;
                    }
                    else
                    {
                        if (data2 > maximum)
                        {
                            maximum = data2;
                        }
                        if (data2 < minimum)
                        {
                            minimum = data2;
                        }

                        double data2asDouble = data2;
                        sum += data2asDouble;
                        sumOfSquares += data2asDouble * data2asDouble;
                    }

                    UInt64 data3 = dataUpper[1];
                    if (hasNoData && (data3 == noDataValue))
                    {
                        ++this.NoDataCells;
                    }
                    else
                    {
                        if (data3 > maximum)
                        {
                            maximum = data3;
                        }
                        if (data3 < minimum)
                        {
                            minimum = data3;
                        }

                        double data3asDouble = data3;
                        sum += data3asDouble;
                        sumOfSquares += data3asDouble * data3asDouble;
                    }

                    this.sum += sum;
                    this.sumOfSquares += sumOfSquares;
                }
            }

            for (int cellIndex = endIndexAvx; cellIndex < bandData.Length; ++cellIndex)
            {
                UInt64 value = bandData[cellIndex];
                if (hasNoData && (value == noDataValue))
                {
                    ++this.NoDataCells;
                }
                else
                {
                    if (value < minimum)
                    {
                        minimum = value;
                    }
                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    this.sum += value;
                    this.sumOfSquares += value * value;
                }
            }

            // assume all band data passed; leave this.IsApproximate as false
            this.CellsSampled = (long)bandData.Length;
            long cellsWithData = this.CellsSampled - this.NoDataCells;
            if (cellsWithData > 0)
            {
                this.Minimum = (double)minimum;
                this.Maximum = (double)maximum;
            }
            this.SetMeanAndStandardDeviation(cellsWithData);

            // histogram is empty as it was not specified
            this.Histogram = [];
            this.HistogramBinWidth = Double.NaN;
            this.HistogramIncludesOutOfRange = false;
            this.HistogramMaximum = Double.NaN;
            this.HistogramMinimum = Double.NaN;
        }

        public bool HasHistogram
        {
            get { return this.Histogram.Length > 0; }
        }

        public void Add(RasterBandStatistics other)
        {
            this.sum += other.sum;
            this.sumOfSquares += other.sumOfSquares;

            this.CellsSampled += other.CellsSampled;
            this.NoDataCells += other.NoDataCells;
            
            long otherCellsWithData = other.CellsSampled - other.NoDataCells;
            if (otherCellsWithData > 0)
            {
                if (other.Minimum < this.Minimum)
                {
                    this.Minimum = other.Minimum;
                }
                if (other.Maximum > this.Maximum)
                {
                    this.Maximum = other.Maximum;
                }
            }

            // invalidate any exsting mean and standard deviation until OnAdditionComplete() is called
            this.Mean = Double.NaN;
            this.StandardDeviation = Double.NaN;

            if (this.HasHistogram)
            {
                // accumulate histogram counts
                // This can easily be relaxed to support merging of aligned histograms. Resampling of unalinged histograms is more difficult.
                if ((this.HistogramBinWidth != other.HistogramBinWidth) || (this.HistogramMinimum != other.HistogramMinimum) || (this.HistogramMaximum != other.HistogramMaximum) || (this.Histogram.Length != other.Histogram.Length))
                {
                    throw new NotSupportedException($"Histograms are mismatched. Bin width of {this.HistogramBinWidth} versus {other.HistogramBinWidth}, minumum {this.HistogramMinimum} versus {other.HistogramMinimum}, maximum {this.HistogramMaximum} versus {other.HistogramMaximum}, and {this.Histogram.Length} versus {other.Histogram.Length} bins.");
                }

                AvxExtensions.Accumulate(other.Histogram, this.Histogram);
                this.HistogramIncludesOutOfRange |= other.HistogramIncludesOutOfRange;
            }
            else
            {
                this.Histogram = new int[other.Histogram.Length];
                Array.Copy(other.Histogram, this.Histogram, other.Histogram.Length);
                this.HistogramBinWidth = other.HistogramBinWidth;
                this.HistogramIncludesOutOfRange = other.HistogramIncludesOutOfRange;
                this.HistogramMaximum = other.HistogramMaximum;
                this.HistogramMinimum = other.HistogramMinimum;
            }
        }

        public double GetDataFraction()
        {
            return (double)(this.CellsSampled - this.NoDataCells) / (double)this.CellsSampled;
        }

        public void OnAdditionComplete()
        {
            this.SetMeanAndStandardDeviation(this.CellsSampled - this.NoDataCells);
        }

        private void SetMeanAndStandardDeviation(long cellsWithData)
        {
            if (cellsWithData > 0)
            {
                double cellsWithDataAsDouble = cellsWithData;
                this.Mean = this.sum / cellsWithDataAsDouble;
                this.StandardDeviation = Double.Sqrt(this.sumOfSquares / cellsWithDataAsDouble - this.Mean * this.Mean);
            }
            else
            {
                // invalidate any exsting mean and standard deviation
                this.Mean = Double.NaN;
                this.StandardDeviation = Double.NaN;
            }
        }
    }
}
