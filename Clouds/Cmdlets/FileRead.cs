using DocumentFormat.OpenXml.Drawing;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    /// <summary>
    /// Track input file reads for a workload whose output is either not tiled (monolithic) or modifies files in place rather than
    /// writing separate files.
    /// </summary>
    public class FileRead
    {
        // explicit fields because Interlocked.Increment() requires a ref parameter and auto-properties cannot be passed by ref
        private int fileReadIndex;
        private int filesRead;

        public FileRead()
        {
            this.fileReadIndex = -1;
            this.filesRead = 0;
        }

        public int FileReadIndex
        {
            get { return this.fileReadIndex; }
        }

        public int FilesRead
        {
            get { return this.filesRead; }
            set { this.filesRead = value; }
        }

        public int GetNextFileReadIndexThreadSafe()
        {
            return Interlocked.Increment(ref this.fileReadIndex);
        }

        public virtual string GetPointCloudReadStatusDescription(int pointCloudsToRead, int readThreads)
        {
            return $"{this.FilesRead} point {(this.FilesRead == 1 ? "cloud" : "clouds")} read of {pointCloudsToRead} {(pointCloudsToRead == 1 ? "cloud" : "clouds")} ({readThreads} {(readThreads == 1 ? " thread" : " threads")})...";
        }

        public int IncrementFilesReadThreadSafe()
        {
            return Interlocked.Increment(ref this.filesRead);
        }
    }
}
