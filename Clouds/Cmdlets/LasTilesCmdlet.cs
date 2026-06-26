using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public abstract class LasTilesCmdlet : LasFilesCmdlet
    {
        [Parameter(HelpMessage = "Whether to snap tile origin and extent to next largest integer in CRS units. This can be a useful correction in cases where .las file extents are slightly smaller than the actual tile size and, because a 2x2 or larger grid of tiles is not being processed, either a tile's true x extent, y extent, or both cannot be inferred from tile to tile spacing.")]
        public SwitchParameter Snap { get; set; }

        protected LasTilesCmdlet() 
        {
            this.Snap = false;
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(string cmdletName)
        {
            List<string> lasTilePaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            List<LasTile> lasTiles = new(lasTilePaths.Count);

            // due to the small read size asynchronous would probably be more efficient than multithreaded synchronous here
            // The small amount of data transferred means synchronous doesn't take too long, even with hard drives, though. So investigating
            // an asynchronous implementation hasn't been a priority.
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetHeaderBandwidth();
            int readThreads = this.GetPointCloudReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs);
            int tileReadsInitiated = -1;
            ParallelTasks readLasHeaders = new(Int32Extensions.Min(readThreads, lasTilePaths.Count, this.MetadataThreads), () =>
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
                    LasTile lasTile = new(lasTilePath, lasReader, this.FallbackDate);
                    lock (lasTiles)
                    {
                        lasTiles.Add(lasTile);
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord tileIndexProgress = new(cmdletName, $"Reading point cloud tile header {tileReadsInitiated} of {lasTilePaths.Count} ({readLasHeaders.Count} {(readLasHeaders.Count == 1 ? "thread" : "threads")})..."); // can't pass null or empty statusDescription
            this.WriteProgress(tileIndexProgress);
            while (readLasHeaders.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                tileIndexProgress.StatusDescription = $"Reading point cloud tile header {tileReadsInitiated} of {lasTilePaths.Count} ({readLasHeaders.Count} {(readLasHeaders.Count == 1 ? "thread" : "threads")})...";
                tileIndexProgress.Update(tileReadsInitiated, lasTilePaths.Count);
                this.WriteProgress(tileIndexProgress);
            }

            tileIndexProgress.Stopwatch.Stop();
            LasTileGrid lasGrid = LasTileGrid.Create(lasTiles, this.Snap);
            return lasGrid;
        }
    }
}
