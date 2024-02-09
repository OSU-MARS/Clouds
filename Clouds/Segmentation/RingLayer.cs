using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Segmentation
{
    internal class RingLayer : TreetopLayer
    {
        private readonly int dsmZindex;
        private readonly int inSimilarElevationIndex;
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
            this.inSimilarElevationIndex = this.Definition.GetFieldIndex("inSimilarElevationGroup");
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

        /// <param name="id">candidate treetop ID (may not be unique)</param>
        /// <param name="x">local maxima's x location in CRS units</param>
        /// <param name="y">local maxima's y location in CRS units</param>
        /// <param name="elevation">local maxima's DTM elevation in CRS units</param>
        /// <param name="dsmZ">local maxima's DSM elevation in CRS units</param>
        /// <param name="localMaximaRadius">radius over which local maxima is known to be the highest point in CRS units (either the distance to the next highest DSM cell or the maximum search radius)</param>
        /// <param name="maxRingIndexEvaluated">maximum index to which ring elevations are populated</param>
        /// <param name="maxRingElevation">minimum DSM elevation by ring in CRS units</param>
        /// <param name="minRingElevation">minimum DSM elevation by ring in CRS units</param>
        public void Add(int id, double x, double y, double elevation, double dsmZ, bool isInSimilarElevationGroup, double localMaximaRadius, double netProminenceNormalized, int maxRingIndexEvaluated, ReadOnlySpan<float> maxRingElevation, ReadOnlySpan<float> minRingElevation)
        {
            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.TileFieldIndex, this.tileName);
            treetopCandidate.SetField(this.TreeIDfieldIndex, id);
            treetopCandidate.SetField(this.HeightFieldIndex, dsmZ - elevation);
            treetopCandidate.SetField(this.RadiusFieldIndex, localMaximaRadius);
            treetopCandidate.SetField(this.dsmZindex, dsmZ);
            treetopCandidate.SetField(this.inSimilarElevationIndex, isInSimilarElevationGroup ? 1.0F : 0.0F);
            treetopCandidate.SetField(this.netProminenceNormalizedIndex, netProminenceNormalized);

            int maxLoggedRingIndex = Int32.Min(this.maxRingIndex.Length - 1, maxRingIndexEvaluated);
            for (int ringIndex = 0; ringIndex <= maxLoggedRingIndex; ++ringIndex)
            {
                treetopCandidate.SetField(this.maxRingIndex[ringIndex], maxRingElevation[ringIndex]);
                treetopCandidate.SetField(this.minRingIndex[ringIndex], minRingElevation[ringIndex]);
            }
            for (int ringIndex = maxLoggedRingIndex + 1; ringIndex < this.maxRingIndex.Length; ++ringIndex)
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
            gdalLayer.CreateField("inSimilarElevationGroup", FieldType.OFTReal);
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
