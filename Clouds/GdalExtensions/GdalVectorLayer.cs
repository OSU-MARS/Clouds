using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.GdalExtensions
{
    // wrap OSGeo.OGR.Layer as GDAL classes implement IDisposable but lack the standard dispose pattern's Dispose(bool)
    internal class GdalVectorLayer : IDisposable
    {
        protected static int MaximumTransactionSizeInFeatures = 10000;

        private bool isInEditMode;
        private bool isDisposed;

        protected FeatureDefn Definition { get; init; }
        protected Layer Layer { get; init; }

        protected GdalVectorLayer(Layer gdalLayer)
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

        protected void RestartTransaction()
        {
            Debug.Assert(this.IsInEditMode);

            // commit any pending changes and open a new transaction for further changes
            // For GeoPackages this, in principle, allows background processing and writes to disk by SQLite threads while remaining
            // modifications are made to a layer. This interleaving might shorten runtimes.
            this.Layer.CommitTransaction();
            this.Layer.StartTransaction();
        }
    }
}
