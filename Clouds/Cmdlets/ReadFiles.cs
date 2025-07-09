using Mars.Clouds.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommunications.Read, "Files")]
    public class ReadFiles : FileCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of directories or wildcarded file paths to read files from.")]
        [ValidateNotNullOrWhiteSpace]
        public List<string> Files { get; set; }

        [Parameter(HelpMessage = "Time to read files for. Default is 10 minutes.")]
        public TimeSpan Duration { get; set; }

        [Parameter(HelpMessage = "Number of threads to use for read. Default is 1.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int Threads { get; set; }

        [Parameter(HelpMessage = "If -Files contains directories or wildcarded paths, read files only from top directory specified (the default) or from all subdirectories.")]
        public SearchOption Search { get; set; }

        [Parameter(HelpMessage = "If an IOException is raised when trying to read a file, ignore it and move on to the next file. Depending on -Files and testing objectives, may be useful for skipping files opened exclusively or locked by other programs.")]
        public SwitchParameter IgnoreIOExceptions { get; set; }

        public ReadFiles()
        {
            this.Duration = TimeSpan.FromMinutes(10.0);
            this.Threads = 1;
            this.Files = [];
            this.Search = SearchOption.TopDirectoryOnly;
        }

        protected override void ProcessRecord()
        {
            // Can check drive capabilities and do automatic thread setting if needed.
            List<string> filePathsToRead = this.GetExistingFilePaths(this.Files, ".*", this.Search);

            long totalBytesRead = 0;
            bool durationElapsedOrFaulted = false;
            int fileReadIndex = -1;
            ParallelTasks readFiles = new(this.Threads, () =>
            {
                byte[] readBuffer = new byte[1024 * 1024];
                while (durationElapsedOrFaulted == false)
                {
                    int fileIndex = Interlocked.Increment(ref fileReadIndex) % filePathsToRead.Count;
                    if (durationElapsedOrFaulted || this.CancellationTokenSource.IsCancellationRequested || this.Stopping)
                    {
                        return;
                    }

                    // for now, just do buffered sequential reads
                    // 5950X CPU lanes
                    // drive    threads   read, GB/s
                    // SN850X   1         4.1
                    // SN850X   2         5.9
                    // SN850X   3         6.6
                    // SN850X   4         6.9
                    // SN850X   8         7.1
                    long fileBytesRead = 0;
                    try
                    {
                        string filePath = filePathsToRead[fileIndex];
                        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 2 * 1024 * 1024, useAsync: false);
                        for (int bytesRead = stream.Read(readBuffer, 0, readBuffer.Length); bytesRead > 0; bytesRead = stream.Read(readBuffer, 0, readBuffer.Length))
                        {
                            fileBytesRead += bytesRead;
                        }
                    }
                    catch (IOException)
                    {
                        if (this.IgnoreIOExceptions == false)
                        {
                            throw;
                        }
                    }

                    Interlocked.Add(ref totalBytesRead, fileBytesRead);
                }
            }, this.CancellationTokenSource);

            float gibibytesRead;
            TimedProgressRecord progress = new("Read-Files", "placeholder");
            double totalSeconds = this.Duration.TotalSeconds;
            try
            {
                while (readFiles.WaitAll(Constant.DefaultProgressInterval) == false)
                {
                    double secondsRemaining = (this.Duration - progress.Stopwatch.Elapsed).TotalSeconds;
                    if (secondsRemaining < 1.0)
                    {
                        durationElapsedOrFaulted = true;
                    }
                    gibibytesRead = totalBytesRead / (1024.0F * 1024.0F * 1024.0F);
                    progress.PercentComplete = Int32.Min((int)(100.0 * (1.0 - secondsRemaining / totalSeconds)), 100);
                    progress.SecondsRemaining = secondsRemaining > 0.0 ? (int)secondsRemaining : 0;
                    progress.StatusDescription = gibibytesRead.ToString("0.0") + " GiB read from " + filePathsToRead.Count + " files (" + readFiles.Count + " threads)...";
                    this.WriteProgress(progress);
                }
            }
            finally
            {
                // signal all other reading threads to stop if WaitAll() throws because a read thread faults
                durationElapsedOrFaulted = true;
            }
            
            progress.Stopwatch.Stop();
            gibibytesRead = totalBytesRead / (1024.0F * 1024.0F * 1024.0F);
            float gibibytesPerSecond = gibibytesRead / (float)progress.Stopwatch.Elapsed.TotalSeconds;
            float gigabytesRead = 1E-9F * totalBytesRead;
            float gigabytesPerSecond = gigabytesRead / (float)progress.Stopwatch.Elapsed.TotalSeconds;
            this.WriteVerbose(gibibytesRead.ToString("0.0") + " GiB read in " + progress.Stopwatch.ToElapsedString() + " by " + readFiles.Count + " threads (" + gibibytesPerSecond.ToString("0.00") + " GiB/s, " + gigabytesPerSecond.ToString("0.00") + " GB/s).");
            base.ProcessRecord();
        }
    }
}
