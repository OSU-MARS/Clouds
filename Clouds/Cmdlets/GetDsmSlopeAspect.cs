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

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single surface model (DSM, DTM, CHM...) to calculate slope and aspect of, 2) wildcarded path to a set of surface tiles to process, or 3) path to a directory of GeoTIFF files (.tif extension) to process. Each tile must contain DigitalSurfaceModel's required bands.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Position = 1, HelpMessage = "Name of digital surface band in surface model (virtual) raster. Default is \"dsm\".")]
        public string? DsmBand { get; set; }

        [Parameter(Position = 2, HelpMessage = "Name of smoothed surface band in surface model (virtual) raster. Default is \"cmm3\".")]
        public string? CmmBand { get; set; }

        [Parameter(HelpMessage = "Whether or not to create a virtual raster of the slope and aspect tiles generated.")]
        public SwitchParameter Vrt { get; set; }

        [Parameter(HelpMessage = "Whether or not to compress slope and aspect rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

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
            const string cmdletName = "Get-DsmSlopeAspect";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRasterMetadata<DigitalSurfaceModel>(cmdletName, this.Dsm, DigitalSurfaceModel.CreateFromPrimaryBandMetadata, this.cancellationTokenSource);
            Debug.Assert(dsm.TileGrid != null);

            GridNullable<List<RasterBandStatistics>>? slopeAspectStatistics = this.Vrt ? new(dsm.TileGrid, cloneCrsAndTransform: false) : null;
            TileReadWriteStreaming<DigitalSurfaceModel, TileStreamPosition> slopeAspectReadWrite = TileReadWriteStreaming.Create(dsm.TileGrid, outputPathIsDirectory: true);
            int maxDsmTileIndex = dsm.SizeInTilesX * dsm.SizeInTilesY;
            ParallelTasks slopeAspectTasks = new(Int32.Min(this.DataThreads, dsm.NonNullTileCount), () =>
            {
                for (int tileWriteIndex = slopeAspectReadWrite.GetNextTileWriteIndexThreadSafe(); tileWriteIndex < slopeAspectReadWrite.MaxTileIndex; tileWriteIndex = slopeAspectReadWrite.GetNextTileWriteIndexThreadSafe())
                {
                    DigitalSurfaceModel? dsmTile = dsm[tileWriteIndex];
                    if (dsmTile == null)
                    {
                        continue;
                    }

                    // assist in read until neighborhood complete or no more tiles left to read
                    // Tiles are created with only primary DSM bands so that only primary band data is read.
                    if (slopeAspectReadWrite.TryEnsureNeighborhoodRead(tileWriteIndex, dsm, this.cancellationTokenSource) == false)
                    {
                        Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                        return; // reading was aborted
                    }

                    (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileWriteIndex);
                    RasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.DsmBand);
                    RasterNeighborhood8<float> cmmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.CmmBand);
                    lock (slopeAspectReadWrite)
                    {
                        // obtain slope and aspect bands so they can be calculated
                        // Since this is called at the tile level the overall DSM's list of bands is not updated.
                        dsmTile.EnsureSupportingBandsCreated(DigitalSurfaceModelBands.SlopeAspect, slopeAspectReadWrite.RasterBandPool);
                    }
                    dsmTile.CalculateSlopeAndAspect(dsmNeighborhood, cmmNeighborhood);
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    if (this.NoWrite == false)
                    {
                        dsmTile.Write(dsmTile.FilePath, DigitalSurfaceModelBands.SlopeAspect, this.CompressRasters);
                    }
                    if (slopeAspectStatistics != null)
                    {
                        // virtual raster generation currently requires statistics be aligned by band number
                        slopeAspectStatistics[tileIndexX, tileIndexY] = dsmTile.GetBandStatistics();
                    }
                    lock (slopeAspectReadWrite)
                    {
                        // slope and aspect bands are no longer in use, so return to pool
                        // Primary bands may lie in other, incomplete, processing neighborhoods so are not returned to pool until rows
                        // are released from OnTileRead(). Slope and aspect bands could be returned at the same time as primary bands
                        // but it's somewhat more memory compact to return them here.
                        dsmTile.ReturnBandData(DigitalSurfaceModelBands.SlopeAspect, slopeAspectReadWrite.RasterBandPool);
                        slopeAspectReadWrite.OnTileWritten(tileIndexX, tileIndexY);
                    }
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "placeholder");
            while (slopeAspectTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = slopeAspectReadWrite.TilesRead + (slopeAspectReadWrite.TilesRead == 1 ? " DSM read, " : " DSMs read, ") +
                                             slopeAspectReadWrite.TilesWritten + " of " + dsm.NonNullTileCount + " tiles " + (this.NoWrite ? "completed (" : "written (") + 
                                             slopeAspectTasks.Count + (slopeAspectTasks.Count == 1 ? " thread)..." : " threads)...");
                progress.Update(slopeAspectReadWrite.TilesWritten, dsm.NonNullTileCount);
                this.WriteProgress(progress);
            }

            if (this.Vrt) // debatable if this.NoWrite should be considered here; for now treat -Vrt as an independent switch
            {
                // update DSM's band information with the added slope and aspect bands
                // Total rebuild of band metadata's somewhat inefficient but currently necessary for VirtualRaster<T> instances to pick
                // up bands which were added to tiles.
                // The DSM has the information to include its primary bands in the .vrt as well as slope and aspect bands but, as this
                // is a cmdlet for add on calculation, it's assumed including the primary bands in a slope and aspect .vrt would not
                // be helpful.
                dsm.RefreshBandMetadata();

                (string slopeAspectVrtFilePath, string slopeAspectVrtDatasetPath) = VrtDataset.GetVrtPaths(this.Dsm, slopeAspectReadWrite.OutputPathIsDirectory, DigitalSurfaceModel.DirectorySlopeAspect, "slopeAspect.vrt");
                List<string> slopeAspectBands = [ DigitalSurfaceModel.DsmSlopeBandName, DigitalSurfaceModel.DsmAspectBandName, DigitalSurfaceModel.CmmSlope3BandName, DigitalSurfaceModel.CmmAspect3BandName ];
                VrtDataset slopeAspectVrt = dsm.CreateDataset(slopeAspectVrtDatasetPath, slopeAspectBands, slopeAspectStatistics);
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
