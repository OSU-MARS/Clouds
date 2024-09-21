using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using Mars.Clouds.Vrt;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata.Ecma335;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Crowns")]
    public class GetCrowns : GdalCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate tree crowns within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each file must contain DigitalSurfaceModel's required bands.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to a single treetop tile matching the DSM tile or 1,2,3) path to directory containing treetop tiles whose names match the DSM tiles. Treetop tiles are expected to be GeoPackages and, if not specified, have .gpkg extension.")]
        [ValidateNotNullOrEmpty]
        public string Treetops { get; set; }

        [Parameter(Mandatory = true, Position = 3, HelpMessage = "1) path to write crown raster to or 2,3) path to a directory to write crown raster tiles to.")]
        [ValidateNotNullOrEmpty]
        public string Crowns { get; set; }

        [Parameter(Position = 3, HelpMessage = "DSM band to find crowns in. Default is 'dsm'.")]
        [ValidateNotNullOrWhiteSpace]
        public string DsmBand { get; set; }

        [Parameter(HelpMessage = "Maximum crown ratio. Default is 0.9.")]
        [ValidateRange(0.0F, 1.0F)]
        public float MaxCrownRatio { get; set; }

        [Parameter(HelpMessage = "Scale factor for cost increase applied when digital surface model height exceeds treetop height. Default is 2.0.")]
        [ValidateRange(0.0F, 1000.0F)] // arbitrary upper bound, could also allow negative
        public float AboveTopPenalty { get; set; }

        [Parameter(HelpMessage = "Whether or not to create a virtual raster of the crown tiles generated.")]
        public SwitchParameter Vrt { get; set; }

        [Parameter(HelpMessage = "Subsampling ratio for gridding treetops.")]
        [ValidateRange(1, 100)] // arbitrary upper bound
        public int Subsample { get; set; }

        [Parameter(HelpMessage = "Whether or not to compress slope and aspect rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(HelpMessage = "Turn off writing of output rasters. This is useful in certain benchmarking and development situations. If -Vrt is specified a .vrt will be generated regardless of whether -NoWrite is used.")]
        public SwitchParameter NoWrite { get; set; }

        public GetCrowns()
        {
            this.cancellationTokenSource = new();

            this.AboveTopPenalty = 2.0F;
            this.CompressRasters = false;
            this.Crowns = String.Empty;
            this.Dsm = String.Empty;
            this.DsmBand = DigitalSurfaceModel.SurfaceBandName;
            this.MaxCrownRatio = 0.9F;
            this.NoWrite = false;
            this.Subsample = 10;
            this.Treetops = String.Empty;
            this.Vrt = false;
        }

        protected override void ProcessRecord()
        {
            const string cmdletName = "Get-Crowns";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRaster<DigitalSurfaceModel>(cmdletName, this.Dsm, (string dsmPrimaryBandFilePath) =>
            {
                return DigitalSurfaceModel.CreateFromPrimaryBandMetadata(dsmPrimaryBandFilePath, DigitalSurfaceModelBands.Primary | DigitalSurfaceModelBands.DsmSlope | DigitalSurfaceModelBands.DsmAspect);
            }, this.cancellationTokenSource);
            bool treetopsPathIsDirectory = Directory.Exists(this.Treetops);
            bool crownsPathIsDirectory = GdalCmdlet.ValidateOrCreateOutputPath(this.Crowns, dsm, nameof(this.Dsm), nameof(this.Crowns));
            if (dsm.NonNullTileCount > 1)
            {
                if (treetopsPathIsDirectory == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Treetops), "-" + this.Treetops + " must be a directory when -" + nameof(this.Dsm) + " indicates multiple tiles to process.");
                }
                if (crownsPathIsDirectory == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Crowns), "-" + this.Crowns + " must be a directory when -" + nameof(this.Dsm) + " indicates multiple tiles to process.");
                }
            }

            VirtualRaster<TreeCrownRaster> crowns = dsm.CreateEmptyCopy<TreeCrownRaster>();
            Debug.Assert((dsm.TileGrid != null) && (crowns.TileGrid != null));
            TileReadCreateWriteStreaming<GridNullable<DigitalSurfaceModel>, DigitalSurfaceModel, TreeCrownRaster> crownReadWrite = TileReadCreateWriteStreaming.Create<GridNullable<DigitalSurfaceModel>, DigitalSurfaceModel, TreeCrownRaster>(dsm.TileGrid, crowns, crownsPathIsDirectory, this.NoWrite, this.CompressRasters);
            GridNullable<List<RasterBandStatistics>>? crownStatistics = this.Vrt ? new(crowns.TileGrid, cloneCrsAndTransform: false) : null;

            long crownCount = 0;
            ParallelTasks crownTasks = new(Int32.Min(this.MaxThreads, dsm.NonNullTileCount), () =>
            {
                TreeCrownSegmentationState segmentationState = new()
                {
                    AboveTopCostScaleFactor = this.AboveTopPenalty,
                    MaximumCrownRatio = this.MaxCrownRatio,
                    MinimumHeightInCrsUnits = 0.30F / (float)dsm.Crs.GetLinearUnits()
                };
                TreetopsGrid? treetops = null; // reuse to offload GC
                while (crownReadWrite.TileWritesInitiated < dsm.NonNullTileCount)
                {
                    crownReadWrite.TryWriteCompletedTiles(this.cancellationTokenSource, crownStatistics);

                    // if all available tiles are written and tiles remain, load next DSM neighborhood and create next crown tile
                    for (int tileIndex = crownReadWrite.GetNextTileReadIndexThreadSafe(); tileIndex < crownReadWrite.MaxTileIndex; tileIndex = crownReadWrite.GetNextTileReadIndexThreadSafe())
                    {
                        DigitalSurfaceModel? dsmTile = dsm[tileIndex];
                        if (dsmTile == null)
                        {
                            continue; // nothing to do as no tile is present at this grid position
                        }

                        // assist in DSM read until neighborhood complete or no more tiles left to read
                        if (crownReadWrite.TryEnsureRasterNeighborhoodRead(tileIndex, dsm, this.cancellationTokenSource) == false)
                        {
                            Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                            return; // reading was aborted
                        }

                        // read tile's treetops
                        string tileName = Tile.GetName(dsmTile.FilePath);
                        string treetopTilePath = treetopsPathIsDirectory ? Path.Combine(this.Treetops, tileName + Constant.File.GeoPackageExtension) : this.Treetops;
                        using DataSource treetopTile = OgrExtensions.OpenForRead(treetopTilePath);
                        using TreetopVector treetopLayer = TreetopVector.Open(treetopTile);
                        SpatialReference treetopCrs = treetopLayer.GetSpatialReference();
                        if (SpatialReferenceExtensions.IsSameCrs(treetopCrs, dsm.Crs) == false)
                        {
                            throw new NotSupportedException("Tile '" + treetopTilePath + "' does not have the same coordinate system ('" + treetopCrs.GetName() + "') as the digital surface model ('" + dsm.Crs.GetName() + "').");
                        }

                        if (treetops == null)
                        {
                            (GridGeoTransform subsampledTransform, int subsampledSizeX, int subsampledSizeY) = dsmTile.Subsample(this.Subsample);
                            treetops = new(dsm.Crs.Clone(), subsampledTransform, subsampledSizeX, subsampledSizeY, treetopLayer.GetFeatureCount(), cloneCrsAndTransform: false);
                        }
                        else
                        {
                            treetops.Reset(dsmTile, this.Subsample);
                        }

                        treetopLayer.GetTreetops(treetops, dsmTile);
                        if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }

                        // report read complete and create crown tile
                        string crownTilePath = crownsPathIsDirectory ? Path.Combine(this.Crowns, tileName + Constant.File.GeoTiffExtension) : this.Crowns;
                        (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileIndex);
                        TreeCrownRaster? crownTile;
                        lock (crownReadWrite)
                        {
                            crownReadWrite.OnTileRead(tileIndexX, tileIndexY);
                            crownTile = new(dsmTile!, crownReadWrite.RasterBandPool)
                            {
                                FilePath = crownTilePath
                            };
                        }

                        // segment crowns
                        // Not currently implemented: treetop addition and removal
                        segmentationState.SetNeighborhoods(dsm, tileIndexX, tileIndexY, this.DsmBand);
                        crownTile.SegmentCrowns(treetops, segmentationState);                        
                        if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }

                        lock (crownReadWrite)
                        {
                            crowns.Add(crownTile);
                            crownReadWrite.OnTileCreated(tileIndexX, tileIndexY);
                        }
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "placeholder");
            while (crownTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = crownReadWrite.TilesRead + (crownReadWrite.TilesRead == 1 ? " DSM read, " : " DSMs read, ") +
                                             crowns.NonNullTileCount + (crowns.NonNullTileCount == 1 ? " tile created, " : " tiles created, ") +
                                             crownReadWrite.TilesWritten + " of " + dsm.NonNullTileCount + " tiles " + (this.NoWrite ? "completed (" : "written (") + 
                                             crownTasks.Count + (crownTasks.Count == 1 ? " thread)..." : " threads)...");
                progress.Update(crownReadWrite.TilesCreated, dsm.NonNullTileCount);
                this.WriteProgress(progress);
            }

            if (crownStatistics != null) // debatable if this.NoWrite should be considered here; for now treat -Vrt as an independent switch
            {
                (string crownsVrtFilePath, string crownsVrtDatasetPath) = VrtDataset.GetVrtPaths(this.Dsm, crownReadWrite.OutputPathIsDirectory, subdirectory: null, "crowns.vrt");
                VrtDataset crownsVrt = crowns.CreateDataset(crownsVrtDatasetPath, crownStatistics);
                crownsVrt.WriteXml(crownsVrtDatasetPath);
            }

            long meanCrownsPerTile = crownCount / dsm.NonNullTileCount;
            progress.Stopwatch.Stop();
            this.WriteVerbose("Found " + crownCount.ToString("n0") + " crowns within " + (dsm.NonNullTileCount > 1 ? dsm.NonNullTileCount + " tiles" : "one tile") + (this.Vrt ? " and generated .vrt" : null) + " in " + progress.Stopwatch.ToElapsedString() + " (" + meanCrownsPerTile.ToString("n0") + " crowns/tile).");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource?.Cancel();
            base.StopProcessing();
        }
    }
}
