using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using MaxRev.Gdal.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    public class GdalCmdlet : Cmdlet
    {
        [Parameter(HelpMessage = "Maximum number of threads to use when processing tiles in parallel. Default is half of the procesor's thread count.")]
        public int MaxThreads { get; set; }

        static GdalCmdlet()
        {
            GdalBase.ConfigureAll();
        }

        protected GdalCmdlet()
        {
            this.MaxThreads = Environment.ProcessorCount / 2; // for now, assume all cores are hyperthreaded
        }

        protected static (string? directoryPath, string? searchPattern) ExtractTileDirectoryPathAndSearchPattern(string cmdletTilePath, string defaultSearchPattern)
        {
            string? directoryPath = null;
            string? searchPattern = null;
            bool expandDsmWildcards = cmdletTilePath.Contains('*', StringComparison.Ordinal) || cmdletTilePath.Contains('?', StringComparison.Ordinal);
            if (expandDsmWildcards)
            {
                directoryPath = Path.GetDirectoryName(cmdletTilePath);
                searchPattern = Path.GetFileName(cmdletTilePath);
            }
            else
            {
                if (Directory.Exists(cmdletTilePath))
                {
                    directoryPath = cmdletTilePath;
                    searchPattern = defaultSearchPattern;
                }
            }

            return (directoryPath, searchPattern);
        }

        protected static string[] GetExistingTilePaths(string? tileSearchPath, string defaultTileFileExtension)
        {
            ArgumentOutOfRangeException.ThrowIfNullOrWhiteSpace(tileSearchPath, nameof(tileSearchPath));

            string? directoryPath;
            string? fileSearchPattern;
            if (tileSearchPath.Contains('*', StringComparison.Ordinal) || tileSearchPath.Contains('?', StringComparison.Ordinal))
            {
                // presence of wildcards indicates a set of files in some directory
                directoryPath = Path.GetDirectoryName(tileSearchPath);
                if (directoryPath == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(tileSearchPath), "Wildcarded tile search path '" + tileSearchPath + "' doesn't contain a directory path.");
                }
                fileSearchPattern = Path.GetFileName(tileSearchPath);
                if (fileSearchPattern == null)
                {
                    fileSearchPattern = "*" + defaultTileFileExtension;
                }
            }
            else if (Directory.Exists(tileSearchPath))
            {
                // if path indicates an existing directory, search it with the default extension
                directoryPath = tileSearchPath;
                fileSearchPattern = "*" + defaultTileFileExtension;
            }
            else
            {
                if (File.Exists(tileSearchPath) == false)
                {
                    throw new ArgumentOutOfRangeException(nameof(tileSearchPath), "Can't load tiles. Path '" + tileSearchPath + "' is not an existing file, existing directory, or wildcarded search path.");
                }
                return [ tileSearchPath ];
            }

            string[] tilePaths = Directory.GetFiles(directoryPath, fileSearchPattern);
            if (tilePaths.Length == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileSearchPath), "Can't load tiles. No files matched '" + Path.Combine(directoryPath, fileSearchPattern) + "'.");
            }
            return tilePaths;
        }

        protected VirtualRaster<float> ReadVirtualRaster(string cmdletName, string virtualRasterPath)
        {
            VirtualRaster<float> vrt = new();

            string[] tilePaths = GdalCmdlet.GetExistingTilePaths(virtualRasterPath, Constant.File.GeoTiffExtension);
            if (tilePaths.Length == 1)
            {
                // synchronous read for single tiles
                string tilePath = tilePaths[0];
                string tileName = Tile.GetName(tilePath);
                Raster<float> tile = Raster<float>.Read(tilePath);
                vrt.Add(tileName, tile);
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
                        string tileName = Tile.GetName(tilePath);
                        Raster<float> tile = Raster<float>.Read(tilePath);
                        lock (vrt)
                        {
                            vrt.Add(tileName, tile);
                            ++tilesLoaded;
                        }

                        mostRecentDsmTileName = tileName;
                    });
                });

                ProgressRecord progressRecord = new(0, cmdletName, "placeholder");
                while (loadTilesTask.Wait(Constant.DefaultProgressInterval) == false)
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
