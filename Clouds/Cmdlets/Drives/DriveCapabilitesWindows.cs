using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Management.Automation.Runspaces;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Drives
{
    /// <summary>
    /// Windows Storage API queries for disk capabilities via WMI (Windows Management Instrumentation)
    /// </summary>
    internal class DriveCapabilitesWindows : DriveCapabilities
    {
        [SupportedOSPlatform("windows")]
        public DriveCapabilitesWindows(List<string> paths)
        {
            // get information for drives of interest
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive
            // InterfaceType: SCSI, IDE, USB, Firewire
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
            // Number + BusType - maybe different from MSFT_PhysicalDisk with RAID
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisk
            // NumberOfDataCopies if RAID

            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisktopartition
            // {[Antecedent, \\MARIK\root\cimv2:Win32_DiskPartition.DeviceID="Disk #2, Partition #1"]}
            // {[Dependent, \\MARIK\root\cimv2:Win32_LogicalDisk.DeviceID="C:"]}
            List<string> pathRoots = DriveCapabilitesWindows.GetPathRoots(paths);
            SortedList<string, int> physicalDiskNumberByPathRoot = []; // mapping not needed but useful for debugging
            ManagementClass logicalDisks = new("Win32_LogicalDiskToPartition");
            foreach (ManagementBaseObject partitionToDriveLetter in logicalDisks.GetInstances())
            {
                string partition = (string)partitionToDriveLetter.GetPropertyValue("Antecedent");
                int physicalDiskNumber = int.Parse(partition[(partition.IndexOf("Disk #") + 6)..partition.LastIndexOf(',')]);

                string logicalDisk = (string)partitionToDriveLetter.GetPropertyValue("Dependent");
                string driveLetter = logicalDisk[(logicalDisk.IndexOf('"') + 1)..logicalDisk.LastIndexOf('"')]; // with colon
                string pathRoot = driveLetter + Path.DirectorySeparatorChar;

                if (pathRoots.Contains(pathRoot))
                {
                    physicalDiskNumberByPathRoot.Add(pathRoot, physicalDiskNumber);
                }
            }

            for (int rootIndex = 0; rootIndex < pathRoots.Count; ++rootIndex)
            {
                string pathRoot = pathRoots[rootIndex];
                if (physicalDiskNumberByPathRoot.ContainsKey(pathRoot) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(paths), "No physical disk found for drive '" + pathRoot + "'. Is it a RAID array?");
                }
            }

            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
            // DeviceId + BusType (NVMe, SATA, USB, SAS, RAID) + MediaType (hard drive, solid state drive)
            ManagementObjectSearcher physicalDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
            SortedList<int, PhysicalDisk> physicalDiskByNumber = []; // mapping not needed but useful for debugging
            foreach (ManagementBaseObject physicalDisk in physicalDisks.Get())
            {
                int physicalDiskNumber = int.Parse((string)physicalDisk.GetPropertyValue("DeviceID")); // no \\.\PHYSICALDRIVE prefix
                if (physicalDiskNumberByPathRoot.ContainsValue(physicalDiskNumber))
                {
                    BusType busType = (BusType)physicalDisk.GetPropertyValue("BusType");
                    MediaType mediaType = (MediaType)physicalDisk.GetPropertyValue("MediaType");

                    physicalDiskByNumber.Add(physicalDiskNumber, new PhysicalDisk() { BusType = busType, MediaType = mediaType });
                }
            }

            if (physicalDiskByNumber.Values.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paths), "No physical disks found among drives '" + String.Join(", ", pathRoots) + "'.");
            }

            // TODO: bus speeds
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bus
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity

            // coalesce drive capabilites
            PhysicalDisk disk = physicalDiskByNumber.Values[0];
            this.BusType = disk.BusType;
            this.MediaType = disk.MediaType;
            // TODO: this.NumberOfDataCopies once RAID is supported
            for (int driveIndex = 0; driveIndex < physicalDiskByNumber.Values.Count; ++driveIndex)
            {
                disk = physicalDiskByNumber.Values[driveIndex];
                if (disk.MediaType != this.MediaType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across drives of type " + disk.MediaType + " and " + this.MediaType + " is not currently implemented.");
                }

                if (disk.BusType != this.BusType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across bus types " + disk.BusType + " and " + this.BusType + " is not currently implemented.");
                }
            }
        }

        private static List<string> GetPathRoots(List<string> paths)
        {
            List<string> pathRoots = [];
            for (int index = 0; index < paths.Count; ++index)
            {
                string path = paths[index];
                string? pathRoot = Path.GetPathRoot(path);
                if (pathRoot == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(paths), "Path '" + path + "' is rootless");
                }

                if (pathRoots.Contains(pathRoot) == false)
                {
                    pathRoots.Add(pathRoot);
                }
            }

            return pathRoots;
        }
    }
}
