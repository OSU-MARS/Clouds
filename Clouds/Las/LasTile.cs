using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Very thin shell over <see cref="LasFile"/> to support grids of point cloud tiles.
    /// </summary>
    /// <remarks>
    /// Exists only so that <see cref="LasTileGrid"/> can adjust a tile's grid extent.
    /// </remarks>
    public class LasTile : LasFile
    {
        public Extent GridExtent { get; set; }

        public LasTile(string lasFilePath, LasReader reader, DateOnly? fallbackCreationDate)
            : base(lasFilePath, reader, fallbackCreationDate)
        {
            this.GridExtent = new(this.Header.MinX, this.Header.MaxX, this.Header.MinY, this.Header.MaxY); // default extent to bounds declared in .las file header
        }
    }
}
