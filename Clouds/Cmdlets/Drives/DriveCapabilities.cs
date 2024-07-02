using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mars.Clouds.Cmdlets.Drives
{
    internal class DriveCapabilities
    {
        public const float HardDriveDefaultTransferRateInGBs = 0.25F;

        public BusType BusType { get; protected set; }

        /// <summary>
        /// Rough estimate (likely within a factor of two) of upper bound on sequential transfer rates in GB/s based on drive type and connection.
        /// </summary>
        public float EstimatedSequentialSpeedInGBs { get; protected set; }

        public MediaType MediaType { get; protected set; }
        public int NumberOfDataCopies { get; protected set; }

        protected DriveCapabilities()
        {
            this.NumberOfDataCopies = 1;
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

        public int GetPracticalReadThreadCount(float workloadRatePerThreadInGBs)
        {
            int threads = (int)(this.EstimatedSequentialSpeedInGBs / workloadRatePerThreadInGBs);
            if (this.MediaType == MediaType.HardDrive)
            {
                // for now, limit workloads to one thread per hard drive to limit acutator contention
                // Underlying assumption is workloads are fast enough to exploit single threaded multi-actuator actuator configurations (e.g. dual
                // actuators in standard RAID0 or RAID10).
                threads = Int32.Min(threads, this.NumberOfDataCopies);
            }

            return Int32.Max(threads, 1); // guarantee at least one thread
        }
    }
}
