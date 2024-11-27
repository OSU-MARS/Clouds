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

        [Parameter(HelpMessage = "Maximum path cost in CRS units. Default is 40 m.")]
        [ValidateRange(0.0F, 200.0F)] // arbitrary upper bound
        public float MaxPath { get; set; }

        [Parameter(HelpMessage = "Minimum height, in CRS units, for tree crown connectivity. Default is 1.0 m.")]
        [ValidateRange(0.0F, 1.0F)]
        public float MinHeight { get; set; }

        [Parameter(HelpMessage = "Scale factor for cost increase applied when digital surface model height exceeds treetop height. Default is 2.0.")]
        [ValidateRange(0.0F, 1000.0F)] // arbitrary upper bound, could also allow negative
        public float AboveTopPenalty { get; set; }

        [Parameter(HelpMessage = "Whether or not to create a virtual raster of the crown tiles generated.")]
        public SwitchParameter Vrt { get; set; }

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
            this.MaxPath = Single.NaN;
            this.MinHeight = Single.NaN;
            this.NoWrite = false;
            this.Treetops = String.Empty;
            this.Vrt = false;
        }

        protected override void ProcessRecord()
        {
            const string cmdletName = "Get-Crowns";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRasterMetadata<DigitalSurfaceModel>(cmdletName, this.Dsm, (string dsmPrimaryBandFilePath) =>
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

            TreeCrownReadCreateWriteStreaming crownReadWrite = TreeCrownReadCreateWriteStreaming.Create(this.Treetops, dsm, crownsPathIsDirectory, this.NoWrite, this.CompressRasters);
            VirtualRaster<TreeCrownRaster> crowns = crownReadWrite.Crowns;
            VirtualVector<TreetopsGrid> treetops = crownReadWrite.Treetops;

            Debug.Assert(crowns.TileGrid != null);
            GridNullable<List<RasterBandStatistics>>? crownStatistics = this.Vrt ? new(crowns.TileGrid, cloneCrsAndTransform: false) : null;

            long crownCount = 0;
            float crsLinearUnitInM = (float)dsm.Crs.GetLinearUnits();
            float minimumHeightInCrsUnits = Single.IsNaN(this.MinHeight) ? 1.0F / crsLinearUnitInM : this.MinHeight;
            float pathCostLimitInCrsUnits = Single.IsNaN(this.MaxPath) ? 40.0F / crsLinearUnitInM : this.MaxPath;
            ParallelTasks crownTasks = new(Int32.Min(this.DataThreads, dsm.NonNullTileCount), () =>
            {
                TreeCrownSegmentationState segmentationState = new()
                {
                    AboveTopCostScaleFactor = this.AboveTopPenalty,
                    MaximumCrownRatio = this.MaxCrownRatio,
                    MinimumHeightInCrsUnits = minimumHeightInCrsUnits,
                    PathCostLimitInCrsUnits = pathCostLimitInCrsUnits
                };
                while (crownReadWrite.TileWritesInitiated < dsm.NonNullTileCount)
                {
                    crownReadWrite.TryWriteCompletedTiles(this.cancellationTokenSource, crownStatistics);

                    // if all available tiles are written and tiles remain, load next DSM neighborhood and create next crown tile
                    for (int tileCreateIndex = crownReadWrite.GetNextTileCreateIndexThreadSafe(); tileCreateIndex < crownReadWrite.MaxTileIndex; tileCreateIndex = crownReadWrite.GetNextTileCreateIndexThreadSafe())
                    {
                        DigitalSurfaceModel? dsmTile = dsm[tileCreateIndex];
                        if (dsmTile == null)
                        {
                            continue; // nothing to do as no tile is present at this grid position
                        }

                        // assist in treetop and DSM read until neighborhood complete or no more tiles left to read
                        if (crownReadWrite.TryEnsureNeighborhoodRead(tileCreateIndex, dsm, this.cancellationTokenSource) == false)
                        {
                            Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                            return; // reading was aborted
                        }

                        TreetopsGrid? treetopTile = crownReadWrite.Treetops[tileCreateIndex];
                        Debug.Assert(treetopTile != null);

                        // report read complete and create crown tile
                        string tileName = Tile.GetName(dsmTile.FilePath);
                        string crownTilePath = crownsPathIsDirectory ? Path.Combine(this.Crowns, tileName + Constant.File.GeoTiffExtension) : this.Crowns;
                        (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileCreateIndex);
                        TreeCrownRaster? crownTile;
                        lock (crownReadWrite)
                        {
                            crownTile = new(dsmTile, crownReadWrite.WriteBandPool)
                            {
                                FilePath = crownTilePath
                            };
                        }

                        // segment crowns
                        // Not currently implemented: treetop addition and removal
                        segmentationState.SetNeighborhoodsAndCellSize(dsm, treetops, tileIndexX, tileIndexY, this.DsmBand);
                        crownTile.SegmentCrowns(segmentationState);
                        if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }

                        lock (crownReadWrite)
                        {
                            crownCount += treetopTile.Treetops;
                            crowns.Add(tileIndexX, tileIndexY, crownTile);
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
            this.WriteVerbose("Delineated " + crownCount.ToString("n0") + " crowns within " + (dsm.NonNullTileCount > 1 ? dsm.NonNullTileCount + " tiles" : "one tile") + (this.Vrt ? " and generated .vrt" : null) + " in " + progress.Stopwatch.ToElapsedString() + " (" + meanCrownsPerTile.ToString("n0") + " crowns/tile).");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource?.Cancel();
            base.StopProcessing();
        }

        private class TreeCrownReadCreateWriteStreaming : TileReadCreateWriteStreaming<GridNullable<DigitalSurfaceModel>, DigitalSurfaceModel, TreeCrownRaster>
        {
            private readonly string treetopsPath;

            public VirtualRaster<TreeCrownRaster> Crowns { get; private init; }
            public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
            public ObjectPool<TreetopsGrid> TreetopPool { get; private init; }
            public VirtualVector<TreetopsGrid> Treetops { get; private init; }

            protected TreeCrownReadCreateWriteStreaming(string treetopsPath, VirtualRaster<DigitalSurfaceModel> dsm, GridNullable<DigitalSurfaceModel> dsmGrid, bool[,] unpopulatedTileMapForRead, VirtualRaster<TreeCrownRaster> crowns, GridNullable<TreeCrownRaster> crownsGrid, bool[,] unpopulatedTileMapForCreate, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
                : base(dsmGrid, unpopulatedTileMapForRead, crownsGrid, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, outputPathIsDirectory)
            {
                this.treetopsPath = treetopsPath;

                this.Crowns = crowns;
                this.Dsm = dsm;
                this.TreetopPool = new();
                this.Treetops = new(dsmGrid);
            }

            public static TreeCrownReadCreateWriteStreaming Create(string treetopsPath, VirtualRaster<DigitalSurfaceModel> dsm, bool crownsPathIsDirectory, bool bypassOutputRasterWriteToDisk, bool compressRasters)
            {
                if (dsm.TileGrid == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "DSM virtual raster's grid must be created before tiles can be streamed from it.");
                }

                VirtualRaster<TreeCrownRaster> crowns = dsm.CreateEmptyCopy<TreeCrownRaster>();
                Debug.Assert(crowns.TileGrid != null);

                bool[,] unpopulatedTileMapForRead = dsm.TileGrid.GetUnpopulatedCellMap();
                bool[,] unpopulatedTileMapForCreate = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                return new(treetopsPath, dsm, dsm.TileGrid, unpopulatedTileMapForRead, crowns, crowns.TileGrid, unpopulatedTileMapForCreate, unpopulatedTileMapForWrite, crownsPathIsDirectory)
                {
                    BypassOutputRasterWriteToDisk = bypassOutputRasterWriteToDisk,
                    CompressRasters = compressRasters,
                };
            }

            protected override void OnCreatedTileUnreferenced(int unreferencedTileIndexX, int unreferencedTileIndexY, TreeCrownRaster tile)
            {
                // return raster bands as usual
                base.OnCreatedTileUnreferenced(unreferencedTileIndexX, unreferencedTileIndexY, tile);

                // also return gridded vector lists of treetops
                if (this.Treetops.TryRemoveAt(unreferencedTileIndexX, unreferencedTileIndexY, out TreetopsGrid? treetopsTile))
                {
                    this.TreetopPool.Return(treetopsTile);
                }
            }

            protected override void OnSourceTileRead(int tileReadIndexX, int tileReadIndexY)
            {
                DigitalSurfaceModel? dsmTile = this.Dsm[tileReadIndexX, tileReadIndexY];
                Debug.Assert(dsmTile != null);

                // read tile's treetops
                // Treetops are read before the DSM as base.OnTileRead() marks the read at this position complete.
                string tileName = Tile.GetName(dsmTile.FilePath);
                string treetopTilePath = this.OutputPathIsDirectory ? Path.Combine(this.treetopsPath, tileName + Constant.File.GeoPackageExtension) : this.treetopsPath;
                using DataSource treetopDataSource = OgrExtensions.OpenForRead(treetopTilePath);
                using TreetopVector treetopLayer = TreetopVector.Open(treetopDataSource);
                SpatialReference treetopCrs = treetopLayer.GetSpatialReference();
                if (SpatialReferenceExtensions.IsSameCrs(treetopCrs, dsmTile.Crs) == false)
                {
                    throw new NotSupportedException("Tile '" + treetopTilePath + "' does not have the same coordinate system ('" + treetopCrs.GetName() + "') as the digital surface model ('" + dsmTile.Crs.GetName() + "').");
                }

                if (this.TreetopPool.TryGet(out TreetopsGrid? treetopTile))
                {
                    treetopTile.Reset(dsmTile);
                }
                else
                {
                    double treetopGridCellSizeX = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellWidth;
                    double treetopGridCellSizeY = TreeCrownCostField.CapacityXY * dsmTile.Transform.CellHeight; // default to same sign as DSM cell height
                    (GridGeoTransform transform, int spanningSizeX, int spanningSizeY) = dsmTile.GetSpanningEquivalent(treetopGridCellSizeX, treetopGridCellSizeY);
                    treetopTile = new(dsmTile.Crs.Clone(), transform, spanningSizeX, spanningSizeY, cloneCrsAndTransform: false);
                }

                treetopLayer.GetTreetops(treetopTile, dsmTile);
                this.Treetops.Add(tileReadIndexX, tileReadIndexY, treetopTile);
            }
        }
    }
}
