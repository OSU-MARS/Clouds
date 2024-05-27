using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopLayer : GdalLayer
    {
        private const string DefaultLayerName = "treetops";

        protected int HeightFieldIndex { get; private init; }
        protected int TileFieldIndex { get; private init; }
        protected int RadiusFieldIndex { get; private init; }
        protected int TreeIDfieldIndex { get; private init; }

        protected TreetopLayer(Layer gdalLayer)
            : base(gdalLayer)
        {
            this.TileFieldIndex = this.Definition.GetFieldIndex("tile"); // -1 if tile field wasn't created
            this.TreeIDfieldIndex = this.Definition.GetFieldIndex("treeID");
            this.HeightFieldIndex = this.Definition.GetFieldIndex("height");
            this.RadiusFieldIndex = this.Definition.GetFieldIndex("radius");
            Debug.Assert((this.TreeIDfieldIndex >= 0) && (this.HeightFieldIndex > this.TreeIDfieldIndex) && (this.RadiusFieldIndex > this.HeightFieldIndex));
        }

        public void Add(int id, double x, double y, double elevation, double height, double radius)
        {
            Debug.Assert(this.IsInEditMode && (this.TileFieldIndex == -1));

            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.TreeIDfieldIndex, id);
            treetopCandidate.SetField(this.HeightFieldIndex, height);
            treetopCandidate.SetField(this.RadiusFieldIndex, radius);
            this.Layer.CreateFeature(treetopCandidate);
        }

        public void Add(string tileName, Treetops treetops, IList<string> classNames)
        {
            Debug.Assert(this.IsInEditMode && (this.TileFieldIndex >= 0));
            if (classNames.Count < treetops.ClassCounts.GetLength(1))
            {
                throw new ArgumentOutOfRangeException(nameof(classNames), "Names for some class fields are missing. Treetrops have counts for " + treetops.ClassCounts.GetLength(1) + " classes but only " + classNames.Count + " class names were provided.");
            }

            Span<int> classFieldIndices = stackalloc int[classNames.Count];
            for (int classIndex = 0; classIndex < classNames.Count; ++classIndex)
            {
                int classFieldIndex = this.Definition.GetFieldIndex(classNames[classIndex]);
                if (classFieldIndex < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(classNames), "Could not find field '" + classNames[classIndex] + "' in layer definition.");
                }
                classFieldIndices[classIndex] = classFieldIndex;
            }

            for (int treetopIndex = 0; treetopIndex < treetops.Count; ++treetopIndex)
            {
                Feature treetopCandidate = new(this.Definition);
                Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
                treetopPosition.AddPoint(treetops.X[treetopIndex], treetops.Y[treetopIndex], treetops.Elevation[treetopIndex]);
                treetopCandidate.SetGeometry(treetopPosition);
                treetopCandidate.SetField(this.TileFieldIndex, tileName);
                treetopCandidate.SetField(this.TreeIDfieldIndex, treetops.ID[treetopIndex]);
                treetopCandidate.SetField(this.HeightFieldIndex, treetops.Height[treetopIndex]);
                treetopCandidate.SetField(this.RadiusFieldIndex, treetops.Radius[treetopIndex]);
                for (int classIndex = 0; classIndex < classFieldIndices.Length; ++classIndex)
                {
                    treetopCandidate.SetField(classFieldIndices[classIndex], treetops.ClassCounts[treetopIndex, classIndex]);
                }
                this.Layer.CreateFeature(treetopCandidate);
            }
        }

        protected static Layer CreateGdalLayer(DataSource source, string layerName, SpatialReference crs, int tileFieldWidth)
        {
            Layer gdalLayer = source.CreateLayer(layerName, crs, wkbGeometryType.wkbPoint25D, [ "OVERWRITE=YES" ]); // https://gdal.org/drivers/vector/gpkg.html#vector-gpkg or equivalent for creation options
            gdalLayer.StartTransaction();

            if (tileFieldWidth > 0)
            {
                FieldDefn tileFieldDefinition = new("tile", FieldType.OFTString);
                tileFieldDefinition.SetWidth(tileFieldWidth);
                gdalLayer.CreateField(tileFieldDefinition);
            }

            gdalLayer.CreateField("treeID", FieldType.OFTInteger);
            gdalLayer.CreateField("height", FieldType.OFTReal);
            gdalLayer.CreateField("radius", FieldType.OFTReal);

            return gdalLayer;
        }

        public static TreetopLayer CreateOrOverwrite(DataSource treetopFile, SpatialReference crs)
        {
            return TreetopLayer.CreateOrOverwrite(treetopFile, crs, -1, Array.Empty<string>());
        }

        public static TreetopLayer CreateOrOverwrite(DataSource treetopFile, SpatialReference crs, int tileFieldWidth, IList<string> classNames)
        {
            Layer gdalLayer = TreetopLayer.CreateGdalLayer(treetopFile, TreetopLayer.DefaultLayerName, crs, tileFieldWidth);
            for (int classIndex = 0; classIndex < classNames.Count; ++classIndex)
            {
                gdalLayer.CreateField(classNames[classIndex], FieldType.OFTInteger);
            }

            TreetopLayer treetopLayer = new(gdalLayer)
            {
                IsInEditMode = true
            };
            return treetopLayer;
        }

        public (double xCentroid, double yCentroid) GetCentroid()
        {
            Envelope extent = new();
            if (this.Layer.GetExtent(extent, force: 1) != OgrError.NONE)
            {
                throw new InvalidOperationException("Getting layer extent failed.");
            }
            return (0.5 * (extent.MaxX + extent.MinX), 0.5 * (extent.MaxY + extent.MinY));
        }

        public Treetops GetTreetops(int classCapacity)
        {
            Treetops treetops = new((int)this.GetFeatureCount(), classCapacity);
            double[] pointBuffer = new double[3];

            this.Layer.ResetReading();
            Feature? treetop = this.Layer.GetNextFeature();
            for (int treetopIndex = 0; treetop != null; treetop = this.Layer.GetNextFeature(), ++treetopIndex)
            {
                Geometry geometry = treetop.GetGeometryRef();
                geometry.GetPoint(0, pointBuffer);
                treetops.ID[treetopIndex] = treetop.GetFieldAsInteger(this.TreeIDfieldIndex);
                treetops.X[treetopIndex] = pointBuffer[0];
                treetops.Y[treetopIndex] = pointBuffer[1];
                treetops.Elevation[treetopIndex] = pointBuffer[2];
                treetops.Height[treetopIndex] = treetop.GetFieldAsDouble(this.HeightFieldIndex);
                treetops.Radius[treetopIndex] = treetop.GetFieldAsDouble(this.RadiusFieldIndex);
            }

            treetops.Count = treetops.Capacity;
            return treetops;
        }

        public static TreetopLayer Open(DataSource treetopFile)
        {
            Layer? gdalLayer = treetopFile.GetLayerByName(TreetopLayer.DefaultLayerName);
            if (gdalLayer == null)
            {
                if (treetopFile.GetLayerCount() != 1)
                {
                    throw new NotSupportedException("Treetop file either contains multiple layers but does not contain a layer named treetops or contains no layers.");
                }

                gdalLayer = treetopFile.GetLayerByIndex(0);
            }

            return new TreetopLayer(gdalLayer);
        }
    }
}
