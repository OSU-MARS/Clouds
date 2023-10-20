using Mars.Clouds.GdalExtensions;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Thin shell over <see cref="LasFile"/> to support grids of point cloud tiles.
    /// </summary>
    public class LasTile : LasFile
    {
        public string FilePath { get; private init; }
        public long FileSize { get; private init; }
        public Extent GridExtent { get; set; }

        public LasTile(string lasFilePath, LasReader reader)
            : base(reader)
        {
            this.FilePath = lasFilePath;
            this.FileSize = reader.BaseStream.BaseStream.Length;
            this.GridExtent = new(this.Header.MinX, this.Header.MaxX, this.Header.MinY, this.Header.MaxY);
        }

        public LasReader CreatePointReader()
        {
            // rough scaling with file size from https://github.com/dotnet/runtime/discussions/74405#discussioncomment-3488674
            int bufferSizeInKB;
            if (this.FileSize > 512 * 1024 * 1024) // 512 MB
            {
                bufferSizeInKB = 1024;
            }
            else if (this.FileSize > 64 * 1024 * 1024) // 64 MB
            {
                bufferSizeInKB = 512;
            }
            else if (this.FileSize > 8 * 1024 * 1024) // 8 MB
            {
                bufferSizeInKB = 256;
            }
            else
            {
                bufferSizeInKB = 128;
            }

            FileStream stream = new(this.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSizeInKB * 1024);
            LasReader reader = new(stream);
            reader.BaseStream.BaseStream.Seek(this.Header.OffsetToPointData, SeekOrigin.Begin);
            return reader;
        }
    }
}
