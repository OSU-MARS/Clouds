using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Points with xyz values, classification, and point source ID plus ground elevation.
    /// </summary>
    public class PointList<TPointBatch> : List<TPointBatch> where TPointBatch : PointBatch, new()
    {
        public SpatialReference Crs { get; private init; }
        public Extent GridExtent { get; private init; }
        public double XScaleFactor { get; private init; }
        public double YScaleFactor { get; private init; }
        public float ZScaleFactor { get; private init; }
        public double XOffset { get; private init; }
        public double YOffset { get; private init; }
        public float ZOffset { get; private init; }

        public PointList(LasTile lasTile, ObjectPool<TPointBatch> pointBatchPool)
        {
            LasHeader10 lasHeader = lasTile.Header;
            this.Crs = lasTile.GetSpatialReference();
            this.GridExtent = lasTile.GridExtent;
            this.XScaleFactor = lasHeader.XScaleFactor;
            this.YScaleFactor = lasHeader.YScaleFactor;
            this.ZScaleFactor = (float)lasHeader.ZScaleFactor;
            this.XOffset = lasHeader.XOffset;
            this.YOffset = lasHeader.YOffset;
            this.ZOffset = (float)lasHeader.ZOffset;

            UInt64 pointCount = lasTile.Header.GetNumberOfPoints();
            int completeBatches = (int)(pointCount / PointBatch.DefaultCapacity);
            int batches = completeBatches + (pointCount % PointBatch.DefaultCapacity != 0 ? 1 : 0);
            pointBatchPool.GetThreadSafe(this, batches);
        }

        //public long GetNumberOfPoints()
        //{
        //    long pointCount = 0;
        //    for (int batchIndex = 0; batchIndex < this.Count; ++batchIndex)
        //    {
        //        pointCount += this[batchIndex].Count;
        //    }
        //    return pointCount;
        //}

        public void Return(ObjectPool<TPointBatch> pointBatchPool)
        {
            for (int batchIndex = 0; batchIndex < this.Count; ++batchIndex)
            {
                this[batchIndex].Clear();
            }
            pointBatchPool.ReturnThreadSafe(this);
            this.Clear();
        }
    }
}
