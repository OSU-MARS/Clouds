using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
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

        [Parameter(HelpMessage = "Name of band to values in surface raster. Default is \"dsm\".")]
        public string? DsmBand { get; set; }

        public GetDsmSlopeAspect()
        {
            this.cancellationTokenSource = new();

            this.Dsm = String.Empty; // mandatory
            this.DsmBand = DigitalSurfaceModel.SurfaceBandName;
        }

        protected override void ProcessRecord()
        {
            string cmdletName = "Get-DsmSlopeAspect";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRaster<DigitalSurfaceModel>(cmdletName, this.Dsm, readData: true, this.cancellationTokenSource);

            int maxDsmTileIndex = dsm.VirtualRasterSizeInTilesX * dsm.VirtualRasterSizeInTilesY;
            int treetopFindsInitiated = -1;
            int treetopFindsCompleted = 0;
            ParallelTasks slopeAspectTasks = new(Int32.Min(this.MaxThreads, dsm.NonNullTileCount), () =>
            {
                for (int tileIndex = Interlocked.Increment(ref treetopFindsInitiated); tileIndex < maxDsmTileIndex; tileIndex = Interlocked.Increment(ref treetopFindsInitiated))
                {
                    DigitalSurfaceModel? dsmTile = dsm[tileIndex];
                    if (dsmTile == null)
                    {
                        continue;
                    }

                    (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileIndex);
                    VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.DsmBand);
                    SlopeAspectRaster slopeAspectTile = new(dsmNeighborhood);
                    string slopeAspectFilePath = Raster.GetDiagnosticFilePath(dsmTile.FilePath, DigitalSurfaceModel.DiagnosticDirectorySlopeAspect, createDiagnosticDirectory: true);
                    slopeAspectTile.Write(slopeAspectFilePath, this.CompressRasters);

                    lock (dsm)
                    {
                        ++treetopFindsCompleted;
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "placeholder");
            while (slopeAspectTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Calculating slope and aspect over " + dsm.NonNullTileCount + (dsm.NonNullTileCount == 1 ? " tile (" : " tiles (") + slopeAspectTasks.Count + (slopeAspectTasks.Count == 1 ? " thread)..." : " threads)...");
                progress.Update(treetopFindsCompleted, dsm.NonNullTileCount);
                this.WriteProgress(progress);
            }

            progress.Stopwatch.Stop();
            string tileOrTiles = dsm.NonNullTileCount > 1 ? "tiles" : "tile";
            this.WriteVerbose("Found slope and aspect in " + dsm.NonNullTileCount + " " + tileOrTiles + " in " + progress.Stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource?.Cancel();
            base.StopProcessing();
        }
    }
}
