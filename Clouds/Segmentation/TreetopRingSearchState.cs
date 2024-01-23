using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRingSearchState : TreetopTileSearchState
    {
        public RasterBand<float>? NetProminenceBand { get; private init; }
        public RasterBand<float>? RadiusBand { get; private init; }
        public RasterBand<float>? RangeProminenceRatioBand { get; private init; }
        public RasterBand<float>? TotalProminenceBand { get; private init; }
        public RasterBand<float>? TotalRangeBand { get; private init; }

        public Raster<float>? RingData { get; private init; }

        public TreetopRingSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood, bool diagnostics)
            : base(dsmNeighborhood, dtmNeighborhood)
        {
            if (diagnostics)
            {
                this.RingData = new(this.Dsm.Crs, this.Dsm.Transform, this.Dsm.XSize, this.Dsm.YSize, 5, Single.NaN);
                this.NetProminenceBand = this.RingData.Bands[0];
                this.RangeProminenceRatioBand = this.RingData.Bands[1];
                this.TotalProminenceBand = this.RingData.Bands[2];
                this.TotalRangeBand = this.RingData.Bands[3];
                this.RadiusBand = this.RingData.Bands[4];

                this.NetProminenceBand.Name = "net prominence normalized";
                this.RangeProminenceRatioBand.Name = "range-prominence normalized";
                this.TotalProminenceBand.Name = "total prominence normalized";
                this.TotalRangeBand.Name = "total range normalized";
                this.RadiusBand.Name = "radius";
            }
            else
            {
                this.NetProminenceBand = null;
                this.RangeProminenceRatioBand = null;
                this.RingData = null;
                this.TotalProminenceBand = null;
                this.TotalRangeBand = null;
            }
        }
    }
}
