using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class LocalMaximaLayer : GdalLayer
    {
        public const int RingCount = 5;

        private readonly double cellSize;
        private readonly int dsmZindex;
        private readonly int heightFieldIndex;
        private readonly int idFieldIndex;
        private readonly int[] maxRingIndex;
        private readonly int[] minRingIndex;
        private int nextMaximaID;
        private readonly int radiusFieldIndex;
        private readonly int tileFieldIndex;

        private readonly string tileName;

        protected LocalMaximaLayer(Layer gdalLayer, string tileName, double cellSize)
            : base(gdalLayer)
        {
            this.maxRingIndex = new int[LocalMaximaLayer.RingCount];
            this.minRingIndex = new int[LocalMaximaLayer.RingCount];
            this.nextMaximaID = 0;
            this.tileName = tileName;

            this.cellSize = cellSize;
            this.dsmZindex = this.Definition.GetFieldIndex("dsmZ");
            this.heightFieldIndex = this.Definition.GetFieldIndex("height");
            this.idFieldIndex = this.Definition.GetFieldIndex("id");
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
            this.radiusFieldIndex = this.Definition.GetFieldIndex("radius");
            this.tileFieldIndex = this.Definition.GetFieldIndex("tile");
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
        public void Add(double x, double y, double elevation, double dsmZ, byte localMaximaRadius, ReadOnlySpan<float> maxRingElevation, ReadOnlySpan<float> minRingElevation)
        {
            Debug.Assert(this.IsInEditMode);

            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.tileFieldIndex, this.tileName);
            treetopCandidate.SetField(this.idFieldIndex, this.nextMaximaID++);
            treetopCandidate.SetField(this.heightFieldIndex, dsmZ - elevation);
            treetopCandidate.SetField(this.radiusFieldIndex, localMaximaRadius);
            treetopCandidate.SetField(this.dsmZindex, dsmZ);

            for (int ringIndex = 0; ringIndex <= this.maxRingIndex.Length; ++ringIndex)
            {
                treetopCandidate.SetField(this.maxRingIndex[ringIndex], maxRingElevation[ringIndex]);
                treetopCandidate.SetField(this.minRingIndex[ringIndex], minRingElevation[ringIndex]);
            }

            this.Layer.CreateFeature(treetopCandidate);
        }

        public static LocalMaximaLayer CreateOrOverwrite(DataSource localMaximaVector, Grid dsmTile, string tileName)
        {
            if (dsmTile.Transform.CellHeight != dsmTile.Transform.CellWidth)
            {
                throw new NotSupportedException("Rectangular DSM cells are not currently supported.");
            }

            Layer gdalLayer = localMaximaVector.CreateLayer("localMaxima", dsmTile.Crs, wkbGeometryType.wkbPoint25D, ["OVERWRITE=YES"]);
            gdalLayer.StartTransaction();

            FieldDefn tileFieldDefinition = new("tile", FieldType.OFTString);
            tileFieldDefinition.SetWidth(tileName.Length);
            gdalLayer.CreateField(tileFieldDefinition);
            gdalLayer.CreateField("id", FieldType.OFTInteger);
            gdalLayer.CreateField("dsmZ", FieldType.OFTReal);
            gdalLayer.CreateField("height", FieldType.OFTReal);
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

            double dsmCellSizeInCrsUnits = dsmTile.Transform.CellHeight;
            LocalMaximaLayer maximaLayer = new(gdalLayer, tileName, dsmCellSizeInCrsUnits)
            {
                IsInEditMode = true
            };
            return maximaLayer;
        }
    }
}
