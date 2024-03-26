using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class CloudTest
    {
        protected string UnitTestPath { get; private set; }

        public TestContext? TestContext { get; set; }

        protected CloudTest() 
        { 
            this.UnitTestPath = String.Empty;
        }

        [TestInitialize]
        public void TestInitialize()
        {
            this.UnitTestPath = Path.Combine(this.TestContext!.TestRunDirectory!, "..\\..\\UnitTests");
        }
    }
}
