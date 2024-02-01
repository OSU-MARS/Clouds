using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Segmentation
{
    internal class RingLayer : TreetopLayer
    {
        private readonly int dsmZindex;
        private readonly int[] maxRingIndex;
        private readonly int[] minRingIndex;
        private readonly int netProminenceNormalizedIndex;

        private readonly string tileName;

        protected RingLayer(Layer gdalLayer, string tileName)
            : base(gdalLayer)
        {
            this.maxRingIndex = new int[5];
            this.minRingIndex = new int[5];
            this.tileName = tileName;

            this.dsmZindex = this.Definition.GetFieldIndex("dsmZ");
            this.maxRingIndex[0] = this.Definition.GetFieldIndex("maxRing1");
            this.maxRingIndex[1] = this.Definition.GetFieldIndex("maxRing2");
            this.maxRingIndex[2] = this.Definition.GetFieldIndex("maxRing3");
            this.maxRingIndex[3] = this.Definition.GetFieldIndex("maxRing4");
            this.maxRingIndex[4] = this.Definition.GetFieldIndex("maxRing5");
            this.minRingIndex[0] = this.Definition.GetFieldIndex("minRing1");
            this.minRingIndex[1] = this.Definition.GetFieldIndex("minRing2");
            this.minRingIndex[2] = this.Definition.GetFieldIndex("minRing3");
            this.minRingIndex[3] = this.Definition.GetFieldIndex("minRing4");
            this.minRingIndex[4] = this.Definition.GetFieldIndex("minRing5");
            this.netProminenceNormalizedIndex = this.Definition.GetFieldIndex("netProminenceNormalized");
        }

        public void Add(int id, double x, double y, double elevation, double dsmZ, double radius, double netProminenceNormalized, int maxRingIndex, ReadOnlySpan<float> maxRingHeight, ReadOnlySpan<float> minRingHeight)
        {
            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.TileFieldIndex, this.tileName);
            treetopCandidate.SetField(this.TreeIDfieldIndex, id);
            treetopCandidate.SetField(this.HeightFieldIndex, dsmZ - elevation);
            treetopCandidate.SetField(this.RadiusFieldIndex, radius);
            treetopCandidate.SetField(this.dsmZindex, dsmZ);
            treetopCandidate.SetField(this.netProminenceNormalizedIndex, netProminenceNormalized);

            int maxLoggedRingIndex = Int32.Min(this.maxRingIndex.Length, maxRingIndex);
            for (int ringIndex = 0; ringIndex < maxLoggedRingIndex; ++ringIndex)
            {
                treetopCandidate.SetField(this.maxRingIndex[ringIndex], maxRingHeight[ringIndex]);
                treetopCandidate.SetField(this.minRingIndex[ringIndex], minRingHeight[ringIndex]);
            }
            for (int ringIndex = maxLoggedRingIndex; ringIndex < this.maxRingIndex.Length; ++ringIndex)
            {
                treetopCandidate.SetField(this.maxRingIndex[ringIndex], Double.NaN);
                treetopCandidate.SetField(this.minRingIndex[ringIndex], Double.NaN);
            }

            this.Layer.CreateFeature(treetopCandidate);
        }

        public static RingLayer CreateOrOverwrite(DataSource ringFile, SpatialReference crs, string tileName)
        {
            Layer gdalLayer = TreetopLayer.CreateGdalLayer(ringFile, "ringDsm", crs, tileName.Length);
            gdalLayer.CreateField("netProminenceNormalized", FieldType.OFTReal);
            gdalLayer.CreateField("dsmZ", FieldType.OFTReal);
            gdalLayer.CreateField("maxRing1", FieldType.OFTReal);
            gdalLayer.CreateField("maxRing2", FieldType.OFTReal);
            gdalLayer.CreateField("maxRing3", FieldType.OFTReal);
            gdalLayer.CreateField("maxRing4", FieldType.OFTReal);
            gdalLayer.CreateField("maxRing5", FieldType.OFTReal);
            gdalLayer.CreateField("minRing1", FieldType.OFTReal);
            gdalLayer.CreateField("minRing2", FieldType.OFTReal);
            gdalLayer.CreateField("minRing3", FieldType.OFTReal);
            gdalLayer.CreateField("minRing4", FieldType.OFTReal);
            gdalLayer.CreateField("minRing5", FieldType.OFTReal);

            RingLayer ringLayer = new(gdalLayer, tileName)
            {
                IsInEditMode = true
            };
            return ringLayer;
        }
    }
}
