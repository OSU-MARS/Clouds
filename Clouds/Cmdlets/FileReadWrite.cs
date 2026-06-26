using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Track input file reads and output file writes when each output file uses only data in the corresponding input file.
    /// </summary>
    public class FileReadWrite : FileRead
    {
        // explicit fields because Interlocked.Increment() requires a ref parameter and auto-properties cannot be passed by ref
        private int filesWritten;
        private int fileWriteIndex;

        public bool OutputPathIsDirectory { get; private init; }

        public FileReadWrite(bool outputPathIsDirectory)
        {
            this.filesWritten = 0;
            this.fileWriteIndex = -1;

            this.OutputPathIsDirectory = outputPathIsDirectory;
        }

        public int FilesWritten 
        { 
            get { return this.filesWritten; }
            set { this.filesWritten = value; }
        }

        public int GetNextFileWriteIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.fileWriteIndex);
        }

        public virtual string GetPointCloudReadFileWriteStatusDescription(int lasFilesToRead, int activeReadThreads, int totalThreads)
        {
            return $"{this.FilesRead} point {(this.FilesRead == 1 ? "cloud" : "clouds")} read, {this.FilesWritten} of {lasFilesToRead} tiles written " +
                   $"({totalThreads} {(totalThreads == 1 ? "thread" : "threads")}, {activeReadThreads} reading)...";
        }

        public int IncrementFilesWrittenThreadSafe()
        {
            return Interlocked.Increment(ref this.filesWritten);
        }
    }
}
