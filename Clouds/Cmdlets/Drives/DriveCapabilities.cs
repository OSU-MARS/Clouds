﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mars.Clouds.Cmdlets.Drives
{
    internal class DriveCapabilities
    {
        public BusType BusType { get; protected set; }
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
                return new DriveCapabilitesWindows(paths);
            }

            throw new NotSupportedException("Unhandled operating system " + RuntimeInformation.OSDescription + ".");
        }

        public int GetPracticalThreadCount(float workloadRatePerThreadInGBs)
        {
            int threads = (int)(this.GetSequentialCapabilityInGBs() / workloadRatePerThreadInGBs);
            if (this.MediaType == MediaType.HardDrive)
            {
                // for now, limit workloads to one thread per hard drive to limit acutator contention
                // Underlying assumption is workloads are fast enough to exploit single threaded multi-actuator actuator configurations (e.g. dual
                // actuators in standard RAID0 or RAID10).
                threads = Int32.Min(threads, this.NumberOfDataCopies);
            }

            return Int32.Max(threads, 1); // guarantee at least one thread
        }

        /// <returns>Rough estimate (likely within a factor of two) of upper bound on sequential transfer rates in GB/s based on drive type and connection.</returns>
        public float GetSequentialCapabilityInGBs()
        {
            if (this.MediaType == MediaType.HardDrive) 
            {
                // default single actuator per mirror approximation, SATA II+ or SAS1+ assumed
                // TODO: RAID0 support
                return 0.25F * this.NumberOfDataCopies;
            }

            return this.BusType switch
            {
                // TODO: utilize bus speed information once available
                BusType.NVMe => 7.0F, // for now, assume full rate PCIe 4.0 x4
                // SSDs on SAS or SATA
                BusType.SAS => 4.3F, // for now, assume SAS4
                BusType.SATA => 0.55F, // for now, assume SATA III
                BusType.USB => 0.45F, // for now, assume 3.0 gen 1 (5 Gb/s)
                BusType.RAID => throw new NotSupportedException("RAID arrays are not currently supported."),
                BusType.StorageSpace => throw new NotSupportedException("Storage spaces are not currently supported."),
                // Unknown, (i)SCSI, ATAPI, ATA, FireWire, SSA, FibreChannel, SecureDigital, MultimediaCard, ReservedMax, FileBackedVirtual, MicrosoftReserved
                _ => throw new NotSupportedException("Unhandled bus type " + this.BusType + ".")
            };
        }
    }
}