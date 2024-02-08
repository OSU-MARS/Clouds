using MaxRev.Gdal.Core;
using System;
using System.IO;
using System.Management.Automation;

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
    }
}
