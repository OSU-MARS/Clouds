using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopLayer : GdalLayer
    {
        private const string DefaultLayerName = "treetops";

        private readonly int heightFieldIndex;
        private readonly int tileFieldIndex;
        private readonly int radiusFieldIndex;
        private readonly int treeIDfieldIndex;

        protected TreetopLayer(Layer gdalLayer)
            : base(gdalLayer)
        {
            this.tileFieldIndex = this.Definition.GetFieldIndex("tile"); // -1 if tile field wasn't created
            this.treeIDfieldIndex = this.Definition.GetFieldIndex("treeID");
            this.heightFieldIndex = this.Definition.GetFieldIndex("height");
            this.radiusFieldIndex = this.Definition.GetFieldIndex("radius");
            Debug.Assert((this.treeIDfieldIndex >= 0) && (this.heightFieldIndex > this.treeIDfieldIndex) && (this.radiusFieldIndex > this.heightFieldIndex));
        }

        public void Add(int id, double x, double y, double elevation, double height, double radius)
        {
            Debug.Assert(this.tileFieldIndex == -1);

            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.treeIDfieldIndex, id);
            treetopCandidate.SetField(this.heightFieldIndex, height);
            treetopCandidate.SetField(this.radiusFieldIndex, radius);
            this.Layer.CreateFeature(treetopCandidate);
        }

        public void Add(string tileName, Treetops treetops, IList<string> classNames)
        {
            Debug.Assert(this.tileFieldIndex >= 0);
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
                treetopCandidate.SetField(this.tileFieldIndex, tileName);
                treetopCandidate.SetField(this.treeIDfieldIndex, treetops.ID[treetopIndex]);
                treetopCandidate.SetField(this.heightFieldIndex, treetops.Height[treetopIndex]);
                treetopCandidate.SetField(this.radiusFieldIndex, treetops.Radius[treetopIndex]);
                for (int classIndex = 0; classIndex < classFieldIndices.Length; ++classIndex)
                {
                    treetopCandidate.SetField(classFieldIndices[classIndex], treetops.ClassCounts[treetopIndex, classIndex]);
                }
                this.Layer.CreateFeature(treetopCandidate);
            }
        }

        public static TreetopLayer CreateOrOverwrite(DataSource treetopFile, SpatialReference coordinateSystem)
        {
            return TreetopLayer.CreateOrOverwrite(treetopFile, coordinateSystem, -1, Array.Empty<string>());
        }

        public static TreetopLayer CreateOrOverwrite(DataSource treetopFile, SpatialReference coordinateSystem, int tileFieldWidth, IList<string> classNames)
        {
            Layer gdalLayer = treetopFile.CreateLayer(TreetopLayer.DefaultLayerName, coordinateSystem, wkbGeometryType.wkbPoint25D, new string[] { "OVERWRITE=YES" }); // https://gdal.org/drivers/vector/gpkg.html#vector-gpkg or equivalent for creation options
            gdalLayer.StartTransaction();

            int tileFieldCreationResult = OgrError.NONE;
            if (tileFieldWidth > 0)
            {
                FieldDefn tileFieldDefinition = new("tile", FieldType.OFTString);
                tileFieldDefinition.SetWidth(tileFieldWidth);
                tileFieldCreationResult = gdalLayer.CreateField(tileFieldDefinition, approx_ok: 0);
            }
            int treeIDfieldCreationResult = gdalLayer.CreateField(new FieldDefn("treeID", FieldType.OFTInteger), approx_ok: 0);
            int heightFieldCreationResult = gdalLayer.CreateField(new FieldDefn("height", FieldType.OFTReal), approx_ok: 0);
            int radiusFieldCreationResult = gdalLayer.CreateField(new FieldDefn("radius", FieldType.OFTReal), approx_ok: 0);
            if ((tileFieldCreationResult != OgrError.NONE) ||
                (treeIDfieldCreationResult != OgrError.NONE) ||
                (heightFieldCreationResult != OgrError.NONE) ||
                (radiusFieldCreationResult != OgrError.NONE))
            {
                throw new InvalidOperationException("Failed to create tile, tree ID, height, or radius field in treetop layer of '" + treetopFile + "' (OGR error codes: tile " + tileFieldCreationResult + ", tree ID " + treeIDfieldCreationResult + ", height " + heightFieldCreationResult + ", radius " + radiusFieldCreationResult + ").");
            }

            for (int classIndex = 0; classIndex < classNames.Count; ++classIndex)
            {
                int classCountCreationResult = gdalLayer.CreateField(new FieldDefn(classNames[classIndex], FieldType.OFTInteger), approx_ok: 0);
                if (classCountCreationResult != OgrError.NONE)
                {
                    throw new InvalidOperationException("Failed to field in treetop layer of '" + treetopFile + "' (OGR error code " + classCountCreationResult + ").");
                }
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
                treetops.ID[treetopIndex] = treetop.GetFieldAsInteger(this.treeIDfieldIndex);
                treetops.X[treetopIndex] = pointBuffer[0];
                treetops.Y[treetopIndex] = pointBuffer[1];
                treetops.Elevation[treetopIndex] = pointBuffer[2];
                treetops.Height[treetopIndex] = treetop.GetFieldAsDouble(this.heightFieldIndex);
                treetops.Radius[treetopIndex] = treetop.GetFieldAsDouble(this.radiusFieldIndex);
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
