using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Collections.Concurrent;
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
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTilesToTilesCmdlet
    {
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to write DSM to as a GeoTIFF or path to a directory to write DSM tiles to. The DSM's cell sizes and positions will be the same as the DTM's.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles. The read capability of the drive the DTMs are stored on is assumed to be the same as or greater than the DSM destination drive.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = $"If set, calculate the canopy maxima model. Combined with -{nameof(GetDsm.SlopeAspect)} this can be useful when slope and aspect are included as bidirectional reflectance distribution function inputs.")]
        public SwitchParameter Cmm { get; set; }

        [Parameter(HelpMessage = $"If set, calculate the DSM and, if -{nameof(GetDsm.Cmm)} is set, canopy maxima models' slope and aspect. Calculating the canopy height model's slope and aspect is not supported as the CHM is warped by the ground slope.")]
        public SwitchParameter SlopeAspect { get; set; }

        [Parameter(HelpMessage = "Separation distance between DSM layer and subsurface layer. Default is no distance specified which leaves DSM subsurface extraction disabled. Specifying a gap distance enables the DSM's subsurface layer and subsurface buffer used to look for gaps.")]
        [ValidateRange(0.0F, 300.0F)] // arbitrary upper bound
        public float SubsurfaceGap { get; set; }

        // TODO: support actual .vrt generation
        [Parameter(HelpMessage = "Indicates the input point clouds are a tiled dataset and thus that the DSM should be generated as a virtual raster (.vrt).")]
        public SwitchParameter Vrt { get; set; }

        [Parameter(HelpMessage = "EPSG of input point clouds' or DTMs' vertical coordinate systems if not specified in the data files. Default is unset, in which case DSM generation will fail if both the source point cloud and corresponding DTM lack a vertical CRS. Common vertical CRSes in the United States are 5703 (NAVD88 meters), 6360 (NAVD88 feet), and 8228 (NAVD88 feet for certain state planes).")]
        [ValidateRange(Constant.Epsg.Min, Constant.Epsg.Max)]
        public int InputVerticalEpsg { get; set; }

        public GetDsm()
        {
            this.Cmm = false;
            // leave this.DataThreads at default
            this.Dsm = String.Empty;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.InputVerticalEpsg = -1;
            // leave this.MetadataThreads at default
            this.SlopeAspect = false;
            this.SubsurfaceGap = Single.NaN;
        }

        private void GetDsmsForFiles(string cmdletName, List<string> cloudPaths, int pointCloudsToRead, SpatialReference? inputVerticalCrs, DigitalSurfaceModelBands dsmBands, bool dsmPathIsDirectory, bool dtmPathIsDirectory, int readThreads, int fileCompletionThreads)
        {
            long dsmCellsWritten = 0;
            FileReadWrite dsmReadCreateWrite = new(dsmPathIsDirectory);
            ConcurrentDictionary<int, RasterBandPool> rasterBandPoolsByCellCount = [];
            UInt64 totalNumberOfPoints = 0;
            long totalPointDataInBytes = 0;

            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dsmTasks = new(readThreads + fileCompletionThreads, () =>
            {
                RasterBand<float>? dtmFile = null;
                byte[]? pointReadBuffer = null;
                float[]? dsmSubsurfaceBuffer = null;
                for (int fileIndex = dsmReadCreateWrite.GetNextFileReadIndexThreadSafe(); fileIndex < cloudPaths.Count; fileIndex = dsmReadCreateWrite.GetNextFileReadIndexThreadSafe())
                {
                    readSemaphore.Wait(this.CancellationTokenSource.Token);

                    string cloudPath = cloudPaths[fileIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath, this.DiscardOverrunningVlrs, this.Unbuffered);
                    LasFile lasFile = new(cloudPath, reader, this.FallbackDate);
                    SpatialReference lasFileCrs = lasFile.GetSpatialReference();

                    // read DTM file or tile for this point cloud
                    string fileName = Tile.GetName(lasFile.FilePath);
                    string dtmFileOrTilePath = dtmPathIsDirectory ? GdalCmdlet.GetRasterTilePath(this.Dtm, fileName) : this.Dtm;
                    dtmFile = RasterBand<float>.CreateOrLoad(dtmFileOrTilePath, this.DtmBand, dtmFile);
                    if (SpatialReferenceExtensions.IsSameCrs(lasFileCrs, dtmFile.Crs) == false)
                    {
                        throw new NotSupportedException($"{fileName}: Point cloud '{cloudPath}'s coordinate system ('{lasFileCrs.GetName()}') does not match the DTM's coordinate system ('{dtmFile.Crs.GetName()}').");
                    }

                    // point cloud must be in the same CRS as the DTM (which the DSM inherits) but can have any extent equal to or smaller than the DSM and DTM tiles
                    Extent pointCloudHorizontalExtent = new(lasFile.Header.MinX, lasFile.Header.MaxX, lasFile.Header.MinY, lasFile.Header.MaxY);
                    (double dtmXmin, double dtmXmax, double dtmYmin, double dtmYmax) = dtmFile.GetExtent();
                    if (pointCloudHorizontalExtent.IsSameOrWithin(dtmXmin, dtmXmax, dtmYmin, dtmYmax) == false)
                    {
                        throw new NotSupportedException($"{fileName}: point cloud extent ({pointCloudHorizontalExtent.GetExtentString()}) is not fully contained within the corresponding DTM's extent ({dtmFile.GetExtentString()}).");
                    }

                    // instantiate DSM file for this point cloud
                    // DSM inherits the DTM's extent and transform.
                    string dsmFilePath = dsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, fileName + Constant.File.GeoTiffExtension) : this.Dsm;
                    SpatialReference dsmCrs = lasFileCrs;
                    if (lasFileCrs.IsVertical() == 0)
                    {
                        if (dtmFile.Crs.IsVertical() == 0)
                        {
                            if (inputVerticalCrs != null)
                            {
                                dsmCrs = SpatialReferenceExtensions.CreateCompound(lasFileCrs, inputVerticalCrs);
                            }
                            else
                            {
                                // for now, fall through to error checking in DigitalSurfaceModel..ctor()
                            }
                        }
                        else
                        {
                            dsmCrs = dtmFile.Crs;
                        }
                    }

                    RasterBandPool rasterBandPool = rasterBandPoolsByCellCount.GetOrAdd(dtmFile.Cells, (int cellCount) => { return new RasterBandPool(); });
                    DigitalSurfaceModel dsmFile;
                    lock (rasterBandPool)
                    {
                        dsmFile = new(dsmFilePath, lasFile, dsmCrs, dsmBands, dtmFile, rasterBandPool);
                    }

                    // create DSM for this tile
                    using LasReader pointReader = lasFile.CreatePointReader(this.Unbuffered);
                    pointReader.ReadPointsToDsm(lasFile, dsmFile, ref pointReadBuffer, ref dsmSubsurfaceBuffer);
                    readSemaphore.Release(); // exit semaphore as DTM and .las file have both been read
                    dsmReadCreateWrite.IncrementFilesReadThreadSafe();
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    dsmFile.OnPointAdditionComplete(dtmFile, this.SubsurfaceGap, dsmSubsurfaceBuffer);
                    if (this.NoWrite == false)
                    {
                        dsmFile.Write(dsmFilePath, this.CompressRasters);
                    }
                    lock (dsmReadCreateWrite)
                    {
                        if (this.NoWrite == false)
                        {
                            ++dsmReadCreateWrite.FilesWritten;
                            dsmCellsWritten += dsmFile.Cells;
                        }
                        totalNumberOfPoints += lasFile.Header.GetNumberOfPoints();
                        totalPointDataInBytes += reader.BaseStream.Length;
                    }

                    lock (rasterBandPool)
                    {
                        dsmFile.ReturnBandData(rasterBandPool); // TODO: just reset thread's DSM instance
                    }
                }
            }, this.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, dsmReadCreateWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dsmTasks.Count));
            this.WriteProgress(progress);
            while (dsmTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = dsmReadCreateWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dsmTasks.Count);
                progress.Update(dsmReadCreateWrite.FilesWritten, pointCloudsToRead);
                this.WriteProgress(progress);
            }

            // release point batches and trigger a gen 2 collection
            // There's easily tens of GB in gen 2 and in the large object heap. Left on its own, the .NET 8 garbage collector often takes
            // minutes to release these but an explicit call to Collect() results in release within a second or so. Requesting compaction
            // brings Windows' display of process memory used into line with the actual size of the managed heap after collection.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            float meanPointsPerTile = totalNumberOfPoints / (float)pointCloudsToRead;
            float totalPointDataInGB = totalPointDataInBytes / (1024.0F * 1024.0F * 1024.0F);
            progress.Stopwatch.Stop();
            this.WriteVerbose($"{dsmCellsWritten:n0} DSM cells from {pointCloudsToRead} {(pointCloudsToRead == 1 ? "tile" : "tiles")} ({totalNumberOfPoints / 1E6:0.0} Mpoints) in {progress.Stopwatch.ToElapsedString()}: {totalPointDataInGB:0.00} GB at {dsmReadCreateWrite.FilesWritten / progress.Stopwatch.Elapsed.TotalSeconds:0.00} tiles/s ({meanPointsPerTile / 1E6:0.0} Mpoints/tile, {totalPointDataInGB / progress.Stopwatch.Elapsed.TotalSeconds:0.0} GB/s).");
        }

        private void GetDsmTiled(string cmdletName, LasTileGrid cloudGrid, int pointCloudsToRead, SpatialReference? inputVerticalCrs, DigitalSurfaceModelBands dsmBands, bool dsmPathIsDirectory, bool dtmPathIsDirectory, int readThreads, int fileCompletionThreads)
        {
            VirtualRaster<DigitalSurfaceModel> dsm = new(cloudGrid);
            DsmReadCreateWrite tiledDsmReadCreateWrite = DsmReadCreateWrite.Create(cloudGrid, dsm, this.Cmm, this.SlopeAspect, dsmPathIsDirectory, dtmPathIsDirectory, this.NoWrite);

            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            ParallelTasks dsmTasks = new(readThreads + fileCompletionThreads, () =>
            {
                RasterBand<float>? dtmTile = null;
                byte[]? pointReadBuffer = null;
                float[]? dsmSubsurfaceBuffer = null;
                while (tiledDsmReadCreateWrite.TileWritesInitiated < pointCloudsToRead)
                {
                    // create canopy maxima models and write as many tiles as are available for completion
                    // An emphasis on completion minimizes total memory footprint by keeping as few tiles in DDR as is practical.
                    // TODO: support .vrt creation
                    tiledDsmReadCreateWrite.TryWriteCompletedTiles(this.CancellationTokenSource, bandStatisticsByTile: null);

                    // if all available tiles are completed and tiles remain to be read, read another tile and create its DSM
                    if (tiledDsmReadCreateWrite.FileReadIndex < tiledDsmReadCreateWrite.MaxTileIndex)
                    {
                        readSemaphore.Wait(this.CancellationTokenSource.Token);
                        if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                        {
                            // cmdlet is stopping (or, as an edge case, task is cancelled) so no point in looking for a tile to read
                            readSemaphore.Release();
                            return;
                        }

                        LasTile? lasTile = null;
                        for (int tileIndex = tiledDsmReadCreateWrite.GetNextFileReadIndexThreadSafe(); tileIndex < tiledDsmReadCreateWrite.MaxTileIndex; tileIndex = tiledDsmReadCreateWrite.GetNextFileReadIndexThreadSafe())
                        {
                            lasTile = cloudGrid[tileIndex];
                            if (lasTile == null)
                            {
                                continue; // nothing to do as no tile is present at this grid position
                            }

                            // read DTM file or tile for this point cloud
                            string tileName = Tile.GetName(lasTile.FilePath);
                            string dtmFileOrTilePath = tiledDsmReadCreateWrite.DtmPathIsDirectory ? GdalCmdlet.GetRasterTilePath(this.Dtm, tileName) : this.Dtm;
                            dtmTile = RasterBand<float>.CreateOrLoad(dtmFileOrTilePath, this.DtmBand, dtmTile);

                            // tilePoints must be in the same CRS as the DTM but can have any extent equal to or smaller than the DSM and DTM tiles
                            SpatialReference lasTileCrs = lasTile.GetSpatialReference();
                            if (SpatialReferenceExtensions.IsSameCrs(lasTileCrs, dtmTile.Crs) == false)
                            {
                                throw new NotSupportedException($"{tileName}: Point cloud '{lasTile.FilePath}'s coordinate system ('{lasTileCrs.GetName()}') does not match the DTM's coordinate system ('{dtmTile.Crs.GetName()}').");
                            }
                            if (dtmTile.IsSameExtent(lasTile.GridExtent) == false)
                            {
                                throw new NotSupportedException($"{tileName}: DTM tile extent ({dtmTile.GetExtentString()}) does not match point cloud tile extent ({lasTile.GridExtent.GetExtentString()}).");
                            }

                            // if there's only one point cloud and the DTM extends beyond the cloud expand DTM virtual raster to match the DTM tile extent
                            // This enables support for scan areas of limited extent (usually handheld/backpack or drone) without having to crop a larger DTM to
                            // exactly match. The DSM produced in these cases matches the DTM extent and thus extends beyond the scan.
                            // TODO: generalize this to
                            //   1) multiple point clouds on a single DTM producing a single DSM
                            //   2) drop off DTM portions of point clouds so that a project area DTM can be used to crop the DSM to the area of interest
                            // Design issue here is switching between using point clouds or DTMs for framing DSM output structure.
                            if ((tiledDsmReadCreateWrite.Las.NonNullCells == 1) && (tiledDsmReadCreateWrite.Dsm.NonNullTileCount == 0))
                            {
                                tiledDsmReadCreateWrite.Dsm.TileTransform.Copy(dtmTile.Transform);
                            }

                            // create new DSM tile
                            // Not ideal to call DigitalSurfaceModel..ctor() from within the read semaphore but initial creation is not usefully
                            // possible until the DTM has been read and Reset() of an object pooled DTM tile is inexpensive compared to the .las
                            // read cost.
                            string dsmFileOrTilePath = tiledDsmReadCreateWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;

                            SpatialReference dsmCrs = lasTileCrs;
                            if ((dsmCrs.IsVertical() == 0) && (inputVerticalCrs != null))
                            {
                                dsmCrs = SpatialReferenceExtensions.CreateCompound(dsmCrs, inputVerticalCrs);
                            }
                            DigitalSurfaceModel dsmTile;
                            lock (tiledDsmReadCreateWrite) // for use of RasterBandBool
                            {
                                dsmTile = new(dsmFileOrTilePath, lasTile, dsmCrs, dsmBands, dtmTile, tiledDsmReadCreateWrite.RasterBandPool);
                            }

                            // internal consistency currently expected for fully tiled cases
                            Debug.Assert(dsmTile.IsSameExtent(lasTile.GridExtent) && dsmTile.IsSameExtentAndSpatialResolution(dtmTile));

                            // create DSM for this tile
                            using LasReader pointReader = lasTile.CreatePointReader(this.Unbuffered);
                            pointReader.ReadPointsToDsm(lasTile, dsmTile, ref pointReadBuffer, ref dsmSubsurfaceBuffer);
                            readSemaphore.Release(); // exit semaphore as DTM and .las file have both been read

                            dsmTile.OnPointAdditionComplete(dtmTile, this.SubsurfaceGap, dsmSubsurfaceBuffer);

                            (int tileIndexX, int tileIndexY) = cloudGrid.ToGridIndices(tileIndex);
                            lock (tiledDsmReadCreateWrite) // all DSM create and write operations lock on DSM virtual raster
                            {
                                tiledDsmReadCreateWrite.Dsm.Add(tileIndexX, tileIndexY, dsmTile);
                                tiledDsmReadCreateWrite.OnTileRead(tileIndexX, tileIndexY);
                                tiledDsmReadCreateWrite.OnTileCreated(tileIndexX, tileIndexY);
                            }

                            // a tile has been read and its DSM created so exit creation loop to check for completable tiles
                            break;
                        }

                        if (lasTile == null)
                        {
                            // end of input tiles has been reached but semaphore was taken and still needs to be released
                            // If semaphore is not released any other worker threads blocked on it can't enter it and thus can't return.
                            readSemaphore.Release();
                        }
                    }
                }
            }, this.CancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord progress = new(cmdletName, tiledDsmReadCreateWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dsmTasks.Count));
            this.WriteProgress(progress);
            while (dsmTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                progress.StatusDescription = tiledDsmReadCreateWrite.GetPointCloudReadFileWriteStatusDescription(pointCloudsToRead, activeReadThreads, dsmTasks.Count);
                progress.Update(tiledDsmReadCreateWrite.FilesWritten, pointCloudsToRead);
                this.WriteProgress(progress);
            }

            // TODO: generate .vrts

            // release point batches and trigger a gen 2 collection
            // There's easily tens of GB in gen 2 and in the large object heap. Left on its own, the .NET 8 garbage collector often takes
            // minutes to release these but an explicit call to Collect() results in release within a second or so. Requesting compaction
            // brings Windows' display of process memory used into line with the actual size of the managed heap after collection.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            progress.Stopwatch.Stop();
            this.WriteVerbose($"{tiledDsmReadCreateWrite.CellsWritten:n0} DSM cells from {pointCloudsToRead} {(pointCloudsToRead == 1 ? " tile" : " tiles")} ({tiledDsmReadCreateWrite.TotalNumberOfPoints / 1E6:0.0} Mpoints) in {progress.Stopwatch.ToElapsedString()}: {tiledDsmReadCreateWrite.TotalPointDataInGB:0.00} GB at {tiledDsmReadCreateWrite.FilesWritten / progress.Stopwatch.Elapsed.TotalSeconds:0.00} tiles/s ({tiledDsmReadCreateWrite.MeanPointsPerTile / 1E6:0.0} Mpoints/tile, {tiledDsmReadCreateWrite.TotalPointDataInGB / progress.Stopwatch.Elapsed.TotalSeconds:0.0} GB/s).");
        }

        public override string GetName()
        {
            return $"{VerbsCommon.Get}-Dsm";
        }

        protected override void ProcessRecord()
        {
            if (this.Cmm && (Fma.IsSupported == false))
            {
                throw new NotSupportedException("Generation of a canopy maxima model requires FMA instructions (and DSM subsurface generation uses AVX).");
            }
            if (this.DataThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), $"-{nameof(this.DataThreads)} must be at least two.");
            }

            string cmdletName = this.GetName();
            LasTileGrid? cloudGrid = null;
            List<string>? cloudPaths = null;
            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            // TODO: infer point clouds are tiled if -Dtm is a .vrt?
            int pointCloudsToRead;
            if (this.Vrt)
            {
                cloudGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Dsm), dsmPathIsDirectory);
                pointCloudsToRead = cloudGrid.NonNullCells;
            }
            else
            {
                cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
                pointCloudsToRead = cloudPaths.Count;
            }

            DigitalSurfaceModelBands dsmBands = DigitalSurfaceModelBands.Default;
            if (Single.IsNaN(this.SubsurfaceGap))
            {
                dsmBands |= DigitalSurfaceModelBands.Subsurface;
            }
            if (this.SlopeAspect)
            {
                dsmBands |= DigitalSurfaceModelBands.SlopeAspect;
            }

            SpatialReference? inputVerticalCrs = null;
            if (this.InputVerticalEpsg >= Constant.Epsg.Min)
            {
                inputVerticalCrs = SpatialReferenceExtensions.Create(this.InputVerticalEpsg);
            }

            // find point cloud read and tile worker thread counts
            // For small sets of tiles, runtime is dominated by DSM creation latency after tiles are read into memory (streaming read
            // would be helpful but isn't implemented) with filling and sorting the workers' z and source ID lists for each cell being
            // the primary component of runtime. Smaller sets therefore benefit from having up to one worker thread per tile and, up to
            // the point cloud density, greater initial list capacities. For larger sets of tiles, worker requirements set by tile read
            // speed.
            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToDsmBandwidth(dsmBands, this.Unbuffered);
            int readThreads = this.GetPointCloudReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs);

            int preferredCompletionThreadsAsymptotic = Int32.Min(this.DataThreads - readThreads, Int32.Max(readThreads / 3, 2)); // provide at least two completion threads as it appears sometimes beneficial to have more than one
            // but with small numbers of tiles the preferred number of workers increases to reduce overall latency
            int preferredCompletionThreadsWithFewTiles = Int32.Min(this.DataThreads, 25 - (int)(1.5F * pointCloudsToRead)); // negative for 17+ tiles
            // default number of workers is the number of tiles or the asymptotic limit with large number of tiles, whichever is less
            // No value in more than one worker per tile.
            int fileCompletionThreads = Int32.Min(pointCloudsToRead, Int32.Max(preferredCompletionThreadsWithFewTiles, preferredCompletionThreadsAsymptotic));
            Debug.Assert(readThreads + fileCompletionThreads <= this.DataThreads);

            if (this.Vrt)
            {
                Debug.Assert(cloudGrid != null);
                this.GetDsmTiled(cmdletName, cloudGrid, pointCloudsToRead, inputVerticalCrs, dsmBands, dsmPathIsDirectory, dtmPathIsDirectory, readThreads, fileCompletionThreads);
            }
            else
            {
                Debug.Assert(cloudPaths != null);
                this.GetDsmsForFiles(cmdletName, cloudPaths, pointCloudsToRead, inputVerticalCrs, dsmBands, dsmPathIsDirectory, dtmPathIsDirectory, readThreads, fileCompletionThreads);
            }

            base.ProcessRecord();
        }

        private class DsmReadCreateWrite : TileReadCreateWriteStreaming<LasTileGrid, LasTile, DigitalSurfaceModel>
        {
            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public bool DtmPathIsDirectory { get; private init; }
            public LasTileGrid Las { get; private init; }

            public bool CalculateSlopeAndAspect { get; init; }
            public bool CanopyMaximaModel { get; init; }
            public float MeanPointsPerTile { get; private init; }
            public UInt64 TotalNumberOfPoints { get; private init; }
            public float TotalPointDataInGB { get; private init; }

            // dsm.TileGrid checked for null in Create()
            protected DsmReadCreateWrite(LasTileGrid lasGrid, bool[,] unpopulatedTileMapForRead, VirtualRaster<DigitalSurfaceModel> dsm, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool dsmPathIsDirectory, bool dtmPathIsDirectory)
                : base(lasGrid, unpopulatedTileMapForRead, dsm.TileGrid!, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, dsmPathIsDirectory)
            {
                Debug.Assert(SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dsm.Crs));
                if (lasGrid.IsSameExtentAndTileResolution(dsm) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), $"Point cloud tile grid is {lasGrid.SizeX} x {lasGrid.SizeY} with extent ({lasGrid.GetExtentString()}) while the DSM tile grid is {dsm.SizeInTilesX} x {dsm.SizeInTilesY} with extent {dsm.GetExtentString()}. Are the LAS and DTM tiles matched?");
                }

                this.BypassOutputRasterWriteToDisk = false;
                this.CalculateSlopeAndAspect = false;
                this.Dsm = dsm;
                this.DtmPathIsDirectory = dtmPathIsDirectory;
                this.Las = lasGrid;
                this.TileCreationDoesNotRequirePreviousRow = true;

                this.TotalNumberOfPoints = 0;
                long totalTileSizeInBytes = 0;
                for (int lasIndex = 0; lasIndex < lasGrid.Cells; ++lasIndex)
                {
                    LasTile? tile = lasGrid[lasIndex];
                    if (tile != null)
                    {
                        this.TotalNumberOfPoints += tile.Header.GetNumberOfPoints();
                        totalTileSizeInBytes += tile.FileSizeInBytes;
                    }
                }

                this.MeanPointsPerTile = (float)this.TotalNumberOfPoints / (float)lasGrid.NonNullCells;
                this.TotalPointDataInGB = (float)totalTileSizeInBytes / (1024.0F * 1024.0F * 1024.0F);
            }

            public static DsmReadCreateWrite Create(LasTileGrid lasGrid, VirtualRaster<DigitalSurfaceModel> dsm, bool canopyMaximaModel, bool calculateSlopeAndAspect, bool dsmPathIsDirectory, bool dtmPathIsDirectory, bool bypassOutputRasterWriteToDisk)
            {
                if (dsm.TileGrid == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "DSM's grid has not been built.");
                }

                bool[,] unpopulatedTileMapForRead = lasGrid.GetUnpopulatedCellMap();
                bool[,] unpopulatedTileMapForCreate = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                return new(lasGrid, unpopulatedTileMapForRead, dsm, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, dsmPathIsDirectory, dtmPathIsDirectory)
                {
                    BypassOutputRasterWriteToDisk = bypassOutputRasterWriteToDisk,
                    CalculateSlopeAndAspect = calculateSlopeAndAspect,
                    CanopyMaximaModel = canopyMaximaModel
                };
            }

            public override string GetPointCloudReadFileWriteStatusDescription(int lasFilesToRead, int activeReadThreads, int totalThreads)
            {
                string status = this.FilesRead + (this.FilesRead == 1 ? " cloud read, " : " clouds read, ") +
                                this.Dsm.NonNullTileCount + (this.Dsm.NonNullTileCount == 1 ? " DSM created, " : " DSMs created, ") +
                                this.FilesWritten + " of " + lasFilesToRead + " tiles " + (this.BypassOutputRasterWriteToDisk ? "completed (" : "written (") + totalThreads +
                                (totalThreads == 1 ? " thread, " : " threads, ") + activeReadThreads + " reading)...";
                return status;
            }

            protected override void OnTileWrite(int tileWriteIndexX, int tileWriteIndexY, DigitalSurfaceModel dsmTileToCmm)
            {
                if (this.CanopyMaximaModel)
                {
                    RasterNeighborhood8<float> dsmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToCmm.Surface.Name);
                    Debug.Assert((dsmTileToCmm.CanopyMaxima3 != null) && Object.ReferenceEquals(dsmTileToCmm.Surface, dsmNeighborhood.Center));

                    Binomial.Smooth3x3(dsmNeighborhood, dsmTileToCmm.CanopyMaxima3);
                    if (this.CalculateSlopeAndAspect)
                    {
                        RasterNeighborhood8<float> cmmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileWriteIndexX, tileWriteIndexY, dsmTileToCmm.CanopyMaxima3.Name);
                        dsmTileToCmm.CalculateSlopeAndAspect(dsmNeighborhood, cmmNeighborhood);
                    }
                }
            }
        }
    }
}
