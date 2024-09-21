using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class VirtualLayer
    {
        private SpatialReference? crs;

        public int NonNullTileCount { get; protected set; }

        public int SizeInTilesX { get; protected set; }
        public int SizeInTilesY { get; protected set; }

        public VirtualLayer() 
        {
            this.crs = null;

            this.NonNullTileCount = 0;
            this.SizeInTilesX = -1;
            this.SizeInTilesY = -1;
        }

        public VirtualLayer(VirtualLayer other)
        {
            this.crs = other.Crs.Clone();

            this.NonNullTileCount = 0;
            this.SizeInTilesX = other.SizeInTilesX;
            this.SizeInTilesY = other.SizeInTilesY;
        }

        public SpatialReference Crs
        {
            get
            {
                if (this.crs == null)
                {
                    throw new InvalidOperationException("Virtual raster's CRS is unknown as no tiles have been added to it. Call Add() before accessing " + nameof(this.Crs) + " { get; }.");
                }
                return this.crs;
            }
            protected set { this.crs = value; }
        }

        protected bool HasCrs
        {
            get { return this.crs != null; }
        }

        public abstract GridGeoTransform TileTransform { get; }
    }
}
