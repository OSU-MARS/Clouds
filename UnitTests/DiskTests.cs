using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.DiskSpd;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class DiskTests : CloudTest
    {
        [TestMethod]
        public void FixedDriveCapabilities()
        {
            // sanity test of whatever fixed local drives are available
            // Assumes, for now, no SAS drives.
            DriveInfo[] drives = DriveInfo.GetDrives();
            List<string> drivePaths = [];
            for (int driveIndex = 0; driveIndex < drives.Length; ++driveIndex)
            {
                DriveInfo drive = drives[driveIndex];
                if (drive.DriveType == DriveType.Fixed)
                {
                    drivePaths.Add(drive.RootDirectory.Name); // includes trailing backslash
                }
            }

            HardwareCapabilities capabilities = HardwareCapabilities.Current;
            Assert.IsTrue((4 <= capabilities.PhysicalCores) && (capabilities.PhysicalCores <= 32));
            Assert.IsTrue((17.06F < capabilities.DdrBandwidthInGBs) && (capabilities.DdrBandwidthInGBs < 512.0F)); // sanity check assuming at least one DDR4 channel at 2133 MT/s or equivalent
            int totalFixedDisks = capabilities.PhysicalDisksByRoot.Count + capabilities.VirtualDisksByRoot.Count;
            Assert.IsTrue((0 < totalFixedDisks) && (totalFixedDisks <= drivePaths.Count)); // can have multiple volumes per disk but not more volumes than drive paths
            for (int diskIndex = 0; diskIndex < capabilities.PhysicalDisksByRoot.Count; ++diskIndex)
            {
                PhysicalDisk physicalDisk = capabilities.PhysicalDisksByRoot.Values[diskIndex];
                DiskTests.ValidatePhysicalDisk(physicalDisk);
            }
            for (int spaceIndex = 0; spaceIndex < capabilities.VirtualDisksByRoot.Count; ++spaceIndex)
            {
                VirtualDisk virtualDisk = capabilities.VirtualDisksByRoot.Values[spaceIndex];
                DiskTests.ValidateVirtualDisk(virtualDisk);
            }

            int readThreads = capabilities.GetPracticalReadThreadCount(drivePaths, 2.0F, 4.5F * 2.0F);
            Assert.IsTrue(readThreads > 0);
        }

        [TestMethod]
        public void HostSpecificHardwareCapabilities()
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            if ((drives.Length <= 5) || (drives[1].TotalSize != 1798944456704) || (drives[2].TotalSize != 4000768323584))
            {
                return;
            }

            HardwareCapabilities capabilities = HardwareCapabilities.Current;
            PhysicalDisk c = capabilities.PhysicalDisksByRoot["C:\\"];
            PhysicalDisk d = capabilities.PhysicalDisksByRoot["D:\\"];
            PhysicalDisk e = capabilities.PhysicalDisksByRoot["E:\\"];
            PhysicalDisk f = capabilities.PhysicalDisksByRoot["F:\\"];
            PhysicalDisk g = capabilities.PhysicalDisksByRoot["G:\\"];
            Assert.IsTrue((c.BusType == BusType.NVMe) && (c.MediaType == MediaType.SolidStateDrive) && (c.PcieVersion == 3) && (c.PcieLanes == 4) && (c.GetEstimatedMaximumTransferRateInGBs() == 3.5F));
            Assert.IsTrue((d.BusType == BusType.NVMe) && (d.MediaType == MediaType.SolidStateDrive) && (d.PcieVersion == 4) && (d.PcieLanes == 4) && (d.GetEstimatedMaximumTransferRateInGBs() == 7.0F));
            Assert.IsTrue((e.BusType == BusType.SATA) && (e.MediaType == MediaType.HardDrive) && (e.PcieVersion == -1) && (e.PcieLanes == -1) && (e.GetEstimatedMaximumTransferRateInGBs() == HardwareCapabilities.HardDriveDefaultTransferRateInGBs));
            Assert.IsTrue((f.BusType == BusType.SATA) && (f.MediaType == MediaType.HardDrive) && (f.PcieVersion == -1) && (f.PcieLanes == -1) && (f.GetEstimatedMaximumTransferRateInGBs() == HardwareCapabilities.HardDriveDefaultTransferRateInGBs));
            Assert.IsTrue(Object.ReferenceEquals(g, f));

            int cThreads = capabilities.GetPracticalReadThreadCount(["C:\\"], 1.0F, 4.5F);
            int dThreads = capabilities.GetPracticalReadThreadCount(["D:\\"], 1.0F, 4.5F);
            int eThreads = capabilities.GetPracticalReadThreadCount(["E:\\"], 1.0F, 4.5F);
            int fgThreads = capabilities.GetPracticalReadThreadCount(["F:\\", "G:\\"], 1.0F, 4.5F);
            Assert.IsTrue((cThreads == 4) && (dThreads == 11) && (eThreads == 1) && (fgThreads == 1));
        }

        [TestMethod]
        public void ReadDiskSpdXmlToLongformResults() 
        {
            Results results = new(Path.Combine(this.UnitTestPath, "DiskSpd SEQ1M Q8T2 mix 7030 results.xml"));
            DiskSpdLongformResults longformResults = new([ results ]);

            // verify deserialization
            Assert.IsTrue(String.Equals(results.System.ComputerName, "disk-tester", StringComparison.Ordinal));
            // diskSpdResults.System.ProcessorTopology not currently checked
            // diskSpdResults.System.RunTime not currently checked
            // diskSpdResults.System.Tool not currently checked
            // diskSpdResults.Profile not currently checked
            Assert.IsTrue(results.TimeSpan.TestTimeSeconds == 30.00F);
            Assert.IsTrue(results.TimeSpan.ThreadCount == 4);
            // diskSpdResults.TimeSpan.RequestCount not currently checked
            // diskSpdResults.TimeSpan.ProcCount not currently checked
            // diskSpdResults.TimeSpan.CpuUtilization not currently checked
            // diskSpdResults.TimeSpan.Latency not currently checked
            // diskSpdResults.TimeSpan.Iops not currently checked
            Assert.IsTrue(results.TimeSpan.Threads.Count == 4);

            ThreadTarget target0 = results.TimeSpan.Threads[0].Target;
            Assert.IsTrue(target0.ReadBytes == 1213202432L);
            Assert.IsTrue(target0.ReadCount == 1157);
            Assert.IsTrue(target0.WriteBytes == 527433728L);
            Assert.IsTrue(target0.WriteCount == 503);
            Assert.IsTrue(target0.AverageReadLatencyMilliseconds == 139.327F);
            Assert.IsTrue(target0.AverageWriteLatencyMilliseconds == 158.178F);

            ThreadTarget target1 = results.TimeSpan.Threads[1].Target;
            Assert.IsTrue(target1.ReadBytes == 1194328064L);
            Assert.IsTrue(target1.ReadCount == 1139);
            Assert.IsTrue(target1.WriteBytes == 547356672);
            Assert.IsTrue(target1.WriteCount == 522);
            Assert.IsTrue(target1.AverageReadLatencyMilliseconds == 137.532F);
            Assert.IsTrue(target1.AverageWriteLatencyMilliseconds == 161.036F);

            ThreadTarget target2 = results.TimeSpan.Threads[2].Target;
            Assert.IsTrue(target2.ReadBytes == 1310720000L);
            Assert.IsTrue(target2.ReadCount == 1250);
            Assert.IsTrue(target2.WriteBytes == 600834048L);
            Assert.IsTrue(target2.WriteCount == 573);
            Assert.IsTrue(target2.AverageReadLatencyMilliseconds == 126.608F);
            Assert.IsTrue(target2.AverageWriteLatencyMilliseconds == 142.784F);

            ThreadTarget target3 = results.TimeSpan.Threads[3].Target;
            Assert.IsTrue(target3.ReadBytes == 1351614464L);
            Assert.IsTrue(target3.ReadCount == 1289);
            Assert.IsTrue(target3.WriteBytes == 564133888L);
            Assert.IsTrue(target3.WriteCount == 538);
            Assert.IsTrue(target3.AverageReadLatencyMilliseconds == 126.051F);
            Assert.IsTrue(target3.AverageWriteLatencyMilliseconds == 144.038F);

            // verify longform translation
            Assert.IsTrue(longformResults.Count == 4);

            Assert.IsTrue(String.Equals(longformResults.Host[0], "disk-tester", StringComparison.Ordinal));
            Assert.IsTrue(String.Equals(longformResults.TargetPath[0], "D:\\diskspd\\1GB.tmp", StringComparison.Ordinal));
            Assert.IsTrue(longformResults.RandomSize[0] == -1);
            Assert.IsTrue(longformResults.BlockSize[0] == 1048576);
            Assert.IsTrue(longformResults.QueueDepth[0] == 8);
            Assert.IsTrue(longformResults.ThreadsPerFile[0] == 2);
            Assert.IsTrue(longformResults.ThreadID[0] == 0);
            Assert.IsTrue(longformResults.DurationInS[0] == 30.0F);

            Assert.IsTrue(longformResults.ReadBytes[0] == 1213202432L);
            Assert.IsTrue(longformResults.WriteBytes[0] == 527433728L);
            Assert.IsTrue(longformResults.AverageReadLatencyMilliseconds[0] == 139.327F);
            Assert.IsTrue(longformResults.ReadLatencyStdev[0] == 206.970F);
            Assert.IsTrue(longformResults.AverageWriteLatencyMilliseconds[0] == 158.178F);
            Assert.IsTrue(longformResults.WriteLatencyStdev[0] == 214.636F);

            Assert.IsTrue(String.Equals(longformResults.Host[1], "disk-tester", StringComparison.Ordinal));
            Assert.IsTrue(String.Equals(longformResults.TargetPath[1], "D:\\diskspd\\1GB.tmp", StringComparison.Ordinal));
            Assert.IsTrue(longformResults.RandomSize[1] == -1);
            Assert.IsTrue(longformResults.BlockSize[1] == 1048576);
            Assert.IsTrue(longformResults.QueueDepth[1] == 8);
            Assert.IsTrue(longformResults.ThreadsPerFile[1] == 2);
            Assert.IsTrue(longformResults.ThreadID[1] == 1);
            Assert.IsTrue(longformResults.DurationInS[1] == 30.0F);

            Assert.IsTrue(longformResults.ReadBytes[1] == 1194328064L);
            Assert.IsTrue(longformResults.WriteBytes[1] == 547356672);
            Assert.IsTrue(longformResults.AverageReadLatencyMilliseconds[1] == 137.532F);
            Assert.IsTrue(longformResults.ReadLatencyStdev[1] == 208.460F);
            Assert.IsTrue(longformResults.AverageWriteLatencyMilliseconds[1] == 161.036F);
            Assert.IsTrue(longformResults.WriteLatencyStdev[1] == 226.541F);

            Assert.IsTrue(String.Equals(longformResults.Host[2], "disk-tester", StringComparison.Ordinal));
            Assert.IsTrue(String.Equals(longformResults.TargetPath[2], "E:\\diskspd\\1GB.tmp", StringComparison.Ordinal));
            Assert.IsTrue(longformResults.RandomSize[2] == -1);
            Assert.IsTrue(longformResults.BlockSize[2] == 1048576);
            Assert.IsTrue(longformResults.QueueDepth[2] == 8);
            Assert.IsTrue(longformResults.ThreadsPerFile[2] == 2);
            Assert.IsTrue(longformResults.ThreadID[2] == 2);
            Assert.IsTrue(longformResults.DurationInS[2] == 30.0F);

            Assert.IsTrue(longformResults.ReadBytes[2] == 1310720000L);
            Assert.IsTrue(longformResults.WriteBytes[2] == 600834048L);
            Assert.IsTrue(longformResults.AverageReadLatencyMilliseconds[2] == 126.608F);
            Assert.IsTrue(longformResults.ReadLatencyStdev[2] == 104.524F);
            Assert.IsTrue(longformResults.AverageWriteLatencyMilliseconds[2] == 142.784F);
            Assert.IsTrue(longformResults.WriteLatencyStdev[2] == 70.796F);

            Assert.IsTrue(String.Equals(longformResults.Host[3], "disk-tester", StringComparison.Ordinal));
            Assert.IsTrue(String.Equals(longformResults.TargetPath[3], "E:\\diskspd\\1GB.tmp", StringComparison.Ordinal));
            Assert.IsTrue(longformResults.RandomSize[3] == -1);
            Assert.IsTrue(longformResults.BlockSize[3] == 1048576);
            Assert.IsTrue(longformResults.QueueDepth[3] == 8);
            Assert.IsTrue(longformResults.ThreadsPerFile[3] == 2);
            Assert.IsTrue(longformResults.ThreadID[3] == 3);
            Assert.IsTrue(longformResults.DurationInS[3] == 30.0F);

            Assert.IsTrue(longformResults.ReadBytes[3] == 1351614464L);
            Assert.IsTrue(longformResults.WriteBytes[3] == 564133888L);
            Assert.IsTrue(longformResults.AverageReadLatencyMilliseconds[3] == 126.051F);
            Assert.IsTrue(longformResults.ReadLatencyStdev[3] == 100.735F);
            Assert.IsTrue(longformResults.AverageWriteLatencyMilliseconds[3] == 144.038F);
            Assert.IsTrue(longformResults.WriteLatencyStdev[3] == 76.912F);
        }

        private static void ValidatePhysicalDisk(PhysicalDisk physicalDisk)
        {
            Assert.IsTrue((physicalDisk.BusType == BusType.NVMe) || (physicalDisk.BusType == BusType.SATA));
            Assert.IsTrue(physicalDisk.MediaType != MediaType.Unspecified);
            Assert.IsTrue((physicalDisk.Bus >= 0) && (physicalDisk.Device >= 0) && (physicalDisk.Function >= 0) && (physicalDisk.Adapter >= 0) &&
                          (physicalDisk.Bus < 32) && (physicalDisk.Device < 32) && (physicalDisk.Function < 32) && (physicalDisk.Adapter < 32)); // sanity upper bounds

            if (physicalDisk.BusType == BusType.NVMe)
            {
                Assert.IsTrue((physicalDisk.PcieVersion > 1) && (physicalDisk.PcieVersion < 7));
                Assert.IsTrue((physicalDisk.PcieLanes >= 1) && (physicalDisk.PcieLanes <= 4));
            }
            else
            {
                Assert.IsTrue((physicalDisk.PcieVersion == -1) && (physicalDisk.PcieLanes == -1));
            }

            if (physicalDisk.BusType == BusType.SATA)
            {
                Assert.IsTrue(physicalDisk.Port >= 0);
            }
            else
            {
                Assert.IsTrue(physicalDisk.Port == -1);
            }
        }

        private static void ValidateVirtualDisk(VirtualDisk virtualDisk)
        {
            Assert.IsTrue((virtualDisk.NumberOfDataCopies >= 1) && (virtualDisk.NumberOfDataCopies <= 8)); // sanity upper bound
            Assert.IsTrue((virtualDisk.PhysicalDisks.Count >= 1) && (virtualDisk.PhysicalDisks.Count <= 32)); // sanity upper bound
            for (int diskIndex = 0; diskIndex < virtualDisk.PhysicalDisks.Count; ++diskIndex)
            {
                DiskTests.ValidatePhysicalDisk(virtualDisk.PhysicalDisks[diskIndex]);
            }
        }
    }
}
