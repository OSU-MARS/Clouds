using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownSegmentationState
    {
        public float AboveTopCostScaleFactor { get; init; }
        public RasterNeighborhood8<float>? AspectNeighborhood { get; private set; }
        public RasterNeighborhood8<float>? ChmNeighborhood { get; private set; }
        public RasterNeighborhood8<float>? DsmNeighborhood { get; private set; }
        public ObjectPool<TreeCrownCostField> FieldPool { get; private init; }
        public float MaximumCrownRatio { get; init; }
        public float MinimumHeightInCrsUnits { get; init; }
        public RasterNeighborhood8<float>? SlopeNeighborhood { get; private set; }

        public TreeCrownSegmentationState()
        {
            this.AboveTopCostScaleFactor = 2.0F;
            this.AspectNeighborhood = null;
            this.ChmNeighborhood = null;
            this.DsmNeighborhood = null;
            this.FieldPool = new();
            this.MaximumCrownRatio = 0.90F;
            this.MinimumHeightInCrsUnits = 0.30F;
            this.SlopeNeighborhood = null;
        }

        public void SetNeighborhoods(VirtualRaster<DigitalSurfaceModel> dsm, int tileIndexX, int tileIndexY, string dsmBand)
        {
            this.AspectNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.DsmAspectBandName);
            this.ChmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.CanopyHeightBandName);
            this.DsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, dsmBand);
            this.SlopeNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.DsmSlopeBandName);
        }
    }
}
