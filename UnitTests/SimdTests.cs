using Mars.Clouds.GdalExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

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
            for (sbyte signedIndex = 0; signedIndex < signedArrayLength; ++signedIndex)
            {
                sourceInt8[signedIndex] = signedIndex;
                sourceInt16[signedIndex] = signedIndex;
                sourceInt32[signedIndex] = signedIndex;
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

            for (sbyte signedIndex = 0; signedIndex < signedArrayLength; ++signedIndex)
            {
                Assert.IsTrue(destinationInt8toInt16[signedIndex] == signedIndex);
                Assert.IsTrue(destinationInt8toInt32[signedIndex] == signedIndex);
                Assert.IsTrue(destinationInt8toInt64[signedIndex] == signedIndex);
                Assert.IsTrue(destinationInt16toInt32[signedIndex] == signedIndex);
                Assert.IsTrue(destinationInt16toInt64[signedIndex] == signedIndex);
                Assert.IsTrue(destinationInt32toInt64[signedIndex] == signedIndex);
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
    }
}
