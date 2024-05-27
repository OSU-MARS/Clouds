using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using MaxRev.Gdal.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class GdalCmdlet : FileCmdlet
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

        protected static string GetRasterTilePath(string directoryPath, string tileName)
        {
            return Path.Combine(directoryPath, tileName + Constant.File.GeoTiffExtension);
        }

        protected VirtualRaster<TTile> ReadVirtualRaster<TTile>(string cmdletName, string virtualRasterPath) where TTile : Raster, IRasterSerializable<TTile>
        {
            VirtualRaster<TTile> vrt = [];

            List<string> tilePaths = GdalCmdlet.GetExistingFilePaths([ virtualRasterPath ], Constant.File.GeoTiffExtension);
            if (tilePaths.Count == 1)
            {
                // synchronous read for single tiles
                TTile tile = TTile.Read(tilePaths[0], readData: true);
                vrt.Add(tile);
            }
            else
            {
                // multithreaded read for multiple tiles
                // Assume read is from flash (NVMe, SSD) and there's no distinct constraint on the number of read threads or a data
                // locality advantage.
                TileRead tileRead = new();
                ParallelTasks tileReadTasks = new(Int32.Min(this.MaxThreads, tilePaths.Count), () =>
                {
                    for (int tileIndex = tileRead.GetNextTileReadIndexThreadSafe(); tileIndex < tilePaths.Count; tileIndex = tileRead.GetNextTileReadIndexThreadSafe())
                    {
                        string tilePath = tilePaths[tileIndex];
                        TTile tile = TTile.Read(tilePath, readData: true);
                        lock (vrt)
                        {
                            vrt.Add(tile);
                            ++tileRead.TilesRead;
                        }
                    }
                });

                TimedProgressRecord progress = new(cmdletName, "placeholder"); // can't pass null or empty statusDescription
                while (tileReadTasks.WaitAll(Constant.DefaultProgressInterval) == false)
                {
                    progress.StatusDescription = "Loaded " + tileRead.TilesRead + " of " + tilePaths.Count + " virtual raster " + (tilePaths.Count == 1 ? "tile..." : "tiles...");
                    progress.Update(tileRead.TilesRead, tilePaths.Count);
                    this.WriteProgress(progress);
                }
            }

            vrt.CreateTileGrid(); // unlike LasTileGrid, VirtualRaster<T> doesn't need snapping as it doesn't store tile sizes as doubles
            return vrt;
        }
    }
}
