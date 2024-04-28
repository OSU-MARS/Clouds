using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using System;
using System.Diagnostics;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    internal static class DataTypeExtensions
    {
        //public static void Convert<TSource, TDestination>(TSource[] source, TDestination[] destination) 
        //    where TSource : INumber<TSource> 
        //    where TDestination : INumber<TDestination>
        //{
        //    for (int index = 0; index < source.Length; ++index)
        //    {
        //        destination[index] = TDestination.CreateChecked(source[index]);
        //    }
        //}

        public static void Convert<TBand>(byte[] source, TBand[] destination) where TBand : INumber<TBand>
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
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
        }

        public static void Convert<TBand>(Int16[] source, TBand[] destination) where TBand : INumber<TBand>
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
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
        }

        public static void Convert<TBand>(Int32[] source, TBand[] destination) where TBand : INumber<TBand>
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Int64:
                    Int64[]? destinationInt64 = destination as Int64[];
                    Debug.Assert(destinationInt64 != null);
                    AvxExtensions.Convert(source, destinationInt64);
                    break;
                default:
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
        }

        public static void Convert<TBand>(sbyte[] source, TBand[] destination) where TBand : INumber<TBand>
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
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
        }

        public static void Convert<TBand>(UInt16[] source, TBand[] destination) where TBand : INumber<TBand>
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
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
        }

        public static void Convert<TBand>(UInt32[] source, TBand[] destination) where TBand : INumber<TBand>
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
                    throw new NotSupportedException("Unhandled destination type " + typeof(TBand).Name + " for expanding byte data.");
            }
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
                _ => throw new NotSupportedException("Unhandled GDAL data type " + from + ".")
            }; ;
        }
    }
}
