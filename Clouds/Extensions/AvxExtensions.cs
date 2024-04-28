using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mars.Clouds.Extensions
{
    internal static class AvxExtensions
    {
        public static unsafe void Convert(sbyte[] source, Int16[] destination)
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

        public static unsafe void Convert(sbyte[] source, Int32[] destination)
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

        public static unsafe void Convert(sbyte[] source, Int64[] destination)
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

        public static unsafe void Convert(Int16[] source, Int32[] destination)
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

        public static unsafe void Convert(Int16[] source, Int64[] destination)
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

        public static unsafe void Convert(Int32[] source, Int64[] destination)
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

        public static unsafe void Convert(byte[] source, Int16[] destination)
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

        public static unsafe void Convert(byte[] source, Int32[] destination)
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

        public static unsafe void Convert(byte[] source, Int64[] destination)
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

        public static unsafe void Convert(byte[] source, UInt16[] destination)
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

        public static unsafe void Convert(byte[] source, UInt32[] destination)
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

        public static unsafe void Convert(byte[] source, UInt64[] destination)
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

        public static unsafe void Convert(UInt16[] source, Int32[] destination)
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

        public static unsafe void Convert(UInt16[] source, Int64[] destination)
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

        public static unsafe void Convert(UInt16[] source, UInt32[] destination)
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

        public static unsafe void Convert(UInt16[] source, UInt64[] destination)
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

        public static unsafe void Convert(UInt32[] source, Int64[] destination)
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

        public static unsafe void Convert(UInt32[] source, UInt64[] destination)
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
        public static Vector128<byte> ShuffleInAndUp(byte e0, byte e1, byte e2, Vector128<byte> data)
        {
            return Avx2.Blend(Vector128.CreateScalarUnsafe((e0 << 0) | (e1 << 8) | (e2 << 16)), Avx.Shuffle(data.AsInt32(), Constant.Simd128.CircularUp1), Constant.Simd128.BlendA0B123).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> ShuffleInAndUp(float e0, Vector128<float> data)
        {
            return Avx.Blend(Vector128.CreateScalarUnsafe(e0), Avx.Shuffle(data, data, Constant.Simd128.CircularUp1), Constant.Simd128.BlendA0B123);
        }
    }
}
