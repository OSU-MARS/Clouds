﻿using System;

namespace Mars.Clouds.Cmdlets.Hardware
{
    public enum MediaType : UInt16
    {
        Unspecified = 0,
        HardDrive = 3,
        SolidStateDrive = 4,
        StorageClassMemory = 5 // Optane, XL-FLASH
    }
}
