﻿using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LasInfo")]
    public class GetLasInfo : LasTilesCmdlet
    {
        protected override void ProcessRecord()
        {
            // TODO: drop tile requirement
            LasTileGrid lasTileGrid = this.ReadLasHeadersAndFormGrid("Get-LasInfo");
            List<LasTile> tiles = new(lasTileGrid.NonNullCells);
            for (int tileIndex = 0; tileIndex < lasTileGrid.Cells; ++tileIndex)
            {
                LasTile? tile = lasTileGrid[tileIndex];
                if (tile != null)
                {
                    tiles.Add(tile);
                }

                if (this.Stopping)
                {
                    return;
                }
            }

            this.WriteObject(tiles);
        }
    }
}
