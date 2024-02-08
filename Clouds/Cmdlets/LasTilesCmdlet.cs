using Mars.Clouds.Las;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using System.Management.Automation;
using Mars.Clouds.GdalExtensions;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    public class LasTilesCmdlet : GdalCmdlet
    {
        protected static TimeSpan ProgressUpdateInterval { get; private set; }

        [Parameter(Mandatory = true, HelpMessage = ".las files to load points from. Can be a single file or a set of files if wildcards are used.")]
        [ValidateNotNullOrEmpty]
        public string? Las { get; set; }

        // quick solution for constraining memory consumption
        // A more adaptive implementation would track memory consumption (e.g. GlobalMemoryStatusEx()), estimate it from the size of the
        // tiles, or use a rasonably stable bound such as the total number of points loaded.
        [Parameter(HelpMessage = "Maximum number of tiles to have fully loaded at the same time (default is 25). This is a safeguard to constrain maximum memory consumption in situations where tiles are loaded faster than point metrics can be calculated.")]
        public int MaxTiles { get; set; }

        [Parameter(HelpMessage = "Whether to snap tile origin and extent to next largest integer in CRS units. This can be a useful correction in cases where .las file extents are slightly smaller than the actual tile size and, because a 2x2 or larger grid of tiles is not being processed, either a tile's true x extent, y extent, or both cannot be inferred from tile to tile spacing.")]
        public SwitchParameter Snap { get; set; }

        static LasTilesCmdlet()
        {
            LasTilesCmdlet.ProgressUpdateInterval = TimeSpan.FromSeconds(10.0);
        }

        protected LasTilesCmdlet() 
        {
            // this.Las is mandatory
            this.MaxTiles = 25;
            this.Snap = false;
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(int? requiredEpsg)
        {
            string[] lasTilePaths = GdalCmdlet.GetExistingTilePaths(this.Las, Constant.File.LasExtension);

            Stopwatch stopwatch = new();
            stopwatch.Start();

            List<LasTile> lasTiles = new(lasTilePaths.Length);
            ProgressRecord tileIndexProgress = new(0, "Get-GridMetrics", "placeholder"); // can't pass null or empty statusDescription
            for (int tileIndex = 0; tileIndex < lasTilePaths.Length; tileIndex++)
            {
                // tile load status
                float fractionComplete = (float)tileIndex / (float)lasTilePaths.Length;
                tileIndexProgress.StatusDescription = "Reading tile header " + tileIndex + " of " + lasTilePaths.Length + "...";
                tileIndexProgress.PercentComplete = (int)(100.0F * fractionComplete);
                tileIndexProgress.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                this.WriteProgress(tileIndexProgress);

                // create tile by reading header and variable length records
                // FileStream with default 4 kB buffer is used here as a compromise. The LAS header is 227-375 bytes and often only a single
                // CRS VLR is present with length 54 + 8 + 2 * 8 = 78 bytes if it's GeoKey record with GeoTIFF horizontal and vertical EPSG
                // tags, meaning only 305-453 bytes need be read. However, if it's an OGC WKT record then it's likely ~5 kB long. Buffer size
                // when reading LAS headers and VLRs is negligible to long compute runs but can influence tile indexing time by a factor of
                // 2-3x if overlarge buffers result in unnecessary prefetching.
                string lasTilePath = lasTilePaths[tileIndex];
                using FileStream stream = new(lasTilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024);
                using LasReader headerVlrReader = new(stream);
                lasTiles.Add(new(lasTilePath, headerVlrReader));
            }

            tileIndexProgress.StatusDescription = "Forming grid of point clouds...";
            tileIndexProgress.PercentComplete = 0;
            tileIndexProgress.SecondsRemaining = -1;
            this.WriteProgress(tileIndexProgress);

            LasTileGrid lasGrid = LasTileGrid.Create(lasTiles, this.Snap, requiredEpsg);
            return lasGrid;
        }

        protected VirtualRaster<float> ReadVirtualRaster(string cmdletName, string virtualRasterPath)
        {
            VirtualRaster<float> vrt = new();

            string[] tilePaths = GdalCmdlet.GetExistingTilePaths(virtualRasterPath, Constant.File.GeoTiffExtension);
            if (tilePaths.Length == 1)
            {
                // synchronous read for single tiles
                Raster<float> tile = Raster<float>.Read(tilePaths[0]);
                vrt.Add(tile);
            }
            else
            {
                // multithreaded read for multiple tiles
                // Assume read is from flash (NVMe, SSD) and there's no distinct constraint on the number of read threads or a data
                // locality advantage.
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = Int32.Min(this.MaxThreads, tilePaths.Length)
                };

                string? mostRecentDsmTileName = null;
                Stopwatch stopwatch = Stopwatch.StartNew();
                int tilesLoaded = 0;
                Task loadTilesTask = Task.Run(() =>
                {
                    Parallel.For(0, tilePaths.Length, parallelOptions, (int tileIndex) =>
                    {
                        // find treetops in tile
                        string tilePath = tilePaths[tileIndex];
                        string tileFileName = Path.GetFileName(tilePath);
                        Raster<float> tile = Raster<float>.Read(tilePath);
                        lock (vrt)
                        {
                            vrt.Add(tile);
                            ++tilesLoaded;
                        }

                        mostRecentDsmTileName = tileFileName;
                    });
                });

                TimeSpan progressInterval = TimeSpan.FromSeconds(2.0);
                ProgressRecord progressRecord = new(0, cmdletName, "placeholder");
                while (loadTilesTask.Wait(progressInterval) == false)
                {
                    float fractionComplete = (float)tilesLoaded / (float)tilePaths.Length;
                    progressRecord.StatusDescription = mostRecentDsmTileName != null ? "Loading tile " + mostRecentDsmTileName + "..." : "Loading tiles...";
                    progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                    progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                    this.WriteProgress(progressRecord);
                }

                stopwatch.Stop();
            }

            vrt.BuildGrid(); // unlike LasTileGrid, VirtualRaster<T> doesn't need snapping as it doesn't store tile sizes as doubles
            return vrt;
        }
    }
}
