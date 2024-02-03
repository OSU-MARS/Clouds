using Mars.Clouds.Segmentation;
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
    [Cmdlet(VerbsCommon.Get, "Treetops")]
    public class GetTreetops : GdalCmdlet
    {
        [Parameter(HelpMessage = "Path where diagnostic files will be written. No diagnostics will be output if null (default), empty, or blank.")]
        public string? Diagnostics { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each DSM must be a single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Method of treetop detection. ChmRadius = local maxima in CHM filtered by height dependent radius (default), DsmRadius = local maxima in DSM filtered by height dependent radius, DsmRing = ring prominence in DSM.")]
        public TreetopDetectionMethod Method { get; set; }

        [Parameter(HelpMessage = "Minimum height above DTM for a DSM cell to be considered a possible treetop. Default is 1.5 m, which is automatically converted to feet if the DSM CRS is in English units. If any other value is specified it is used without conversion.")]
        [ValidateRange(0.0F, 100.0F)]
        public float MinimumHeight { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "1) path to write treetop candidates to as an XYZ point layer with fields treeID and height or 2,3) path to a directory to write treetop candidate .gpkg tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Treetops { get; set; }

        public GetTreetops()
        {
            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.Diagnostics = null;
            this.Method = TreetopDetectionMethod.DsmRadius;
            this.MinimumHeight = Single.NaN;
            // this.Treetops is mandatory
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null) && (this.Treetops != null));
            (string? dsmDirectoryPath, string? dsmTileSearchPattern) = GdalCmdlet.ExtractTileDirectoryPathAndSearchPattern(this.Dsm, "*" + Constant.File.GeoTiffExtension);

            Stopwatch stopwatch = Stopwatch.StartNew();
            TreetopSearch treetopSearch = TreetopSearch.Create(this.Method);
            treetopSearch.DiagnosticsPath = this.Diagnostics;

            List<string> dsmTilePaths;
            int treetopCandidates = 0;
            if (dsmDirectoryPath == null)
            {
                // single tile case
                dsmTilePaths = [ this.Dsm ];
                treetopSearch.AddTile(this.Dsm, this.Dtm);
                treetopSearch.BuildGrids();
                treetopCandidates += treetopSearch.FindTreetops(0, this.Treetops);
            }
            else
            {
                // multi-tile case
                Debug.Assert(dsmTileSearchPattern != null);
                dsmTilePaths = Directory.EnumerateFiles(dsmDirectoryPath, dsmTileSearchPattern, SearchOption.TopDirectoryOnly).ToList();
                if (dsmTilePaths.Count < 1)
                {
                    // nothing to do
                    this.WriteVerbose("Exiting without performing any processing. Path '" + Path.Combine(dsmDirectoryPath, dsmTileSearchPattern) + "' does not match any DSM tiles.");
                    return;
                }

                if (Directory.Exists(this.Dtm) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Dtm), "-" + nameof(this.Dtm) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }
                if (Directory.Exists(this.Treetops) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Treetops), "-" + nameof(this.Treetops) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }

                // load all tiles
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = this.MaxThreads
                };

                string? mostRecentDsmTileName = null;
                int tilesLoaded = 0;
                Task loadTilesTask = Task.Run(() =>
                {
                    Parallel.For(0, dsmTilePaths.Count, parallelOptions, (int tileIndex) =>
                    {
                        // find treetops in tile
                        string dsmTilePath = dsmTilePaths[tileIndex];
                        string dsmTileFileName = Path.GetFileName(dsmTilePath);
                        string dtmTilePath = Path.Combine(this.Dtm, dsmTileFileName);
                        treetopSearch.AddTile(dsmTilePath, dtmTilePath);

                        mostRecentDsmTileName = dsmTileFileName;
                        Interlocked.Increment(ref tilesLoaded);
                    });
                });

                TimeSpan progressInterval = TimeSpan.FromSeconds(2.0);
                ProgressRecord progressRecord = new(0, "Get-Treetops", "placeholder");
                while (loadTilesTask.Wait(progressInterval) == false)
                {
                    float fractionComplete = (float)tilesLoaded / (float)dsmTilePaths.Count;
                    progressRecord.StatusDescription = mostRecentDsmTileName != null ? "Loading DSM and DTM tile " + mostRecentDsmTileName + "..." : "Loading DSM and DTM tiles...";
                    progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                    progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                    this.WriteProgress(progressRecord);
                }

                treetopSearch.BuildGrids();

                // find treetop candidates in all tiles
                mostRecentDsmTileName = null;
                int tilesCompleted = 0;
                Task findTreetopsTask = Task.Run(() =>
                {
                    Parallel.For(0, treetopSearch.DsmTiles.TileCount, parallelOptions, (int tileIndex) =>
                    {
                        string dsmTilePath = treetopSearch.DsmTiles[tileIndex].FilePath;
                        Debug.Assert(String.IsNullOrWhiteSpace(dsmTilePath) == false);

                        string dsmFileName = Path.GetFileName(dsmTilePath);
                        string dsmFileNameWithoutExtension = Path.GetFileNameWithoutExtension(dsmFileName);
                        string treetopTilePath = Path.Combine(this.Treetops, dsmFileNameWithoutExtension + Constant.File.GeoPackageExtension);
                        mostRecentDsmTileName = dsmFileName;

                        int treetopCandidatesInTile = treetopSearch.FindTreetops(tileIndex, treetopTilePath);
                        Interlocked.Add(ref treetopCandidates, treetopCandidatesInTile);
                        Interlocked.Increment(ref tilesCompleted);
                    });
                });

                while (findTreetopsTask.Wait(progressInterval) == false)
                {
                    float fractionComplete = (float)tilesCompleted / (float)dsmTilePaths.Count;
                    progressRecord.StatusDescription = mostRecentDsmTileName != null ? "Finding treetops in " + mostRecentDsmTileName + "..." : "Finding treetops...";
                    progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                    progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                    this.WriteProgress(progressRecord);
                }
            }

            stopwatch.Stop();
            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours >= 1.0 ? "hh\\:mm\\:ss" : "mm\\:ss";
            string tileOrTiles = dsmTilePaths.Count > 1 ? "tiles" : "tile";
            this.WriteVerbose(dsmTilePaths.Count + " " + tileOrTiles + " and " + treetopCandidates.ToString("n0") + " treetop candidates in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
        }
    }
}