using Mars.Clouds.GdalExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Numerics;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class SimdTests
    {
        [TestMethod]
        public void Convert()
        {
            sbyte[] signedArrayLengths = [ 7, 41, 71, 97, 113, 127 ];
            byte[] unsignedArrayLengths = [ 5, 19, 47, 101, 131, 193, 251 ];

            sbyte signedArrayLength = signedArrayLengths[Random.Shared.Next(signedArrayLengths.Length)];
            byte unsignedArrayLength = unsignedArrayLengths[Random.Shared.Next(unsignedArrayLengths.Length)];

            sbyte[] sourceInt8 = new sbyte[signedArrayLength];
            Int16[] sourceInt16 = new Int16[signedArrayLength];
            Int32[] sourceInt32 = new Int32[signedArrayLength];
            for (sbyte sign = -1, signedIndex = 0; signedIndex < signedArrayLength; sign *= -1, ++signedIndex)
            {
                sbyte value = (sbyte)(sign * signedIndex);
                sourceInt8[signedIndex] = value;
                sourceInt16[signedIndex] = value;
                sourceInt32[signedIndex] = value;
            }

            byte[] sourceUInt8 = new byte[unsignedArrayLength];
            UInt16[] sourceUInt16 = new UInt16[unsignedArrayLength];
            UInt32[] sourceUInt32 = new UInt32[unsignedArrayLength];
            for (byte unsignedIndex = 0; unsignedIndex < unsignedArrayLength; ++unsignedIndex)
            {
                sourceUInt8[unsignedIndex] = unsignedIndex;
                sourceUInt16[unsignedIndex] = unsignedIndex;
                sourceUInt32[unsignedIndex] = unsignedIndex;
            }

            Int16[] destinationInt8toInt16 = new Int16[signedArrayLength];
            Int32[] destinationInt8toInt32 = new Int32[signedArrayLength];
            Int64[] destinationInt8toInt64 = new Int64[signedArrayLength];
            Int32[] destinationInt16toInt32 = new Int32[signedArrayLength];
            Int64[] destinationInt16toInt64 = new Int64[signedArrayLength];
            Int64[] destinationInt32toInt64 = new Int64[signedArrayLength];
            DataTypeExtensions.Convert(sourceInt8, destinationInt8toInt16);
            DataTypeExtensions.Convert(sourceInt8, destinationInt8toInt32);
            DataTypeExtensions.Convert(sourceInt8, destinationInt8toInt64);
            DataTypeExtensions.Convert(sourceInt16, destinationInt16toInt32);
            DataTypeExtensions.Convert(sourceInt16, destinationInt16toInt64);
            DataTypeExtensions.Convert(sourceInt32, destinationInt32toInt64);

            Int16[] destinationUInt8toInt16 = new Int16[unsignedArrayLength];
            Int32[] destinationUInt8toInt32 = new Int32[unsignedArrayLength];
            Int64[] destinationUInt8toInt64 = new Int64[unsignedArrayLength];
            Int32[] destinationUInt16toInt32 = new Int32[unsignedArrayLength];
            Int64[] destinationUInt16toInt64 = new Int64[unsignedArrayLength];
            Int64[] destinationUInt32toInt64 = new Int64[unsignedArrayLength];
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toInt16);
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toInt32);
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toInt64);
            DataTypeExtensions.Convert(sourceUInt16, destinationUInt16toInt32);
            DataTypeExtensions.Convert(sourceUInt16, destinationUInt16toInt64);
            DataTypeExtensions.Convert(sourceUInt32, destinationUInt32toInt64);

            UInt16[] destinationUInt8toUInt16 = new UInt16[unsignedArrayLength];
            UInt32[] destinationUInt8toUInt32 = new UInt32[unsignedArrayLength];
            UInt64[] destinationUInt8toUInt64 = new UInt64[unsignedArrayLength];
            UInt32[] destinationUInt16toUInt32 = new UInt32[unsignedArrayLength];
            UInt64[] destinationUInt16toUInt64 = new UInt64[unsignedArrayLength];
            UInt64[] destinationUInt32toUInt64 = new UInt64[unsignedArrayLength];
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toUInt16);
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toUInt32);
            DataTypeExtensions.Convert(sourceUInt8, destinationUInt8toUInt64);
            DataTypeExtensions.Convert(sourceUInt16, destinationUInt16toUInt32);
            DataTypeExtensions.Convert(sourceUInt16, destinationUInt16toUInt64);
            DataTypeExtensions.Convert(sourceUInt32, destinationUInt32toUInt64);

            for (sbyte sign = -1, signedIndex = 0; signedIndex < signedArrayLength; sign *= -1, ++signedIndex)
            {
                sbyte value = (sbyte)(sign * signedIndex);
                Assert.IsTrue(destinationInt8toInt16[signedIndex] == value);
                Assert.IsTrue(destinationInt8toInt32[signedIndex] == value);
                Assert.IsTrue(destinationInt8toInt64[signedIndex] == value);
                Assert.IsTrue(destinationInt16toInt32[signedIndex] == value);
                Assert.IsTrue(destinationInt16toInt64[signedIndex] == value);
                Assert.IsTrue(destinationInt32toInt64[signedIndex] == value);
            }

            for (byte unsignedIndex = 0; unsignedIndex < unsignedArrayLength; ++unsignedIndex)
            {
                Assert.IsTrue(destinationUInt8toInt16[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt8toInt32[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt8toInt64[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt16toInt32[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt16toInt64[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt32toInt64[unsignedIndex] == unsignedIndex);

                Assert.IsTrue(destinationUInt8toUInt16[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt8toUInt32[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt8toUInt64[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt16toUInt32[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt16toUInt64[unsignedIndex] == unsignedIndex);
                Assert.IsTrue(destinationUInt32toUInt64[unsignedIndex] == unsignedIndex);
            }
        }

        [TestMethod]
        public void RasterBandStatistics()
        {
            // floating point test data
            double[] doubles = new double[8191];
            int value = -doubles.Length / 2;
            for (int index = 0; index < doubles.Length; ++index, ++value)
            {
                doubles[index] = value != 0 ? (float)value : RasterBand.NoDataDefaultDouble;
            }
            doubles.RandomizeOrder();

            float[] floats = new float[4099];
            value = -floats.Length / 2;
            for (int index = 0; index < floats.Length; ++index, ++value)
            {
                floats[index] = value != 0 ? (float)value : RasterBand.NoDataDefaultFloat;
            }
            floats.RandomizeOrder();

            // signed integer test data
            sbyte[] int8s = new sbyte[191];
            sbyte int8 = (sbyte)(-int8s.Length / 2);
            for (byte index = 0; index < int8s.Length; ++index, ++int8)
            {
                int8s[index] = int8 != 0 ? int8 : RasterBand.NoDataDefaultSByte; // inject one no data value
            }
            int8s.RandomizeOrder(); // randomize so each test execution checks for sensitivity to data order

            Int16[] int16s = new Int16[317];
            Int16 int16 = (Int16)(-int16s.Length / 2);
            for (Int16 index = 0; index < int16s.Length; ++index, ++int16)
            {
                int16s[index] = int16 != 0 ? int16 : RasterBand.NoDataDefaultInt16;
            }
            int16s.RandomizeOrder();

            Int32[] int32s = new Int32[1103];
            Int32 int32 = -int32s.Length / 2;
            for (int index = 0; index < int32s.Length; ++index, ++int32)
            {
                int32s[index] = int32 != 0 ? int32 : RasterBand.NoDataDefaultInt32;
            }
            int32s.RandomizeOrder();

            Int64[] int64s = new Int64[1571];
            Int64 int64 = -int64s.Length / 2;
            for (int index = 0; index < int64s.Length; ++index, ++int64)
            {
                int64s[index] = int64 != 0 ? int64 : RasterBand.NoDataDefaultInt64;
            }
            int64s.RandomizeOrder();

            // unsigned integer test data
            byte[] uint8s = new byte[251];
            uint8s[0] = RasterBand.NoDataDefaultByte;
            for (byte index = 1; index < uint8s.Length; ++index)
            {
                uint8s[index] = index;
            }
            uint8s.RandomizeOrder();

            UInt16[] uint16s = new UInt16[439];
            uint16s[0] = RasterBand.NoDataDefaultUInt16;
            for (UInt16 index = 1; index < uint16s.Length; ++index)
            {
                uint16s[index] = index;
            }
            uint16s.RandomizeOrder();

            UInt32[] uint32s = new UInt32[509];
            uint32s[0] = RasterBand.NoDataDefaultUInt32;
            for (UInt32 index = 1; index < uint32s.Length; ++index)
            {
                uint32s[index] = index;
            }
            uint32s.RandomizeOrder();

            UInt64[] uint64s = new UInt64[1477];
            uint64s[0] = RasterBand.NoDataDefaultUInt64;
            for (UInt32 index = 1; index < uint64s.Length; ++index)
            {
                uint64s[index] = index;
            }
            uint64s.RandomizeOrder();

            // get statistics
            RasterBandStatistics floatStats = new(floats, hasNoData: true, noDataValue: RasterBand.NoDataDefaultFloat);
            RasterBandStatistics doubleStats = new(doubles, hasNoData: true, noDataValue: RasterBand.NoDataDefaultDouble);

            RasterBandStatistics int8stats = new(int8s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultSByte);
            RasterBandStatistics int16stats = new(int16s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultInt16);
            RasterBandStatistics int32stats = new(int32s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultInt32);
            RasterBandStatistics int64stats = new(int64s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultInt64);

            RasterBandStatistics uint8stats = new(uint8s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultByte);
            RasterBandStatistics uint16stats = new(uint16s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultUInt16);
            RasterBandStatistics uint32stats = new(uint32s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultUInt32);
            RasterBandStatistics uint64stats = new(uint64s, hasNoData: true, noDataValue: RasterBand.NoDataDefaultUInt64);

            // check statistics
            SimdTests.VerifyFloatingPointStatistics(doubles, 2364.5380098446294, doubleStats);
            SimdTests.VerifyFloatingPointStatistics(floats, 1183.2793414912642, floatStats);
            
            SimdTests.VerifySignedStatistics(int8s, 55.136195008360886, int8stats);
            SimdTests.VerifySignedStatistics(int16s, 91.509562341866769, int16stats);
            SimdTests.VerifySignedStatistics(int32s, 318.40854259896986, int32stats);
            SimdTests.VerifySignedStatistics(int64s, 453.50854457220538, int64stats);

            SimdTests.VerifyUnsignedStatistics(uint8s, 72.4568837309472, uint8stats);
            SimdTests.VerifyUnsignedStatistics(uint16s, 126.7280552995271, uint16stats);
            SimdTests.VerifyUnsignedStatistics(uint32s, 146.935359937627, uint32stats);
            SimdTests.VerifyUnsignedStatistics(uint64s, 426.373076073056, uint64stats);
        }

        private static void VerifyFloatingPointStatistics<TBand>(TBand[] data, double standardDeviation, RasterBandStatistics stats) where TBand : IFloatingPoint<TBand>
        {
            Assert.IsTrue((stats.CellsSampled == data.Length) && (stats.NoDataCells == 1) && (stats.GetDataFraction() == (double)(data.Length - 1) / (double)data.Length));
            Assert.IsTrue((stats.Minimum == -0.5 * (data.Length - 1)) && (stats.Mean == 0.0) && (stats.Maximum == 0.5 * (data.Length - 1)) && (stats.StandardDeviation == standardDeviation));
        }

        private static void VerifySignedStatistics<TBand>(TBand[] data, double standardDeviation, RasterBandStatistics stats) where TBand : ISignedNumber<TBand>
        {
            Assert.IsTrue((stats.CellsSampled == data.Length) && (stats.NoDataCells == 1) && (stats.GetDataFraction() == (double)(data.Length - 1) / (double)data.Length));
            Assert.IsTrue((stats.Minimum == -0.5 * (data.Length - 1)) && (stats.Mean == 0.0) && (stats.Maximum == 0.5 * (data.Length - 1)) && (stats.StandardDeviation == standardDeviation));
        }

        private static void VerifyUnsignedStatistics<TBand>(TBand[] data, double standardDeviation, RasterBandStatistics stats) where TBand : IUnsignedNumber<TBand>
        {
            Assert.IsTrue((stats.CellsSampled == data.Length) && (stats.NoDataCells == 1) && (stats.GetDataFraction() == (double)(data.Length - 1) / (double)data.Length));
            Assert.IsTrue((stats.Minimum == 1.0) && (stats.Mean == 0.5 * (data.Length - 1)) && (stats.Maximum == data.Length - 1) && (stats.StandardDeviation == standardDeviation));
        }
    }
}
