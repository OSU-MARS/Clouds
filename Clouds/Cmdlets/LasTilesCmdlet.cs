using Mars.Clouds.Las;
using System.Collections.Generic;
using System.IO;
using System;
using System.Management.Automation;
using Mars.Clouds.Cmdlets.Drives;
using Mars.Clouds.Extensions;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesCmdlet : GdalCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to ignore cases where a .las file's header indicates more variable length records (VLRs) than fit between the header and point data. Default is false but this is a common issue with .las files, particularly a two byte fragment between the last VLR and the start of the points.")]
        public SwitchParameter DiscardOverrunningVlrs { get; set; }

        [Parameter(Mandatory = true, HelpMessage = ".las files to load points from. Can be a single file or a set of files if wildcards are used.")]
        [ValidateNotNullOrWhiteSpace]
        public List<string> Las { get; set; }

        // quick solution for constraining memory consumption
        // A more adaptive implementation would track memory consumption (e.g. GlobalMemoryStatusEx()), estimate it from the size of the
        // tiles, or use a rasonably stable bound such as the total number of points loaded.
        [Parameter(HelpMessage = "Maximum number of point cloud tiles to have fully loaded at the same time (default is 25 or the estimaated number of physical cores, whichever is higher). This is a safeguard to constrain maximum memory consumption in situations where tiles are loaded faster than point metrics can be calculated.")]
        public int MaxPointTiles { get; set; }

        [Parameter(HelpMessage = "Number of threads, out of -MaxThreads, to use for reading tiles. Default is automatic estimation, which will typically choose single read thread.")]
        [ValidateRange(1, 32)] // arbitrary upper bound
        public int ReadThreads { get; set; }

        [Parameter(HelpMessage = "Whether to snap tile origin and extent to next largest integer in CRS units. This can be a useful correction in cases where .las file extents are slightly smaller than the actual tile size and, because a 2x2 or larger grid of tiles is not being processed, either a tile's true x extent, y extent, or both cannot be inferred from tile to tile spacing.")]
        public SwitchParameter Snap { get; set; }

        protected LasTilesCmdlet() 
        {
            this.DiscardOverrunningVlrs = false;
            this.Las = [];
            this.MaxPointTiles = Int32.Max(25, Environment.ProcessorCount / 2);
            this.Snap = false;
            this.ReadThreads = -1;
        }

        protected int GetLasTileReadThreadCount(DriveCapabilities driveCapabilities, float readSpeedPerThreadInGBs, int minWorkerThreadsPerReadThread)
        {
            if (this.ReadThreads == -1)
            {
                int driveBasedReadThreadEstimate = driveCapabilities.GetPracticalReadThreadCount(readSpeedPerThreadInGBs);
                this.ReadThreads = Int32.Min(driveBasedReadThreadEstimate, this.MaxThreads / (1 + minWorkerThreadsPerReadThread));
            }

            return this.ReadThreads;
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(string cmdletName, DriveCapabilities driveCapabilities, int? requiredEpsg)
        {
            List<string> lasTilePaths = GdalCmdlet.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            int readThreads = this.GetLasTileReadThreadCount(driveCapabilities, DriveCapabilities.HardDriveDefaultTransferRateInGBs, minWorkerThreadsPerReadThread: 0);

            List<LasTile> lasTiles = new(lasTilePaths.Count);
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

            LasTileGrid lasGrid = LasTileGrid.Create(lasTiles, this.Snap, requiredEpsg);
            return lasGrid;
        }

        protected void ValidateParameters(int minWorkerThreads)
        {
            int maxReadThreads = this.MaxThreads - minWorkerThreads;
            if (this.ReadThreads > maxReadThreads)
            {
                throw new ParameterOutOfRangeException(nameof(this.ReadThreads), "-" + nameof(this.ReadThreads) + " (" + this.ReadThreads + " threads) must be less than -" + nameof(this.MaxThreads) + " (" + this.MaxThreads + " threads) in order for at least one worker thread to process the tiles being read.");
            }
        }
    }
}
