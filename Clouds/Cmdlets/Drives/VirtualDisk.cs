using System.Collections.Generic;

namespace Mars.Clouds.Cmdlets.Drives
{
    internal class VirtualDisk
    {
        public int NumberOfDataCopies { get; init; }
        public List<PhysicalDisk> PhysicalDisks { get; private init; }

        public VirtualDisk()
        {
            this.NumberOfDataCopies = 0;
            this.PhysicalDisks = [];
        }
    }
}
