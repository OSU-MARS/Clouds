using System;
using System.IO;

namespace Mars.Clouds.Las
{
    public class LasStream : IDisposable
    {
        private bool isDisposed;

        public Stream BaseStream { get; private init; }

        protected LasStream(Stream stream)
        {
            this.BaseStream = stream;
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
                    this.BaseStream.Dispose();
                }

                this.isDisposed = true;
            }
        }

        public void MoveToPoints(LasFile file)
        {
            if (file.IsPointFormatCompressed())
            {
                throw new ArgumentOutOfRangeException(nameof(file), ".laz files are not currently supported.");
            }

            LasHeader10 lasHeader = file.Header;
            if (this.BaseStream.Position != lasHeader.OffsetToPointData)
            {
                this.BaseStream.Seek(lasHeader.OffsetToPointData, SeekOrigin.Begin);
            }
        }
    }
}
