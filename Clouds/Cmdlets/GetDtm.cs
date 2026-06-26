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
using System.Runtime;
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

        [Parameter(HelpMessage = "Maximum distance, in DTM CRS units, to search for data values when filling no data values. Default is 3 m for metric point clouds and 10 feet for point clouds with English units.")]
        [ValidateRange(0.0F, 10000)] // sanity upper bound
        public double FillDistance { get; set; }

        [Parameter(HelpMessage = "If fill power is greater than zero, search only cardinal directions (north, south, east, and west) for values to interpolate rathern than cardinal and ordinal (northeast, northwest, southeast, and southwest) directions. Searching cardinal and ordinal (the default) is usually preferred as it's generally more likely to find closer neighbors.")]
        public SwitchParameter FillFromCardinalOnly { get; set; }

        [Parameter(HelpMessage = $"If one (default), DTM cells without any ground classified points will be interpolated by inverse linear distance weighting of the nearest cardinal neighbors (north, south, east, and west, where present). If two, no data DTM cells will be interpolated by inverse squared distance weighting of the nearest cardinal neighbors. Filling is performed iteratively, up to -{nameof(GetDtm.FillDistance)}, until all no data cells have been interpolated. Stenciling and smoothing iterations are likely to be useful after interpolation, especially when ground hits are few. Inverse squared distance may track complex terrain somewhat more closely than inverse linear distance.")]
        [ValidateRange(0, 2)]
        public int FillPower { get; set; }

        [Parameter(HelpMessage = "Include point cloud band when writing DTM tiles to disk.")]
        public SwitchParameter PointCounts { get; set; }

        [Parameter(HelpMessage = $"Number of stenciling iterations to apply to the DTM (after interpolation if -{nameof(GetDtm.FillPower)} is specified, default is zero iterations). Each iteration applies a 3x3 lowpass filter (simple moving average) to smooth interpolated data values and then reapplies the ground values obtained from the input point cloud to snap those locations back to measured heights. The useful number of iterations decreases as the consistency of ground resolution increases, with success in filling data voids with interpolation, and with rugosity in the true ground surface. Some starting points are 5 stencil and 1 smoothing iteration, 10 and 2, 25 and 5, and 50 and 5 iterations depending on the amount of smoothing needed.")]
        [ValidateRange(0, 100000)] // sanity upper bound
        public int StencilIterations { get; set; }

        [Parameter(HelpMessage = $"Number of smoothing iterations to apply to the DTM (after interpolation if -{nameof(GetDtm.FillPower)} is specified and after stenciling if -{nameof(GetDtm.StencilIterations)} is greater than zero, default is zero iterations). Each smoothing iteration also runs a 3x3 lowpass but, unlike stenciling, data values are not restored after smoothing.")]
        [ValidateRange(0, 100000)] // sanity upper bound
        public int SmoothIterations { get; set; }

        [Parameter(HelpMessage = "Indicates the input point clouds are a tiled dataset and thus that the DTM should be generated as a virtual raster (.vrt).")]
        public SwitchParameter Vrt { get; set; }

        public GetDtm()
        {
            this.CellSize = Double.NaN;
            // leave this.DataThreads at default
            this.Dtm = String.Empty;
            this.FillDistance = Double.NaN;
            this.FillPower = 1;
            // leave this.MetadataThreads at default
            this.PointCounts = false;
            this.StencilIterations = 0;
            this.SmoothIterations = 0;
            this.Vrt = false;
        }

        public override string GetName()
        {
            return $"{VerbsCommon.Get}-Dtm";
        }

        protected override void ProcessRecord()
        {
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
                (this.CellSize, dtmTileSizeX, dtmTileSizeY) = LasTilesToTilesCmdlet.GetTileRasterSizing(cloudGrid, this.CellSize); // snapping handled within this.ReadLasHeadersAndFormGrid()
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

            // TODO: tune completion thread count for DTM generation based on interpolation and smoothing settings
            int preferredCompletionThreadsAsymptotic = Int32.Min(this.DataThreads - readThreads, Int32.Max(readThreads / 3, 2)); 
            int preferredCompletionThreadsWithFewTiles = Int32.Min(this.DataThreads, 25 - (int)(1.5F * pointCloudsOrGridPositionsToRead));
            int tileCompletionThreads = Int32.Min(Int32.Max(preferredCompletionThreadsWithFewTiles, preferredCompletionThreadsAsymptotic), this.DataThreads - readThreads);
            Debug.Assert(readThreads + tileCompletionThreads <= this.DataThreads);

            int totalThreads = Int32.Min(readThreads + tileCompletionThreads, pointCloudsOrGridPositionsToRead);

            long cellsWritten = 0;
            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dtmTasks = new(totalThreads, () =>
            {
                DigitalTerrainModel? dtmTile = null;
                RasterBand<float>? dtmBuffer1 = null;
                RasterBand<float>? dtmBuffer2 = null;
                byte[]? pointReadBuffer = null;
                RowBuffers? smoothingRowBuffers = null;

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
                    int maxFillDistanceInRasterCells;
                    try
                    {
                        double dtmCellSizeInCrsUnits;
                        SpatialReference dtmCrs;
                        double dtmOriginX;
                        double dtmOriginY;
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
                            (int gridX, int gridY) = cloudGrid.ToGridIndices(lasFileIndex);
                            dtmCellSizeInCrsUnits = this.CellSize;
                            dtmCrs = cloudGrid.Crs;
                            dtmOriginX = cloudGrid.Transform.OriginX + gridX * cloudGrid.Transform.CellWidth;
                            dtmOriginY = cloudGrid.Transform.OriginY + gridY * cloudGrid.Transform.CellHeight;
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
                            dtmOriginX = lasFile.Header.MinX;
                            double dtmMaxX = lasFile.Header.MaxX;
                            dtmOriginY = lasFile.Header.MaxY;
                            double dtmMinY = lasFile.Header.MinY;

                            if (this.Snap)
                            {
                                // if needed, support can be added for snapping to mutiples of cell sizes rather than CRS units
                                dtmOriginX = Double.Floor(dtmOriginX);
                                dtmMaxX = Double.Ceiling(dtmMaxX);
                                dtmOriginY = Double.Ceiling(dtmOriginY);
                                dtmMinY = Double.Floor(dtmMinY);
                            }
                            double dtmExtentX = dtmMaxX - dtmOriginX;
                            double dtmExtentY = dtmOriginY - dtmMinY;

                            (dtmCellSizeInCrsUnits, dtmSizeInCellsX, dtmSizeInCellsY) = LasTilesToTilesCmdlet.GetRasterSizing(dtmCrs, this.CellSize, dtmExtentX, dtmExtentY);
                        }

                        string tileName = Tile.GetName(lasFile.FilePath);
                        string dtmTilePath = dtmReadWrite.OutputPathIsDirectory ? Path.Combine(this.Dtm, tileName + Constant.File.GeoTiffExtension) : this.Dtm;
                        GridGeoTransform dtmTransform = new(dtmOriginX, dtmOriginY, dtmCellSizeInCrsUnits, -dtmCellSizeInCrsUnits);
                        dtmTile = DigitalTerrainModel.CreateRecreateOrReset(dtmTile, dtmCrs, dtmTransform, dtmSizeInCellsX, dtmSizeInCellsY, dtmTilePath);
                        lasReader.ReadGroundPointsToDtm(lasFile, dtmTile, ref pointReadBuffer);

                        double dtmFillDistanceInCrsUnits = this.FillDistance;
                        if (Double.IsNaN(dtmFillDistanceInCrsUnits))
                        {
                            double crsProjectedLinearUnitInM = dtmCrs.GetProjectedLinearUnitInM();
                            dtmFillDistanceInCrsUnits = crsProjectedLinearUnitInM == 1.0 ? 3.0 : 10.0; // 3 m or 10 feet
                        }
                        maxFillDistanceInRasterCells = (int)Math.Round(dtmFillDistanceInCrsUnits / dtmCellSizeInCrsUnits);
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

                    // calculate means
                    dtmTile.OnPointAdditionComplete();

                    // interpolate missing values
                    bool noSmoothing = (this.SmoothIterations == 0) && (this.StencilIterations == 0);
                    int noDataValues = -1;
                    if (this.FillPower == 1)
                    {
                        if (this.FillFromCardinalOnly)
                        {
                            noDataValues = dtmTile.FillNoDataFromCardinalLinearDistances(maxFillDistanceInRasterCells, ref dtmBuffer1, ref dtmBuffer2);
                        }
                        else
                        {
                            noDataValues = dtmTile.FillNoDataFromCardinalAndOrdinalLinearDistances(maxFillDistanceInRasterCells, ref dtmBuffer1, ref dtmBuffer2);
                        }
                        if (noSmoothing)
                        {
                            Debug.Assert(dtmBuffer1 != null);
                            dtmTile.Z.CopyAllValuesFrom(dtmBuffer1);
                        }
                    }
                    else if (this.FillPower == 2)
                    {
                        if (this.FillFromCardinalOnly)
                        {
                            noDataValues = dtmTile.FillNoDataFromCardinalSquaredDistances(maxFillDistanceInRasterCells, ref dtmBuffer1, ref dtmBuffer2);
                        }
                        else
                        {
                            noDataValues = dtmTile.FillNoDataFromCardinalAndOrdinalSquaredDistances(maxFillDistanceInRasterCells, ref dtmBuffer1, ref dtmBuffer2);
                        }
                        if (noSmoothing)
                        {
                            Debug.Assert(dtmBuffer1 != null);
                            dtmTile.Z.CopyAllValuesFrom(dtmBuffer1);
                        }
                    }
                    Debug.Assert(noDataValues == 0, $"Expected zero no data values to remain in DTM `{dtmTile.FilePath}` but {noDataValues} values persisted.");
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return; // no point in writing tile
                    }

                    // smooth interpolated values
                    if (noSmoothing == false)
                    {
                        if ((dtmBuffer1 == null) || (dtmTile.SizeX != dtmBuffer1.SizeX) || (dtmTile.SizeY != dtmBuffer1.SizeY))
                        {
                            dtmBuffer1 = new(dtmTile, "dtmZbuffer1", dtmTile.Z.NoDataValue, RasterBandInitialValue.Unintialized);
                        }
                        if ((dtmBuffer2 == null) || (dtmTile.SizeX != dtmBuffer2.SizeX) || (dtmTile.SizeY != dtmBuffer2.SizeY))
                        {
                            dtmBuffer2 = new(dtmTile, "dtmZbuffer2", dtmTile.Z.NoDataValue, RasterBandInitialValue.Unintialized);
                        }
                        if (this.FillPower == 0)
                        {
                            // if interpolation was done then the values to smooth are already in dtmBuffer1
                            // If not, dtmBuffer1's contents are undefined and need to be synced to the measured z values for stenciling.
                            // This copy and instantiatio of dtmBuffer2 can be optimized out if only smoothing is applied.
                            dtmBuffer1.CopyAllValuesFrom(dtmTile.Z);
                        }

                        // stenciling iterations
                        RasterBand<float> sourceBuffer = dtmBuffer1;
                        RasterBand<float> destinationBuffer = dtmBuffer2;
                        for (int stencilteration = 0; stencilteration < this.StencilIterations; ++stencilteration)
                        {
                            Filter.Average3x3(sourceBuffer, destinationBuffer, ref smoothingRowBuffers);

                            if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                            {
                                return; // no point in writing tile
                            }

                            (sourceBuffer, destinationBuffer) = (destinationBuffer, sourceBuffer);
                            dtmTile.Z.StencilValuesTo(sourceBuffer);
                        }

                        // smoothing iterations
                        for (int smoothingIteration = 0; smoothingIteration < this.SmoothIterations; ++smoothingIteration)
                        {
                            Filter.Average3x3(sourceBuffer, destinationBuffer, ref smoothingRowBuffers);

                            if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                            {
                                return; // no point in writing tile
                            }

                            (sourceBuffer, destinationBuffer) = (destinationBuffer, sourceBuffer);
                        }

                        dtmTile.Z.CopyAllValuesFrom(sourceBuffer); // last iteration's destination has been swapped into the source position
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
            this.WriteVerbose($"{cellsWritten:n0} DTM cells in {dtmReadWrite.FilesRead} point cloud {(dtmReadWrite.FilesRead == 1 ? "tile" : "tiles")} in {progress.Stopwatch.ToElapsedString()}: {dtmReadWrite.FilesRead / progress.Stopwatch.Elapsed.TotalSeconds:0.0} tiles/s.");
            base.ProcessRecord();
        }
    }
}
