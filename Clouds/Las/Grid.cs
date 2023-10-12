using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Las
{
    public class Grid<TCell> where TCell : class, new()
    {
        private readonly TCell?[] cells;

        public SpatialReference Crs { get; private init; }
        public RasterGeoTransform Transform { get; private init; }
        public int XSize { get; protected set; }
        public int YSize { get; protected set; }

        public Grid(Raster<UInt16> mask)
        {
            this.cells = new TCell?[mask.XSize * mask.YSize];
            mask.Crs.ExportToWkt(out string wkt, Array.Empty<string>());
            this.Crs = new(wkt);
            this.Transform = new(mask.Transform);
            this.XSize = mask.XSize;
            this.YSize = mask.YSize;

            for (int yIndex = 0; yIndex < mask.YSize; ++yIndex)
            {
                int rowGridIndexStart = yIndex * mask.XSize;
                int rowGridIndexStop = (yIndex + 1) * mask.XSize;
                for (int gridIndex = rowGridIndexStart; gridIndex < rowGridIndexStop; ++gridIndex) 
                {
                    this.cells[gridIndex] = new();
                }
            }
        }

        public TCell? this[int xIndex, int yIndex]
        {
            get { return this.cells[xIndex + yIndex * this.XSize]; }
        }
    }
}
