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
        public void ConvertToWiderTypes()
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
        public void PackToNarrowerTypes()
        {
            Int16[] sourceInt16 = [ Int16.MinValue, Int16.MinValue + 1, Int16.MinValue + 2, Int16.MinValue + 3, SByte.MinValue, SByte.MinValue + 1, SByte.MinValue + 2, SByte.MinValue + 3, 
                                    -4, -3, -2, -1, 0, 1, 2, 3,
                                    Int16.MaxValue, Int16.MaxValue - 1, Int16.MaxValue - 2, Int16.MaxValue - 3, SByte.MaxValue, SByte.MaxValue - 1, SByte.MaxValue - 2, SByte.MaxValue - 3, 
                                    SByte.MaxValue - 4, SByte.MaxValue - 5, SByte.MaxValue - 6, SByte.MaxValue - 7, SByte.MaxValue - 8, SByte.MaxValue - 9, SByte.MaxValue - 10, SByte.MaxValue - 11, 
                                    Int16.MinValue, SByte.MinValue, 0, SByte.MaxValue, Int16.MaxValue ];
            Int32[] sourceInt32 = [ Int32.MinValue, Int16.MinValue, Byte.MinValue, Int16.MinValue + 2, Int16.MinValue + 3, -1, 0, 1, 
                                    Int32.MaxValue, Int16.MaxValue, Byte.MaxValue, Int16.MaxValue - 1, SByte.MaxValue - 1, Int16.MaxValue - 2, SByte.MaxValue - 2, Int16.MaxValue - 3,
                                    Int16.MinValue, SByte.MinValue, 0, Int16.MaxValue, SByte.MaxValue ];
            Int64[] sourceInt64 = [ Int64.MinValue, Int32.MinValue, Int16.MinValue, Byte.MinValue + 2, Int32.MinValue + 3, -1, 0, 1, 
                                    Int64.MaxValue, Int32.MaxValue, Int16.MaxValue, Byte.MaxValue, Int32.MaxValue - 1, Int16.MaxValue - 1, SByte.MaxValue - 1, Int32.MaxValue - 2, 
                                    Int32.MinValue, Int16.MinValue, SByte.MinValue, 0, Int32.MaxValue, Int16.MaxValue, SByte.MaxValue ];

            UInt16[] sourceUInt16 = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                                      UInt16.MaxValue, Byte.MaxValue, Byte.MaxValue - 1, Byte.MaxValue - 2, Byte.MaxValue - 3, Byte.MaxValue - 4, Byte.MaxValue - 5, Byte.MaxValue - 6, 
                                      UInt16.MaxValue - 1, Byte.MaxValue - 7, Byte.MaxValue - 8, Byte.MaxValue - 9, Byte.MaxValue - 10, Byte.MaxValue - 11, Byte.MaxValue - 12, Byte.MaxValue - 13,
                                      0, 1, 2, 3, 4, UInt16.MaxValue, UInt16.MaxValue - 1, Byte.MaxValue, Byte.MaxValue - 1 ];
            UInt32[] sourceUInt32 = [ 0, 1, 2, 3, 4, 5, 6, 7, UInt32.MaxValue, UInt16.MaxValue, Byte.MaxValue, UInt16.MaxValue - 1, Byte.MaxValue - 1, UInt16.MaxValue - 2, Byte.MaxValue - 2, UInt16.MaxValue - 3,
                                      0, UInt16.MaxValue, Byte.MaxValue ];
            UInt64[] sourceUInt64 = [ 0, 1, 2, 3, 4, 5, 6, 7, UInt64.MaxValue, UInt32.MaxValue, UInt16.MaxValue, Byte.MaxValue, UInt32.MaxValue - 1, UInt16.MaxValue - 1, Byte.MaxValue - 1, UInt32.MaxValue - 2, 
                                      0, UInt32.MaxValue, UInt16.MaxValue, Byte.MaxValue ];

            sourceInt16.RandomizeOrder();
            sourceInt32.RandomizeOrder();
            sourceInt64.RandomizeOrder();
            sourceUInt16.RandomizeOrder();
            sourceUInt32.RandomizeOrder();
            sourceUInt64.RandomizeOrder();

            // signed packs
            // { 16, 32, 64 } -> 8
            sbyte[] destinationInt16toInt8noDataSaturating = new sbyte[sourceInt16.Length];
            DataTypeExtensions.Pack(sourceInt16, destinationInt16toInt8noDataSaturating, noDataSaturatingFromBelow: true);
            sbyte[] destinationInt16toInt8noDataUnsaturating = new sbyte[sourceInt16.Length];
            DataTypeExtensions.Pack(sourceInt16, destinationInt16toInt8noDataUnsaturating, noDataSaturatingFromBelow: false);

            sbyte[] destinationInt32toInt8noDataSaturating = new sbyte[sourceInt32.Length];
            DataTypeExtensions.Pack(sourceInt32, destinationInt32toInt8noDataSaturating, noDataSaturatingFromBelow: true);
            sbyte[] destinationInt32toInt8noDataUnsaturating = new sbyte[sourceInt32.Length];
            DataTypeExtensions.Pack(sourceInt32, destinationInt32toInt8noDataUnsaturating, noDataSaturatingFromBelow: false);

            sbyte[] destinationInt64toInt8noDataSaturating = new sbyte[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt8noDataSaturating, noDataSaturatingFromBelow: true);
            sbyte[] destinationInt64toInt8noDataUnsaturating = new sbyte[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt8noDataUnsaturating, noDataSaturatingFromBelow: false);

            // { 32, 64 } -> 16
            Int16[] destinationInt32toInt16noDataSaturating = new Int16[sourceInt32.Length];
            DataTypeExtensions.Pack(sourceInt32, destinationInt32toInt16noDataSaturating, noDataSaturatingFromBelow: true);
            Int16[] destinationInt32toInt16noDataUnsaturating = new Int16[sourceInt32.Length];
            DataTypeExtensions.Pack(sourceInt32, destinationInt32toInt16noDataUnsaturating, noDataSaturatingFromBelow: false);

            Int16[] destinationInt64toInt16noDataSaturating = new Int16[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt16noDataSaturating, noDataSaturatingFromBelow: true);
            Int16[] destinationInt64toInt16noDataUnsaturating = new Int16[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt16noDataUnsaturating, noDataSaturatingFromBelow: false);


            // 64 -> 32
            Int32[] destinationInt64toInt32noDataSaturating = new Int32[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt32noDataSaturating, noDataSaturatingFromBelow: true);
            Int32[] destinationInt64toInt32noDataUnsaturating = new Int32[sourceInt64.Length];
            DataTypeExtensions.Pack(sourceInt64, destinationInt64toInt32noDataUnsaturating, noDataSaturatingFromBelow: false);

            // unsigned packs
            // { 16, 32, 64 } -> 8
            byte[] destinationUInt16toUInt8noDataSaturating = new byte[sourceUInt16.Length];
            DataTypeExtensions.Pack(sourceUInt16, destinationUInt16toUInt8noDataSaturating, noDataSaturatingFromAbove: true);
            byte[] destinationUInt16toUInt8noDataUnsaturating = new byte[sourceUInt16.Length];
            DataTypeExtensions.Pack(sourceUInt16, destinationUInt16toUInt8noDataUnsaturating, noDataSaturatingFromAbove: false);

            byte[] destinationUInt32toUInt8noDataSaturating = new byte[sourceUInt32.Length];
            DataTypeExtensions.Pack(sourceUInt32, destinationUInt32toUInt8noDataSaturating, noDataSaturatingFromAbove: true);
            byte[] destinationUInt32toUInt8noDataUnsaturating = new byte[sourceUInt32.Length];
            DataTypeExtensions.Pack(sourceUInt32, destinationUInt32toUInt8noDataUnsaturating, noDataSaturatingFromAbove: false);

            byte[] destinationUInt64toUInt8noDataSaturating = new byte[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt8noDataSaturating, noDataSaturatingFromAbove: true);
            byte[] destinationUInt64toUInt8noDataUnsaturating = new byte[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt8noDataUnsaturating, noDataSaturatingFromAbove: false);

            // { 32, 64 } -> 16
            UInt16[] destinationUInt32toUInt16noDataSaturating = new UInt16[sourceUInt32.Length];
            DataTypeExtensions.Pack(sourceUInt32, destinationUInt32toUInt16noDataSaturating, noDataSaturatingFromAbove: true);
            UInt16[] destinationUInt32toUInt16noDataUnsaturating = new UInt16[sourceUInt32.Length];
            DataTypeExtensions.Pack(sourceUInt32, destinationUInt32toUInt16noDataUnsaturating, noDataSaturatingFromAbove: false);

            UInt16[] destinationUInt64toUInt16noDataSaturating = new UInt16[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt16noDataSaturating, noDataSaturatingFromAbove: true);
            UInt16[] destinationUInt64toUInt16noDataUnsaturating = new UInt16[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt16noDataUnsaturating, noDataSaturatingFromAbove: false);

            // 64 -> 32
            UInt32[] destinationUInt64toUInt32noDataSaturating = new UInt32[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt32noDataSaturating, noDataSaturatingFromAbove: true);
            UInt32[] destinationUInt64toUInt32noDataUnsaturating = new UInt32[sourceUInt64.Length];
            DataTypeExtensions.Pack(sourceUInt64, destinationUInt64toUInt32noDataUnsaturating, noDataSaturatingFromAbove: false);

            // signed validation
            // Int16 -> 8
            for (int index = 0; index < sourceInt16.Length; ++index)
            {
                Int16 valueInt16 = sourceInt16[index];

                sbyte expectedValueInt16toInt8noDataSaturating = valueInt16 == SByte.MinValue ? (sbyte)(SByte.MinValue + 1) : SByte.CreateSaturating(valueInt16);
                sbyte valueInt16toInt8noDataSaturating = destinationInt16toInt8noDataSaturating[index];
                sbyte expectedValueInt16toInt8noDataUnsaturating = SByte.CreateSaturating(valueInt16);
                sbyte valueInt16toInt8noDataUnsaturating = destinationInt16toInt8noDataUnsaturating[index];

                Assert.IsTrue((expectedValueInt16toInt8noDataSaturating == valueInt16toInt8noDataSaturating) && (expectedValueInt16toInt8noDataUnsaturating == valueInt16toInt8noDataUnsaturating));
            }

            // Int32 -> { 16, 8 }
            for (int index = 0; index < sourceInt32.Length; ++index)
            {
                Int32 valueInt32 = sourceInt32[index];

                sbyte expectedValueInt32toInt8noDataSaturating = valueInt32 == SByte.MinValue ? (sbyte)(SByte.MinValue + 1) : SByte.CreateSaturating(valueInt32);
                sbyte valueInt32toInt8noDataSaturating = destinationInt32toInt8noDataSaturating[index];
                sbyte expectedValueInt32toInt8noDataUnsaturating = SByte.CreateSaturating(valueInt32);
                sbyte valueInt32toInt8noDataUnsaturating = destinationInt32toInt8noDataUnsaturating[index];

                Int16 expectedValueInt32toInt16noDataSaturating = valueInt32 == Int16.MinValue ? (Int16)(Int16.MinValue + 1) : Int16.CreateSaturating(valueInt32);
                Int16 valueInt32toInt16noDataSaturating = destinationInt32toInt16noDataSaturating[index];
                Int16 expectedValueInt32toInt16noDataUnsaturating = Int16.CreateSaturating(valueInt32);
                Int16 valueInt32toInt16noDataUnsaturating = destinationInt32toInt16noDataUnsaturating[index];

                Assert.IsTrue((expectedValueInt32toInt8noDataSaturating == valueInt32toInt8noDataSaturating) && (expectedValueInt32toInt8noDataUnsaturating == valueInt32toInt8noDataUnsaturating));
                Assert.IsTrue((expectedValueInt32toInt16noDataSaturating == valueInt32toInt16noDataSaturating) && (expectedValueInt32toInt16noDataUnsaturating == valueInt32toInt16noDataUnsaturating));
            }

            // Int64 -> { 32, 16, 8 }
            for (int index = 0; index < sourceInt64.Length; ++index)
            {
                Int64 valueInt64 = sourceInt64[index];

                sbyte expectedValueInt64toInt8noDataSaturating = valueInt64 == SByte.MinValue ? (sbyte)(SByte.MinValue + 1) : SByte.CreateSaturating(valueInt64);
                sbyte valueInt64toInt8noDataSaturating = destinationInt64toInt8noDataSaturating[index];
                sbyte expectedValueInt64toInt8noDataUnsaturating = SByte.CreateSaturating(valueInt64);
                sbyte valueInt64toInt8noDataUnsaturating = destinationInt64toInt8noDataUnsaturating[index];

                Int16 expectedValueInt64toInt16noDataSaturating = valueInt64 == Int16.MinValue ? (Int16)(Int16.MinValue + 1) : Int16.CreateSaturating(valueInt64);
                Int16 valueInt64toInt16noDataSaturating = destinationInt64toInt16noDataSaturating[index];
                Int16 expectedValueInt64toInt16noDataUnsaturating = Int16.CreateSaturating(valueInt64);
                Int16 valueInt64toInt16noDataUnsaturating = destinationInt64toInt16noDataUnsaturating[index];

                Int32 expectedValueInt64toInt32noDataSaturating = valueInt64 == Int32.MinValue ? Int32.MinValue + 1 : Int32.CreateSaturating(valueInt64);
                Int32 valueInt64toInt32noDataSaturating = destinationInt64toInt32noDataSaturating[index];
                Int32 expectedValueInt64toInt32noDataUnsaturating = Int32.CreateSaturating(valueInt64);
                Int32 valueInt64toInt32noDataUnsaturating = destinationInt64toInt32noDataUnsaturating[index];

                Assert.IsTrue((expectedValueInt64toInt8noDataSaturating == valueInt64toInt8noDataSaturating) && (expectedValueInt64toInt8noDataUnsaturating == valueInt64toInt8noDataUnsaturating));
                Assert.IsTrue((expectedValueInt64toInt16noDataSaturating == valueInt64toInt16noDataSaturating) && (expectedValueInt64toInt16noDataUnsaturating == valueInt64toInt16noDataUnsaturating));
                Assert.IsTrue((expectedValueInt64toInt32noDataSaturating == valueInt64toInt32noDataSaturating) && (expectedValueInt64toInt32noDataUnsaturating == valueInt64toInt32noDataUnsaturating));
            }

            // unsigned validation
            // UInt16 -> 8
            for (int index = 0; index < sourceUInt16.Length; ++index)
            {
                UInt16 valueUInt16 = sourceUInt16[index];

                byte expectedValueUInt16toUInt8noDataSaturating = valueUInt16 == Byte.MaxValue ? (byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(valueUInt16);
                byte valueUInt16toUInt8noDataSaturating = destinationUInt16toUInt8noDataSaturating[index];
                byte expectedValueUInt16toUInt8noDataUnsaturating = Byte.CreateSaturating(valueUInt16);
                byte valueUInt16toUInt8noDataUnsaturating = destinationUInt16toUInt8noDataUnsaturating[index];

                Assert.IsTrue((expectedValueUInt16toUInt8noDataSaturating == valueUInt16toUInt8noDataSaturating) && (expectedValueUInt16toUInt8noDataUnsaturating == valueUInt16toUInt8noDataUnsaturating));
            }

            // UInt32 -> { 16, 8 }
            for (int index = 0; index < sourceUInt32.Length; ++index)
            {
                UInt32 valueUInt32 = sourceUInt32[index];

                byte expectedValueUInt32toUInt8noDataSaturating = valueUInt32 == Byte.MaxValue ? (byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(valueUInt32);
                byte valueUInt32toUInt8noDataSaturating = destinationUInt32toUInt8noDataSaturating[index];
                byte expectedValueUInt32toUInt8noDataUnsaturating = Byte.CreateSaturating(valueUInt32);
                byte valueUInt32toUInt8noDataUnsaturating = destinationUInt32toUInt8noDataUnsaturating[index];

                UInt16 expectedValueUInt32toUInt16noDataSaturating = valueUInt32 == UInt16.MaxValue ? (UInt16)(UInt16.MaxValue - 1) : UInt16.CreateSaturating(valueUInt32);
                UInt16 valueUInt32toUInt16noDataSaturating = destinationUInt32toUInt16noDataSaturating[index];
                UInt16 expectedValueUInt32toUInt16noDataUnsaturating = UInt16.CreateSaturating(valueUInt32);
                UInt16 valueUInt32toUInt16noDataUnsaturating = destinationUInt32toUInt16noDataUnsaturating[index];
                
                Assert.IsTrue((expectedValueUInt32toUInt8noDataSaturating == valueUInt32toUInt8noDataSaturating) && (expectedValueUInt32toUInt8noDataUnsaturating == valueUInt32toUInt8noDataUnsaturating));
                Assert.IsTrue((expectedValueUInt32toUInt16noDataSaturating == valueUInt32toUInt16noDataSaturating) && (expectedValueUInt32toUInt16noDataUnsaturating == valueUInt32toUInt16noDataUnsaturating));

            }

            // UInt64 -> { 32, 16, 8 }
            for (int index = 0; index < sourceUInt64.Length; ++index)
            {
                UInt64 valueUInt64 = sourceUInt64[index];

                byte expectedValueUInt64toUInt8noDataSaturating = valueUInt64 == Byte.MaxValue ? (byte)(Byte.MaxValue - 1) : Byte.CreateSaturating(valueUInt64);
                byte valueUInt64toUInt8noDataSaturating = destinationUInt64toUInt8noDataSaturating[index];
                byte expectedValueUInt64toUInt8noDataUnsaturating = Byte.CreateSaturating(valueUInt64);
                byte valueUInt64toUInt8noDataUnsaturating = destinationUInt64toUInt8noDataUnsaturating[index];

                UInt16 expectedValueUInt64toUInt16noDataSaturating = valueUInt64 == UInt16.MaxValue ? (UInt16)(UInt16.MaxValue - 1) : UInt16.CreateSaturating(valueUInt64);
                UInt16 valueUInt64toUInt16noDataSaturating = destinationUInt64toUInt16noDataSaturating[index];
                UInt16 expectedValueUInt64toUInt16noDataUnsaturating = UInt16.CreateSaturating(valueUInt64);
                UInt16 valueUInt64toUInt16noDataUnsaturating = destinationUInt64toUInt16noDataUnsaturating[index];

                UInt32 expectedValueUInt64toUInt32noDataSaturating = valueUInt64 == UInt32.MaxValue ? UInt32.MaxValue - 1 : UInt32.CreateSaturating(valueUInt64);
                UInt32 valueUInt64toUInt32noDataSaturating = destinationUInt64toUInt32noDataSaturating[index];
                UInt32 expectedValueUInt64toUInt32noDataUnsaturating = UInt32.CreateSaturating(valueUInt64);
                UInt32 valueUInt64toUInt32noDataUnsaturating = destinationUInt64toUInt32noDataUnsaturating[index];

                Assert.IsTrue((expectedValueUInt64toUInt8noDataSaturating == valueUInt64toUInt8noDataSaturating) && (expectedValueUInt64toUInt8noDataUnsaturating == valueUInt64toUInt8noDataUnsaturating));
                Assert.IsTrue((expectedValueUInt64toUInt16noDataSaturating == valueUInt64toUInt16noDataSaturating) && (expectedValueUInt64toUInt16noDataUnsaturating == valueUInt64toUInt16noDataUnsaturating));
                Assert.IsTrue((expectedValueUInt64toUInt32noDataSaturating == valueUInt64toUInt32noDataSaturating) && (expectedValueUInt64toUInt32noDataUnsaturating == valueUInt64toUInt32noDataUnsaturating));
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
            SimdTests.VerifyFloatingPointStatistics(doubles, 2364.6823606283078, doubleStats);
            SimdTests.VerifyFloatingPointStatistics(floats, 1183.4237054692908, floatStats);
            
            SimdTests.VerifySignedStatistics(int8s, 55.281099844341014, int8stats);
            SimdTests.VerifySignedStatistics(int16s, 91.654241582154839, int16stats);
            SimdTests.VerifySignedStatistics(int32s, 318.55297832542703, int32stats);
            SimdTests.VerifySignedStatistics(int64s, 453.65295105399679, int64stats);

            SimdTests.VerifyUnsignedStatistics(uint8s, 72.168206296124609, uint8stats);
            SimdTests.VerifyUnsignedStatistics(uint16s, 126.43937941427372, uint16stats);
            SimdTests.VerifyUnsignedStatistics(uint32s, 146.64668424482022, uint32stats);
            SimdTests.VerifyUnsignedStatistics(uint64s, 426.08440087225279, uint64stats);
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
            Assert.IsTrue((stats.Minimum == 1.0) && (stats.Mean == 0.5 * data.Length) && (stats.Maximum == data.Length - 1) && (stats.StandardDeviation == standardDeviation));
        }
    }
}
