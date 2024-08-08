using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Vrt;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "DsmSlopeAspect")]
    public class GetDsmSlopeAspect : GdalCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(HelpMessage = "Whether or not to compress slope and aspect rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single surface model (DSM, DTM, CHM...) to calculate slope and aspect of, 2) wildcarded path to a set of surface tiles to process, or 3) path to a directory of GeoTIFF files (.tif extension) to process. Each tile must contain DigitalSurfaceModel's required bands.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(HelpMessage = "Name of smoothed surface band in surface model (virtual) raster. Default is \"cmm3\".")]
        public string? CmmBand { get; set; }

        [Parameter(HelpMessage = "Name of digital surface band in surface model (virtual) raster. Default is \"dsm\".")]
        public string? DsmBand { get; set; }

        [Parameter(HelpMessage = "Whether or not to create a virtual raster for slope and aspect.")]
        public SwitchParameter Vrt { get; set; }

        [Parameter(HelpMessage = "Turn off writing of output rasters. This is useful in certain benchmarking and development situations. If -Vrt is specified a .vrt will be generated regardless of whether -NoWrite is used.")]
        public SwitchParameter NoWrite { get; set; }

        public GetDsmSlopeAspect()
        {
            this.cancellationTokenSource = new();

            this.CmmBand = DigitalSurfaceModel.CanopyMaximaBandName;
            this.Dsm = String.Empty; // mandatory
            this.DsmBand = DigitalSurfaceModel.SurfaceBandName;
            this.NoWrite = false;
            this.Vrt = false;
        }

        protected override void ProcessRecord()
        {
            string cmdletName = "Get-DsmSlopeAspect";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRaster<DigitalSurfaceModel>(cmdletName, this.Dsm, readData: false, this.cancellationTokenSource);
            VirtualRaster<SlopeAspectRaster> slopeAspect = dsm.CreateEmptyCopy<SlopeAspectRaster>();
            Debug.Assert(slopeAspect.TileGrid != null);
            GridNullable<List<RasterBandStatistics>> slopeAspectStatistics = new(slopeAspect.TileGrid, cloneCrsAndTransform: false);

            Debug.Assert((dsm.TileGrid != null) && (slopeAspect.TileGrid != null));
            TileReadCreateWriteStreaming<GridNullable<DigitalSurfaceModel>, DigitalSurfaceModel, SlopeAspectRaster> slopeAspectReadCreateWrite = new(dsm.TileGrid, slopeAspect, outputPathIsDirectory: true);
            int maxDsmTileIndex = dsm.VirtualRasterSizeInTilesX * dsm.VirtualRasterSizeInTilesY;
            ParallelTasks slopeAspectTasks = new(Int32.Min(this.MaxThreads, dsm.NonNullTileCount), () =>
            {
                while (slopeAspectReadCreateWrite.TileWritesInitiated < dsm.NonNullTileCount)
                {
                    bool noTilesAvailableForWrite = false;
                    while (noTilesAvailableForWrite == false)
                    {
                        // write as many tiles as are available for completion
                        SlopeAspectRaster? slopeAspectTileToWrite = null;
                        int tileWriteIndexX = -1;
                        int tileWriteIndexY = -1;
                        lock (slopeAspectReadCreateWrite)
                        {
                            if (slopeAspectReadCreateWrite.TryGetNextTileToWrite(out tileWriteIndexX, out tileWriteIndexY, out slopeAspectTileToWrite))
                            {
                                ++slopeAspectReadCreateWrite.TileWritesInitiated;
                            }
                            else
                            {
                                if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                                {
                                    return;
                                }
                                noTilesAvailableForWrite = true;
                            }
                        }
                        if (slopeAspectTileToWrite != null)
                        {
                            if (this.NoWrite == false)
                            {
                                slopeAspectTileToWrite.Write(slopeAspectTileToWrite.FilePath, this.CompressRasters);
                            }
                            if (this.Vrt)
                            {
                                slopeAspectStatistics[tileWriteIndexX, tileWriteIndexY] = slopeAspectTileToWrite.GetBandStatistics();
                            }
                            lock (slopeAspectReadCreateWrite)
                            {
                                // mark tile as written even when NoWrite is set so that virtual raster completion's updated and the tile's returned to the object pool
                                // Since OnTileWritten() returns completed tiles to the DSM object pool the lock taken here must be on the
                                // same object as when tiles are requested from the pool.
                                slopeAspectReadCreateWrite.OnTileWritten(tileWriteIndexX, tileWriteIndexY, slopeAspectTileToWrite);
                            }
                        }

                        if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    // if all available tiles are completed and tiles remain to be read, load another DSM tile's surface band
                    if (slopeAspectReadCreateWrite.TileReadIndex < maxDsmTileIndex)
                    {
                        for (int tileReadIndex = slopeAspectReadCreateWrite.GetNextTileReadIndexThreadSafe(); tileReadIndex < maxDsmTileIndex; tileReadIndex = slopeAspectReadCreateWrite.GetNextTileReadIndexThreadSafe())
                        {
                            DigitalSurfaceModel? dsmTileForBandRead = dsm[tileReadIndex];
                            if (dsmTileForBandRead == null)
                            {
                                continue; // nothing to do as no tile is present at this grid position
                            }

                            // read DSM tile's band
                            RasterBand<float> dsmBandForRead = (RasterBand<float>)dsmTileForBandRead.GetBand(this.DsmBand);
                            RasterBand<float> cmmBandForRead = (RasterBand<float>)dsmTileForBandRead.GetBand(this.CmmBand);
                            if (slopeAspectReadCreateWrite.RasterBandPool.FloatPool.Count > 0)
                            {
                                lock (slopeAspectReadCreateWrite)
                                {
                                    dsmBandForRead.TryTakeOwnershipOfDataBuffer(slopeAspectReadCreateWrite.RasterBandPool);
                                    cmmBandForRead.TryTakeOwnershipOfDataBuffer(slopeAspectReadCreateWrite.RasterBandPool);
                                }
                            }
                            dsmBandForRead.Read(dsmTileForBandRead.FilePath);
                            cmmBandForRead.Read(dsmTileForBandRead.FilePath);
                            if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                            {
                                return;
                            }

                            lock (slopeAspectReadCreateWrite)
                            {
                                (int tileReadIndexX, int tileReadIndexY) = dsm.ToGridIndices(tileReadIndex);
                                slopeAspectReadCreateWrite.OnTileRead(tileReadIndexX, tileReadIndexY);
                            }

                            // a tile has been read so exit read loop to check for creatable or completable tiles
                            break;
                        }
                    }

                    bool tileAvailableForCreate;
                    int tileCreateIndexX;
                    int tileCreateIndexY;
                    DigitalSurfaceModel? dsmTile;
                    lock (slopeAspectReadCreateWrite)
                    {
                        tileAvailableForCreate = slopeAspectReadCreateWrite.TryGetNextTileCreation(out tileCreateIndexX, out tileCreateIndexY, out dsmTile);
                    }
                    if (tileAvailableForCreate)
                    {
                        string slopeAspectTilePath = Raster.GetDiagnosticFilePath(dsmTile!.FilePath, DigitalSurfaceModel.DiagnosticDirectorySlopeAspect, createDiagnosticDirectory: true);
                        SlopeAspectRaster? slopeAspectTile;
                        lock (slopeAspectReadCreateWrite)
                        {
                            slopeAspectTile = new(dsmTile, slopeAspectReadCreateWrite.RasterBandPool)
                            {
                                FilePath = slopeAspectTilePath
                            };
                        }

                        VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileCreateIndexX, tileCreateIndexY, this.DsmBand);
                        VirtualRasterNeighborhood8<float> cmmNeighborhood = dsm.GetNeighborhood8<float>(tileCreateIndexX, tileCreateIndexY, this.CmmBand);
                        slopeAspectTile.CalculateSlopeAndAspect(dsmNeighborhood, cmmNeighborhood);
                        if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                        lock (slopeAspectReadCreateWrite)
                        {
                            slopeAspect.Add(slopeAspectTile);
                            slopeAspectReadCreateWrite.OnTileCreated(tileCreateIndexX, tileCreateIndexY);
                        }
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "placeholder");
            while (slopeAspectTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = slopeAspectReadCreateWrite.TilesRead + (slopeAspectReadCreateWrite.TilesRead == 1 ? " DSM read, " : " DSMs read, ") +
                                             slopeAspect.NonNullTileCount + (slopeAspect.NonNullTileCount == 1 ? " tile created, " : " tiles created, ") +
                                             slopeAspectReadCreateWrite.TilesWritten + " of " + dsm.NonNullTileCount + " tiles " + (this.NoWrite ? "completed (" : "written (") + slopeAspectTasks.Count +
                                             (slopeAspectTasks.Count == 1 ? " thread)..." : " threads)...");
                progress.Update(slopeAspectReadCreateWrite.TilesCreated, dsm.NonNullTileCount);
                this.WriteProgress(progress);
            }

            if (this.Vrt) // debatable if this.NoWrite should be considered here; for now treat -Vrt as an independent switch
            {
                (string slopeAspectVrtFilePath, string slopeAspectVrtDatasetPath) = VrtDataset.GetVrtPaths(this.Dsm, slopeAspectReadCreateWrite.OutputPathIsDirectory, DigitalSurfaceModel.DiagnosticDirectorySlopeAspect, "slopeAspect.vrt");
                VrtDataset slopeAspectVrt = slopeAspect.CreateDataset(slopeAspectVrtDatasetPath, slopeAspectStatistics);
                slopeAspectVrt.WriteXml(slopeAspectVrtFilePath);
            }

            progress.Stopwatch.Stop();
            string tileOrTiles = dsm.NonNullTileCount > 1 ? "tiles" : "tile";
            this.WriteVerbose("Found slope and aspect in " + dsm.NonNullTileCount + " " + tileOrTiles + (this.Vrt ? " and generated .vrt" : null) + " in " + progress.Stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource?.Cancel();
            base.StopProcessing();
        }
    }
}
