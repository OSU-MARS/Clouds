using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Drives
{
    public class VirtualDisk
    {
        public int NumberOfColumns { get; private init; }
        public int NumberOfDataCopies { get; private init; }
        public List<PhysicalDisk> PhysicalDisks { get; private init; }

        [SupportedOSPlatform("windows")]
        public VirtualDisk(ManagementBaseObject microsoftVirtualDisk)
        {
            // MediaType mediaType = (MediaType)microsoftVirtualDisk.GetPropertyValue("MediaType"); // undocumented
            this.NumberOfColumns = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("NumberOfColumns"));
            // int numberOfGroups = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("NumberOfGroups")); // undocumented
            // int physicalDiskRedundancy = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("PhysicalDiskRedundancy"));
            this.NumberOfDataCopies = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("NumberOfDataCopies"));
            // string resiliency = (string)microsoftVirtualDisk.GetPropertyValue("ResiliencySettingName"); // mirror and (probably) simple

            this.PhysicalDisks = [];
        }
    }
}
