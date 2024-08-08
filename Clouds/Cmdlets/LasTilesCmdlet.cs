using Mars.Clouds.Las;
using System.Collections.Generic;
using System;
using System.Management.Automation;
using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesCmdlet : GdalCmdlet
    {
        protected CancellationTokenSource CancellationTokenSource { get; private init; }

        [Parameter(HelpMessage = "Whether or not to ignore cases where a .las file's header indicates more variable length records (VLRs) than fit between the header and point data. Default is false but this is a common issue with .las files, particularly a two byte fragment between the last VLR and the start of the points.")]
        public SwitchParameter DiscardOverrunningVlrs { get; set; }

        [Parameter(Mandatory = true, HelpMessage = ".las files to load points from. Can be a single file or a set of files if wildcards are used.")]
        [ValidateNotNullOrWhiteSpace]
        public List<string> Las { get; set; }

        [Parameter(HelpMessage = "Number of threads, out of -MaxThreads, to use for reading tiles. Default is automatic estimation, which will typically choose single read thread.")]
        [ValidateRange(1, 32)] // arbitrary upper bound
        public int ReadThreads { get; set; }

        [Parameter(HelpMessage = "Whether to snap tile origin and extent to next largest integer in CRS units. This can be a useful correction in cases where .las file extents are slightly smaller than the actual tile size and, because a 2x2 or larger grid of tiles is not being processed, either a tile's true x extent, y extent, or both cannot be inferred from tile to tile spacing.")]
        public SwitchParameter Snap { get; set; }

        protected LasTilesCmdlet() 
        {
            this.CancellationTokenSource = new();
            this.DiscardOverrunningVlrs = false;
            this.Las = [];
            this.Snap = false;
            this.ReadThreads = -1;
        }

        protected int GetLasTileReadThreadCount(float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs, int minWorkerThreadsPerReadThread)
        {
            if (this.ReadThreads != -1)
            {
                if (this.ReadThreads > this.MaxThreads)
                {
                    throw new ParameterOutOfRangeException(nameof(this.ReadThreads), "-" + nameof(this.ReadThreads) + " is " + this.ReadThreads + " which exceeds the maximum of " + nameof(this.MaxThreads) + " threads. Set -" + nameof(this.ReadThreads) + " and -" + nameof(this.MaxThreads) + " such that the number of read threads is less than or equal to the maximum number of threads.");
                }
                return this.ReadThreads; // nothing to do as user's specified the number of read threads
            }

            if (this.MaxThreads < 1)
            {
                throw new InvalidOperationException("-" + nameof(this.MaxThreads) + " is " + this.MaxThreads + ". At least one thread must be allowed. Is the caller failing to assign a default value to -" + this.MaxThreads + " when it is not user specified?");
            }
            int driveBasedReadThreadEstimate = HardwareCapabilities.Current.GetPracticalReadThreadCount(this.Las, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs);
            return Int32.Min(driveBasedReadThreadEstimate, this.MaxThreads / (1 + minWorkerThreadsPerReadThread));
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(string cmdletName, int? requiredEpsg)
        {
            List<string> lasTilePaths = GdalCmdlet.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            List<LasTile> lasTiles = new(lasTilePaths.Count);

            // due to the small read size asynchronous would probably be more efficient than multithreaded synchronous here
            // The small amount of data transferred means synchronous doesn't take too long, even with hard drives, though. So investigating
            // an asynchronous implementation hasn't been a priority.
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetHeaderBandwidth();
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);
            int tileReadsInitiated = -1;
            ParallelTasks readLasHeaders = new(Int32.Min(readThreads, lasTilePaths.Count), () =>
            {
                for (int tileIndex = Interlocked.Increment(ref tileReadsInitiated); tileIndex < lasTilePaths.Count; tileIndex = Interlocked.Increment(ref tileReadsInitiated))
                {
                    // create tile by reading header and variable length records
                    // FileStream with default 4 kB buffer is used here as a compromise. The LAS header is 227-375 bytes and often only a single
                    // CRS VLR is present with length 54 + 8 + 2 * 8 = 78 bytes if it's GeoKey record with GeoTIFF horizontal and vertical EPSG
                    // tags, meaning only 305-453 bytes need be read. However, if it's an OGC WKT record then it's likely ~5 kB long. Buffer size
                    // when reading LAS headers and VLRs is negligible to long compute runs but can influence tile indexing time by a factor of
                    // 2-3x if overlarge buffers result in unnecessary prefetching.
                    string lasTilePath = lasTilePaths[tileIndex];
                    using LasReader lasReader = LasReader.CreateForHeaderAndVlrRead(lasTilePath, this.DiscardOverrunningVlrs);
                    LasTile lasTile = new(lasTilePath, lasReader, fallbackCreationDate: null);
                    lock (lasTiles)
                    {
                        lasTiles.Add(lasTile);
                    }
                }
            }, new());

            TimedProgressRecord tileIndexProgress = new(cmdletName, "Forming grid of point clouds..."); // can't pass null or empty statusDescription
            this.WriteProgress(tileIndexProgress);
            while (readLasHeaders.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                tileIndexProgress.StatusDescription = "Reading point cloud tile header " + tileReadsInitiated + " of " + lasTilePaths.Count + "...";
                tileIndexProgress.Update(tileReadsInitiated, lasTilePaths.Count);
                this.WriteProgress(tileIndexProgress);
            }

            tileIndexProgress.Stopwatch.Stop();
            LasTileGrid lasGrid = LasTileGrid.Create(lasTiles, this.Snap, requiredEpsg);
            return lasGrid;
        }

        protected override void StopProcessing()
        {
            this.CancellationTokenSource.Cancel();
            base.StopProcessing();
        }
    }
}
