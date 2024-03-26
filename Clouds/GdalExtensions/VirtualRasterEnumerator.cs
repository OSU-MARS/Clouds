using System;
using System.Collections;
using System.Collections.Generic;

namespace Mars.Clouds.GdalExtensions
{
    public class VirtualRasterEnumerator<TTile> : IEnumerator<TTile> where TTile : Raster
    {
        private int index;
        private readonly object lockObject;
        private TTile? tile;
        private readonly VirtualRaster<TTile> virtualRaster;

        public VirtualRasterEnumerator(VirtualRaster<TTile> virtualRaster)
        {
            this.index = -1;
            this.lockObject = new();
            this.tile = null;
            this.virtualRaster = virtualRaster;
        }

        public TTile Current
        {
            get 
            {
                if (this.tile == null)
                {
                    throw new InvalidOperationException();
                }
                return this.tile;
            }
        }

        object IEnumerator.Current
        {
            get { return this.Current; }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // nothing to do
        }

        public bool MoveNext()
        {
            int maxTileIndex = this.virtualRaster.VirtualRasterSizeInTilesX * this.virtualRaster.VirtualRasterSizeInTilesY;
            for (; this.index < maxTileIndex; ++this.index)
            {
                this.tile = this.virtualRaster[this.index];
                if (this.tile != null)
                {
                    return true;
                }
            }

            return false;
        }

        public (int xIndex, int yIndex) GetGridIndices()
        {
            return this.virtualRaster.ToGridIndices(this.index);
        }

        public bool MoveNextThreadSafe()
        {
            lock (this.lockObject) 
            {
                return this.MoveNext();
            }
        }

        public void Reset()
        {
            this.index = -1;
        }
    }
}
