using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Segmentation;
using OSGeo.OGR;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

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
        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Path to a directory containing the treetop .gpkg files to merge or a wildcarded path to a set of treetop tiles to process.")]
        [ValidateNotNullOrEmpty]
        public string? Treetops { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to a directory containing classification raster tiles to merge. Names must match the treetop tile names and the data type must be bytes and they must be in the same CRS as the treetops.")]
        [ValidateNotNullOrEmpty]
        public string? Classification { get; set; }

        [Parameter(Position = 2, HelpMessage = "Name of merged treetop file to create in the directory indicated by -Treetops.")]
        [ValidateNotNullOrEmpty]
        public string Merge { get; set; }

        [Parameter(Position = 3, HelpMessage = "Names of classses in the classification rasters. It is assumed a raster value of 1 indicates the first named class, 2 the second class, and so on.")]
        [ValidateNotNullOrEmpty]
        public string[] ClassNames { get; set; }

        public MergeTreetops() 
        { 
            this.ClassNames = [ "conifer", "nonForest", "hardwood", "unknown" ];
            this.Merge = "treetops.gpkg";
        }

        private (VirtualRaster<byte> classificationTiles, TimeSpan elapsedTime) LoadClassificationTiles(IList<string> treetopTilePaths)
        {
            Debug.Assert(this.Classification != null);

            Stopwatch stopwatch = Stopwatch.StartNew();
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = this.MaxThreads
            };

            int tilesLoaded = 0;
            VirtualRaster<byte> classificationTiles = new();
            Task loadClassificationVrt = Task.Run(() =>
            {
                // for now, load classification tiles in direct correspondence to treetop tiles
                // Current behavior is ok if classification and treetop tiles are 1:1 over a contiguous extent or if treetop tiles
                // extend beyond the classified area. If either tile grid has holes or only part of the treetop tiles is indicated 
                // then useful classification tiles may not be loaded. Incomplete classification accounts will follow from the edge
                // effects.
                Parallel.For(0, treetopTilePaths.Count, parallelOptions, (int tileIndex) =>
                {
                    string treetopTilePath = treetopTilePaths[tileIndex];
                    string treetopTileBasename = Path.GetFileNameWithoutExtension(treetopTilePath);
                    string classificationTilePath = Path.Combine(this.Classification, treetopTileBasename + ".tif");
                    if (File.Exists(classificationTilePath) == false)
                    {
                        return; // treetop tile has no corresponding classification tile
                    }

                    Raster<byte> classificationTile = GdalCmdlet.ReadRaster<byte>(classificationTilePath);
                    lock (classificationTiles)
                    {
                        classificationTiles.Add(classificationTile);
                        ++tilesLoaded;
                    }
                });
            });

            TimeSpan progressInterval = TimeSpan.FromSeconds(2.0);
            ProgressRecord progressRecord = new(0, "Get-Treetops", "placeholder");
            while (loadClassificationVrt.Wait(progressInterval) == false)
            {
                float fractionComplete = (float)tilesLoaded / (float)treetopTilePaths.Count;
                progressRecord.StatusDescription = "Loading classification tile " + Path.GetFileNameWithoutExtension(treetopTilePaths[Int32.Min(tilesLoaded, treetopTilePaths.Count - 1)]) + "..."; // same basename
                progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(progressRecord);
            }

            stopwatch.Stop();
            return (classificationTiles, stopwatch.Elapsed);
        }

        private (SortedList<string, Treetops>, int tileFieldWidth, TimeSpan elapsedTime) LoadTreetopTiles(IList<string> treetopTilePaths, VirtualRaster<byte> classificationTiles)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = this.MaxThreads
            };

            SortedList<string, Treetops> treetopsByTile = new(treetopTilePaths.Count);
            int tileFieldWidth = 16;
            int tilesLoaded = 0;
            Task loadAndClassifyTreetops = Task.Run(() =>
            {
                // TODO: schedule tasks explicitly as Parallel tends to use only 2-4 cores
                Parallel.For(0, treetopTilePaths.Count, parallelOptions, (int tileIndex) =>
                {
                    string treetopTilePath = treetopTilePaths[tileIndex];
                    using DataSource? treetopTile = Ogr.Open(treetopTilePath, update: 0);
                    using TreetopLayer treetopLayer = TreetopLayer.Open(treetopTile);
                    SpatialReference tileCrs = treetopLayer.GetSpatialReference();
                    if (tileCrs.IsSame(classificationTiles.Crs, []) != 1)
                    {
                        throw new NotSupportedException("Tile '" + treetopTilePath + "' does not have the same coordinate system ('" + tileCrs.GetName() + "') as other tiles ('" + classificationTiles.Crs.GetName() + "').");
                    }

                    Treetops treetops = treetopLayer.GetTreetops(this.ClassNames.Length);
                    (double treetopTileXcentroid, double treetopTileYcentroid) = treetopLayer.GetCentroid();
                    if (classificationTiles.TryGetNeighborhood8(treetopTileXcentroid, treetopTileYcentroid, 0, out VirtualRasterNeighborhood8<byte>? neighborhood))
                    {
                        // get classification counts if the treetop tile has a corresponding classification tile
                        // Some incomplete classification information may available for treetops with a classification within their radius.
                        // For now, it's considered acceptable to leave these trees' counts as zero. Effetively incomplete counts are
                        // reported for treetops which like within a classification tile but whose radius extends beyond the area where
                        // classification information is available.
                        treetops.GetClassCounts(neighborhood);
                    }

                    string treetopTileBaseName = Path.GetFileNameWithoutExtension(treetopTilePath);
                    lock (treetopsByTile)
                    {
                        treetopsByTile.Add(treetopTileBaseName, treetops);

                        if (tileFieldWidth < treetopTileBaseName.Length)
                        {
                            tileFieldWidth = treetopTileBaseName.Length;
                        }

                        ++tilesLoaded;
                    }
                });
            });

            TimeSpan progressInterval = TimeSpan.FromSeconds(2.0);
            ProgressRecord progressRecord = new(0, "Get-Treetops", "placeholder");
            while (loadAndClassifyTreetops.Wait(progressInterval) == false)
            {
                float fractionComplete = (float)tilesLoaded / (float)treetopTilePaths.Count;
                progressRecord.StatusDescription = "Loading treetops and classifications in " + Path.GetFileNameWithoutExtension(treetopTilePaths[Int32.Min(tilesLoaded, treetopTilePaths.Count - 1)]) + "...";
                progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(progressRecord);
            }

            stopwatch.Stop();
            return (treetopsByTile, tileFieldWidth, stopwatch.Elapsed);
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Classification != null) && (this.Treetops != null));

            (string? treetopDirectoryPath, string? treetopTileSearchPattern) = GdalCmdlet.ExtractTileDirectoryPathAndSearchPattern(this.Treetops, "*.gpkg");
            if (treetopDirectoryPath == null)
            {
                throw new ParameterOutOfRangeException(nameof(this.Treetops), "-" + nameof(this.Treetops) + " must indicate an existing directory where treetop files are located.");
            }
            if (treetopTileSearchPattern == null)
            {
                throw new ParameterOutOfRangeException(nameof(this.Treetops), "If -" + nameof(this.Treetops) + " does not indicate a directory it must include a file name pattern to match.");
            }
            FileAttributes classificationPathAttributes = File.GetAttributes(this.Classification);
            if (classificationPathAttributes.HasFlag(FileAttributes.Directory) == false)
            {
                throw new ParameterOutOfRangeException(nameof(this.Classification), "-" + nameof(this.Classification) + " must be an existing directory containing classification rasters.");
            }

            List<string> treetopTilePaths = Directory.EnumerateFiles(treetopDirectoryPath, treetopTileSearchPattern, SearchOption.TopDirectoryOnly).ToList();
            if (treetopTilePaths.Count < 2)
            {
                // nothing to do
                this.WriteVerbose("Exiting without performing any processing. Path '" + Path.Combine(treetopDirectoryPath, treetopTileSearchPattern) + "' either does indicate at least two treetop tiles to merge.");
                return;
            }

            string mergedTreetopFilePath = Path.Combine(treetopDirectoryPath, this.Merge);
            int outputFileIndex = treetopTilePaths.IndexOf(mergedTreetopFilePath);
            if (outputFileIndex >= 0)
            { 
                treetopTilePaths.RemoveAt(outputFileIndex);
            }
            treetopTilePaths.Sort(StringComparer.Ordinal);

            // load tiles and get treetop classifications
            (VirtualRaster<byte> classificationTiles, TimeSpan classificationLoadTime) = this.LoadClassificationTiles(treetopTilePaths);
            classificationTiles.BuildGrid();
            (SortedList<string, Treetops> treetopsByTile, int tileFieldWidth, TimeSpan treetopLoadTime) = this.LoadTreetopTiles(treetopTilePaths, classificationTiles);

            // write merged and classified treetops
            // GDAL APIs work with a single thread per layer or file, so an unavoidable bottleneck. Particularly in write to disk.
            Stopwatch stopwatch = Stopwatch.StartNew();
            int totalTreetops = 0;
            DataSource mergedTreetopFile = File.Exists(mergedTreetopFilePath) ? Ogr.Open(mergedTreetopFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(mergedTreetopFilePath, []);
            TreetopLayer mergedTreetopLayer = TreetopLayer.CreateOrOverwrite(mergedTreetopFile, classificationTiles.Crs, tileFieldWidth, this.ClassNames);
            ProgressRecord progressRecord = new(0, "Get-Treetops", "placeholder");
            for (int tileIndex = 0; tileIndex < treetopsByTile.Count; ++tileIndex)
            {
                if (tileIndex % 10 == 0)
                {
                    float fractionComplete = (float)tileIndex / (float)treetopsByTile.Count;
                    progressRecord.StatusDescription = "Adding treetops to " + this.Merge + " (tile " + tileIndex + " of " + treetopsByTile.Count + ")...";
                    progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                    progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                    this.WriteProgress(progressRecord);
                }

                Treetops treetops = treetopsByTile.Values[tileIndex];
                mergedTreetopLayer.Add(treetopsByTile.Keys[tileIndex], treetops, this.ClassNames);
                totalTreetops += treetops.Count;
            }

            progressRecord.StatusDescription = "Completing write of " + totalTreetops.ToString("#,#,0") + " treetops to " + this.Merge + "...";
            progressRecord.PercentComplete = 0;
            progressRecord.SecondsRemaining = -1;
            this.WriteProgress(progressRecord);
            // explicitly dispose merged treetop layer to trigger write transaction commit and then flush the write cache to file
            // CLR doesn't have to Dispose() when a using() { } ends and tends not to, so relying using() and not synchronously waiting
            // on the flush creates problems with reporting incomplete execution times and the cmdlet appearing to have exited while the
            // write is committing to disk.
            mergedTreetopLayer.Dispose();
            mergedTreetopFile.FlushCache(); // GDAL write speeds are only ~10 MB/s max

            stopwatch.Stop();
            TimeSpan writeTime = stopwatch.Elapsed;
            TimeSpan totalTime = classificationLoadTime + treetopLoadTime + writeTime;
            string elapsedTimeFormat = totalTime.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Merged " + totalTreetops.ToString("#,#,0") + " treetops in " + totalTime.ToString(elapsedTimeFormat) + " (classification raster load " + classificationLoadTime.ToString("mm\\:ss") + ", treetop load and class counting " + treetopLoadTime.ToString("mm\\:ss") + ").");
        }
    }
}
