using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata;
using System.Runtime;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dtm")]
    public class GetDtm : LasTilesToTilesCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Path to write DTM to as a GeoTIFF or path to a directory to write DTM tiles to. The DTM's cell size is specified by -CellSize and its raster grid is aligned to the point clouds' bounding boxes.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Size of a DTM in the point clouds' CRS units. Must be an integer multiple of the point cloud size. Default is 0.5 m for metric point clouds and 1.5 feet for point clouds with English units.")]
        public double CellSize { get; set; }

        [Parameter(HelpMessage = "If set, DTM cells without any ground classified points will be interpolated by inverse linear distance weighting of the nearest cardinal neighbors. If no neighbor is found in a cardinal raster direction the remaining directions are used, potentially collapsing to taking the value of a single nearest neighbor.")]
        public SwitchParameter FillNoData { get; set; }

        [Parameter(HelpMessage = "Include point cloud band when writing DTM tiles to disk.")]
        public SwitchParameter PointCounts { get; set; }

        [Parameter(HelpMessage = "Indicates the input point clouds are a tiled dataset and thus that the DTM should be generated as a virtual raster (.vrt).")]
        public SwitchParameter Vrt { get; set; }

        public GetDtm()
        {
            // leave this.DataThreads at default
            this.Dtm = String.Empty;
            this.FillNoData = false;
            // leave this.MetadataThreads at default
            this.PointCounts = false;
            this.Vrt = false;
        }

        public override string GetName()
        {
            return $"{VerbsCommon.Get}-Dtm";
        }

        protected override void ProcessRecord()
        {
            if (Fma.IsSupported == false)
            {
                throw new NotSupportedException("Generation of a canopy maxima model requires FMA instructions (and DSM subsurface generation uses AVX).");
            }

            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            if (this.DataThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), $"-{nameof(this.DataThreads)} must be at least two.");
            }

            string cmdletName = this.GetName();
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            int dtmTileSizeX = -1;
            int dtmTileSizeY = -1;
            LasTileGrid? cloudGrid = null;
            List<string>? cloudPaths = null;
            int pointCloudsToRead;
            int pointCloudsOrGridPositionsToRead;
            if (this.Vrt)
            {
                cloudGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dtm), dtmPathIsDirectory);
                (this.CellSize, dtmTileSizeX, dtmTileSizeY) = LasTilesToTilesCmdlet.GetRasterSizing(cloudGrid, this.CellSize);
                pointCloudsToRead = cloudGrid.NonNullCells;
                pointCloudsOrGridPositionsToRead = cloudGrid.Cells;
            }
            else
            {
                cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
                pointCloudsToRead = cloudPaths.Count;
                pointCloudsOrGridPositionsToRead = cloudPaths.Count;
            }

            FileReadWrite dtmReadWrite = new(dtmPathIsDirectory);

            // estimate read thread count and number of useful threads for DTM interpolation
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToDtmBandwidth(this.Unbuffered);
            int readThreads = this.GetPointCloudReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs);

            // TODO: tune completion thread count for DTM generation with and without interpolation
            int preferredCompletionThreadsAsymptotic = Int32.Min(this.DataThreads - readThreads, Int32.Max(readThreads / 3, 2)); 
            int preferredCompletionThreadsWithFewTiles = Int32.Min(this.DataThreads, 25 - (int)(1.5F * pointCloudsOrGridPositionsToRead));
            int tileCompletionThreads = Int32.Min(pointCloudsOrGridPositionsToRead, Int32.Max(preferredCompletionThreadsWithFewTiles, preferredCompletionThreadsAsymptotic));
            Debug.Assert(readThreads + tileCompletionThreads <= this.DataThreads);

            long cellsWritten = 0;
            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dtmTasks = new(readThreads + tileCompletionThreads, () =>
            {
                DigitalTerrainModel? dtmTile = null;
                RasterBand<float>? noDataFillBuffer = null;
                byte[]? pointReadBuffer = null;
                readSemaphore.Wait(this.CancellationTokenSource.Token);
                if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                {
                    // task is cancelled so no point in looking for a tile to read
                    // Also not valid to continue if the read semaphore wasn't entered due to cancellation.
                    return;
                }

                for (int lasFileIndex = dtmReadWrite.GetNextFileReadIndexThreadSafe(); lasFileIndex < pointCloudsOrGridPositionsToRead; lasFileIndex = dtmReadWrite.GetNextFileReadIndexThreadSafe())
                {
                    LasReader? lasReader = null;
                    try
                    {
                        double dtmCellSize;
                        SpatialReference dtmCrs;
                        int dtmSizeInCellsX;
                        int dtmSizeInCellsY;
                        LasFile? lasFile;
                        if (this.Vrt)
                        {
                            Debug.Assert(cloudGrid != null);
                            lasFile = cloudGrid[lasFileIndex];
                            if (lasFile == null)
                            {
                                continue; // nothing to do as no tile is present at this grid position
                            }
                            lasReader = lasFile.CreatePointReader(this.Unbuffered, enableAsync: false);

                            // copy grid properties to local thread variables
                            // Local copies aren't needed when producing a virtual raster as the grid of .las files has constant properties but are required when processing a set of 
                            // loose .las files as individual files may well be in different coordinate systems and are likely to have different extents unless extracted from larger
                            // scans.
                            dtmCellSize = this.CellSize;
                            dtmCrs = cloudGrid.Crs;
                            dtmSizeInCellsX = dtmTileSizeX;
                            dtmSizeInCellsY = dtmTileSizeY;
                        }
                        else
                        {
                            Debug.Assert(cloudPaths != null);
                            string cloudPath = cloudPaths[lasFileIndex];
                            lasReader = LasReader.CreateForPointRead(cloudPath, this.DiscardOverrunningVlrs);
                            lasFile = new(cloudPath, lasReader, this.FallbackDate);
                            dtmCrs = lasFile.GetSpatialReference();
                            double lasFileExtentX = lasFile.Header.MaxX - lasFile.Header.MinX;
                            double lasFileExtentY = lasFile.Header.MaxY - lasFile.Header.MinY;

                            (dtmCellSize, dtmSizeInCellsX, dtmSizeInCellsY) = LasTilesToTilesCmdlet.GetRasterSizing(dtmCrs, this.CellSize, lasFileExtentX, lasFileExtentY);
                        }

                        string tileName = Tile.GetName(lasFile.FilePath);
                        string dtmTilePath = dtmReadWrite.OutputPathIsDirectory ? Path.Combine(this.Dtm, tileName + Constant.File.GeoTiffExtension) : this.Dtm;
                        dtmTile = DigitalTerrainModel.CreateRecreateOrReset(dtmTile, lasFile, dtmCrs, dtmCellSize, dtmSizeInCellsX, dtmSizeInCellsY, dtmTilePath);
                        lasReader.ReadGroundPointsToDtm(lasFile, dtmTile, ref pointReadBuffer);
                    }
                    finally
                    {
                        lasReader?.Dispose();
                    }
                    readSemaphore.Release(); // should not be in finally block due to use of continue within try { }
                    dtmReadWrite.IncrementFilesReadThreadSafe();

                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // if cmdlet's been stopped with ctrl+c the read semaphore may already be disposed
                    }

                    // calculate means and interpolate missing values if requested
                    dtmTile.OnPointAdditionComplete();
                    if (this.FillNoData)
                    {
                        dtmTile.FillNoDataFromCardinalDistance(ref noDataFillBuffer);
                    }
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // no point in writing tile
                    }

                    // write tile to disk
                    if (this.NoWrite == false)
                    {
                        dtmTile.Write(dtmTile.FilePath, this.PointCounts, this.CompressRasters);
                        lock (dtmReadWrite)
                        {
                            cellsWritten += dtmTile.Cells;
                            ++dtmReadWrite.FilesWritten;
                        }
                    }

                    // reacquire read semaphore for next tile
                    // Unnecessary on last loop iteration but no good way to identify if this is the last iteration.
                    readSemaphore.Wait(this.CancellationTokenSource.Token);
                }

                // ensure read semaphore is released
                // Reads have been initiated on all tiles at this point but it's probable there's one or more threads blocked in Wait() which
                // must acquire the semaphore to exit.
                readSemaphore.Release();
            }, this.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, dtmReadWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dtmTasks.Count));
            this.WriteProgress(progress);
            while (dtmTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = dtmReadWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dtmTasks.Count);
                progress.Update(dtmReadWrite.FilesWritten, pointCloudsToRead);
                this.WriteProgress(progress);
            }

            // TODO: generate .vrt if -Vrt is set

            // release point batches and trigger a gen 2 collection
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            progress.Stopwatch.Stop();
            this.WriteVerbose($"{cellsWritten:n0} DTM cells in {dtmReadWrite.FilesRead} point cloud {(dtmReadWrite.FilesRead == 1 ? "tile" : "tiles")} in {progress.Stopwatch.ToElapsedString()}: {dtmReadWrite.FilesWritten / progress.Stopwatch.Elapsed.TotalSeconds:0.0} tiles/s.");
            base.ProcessRecord();
        }
    }
}
