using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Vrt")]
    public class GetVrt : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of directory paths, including wildcards, to search for virtual raster tiles. Each directory is assumed to contain a distinct set of tiles.")]
        [ValidateNotNullOrEmpty]
        public List<string> TilePaths { get; set; }

        [Parameter(HelpMessage = "List of bands to include in the virtual raster. All available bands are included if not specified.")]
        public List<string> Bands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to output .vrt file.")]
        public string Vrt { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 16 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        [Parameter(HelpMessage = "Asymptotic fraction of tiles to load for computing approximate band statistics. This is quantized to every nth tile in the raster, declining as 1/n, with a default asymptote of every fourth tile (fraction = 0.25). Bands in the first tile are always sampled for fractions greater than zero.")]
        [ValidateRange(0.0F, 1.0F)]
        public float SamplingFraction { get; set; }

        [Parameter(HelpMessage = "Frequency at which the status of virtual raster loading and .vrt generation is updated. Default is one second.")]
        public TimeSpan ProgressInterval { get; set; }

        public GetVrt()
        {
            this.Bands = [];
            this.EnumerationOptions = new()
            {
                BufferSize = 16 * 1024,
                IgnoreInaccessible = true
            };
            this.ProgressInterval = TimeSpan.FromSeconds(1.0);
            this.SamplingFraction = 0.25F;
            this.TilePaths = [];
            this.Vrt = String.Empty;
        }

        private (VirtualRaster<Raster>[] vrts, List<string>[] bandsByVrtIndex) AssembleVrts()
        {
            Debug.Assert(this.TilePaths.Count > 0);

            List<string>[] bandsByVrtIndex = new List<string>[this.TilePaths.Count];
            VirtualRaster<Raster>[] vrts = new VirtualRaster<Raster>[this.TilePaths.Count];

            int readThreads = Int32.Min(this.TilePaths.Count, this.MaxThreads);
            int tilesRead = 0;
            int vrtReadIndex = -1;
            int vrtsRead = 0;
            ParallelTasks readVrts = new(readThreads, () =>
            {
                for (int vrtIndex = Interlocked.Increment(ref vrtReadIndex); vrtIndex < vrts.Length; vrtIndex = Interlocked.Increment(ref vrtReadIndex))
                {
                    VirtualRaster<Raster> vrt = this.LoadVrt(this.TilePaths[vrtIndex]);
                    vrts[vrtIndex] = vrt;

                    if (this.Bands.Count > 0)
                    {
                        List<string> bandsFromVrt = [];
                        for (int bandIndex = 0; bandIndex < this.Bands.Count; ++bandIndex)
                        {
                            string bandName = this.Bands[bandIndex];
                            if (vrt.BandNames.Contains(bandName, StringComparer.Ordinal))
                            {
                                bandsFromVrt.Add(bandName);
                            }
                        }
                        bandsByVrtIndex[vrtIndex] = bandsFromVrt;
                    }
                    else
                    {
                        bandsByVrtIndex[vrtIndex] = vrt.BandNames;
                    }

                    lock (vrts)
                    {
                        tilesRead += vrt.TileCount;
                        ++vrtsRead;
                    }
                }
            });

            ProgressRecord progress = new(0, "Get-Vrt", "0 tiles read from 0 of " + vrts.Length + " directories...");
            this.WriteProgress(progress);
            while (readVrts.WaitAll(this.ProgressInterval) == false)
            {
                progress.StatusDescription = tilesRead + " tiles read from " + vrtsRead + " of " + vrts.Length + " directories...";
                progress.PercentComplete = (int)(100.0F * (float)vrtsRead / (float)vrts.Length);
                this.WriteProgress(progress);
            }

            VirtualRaster<Raster>? previousVrt = null;
            for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
            {
                VirtualRaster<Raster> vrt = this.LoadVrt(this.TilePaths[vrtIndex]);
                if (previousVrt != null)
                {
                    // for now, require that tile sets be exactly matched
                    if (SpatialReferenceExtensions.IsSameCrs(vrt.Crs, previousVrt.Crs) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[vrtIndex - 1] + "' is in '" + previousVrt.Crs.GetName() + "' while '" + this.TilePaths[vrtIndex] + "' is in '" + vrt.Crs.GetName() + "'.");
                    }
                    if (vrt.IsSameExtentAndSpatialResolution(previousVrt) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[vrtIndex - 1] + "' and '" + this.TilePaths[vrtIndex] + "' differ in spatial extent or resolution. Sizes are " + previousVrt.VirtualRasterSizeInTilesX + " by " + previousVrt.VirtualRasterSizeInTilesY + " and " + vrt.VirtualRasterSizeInTilesX + " by " + vrt.VirtualRasterSizeInTilesY + " tiles with tiles being " + previousVrt.TileCellSizeX + " by " + previousVrt.TileSizeInCellsY + " and " + vrt.TileSizeInCellsX + " by " + vrt.TileSizeInCellsY + " cells, respectively.");
                    }
                }
            }

            return (vrts, bandsByVrtIndex);
        }

        // could be merged into AssembleVrts()
        private (VirtualRaster<Raster> firstVrt, int firstVrtIndex) CheckBands(VirtualRaster<Raster>[] vrts, List<string>[] bandsByVrtIndex)
        {
            if (this.Bands.Count > 0)
            {
                int bandsFound = 0;
                HashSet<string> uniqueBandsFound = [];
                for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
                {
                    List<string> vrtBands = bandsByVrtIndex[vrtIndex];
                    bandsFound += vrtBands.Count;

                    for (int bandIndex = 0; bandIndex < vrtBands.Count; ++bandIndex)
                    {
                        uniqueBandsFound.Add(vrtBands[bandIndex]);
                    }
                }

                if (uniqueBandsFound.Count != this.Bands.Count)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Bands), "-" + nameof(this.Bands) + " specifies " + this.Bands.Count + " bands but " + bandsFound + " matching bands were found with " + uniqueBandsFound.Count + " unique names. Are the entries in -" + nameof(this.Bands) + " unique?");
                }
            }

            for (int vrtIndex = 0; vrtIndex < vrts.Length; ++vrtIndex)
            {
                if (bandsByVrtIndex[vrtIndex].Count > 0)
                {
                    return (vrts[vrtIndex], vrtIndex);
                }
            }

            throw new ParameterOutOfRangeException(nameof(this.Bands), "Either the virtual raster tiles specified by -" + nameof(this.TilePaths) + " contain no bands or -" + nameof(this.Bands) + " specifies none of the bands present in the tiles.");
        }

        private List<RasterBandStatistics>?[] MaybeEstimateBandStatistics(VirtualRaster<Raster>[] vrts, List<string>[] bandsByVrtIndex, int firstVrtIndex)
        {
            List<RasterBandStatistics>?[] statisticsByVrtIndex = new List<RasterBandStatistics>?[vrts.Length];
            if (this.SamplingFraction <= 0.0F)
            {
                return statisticsByVrtIndex;
            }

            int bandsToEstimate = 0;
            int vrtsWithBands = vrts.Length - firstVrtIndex;
            for (int vrtCountingIndex = 0; vrtCountingIndex < vrts.Length; ++vrtCountingIndex)
            {
                int bandsForVrt = bandsByVrtIndex[vrtCountingIndex].Count;
                if (bandsForVrt == 0)
                {
                    --vrtsWithBands;
                }
                else
                {
                    bandsToEstimate += bandsForVrt;
                }
            }
            Debug.Assert(bandsToEstimate > 0); // .vrt would be empty; this should be caught earlier

            int statisticsThreads = Int32.Min(vrts.Length - firstVrtIndex, this.MaxThreads);
            int vrtEstimationIndex = firstVrtIndex - 1; // since Interlocked.Increment() is called
            int bandsCompleted = 0;
            ParallelTasks estimateStatistics = new(statisticsThreads, () =>
            {
                for (int vrtIndex = Interlocked.Increment(ref vrtEstimationIndex); vrtIndex < vrts.Length; vrtIndex = Interlocked.Increment(ref vrtEstimationIndex))
                {
                    VirtualRaster<Raster> vrt = vrts[vrtIndex];
                    Debug.Assert(vrt.TileCount > 0); // should be checked earlier
                    float tileCountAdjustedSamplingFraction = Single.Max(1.0F / vrt.TileCount, this.SamplingFraction);

                    List<string> bandNames = bandsByVrtIndex[vrtIndex];
                    List<RasterBandStatistics> statisticsForVrt = new(bandNames.Count);
                    for (int bandIndex = 0; bandIndex < bandNames.Count; ++bandIndex)
                    {
                        string bandName = bandNames[bandIndex];
                        RasterBandStatistics bandStatistics = vrt.SampleBandStatistics(bandName, tileCountAdjustedSamplingFraction);
                        statisticsForVrt.Add(bandStatistics);
                        Interlocked.Increment(ref bandsCompleted);
                    }

                    statisticsByVrtIndex[vrtIndex] = statisticsForVrt;
                }
            });

            ProgressRecord progress = new(0, "Get-Vrt", "Estimated statistics for " + bandsCompleted + " of " + bandsToEstimate + " bands in " + vrtsWithBands + (vrtsWithBands == 1 ? " virtual raster..." : " virtual rasters..."));
            this.WriteProgress(progress);
            while (estimateStatistics.WaitAll(this.ProgressInterval) == false)
            {
                progress.StatusDescription = "Estimated statistics for " + bandsCompleted + " of " + bandsToEstimate + " bands in " + vrtsWithBands + (vrtsWithBands == 1 ? " virtual raster..." : " virtual rasters...");
                progress.PercentComplete = (int)(100.0F * (float)bandsCompleted / (float)bandsToEstimate);
            }

            return statisticsByVrtIndex;
        }

        protected override void ProcessRecord()
        {
            string? vrtDatasetDirectory = Path.GetDirectoryName(this.Vrt);
            if (vrtDatasetDirectory == null)
            {
                throw new ParameterOutOfRangeException(nameof(this.Vrt), "-" + nameof(this.Vrt) + " does not contain a directory path.");
            }
            if ((this.SamplingFraction > 0.0F) && (Fma.IsSupported == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.SamplingFraction), "Estimation of band statistics requires at least AVX2 and, potentially, FMA instructions. -" + nameof(this.SamplingFraction) + " = 0.0F can be used to disable statistics and circumvent this restriction on older processors.");
            }

            // read tile metadata, checking inputs for spatial and band data type consistency
            (VirtualRaster<Raster>[] vrts, List<string>[] bandsByVrtIndex) = this.AssembleVrts();
            (VirtualRaster<Raster> firstVrt, int firstVrtIndex) = this.CheckBands(vrts, bandsByVrtIndex);

            // get band statistics
            List<RasterBandStatistics>?[] bandStatistics = this.MaybeEstimateBandStatistics(vrts, bandsByVrtIndex, firstVrtIndex);

            // create and write .vrt
            // Incremental progress is not shown for dataset generation as time spent is negligible at 2000+ tiles. Runtime is dominated
            // by getting tile metadata through GDAL and estimating band statistics.
            ProgressRecord progress = new(0, "Get-Vrt", "Creating and writing .vrt...");
            this.WriteProgress(progress);

            VrtDataset vrtDataset = firstVrt.CreateDataset(vrtDatasetDirectory, bandsByVrtIndex[firstVrtIndex], bandStatistics[firstVrtIndex]);
            for (int vrtIndex = firstVrtIndex + 1; vrtIndex < vrts.Length; ++vrtIndex)
            {
                vrtDataset.AppendBands(vrtDatasetDirectory, vrts[vrtIndex], bandsByVrtIndex[vrtIndex], bandStatistics[vrtIndex]);
            }

            vrtDataset.WriteXml(this.Vrt);
            base.ProcessRecord();
        }

        private VirtualRaster<Raster> LoadVrt(string vrtPath)
        {
            bool vrtPathIsDirectory = Directory.Exists(vrtPath);
            string? vrtDirectoryPath = vrtPathIsDirectory ? vrtPath : Path.GetDirectoryName(vrtPath);
            if (vrtDirectoryPath == null)
            {
                throw new ArgumentOutOfRangeException(nameof(vrtPath), "Virtual raster path '" + vrtPath + "' does not contain a directory.");
            }
            if (Directory.Exists(vrtDirectoryPath) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(vrtPath), "Directory indicated by virtual raster path '" + vrtPath + "' does not exist.");
            }

            string? vrtSearchPattern = vrtPathIsDirectory ? "*" + Constant.File.GeoTiffExtension : Path.GetFileName(vrtPath);
            IEnumerable<string> tilePaths = vrtSearchPattern != null ? Directory.EnumerateFiles(vrtDirectoryPath, vrtSearchPattern) : Directory.EnumerateFiles(vrtDirectoryPath);
            VirtualRaster<Raster> vrt = [];
            foreach (string tilePath in tilePaths)
            {
                using Dataset tileDataset = Gdal.Open(tilePath, Access.GA_ReadOnly);
                Raster tile = Raster.Read(tileDataset, readData: false);
                vrt.Add(tile);
            }

            if (vrt.TileCount < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.TilePaths), "-" + nameof(this.TilePaths) + " does not specify any virtual raster tiles.");
            }

            vrt.CreateTileGrid();
            return vrt;
        }
    }
}
