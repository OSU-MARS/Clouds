using Mars.Clouds.GdalExtensions;
using MaxRev.Gdal.Core;
using OSGeo.GDAL;
using System;
using System.IO;
using System.Management.Automation;
using System.Numerics;

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
            this.MaxThreads = Environment.ProcessorCount / 2;
        }

        protected static (string? directoryPath, string? searchPattern) ExtractTileDirectoryPathAndSearchPattern(string cmdletTileParameter, string defaultSearchPattern)
        {
            string? directoryPath = null;
            string? searchPattern = null;
            bool expandDsmWildcards = cmdletTileParameter.Contains('*', StringComparison.Ordinal) || cmdletTileParameter.Contains('?', StringComparison.Ordinal);
            if (expandDsmWildcards)
            {
                directoryPath = Path.GetDirectoryName(cmdletTileParameter);
                searchPattern = Path.GetFileName(cmdletTileParameter);
            }
            else
            {
                FileAttributes dsmPathAttributes = File.GetAttributes(cmdletTileParameter);
                if (dsmPathAttributes.HasFlag(FileAttributes.Directory))
                {
                    directoryPath = cmdletTileParameter;
                    searchPattern = defaultSearchPattern;
                }
            }

            return (directoryPath,  searchPattern);
        }
    }
}
