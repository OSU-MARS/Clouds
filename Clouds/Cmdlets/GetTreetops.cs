using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata.Ecma335;
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
        [ValidateNotNullOrWhiteSpace]
        public string? Dsm { get; set; }

        [Parameter(HelpMessage = "Band number of DSM height values in surface raster. Default is \"dsm\".")]
        public string? DsmBand { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Band number of DTM height values in terrain raster. Default is null, which accepts any single band raster.")]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Method of treetop detection. ChmRadius = local maxima in CHM filtered by height dependent radius (default), DsmRadius = local maxima in DSM filtered by height dependent radius, DsmRing = ring prominence in DSM.")]
        public TreetopDetectionMethod Method { get; set; }

        [Parameter(HelpMessage = "Minimum height above DTM for a DSM cell to be considered a possible treetop. Default is 1.5 m, which is automatically converted to feet if the DSM CRS is in English units. If any other value is specified it is used without conversion.")]
        [ValidateRange(0.0F, 100.0F)]
        public float MinimumHeight { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "1) path to write treetop candidates to as an XYZ point layer with fields treeID and height or 2,3) path to a directory to write treetop candidate .gpkg tiles to.")]
        [ValidateNotNullOrWhiteSpace]
        public string? Treetops { get; set; }

        public GetTreetops()
        {
            this.Diagnostics = null;
            // this.Dsm is mandatory
            this.DsmBand = "dsm";
            // this.Dtm is mandatory
            this.DtmBand = null;
            // leave this.MaxThreads at default
            this.Method = TreetopDetectionMethod.DsmRadius;
            this.MinimumHeight = Single.NaN;
            // this.Treetops is mandatory
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null) && (this.Treetops != null));

            Stopwatch stopwatch = Stopwatch.StartNew();
            TreetopSearch treetopSearch = TreetopSearch.Create(this.Method, this.DsmBand, this.DtmBand);
            treetopSearch.DiagnosticsPath = this.Diagnostics;

            List<string> dsmTilePaths = GdalCmdlet.GetExistingFilePaths([ this.Dsm ], Constant.File.GeoTiffExtension);
            if ((dsmTilePaths.Count >= 1) && (Directory.Exists(this.Treetops) == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.Treetops), "-" + nameof(this.Treetops) + " must be an existing directory when -" + nameof(this.Dsm) + " indicates multiple files.");
            }

            // load all DSM and DTM tiles
            // TODO: update to current dtim
            string? mostRecentDsmTileName = null;
            int tileLoadsInitiated = -1;
            int tileLoadsCompleted = 0;
            ParallelTasks loadTilesTasks = new(Int32.Min(this.MaxThreads, dsmTilePaths.Count), () =>
            {
                for (int tileIndex = Interlocked.Increment(ref tileLoadsInitiated); tileIndex < dsmTilePaths.Count; tileIndex = Interlocked.Increment(ref tileLoadsInitiated))
                {
                    // find treetops in tile
                    string dsmTilePath = dsmTilePaths[tileIndex];
                    string dsmTileFileName = Path.GetFileName(dsmTilePath);
                    string dtmTilePath = Path.Combine(this.Dtm, dsmTileFileName);
                    string tileName = Tile.GetName(dsmTilePath);
                    treetopSearch.AddTile(dsmTilePath, dtmTilePath);

                    mostRecentDsmTileName = tileName;
                    Interlocked.Increment(ref tileLoadsCompleted);
                }
            }, new());

            TimedProgressRecord progress = new("Get-Treetops", "placeholder"); // can't pass null or empty statusDescription
            while (loadTilesTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = mostRecentDsmTileName != null ? "Loading DSM and DTM tile " + mostRecentDsmTileName + "..." : "Loading DSM and DTM tiles...";
                progress.Update(tileLoadsCompleted, dsmTilePaths.Count);
                this.WriteProgress(progress);
            }

            treetopSearch.BuildGrids();

            // find treetop candidates in all tiles
            mostRecentDsmTileName = null;
            int treetopFindsInitiated = -1;
            int treetopFindsCompleted = 0;

            int maxDsmTileIndex = treetopSearch.Dsm.VirtualRasterSizeInTilesX * treetopSearch.Dsm.VirtualRasterSizeInTilesY;
            int treetopCandidates = 0;
            ParallelTasks findTreetopsTasks = new(loadTilesTasks.Count, () =>
            {
                for (int tileIndex = Interlocked.Increment(ref treetopFindsInitiated); tileIndex < maxDsmTileIndex; tileIndex = Interlocked.Increment(ref treetopFindsInitiated))
                {
                    DigitalSurfaceModel? dsmTile = treetopSearch.Dsm[tileIndex];
                    if (dsmTile == null)
                    {
                        continue;
                    }

                    string dsmTilePath = dsmTile.FilePath;
                    Debug.Assert(String.IsNullOrWhiteSpace(dsmTilePath) == false);

                    string dsmFileName = Path.GetFileName(dsmTilePath);
                    string dsmFileNameWithoutExtension = Path.GetFileNameWithoutExtension(dsmFileName);
                    string treetopTilePath = Path.Combine(this.Treetops, dsmFileNameWithoutExtension + Constant.File.GeoPackageExtension);

                    (int tileIndexX, int tileIndexY) = treetopSearch.Dsm.ToGridIndices(tileIndex);
                    int treetopCandidatesInTile = treetopSearch.FindTreetops(tileIndexX, tileIndexY, treetopTilePath);

                    lock (treetopSearch)
                    {
                        treetopCandidates += treetopCandidatesInTile;
                        ++treetopFindsCompleted;
                    }
                    mostRecentDsmTileName = dsmFileName;
                }
            }, new());

            progress.Stopwatch.Restart();
            while (findTreetopsTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = mostRecentDsmTileName != null ? "Finding treetops in " + mostRecentDsmTileName + "..." : "Finding treetops...";
                progress.Update(treetopFindsCompleted, dsmTilePaths.Count);
                this.WriteProgress(progress);
            }

            stopwatch.Stop();
            string tileOrTiles = dsmTilePaths.Count > 1 ? "tiles" : "tile";
            this.WriteVerbose(dsmTilePaths.Count + " " + tileOrTiles + " and " + treetopCandidates.ToString("n0") + " treetop candidates in " + stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }
    }
}