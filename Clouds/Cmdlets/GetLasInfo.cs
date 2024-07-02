using Mars.Clouds.Cmdlets.Drives;
using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LasInfo")]
    public class GetLasInfo : LasTilesCmdlet
    {
        protected override void ProcessRecord()
        {
            base.ValidateParameters(minWorkerThreads: 0);
            DriveCapabilities driveCapabilities = DriveCapabilities.Create(this.Las);

            LasTileGrid lasTileGrid = this.ReadLasHeadersAndFormGrid("Get-LasInfo", driveCapabilities, requiredEpsg: null);
            List<LasTile> tiles = new(lasTileGrid.NonNullCells);
            for (int tileIndex = 0; tileIndex < lasTileGrid.Cells; ++tileIndex)
            {
                LasTile? tile = lasTileGrid[tileIndex];
                if (tile != null)
                {
                    tiles.Add(tile);
                }
            }

            this.WriteObject(tiles);
        }
    }
}
