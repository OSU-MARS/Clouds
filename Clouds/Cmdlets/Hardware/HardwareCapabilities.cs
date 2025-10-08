using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Mars.Clouds.Cmdlets.Hardware
{
    public class HardwareCapabilities
    {
        private const float HardDriveMultipleAccessDerating = 0.7F; // for now, assume tiles are laid out more or less adjacently

        public const float HardDriveDefaultTransferRateInGBs = 0.285F; // 18 TB IronWolf Pro benchmark at outer platter edge, same spec for 20, 22, and 24 TB versions
        public const float Pcie2LaneBandwidthInGBs = 1.7F / 4.0F; // usable bandwidth as commonly implemeted by available NVMe drives
        public const float Pcie3LaneBandwidthInGBs = 3.5F / 4.0F;
        public const float Pcie4LaneBandwidthInGBs = 7.0F / 4.0F;
        public const float Pcie5LaneBandwidthInGBs = 14.0F / 4.0F;

        public static HardwareCapabilities Current { get; private set; }

        public float DdrBandwidthInGBs { get; protected init; }
        public float HardDriveTransferRateInGBs { get; protected set; }
        public int PhysicalCores { get; protected set; }
        public SortedList<string, PhysicalDisk> PhysicalDisksByRoot { get; private init; }
        public SortedList<string, VirtualDisk> VirtualDisksByRoot { get; private init; }

        static HardwareCapabilities()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                HardwareCapabilities.Current = new HardwareCapabilitiesWindows();
            }
            else
            {
                throw new NotSupportedException($"Unhandled operating system {RuntimeInformation.OSDescription}.");
            }
        }

        protected HardwareCapabilities()
        {
            // default hard drives to reasonable maximum transfer rate similar to NVMe calculations
            this.HardDriveTransferRateInGBs = HardwareCapabilities.HardDriveDefaultTransferRateInGBs;
            this.PhysicalDisksByRoot = [];
            this.VirtualDisksByRoot = [];
        }

        private static List<string> GetPathRoots(List<string> paths, string currentLocation)
        {
            if (paths.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(paths), "At least one path must be specified.");
            }

            List<string> pathRoots = [];
            for (int index = 0; index < paths.Count; ++index)
            {
                string path = paths[index];
                string? pathRoot = Path.GetPathRoot(path);
                if (String.IsNullOrEmpty(pathRoot))
                {
                    pathRoot = Path.GetPathRoot(currentLocation);
                    if (String.IsNullOrEmpty(pathRoot))
                    {
                        throw new NotSupportedException($"Both the path '{path}' and the current directory '{currentLocation}' are rootless.");
                    }
                }

                if (pathRoots.Contains(pathRoot) == false)
                {
                    pathRoots.Add(pathRoot);
                }
            }

            return pathRoots;
        }

        public int GetPracticalReadThreadCount(List<string> drivePaths, string currentLocation, float driveTransferRatePerThreadInGBs, float ddrBandwidthPerThreadInGBs)
        {
            // currently Windows specific
            // When needed, support can be added for other drive identification methods such as /def/sd and mount paths on Linux.
            List<string> pathRoots = HardwareCapabilities.GetPathRoots(drivePaths, currentLocation);
            if (pathRoots.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(drivePaths), $"{drivePaths.Count} drive paths provided zero path roots.");
            }

            // add up capabilities of utilized drives
            int minimumHardDriveThreads = 0;
            float totalTransferRateFromFlash = 0.0F;
            float totalTransferRateFromHardDrives = 0.0F;
            List<PhysicalDisk> physicalDisksIncluded = [];
            for (int rootIndex = 0; rootIndex < pathRoots.Count; ++rootIndex)
            {
                string pathRoot = pathRoots[rootIndex];
                if (this.PhysicalDisksByRoot.TryGetValue(pathRoot, out PhysicalDisk? physicalDisk))
                {
                    if (physicalDisksIncluded.Contains(physicalDisk) == false)
                    {
                        if (physicalDisk.IsHardDrive)
                        {
                            ++minimumHardDriveThreads;
                            totalTransferRateFromHardDrives += this.HardDriveTransferRateInGBs;
                        }
                        else
                        {
                            totalTransferRateFromFlash += physicalDisk.GetEstimatedMaximumTransferRateInGBs();
                        }

                        physicalDisksIncluded.Add(physicalDisk);
                    }
                    // otherwise partition is on a drive whose capabilities have already been added
                }
                else if (this.VirtualDisksByRoot.TryGetValue(pathRoot, out VirtualDisk? virtualDisk))
                {
                    int physicalHardDrivesInVirtualDisk = 0;
                    float virtualDiskTotalTransferRateFromFlash = 0.0F;
                    for (int physicalDiskIndex = 0; physicalDiskIndex < virtualDisk.PhysicalDisks.Count; ++physicalDiskIndex)
                    {
                        physicalDisk = virtualDisk.PhysicalDisks[physicalDiskIndex];
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
                        throw new NotSupportedException($"Virtual disk contains {physicalHardDrivesInVirtualDisk} hard drives as well as flash drives capable of transferring {virtualDiskTotalTransferRateFromFlash} GB/s. Mixtures of drive media types are not currently supported.");
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
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(drivePaths), $"Found no physical or virtual disk for path root '{pathRoot}'.");
                }
            }

            // estimate number of threads
            float totalDdrBandwidthDemand = (totalTransferRateFromHardDrives + totalTransferRateFromFlash) * ddrBandwidthPerThreadInGBs / driveTransferRatePerThreadInGBs;
            float ddrDemandDerating = 1.0F;
            if (totalDdrBandwidthDemand > ddrBandwidthPerThreadInGBs)
            {
                // see throughputModel.R
                ddrDemandDerating -= totalDdrBandwidthDemand / (totalDdrBandwidthDemand + this.DdrBandwidthInGBs);
            }

            int threads = 0;
            if (minimumHardDriveThreads > 0)
            {
                // if read rates are low then drive actuators can support multiple read threads
                threads = (int)(HardwareCapabilities.HardDriveMultipleAccessDerating * totalTransferRateFromHardDrives / (driveTransferRatePerThreadInGBs * ddrDemandDerating));
                if (threads < minimumHardDriveThreads)
                {
                    // otherwise require at least one thread per hard drive read channel to encourage full drive utilization
                    // Synchronous threads with higher workload rates remain pinned to a drive rather than roaming across drives.
                    threads = minimumHardDriveThreads;
                }
            }

            threads += (int)(totalTransferRateFromFlash / (driveTransferRatePerThreadInGBs * ddrDemandDerating)); // should this ceiling instead of floor?
            if (threads < 1)
            {
                threads = 1; // must have at least one thread
            }

            return threads;
        }
    }
}
