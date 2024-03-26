using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mars.Clouds.Extensions
{
    internal static class AvxExtensions
    {
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
