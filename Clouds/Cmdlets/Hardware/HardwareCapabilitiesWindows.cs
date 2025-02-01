using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace Mars.Clouds.Cmdlets.Hardware
{
    /// <summary>
    /// Windows Storage API queries for disk capabilities via WMI (Windows Management Instrumentation)
    /// </summary>
    internal class HardwareCapabilitiesWindows : HardwareCapabilities
    {
        [SupportedOSPlatform("windows")]
        public HardwareCapabilitiesWindows()
        {
            #region processor
            // get physical core count as Environment.ProcessorCount provides the logical core count (number of threads)
            this.PhysicalCores = NativeMethodsWindows.GetPhysicalCoreCount();
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-processor or p/invoke
            // Also provides physical core count plus enabled physical core count. Contains maximum and current clock speed fields as well
            // but, at least with a 5950X, these fields aren't of much use as they're set to the specified base clock frequency rather than
            // the actual boost and current frequency.
            //ManagementClass processors = new("Win32_Processor");
            //foreach (ManagementBaseObject processor in processors.GetInstances())
            //{
            //    this.PhysicalCores += (int)(UInt32)processor.GetPropertyValue("NumberOfCores");
            //}
            #endregion

            #region DDR
            // get information for DDR
            // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-physicalmemory
            List<string> channels = [];
            ManagementClass dimms = new("Win32_PhysicalMemory");
            foreach (ManagementBaseObject dimm in dimms.GetInstances())
            {
                string bankLabel = (string)dimm.GetPropertyValue("BankLabel");
                bool channelAlreadyCounted = false;
                for (int channelIndex = 0; channelIndex < channels.Count; ++channelIndex)
                {
                    if (String.Equals(channels[channelIndex], bankLabel))
                    {
                        channelAlreadyCounted = true;
                        break;
                    }
                }
                if (channelAlreadyCounted == false)
                {
                    UInt32 speedInMTs = (UInt32)dimm.GetPropertyValue("Speed");
                    UInt16 totalWidthInBits = (UInt16)dimm.GetPropertyValue("TotalWidth");
                    this.DdrBandwidthInGBs += 0.001F * totalWidthInBits / 8 * speedInMTs;

                    channels.Add(bankLabel);
                }
            }
            #endregion

            #region physical and virtual disks
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
            SortedList<string, List<int>> diskNumbersByPathRoot = []; // 1:1 for JBOD (Windows basic partitions, one or more per physical disk), 1:n for RAID
            ManagementClass partitionsToDriveLetters = new("Win32_LogicalDiskToPartition");
            foreach (ManagementBaseObject partitionToDriveLetter in partitionsToDriveLetters.GetInstances())
            {
                // get physical disk number for basic and dynamic volumes
                // will be virtual disk number for virtual disks
                string partition = (string)partitionToDriveLetter.GetPropertyValue("Antecedent");
                int diskNumber = Int32.Parse(partition[(partition.IndexOf("Disk #") + 6)..partition.LastIndexOf(',')]);

                string logicalDisk = (string)partitionToDriveLetter.GetPropertyValue("Dependent");
                string pathRoot = logicalDisk[(logicalDisk.IndexOf('"') + 1)..logicalDisk.LastIndexOf('"')] + '\\'; // add trailing backslash to match .NET's root directory naming convention
                //UInt64 startingAddress = (UInt64)partitionToDriveLetter.GetPropertyValue("StartingAddress");
                //UInt64 endingAddress = (UInt64)partitionToDriveLetter.GetPropertyValue("EndingAddress");

                if (diskNumbersByPathRoot.TryGetValue(pathRoot, out List<int>? diskNumbers))
                {
                    diskNumbers.Add(diskNumber);
                }
                else
                {
                    diskNumbersByPathRoot.Add(pathRoot, [ diskNumber ]);
                }
            }

            // get physical disks of referenced basic volumes and dynamic disks (Disk Management stripes and mirrors) and any disk referenced by a storage space
            // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
            // DeviceId + BusType (NVMe, SATA, USB, SAS, RAID) + MediaType (hard drive, solid state drive)
            ManagementObjectSearcher physicalDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
            SortedList<string, PhysicalDisk> physicalDiskByStorageSpaceGuid = [];
            foreach (ManagementBaseObject physicalDiskManagementObject in physicalDisks.Get())
            {
                PhysicalDisk physicalDisk = new(physicalDiskManagementObject);

                int physicalDiskNumber = Int32.Parse((string)physicalDiskManagementObject.GetPropertyValue("DeviceID")); // no \\.\PHYSICALDRIVE prefix
                bool isReferencedByBasicOrDynamicDisk = false;
                for (int rootIndex = 0; rootIndex < diskNumbersByPathRoot.Count; ++rootIndex)
                {
                    if (diskNumbersByPathRoot.Values[rootIndex].Contains(physicalDiskNumber))
                    {
                        this.PhysicalDisksByRoot.Add(diskNumbersByPathRoot.Keys[rootIndex], physicalDisk);
                        isReferencedByBasicOrDynamicDisk = true;
                        // do not break as multiple partitions, and thus drive letters (path roots), may be present on a single drive
                    }
                }

                if (isReferencedByBasicOrDynamicDisk == false)
                {
                    string objectID = (string)physicalDiskManagementObject.GetPropertyValue("ObjectId");
                    if (objectID.Contains("SPACES_PhysicalDisk", StringComparison.Ordinal))
                    {
                        string storageSpaceDiskGuid = HardwareCapabilitiesWindows.GetLastGuidFromObjectID(objectID);
                        physicalDiskByStorageSpaceGuid.Add(storageSpaceDiskGuid, physicalDisk);
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

            bool nvmeDriveInVirtualDisk = false;
            if (diskNumberByStorageSpaceID.Count > 0)
            {
                // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisk
                // RAID properties for storage spaces. Dynamic disks do not appear as virtual disks (Windows 10 22H2).
                ManagementObjectSearcher microsoftVirtualDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_VirtualDisk");
                foreach (ManagementBaseObject microsoftVirtualDisk in microsoftVirtualDisks.Get())
                {
                    string objectID = (string)microsoftVirtualDisk.GetPropertyValue("ObjectID");
                    string storageSpaceGuid = HardwareCapabilitiesWindows.GetLastGuidFromObjectID(objectID);
                    int virtualDiskNumber = diskNumberByStorageSpaceID[storageSpaceGuid];
                    for (int rootIndex = 0; rootIndex < diskNumbersByPathRoot.Count; ++rootIndex)
                    {
                        if (diskNumbersByPathRoot.Values[rootIndex].Contains(virtualDiskNumber))
                        {
                            this.VirtualDisksByRoot.Add(diskNumbersByPathRoot.Keys[rootIndex], new(microsoftVirtualDisk));
                            break;
                        }
                    }
                }

                // https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-virtualdisktophysicaldisk
                ManagementObjectSearcher virtualDisksToPhysicalDisks = new(@"\\localhost\ROOT\Microsoft\Windows\Storage", "SELECT * FROM MSFT_VirtualDiskToPhysicalDisk");
                foreach (ManagementBaseObject virtualToPhysicalDisk in virtualDisksToPhysicalDisks.Get())
                {
                    string virtualDiskObjectID = (string)virtualToPhysicalDisk.GetPropertyValue("VirtualDisk");
                    string virtualDiskGuid = HardwareCapabilitiesWindows.GetLastGuidFromObjectID(virtualDiskObjectID);
                    if (diskNumberByStorageSpaceID.TryGetValue(virtualDiskGuid, out int diskNumber))
                    {
                        string physicalDiskObjectID = (string)virtualToPhysicalDisk.GetPropertyValue("PhysicalDisk");
                        string physicalDiskGuid = HardwareCapabilitiesWindows.GetLastGuidFromObjectID(physicalDiskObjectID);

                        PhysicalDisk physicalDisk = physicalDiskByStorageSpaceGuid[physicalDiskGuid];

                        bool virtualDiskFound = false;
                        for (int rootIndex = 0; rootIndex < diskNumbersByPathRoot.Count; ++rootIndex)
                        {
                            if (diskNumbersByPathRoot.Values[rootIndex].Contains(diskNumber))
                            {
                                VirtualDisk virtualDisk = this.VirtualDisksByRoot[diskNumbersByPathRoot.Keys[rootIndex]];
                                virtualDisk.PhysicalDisks.Add(physicalDisk);
                                virtualDiskFound = true;
                                break;
                            }
                        }
                        if (virtualDiskFound == false)
                        {
                            throw new InvalidOperationException("Failed to find virtual disk containing physical disk number " + diskNumber + "(physical disk object ID " + physicalDiskGuid + ").");
                        }

                        if (physicalDisk.BusType == BusType.NVMe)
                        {
                            nvmeDriveInVirtualDisk = true;
                        }
                    }
                }
            }

            if ((this.PhysicalDisksByRoot.Count == 0) && (this.VirtualDisksByRoot.Count == 0))
            {
                throw new InvalidOperationException("No physical or virtual disks found."); // should be unreachable but just in case
            }

            bool nvmePhysicalDiskPresent = false;
            for (int physicalDiskIndex = 0; physicalDiskIndex < this.PhysicalDisksByRoot.Count; ++physicalDiskIndex)
            {
                PhysicalDisk physicalDisk = this.PhysicalDisksByRoot.Values[physicalDiskIndex];
                if (physicalDisk.BusType == BusType.NVMe)
                {
                    nvmePhysicalDiskPresent = true;
                    break;
                }
            }
            if (nvmePhysicalDiskPresent | nvmeDriveInVirtualDisk)
            {
                // query for NVMe controllers
                // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bus
                // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity
                // https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/getdeviceproperties-win32-pnpentity
                // https://learn.microsoft.com/en-us/windows-hardware/drivers/install/devpkey-device-busreporteddevicedesc and other unified device model properties
                // https://superuser.com/questions/1732084/is-there-a-way-to-identify-the-pcie-speed-for-a-device-using-powershell-win10
                // https://stackoverflow.com/questions/69362886/get-devpkey-device-busreporteddevicedesc-from-win32-pnpentity-in-c-sharp
                string[] connectionPropertyNames = [ // "DEVPKEY_Device_BusReportedDeviceDesc", // not needed but useful for debugging
                                                     "DEVPKEY_Device_LocationInfo", // DEVPKEY_Device_PhysicalDeviceLocation not populated for NVMe controllers, DEVPKEY_Device_LocationPaths does not contain the contoller's location on the PCIe bus
                                                 //"DEVPKEY_PciDevice_MaxLinkSpeed", // not needed but sometimes useful for debugging
                                                 //"DEVPKEY_PciDevice_MaxLinkWidth", // not needed but sometimes useful for debugging
                                                 "DEVPKEY_PciDevice_CurrentLinkSpeed",
                                                 "DEVPKEY_PciDevice_CurrentLinkWidth" ];
                object?[] getPcieDeviceConnectionProperties = [ connectionPropertyNames, // input argument
                                                            null ]; // placeholder for returned ManagementObject

                // slow without class filter, picks up non-NVMe controllers including
                //   Microsoft Storage Spaces Controller - DeviceID starts with ROOT\SPACEPORT rather than PCI and has Service = 'spaceport'
                ManagementObjectSearcher scsiAdapters = new("select * from Win32_PnPEntity where PNPClass = 'SCSIAdapter'");
                foreach (ManagementBaseObject scsiAdapter in scsiAdapters.Get())
                {
                    Dictionary<string, object> adapterProperties = [];
                    foreach (PropertyData property in scsiAdapter.Properties)
                    {
                        adapterProperties.Add(property.Name, property.Value);
                    }

                    string service = (string)scsiAdapter.GetPropertyValue("Service");
                    if (String.Equals(service, "stornvme", StringComparison.Ordinal)) // might miss NVMes with custom drivers installed, such as older Samsungs?
                    {
                        // query for controller's PCIe connection properties
                        ManagementObject nvmeController = (ManagementObject)scsiAdapter;
                        nvmeController.InvokeMethod("GetDeviceProperties", getPcieDeviceConnectionProperties);
                        ManagementBaseObject[]? controllerPropertyCollections = (ManagementBaseObject[]?)getPcieDeviceConnectionProperties[1];
                        Debug.Assert((controllerPropertyCollections != null) && (controllerPropertyCollections.Length == connectionPropertyNames.Length));

                        // returned collection is in request order
                        // In general, a given device often lacks a value for a requested property. However, NVMe controllers are PCIe devices
                        // and thus should always have both location and link properties.
                        // Location string format is PCI bus n, device n, function n.
                        Debug.Assert((connectionPropertyNames.Length == controllerPropertyCollections.Length) &&
                                     String.Equals(connectionPropertyNames[0], (string)controllerPropertyCollections[0].GetPropertyValue("KeyName"), StringComparison.Ordinal) &&
                                     String.Equals(connectionPropertyNames[1], (string)controllerPropertyCollections[1].GetPropertyValue("KeyName"), StringComparison.Ordinal) &&
                                     String.Equals(connectionPropertyNames[2], (string)controllerPropertyCollections[2].GetPropertyValue("KeyName"), StringComparison.Ordinal));
                        string location = (string)controllerPropertyCollections[0].GetPropertyValue("Data");
                        string[] locationTokens = location.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        if ((locationTokens.Length != 3) ||
                            (locationTokens[0].StartsWith("PCI bus ", StringComparison.Ordinal) == false) ||
                            (locationTokens[1].StartsWith(" device ", StringComparison.Ordinal) == false) ||
                            (locationTokens[2].StartsWith(" function ", StringComparison.Ordinal) == false))
                        {
                            throw new NotSupportedException("Unhandled NVMe controller location format '" + location + "'.");
                        }

                        int bus = Int32.Parse(locationTokens[0][8..]);
                        int device = Int32.Parse(locationTokens[1][8..]);
                        int function = Int32.Parse(locationTokens[2][9..]);
                        for (int physicalDiskIndex = 0; physicalDiskIndex < this.PhysicalDisksByRoot.Count; ++physicalDiskIndex)
                        {
                            PhysicalDisk physicalDisk = this.PhysicalDisksByRoot.Values[physicalDiskIndex];
                            if ((physicalDisk.BusType == BusType.NVMe) && (physicalDisk.Bus == bus) && (physicalDisk.Device == device) && (physicalDisk.Function == function))
                            {
                                physicalDisk.PcieVersion = (int)(UInt32)controllerPropertyCollections[1].GetPropertyValue("Data");
                                physicalDisk.PcieLanes = (int)(UInt32)controllerPropertyCollections[2].GetPropertyValue("Data");
                                break;
                            }
                        }
                        // if no matching drive is found then this NVMe is presumably not in use and should be safe to omit from speed estimation

                        //Dictionary<string, object> deviceConnectionProperties = new() { { "DeviceID", deviceID } };
                        //for (int propertyCollectionIndex = 0; propertyCollectionIndex < connectionPropertyNames.Length; ++propertyCollectionIndex)
                        //{
                        //    ManagementBaseObject propertyCollection = controllerPropertyCollections[propertyCollectionIndex];
                        //    string? propertyName = null;
                        //    object? propertyValue = null;
                        //    foreach (PropertyData property in propertyCollection.Properties)
                        //    {
                        //        if (String.Equals(property.Name, "Data", StringComparison.Ordinal))
                        //        {
                        //            propertyValue = property.Value;
                        //            if (propertyName != null)
                        //            {
                        //                break;
                        //            }
                        //        }
                        //        else if (String.Equals(property.Name, "KeyName", StringComparison.Ordinal))
                        //        {
                        //            propertyName = (string)property.Value;
                        //            if (propertyValue != null)
                        //            {
                        //                break;
                        //            }
                        //        }
                        //    }

                        //    if ((propertyName != null) && (propertyValue != null))
                        //    {
                        //        deviceConnectionProperties.Add(propertyName, propertyValue);
                        //    }
                        //}
                    }
                }

                // PCIe bus and device enumeration to find NVMe controllers: slow but potentially useful for debugging
                //List<Dictionary<string, object>> pciDevices = [];
                //ManagementClass busses = new("Win32_Bus");
                //foreach (ManagementBaseObject bus in busses.GetInstances())
                //{
                //    string deviceID = (string)bus.GetPropertyValue("DeviceID");
                //    if (deviceID.StartsWith("PCI_"))
                //    {
                //        ManagementObject pciBus = (ManagementObject)bus;
                //        foreach (ManagementBaseObject deviceOnPciBus in pciBus.GetRelated("Win32_PnPEntity"))
                //        {
                //            string name = (string)deviceOnPciBus.GetPropertyValue("Name");
                //            if (name.EndsWith("NVM Express Controller", StringComparison.Ordinal))
                //            {
                //                ManagementObject nvmeController = (ManagementObject)deviceOnPciBus;

                //                Dictionary<string, object> controllerProperties = [];
                //                foreach(PropertyData property in nvmeController.Properties)
                //                {
                //                    controllerProperties.Add(property.Name, property.Value);
                //                }
                //
                //                Dictionary<string, object> deviceConnectionProperties = new() { { "Name", name } };
                //
                //                // query for device's connection properties
                //                nvmeController.InvokeMethod("GetDeviceProperties", getPcieDeviceConnectionProperties);
                //                ManagementBaseObject[]? devicePropertyCollections = (ManagementBaseObject[]?)getPcieDeviceConnectionProperties[1];
                //                Debug.Assert((devicePropertyCollections != null) && (devicePropertyCollections.Length == connectionPropertyNames.Length));
                //
                //                // no apparent ordering to returned collection
                //                // Not all requested properties have values for all devices.
                //                for (int propertyCollectionIndex = 0; propertyCollectionIndex < connectionPropertyNames.Length; ++propertyCollectionIndex)
                //                {
                //                    ManagementBaseObject propertyCollection = devicePropertyCollections[propertyCollectionIndex];
                //                    string? propertyName = null;
                //                    object? propertyValue = null;
                //                    foreach (PropertyData property in propertyCollection.Properties)
                //                    {
                //                        if (String.Equals(property.Name, "Data", StringComparison.Ordinal))
                //                        {
                //                            propertyValue = property.Value;
                //                            if (propertyName != null)
                //                            {
                //                                break;
                //                            }
                //                        }
                //                        else if (String.Equals(property.Name, "KeyName", StringComparison.Ordinal))
                //                        {
                //                            propertyName = (string)property.Value;
                //                            if (propertyValue != null)
                //                            {
                //                                break;
                //                            }
                //                        }
                //                    }

                //                    if ((propertyName != null) && (propertyValue != null))
                //                    {
                //                        deviceConnectionProperties.Add(propertyName, propertyValue);
                //                    }
                //                }
                //                pciDevices.Add(deviceConnectionProperties);
                //            }
                //        }
                //    }
                //}
            }
            #endregion
        }

        private static string GetLastGuidFromObjectID(string objectID)
        {
            int startIndex = objectID.LastIndexOf('{') + 1;
            int endIndex = startIndex + 36; // should be 36 characters in GUID
            return objectID[startIndex..endIndex];
        }
    }
}
