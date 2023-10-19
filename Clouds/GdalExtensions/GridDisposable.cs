using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public class GridDisposable<TCell> : Grid<TCell>, IDisposable where TCell : class, IDisposable
    {
        private bool isDisposed;

        protected GridDisposable(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize)
        {
            this.isDisposed = false;
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
                    for (int yIndex = 0; yIndex < this.YSize; ++yIndex)
                    {
                        for (int xIndex = 0; xIndex < this.XSize; ++xIndex)
                        {
                            this[xIndex, yIndex]?.Dispose();
                        }
                    }
                }

                this.isDisposed = true;
            }
        }
    }
}
