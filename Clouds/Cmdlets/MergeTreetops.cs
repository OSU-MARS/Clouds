using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Segmentation;
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
    /// <summary>
    /// Merge treetop and taxa classification tiles into a single treetop file with class prevalence counts within each tree's nominal radius.
    /// </summary>
    /// <remarks>
    /// IO bound as GDAL read speeds on both vector (.gpkg) and raster (.tif) tiles are low, as are write speeds (.gpkg, ~10 MB/s max but 
    /// usually lower). Using 32 threads for tile loading instead of 16 has negligible effect.
    /// </remarks>
    [Cmdlet(VerbsData.Merge, "Treetops")]
    public class MergeTreetops : GdalCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Paths to one or more directories containing treetop tiles. Wildcards may be included and, if not specfied, *.gpkg search pattern will be used.")]
        [ValidateNotNullOrEmpty]
        public List<string> Treetops { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to a directory containing crown rasters raster tiles to merge. Tile names and extents must match treetop tile names and tiles must be in the same CRS as the treetop tiles.")]
        [ValidateNotNullOrEmpty]
        public string Crowns { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "Path to a directory containing classification raster tiles to merge. Tile names and extents must match treetop tile names and tiles must be in the same CRS as the treetop tiles.")]
        [ValidateNotNullOrEmpty]
        public string Classification { get; set; }

        [Parameter(Position = 3, HelpMessage = "Path to merged treetop file to create. If only a file name is specified (default is treetops.gpkg) or if this argument is the treetops file will be created in the first directory indicated by -Treetops.")]
        [ValidateNotNullOrEmpty]
        public string Merge { get; set; }

        public MergeTreetops()
        {
            this.cancellationTokenSource = new();

            this.Classification = String.Empty;
            this.Crowns = String.Empty;
            this.Merge = "treetops" + Constant.File.GeoPackageExtension;
            this.Treetops = [];
        }

        protected override void ProcessRecord()
        {
            // parameter checking
            List<string> treetopTilePaths = this.GetExistingFilePaths(this.Treetops, Constant.File.GeoPackageExtension);
            if (treetopTilePaths.Count < 1)
            {
                // nothing to do
                this.WriteVerbose("Exiting without performing any processing. -" + nameof(this.Treetops) + " = '" + String.Join(", ", this.Treetops) + "' must indicate at least one treetop tile to count classifications over.");
                return;
            }

            if (Directory.Exists(this.Crowns) == false)
            {
                throw new ParameterOutOfRangeException(nameof(this.Crowns), "-" + nameof(this.Crowns) + " must be an existing directory containing crown raster tiles.");
            }
            if (Directory.Exists(this.Classification) == false)
            {
                throw new ParameterOutOfRangeException(nameof(this.Classification), "-" + nameof(this.Classification) + " must be an existing directory containing classification raster tiles.");
            }

            int geopackageSqlBackgroundThreads = GdalCmdlet.EstimateGeopackageSqlBackgroundThreads();
            if (this.DataThreads <= geopackageSqlBackgroundThreads)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), "-" + nameof(this.DataThreads) + " must be at least " + (geopackageSqlBackgroundThreads + 1) + " to allow for local maxima to be identified concurrent with " + geopackageSqlBackgroundThreads + (geopackageSqlBackgroundThreads == 1 ? " SQL thread." : " SQL threads."));
            }

            // build treetop tile dictionary
            string mergedTreetopFilePath = this.Merge;
            string? treetopDirectoryPath = Path.GetDirectoryName(this.Merge);
            if (treetopDirectoryPath == null)
            {
                treetopDirectoryPath = Path.GetDirectoryName(this.Treetops[0]);
                treetopDirectoryPath ??= Environment.CurrentDirectory;

                mergedTreetopFilePath = Path.Combine(treetopDirectoryPath, this.Merge);
            }

            int outputFileIndex = treetopTilePaths.IndexOf(mergedTreetopFilePath);
            if (outputFileIndex >= 0)
            {
                // if an existing output file is present, remove it from the list of input files
                // It's possible other merge files exist in other input directories. These will not be excluded.
                treetopTilePaths.RemoveAt(outputFileIndex);
            }

            Dictionary<string, TilePathAndIsMerged> treetopTilePathsByName = new(treetopTilePaths.Count);
            int tileFieldWidth = 16;
            for (int treetopTileIndex = 0; treetopTileIndex < treetopTilePaths.Count; ++treetopTileIndex)
            {
                string tilePath = treetopTilePaths[treetopTileIndex];
                string tileName = Tile.GetName(tilePath);
                treetopTilePathsByName.Add(tileName, new(tilePath));

                if (tileFieldWidth < tileName.Length)
                {
                    tileFieldWidth = tileName.Length;
                }
            }

            // grid crown and classification virtual rasters
            const string cmdletName = "Merge-Treetops";
            VirtualRaster<TreeCrownRaster> crowns = this.ReadVirtualRasterMetadata(cmdletName, this.Crowns, TreeCrownRaster.CreateFromBandMetadata, this.cancellationTokenSource);
            VirtualRaster<LandCoverRaster> classification = this.ReadVirtualRasterMetadata(cmdletName, this.Merge, LandCoverRaster.CreateFromBandMetadata, this.cancellationTokenSource);
            if (SpatialReferenceExtensions.IsSameCrs(crowns.Crs, classification.Crs) == false)
            {
                throw new NotSupportedException("Crown virtual raster '" + this.Crowns + "' is in '" + crowns.Crs.GetName() + "' while classification virtual raster '" + this.Classification + "' is in '" + classification.Crs.GetName() + "'.");
            }
            if (crowns.IsSameExtentAndSpatialResolution(classification) == false)
            {
                throw new NotSupportedException("Crown and classification virtual rasters '" + this.Crowns + "' and '" + this.Classification + "' differ in spatial extent or resolution. Sizes are " + crowns.SizeInTilesX + " by " + crowns.SizeInTilesY + " and " + classification.SizeInTilesX + " by " + classification.SizeInTilesY + " tiles with tiles being " + crowns.TileCellSizeX + " by " + crowns.TileSizeInCellsY + " and " + classification.TileSizeInCellsX + " by " + classification.TileSizeInCellsY + " cells, respectively.");
            }

            // create output layer
            string[] classNames = Enum.GetNames<LandCoverClassification>();
            Debug.Assert(classNames.Length == (int)LandCoverClassification.MaxValue);
            using DataSource mergedTreetopFile = OgrExtensions.CreateOrOpenForWrite(mergedTreetopFilePath);
            TreetopVector mergedTreetopLayer = TreetopVector.CreateOrOverwrite(mergedTreetopFile, classification.Crs, tileFieldWidth, classNames);

            // load tiles, count trees cells' by classification, and commit trees to output
            int maxDataThreads = this.DataThreads - geopackageSqlBackgroundThreads;
            int totalTreetops = 0;
            TreetopsReadCreateWrite treetopReadWrite = TreetopsReadCreateWrite.Create(crowns, classification);
            ParallelTasks loadAndClassifyTreetops = new(Int32.Min(maxDataThreads, treetopTilePaths.Count), () =>
            {
                Treetops? treetops = null;
                for (int tileIndex = treetopReadWrite.GetNextTileReadIndexThreadSafe(); tileIndex < treetopReadWrite.MaxTileIndex; tileIndex = treetopReadWrite.GetNextTileReadIndexThreadSafe())
                {
                    if (treetopReadWrite.TryEnsureNeighborhoodRead(tileIndex, crowns, this.cancellationTokenSource) == false)
                    {
                        Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                        return; // reading was aborted
                    }

                    TreeCrownRaster? crownTile = crowns[tileIndex];
                    LandCoverRaster? classificationTile = classification[tileIndex];
                    if ((crownTile == null) || (classificationTile == null))
                    {
                        continue; // any unmerged treetop tiles are logged below
                    }
                    if (crownTile.IsSameExtentAndSpatialResolution(classification) == false)
                    {

                    }

                    // read treetop tile matching this classification tile
                    string tileName = Tile.GetName(classificationTile.FilePath);
                    TilePathAndIsMerged treetopTilePathAndIsMerged = treetopTilePathsByName[tileName];

                    using DataSource? treetopTile = OgrExtensions.OpenForRead(treetopTilePathAndIsMerged.Path);
                    using TreetopVector treetopLayer = TreetopVector.Open(treetopTile);
                    SpatialReference treetopTileCrs = treetopLayer.GetSpatialReference();
                    if (SpatialReferenceExtensions.IsSameCrs(treetopTileCrs, classification.Crs) == false)
                    {
                        throw new NotSupportedException("Tile '" + treetopTilePathAndIsMerged + "' does not have the same coordinate system ('" + treetopTileCrs.GetName() + "') as other tiles ('" + classification.Crs.GetName() + "').");
                    }

                    (double crownXmin, double crownXmax, double crownYmin, double crownYmax) = crownTile.GetExtent();
                    Extent treetopExtent = treetopLayer.GetExtent();
                    if (treetopExtent.IsSameOrWithin(crownXmin, crownXmax, crownXmin, crownYmax) == false)
                    {
                        throw new NotSupportedException("Treetop tile '" + treetopTilePathAndIsMerged.Path + "' with extent (" + treetopExtent.GetExtentString() + ") does not lie within crown tile '" + crownTile.FilePath + "' with extents (" + crownTile.GetExtentString() + ").");
                    }

                    treetopLayer.GetTreetops(classNames.Length, ref treetops);
                    (double tileCentroidX, double tileCentroidY) = crownTile.GetCentroid();
                    if (crowns.TryGetNeighborhood8(tileCentroidX, tileCentroidY, bandName: null, out RasterNeighborhood8<int>? crownNeighborhood) == false)
                    {
                        throw new InvalidOperationException("Could not form crown neighborhood for treetop tile centered at (" + tileCentroidX + ", " + tileCentroidY + ").");
                    }
                    if (classification.TryGetNeighborhood8(tileCentroidX, tileCentroidY, bandName: null, out RasterNeighborhood8<byte>? classificationNeighborhood) == false)
                    {
                        throw new InvalidOperationException("Could not form classification neighborhood for treetop tile centered at (" + tileCentroidX + ", " + tileCentroidY + ").");
                    }

                    // count classes appearing within each tree's delineated, connected crown
                    treetops.GetClassCounts(crownNeighborhood, classificationNeighborhood);

                    // write treetops with their class counts
                    // GDAL APIs work with a single thread per layer or file, so single threaded output to a single layer is an unavoidable
                    // bottleneck. https://gdal.org/en/stable/user/multithreading.html
                    lock (mergedTreetopLayer)
                    {
                        mergedTreetopLayer.Add(tileName, treetops, classNames);
                        treetopTilePathAndIsMerged.IsMerged = true;
                        totalTreetops += treetops.Count;
                    }

                    (int tileIndexX, int tileIndexY) = crowns.ToGridIndices(tileIndex);
                    lock (treetopReadWrite)
                    {
                        treetopReadWrite.OnTileWritten(tileIndexX, tileIndexY);
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "placeholder");
            while (loadAndClassifyTreetops.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Merging treetops and classifications: " + treetopReadWrite.TilesRead + " of " + treetopTilePaths.Count + (treetopTilePaths.Count == 1 ? " tile " : " tiles ") + "(" + loadAndClassifyTreetops.Count + (loadAndClassifyTreetops.Count == 1 ? " threads)..." : " thread)...");
                progress.Update(treetopReadWrite.TilesWritten, treetopTilePaths.Count);
                this.WriteProgress(progress);
            }

            // explicitly dispose merged treetop layer to trigger final write transaction commit and then flush the write cache to file
            // CLR doesn't have to Dispose() when a using() { } ends and tends not to, so relying on using() and not synchronously waiting
            // on the flush creates problems with reporting incomplete execution times and the cmdlet appearing to have exited while the
            // write is still continuing to disk.
            progress.StatusDescription = "Waiting for GDAL to finish committing treetops to '" + this.Merge + "'...";
            progress.PercentComplete = 0;
            progress.SecondsRemaining = -1;
            this.WriteProgress(progress);

            mergedTreetopLayer.Dispose();
            mergedTreetopFile.FlushCache(); // GDAL write speeds are only ~10 MB/s max

            // report any unmerged treetop tiles
            foreach (TilePathAndIsMerged treetopTile in treetopTilePathsByName.Values)
            {
                if (treetopTile.IsMerged == false)
                {
                    this.WriteError(new ErrorRecord(null, "Treetop tile '" + treetopTile.Path + "' was not merged because no corresponding classification tile was found.", ErrorCategory.ReadError, null));
                }
            }

            progress.Stopwatch.Stop();
            this.WriteVerbose("Merged " + totalTreetops.ToString("#,#,0") + " treetops from " + treetopReadWrite.TilesWritten + (treetopReadWrite.TilesWritten == 1 ? " tile " : " tiles ") + "in " + progress.Stopwatch.ToElapsedString() + ".");
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource.Cancel();
            base.StopProcessing();
        }

        private class TilePathAndIsMerged
        {
            public string Path { get; init; }
            public bool IsMerged { get; set; }

            public TilePathAndIsMerged(string path)
            {
                this.Path = path;
                this.IsMerged = false;
            }
        }

        private class TreetopsReadCreateWrite : TileReadWriteStreaming<TreeCrownRaster, TileStreamPosition>
        {
            public VirtualRaster<LandCoverRaster> Classification { get; private init; }
            public VirtualRaster<TreeCrownRaster> Crowns { get; private init; }

            protected TreetopsReadCreateWrite(VirtualRaster<TreeCrownRaster> crowns, VirtualRaster<LandCoverRaster> classification, bool[,] unpopulatedTileMapForRead, TileStreamPosition tileWritePosition)
                : base(crowns.TileGrid!, unpopulatedTileMapForRead, tileWritePosition, outputPathIsDirectory: false)
            {
                this.Classification = classification;
                this.Crowns = crowns;
            }

            public static TreetopsReadCreateWrite Create(VirtualRaster<TreeCrownRaster> crowns, VirtualRaster<LandCoverRaster> classification)
            {
                GridNullable<TreeCrownRaster>? crownTileGrid = crowns.TileGrid;
                if (crownTileGrid == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(crowns), "Crown virtual raster's grid has not been created.");
                }

                bool[,] unpopulatedTileMapForRead = crownTileGrid.GetUnpopulatedCellMap();
                bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
                return new(crowns, classification, unpopulatedTileMapForRead, new(crownTileGrid, unpopulatedTileMapForWrite));
            }

            protected override void OnSourceTileRead(int tileReadIndexX, int tileReadIndexY)
            {
                base.OnSourceTileRead(tileReadIndexX, tileReadIndexY);

                LandCoverRaster? classificationTile = this.Classification[tileReadIndexX, tileReadIndexY];
                if (classificationTile == null)
                {
                    return;
                }

                // must load tile at given index even if it's beyond the necessary neighborhood
                // Otherwise some tiles would not get loaded.
                lock (this)
                {
                    classificationTile.TryTakeOwnershipOfDataBuffers(this.RasterBandPool);
                }

                classificationTile.ReadBandData();
            }
        }
    }
}
