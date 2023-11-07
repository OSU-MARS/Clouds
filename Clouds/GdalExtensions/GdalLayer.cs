using OSGeo.OGR;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    internal class GdalLayer : IDisposable // wrap OSGeo.OGR.Layer as GDAL classes don't implement dispose pattern
    {
        private bool isInEditMode;
        private bool isDisposed;

        protected FeatureDefn Definition { get; init; }
        protected Layer Layer { get; init; }

        protected GdalLayer(Layer gdalLayer)
        {
            this.Definition = gdalLayer.GetLayerDefn();
            this.isInEditMode = false;
            this.Layer = gdalLayer;
        }

        public bool IsInEditMode 
        { 
            get { return this.isInEditMode; }
            init { this.isInEditMode = value; }
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
                    if (this.IsInEditMode)
                    {
                        this.Layer.CommitTransaction();
                        this.isInEditMode = false;
                    }

                    this.Definition.Dispose();
                    this.Layer.Dispose();
                }

                this.isDisposed = true;
            }
        }

        public long GetFeatureCount()
        {
            return this.Layer.GetFeatureCount(force: 1);
        }

        public SpatialReference GetSpatialReference()
        {
            return this.Layer.GetSpatialRef();
        }
    }
}
