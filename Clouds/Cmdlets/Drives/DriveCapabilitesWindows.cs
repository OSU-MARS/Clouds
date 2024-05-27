using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
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
            //ManagementClass diskDrives = new("Win32_DiskDrive");
            //foreach (ManagementBaseObject diskDrive in diskDrives.GetInstances())
            //{
            //    string model = (string)diskDrive.GetPropertyValue("Model"); // also caption and firmware revision
            //    string deviceID = (string)diskDrive.GetPropertyValue("DeviceID");
            //    int physicalDiskNumber = Int32.Parse(deviceID[17..]); // \\.\PHYSICALDRIVE prefix
            //}

            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisk, includes network shares
            // Many properties appear to be null for RAID but no clear RAID indicator with dynamic disks even though they use the
            // LDM (logical disk manager).
            //ManagementClass logicalDisks = new("Win32_LogicalDisk");
            //foreach (ManagementBaseObject logicalDisk in logicalDisks.GetInstances())
            //{
            //    string driveLetter = (string)logicalDisk.GetPropertyValue("DeviceID");
            //}

            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskpartition
            // Type indicates use of basic versus logical disks.
            //ManagementClass diskPartitions = new("Win32_DiskPartition");
            //foreach (ManagementBaseObject diskPartition in diskPartitions.GetInstances())
            //{
            //    int physicalDiskNumber = (int)((UInt32)diskPartition.GetPropertyValue("DiskIndex"));
            //    string type = (string)diskPartition.GetPropertyValue("Type"); // GPT: Basic Data for basic disks, GPT: Logical Disk Manager { Metadata, Data } for dynamic disks, also GPT: System
            //}

            // dynamic disks and do not have MSFT_Disk objects: NTFS striped volumes (and mirrors?)
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-volume
            // HealthStatus, FileSystemType
            //ManagementObjectSearcher volumes = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_Volume");
            //foreach (ManagementBaseObject volume in volumes.Get())
            //{
            //    UInt16 fileSystemType = (UInt16)volume.GetPropertyValue("FileSystemType");
            //}

            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-logicaldisktopartition
            // {[Antecedent, \\MARIK\root\cimv2:Win32_DiskPartition.DeviceID="Disk #2, Partition #1"]}
            // {[Dependent, \\MARIK\root\cimv2:Win32_LogicalDisk.DeviceID="C:"]}
            List<string> pathRoots = DriveCapabilitesWindows.GetPathRoots(paths);
            SortedList<string, List<int>> diskNumbersByPathRoot = []; // 1:1 for JBOD (Windows basic partitions, one or more per physical disk), 1:n for RAID
            ManagementClass partitionsToDriveLetters = new("Win32_LogicalDiskToPartition");
            foreach (ManagementBaseObject partitionToDriveLetter in partitionsToDriveLetters.GetInstances())
            {
                // get physical disk number for basic and dynamic volumes
                // will be virtual disk number for virtual disks
                string partition = (string)partitionToDriveLetter.GetPropertyValue("Antecedent");
                int diskNumber = Int32.Parse(partition[(partition.IndexOf("Disk #") + 6)..partition.LastIndexOf(',')]);

                string logicalDisk = (string)partitionToDriveLetter.GetPropertyValue("Dependent");
                string driveLetter = logicalDisk[(logicalDisk.IndexOf('"') + 1)..logicalDisk.LastIndexOf('"')]; // with colon
                string pathRoot = driveLetter + Path.DirectorySeparatorChar;
                //UInt64 startingAddress = (UInt64)partitionToDriveLetter.GetPropertyValue("StartingAddress");
                //UInt64 endingAddress = (UInt64)partitionToDriveLetter.GetPropertyValue("EndingAddress");

                if (pathRoots.Contains(pathRoot))
                {
                    if (diskNumbersByPathRoot.TryGetValue(pathRoot, out List<int>? diskNumbers))
                    {
                        diskNumbers.Add(diskNumber);
                    }
                    else
                    {
                        diskNumbersByPathRoot.Add(pathRoot, [diskNumber]);
                    }
                }
            }

            for (int rootIndex = 0; rootIndex < pathRoots.Count; ++rootIndex)
            {
                string pathRoot = pathRoots[rootIndex];
                if (diskNumbersByPathRoot.ContainsKey(pathRoot) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(paths), "No disk found for drive '" + pathRoot + "'.");
                }
            }

            // get physical disks of referenced basic volumes and dynamic disks (Disk Management stripes and mirrors) and any disk referenced by a storage space
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
            // DeviceId + BusType (NVMe, SATA, USB, SAS, RAID) + MediaType (hard drive, solid state drive)
            ManagementObjectSearcher physicalDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
            SortedList<int, PhysicalDisk> physicalDiskByNumber = [];
            SortedList<string, PhysicalDisk> physicalDiskByStorageSpaceGuid = [];
            foreach (ManagementBaseObject physicalDisk in physicalDisks.Get())
            {
                Dictionary<string, object> properties = [];
                foreach (PropertyData property in physicalDisk.Properties)
                {
                    properties.Add(property.Name, property.Value);
                }

                int physicalDiskNumber = Int32.Parse((string)physicalDisk.GetPropertyValue("DeviceID")); // no \\.\PHYSICALDRIVE prefix
                bool isReferencedByBasicOrDynamicDisk = false;
                for (int rootIndex = 0; rootIndex < diskNumbersByPathRoot.Count; ++rootIndex)
                {
                    if (diskNumbersByPathRoot.Values[rootIndex].Contains(physicalDiskNumber))
                    {
                        BusType busType = (BusType)physicalDisk.GetPropertyValue("BusType");
                        MediaType mediaType = (MediaType)physicalDisk.GetPropertyValue("MediaType");

                        physicalDiskByNumber.Add(physicalDiskNumber, new PhysicalDisk() { BusType = busType, MediaType = mediaType });
                        isReferencedByBasicOrDynamicDisk = true;
                        break;
                    }
                }

                if (isReferencedByBasicOrDynamicDisk == false)
                {
                    string objectID = (string)physicalDisk.GetPropertyValue("ObjectId");
                    if (objectID.Contains("SPACES_PhysicalDisk", StringComparison.Ordinal))
                    {
                        string storageSpaceDiskGuid = DriveCapabilitesWindows.GetLastGuidFromObjectID(objectID);
                        BusType busType = (BusType)physicalDisk.GetPropertyValue("BusType");
                        MediaType mediaType = (MediaType)physicalDisk.GetPropertyValue("MediaType");
                        physicalDiskByStorageSpaceGuid.Add(storageSpaceDiskGuid, new PhysicalDisk() { BusType = busType, MediaType = mediaType });
                    }
                }
            }

            // find virtual and then physical disks of storage spaces
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-disk
            // HealthStatus, Number + BusType - bus type is physical disk's bus type for dynamic RAID disks, StorageSpace for storage spaces
            // MSFT_Disk is needed to get a storage space's logical/virtual disk number and then join to MSFT_VirtualDisk for RAID properties on
            // the storage space's object ID.
            SortedList<string, int> diskNumberByStorageSpaceID = [];
            ManagementObjectSearcher microsoftDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_Disk");
            foreach (ManagementBaseObject microsoftDisk in microsoftDisks.Get())
            {
                BusType busType = (BusType)microsoftDisk.GetPropertyValue("BusType");
                if (busType == BusType.StorageSpace)
                {
                    // object ID format is
                    // {1}\\<hostname>\root/Microsoft/Windows/Storage/Providers_v2\SPACES_VirtualDisk.ObjectId="{92f536e6-f35c-11ed-afe6-806e6f6e6963}:VD:{<virtual disk GUID?>}{<GUID of storage space>}"
                    // where 92f536e6-f35c-11ed-afe6-806e6f6e6963 is the GUID for any disk.
                    string objectID = (string)microsoftDisk.GetPropertyValue("ObjectID");
                    int startStorageSpaceGuidIndex = objectID.LastIndexOf(@"DI:\\?\storage#disk#{") + 21;
                    int endStorageSpaceGuidIndex = startStorageSpaceGuidIndex + 36; // should be 36 characters in GUID
                    string storageSpaceGuid = objectID[startStorageSpaceGuidIndex..endStorageSpaceGuidIndex];

                    int diskNumber = (int)((UInt32)microsoftDisk.GetPropertyValue("Number"));
                    diskNumberByStorageSpaceID.Add(storageSpaceGuid, diskNumber);
                }
            }

            SortedList<int, VirtualDisk> storageSpacesByDiskNumber = [];
            if (diskNumberByStorageSpaceID.Count > 0)
            {
                // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisk
                // RAID properties for storage spaces. Dynamic disks do not appear as virtual disks (Windows 10 22H2).
                ManagementObjectSearcher microsoftVirtualDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_VirtualDisk");
                foreach (ManagementBaseObject microsoftVirtualDisk in microsoftVirtualDisks.Get())
                {
                    string objectID = (string)microsoftVirtualDisk.GetPropertyValue("ObjectID");
                    string storageSpaceGuid = DriveCapabilitesWindows.GetLastGuidFromObjectID(objectID);
                    int diskNumber = diskNumberByStorageSpaceID[storageSpaceGuid];

                    // MediaType mediaType = (MediaType)microsoftVirtualDisk.GetPropertyValue("MediaType"); // Unspecified
                    // int numberOfGroups = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("NumberOfGroups")); // undocumented
                    // int physicalDiskRedundancy = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("PhysicalDiskRedundancy"));
                    int numberOfDataCopies = (int)((UInt16)microsoftVirtualDisk.GetPropertyValue("NumberOfDataCopies"));
                    // string resiliency = (string)microsoftVirtualDisk.GetPropertyValue("ResiliencySettingName"); // mirror and (probably) simple
                    storageSpacesByDiskNumber.Add(diskNumber, new() { NumberOfDataCopies = numberOfDataCopies });
                }

                // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisktophysicaldisk
                ManagementObjectSearcher virtualDisksToPhysicalDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_VirtualDiskToPhysicalDisk");
                foreach (ManagementBaseObject virtualToPhysicalDisk in virtualDisksToPhysicalDisks.Get())
                {
                    string virtualDiskObjectID = (string)virtualToPhysicalDisk.GetPropertyValue("VirtualDisk");
                    string virtualDiskGuid = DriveCapabilitesWindows.GetLastGuidFromObjectID(virtualDiskObjectID);
                    if (diskNumberByStorageSpaceID.TryGetValue(virtualDiskGuid, out int diskNumber))
                    {
                        string physicalDiskObjectID = (string)virtualToPhysicalDisk.GetPropertyValue("PhysicalDisk");
                        string physicalDiskGuid = DriveCapabilitesWindows.GetLastGuidFromObjectID(physicalDiskObjectID);

                        VirtualDisk storageSpace = storageSpacesByDiskNumber[diskNumber];
                        storageSpace.PhysicalDisks.Add(physicalDiskByStorageSpaceGuid[physicalDiskGuid]);
                    }
                }
            }

            if ((physicalDiskByNumber.Count == 0) && (storageSpacesByDiskNumber.Count == 0))
            {
                throw new ArgumentOutOfRangeException(nameof(paths), "No disks found among drives '" + String.Join(", ", pathRoots) + "'.");
            }
            if ((physicalDiskByNumber.Count != 0) && (storageSpacesByDiskNumber.Count != 0))
            {
                throw new NotSupportedException("Paths '" + String.Join(", ", pathRoots) + "' include at least one basic or dynamic volumes and one storage space. Volume spanning of this type is not currently supported.");
            }

            // TODO: bus speeds
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bus
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity

            // coalesce drive capabilites
            if (physicalDiskByNumber.Count > 0)
            {
                (this.BusType, this.MediaType) = DriveCapabilitesWindows.GetBusAndMediaType(physicalDiskByNumber.Values);
                // leave this.NumberOfDataCopies at default of 1
                // TODO: this.NumberOfDataCopies for storage spaces and if there is a way to distinguish RAID0 and RAID1 dynamic disks
            }
            else
            {
                VirtualDisk storageSpace = storageSpacesByDiskNumber.Values[0];
                (this.BusType, this.MediaType) = DriveCapabilitesWindows.GetBusAndMediaType(storageSpace.PhysicalDisks);
                this.NumberOfDataCopies = storageSpace.NumberOfDataCopies;
            }

            for (int storageSpaceIndex = 0; storageSpaceIndex < storageSpacesByDiskNumber.Count; ++storageSpaceIndex)
            {
                VirtualDisk storageSpace = storageSpacesByDiskNumber.Values[storageSpaceIndex];
                if (storageSpace.NumberOfDataCopies != this.NumberOfDataCopies)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across number of data copies " + storageSpace.NumberOfDataCopies + " and " + this.NumberOfDataCopies + " is not currently implemented.");
                }

                (BusType storageSpaceBus, MediaType storageSpaceMedia) = DriveCapabilitesWindows.GetBusAndMediaType(storageSpace.PhysicalDisks);
                if (storageSpaceBus != this.BusType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across bus types " + storageSpaceBus + " and " + this.BusType + " is not currently implemented.");
                }
                if (storageSpaceMedia != this.MediaType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across drives of type " + storageSpaceMedia + " and " + this.MediaType + " is not currently implemented.");
                }
            }
        }

        private static (BusType busType, MediaType mediaType) GetBusAndMediaType(IList<PhysicalDisk> physicalDisks)
        {
            if (physicalDisks.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(physicalDisks), "List of physical disks is empty.");
            }

            PhysicalDisk disk = physicalDisks[0];
            BusType busType = disk.BusType;
            MediaType mediaType = disk.MediaType;
            for (int physicalDiskIndex = 1; physicalDiskIndex < physicalDisks.Count; ++physicalDiskIndex)
            {
                disk = physicalDisks[physicalDiskIndex];
                if (disk.BusType != busType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across bus types " + disk.BusType + " and " + busType + " is not currently implemented.");
                }
                if (disk.MediaType != mediaType)
                {
                    throw new NotSupportedException("Coalescence of drive capabilities across drives of type " + disk.MediaType + " and " + mediaType + " is not currently implemented.");
                }
            }

            return (busType, mediaType);
        }

        private static string GetLastGuidFromObjectID(string objectID)
        {
            int startIndex = objectID.LastIndexOf('{') + 1;
            int endIndex = startIndex + 36; // should be 36 characters in GUID
            return objectID[startIndex..endIndex];
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
