using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mars.Clouds.Extensions
{
    internal static class AvxExtensions
    {
        /// <summary>
        /// Element by element addition of <paramref name="vector"/> to <paramref name="sum"/>.
        /// </summary>
        public unsafe static void Accumulate(ReadOnlySpan<Int32> vector, Span<Int32> sum)
        {
            Debug.Assert(vector.Length == sum.Length);

            // see https://devblogs.microsoft.com/dotnet/hardware-intrinsics-in-net-core/ for basic profiling
            // Unrolled for potentially 2 * (64 bytes/cycle read + 32 bytes/cycle store) = 128 + 64 bytes/cycle.
            // Zen 3 and 4 cores: 3 256 bit integer loads + 2 stores/cycle = 96 + 64 bytes/cycle
            // Zen 5 cores: 2 512 bit loads + 2 256 bit stores/cycle = 128 + 64 bytes/cycle
            const int stride = 256 / 32;
            const int loopStride = 2 * stride;
            int vectorEndIndexAvx = loopStride * (vector.Length / loopStride);
            fixed (Int32* vectorStart = &vector[0])
            fixed (Int32* sumStart = &sum[0])
            {
                Int32* sumAddress = sumStart;
                Int32* vectorEndAvx = vectorStart + vectorEndIndexAvx;
                for (Int32* vectorAddress = vectorStart; vectorAddress < vectorEndAvx; sumAddress += stride, vectorAddress += stride)
                {
                    Vector256<Int32> sum256lower = Avx2.LoadVector256(sumAddress);
                    Vector256<Int32> value256lower = Avx2.LoadVector256(vectorAddress);
                    sum256lower = Avx2.Add(value256lower, sum256lower);
                    Avx2.Store(sumAddress, sum256lower);

                    sumAddress += stride;
                    vectorAddress += stride;

                    Vector256<Int32> sum256upper = Avx2.LoadVector256(sumAddress);
                    Vector256<Int32> value256upper = Avx2.LoadVector256(vectorAddress);
                    sum256upper = Avx2.Add(value256upper, sum256upper);
                    Avx2.Store(sumAddress, sum256upper);
                }
            }
            for (int scalarIndex = vectorEndIndexAvx; scalarIndex < vector.Length; ++scalarIndex)
            {
                sum[scalarIndex] += vector[scalarIndex];
            }
        }

        /// <summary>
        /// Expand 16 bit signed values to 64 bit signed and accumulate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<Int64> Accumulate(Vector256<Int16> hextet0, Vector256<Int16> hextet1, Vector256<Int64> sum)
        {
            Vector256<Int32> sumInt32octet0 = Avx2.ConvertToVector256Int32(hextet0.GetLower());
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet0.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet0.GetUpper()), sum);
            Vector256<Int32> sumInt32octet1 = Avx2.ConvertToVector256Int32(hextet0.GetUpper());
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet1.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet1.GetUpper()), sum);

            Vector256<Int32> sumInt32octet2 = Avx2.ConvertToVector256Int32(hextet1.GetLower());
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet2.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet2.GetUpper()), sum);
            Vector256<Int32> sumInt32octet3 = Avx2.ConvertToVector256Int32(hextet1.GetUpper());
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet3.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(sumInt32octet3.GetUpper()), sum);

            return sum;
        }

        /// <summary>
        /// Expand 32 bit signed values to 64 bit signed and accumulate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<Int64> Accumulate(Vector256<Int32> octet, Vector256<Int64> sum)
        {
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(octet.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(octet.GetUpper()), sum);
            return sum;
        }

        /// <summary>
        /// Expand 32 bit unsigned values to 64 bit signed and accumulate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<Int64> Accumulate(Vector256<UInt32> octet, Vector256<Int64> sum)
        {
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(octet.GetLower()), sum);
            sum = Avx2.Add(Avx2.ConvertToVector256Int64(octet.GetUpper()), sum);
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> BroadcastScalarToVector256(float value)
        {
            Vector128<float> value128 = Vector128.CreateScalarUnsafe(value);
            return Avx2.BroadcastScalarToVector256(value128);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static Vector256<int> BroadcastScalarToVector256(int value)
        //{
        //    Vector128<int> value128 = Vector128.CreateScalarUnsafe(value);
        //    return Avx2.BroadcastScalarToVector256(value128);
        //}

        public static unsafe void Convert(ReadOnlySpan<sbyte> source, Span<Int16> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 16; // read 16 bytes, convert to 16 UInt16s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (sbyte* sourceStart = &source[0])
            fixed (Int16* destinationStart = &destination[0])
            {
                Int16* destinationAddress = destinationStart;
                sbyte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (sbyte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int16> hextet = Avx2.ConvertToVector256Int16(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<sbyte> source, Span<Int32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 bytes, convert to 8 UInt32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (sbyte* sourceStart = &source[0])
            fixed (Int32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = destinationStart;
                sbyte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (sbyte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<sbyte> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 bytes, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (sbyte* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                sbyte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (sbyte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<Int16> source, Span<Int32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 Int16s, convert to 8 UInt32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int16* sourceStart = &source[0])
            fixed (Int32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = destinationStart;
                Int16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (Int16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<Int16> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 Int16s, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int16* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                Int16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (Int16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<Int32> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 Int32s, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int32* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                Int32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (Int32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<Int16> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 16; // read 16 bytes, convert to 16 Int16s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (Int16* destinationStart = &destination[0])
            {
                Int16* destinationAddress = destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int16> hextet = Avx2.ConvertToVector256Int16(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<Int32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 bytes, convert to 8 Int32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (Int32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 bytes, convert to 4 Int64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<UInt16> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 16; // read 16 bytes, convert to 16 UInt16s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (UInt16* destinationStart = &destination[0])
            {
                Int16* destinationAddress = (Int16*)destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int16> hextet = Avx2.ConvertToVector256Int16(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<UInt32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 bytes, convert to 8 UInt32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (UInt32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = (Int32*)destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<byte> source, Span<UInt64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 bytes, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (byte* sourceStart = &source[0])
            fixed (UInt64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = (Int64*)destinationStart;
                byte* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (byte* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt16> source, Span<Int32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 UInt16s, convert to 8 Int32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt16* sourceStart = &source[0])
            fixed (Int32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = destinationStart;
                UInt16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt16> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 UInt16s, convert to 4 Int64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt16* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                UInt16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt16> source, Span<UInt32> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 UInt16s, convert to 8 UInt32s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt16* sourceStart = &source[0])
            fixed (UInt32* destinationStart = &destination[0])
            {
                Int32* destinationAddress = (Int32*)destinationStart;
                UInt16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int32> hextet = Avx2.ConvertToVector256Int32(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt16> source, Span<UInt64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 UInt16s, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt16* sourceStart = &source[0])
            fixed (UInt64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = (Int64*)destinationStart;
                UInt16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt32> source, Span<Int64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 UInt32s, convert to 4 Int64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt32* sourceStart = &source[0])
            fixed (Int64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = destinationStart;
                UInt32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        public static unsafe void Convert(ReadOnlySpan<UInt32> source, Span<UInt64> destination)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 64; // read 4 UInt32s, convert to 4 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt32* sourceStart = &source[0])
            fixed (UInt64* destinationStart = &destination[0])
            {
                Int64* destinationAddress = (Int64*)destinationStart;
                UInt32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                {
                    Vector256<Int64> hextet = Avx2.ConvertToVector256Int64(sourceAddress);
                    Avx.Store(destinationAddress, hextet);
                }
            }
            for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
            {
                destination[scalarIndex] = source[scalarIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HistogramIncrement(Span<int> histogram, Vector256<int> histogramIndex256)
        {
            // can potentially use gather but only if multiple increments on the same index are detected
            // scatter is in AVX-512
            Vector128<int> histogramIndexLower128 = histogramIndex256.GetLower();
            int histogramIndex0 = histogramIndexLower128.ToScalar();
            ++histogram[histogramIndex0];
            int histogramIndex1 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32OneToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex1];
            int histogramIndex2 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32TwoToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex2];
            int histogramIndex3 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32ThreeToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex3];

            Vector128<int> histogramIndexUpper128 = histogramIndex256.GetUpper();
            int histogramIndex4 = histogramIndexUpper128.ToScalar();
            ++histogram[histogramIndex4];
            int histogramIndex5 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32OneToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex5];
            int histogramIndex6 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32TwoToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex6];
            int histogramIndex7 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32ThreeToZero).AsInt32().ToScalar();
            ++histogram[histogramIndex7];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void HistogramIncrement(Span<int> histogram, Vector256<int> histogramIndex256, int mask)
        {
            Vector128<int> histogramIndexLower128 = histogramIndex256.GetLower();
            if ((mask & 0x01) == 0x00) // mask is set if value is no data
            {
                int histogramIndex0 = histogramIndexLower128.ToScalar();
                ++histogram[histogramIndex0];
            }
            if ((mask & 0x02) == 0x00)
            {
                int histogramIndex1 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32OneToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex1];
            }
            if ((mask & 0x04) == 0x00)
            {
                int histogramIndex2 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32TwoToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex2];
            }
            if ((mask & 0x08) == 0x00)
            {
                int histogramIndex3 = Avx.Permute(histogramIndexLower128.AsSingle(), Constant.Simd128.Copy32ThreeToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex3];
            }

            Vector128<int> histogramIndexUpper128 = histogramIndex256.GetUpper();
            if ((mask & 0x10) == 0x00)
            {
                int histogramIndex4 = histogramIndexUpper128.ToScalar();
                ++histogram[histogramIndex4];
            }
            if ((mask & 0x20) == 0x00)
            {
                int histogramIndex5 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32OneToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex5];
            }
            if ((mask & 0x40) == 0x00)
            {
                int histogramIndex6 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32TwoToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex6];
            }
            if ((mask & 0x80) == 0x00)
            {
                int histogramIndex7 = Avx.Permute(histogramIndexUpper128.AsSingle(), Constant.Simd128.Copy32ThreeToZero).AsInt32().ToScalar();
                ++histogram[histogramIndex7];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double HorizontalAdd(Vector256<double> value)
        {
            Vector128<double> value128 = Avx.Add(value.GetLower(), value.GetUpper());
            return value128.ToScalar() + value128[1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int64 HorizontalAdd(Vector256<Int64> value)
        {
            Vector128<Int64> value128 = Avx.Add(value.GetLower(), value.GetUpper());
            return value128.ToScalar() + value128[1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double HorizontalMax(Vector256<double> value)
        {
            Vector128<double> maximumPair = Avx.Max(value.GetLower(), value.GetUpper());

            double maximum = maximumPair.ToScalar();
            double maximumPairElement1 = maximumPair[1];
            if (maximumPairElement1 > maximum)
            {
                maximum = maximumPairElement1;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HorizontalMax(Vector256<float> value)
        {
            Vector128<float> maximumQuad = Avx.Max(value.GetLower(), value.GetUpper());

            float maximum = maximumQuad.ToScalar();
            float maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            float maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            float maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte HorizontalMax(Vector256<sbyte> value)
        {
            Vector128<sbyte> maximumHextet = Avx2.Max(value.GetLower(), value.GetUpper());
            Vector128<sbyte> maximumOctet = Avx2.Max(maximumHextet, Avx2.Shuffle(maximumHextet.AsInt32(), Constant.Simd128.Circular32Up2).AsSByte());
            Vector128<sbyte> maximumQuad = Avx2.Max(maximumOctet, Avx2.Shuffle(maximumOctet.AsInt32(), Constant.Simd128.Circular32Up1).AsSByte());

            sbyte maximum = maximumQuad.ToScalar();
            sbyte maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            sbyte maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            sbyte maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int16 HorizontalMax(Vector256<Int16> value)
        {
            Vector128<Int16> maximumOctet = Avx2.Max(value.GetLower(), value.GetUpper());
            Vector128<Int16> maximumQuad = Avx2.Max(maximumOctet, Avx2.Shuffle(maximumOctet.AsInt32(), Constant.Simd128.Circular32Up2).AsInt16());

            Int16 maximum = maximumQuad.ToScalar();
            Int16 maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            Int16 maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            Int16 maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int32 HorizontalMax(Vector256<Int32> value)
        {
            Vector128<Int32> maximumQuad = Avx2.Max(value.GetLower(), value.GetUpper());

            Int32 maximum = maximumQuad.ToScalar();
            Int32 maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            Int32 maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            Int32 maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int64 HorizontalMax(Vector256<Int64> value)
        {
            Vector128<Int64> lower = value.GetLower();
            Vector128<Int64> upper = value.GetUpper();
            // _mm_max_epi64() is in AVX-512VL
            Vector128<Int64> maximumPair = Avx2.BlendVariable(lower, upper, Avx2.CompareGreaterThan(upper, lower));

            Int64 maximum = maximumPair.ToScalar();
            Int64 maximumPairElement1 = maximumPair[1];
            if (maximumPairElement1 > maximum)
            {
                maximum = maximumPairElement1;
            }
            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte HorizontalMax(Vector256<byte> value)
        {
            Vector128<byte> maximumHextet = Avx2.Max(value.GetLower(), value.GetUpper());
            Vector128<byte> maximumOctet = Avx2.Max(maximumHextet, Avx2.Shuffle(maximumHextet.AsInt32(), Constant.Simd128.Circular32Up2).AsByte());
            Vector128<byte> maximumQuad = Avx2.Max(maximumOctet, Avx2.Shuffle(maximumOctet.AsInt32(), Constant.Simd128.Circular32Up1).AsByte());

            byte maximum = maximumQuad.ToScalar();
            byte maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            byte maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            byte maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt16 HorizontalMax(Vector256<UInt16> value)
        {
            Vector128<UInt16> maximumOctet = Avx2.Max(value.GetLower(), value.GetUpper());
            Vector128<UInt16> maximumQuad = Avx2.Max(maximumOctet, Avx2.Shuffle(maximumOctet.AsUInt32(), Constant.Simd128.Circular32Up2).AsUInt16());

            UInt16 maximum = maximumQuad.ToScalar();
            UInt16 maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            UInt16 maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            UInt16 maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 HorizontalMax(Vector256<UInt32> value)
        {
            Vector128<UInt32> maximumQuad = Avx2.Max(value.GetLower(), value.GetUpper());

            UInt32 maximum = maximumQuad.ToScalar();
            UInt32 maximumQuadElement1 = maximumQuad[1];
            if (maximumQuadElement1 > maximum)
            {
                maximum = maximumQuadElement1;
            }
            UInt32 maximumQuadElement2 = maximumQuad[2];
            if (maximumQuadElement2 > maximum)
            {
                maximum = maximumQuadElement2;
            }
            UInt32 maximumQuadElement3 = maximumQuad[3];
            if (maximumQuadElement3 > maximum)
            {
                maximum = maximumQuadElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt64 HorizontalMax(Vector256<UInt64> value)
        {
            // _mm_cmp_epu64_mask() is in AVX-512VL
            Vector128<UInt64> lower = value.GetLower();
            Vector128<UInt64> upper = value.GetUpper();

            UInt64 maximum = lower.ToScalar();
            UInt64 maximumPairElement1 = lower[1];
            if (maximumPairElement1 > maximum)
            {
                maximum = maximumPairElement1;
            }
            UInt64 maximumPairElement2 = upper.ToScalar();
            if (maximumPairElement2 > maximum)
            {
                maximum = maximumPairElement2;
            }
            UInt64 maximumPairElement3 = upper[1];
            if (maximumPairElement3 > maximum)
            {
                maximum = maximumPairElement3;
            }

            return maximum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double HorizontalMin(Vector256<double> value)
        {
            Vector128<double> minimumPair = Avx.Min(value.GetLower(), value.GetUpper());

            double minimum = minimumPair.ToScalar();
            double minimumPairElement1 = minimumPair[1];
            if (minimumPairElement1 < minimum)
            {
                minimum = minimumPairElement1;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float HorizontalMin(Vector256<float> value)
        {
            Vector128<float> minimumQuad = Avx.Min(value.GetLower(), value.GetUpper());

            float minimum = minimumQuad.ToScalar();
            float minimumQuadElement1 = minimumQuad[1];
            if (minimumQuadElement1 < minimum)
            {
                minimum = minimumQuadElement1;
            }
            float minimumQuadElement2 = minimumQuad[2];
            if (minimumQuadElement2 < minimum)
            {
                minimum = minimumQuadElement2;
            }
            float minimumQuadElement3 = minimumQuad[3];
            if (minimumQuadElement3 < minimum)
            {
                minimum = minimumQuadElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte HorizontalMin(Vector256<sbyte> value)
        {
            Vector128<sbyte> minimumHextet = Avx2.Min(value.GetLower(), value.GetUpper());
            Vector128<sbyte> minimumOctet = Avx2.Min(minimumHextet, Avx2.Shuffle(minimumHextet.AsInt32(), Constant.Simd128.Circular32Up2).AsSByte());
            Vector128<sbyte> minimumQuad = Avx2.Min(minimumOctet, Avx2.Shuffle(minimumOctet.AsInt32(), Constant.Simd128.Circular32Up1).AsSByte());

            sbyte minimum = minimumQuad.ToScalar();
            sbyte minimumQuadElement1 = minimumQuad[1];
            if (minimumQuadElement1 < minimum)
            {
                minimum = minimumQuadElement1;
            }
            sbyte minimumQuadElement2 = minimumQuad[2];
            if (minimumQuadElement2 < minimum)
            {
                minimum = minimumQuadElement2;
            }
            sbyte minimumQuadElement3 = minimumQuad[3];
            if (minimumQuadElement3 < minimum)
            {
                minimum = minimumQuadElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int16 HorizontalMin(Vector256<Int16> value)
        {
            Vector128<Int16> minimumOctet = Avx2.Min(value.GetLower(), value.GetUpper());
            Vector128<Int16> minimumQuad = Avx2.Min(minimumOctet, Avx2.Shuffle(minimumOctet.AsInt32(), Constant.Simd128.Circular32Up2).AsInt16());

            Int16 minimum = minimumQuad.ToScalar();
            Int16 minimumQuadElement1 = minimumQuad[1];
            if (minimumQuadElement1 < minimum)
            {
                minimum = minimumQuadElement1;
            }
            Int16 minimumQuadElement2 = minimumQuad[2];
            if (minimumQuadElement2 < minimum)
            {
                minimum = minimumQuadElement2;
            }
            Int16 minimumQuadElement3 = minimumQuad[3];
            if (minimumQuadElement3 < minimum)
            {
                minimum = minimumQuadElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int32 HorizontalMin(Vector256<Int32> value)
        {
            Vector128<Int32> minimumQuad = Avx2.Min(value.GetLower(), value.GetUpper());

            Int32 minimum = minimumQuad.ToScalar();
            Int32 minimumQuadElement1 = minimumQuad[1];
            if (minimumQuadElement1 < minimum)
            {
                minimum = minimumQuadElement1;
            }
            Int32 minimumQuadElement2 = minimumQuad[2];
            if (minimumQuadElement2 < minimum)
            {
                minimum = minimumQuadElement2;
            }
            Int32 minimumQuadElement3 = minimumQuad[3];
            if (minimumQuadElement3 < minimum)
            {
                minimum = minimumQuadElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int64 HorizontalMin(Vector256<Int64> value)
        {
            Vector128<Int64> lower = value.GetLower();
            Vector128<Int64> upper = value.GetUpper();
            Vector128<Int64> minimumPair = Avx2.BlendVariable(lower, upper, Avx2.CompareGreaterThan(lower, upper)); // _mm_min_epi64() is in AVX-512VL

            Int64 minimum = minimumPair.ToScalar();
            Int64 minimumPairElement1 = minimumPair[1];
            if (minimumPairElement1 < minimum)
            {
                minimum = minimumPairElement1;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte HorizontalMin(Vector256<byte> value)
        {
            Vector128<byte> minimum128 = Avx2.Min(value.GetLower(), value.GetUpper());
            Vector128<UInt16> octet0 = Avx2.ConvertToVector128Int16(minimum128).AsUInt16();
            Vector128<UInt16> octet1 = Avx2.ConvertToVector128Int16(Avx2.Shuffle(minimum128.AsInt32(), Constant.Simd128.Circular32Up2).AsByte()).AsUInt16();
            Vector128<UInt16> octetMinimum = Avx2.Min(octet0, octet1);
            UInt16 minimum = Avx2.MinHorizontal(octetMinimum).ToScalar();
            return (byte)minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt16 HorizontalMin(Vector256<UInt16> value)
        {
            Vector128<UInt16> minimum128 = Avx2.Min(value.GetLower(), value.GetUpper());
            UInt16 minimum = Avx2.MinHorizontal(minimum128).ToScalar();
            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 HorizontalMin(Vector256<UInt32> value)
        {
            Vector128<UInt32> minimumQuad = Avx2.Min(value.GetLower(), value.GetUpper());

            UInt32 minimum = minimumQuad.ToScalar();
            UInt32 minimumQuadElement1 = minimumQuad[1];
            if (minimumQuadElement1 < minimum)
            {
                minimum = minimumQuadElement1;
            }
            UInt32 minimumQuadElement2 = minimumQuad[2];
            if (minimumQuadElement2 < minimum)
            {
                minimum = minimumQuadElement2;
            }
            UInt32 minimumQuadElement3 = minimumQuad[3];
            if (minimumQuadElement3 < minimum)
            {
                minimum = minimumQuadElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt64 HorizontalMin(Vector256<UInt64> value)
        {
            // _mm_cmp_epu64_mask() is in AVX-512VL
            Vector128<UInt64> lower = value.GetLower();
            Vector128<UInt64> upper = value.GetUpper();

            UInt64 minimum = lower.ToScalar();
            UInt64 minimumPairElement1 = lower[1];
            if (minimumPairElement1 < minimum)
            {
                minimum = minimumPairElement1;
            }
            UInt64 minimumPairElement2 = upper.ToScalar();
            if (minimumPairElement2 < minimum)
            {
                minimum = minimumPairElement2;
            }
            UInt64 minimumPairElement3 = upper[1];
            if (minimumPairElement3 < minimum)
            {
                minimum = minimumPairElement3;
            }

            return minimum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<Int64> Max(Vector256<Int64> value1, Vector256<Int64> value2)
        {
            // _mm256_max_epi64() is in AVX-512VL
            return Avx2.BlendVariable(value1, value2, Avx2.CompareGreaterThan(value2, value1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<Int64> Min(Vector256<Int64> value1, Vector256<Int64> value2)
        {
            // _mm256_min_epi64() is in AVX-512VL
            return Avx2.BlendVariable(value1, value2, Avx2.CompareGreaterThan(value1, value2));
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static Vector256<int> Not(Vector256<int> mask)
        //{
        //    // use integer compare to produce -1 and set all bits for xor to flip
        //    // Float compare behaves correctly when mask values are zero but, since all bits set is NaN, CompareEqual() and CompareOrdered() return zero
        //    // rather than an integer -1 to set all bits.
        //    return Avx2.Xor(mask, Avx2.CompareEqual(mask, mask));
        //}

        public static unsafe void Pack(ReadOnlySpan<Int16> source, Span<sbyte> destination, bool noDataIsSaturatingFromBelow)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 16; // read 16 Int16s, convert to 16 Int64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int16* sourceStart = &source[0])
            fixed (sbyte* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of Int16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                sbyte* destinationAddress = destinationStart;
                Int16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                if (noDataIsSaturatingFromBelow)
                {
                    Vector256<Int16> int8min = Vector256.Create((Int16)SByte.MinValue);
                    Vector256<Int16> int8unsaturatedMin = Vector256.Create((Int16)(SByte.MinValue + 1));
                    for (Int16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<Int16> octet16 = Avx2.LoadVector256(sourceAddress);
                        Vector256<Int16> dataSaturationMask = Avx2.CompareEqual(octet16, int8min);
                        octet16 = Avx2.BlendVariable(octet16, int8unsaturatedMin, dataSaturationMask);
                        Vector128<sbyte> octet8 = Avx2.PackSignedSaturate(octet16.GetLower(), octet16.GetUpper());
                        Avx.Store(destinationAddress, octet8);
                    }
                }
                else
                {
                    for (Int16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<Int16> octet16 = Avx2.LoadVector256(sourceAddress);
                        Vector128<sbyte> octet8 = Avx2.PackSignedSaturate(octet16.GetLower(), octet16.GetUpper());
                        Avx.Store(destinationAddress, octet8);
                    }
                }
            }

            if (noDataIsSaturatingFromBelow)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    Int16 value16 = source[scalarIndex];
                    sbyte value8 = value16 == SByte.MinValue ? (SByte)(SByte.MinValue + 1) : SByte.CreateSaturating(value16);
                    destination[scalarIndex] = value8;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = SByte.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        public static unsafe void Pack(ReadOnlySpan<Int32> source, Span<sbyte> destination, bool noDataIsSaturatingFromAbove)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 2 * 256 / 32; // read 16 Int32s, convert to 16 sbytes
            const int halfStride = stride / 2;
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int32* sourceStart = &source[0])
            fixed (sbyte* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of Int16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                sbyte* destinationAddress = destinationStart;
                Int32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                if (noDataIsSaturatingFromAbove)
                {
                    Vector256<Int32> int8min = Vector256.Create((Int32)SByte.MinValue);
                    Vector256<Int32> int8unsaturatedMin = Vector256.Create((Int32)(SByte.MinValue + 1));
                    for (Int32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += halfStride)
                    {
                        Vector256<Int32> octet32_0 = Avx2.LoadVector256(sourceAddress);
                        Vector256<Int32> dataSaturationMask0 = Avx2.CompareEqual(octet32_0, int8min);
                        octet32_0 = Avx2.BlendVariable(octet32_0, int8unsaturatedMin, dataSaturationMask0);
                        Vector128<Int16> octet16_0 = Avx2.PackSignedSaturate(octet32_0.GetLower(), octet32_0.GetUpper());

                        sourceAddress += halfStride;
                        Vector256<Int32> octet32_1 = Avx2.LoadVector256(sourceAddress);
                        Vector256<Int32> dataSaturationMask1 = Avx2.CompareEqual(octet32_1, int8min);
                        octet32_1 = Avx2.BlendVariable(octet32_1, int8unsaturatedMin, dataSaturationMask1);
                        Vector128<Int16> octet16_1 = Avx2.PackSignedSaturate(octet32_1.GetLower(), octet32_1.GetUpper());

                        Vector128<sbyte> hextet8 = Avx2.PackSignedSaturate(octet16_0, octet16_1);
                        Avx.Store(destinationAddress, hextet8);
                    }
                }
                else
                {
                    for (Int32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += halfStride)
                    {
                        Vector256<Int32> octet32_0 = Avx2.LoadVector256(sourceAddress);
                        Vector128<Int16> octet16_0 = Avx2.PackSignedSaturate(octet32_0.GetLower().AsInt32(), octet32_0.GetUpper().AsInt32());

                        sourceAddress += halfStride;
                        Vector256<Int32> octet32_1 = Avx2.LoadVector256(sourceAddress);
                        Vector128<Int16> octet16_1 = Avx2.PackSignedSaturate(octet32_1.GetLower(), octet32_1.GetUpper());

                        Vector128<sbyte> hextet8 = Avx2.PackSignedSaturate(octet16_0, octet16_1);
                        Avx.Store(destinationAddress, hextet8);
                    }
                }
            }

            if (noDataIsSaturatingFromAbove)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    Int32 value32 = source[scalarIndex];
                    sbyte value8 = value32 == SByte.MinValue ? (sbyte)(SByte.MinValue + 1) : SByte.CreateSaturating(value32);
                    destination[scalarIndex] = value8;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = SByte.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        public static unsafe void Pack(ReadOnlySpan<Int32> source, Span<Int16> destination, bool noDataIsSaturatingFromBelow)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 Int32s, convert to 8 Int16s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (Int32* sourceStart = &source[0])
            fixed (Int16* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of Int16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                Int16* destinationAddress = destinationStart;
                Int32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                if (noDataIsSaturatingFromBelow)
                {
                    Vector256<Int32> int16min = Vector256.Create((Int32)Int16.MinValue);
                    Vector256<Int32> int16unsaturatedMin = Vector256.Create((Int32)(Int16.MinValue + 1));
                    for (Int32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<Int32> octet32 = Avx2.LoadVector256(sourceAddress);
                        Vector256<Int32> dataSaturationMask = Avx2.CompareEqual(octet32, int16min);
                        octet32 = Avx2.BlendVariable(octet32, int16unsaturatedMin, dataSaturationMask);
                        Vector128<Int16> octet16 = Avx2.PackSignedSaturate(octet32.GetLower(), octet32.GetUpper());
                        Avx.Store(destinationAddress, octet16);
                    }
                }
                else
                {
                    for (Int32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<Int32> octet32 = Avx2.LoadVector256(sourceAddress);
                        Vector128<Int16> octet16 = Avx2.PackSignedSaturate(octet32.GetLower(), octet32.GetUpper());
                        Avx.Store(destinationAddress, octet16);
                    }
                }
            }

            if (noDataIsSaturatingFromBelow)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    Int32 value32 = source[scalarIndex];
                    Int16 value16 = value32 == Int16.MinValue ? (Int16)(Int16.MinValue + 1) : Int16.CreateSaturating(value32);
                    destination[scalarIndex] = value16;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = Int16.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        public static unsafe void Pack(ReadOnlySpan<UInt16> source, Span<byte> destination, bool noDataIsSaturatingFromAbove)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 16; // read 16 UInt16s, convert to 16 UInt64s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt16* sourceStart = &source[0])
            fixed (byte* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of UInt16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                byte* destinationAddress = destinationStart;
                UInt16* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                Vector256<UInt16> uint8max = Vector256.Create((UInt16)Byte.MaxValue);
                if (noDataIsSaturatingFromAbove)
                {
                    Vector256<UInt16> uint8unsaturatedMax = Vector256.Create((UInt16)(Byte.MaxValue - 1));
                    for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<UInt16> octet16 = Avx2.LoadVector256(sourceAddress);
                        Vector256<UInt16> dataSaturationMask = Avx2.CompareEqual(octet16, uint8max);
                        octet16 = Avx2.BlendVariable(Avx2.Min(octet16, uint8max), uint8unsaturatedMax, dataSaturationMask);
                        Vector128<byte> octet8 = Avx2.PackUnsignedSaturate(octet16.GetLower().AsInt16(), octet16.GetUpper().AsInt16());
                        Avx.Store(destinationAddress, octet8);
                    }
                }
                else
                {
                    for (UInt16* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<UInt16> octet16 = Avx2.Min(Avx2.LoadVector256(sourceAddress), uint8max);
                        Vector128<byte> octet8 = Avx2.PackUnsignedSaturate(octet16.GetLower().AsInt16(), octet16.GetUpper().AsInt16());
                        Avx.Store(destinationAddress, octet8);
                    }
                }
            }

            if (noDataIsSaturatingFromAbove)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    UInt16 value16 = source[scalarIndex];
                    byte value8 = value16 == Byte.MaxValue ? (Byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(value16);
                    destination[scalarIndex] = value8;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = Byte.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        public static unsafe void Pack(ReadOnlySpan<UInt32> source, Span<byte> destination, bool noDataIsSaturatingFromAbove)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 2 * 256 / 32; // read 16 UInt32s, convert to 16 bytes
            const int halfStride = stride / 2;
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt32* sourceStart = &source[0])
            fixed (byte* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of UInt16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                byte* destinationAddress = destinationStart;
                UInt32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                Vector256<UInt32> uint8max = Vector256.Create((UInt32)Byte.MaxValue);
                if (noDataIsSaturatingFromAbove)
                {
                    Vector256<UInt32> uint8unsaturatedMax = Vector256.Create((UInt32)(Byte.MaxValue - 1));
                    for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += halfStride)
                    {
                        Vector256<UInt32> octet32_0 = Avx2.LoadVector256(sourceAddress);
                        Vector256<UInt32> dataSaturationMask0 = Avx2.CompareEqual(octet32_0, uint8max);
                        octet32_0 = Avx2.BlendVariable(Avx2.Min(octet32_0, uint8max), uint8unsaturatedMax, dataSaturationMask0);
                        Vector128<UInt16> octet16_0 = Avx2.PackUnsignedSaturate(octet32_0.GetLower().AsInt32(), octet32_0.GetUpper().AsInt32());

                        sourceAddress += halfStride;
                        Vector256<UInt32> octet32_1 = Avx2.LoadVector256(sourceAddress);
                        Vector256<UInt32> dataSaturationMask1 = Avx2.CompareEqual(octet32_1, uint8max);
                        octet32_1 = Avx2.BlendVariable(Avx2.Min(octet32_1, uint8max), uint8unsaturatedMax, dataSaturationMask1);
                        Vector128<UInt16> octet16_1 = Avx2.PackUnsignedSaturate(octet32_1.GetLower().AsInt32(), octet32_1.GetUpper().AsInt32());

                        Vector128<byte> hextet8 = Avx2.PackUnsignedSaturate(octet16_0.AsInt16(), octet16_1.AsInt16());
                        Avx.Store(destinationAddress, hextet8);
                    }
                }
                else
                {
                    for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += halfStride)
                    {
                        Vector256<UInt32> octet32_0 = Avx2.Min(Avx2.LoadVector256(sourceAddress), uint8max);
                        Vector128<UInt16> octet16_0 = Avx2.PackUnsignedSaturate(octet32_0.GetLower().AsInt32(), octet32_0.GetUpper().AsInt32());

                        sourceAddress += halfStride;
                        Vector256<UInt32> octet32_1 = Avx2.Min(Avx2.LoadVector256(sourceAddress), uint8max);
                        Vector128<UInt16> octet16_1 = Avx2.PackUnsignedSaturate(octet32_1.GetLower().AsInt32(), octet32_1.GetUpper().AsInt32());

                        Vector128<byte> hextet8 = Avx2.PackUnsignedSaturate(octet16_0.AsInt16(), octet16_1.AsInt16());
                        Avx.Store(destinationAddress, hextet8);
                    }
                }
            }

            if (noDataIsSaturatingFromAbove)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    UInt32 value32 = source[scalarIndex];
                    byte value8 = value32 == Byte.MaxValue ? (byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(value32);
                    destination[scalarIndex] = value8;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = Byte.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        public static unsafe void Pack(ReadOnlySpan<UInt32> source, Span<UInt16> destination, bool noDataIsSaturatingFromAbove)
        {
            if (source.Length != destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destination), "Source array length " + source.Length + " does not match destination array length " + destination.Length + ".");
            }

            const int stride = 256 / 32; // read 8 UInt32s, convert to 8 UInt16s
            int sourceEndIndexAvx = stride * (source.Length / stride);
            fixed (UInt32* sourceStart = &source[0])
            fixed (UInt16* destinationStart = &destination[0])
            {
                // no _mm_packus_epu16() in AVX or AVX10
                // Workaround is to clamp 32 bit values to the maximum value of UInt16 and then just use _mm_packus_epi32() for type conversion rather than
                // taking advantage of its saturation capabilities. _mm256_packus_epi32() isn't particularly helpful here as it packs within lanes, requiring
                // two 256 bit fetches and then lane swapping and permutes to produce a 256 bit store.
                UInt16* destinationAddress = destinationStart;
                UInt32* sourceEndAvx = sourceStart + sourceEndIndexAvx;
                Vector256<UInt32> uint16max = Vector256.Create((UInt32)UInt16.MaxValue);
                if (noDataIsSaturatingFromAbove)
                {
                    Vector256<UInt32> uint16unsaturatedMax = Vector256.Create((UInt32)(UInt16.MaxValue - 1));
                    for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<UInt32> octet32 = Avx2.LoadVector256(sourceAddress);
                        Vector256<UInt32> dataSaturationMask = Avx2.CompareEqual(octet32, uint16max);
                        octet32 = Avx2.BlendVariable(Avx2.Min(octet32, uint16max), uint16unsaturatedMax, dataSaturationMask);
                        Vector128<UInt16> octet16 = Avx2.PackUnsignedSaturate(octet32.GetLower().AsInt32(), octet32.GetUpper().AsInt32());
                        Avx.Store(destinationAddress, octet16);
                    }
                }
                else
                {
                    for (UInt32* sourceAddress = sourceStart; sourceAddress < sourceEndAvx; destinationAddress += stride, sourceAddress += stride)
                    {
                        Vector256<UInt32> octet32 = Avx2.Min(Avx2.LoadVector256(sourceAddress), uint16max);
                        Vector128<UInt16> octet16 = Avx2.PackUnsignedSaturate(octet32.GetLower().AsInt32(), octet32.GetUpper().AsInt32());
                        Avx.Store(destinationAddress, octet16);
                    }
                }
            }

            if (noDataIsSaturatingFromAbove)
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    UInt32 value32 = source[scalarIndex];
                    UInt16 value16 = value32 == UInt16.MaxValue ? (UInt16)(UInt16.MaxValue - 1) : UInt16.CreateSaturating(value32);
                    destination[scalarIndex] = value16;
                }
            }
            else
            {
                for (int scalarIndex = sourceEndIndexAvx; scalarIndex < source.Length; ++scalarIndex)
                {
                    destination[scalarIndex] = UInt16.CreateSaturating(source[scalarIndex]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<byte> ShuffleInAndUp(byte e0, byte e1, byte e2, Vector128<byte> data)
        {
            return Avx2.Blend(Vector128.CreateScalarUnsafe((e0 << 0) | (e1 << 8) | (e2 << 16)), Avx.Shuffle(data.AsInt32(), Constant.Simd128.Circular32Up1), Constant.Simd128.BlendA0B123).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> ShuffleInAndUp(float e0, Vector128<float> data)
        {
            return Avx.Blend(Vector128.CreateScalarUnsafe(e0), Avx.Shuffle(data, data, Constant.Simd128.Circular32Up1), Constant.Simd128.BlendA0B123);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static Vector128<Int32> ShuffleInAndUp(Int32 e0, Vector128<Int32> data)
        //{
        //    return Avx2.Blend(Vector128.CreateScalarUnsafe(e0), Avx2.Shuffle(data, Constant.Simd128.Circular32Up1), Constant.Simd128.BlendA0B123);
        //}

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static Vector128<UInt32> ShuffleInAndUp(UInt32 e0, Vector128<UInt32> data)
        //{
        //    return Avx2.Blend(Vector128.CreateScalarUnsafe(e0), Avx2.Shuffle(data, Constant.Simd128.Circular32Up1), Constant.Simd128.BlendA0B123);
        //}

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public static Vector128<UInt64> ShuffleInAndUp(UInt64 e0, Vector128<UInt64> data)
        //{
        //    return Avx2.Blend(Vector128.CreateScalarUnsafe(e0).AsDouble(), Avx2.Shuffle(data.AsDouble(), data.AsDouble(), Constant.Simd128.Circular64Up1), Constant.Simd128.BlendA0B1).AsUInt64();
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<float> ToFloat(this Vector256<UInt32> value) 
        {
            // FMA implementation of
            // https://stackoverflow.com/questions/34066228/how-to-perform-uint32-float-conversion-with-sse
            Vector256<Int32> maskLow16 = Vector256.Create(0xffff);
            Vector256<Int32> valueIntegerLow16 = Avx2.And(value.AsInt32(), maskLow16);
            Vector256<Int32> valueIntegerUpper16 = Avx2.ShiftRightLogical(value.AsInt32(), 16);

            Vector256<float> valueFloatLow16 = Avx.ConvertToVector256Single(valueIntegerLow16);
            Vector256<float> valueFloatUpper16 = Avx.ConvertToVector256Single(valueIntegerUpper16);

            Vector256<float> uint16max = Vector256.Create(65536.0F);
            Vector256<float> valueAsFloat = Fma.MultiplyAdd(uint16max, valueFloatUpper16, valueFloatLow16);
            return valueAsFloat;
        }
    }
}
