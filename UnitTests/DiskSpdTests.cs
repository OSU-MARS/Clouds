using Mars.Clouds.DiskSpd;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class DiskSpdTests : CloudTest
    {
        [TestMethod]
        public void Read() 
        {
            Results diskSpdResults = new(Path.Combine(this.UnitTestPath, "DiskSpd SEQ1M Q8T2 mix 7030 results.xml"));
            // diskSpdResults.System not currently checked
            // diskSpdResults.Profile not currently checked
            Assert.IsTrue(diskSpdResults.TimeSpan.TestTimeSeconds == 30.00F);
            Assert.IsTrue(diskSpdResults.TimeSpan.ThreadCount == 4);
            // diskSpdResults.TimeSpan.RequestCount not currently checked
            // diskSpdResults.TimeSpan.ProcCount not currently checked
            // diskSpdResults.TimeSpan.CpuUtilization not currently checked
            // diskSpdResults.TimeSpan.Latency not currently checked
            // diskSpdResults.TimeSpan.Iops not currently checked
            Assert.IsTrue(diskSpdResults.TimeSpan.Threads.Count == 4);

            ThreadTarget target0 = diskSpdResults.TimeSpan.Threads[0].Target;
            Assert.IsTrue(target0.ReadBytes == 1213202432L);
            Assert.IsTrue(target0.WriteBytes == 527433728L);
            Assert.IsTrue(target0.AverageReadLatencyMilliseconds == 139.327F);
            Assert.IsTrue(target0.AverageWriteLatencyMilliseconds == 158.178F);

            ThreadTarget target1 = diskSpdResults.TimeSpan.Threads[1].Target;
            Assert.IsTrue(target1.ReadBytes == 1194328064L);
            Assert.IsTrue(target1.WriteBytes == 547356672);
            Assert.IsTrue(target1.AverageReadLatencyMilliseconds == 137.532F);
            Assert.IsTrue(target1.AverageWriteLatencyMilliseconds == 161.036F);

            ThreadTarget target2 = diskSpdResults.TimeSpan.Threads[2].Target;
            Assert.IsTrue(target2.ReadBytes == 1310720000L);
            Assert.IsTrue(target2.WriteBytes == 600834048L);
            Assert.IsTrue(target2.AverageReadLatencyMilliseconds == 126.608F);
            Assert.IsTrue(target2.AverageWriteLatencyMilliseconds == 142.784F);

            ThreadTarget target3 = diskSpdResults.TimeSpan.Threads[3].Target;
            Assert.IsTrue(target3.ReadBytes == 1351614464L);
            Assert.IsTrue(target3.WriteBytes == 564133888L);
            Assert.IsTrue(target3.AverageReadLatencyMilliseconds == 126.051F);
            Assert.IsTrue(target3.AverageWriteLatencyMilliseconds == 144.038F);
        }
    }
}
