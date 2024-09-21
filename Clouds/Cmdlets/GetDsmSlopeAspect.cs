using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using Mars.Clouds.Vrt;
using OSGeo.OGR;
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
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRaster<DigitalSurfaceModel>(cmdletName, this.Dsm, DigitalSurfaceModel.CreateFromPrimaryBandMetadata, this.cancellationTokenSource);
            Debug.Assert(dsm.TileGrid != null);

            GridNullable<List<RasterBandStatistics>>? slopeAspectStatistics = this.Vrt ? new(dsm.TileGrid, cloneCrsAndTransform: false) : null;
            TileReadWriteStreaming<DigitalSurfaceModel, TileStreamPosition> slopeAspectReadWrite = TileReadWriteStreaming.Create(dsm.TileGrid, outputPathIsDirectory: true);
            int maxDsmTileIndex = dsm.VirtualRasterSizeInTilesX * dsm.VirtualRasterSizeInTilesY;
            ParallelTasks slopeAspectTasks = new(Int32.Min(this.MaxThreads, dsm.NonNullTileCount), () =>
            {
                for (int tileIndex = slopeAspectReadWrite.GetNextTileWriteIndexThreadSafe(); tileIndex < slopeAspectReadWrite.MaxTileIndex; tileIndex = slopeAspectReadWrite.GetNextTileWriteIndexThreadSafe())
                {
                    DigitalSurfaceModel? dsmTile = dsm[tileIndex];
                    if (dsmTile == null)
                    {
                        continue;
                    }

                    // assist in read until neighborhood complete or no more tiles left to read
                    // Only primary DSM bands are needed.
                    bool neighborhoodRead = slopeAspectReadWrite.TryEnsureRasterNeighborhoodRead(tileIndex, dsm, this.cancellationTokenSource);
                    if (neighborhoodRead == false)
                    {
                        Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                        return; // reading was aborted
                    }

                    (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileIndex);
                    RasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.DsmBand);
                    RasterNeighborhood8<float> cmmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.CmmBand);
                    dsmTile.CalculateSlopeAndAspect(dsmNeighborhood, cmmNeighborhood, slopeAspectReadWrite.RasterBandPool);
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    if (this.NoWrite == false)
                    {
                        dsmTile.Write(dsmTile.FilePath, DigitalSurfaceModelBands.SlopeAspect, this.CompressRasters);
                    }
                    lock (slopeAspectReadWrite)
                    {
                        // slope and aspect bands are no longer in use, so return to pool
                        // Primary bands may lie in other, incomplete, processing neighborhoods so are not returned to pool until rows
                        // are released from OnTileRead(). Slope and aspect bands could be returned at the same time as primary bands
                        // but it's somewhat more memory compact to return them here.
                        dsmTile.ReturnBands(DigitalSurfaceModelBands.SlopeAspect, slopeAspectReadWrite.RasterBandPool);
                        slopeAspectReadWrite.OnTileWritten(tileIndexX, tileIndexY);
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
                (string slopeAspectVrtFilePath, string slopeAspectVrtDatasetPath) = VrtDataset.GetVrtPaths(this.Dsm, slopeAspectReadWrite.OutputPathIsDirectory, DigitalSurfaceModel.DirectorySlopeAspect, "slopeAspect.vrt");
                VrtDataset slopeAspectVrt = dsm.CreateDataset(slopeAspectVrtDatasetPath, slopeAspectStatistics);
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
