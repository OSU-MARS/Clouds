using Mars.Clouds.GdalExtensions;
using System;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Thin shell over <see cref="LasFile"/> to support grids of point cloud tiles.
    /// </summary>
    public class LasTile : LasFile
    {
        public string FilePath { get; private init; }
        public long FileSizeInBytes { get; private init; }
        public Extent GridExtent { get; set; }

        public LasTile(string lasFilePath, LasReader reader, DateOnly? fallbackCreationDate)
            : base(reader, fallbackCreationDate)
        {
            this.FilePath = lasFilePath;
            this.FileSizeInBytes = reader.BaseStream.Length;
            this.GridExtent = new(this.Header.MinX, this.Header.MaxX, this.Header.MinY, this.Header.MaxY);
        }

        public LasReader CreatePointReader()
        {
            LasReader reader = LasReader.CreateForPointRead(this.FilePath, this.FileSizeInBytes);
            reader.BaseStream.Seek(this.Header.OffsetToPointData, SeekOrigin.Begin);
            return reader;
        }

        public LasReader CreatePointReaderAsync()
        {
            LasReader reader = LasReader.CreateForPointReadAsync(this.FilePath, this.FileSizeInBytes);
            reader.BaseStream.Seek(this.Header.OffsetToPointData, SeekOrigin.Begin);
            return reader;
        }

        public LasReaderWriter CreatePointReaderWriter()
        {
            LasReaderWriter readerWriter = LasReaderWriter.CreateForPointRead(this.FilePath, this.FileSizeInBytes);
            readerWriter.BaseStream.Seek(this.Header.OffsetToPointData, SeekOrigin.Begin);
            return readerWriter;
        }
    }
}
