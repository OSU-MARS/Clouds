using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownSegmentationState
    {
        public float AboveTopCostScaleFactor { get; init; }
        public RasterNeighborhood8<float>? AspectNeighborhood { get; private set; }
        public RasterNeighborhood8<float>? ChmNeighborhood { get; private set; }
        public RasterNeighborhood8<float>? DsmNeighborhood { get; private set; }

        public int DsmStartIndexX { get; set; } // inclusive
        public int DsmStartIndexY { get; set; } // inclusive
        public int DsmEndIndexX { get; set; } // exclusive
        public int DsmEndIndexY { get; set; } // exclusive

        public float[] FieldChm { get; private init; }
        public float[] FieldDsm { get; private init; }
        public ObjectPool<TreeCrownCostField> FieldPool { get; private init; }
        public bool[] FieldSearched { get; private init; }
        public float MaximumCrownRatio { get; init; }
        public float MinimumHeightInCrsUnits { get; init; }
        public float PathCostLimitInCrsUnits { get; init; }
        public RasterNeighborhood8<float>? SlopeNeighborhood { get; private set; }
        public TreeCrownCostGrid? TreetopCostTile { get; private set; }
        public GridNeighborhood8<TreetopsGrid, Treetops>? TreetopNeighborhood { get; private set; }

        public TreeCrownSegmentationState()
        {
            this.AboveTopCostScaleFactor = 2.0F;
            this.AspectNeighborhood = null;
            this.ChmNeighborhood = null;
            this.DsmNeighborhood = null;

            this.DsmStartIndexX = -1;
            this.DsmStartIndexY = -1;
            this.DsmEndIndexX = -1;
            this.DsmEndIndexY = -1;

            this.FieldChm = GC.AllocateUninitializedArray<float>(TreeCrownCostField.Cells);
            this.FieldDsm = GC.AllocateUninitializedArray<float>(TreeCrownCostField.Cells);
            this.FieldPool = new();
            this.FieldSearched = GC.AllocateUninitializedArray<bool>(TreeCrownCostField.Cells);

            this.MaximumCrownRatio = 0.90F;
            this.MinimumHeightInCrsUnits = 1.0F;
            this.PathCostLimitInCrsUnits = 40.0F;
            this.SlopeNeighborhood = null;
            this.TreetopCostTile = null;
            this.TreetopNeighborhood = null;
        }

        public void SetNeighborhoods(VirtualRaster<DigitalSurfaceModel> dsm, VirtualVector<TreetopsGrid> treetops, int tileIndexX, int tileIndexY, string dsmBand)
        {
            this.AspectNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.DsmAspectBandName);
            this.ChmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.CanopyHeightBandName);
            this.DsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, dsmBand);
            this.SlopeNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.DsmSlopeBandName);

            if (this.TreetopNeighborhood == null)
            {
                this.TreetopNeighborhood = new(tileIndexX, tileIndexY, treetops.TileGrid);
            }
            else
            {
                this.TreetopNeighborhood.MoveTo(tileIndexX, tileIndexY, treetops.TileGrid);
            }

            TreetopsGrid treetopsTile = this.TreetopNeighborhood.Center;
            if (this.TreetopCostTile == null)
            {
                this.TreetopCostTile = new(treetopsTile);
            }
            else
            {
                this.TreetopCostTile.Reset(treetopsTile);
            }
        }
    }
}
