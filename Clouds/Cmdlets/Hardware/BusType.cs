using System;

namespace Mars.Clouds.Cmdlets.Hardware
{
    public enum BusType : UInt16
    {
        Unknown = 0,
        SCSI = 1,
        ATAPI = 2,
        ATA = 3,
        Firewire = 4, // IEEE-1394
        SSA = 5,
        FibreChannel = 6,
        USB = 7,
        RAID = 8,
        iSCSI = 9,
        SAS = 10,
        SATA = 11,
        SecureDigital = 12,
        MultimediaCard = 13,
        ReservedMax = 14,
        FileBackedVirtual = 15,
        StorageSpace = 16,
        NVMe = 17,
        MicrosoftReserved = 18
    }
}
