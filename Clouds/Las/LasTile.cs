using System;
using System.IO;

namespace Mars.Clouds.Las
{
    public class LasTile : IDisposable
    {
        private bool isDisposed;

        public LasFile File { get; private init; }
        public LasReader Reader { get; private init; }

        public LasTile(string lasFilePath)
        {
            this.isDisposed = false;

            FileStream stream = new(lasFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.SequentialScan);
            this.Reader = new(stream);

            this.File = this.Reader.ReadHeader();
            this.Reader.ReadVariableLengthRecords(this.File);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.Reader.Dispose();
                }

                this.isDisposed = true;
            }
        }
    }
}
