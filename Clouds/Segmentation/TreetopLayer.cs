using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopLayer : IDisposable
    {
        private readonly int heightFieldIndex;
        private bool isDisposed;
        private readonly Layer layer;
        private readonly int radiusFieldIndex;
        private readonly int treeIDfieldIndex;
        private readonly FeatureDefn treetopDefinition;

        public TreetopLayer(DataSource treetopFile, SpatialReference coordinateSystem)
        {
            this.layer = treetopFile.CreateLayer("treetops", coordinateSystem, wkbGeometryType.wkbPoint25D, new string[] { "OVERWRITE=YES" }); // https://gdal.org/drivers/vector/gpkg.html#vector-gpkg or equivalent for creation options
            this.layer.StartTransaction();

            int treeIDfieldCreationResult = this.layer.CreateField(new FieldDefn("treeID", FieldType.OFTInteger), approx_ok: 0);
            int heightFieldCreationResult = this.layer.CreateField(new FieldDefn("height", FieldType.OFTReal), approx_ok: 0);
            int radiusFieldCreationResult = this.layer.CreateField(new FieldDefn("radius", FieldType.OFTReal), approx_ok: 0);
            if ((treeIDfieldCreationResult != OgrError.NONE) || 
                (heightFieldCreationResult != OgrError.NONE) ||
                (radiusFieldCreationResult != OgrError.NONE))
            {
                throw new InvalidOperationException("Failed to create tree ID, height, or radius field in treetop layer of '" + treetopFile + "' (tree ID OGR error code = " + treeIDfieldCreationResult + ", height field code = " + heightFieldCreationResult + ").");
            }

            this.treeIDfieldIndex = this.layer.FindFieldIndex("treeID", bExactMatch: 1);
            this.heightFieldIndex = this.layer.FindFieldIndex("height", bExactMatch: 1);
            this.radiusFieldIndex = this.layer.FindFieldIndex("radius", bExactMatch: 1);
            Debug.Assert((this.treeIDfieldIndex >= 0) && (this.heightFieldIndex > this.treeIDfieldIndex) && (this.radiusFieldIndex > this.heightFieldIndex));
            this.treetopDefinition = this.layer.GetLayerDefn();
        }

        public void Add(int id, double x, double y, double elevation, double height, double radius)
        {
            Feature treetopCandidate = new(this.treetopDefinition);
            Geometry treetopPosition = new(wkbGeometryType.wkbPoint25D);
            treetopPosition.AddPoint(x, y, elevation);
            treetopCandidate.SetGeometry(treetopPosition);
            treetopCandidate.SetField(this.treeIDfieldIndex, id);
            treetopCandidate.SetField(this.heightFieldIndex, height);
            treetopCandidate.SetField(this.radiusFieldIndex, radius);
            this.layer.CreateFeature(treetopCandidate);
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.layer.CommitTransaction();

                    this.treetopDefinition.Dispose();
                    this.layer.Dispose();
                }

                this.isDisposed = true;
            }
        }
    }
}
