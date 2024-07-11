using System;
using System.Management;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Hardware
{
    public class PhysicalDisk
    {
        // drive connection and type
        public BusType BusType { get; private init; }
        public MediaType MediaType { get; private init; }

        // NVMe drive's PCIe location or the PCIe location of the drive's host bus adapter (or USB controller?)
        public int Adapter { get; private init; }
        public int Bus { get; private init; }
        public int Device { get; private init; }
        public int Function { get; private init; }
        public int Port { get; private init; }

        // PCIe uplink speed for NVMe drives
        public int PcieLanes { get; set; }
        public int PcieVersion { get; set; }

        [SupportedOSPlatform("windows")]
        public PhysicalDisk(ManagementBaseObject physicalDisk)
        {
            this.BusType = (BusType)physicalDisk.GetPropertyValue("BusType");
            if (this.BusType == BusType.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(physicalDisk), "Disk's bus type is unknown.");
            }
            this.MediaType = (MediaType)physicalDisk.GetPropertyValue("MediaType");
            if (this.MediaType == MediaType.Unspecified)
            {
                throw new ArgumentOutOfRangeException(nameof(physicalDisk), "Disk's media type is unspecified.");
            }

            // for debugging or feature addition
            //Dictionary<string, object> diskProperties = [];
            //foreach (PropertyData property in physicalDisk.Properties)
            //{
            //    diskProperties.Add(property.Name, property.Value);
            //}

            // location format for NVMe and SATA drives: Integrated : Bus n : Device n : Function n : Adapter n [ : Port n ]
            // Bus, device, function, and adapter are the PCIe bus location. NVMe drives lack a port, SATA drives (very likely?) have a port.
            string physicalLocation = (string)physicalDisk.GetPropertyValue("PhysicalLocation");
            string[] locationTokens = physicalLocation.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if ((locationTokens.Length < 5) || (locationTokens.Length > 6) ||
                (locationTokens[1].StartsWith(" Bus ", StringComparison.Ordinal) == false) ||
                (locationTokens[2].StartsWith(" Device ", StringComparison.Ordinal) == false) ||
                (locationTokens[3].StartsWith(" Function ", StringComparison.Ordinal) == false) ||
                (locationTokens[4].StartsWith(" Adapter ", StringComparison.Ordinal) == false))
            {
                throw new NotSupportedException("Unhandled disk physical location format '" + physicalLocation + "'.");
            }

            this.Bus = Int32.Parse(locationTokens[1][5..]);
            this.Device = Int32.Parse(locationTokens[2][8..]);
            this.Function = Int32.Parse(locationTokens[3][10..]);
            this.Adapter = Int32.Parse(locationTokens[4][9..]);

            if (locationTokens.Length == 6)
            {
                if (locationTokens[5].StartsWith(" Port ", StringComparison.Ordinal) == false)
                {
                    throw new NotSupportedException("Unhandled disk physical location format '" + physicalLocation + "'.");
                }

                this.Port = Int32.Parse(locationTokens[5][6..]);
            }
            else
            {
                this.Port = -1;
            }

            this.PcieLanes = -1;
            this.PcieVersion = -1;
        }

        public bool IsHardDrive
        {
            get { return this.MediaType == MediaType.HardDrive; }
        }

        public float GetEstimatedMaximumTransferRateInGBs()
        {
            // estimate sequential transfer rates
            if (this.MediaType == MediaType.HardDrive)
            {
                // default single actuator per mirror approximation, SATA II+ or SAS1+ assumed
                // TODO: RAID0 support
                return HardwareCapabilities.HardDriveDefaultTransferRateInGBs;
            }
            else if (this.BusType == BusType.NVMe)
            {
                if ((this.PcieLanes < 1) || (this.PcieLanes == 3) || (this.PcieLanes > 4))
                {
                    throw new InvalidOperationException("Drive has " + this.PcieLanes + " PCIe lanes, which is unexpected.");
                }
                float usableBandwidthPerPcieLaneInGBs = this.PcieVersion switch
                {
                    2 => HardwareCapabilities.Pcie2LaneBandwidthInGBs,
                    3 => HardwareCapabilities.Pcie3LaneBandwidthInGBs,
                    4 => HardwareCapabilities.Pcie4LaneBandwidthInGBs,
                    5 => HardwareCapabilities.Pcie5LaneBandwidthInGBs,
                    _ => throw new NotSupportedException("Unhandled PCIe version " + this.PcieVersion + ".")
                };
                return usableBandwidthPerPcieLaneInGBs * this.PcieLanes;
            }
            else
            {
                return this.BusType switch
                {
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
}
