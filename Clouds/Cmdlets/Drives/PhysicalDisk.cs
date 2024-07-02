using System;
using System.Management;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Drives
{
    internal struct PhysicalDisk
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
            this.MediaType = (MediaType)physicalDisk.GetPropertyValue("MediaType");

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
    }
}
