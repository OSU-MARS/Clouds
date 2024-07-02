using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mars.Clouds.Cmdlets.Drives
{
    public class DriveCapabilities
    {
        private const float HardDriveMultipleAccessDerating = 0.7F; // for now, assume tiles are laid out more or less adjacently

        public const float HardDriveDefaultTransferRateInGBs = 0.285F; // 18 TB IronWolf Pro benchmark at outer platter edge, same spec for 20, 22, and 24 TB versions
        public const float Pcie2LaneBandwidthInGBs = 1.7F / 4.0F; // usable bandwidth as commonly implemeted by available NVMe drives
        public const float Pcie3LaneBandwidthInGBs = 3.5F / 4.0F;
        public const float Pcie4LaneBandwidthInGBs = 7.0F / 4.0F;
        public const float Pcie5LaneBandwidthInGBs = 14.0F / 4.0F;

        public float HardDriveTransferRateInGBs { get; set; }
        public SortedList<int, PhysicalDisk> UtilizedPhysicalDisksByNumber { get; private init; }
        public SortedList<int, VirtualDisk> UtilizedVirtualDisksByNumber { get; private init; }

        protected DriveCapabilities()
        {
            // default hard drives to reasonable maximum transfer rate similar to NVMe calculations
            this.HardDriveTransferRateInGBs = DriveCapabilities.HardDriveDefaultTransferRateInGBs;
            this.UtilizedPhysicalDisksByNumber = [];
            this.UtilizedVirtualDisksByNumber = [];
        }

        public static DriveCapabilities Create(List<string>? paths)
        {
            ArgumentNullException.ThrowIfNull(paths);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new DriveCapabilitiesWindows(paths);
            }

            throw new NotSupportedException("Unhandled operating system " + RuntimeInformation.OSDescription + ".");
        }

        public int GetPracticalReadThreadCount(float synchronousWorkloadRatePerThreadInGBs)
        {
            int minimumHardDriveThreads = 0;
            float totalTransferRateFromFlash = 0.0F;
            float totalTransferRateFromHardDrives = 0.0F;
            for (int physicalDiskIndex = 0; physicalDiskIndex < this.UtilizedPhysicalDisksByNumber.Count; ++physicalDiskIndex)
            {
                PhysicalDisk physicalDisk = this.UtilizedPhysicalDisksByNumber.Values[physicalDiskIndex];
                if (physicalDisk.IsHardDrive)
                {
                    ++minimumHardDriveThreads;
                    totalTransferRateFromHardDrives += this.HardDriveTransferRateInGBs;
                }
                else
                {
                    totalTransferRateFromFlash += physicalDisk.GetEstimatedMaximumTransferRateInGBs();
                }
            }
            if ((minimumHardDriveThreads > 0) && (totalTransferRateFromFlash > 0))
            {
                throw new NotSupportedException("Directly utilized physical disks are " + minimumHardDriveThreads + " hard drives as well as flash drives capable of transferring " + totalTransferRateFromFlash + " GB/s. Mixtures of drive media types are not currently supported.");
            }

            for (int virtualDiskIndex = 0; virtualDiskIndex < this.UtilizedVirtualDisksByNumber.Count; ++virtualDiskIndex)
            {
                VirtualDisk virtualDisk = this.UtilizedVirtualDisksByNumber.Values[virtualDiskIndex];
                int physicalHardDrivesInVirtualDisk = 0;
                float virtualDiskTotalTransferRateFromFlash = 0.0F;
                for (int physicalDiskIndex = 0; physicalDiskIndex < virtualDisk.PhysicalDisks.Count; ++physicalDiskIndex)
                {
                    PhysicalDisk physicalDisk = virtualDisk.PhysicalDisks[physicalDiskIndex];
                    if (physicalDisk.IsHardDrive)
                    {
                        ++physicalHardDrivesInVirtualDisk;
                    }
                    else
                    {
                        virtualDiskTotalTransferRateFromFlash += physicalDisk.GetEstimatedMaximumTransferRateInGBs();
                    }
                }
                
                if ((physicalHardDrivesInVirtualDisk > 0) && (virtualDiskTotalTransferRateFromFlash > 0))
                {
                    throw new NotSupportedException("Virtual disk contains " + physicalHardDrivesInVirtualDisk + " hard drives as well as flash drives capable of transferring " + virtualDiskTotalTransferRateFromFlash + " GB/s. Mixtures of drive media types are not currently supported.");
                }

                int virtualDiskMinimumHardDriveThreads = 1;
                if ((virtualDisk.NumberOfDataCopies > 1) && (virtualDisk.NumberOfColumns == 1))
                {
                    // RAID0 and RAID10 provide full rate access with a single thread but two drive RAID1 requires two threads
                    // Until further characterization is done, assume need for more than one read thread is RAID1 specific.
                    virtualDiskMinimumHardDriveThreads = virtualDisk.NumberOfDataCopies;
                }
                minimumHardDriveThreads += virtualDiskMinimumHardDriveThreads;
                totalTransferRateFromFlash += virtualDiskTotalTransferRateFromFlash / virtualDisk.PhysicalDisks.Count * virtualDisk.NumberOfColumns * virtualDisk.NumberOfDataCopies;
                totalTransferRateFromHardDrives += this.HardDriveTransferRateInGBs * virtualDisk.NumberOfColumns * virtualDisk.NumberOfDataCopies;
            }

            if ((minimumHardDriveThreads > 0) && (totalTransferRateFromFlash > 0))
            {
                throw new NotSupportedException("Combination of directly utilized physical disks and virtual disks results in data availability form " + minimumHardDriveThreads + " hard drives as well as flash drives capable of transferring " + totalTransferRateFromFlash + " GB/s. Mixtures of drive media types are not currently supported.");
            }

            int threads;
            if (minimumHardDriveThreads > 0)
            {
                // if read rates are low then drive actuators can support multiple read threads
                threads = (int)(DriveCapabilities.HardDriveMultipleAccessDerating * totalTransferRateFromHardDrives / synchronousWorkloadRatePerThreadInGBs);
                if (threads < minimumHardDriveThreads)
                {
                    // otherwise require at least one thread per hard drive read channel to encourage full drive utilization
                    // Synchronous threads with higher workload rates remain pinned to a drive rather than roaming across drives.
                    threads = minimumHardDriveThreads;
                }
            }
            else
            {
                threads = (int)(totalTransferRateFromFlash / synchronousWorkloadRatePerThreadInGBs);
                if (threads < 1)
                {
                    threads = 1; // must have at least one thread
                }
            }

            return threads;
        }
    }
}
