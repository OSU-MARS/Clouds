using OSGeo.OGR;
using OSGeo.OSR;

namespace Mars.Clouds.GdalExtensions
{
    public class BoundingBoxLayer : GdalVectorLayer
    {
        private int pendingBoxCommits;

        protected BoundingBoxLayer(Layer gdalLayer)
            : base(gdalLayer)
        {
            this.pendingBoxCommits = 0;
        }

        public void Add(string name, double xMin, double yMin, double xMax, double yMax)
        {
            Feature boundingBox = new(this.Definition);
            Geometry ring = new(wkbGeometryType.wkbLinearRing);
            ring.AddPoint_2D(xMin, yMin);
            ring.AddPoint_2D(xMin, yMax);
            ring.AddPoint_2D(xMax, yMax);
            ring.AddPoint_2D(xMax, yMin);
            ring.AddPoint_2D(xMin, yMin); // GDAL warns if ring is not closed
            Geometry bounds = new(wkbGeometryType.wkbPolygon);
            bounds.AddGeometryDirectly(ring);
            boundingBox.SetGeometryDirectly(bounds);
            boundingBox.SetField(0, name);

            this.Layer.CreateFeature(boundingBox);

            ++this.pendingBoxCommits;
            if (this.pendingBoxCommits >= GdalVectorLayer.MaximumTransactionSizeInFeatures)
            {
                this.RestartTransaction();
                this.pendingBoxCommits = 0;
            }
        }

        public static BoundingBoxLayer CreateOrOverwrite(DataSource dataSource, string layerName, SpatialReference crs, string nameField, int nameLength)
        {
            Layer gdalLayer = dataSource.CreateLayer(layerName, crs, wkbGeometryType.wkbPolygon, [ Constant.Gdal.OverwriteLayer ]);
            gdalLayer.StartTransaction();

            FieldDefn nameFieldDefinition = new(nameField, FieldType.OFTString);
            nameFieldDefinition.SetWidth(nameLength);
            gdalLayer.CreateField(nameFieldDefinition);

            BoundingBoxLayer boundingBoxLayer = new(gdalLayer)
            {
                IsInEditMode = true
            };
            return boundingBoxLayer;
        }
    }
}