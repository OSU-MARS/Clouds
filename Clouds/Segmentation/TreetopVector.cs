using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopVector : GdalVectorLayer
    {
        private const int DefaultCapacityIncrement = 10000; // 10k trees
        private const string DefaultLayerName = "treetops";
        public const string TreeIDFieldName = "treeID";

        private int pendingTreetopCommits;

        private readonly int tileFieldIndex;
        private readonly int idFieldIndex;
        private readonly int radiusFieldIndex;
        private readonly int heightFieldIndex;

        protected TreetopVector(Layer gdalLayer)
            : base(gdalLayer)
        {
            this.pendingTreetopCommits = 0;

            this.tileFieldIndex = this.Definition.GetFieldIndex(LocalMaximaVector.TileFieldName); // -1 if tile field wasn't created
            this.idFieldIndex = this.Definition.GetFieldIndex(TreetopVector.TreeIDFieldName);
            if (this.idFieldIndex < 0)
            {
                this.idFieldIndex = this.Definition.GetFieldIndex(LocalMaximaVector.IDFieldName); // interoperate with local maxima layers or derived layers
            }
            this.heightFieldIndex = this.Definition.GetFieldIndex(LocalMaximaVector.HeightFieldName);
            this.radiusFieldIndex = this.Definition.GetFieldIndex(LocalMaximaVector.RadiusFieldName);

            if (this.tileFieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gdalLayer), LocalMaximaVector.TileFieldName + " field is missing from treetop layer.");
            }
            if (this.idFieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gdalLayer), "Treetop layer does not have an " + LocalMaximaVector.IDFieldName + " or treeID field.");
            }
            if (this.heightFieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gdalLayer), LocalMaximaVector.HeightFieldName + " field is missing from treetop layer.");
            }
            if (this.radiusFieldIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gdalLayer), LocalMaximaVector.RadiusFieldName + " field is missing from treetop layer.");
            }
        }

        public void Add(int id, double x, double y, double elevation, double height, double radius)
        {
            Debug.Assert(this.IsInEditMode);

            Feature treetopCandidate = new(this.Definition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.idFieldIndex, id);
            treetopCandidate.SetField(this.heightFieldIndex, height);
            treetopCandidate.SetField(this.radiusFieldIndex, radius);
            this.Layer.CreateFeature(treetopCandidate);

            ++this.pendingTreetopCommits;
            if (this.pendingTreetopCommits >= GdalVectorLayer.MaximumTransactionSizeInFeatures)
            {
                this.RestartTransaction();
                this.pendingTreetopCommits = 0;
            }
        }

        public void Add(string tileName, Treetops treetops, IList<string> classNames)
        {
            Debug.Assert(this.IsInEditMode && (this.tileFieldIndex >= 0));
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
                treetopCandidate.SetField(this.idFieldIndex, treetops.ID[treetopIndex]);
                treetopCandidate.SetField(this.heightFieldIndex, treetops.Height[treetopIndex]);
                treetopCandidate.SetField(this.radiusFieldIndex, treetops.Radius[treetopIndex]);
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

            FieldDefn tileFieldDefinition = new("tile", FieldType.OFTString);
            tileFieldDefinition.SetWidth(tileFieldWidth);
            gdalLayer.CreateField(tileFieldDefinition);

            gdalLayer.CreateField("treeID", FieldType.OFTInteger);
            gdalLayer.CreateField("height", FieldType.OFTReal);
            gdalLayer.CreateField("radius", FieldType.OFTReal);

            return gdalLayer;
        }

        public static TreetopVector CreateOrOverwrite(DataSource treetopFile, SpatialReference crs, int tileFieldWidth)
        {
            return TreetopVector.CreateOrOverwrite(treetopFile, crs, tileFieldWidth, Array.Empty<string>());
        }

        public static TreetopVector CreateOrOverwrite(DataSource treetopFile, SpatialReference crs, int tileFieldWidth, IList<string> classNames)
        {
            Layer gdalLayer = TreetopVector.CreateGdalLayer(treetopFile, TreetopVector.DefaultLayerName, crs, tileFieldWidth);
            for (int classIndex = 0; classIndex < classNames.Count; ++classIndex)
            {
                gdalLayer.CreateField(classNames[classIndex], FieldType.OFTInteger);
            }

            TreetopVector treetopLayer = new(gdalLayer)
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

        public Treetops GetTreetops(int classCapacity, [NotNull] ref Treetops? treetops)
        {
            int treetopCount = (int)this.GetFeatureCount();
            int treetopCapacity = TreetopVector.DefaultCapacityIncrement * (treetopCount / TreetopVector.DefaultCapacityIncrement + 1);
            if ((treetops == null) || (treetops.Capacity < treetopCount))
            {
                // no need to retain any existing data, so just reallocate rather than extending
                treetops = new(treetopCapacity, xyIndices: false, classCapacity);
            }

            this.Layer.ResetReading();
            double[] pointBuffer = new double[3];
            Feature? treetop = this.Layer.GetNextFeature();
            for (int treetopIndex = 0; treetop != null; treetop = this.Layer.GetNextFeature(), ++treetopIndex)
            {
                Geometry geometry = treetop.GetGeometryRef();
                geometry.GetPoint(0, pointBuffer);
                treetops.ID[treetopIndex] = treetop.GetFieldAsInteger(this.idFieldIndex);
                treetops.X[treetopIndex] = pointBuffer[0];
                treetops.Y[treetopIndex] = pointBuffer[1];
                treetops.Elevation[treetopIndex] = pointBuffer[2];
                treetops.Height[treetopIndex] = treetop.GetFieldAsDouble(this.heightFieldIndex);
                treetops.Radius[treetopIndex] = treetop.GetFieldAsDouble(this.radiusFieldIndex);
            }

            treetops.Count = treetops.Capacity;
            return treetops;
        }

        public void GetTreetops(TreetopsGrid treetopsGrid, Raster surfaceModel)
        {
            this.Layer.ResetReading();
            double[] pointBuffer = new double[3];
            Feature? treetop = this.Layer.GetNextFeature();
            for (int treetopSourceIndex = 0; treetop != null; treetop = this.Layer.GetNextFeature(), ++treetopSourceIndex)
            {
                Geometry geometry = treetop.GetGeometryRef();
                geometry.GetPoint(0, pointBuffer);
                double x = pointBuffer[0];
                double y = pointBuffer[1];
                (int gridIndexX, int gridIndexY) = treetopsGrid.ToGridIndices(x, y);
                Treetops treetops = treetopsGrid[gridIndexX, gridIndexY];

                if (treetops.Count == treetops.Capacity)
                {
                    treetops.Extend(treetops.Capacity + TreetopsGrid.DefaultCellCapacity);
                }

                int treetopDestinationIndex = treetops.Count;
                (int surfaceIndexX, int surfaceIndexY) = surfaceModel.ToGridIndices(x, y);
                treetops.ID[treetopDestinationIndex] = treetop.GetFieldAsInteger(this.idFieldIndex);
                treetops.X[treetopDestinationIndex] = x;
                treetops.XIndex[treetopDestinationIndex] = surfaceIndexX;
                treetops.Y[treetopDestinationIndex] = y;
                treetops.YIndex[treetopDestinationIndex] = surfaceIndexY;
                treetops.Elevation[treetopDestinationIndex] = pointBuffer[2];
                treetops.Height[treetopDestinationIndex] = treetop.GetFieldAsDouble(this.heightFieldIndex);
                treetops.Radius[treetopDestinationIndex] = treetop.GetFieldAsDouble(this.radiusFieldIndex);
                treetops.Count = treetopDestinationIndex + 1;
            }

            treetopsGrid.Treetops = (int)this.GetFeatureCount();
        }

        public static TreetopVector Open(DataSource treetopFile)
        {
            Layer? gdalLayer = treetopFile.GetLayerByName(TreetopVector.DefaultLayerName);
            if (gdalLayer == null)
            {
                if (treetopFile.GetLayerCount() != 1)
                {
                    throw new NotSupportedException("Treetop file contains no layers or contains multiple layers, none of which are named " + TreetopVector.DefaultLayerName + ".");
                }

                gdalLayer = treetopFile.GetLayerByIndex(0);
            }

            return new TreetopVector(gdalLayer);
        }
    }
}
