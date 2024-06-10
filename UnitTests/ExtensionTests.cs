using Mars.Clouds.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class ExtensionTests
    {
        [TestMethod]
        public void Int64()
        {
            Int64 minMin = Int64Extensions.Pack(Int32.MinValue, Int32.MinValue);
            Int64 minMax = Int64Extensions.Pack(Int32.MinValue, Int32.MaxValue);
            Int64 zeroZero = Int64Extensions.Pack(0, 0);
            Int64 maxMin = Int64Extensions.Pack(Int32.MaxValue, Int32.MinValue);
            Int64 maxMax = Int64Extensions.Pack(Int32.MaxValue, Int32.MaxValue);

            Assert.IsTrue((minMin.GetUpperInt32() == Int32.MinValue) && (minMin.GetLowerInt32() == Int32.MinValue));
            Assert.IsTrue((minMax.GetUpperInt32() == Int32.MinValue) && (minMax.GetLowerInt32() == Int32.MaxValue));
            Assert.IsTrue((zeroZero.GetUpperInt32() == 0) && (zeroZero.GetLowerInt32() == 0));
            Assert.IsTrue((maxMin.GetUpperInt32() == Int32.MaxValue) && (maxMin.GetLowerInt32() == Int32.MinValue));
            Assert.IsTrue((maxMax.GetUpperInt32() == Int32.MaxValue) && (maxMax.GetLowerInt32() == Int32.MaxValue));
        }
    }
}
