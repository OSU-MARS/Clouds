using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    internal static class DataTypeExtensions
    {
        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<byte> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int16:
                    Int16[]? destinationInt16 = destination as Int16[];
                    Debug.Assert(destinationInt16 != null);
                    AvxExtensions.Convert(source, destinationInt16);
                    break;
                case TypeCode.Int32:
                    Int32[]? destinationInt32 = destination as Int32[];
                    Debug.Assert(destinationInt32 != null);
                    AvxExtensions.Convert(source, destinationInt32);
                    break;
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                case TypeCode.UInt16:
                    UInt16[]? destinationUInt16 = destination as UInt16[];
                    Debug.Assert(destinationUInt16 != null);
                    AvxExtensions.Convert(source, destinationUInt16);
                    break;
                case TypeCode.UInt32:
                    UInt32[]? destinationUInt32 = destination as UInt32[];
                    Debug.Assert(destinationUInt32 != null);
                    AvxExtensions.Convert(source, destinationUInt32);
                    break;
                case TypeCode.UInt64:
                    UInt64[]? destinationUInt64 = destination as UInt64[];
                    Debug.Assert(destinationUInt64 != null);
                    AvxExtensions.Convert(source, destinationUInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding byte data.");
            }
        }

        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<Int16> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int32:
                    Int32[]? destinationInt32 = destination as Int32[];
                    Debug.Assert(destinationInt32 != null);
                    AvxExtensions.Convert(source, destinationInt32);
                    break;
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding Int16 data.");
            }
        }

        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<Int32> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding Int32 data.");
            }
        }

        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<sbyte> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int16:
                    Int16[]? destinationInt16 = destination as Int16[];
                    Debug.Assert(destinationInt16 != null);
                    AvxExtensions.Convert(source, destinationInt16);
                    break;
                case TypeCode.Int32:
                    Int32[]? destinationInt32 = destination as Int32[];
                    Debug.Assert(destinationInt32 != null);
                    AvxExtensions.Convert(source, destinationInt32);
                    break;
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding signed byte data.");
            }
        }

        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<UInt16> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int32:
                    Int32[]? destinationInt32 = destination as Int32[];
                    Debug.Assert(destinationInt32 != null);
                    AvxExtensions.Convert(source, destinationInt32);
                    break;
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                case TypeCode.UInt32:
                    UInt32[]? destinationUInt32 = destination as UInt32[];
                    Debug.Assert(destinationUInt32 != null);
                    AvxExtensions.Convert(source, destinationUInt32);
                    break;
                case TypeCode.UInt64:
                    UInt64[]? destinationUInt64 = destination as UInt64[];
                    Debug.Assert(destinationUInt64 != null);
                    AvxExtensions.Convert(source, destinationUInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding UInt16 data.");
            }
        }

        public static void ConvertFromKnownType<TBand>(ReadOnlySpan<UInt32> source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                case TypeCode.UInt64:
                    UInt64[]? destinationUInt64 = destination as UInt64[];
                    Debug.Assert(destinationUInt64 != null);
                    AvxExtensions.Convert(source, destinationUInt64);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled destination type {typeof(TBand).Name} for expanding UInt32 data.");
            }
        }

        public static void ConvertToKnownType<TBand>(TBand[] source, Span<double> destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Single:
                    float[]? sourceFloat = source as float[];
                    Debug.Assert(sourceFloat != null);
                    AvxExtensions.Convert(sourceFloat, destination);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for expanding to double data.");
            }
        }

        public static void ConvertToKnownType<TBand>(TBand[] source, Span<Int64> destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.SByte:
                    Int16[]? sourceInt8 = source as Int16[];
                    Debug.Assert(sourceInt8 != null);
                    AvxExtensions.Convert(sourceInt8, destination);
                    break;
                case TypeCode.Int16:
                    Int16[]? sourceInt16 = source as Int16[];
                    Debug.Assert(sourceInt16 != null);
                    AvxExtensions.Convert(sourceInt16, destination);
                    break;
                case TypeCode.Int32:
                    Int32[]? sourceInt32 = source as Int32[];
                    Debug.Assert(sourceInt32 != null);
                    AvxExtensions.Convert(sourceInt32, destination);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for expanding to Int64 data.");
            }
        }

        public static void ConvertToKnownType<TBand>(TBand[] source, Span<UInt64> destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Byte:
                    byte[]? sourceUInt8 = source as byte[];
                    Debug.Assert(sourceUInt8 != null);
                    AvxExtensions.Convert(sourceUInt8, destination);
                    break;
                case TypeCode.UInt16:
                    UInt16[]? sourceUInt16 = source as UInt16[];
                    Debug.Assert(sourceUInt16 != null);
                    AvxExtensions.Convert(sourceUInt16, destination);
                    break;
                case TypeCode.UInt32:
                    UInt32[]? sourceUInt32 = source as UInt32[];
                    Debug.Assert(sourceUInt32 != null);
                    AvxExtensions.Convert(sourceUInt32, destination);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for expanding to UInt64.");
            }
        }

        public static DataType GetMostCompactIntegerType(bool considerBand1, RasterBand<UInt32>? band1, bool considerBand2, RasterBand<UInt32>? band2)
        {
            bool hasMaxValue1 = false;
            UInt32 maxValue1 = UInt32.MaxValue;
            if (considerBand1)
            {
                if (band1 == null)
                {
                    throw new ArgumentNullException(nameof(band1), $"{nameof(band1)} is null but {nameof(considerBand1)} is true.");
                }
                hasMaxValue1 = band1.TryGetMaximumValue(out maxValue1);
            }

            bool hasMaxValue2 = false;
            UInt32 maxValue2 = UInt32.MaxValue;
            if (considerBand2)
            {
                if (band2 == null)
                {
                    throw new ArgumentNullException(nameof(band2), $"{nameof(band2)} is null but {nameof(considerBand2)} is true.");
                }
                hasMaxValue2 = band2.TryGetMaximumValue(out maxValue2);
            }

            if (hasMaxValue1 && hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue2);
            }
            else if (hasMaxValue1)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1);
            }
            else if (hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue2);
            }

            return DataType.GDT_Byte; // minimum size since no data in either band
        }

        public static DataType GetMostCompactIntegerType(RasterBand<UInt16> band)
        {
            bool hasMaxValue = band.TryGetMaximumValue(out UInt16 maxValue);
            if (hasMaxValue)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue);
            }

            return DataType.GDT_Byte; // minimum size since no data
        }

        public static DataType GetMostCompactIntegerType(RasterBand<UInt32> band1, RasterBand<UInt32> band2)
        {
            bool hasMaxValue1 = band1.TryGetMaximumValue(out UInt32 maxValue1);
            bool hasMaxValue2 = band2.TryGetMaximumValue(out UInt32 maxValue2);
            if (hasMaxValue1 && hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue2);
            }
            else if (hasMaxValue1)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1);
            }
            else if (hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue2);
            }

            return DataType.GDT_Byte; // minimum size since no data in either band
        }

        public static DataType GetMostCompactIntegerType(RasterBand<UInt16> band1, RasterBand<UInt16> band2, RasterBand<UInt16> band3)
        {
            bool hasMaxValue1 = band1.TryGetMaximumValue(out UInt16 maxValue1);
            bool hasMaxValue2 = band2.TryGetMaximumValue(out UInt16 maxValue2);
            bool hasMaxValue3 = band3.TryGetMaximumValue(out UInt16 maxValue3);
            if (hasMaxValue1 && hasMaxValue2 && hasMaxValue3)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue2, maxValue3);
            }
            else if (hasMaxValue1 && hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue2);
            }
            else if (hasMaxValue1 && hasMaxValue3)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue3);
            }
            else if (hasMaxValue2 && hasMaxValue3)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue2, maxValue3);
            }
            else if (hasMaxValue1)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1);
            }
            else if (hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue2);
            }
            else if (hasMaxValue3)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue3);
            }

            return DataType.GDT_Byte; // minimum size since no data in any band
        }

        private static DataType GetMostCompactIntegerType(UInt16 maxValue)
        {
            if (maxValue < Byte.MaxValue)
            {
                return DataType.GDT_Byte;
            }

            return DataType.GDT_UInt16;
        }

        private static DataType GetMostCompactIntegerType(UInt16 maxValue1, UInt16 maxValue2)
        {
            if ((maxValue1 < Byte.MaxValue) && (maxValue2 < Byte.MaxValue))
            {
                return DataType.GDT_Byte;
            }

            return DataType.GDT_UInt16;
        }

        private static DataType GetMostCompactIntegerType(UInt16 maxValue1, UInt16 maxValue2, UInt16 maxValue3)
        {
            if ((maxValue1 < Byte.MaxValue) && (maxValue2 < Byte.MaxValue) && (maxValue3 < Byte.MaxValue))
            {
                return DataType.GDT_Byte;
            }

            return DataType.GDT_UInt16;
        }

        private static DataType GetMostCompactIntegerType(UInt32 maxValue)
        {
            if (maxValue < Byte.MaxValue)
            {
                return DataType.GDT_Byte;
            }
            if (maxValue < UInt16.MaxValue)
            {
                return DataType.GDT_UInt16;
            }

            return DataType.GDT_UInt32;
        }

        private static DataType GetMostCompactIntegerType(UInt32 maxValue1, UInt32 maxValue2)
        {
            if ((maxValue1 < Byte.MaxValue) && (maxValue2 < Byte.MaxValue))
            {
                return DataType.GDT_Byte;
            }
            if ((maxValue1 < UInt16.MaxValue) && (maxValue2 < UInt16.MaxValue))
            {
                return DataType.GDT_UInt16;
            }

            return DataType.GDT_UInt32;
        }

        private static DataType GetMostCompactIntegerType<TBand>(TBand maxValue) where TBand : IMinMaxValue<TBand>, INumber<TBand>, IUnsignedNumber<TBand>
        {
            if (maxValue < TBand.CreateChecked(Byte.MaxValue))
            {
                return DataType.GDT_Byte;
            }
            if ((maxValue < TBand.CreateChecked(UInt16.MaxValue)) || (TBand.MaxValue == TBand.CreateChecked(UInt16.MaxValue)))
            {
                return DataType.GDT_UInt16;
            }
            if ((maxValue < TBand.CreateChecked(UInt32.MaxValue)) || (TBand.MaxValue == TBand.CreateChecked(UInt32.MaxValue)))
            {
                return DataType.GDT_UInt32;
            }

            return DataType.GDT_UInt64;
        }

        private static DataType GetMostCompactIntegerType<TBand>(TBand maxValue1, TBand maxValue2) where TBand : IMinMaxValue<TBand>, INumber<TBand>, IUnsignedNumber<TBand>
        {
            if ((maxValue1 < TBand.CreateChecked(Byte.MaxValue)) && (maxValue2 < TBand.CreateChecked(Byte.MaxValue)))
            {
                return DataType.GDT_Byte;
            }
            if (((maxValue1 < TBand.CreateChecked(UInt16.MaxValue)) && (maxValue2 < TBand.CreateChecked(UInt16.MaxValue))) || (TBand.MaxValue == TBand.CreateChecked(UInt16.MaxValue)))
            {
                return DataType.GDT_UInt16;
            }
            if (((maxValue1 < TBand.CreateChecked(UInt32.MaxValue)) && (maxValue2 < TBand.CreateChecked(UInt32.MaxValue))) || (TBand.MaxValue == TBand.CreateChecked(UInt32.MaxValue)))
            {
                return DataType.GDT_UInt32;
            }

            return DataType.GDT_UInt64;
        }

        public static DataType GetMostCompactIntegerType<TBand>(RasterBand<TBand> band1, RasterBand<TBand> band2) where TBand : IMinMaxValue<TBand>, INumber<TBand>, IUnsignedNumber<TBand>
        {
            bool hasMaxValue1 = band1.TryGetMaximumValue(out TBand maxValue1);
            bool hasMaxValue2 = band2.TryGetMaximumValue(out TBand maxValue2);
            if (hasMaxValue1 && hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1, maxValue2);
            }
            else if (hasMaxValue1)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue1);
            }
            else if (hasMaxValue2)
            {
                return DataTypeExtensions.GetMostCompactIntegerType(maxValue2);
            }

            return DataType.GDT_Byte; // minimum size since no data in either band
        }

        public static int GetSizeInBytes(this DataType dataType)
        {
            return dataType switch
            {
                DataType.GDT_Byte => 1,
                DataType.GDT_Float32 => 4,
                DataType.GDT_Float64 => 8,
                DataType.GDT_Int8 => 1,
                DataType.GDT_Int16 => 2,
                DataType.GDT_Int32 => 4,
                DataType.GDT_Int64 => 8,
                DataType.GDT_UInt16 => 2,
                DataType.GDT_UInt32 => 4,
                DataType.GDT_UInt64 => 8,
                _ => throw new NotSupportedException($"Unhandled GDAL data type {dataType}.")
            }; ;
        }

        /// <returns>true if <paramref name="to"/> is a wider version of the same integer data type as <paramref name="from"/>, false otherwise</returns>
        /// <remarks>
        /// Since no expansion occurs, a data type is not considered exactly expandable to itself. Callers may need to check for the
        /// case where <paramref name="to"/> and <paramref name="from"/> are the same.
        /// </remarks>
        public static bool IsExactlyExpandable(DataType from, DataType to)
        {
            return from switch
            {
                DataType.GDT_Byte => (to == DataType.GDT_UInt16) || (to == DataType.GDT_UInt32) || (to == DataType.GDT_UInt64),
                DataType.GDT_Float32 => false, // inexactly expandable to float64
                DataType.GDT_Float64 => false,
                DataType.GDT_Int8 => (to == DataType.GDT_UInt16) || (to == DataType.GDT_Int32) || (to == DataType.GDT_Int64),
                DataType.GDT_Int16 => (to == DataType.GDT_Int32) || (to == DataType.GDT_Int64),
                DataType.GDT_Int32 => to == DataType.GDT_Int64,
                DataType.GDT_Int64 => false,
                DataType.GDT_UInt16 => (to == DataType.GDT_UInt32) || (to == DataType.GDT_UInt64) || (to == DataType.GDT_Int32) || (to == DataType.GDT_Int64),
                DataType.GDT_UInt32 => (to == DataType.GDT_UInt64) || (to == DataType.GDT_Int64),
                DataType.GDT_UInt64 => false,
                _ => throw new NotSupportedException($"Unhandled GDAL data type {from}.")
            }; ;
        }

        public static void Pack<TBand>(TBand[] source, Span<float> destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Double:
                    // vcvtpd2ps seems to be always saturating
                    double[]? sourceDouble = source as double[];
                    Debug.Assert(sourceDouble != null);
                    AvxExtensions.Convert(sourceDouble, destination);
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to sbyte data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<sbyte> destination, bool noDataSaturatingFromBelow) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int16:
                    Int16[]? sourceInt16 = source as Int16[];
                    Debug.Assert(sourceInt16 != null);
                    AvxExtensions.Pack(sourceInt16, destination, noDataSaturatingFromBelow);
                    break;
                case TypeCode.Int32:
                    Int32[]? sourceInt32 = source as Int32[];
                    Debug.Assert(sourceInt32 != null);
                    AvxExtensions.Pack(sourceInt32, destination, noDataSaturatingFromBelow);
                    break;
                case TypeCode.Int64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    // If there was a vpackusqw instruction it'd likely pack to 32 bit, requring a following vpackuswb.
                    Int64[]? sourceInt64 = source as Int64[];
                    Debug.Assert((sourceInt64 != null) && (sourceInt64.Length == destination.Length));
                    if (noDataSaturatingFromBelow)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            Int64 value64 = sourceInt64[index];
                            sbyte value8 = value64 == SByte.MinValue ? (sbyte)(SByte.MinValue + 1) : SByte.CreateSaturating(value64);
                            destination[index] = value8;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = SByte.CreateSaturating(sourceInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to sbyte data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<Int16> destination, bool noDataSaturatingFromBelow) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int32:
                    Int32[]? sourceInt32 = source as Int32[];
                    Debug.Assert(sourceInt32 != null);
                    AvxExtensions.Pack(sourceInt32, destination, noDataSaturatingFromBelow);
                    break;
                case TypeCode.Int64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    // If there was a vpackusqw instruction it'd likely pack to 32 bit, requring a following vpackuswb.
                    Int64[]? sourceInt64 = source as Int64[];
                    Debug.Assert((sourceInt64 != null) && (sourceInt64.Length == destination.Length));
                    if (noDataSaturatingFromBelow)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            Int64 value64 = sourceInt64[index];
                            Int16 value16 = value64 == Int16.MinValue ? (Int16)(Int16.MinValue + 1) : Int16.CreateSaturating(value64);
                            destination[index] = value16;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = Int16.CreateSaturating(sourceInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to Int16 data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<Int32> destination, bool noDataSaturatingFromBelow) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    Int64[]? sourceInt64 = source as Int64[];
                    Debug.Assert((sourceInt64 != null) && (sourceInt64.Length == destination.Length));
                    if (noDataSaturatingFromBelow)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            Int64 value64 = sourceInt64[index];
                            Int32 value32 = value64 == Int32.MinValue ? Int32.MinValue + 1 : Int32.CreateSaturating(value64);
                            destination[index] = value32;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = Int32.CreateSaturating(sourceInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to Int32 data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<byte> destination, bool noDataSaturatingFromAbove) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.UInt16:
                    UInt16[]? sourceUInt16 = source as UInt16[];
                    Debug.Assert(sourceUInt16 != null);
                    AvxExtensions.Pack(sourceUInt16, destination, noDataSaturatingFromAbove);
                    break;
                case TypeCode.UInt32:
                    UInt32[]? sourceUInt32 = source as UInt32[];
                    Debug.Assert(sourceUInt32 != null);
                    AvxExtensions.Pack(sourceUInt32, destination, noDataSaturatingFromAbove);
                    break;
                case TypeCode.UInt64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    // If there was a vpackusqw instruction it'd likely pack to 32 bit, requring a following vpackuswb.
                    UInt64[]? sourceUInt64 = source as UInt64[];
                    Debug.Assert((sourceUInt64 != null) && (sourceUInt64.Length == destination.Length));
                    if (noDataSaturatingFromAbove)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            UInt64 value64 = sourceUInt64[index];
                            byte value8 = value64 == Byte.MaxValue ? (byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(value64);
                            destination[index] = value8;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = Byte.CreateSaturating(sourceUInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to byte data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<UInt16> destination, bool noDataSaturatingFromAbove) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.UInt32:
                    UInt32[]? sourceUInt32 = source as UInt32[];
                    Debug.Assert(sourceUInt32 != null);
                    AvxExtensions.Pack(sourceUInt32, destination, noDataSaturatingFromAbove);
                    break;
                case TypeCode.UInt64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    // If there was a vpackusqw instruction it'd likely pack to 32 bit, requring a following vpackuswb.
                    UInt64[]? sourceUInt64 = source as UInt64[];
                    Debug.Assert((sourceUInt64 != null) && (sourceUInt64.Length == destination.Length));
                    if (noDataSaturatingFromAbove)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            UInt64 value64 = sourceUInt64[index];
                            UInt16 value16 = value64 == UInt16.MaxValue ? (UInt16)(UInt16.MaxValue - 1) : UInt16.CreateSaturating(value64);
                            destination[index] = value16;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = UInt16.CreateSaturating(sourceUInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to UInt16 data.");
            }
        }

        public static void Pack<TBand>(TBand[] source, Span<UInt32> destination, bool noDataSaturatingFromAbove) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.UInt64:
                    // no _mm_packus_epi64() or _mm_packus_epu64() in AVX or AVX10, _mm256_cmp_epu64_mask() is in AVX-512VL
                    UInt64[]? sourceUInt64 = source as UInt64[];
                    Debug.Assert((sourceUInt64 != null) && (sourceUInt64.Length == destination.Length));
                    if (noDataSaturatingFromAbove)
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            UInt64 value64 = sourceUInt64[index];
                            UInt32 value32 = value64 == UInt32.MaxValue ? UInt32.MaxValue - 1 : UInt32.CreateSaturating(value64);
                            destination[index] = value32;
                        }
                    }
                    else
                    {
                        for (int index = 0; index < source.Length; ++index)
                        {
                            destination[index] = UInt32.CreateSaturating(sourceUInt64[index]);
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unhandled source type {typeof(TBand).Name} for packing to UInt32 data.");
            }
        }
    }
}
