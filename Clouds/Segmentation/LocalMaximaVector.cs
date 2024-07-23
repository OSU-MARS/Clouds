using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class LocalMaximaVector : GdalLayer
    {
        public const int RingsWithStatistics = 5;

        private readonly double cellSize;
        private int nextMaximaID;
        private int pendingMaximaCommits;

        private readonly string tileName;

        protected LocalMaximaVector(Layer gdalLayer, string tileName, double cellSize)
            : base(gdalLayer)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cellSize, 0.0, nameof(cellSize));

            this.cellSize = cellSize;
            this.nextMaximaID = 0;
            this.pendingMaximaCommits = 0;
            this.tileName = tileName;

            Debug.Assert((this.Definition.GetFieldIndex("tile") == 0) &&
                         (this.Definition.GetFieldIndex("id") == 1) &&
                         (this.Definition.GetFieldIndex("sourceID") == 2) &&
                         (this.Definition.GetFieldIndex("radius") == 3) &&
                         (this.Definition.GetFieldIndex("dsmZ") == 4) &&
                         (this.Definition.GetFieldIndex("cmmZ") == 5) &&
                         (this.Definition.GetFieldIndex("height") == 6) &&
                         (this.Definition.GetFieldIndex("ring1max") == 7) &&
                         (this.Definition.GetFieldIndex("ring1mean") == 8) &&
                         (this.Definition.GetFieldIndex("ring1min") == 9) &&
                         (this.Definition.GetFieldIndex("ring2max") == 10) &&
                         (this.Definition.GetFieldIndex("ring2mean") == 11) &&
                         (this.Definition.GetFieldIndex("ring2min") == 12) &&
                         (this.Definition.GetFieldIndex("ring3max") == 13) &&
                         (this.Definition.GetFieldIndex("ring3mean") == 14) &&
                         (this.Definition.GetFieldIndex("ring3min") == 15) &&
                         (this.Definition.GetFieldIndex("ring4max") == 16) &&
                         (this.Definition.GetFieldIndex("ring4mean") == 17) &&
                         (this.Definition.GetFieldIndex("ring4min") == 18) &&
                         (this.Definition.GetFieldIndex("ring5max") == 19) &&
                         (this.Definition.GetFieldIndex("ring5mean") == 20) &&
                         (this.Definition.GetFieldIndex("ring5min") == 21) &&
                         (this.Definition.GetFieldIndex("ring1variance") == 22) &&
                         (this.Definition.GetFieldIndex("ring2variance") == 23) &&
                         (this.Definition.GetFieldIndex("ring3variance") == 24) &&
                         (this.Definition.GetFieldIndex("ring4variance") == 25) &&
                         (this.Definition.GetFieldIndex("ring5variance") == 26), "Fields not in expected order.");
        }

        /// <param name="id">candidate treetop ID (may not be unique)</param>
        /// <param name="x">local maxima's x location in CRS units</param>
        /// <param name="y">local maxima's y location in CRS units</param>
        /// <param name="chmHeight">local maxima's DTM elevation in CRS units</param>
        /// <param name="dsmZ">local maxima's DSM elevation in CRS units</param>
        /// <param name="localMaximaRadius">radius over which local maxima is known to be the highest point in CRS units (either the distance to the next highest DSM cell or the maximum search radius)</param>
        /// <param name="maxRingIndexEvaluated">maximum index to which ring elevations are populated</param>
        /// <param name="maxRingZ">minimum DSM elevation by ring in CRS units</param>
        /// <param name="minRingZ">minimum DSM elevation by ring in CRS units</param>
        public void Add(int sourceID, double x, double y, double dsmZ, double cmmZ, double chmHeight, byte localMaximaRadius, ReadOnlySpan<float> maxRingZ, ReadOnlySpan<float> meanRingZ, ReadOnlySpan<float> minRingZ, ReadOnlySpan<float> varianceRingZ)
        {
            Debug.Assert(this.IsInEditMode);

            // indices must be kept in sync with order of field creation
            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, dsmZ - chmHeight);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(0, this.tileName);
            treetopCandidate.SetField(1, this.nextMaximaID++);
            treetopCandidate.SetField(2, sourceID);
            treetopCandidate.SetField(3, this.cellSize * localMaximaRadius);
            treetopCandidate.SetField(4, dsmZ);
            treetopCandidate.SetField(5, cmmZ);
            treetopCandidate.SetField(6, chmHeight);

            for (int ringIndex = 0; ringIndex < LocalMaximaVector.RingsWithStatistics; ++ringIndex)
            {
                int ringMaxMeanMinOffset = 3 * ringIndex;
                treetopCandidate.SetField(7 + ringMaxMeanMinOffset, maxRingZ[ringIndex]);
                treetopCandidate.SetField(8 + ringMaxMeanMinOffset, meanRingZ[ringIndex]);
                treetopCandidate.SetField(9 + ringMaxMeanMinOffset, minRingZ[ringIndex]);
                treetopCandidate.SetField(22 + ringIndex, varianceRingZ[ringIndex]);
            }

            this.Layer.CreateFeature(treetopCandidate);

            ++this.pendingMaximaCommits;
            if (this.pendingMaximaCommits >= GdalLayer.MaximumTransactionSizeInFeatures)
            {
                this.RestartTransaction();
                this.pendingMaximaCommits = 0;
            }
        }

        public static LocalMaximaVector CreateOrOverwrite(DataSource localMaximaVector, Grid dsmTile, string tileName)
        {
            if (Double.Abs(dsmTile.Transform.CellHeight) != dsmTile.Transform.CellWidth)
            {
                throw new NotSupportedException("Rectangular DSM cells are not currently supported.");
            }

            Layer gdalLayer = localMaximaVector.CreateLayer("localMaxima", dsmTile.Crs, wkbGeometryType.wkbPoint25D, ["OVERWRITE=YES"]);
            gdalLayer.StartTransaction();

            // Add() must be kept in sync with field order
            FieldDefn tileFieldDefinition = new("tile", FieldType.OFTString);
            tileFieldDefinition.SetWidth(tileName.Length);
            gdalLayer.CreateField(tileFieldDefinition);
            gdalLayer.CreateField("id", FieldType.OFTInteger);
            gdalLayer.CreateField("sourceID", FieldType.OFTInteger);
            gdalLayer.CreateField("radius", FieldType.OFTReal);
            gdalLayer.CreateField("dsmZ", FieldType.OFTReal);
            gdalLayer.CreateField("cmmZ", FieldType.OFTReal);
            gdalLayer.CreateField("height", FieldType.OFTReal);
            gdalLayer.CreateField("ring1max", FieldType.OFTReal);
            gdalLayer.CreateField("ring1mean", FieldType.OFTReal);
            gdalLayer.CreateField("ring1min", FieldType.OFTReal);
            gdalLayer.CreateField("ring2max", FieldType.OFTReal);
            gdalLayer.CreateField("ring2mean", FieldType.OFTReal);
            gdalLayer.CreateField("ring2min", FieldType.OFTReal);
            gdalLayer.CreateField("ring3max", FieldType.OFTReal);
            gdalLayer.CreateField("ring3mean", FieldType.OFTReal);
            gdalLayer.CreateField("ring3min", FieldType.OFTReal);
            gdalLayer.CreateField("ring4max", FieldType.OFTReal);
            gdalLayer.CreateField("ring4mean", FieldType.OFTReal);
            gdalLayer.CreateField("ring4min", FieldType.OFTReal);
            gdalLayer.CreateField("ring5max", FieldType.OFTReal);
            gdalLayer.CreateField("ring5mean", FieldType.OFTReal);
            gdalLayer.CreateField("ring5min", FieldType.OFTReal);
            gdalLayer.CreateField("ring1variance", FieldType.OFTReal);
            gdalLayer.CreateField("ring2variance", FieldType.OFTReal);
            gdalLayer.CreateField("ring3variance", FieldType.OFTReal);
            gdalLayer.CreateField("ring4variance", FieldType.OFTReal);
            gdalLayer.CreateField("ring5variance", FieldType.OFTReal);

            // square cell constraint means width and height are equal, but height may have a minus sign
            double dsmCellSizeInCrsUnits = dsmTile.Transform.CellWidth;
            LocalMaximaVector maximaLayer = new(gdalLayer, tileName, dsmCellSizeInCrsUnits)
            {
                IsInEditMode = true
            };
            return maximaLayer;
        }
    }
}
