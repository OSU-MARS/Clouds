using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Track input file reads and output file writes when each output file uses only data in the corresponding input file.
    /// </summary>
    public class FileReadWrite : FileRead
    {
        private int fileWriteIndex;

        public int FilesWritten { get; set; }
        public bool OutputPathIsDirectory { get; private init; }

        public FileReadWrite(bool outputPathIsDirectory)
        {
            this.fileWriteIndex = -1;

            this.FilesWritten = 0;
            this.OutputPathIsDirectory = outputPathIsDirectory;
        }

        public int GetNextFileWriteIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.fileWriteIndex);
        }

        public virtual string GetPointCloudReadFileWriteStatusDescription(int lasFilesToRead, int activeReadThreads, int totalThreads)
        {
            return $"{this.FilesRead} point {(this.FilesRead == 1 ? "cloud" : "clouds ")} read, {this.FilesWritten} of {lasFilesToRead} tiles written " +
                   $"({totalThreads} {(totalThreads == 1 ? " thread" : " threads")}, {activeReadThreads} reading)...";
        }
    }
}
