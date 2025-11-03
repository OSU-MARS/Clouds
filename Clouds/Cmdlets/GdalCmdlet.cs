using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using MaxRev.Gdal.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class GdalCmdlet : FileCmdlet
    {
        [Parameter(HelpMessage = "Maximum number of threads to use when processing tiles or other data in parallel. Default is cmdlet specific but is most commonly the number of physical cores.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int DataThreads { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use when reading file metadata in parallel. Default is potentially cmdlet specific but is usually the number of threads supported by the processor.")]
        [ValidateRange(1, Constant.DefaultMaximumThreads)] // arbitrary upper bound
        public int MetadataThreads { get; set; }

        static GdalCmdlet()
        {
            // Gdal.SetConfigOption("GTIFF_DIRECT_IO", "YES"); // bypass GDAL tile cache: slower than using the cache
            GdalBase.ConfigureAll();
        }

        protected GdalCmdlet()
        {
            this.DataThreads = HardwareCapabilities.Current.PhysicalCores;
            this.MetadataThreads = Environment.ProcessorCount; // actual supported thread count
        }

        protected static int EstimateGeopackageSqlBackgroundThreads()
        {
            return 1; // GDAL seems to always use only a single SQL thread
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

        protected static string GetRasterTilePath(string directoryPath, string tileName)
        {
            return Path.Combine(directoryPath, tileName + Constant.File.GeoTiffExtension);
        }

        protected VirtualRaster<TTile> ReadVirtualRasterMetadata<TTile>(string cmdletName, string virtualRasterPath, Func<string, TTile> createTileFromMetadata, CancellationTokenSource cancellationTokenSource) where TTile : Raster
        {
            Debug.Assert(this.MetadataThreads > 0);

            VirtualRaster<TTile> vrt = new();

            List<string> tilePaths = this.GetExistingFilePaths([ virtualRasterPath ], Constant.File.GeoTiffExtension);
            if (tilePaths.Count == 1)
            {
                // synchronous read for single tile
                TTile tile = createTileFromMetadata(tilePaths[0]);
                vrt.Add(tile);
            }
            else
            {
                // multithreaded read for multiple tiles
                // Assume read is from flash (NVMe, SSD) and there's no particular constraint on the number of read threads or a data
                // locality advantage.
                TileRead tileRead = new();
                ParallelTasks tileReadTasks = new(Int32.Min(this.MetadataThreads, tilePaths.Count), () =>
                {
                    for (int tileIndex = tileRead.GetNextTileReadIndexThreadSafe(); tileIndex < tilePaths.Count; tileIndex = tileRead.GetNextTileReadIndexThreadSafe())
                    {
                        string tilePath = tilePaths[tileIndex];
                        TTile tile = createTileFromMetadata(tilePath);
                        lock (vrt)
                        {
                            vrt.Add(tile);
                            ++tileRead.TilesRead;
                        }
                    }
                }, cancellationTokenSource);

                TimedProgressRecord progress = new(cmdletName, "placeholder"); // can't pass null or empty statusDescription
                while (tileReadTasks.WaitAll(Constant.DefaultProgressInterval) == false)
                {
                    progress.StatusDescription = $"Read metadata of {tileRead.TilesRead} of {tilePaths.Count} virtual raster {(tilePaths.Count == 1 ? "tile (" : "tiles (")}{tileReadTasks.Count}{(tileReadTasks.Count == 1 ? " thread)..." : " threads)...")}";
                    progress.Update(tileRead.TilesRead, tilePaths.Count);
                    this.WriteProgress(progress);
                }
            }

            vrt.CreateTileGrid(); // unlike LasTileGrid, VirtualRaster<T> doesn't need snapping as it doesn't store tile sizes as doubles
            return vrt;
        }

        protected static bool ValidateOrCreateOutputPath(string outputPath, VirtualRaster vrt, string inputParameterName, string outputParameterName)
        {
            bool outputPathIsDirectory = Directory.Exists(outputPath);
            if ((vrt.NonNullTileCount > 1) && (outputPathIsDirectory == false))
            {
                if (File.Exists(outputPath))
                {
                    throw new ParameterOutOfRangeException(outputParameterName, $"-{outputParameterName} must be an existing directory when -{inputParameterName} indicates multiple files.");
                }
                else
                {
                    Directory.CreateDirectory(outputPath);
                    outputPathIsDirectory = true;
                }
            }

            return outputPathIsDirectory;
        }
    }
}
