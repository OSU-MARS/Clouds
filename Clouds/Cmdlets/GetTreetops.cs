using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Segmentation;
using OSGeo.OGR;
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
        private const float DefaultMinimumHeight = 1.5F; // m
        private static readonly List<Ring> Rings;

        private readonly VirtualRaster<float> dsmTiles;
        private readonly VirtualRaster<float> dtmTiles;

        [Parameter(HelpMessage = "Path where diagnostic files will be written. No diagnostics will be output if null (default), empty, or blank.")]
        public string? Diagnostics { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each DSM must be a single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use when processing tiles in parallel. Default is half of the procesor's thread count.")]
        public int MaxThreads { get; set; }

        [Parameter(HelpMessage = "Method of treetop detection. ChmRadius = local maxima in CHM filtered by height dependent radius (default), DsmRadius = local maxima in DSM filtered by height dependent radius, DsmRing = ring prominence in DSM.")]
        public TreetopDetectionMethod Method { get; set; }

        [Parameter(HelpMessage = "Minimum height above DTM for a DSM cell to be considered a possible treetop. Default is 1.5 m, which is automatically converted to feet if the DSM CRS is in English units. If any other value is specified it is used without conversion.")]
        [ValidateRange(0.0F, 100.0F)]
        public float MinimumHeight { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "1) path to write treetop candidates to as an XYZ point layer with fields treeID and height or 2,3) path to a directory to write treetop candidate .gpkg tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Treetops { get; set; }

        static GetTreetops()
        {
            GetTreetops.Rings = new()
            {
                new Ring(new int[] { -1, -1, -1, 0, 0, 1, 1, 1 }, new int[] { -1, 0, 1, -1, 1, -1, 0, 1 }), // 1
                new Ring(new int[] { -2, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 2 }, new int[] { -1, 0, 1, -2, 2, -2, 2, -2, 2, -1, 0, 1 }), // 2
                new Ring(new int[] { -3, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 3 }, new int[] { -1, 0, 1, -2, 2, -3, 3, -3, 3, -3, 3, -2, 2, -1, 0, 1 }), // 3
                new Ring(new int[] { -4, -4, -4, -4, -4, -3, -3, -3, -3, -2, -2, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4 }, new int[] { -2, -1, 0, 1, 2, -3, -2, 2, 3, -4, -3, 3, 4, -4, 4, -4, 4, -4, 4, -4, -3, 3, 4, -3, -2, 2, 3, -2, -1, 0, 1, 2 }), // 4
                new Ring(new int[] { -5, -5, -5, -5, -5, -4, -4, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 5, 5, 5 }, new int[] { -2, -1, 0, 1, 2, -3, 3, -4, 4, -5, 5, -5, 5, -5, 5, -5, 5, -5, 5, -4, 4, -3, 3, -2, -1, 0, 1, 2 }), // 5
                new Ring(new int[] { -6, -6, -6, -6, -6, -5, -5, -5, -5, -4, -4, -4, -4, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 6, 6, 6 }, new int[] { -2, -1, 0, 1, 2, -4, -3, 3, 4, -5, -4, 4, 5, -5, 5, -6, 6, -6, 6, -6, 6, -6, 6, -6, 6, -5, 5, -5, -4, 4, 5, -4, -3, 3, 4, -2, -1, 0, 1, 2 }), // 6
                new Ring(new int[] { -7, -7, -7, -7, -7, -6, -6, -6, -6, -5, -5, -4, -4, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 7 }, new int[] { -2, -1, 0, 1, 2, -4, -3, 3, 4, -5, 5, -6, 6, -6, 6, -7, 7, -7, 7, -7, 7, -7, 7, -7, 7, -6, 6, -6, 6, -5, 5, -4, -3, 3, 4, -2, -1, 0, 1, 2 }), // 7
                new Ring(new int[] { -8, -8, -8, -8, -8, -7, -7, -7, -7, -6, -6, -6, -6, -5, -5, -4, -4, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 8 }, new int[] { -2, -1, 0, 1, 2, -4, -3, 3, 4, -6, -5, 5, 6, -6, 6, -7, 7, -7, 7, -8, 8, -8, 8, -8, 8, -8, 8, -8, 8, -7, 7, -7, 7, -6, 6, -6, -5, 5, 6, -4, -3, 3, 4, -2, -1, 0, 1, 2 }), // 8
                new Ring(new int[] { -9, -9, -9, -9, -9, -9, -9, -8, -8, -8, -8, -8, -8, -7, -7, -7, -7, -6, -6, -5, -5, -5, -5, -4, -4, -3, -3, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 5, 5, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9 }, new int[] { -3, -2, -1, 0, 1, 2, 3, -5, -4, -3, 3, 4, 5, -6, -5, 5, 6, -7, 7, -8, -7, 7, 8, -8, 8, -9, -8, 8, 9, -9, 9, -9, 9, -9, 9, -9, 9, -9, 9, -9, -8, 8, 9, -8, 8, -8, -7, 7, 8, -7, 7, -6, -5, 5, 6, -5, -4, -3, 3, 4, 5, -3, -2, -1, 0, 1, 2, 3 }), // 9
                new Ring(new int[] { -10, -10, -10, -10, -10, -10, -10, -9, -9, -9, -9, -8, -8, -7, -7, -6, -6, -5, -5, -4, -4, -3, -3, -2, -2, -1, -1, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10 }, new int[] { -3, -2, -1, 0, 1, 2, 3, -5, -4, 4, 5, -6, 6, -7, 7, -8, 8, -9, 9, -9, 9, -10, 10, -10, 10, -10, 10, -10, 10, -10, 10, -10, 10, -10, 10, -9, 9, -9, 9, -8, 8, -7, 7, -6, 6, -5, -4, 4, 5, -3, -2, -1, 0, 1, 2, 3 }), // 10
            };
        }

        public GetTreetops()
        {
            this.dsmTiles = new();
            this.dtmTiles = new();

            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.Diagnostics = null;
            this.MaxThreads = Environment.ProcessorCount / 2;
            this.Method = TreetopDetectionMethod.DsmRadius;
            this.MinimumHeight = GetTreetops.DefaultMinimumHeight;
            // this.Treetops is mandatory
        }

        private static void AddToLayer(TreetopLayer treetopLayer, TileSearchState tileSearch, int treeID, double rowIndexFractional, double columnIndexFractional, float groundElevationAtBaseOfTree, float treetopElevation)
        {
            (double centroidX, double centroidY) = tileSearch.Dsm.Transform.ToProjectedCoordinate(columnIndexFractional, rowIndexFractional);
            float treeHeightInCrsUnits = treetopElevation - groundElevationAtBaseOfTree;
            Debug.Assert((groundElevationAtBaseOfTree > -200.0F) && (groundElevationAtBaseOfTree < 30000.0F) && (treeHeightInCrsUnits < 400.0F));
            treetopLayer.Add(treeID, centroidX, centroidY, groundElevationAtBaseOfTree, treeHeightInCrsUnits);
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null) && (this.Treetops != null));

            string? dsmDirectoryPath = null;
            string? dsmTileSearchPattern = null;
            bool expandDsmWildcards = this.Dsm.Contains('*', StringComparison.Ordinal) || this.Dsm.Contains('?', StringComparison.Ordinal);
            if (expandDsmWildcards)
            {
                dsmDirectoryPath = Path.GetDirectoryName(this.Dsm);
                dsmTileSearchPattern = Path.GetFileName(this.Dsm);
            }
            else
            {
                FileAttributes dsmPathAttributes = File.GetAttributes(this.Dsm);
                if (dsmPathAttributes.HasFlag(FileAttributes.Directory))
                {
                    dsmDirectoryPath = this.Dsm;
                    dsmTileSearchPattern = "*.tif";
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<string> dsmTilePaths;
            int treetopCandidates = 0;
            if (dsmDirectoryPath == null)
            {
                // single tile case
                dsmTilePaths = new List<string>() { this.Dsm };
                this.LoadTile(this.Dsm, this.Dtm);
                this.dsmTiles.BuildGrid();
                this.dtmTiles.BuildGrid();
                treetopCandidates += this.ProcessTile(0, this.Treetops);
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

                FileAttributes dtmPathAttributes = File.GetAttributes(this.Dtm);
                if (dtmPathAttributes.HasFlag(FileAttributes.Directory) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Dtm), nameof(this.Dtm) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }
                FileAttributes treetopPathAttributes = File.GetAttributes(this.Treetops);
                if (treetopPathAttributes.HasFlag(FileAttributes.Directory) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Treetops), nameof(this.Treetops) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }

                // load all tiles
                int loggingThreadID = Environment.CurrentManagedThreadId;
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = this.MaxThreads
                };

                this.dsmTiles.TileCapacity = dsmTilePaths.Count;
                this.dtmTiles.TileCapacity = dsmTilePaths.Capacity;
                int tilesLoaded = 0;
                Parallel.For(0, dsmTilePaths.Count, parallelOptions, (int tileIndex) =>
                {
                    // find treetops in tile
                    string dsmTileName = this.LoadTile(dsmTilePaths, tileIndex);
                    int loadedTileCount = Interlocked.Increment(ref tilesLoaded);

                    // update progress
                    // PowerShell allows writing only from the thread which entered ProcessRecord(). A lightweight solution
                    // to log only from that thread, though doing so reduces the frequency of progress updates and is fragile to
                    // Parallel.For() not using PowerShell's entry thread.
                    if (Environment.CurrentManagedThreadId == loggingThreadID)
                    {
                        double fractionComplete = (double)loadedTileCount / (double)dsmTilePaths.Count;
                        double secondsElapsed = stopwatch.Elapsed.TotalSeconds;
                        int secondsRemaining = (int)Double.Round(secondsElapsed * (1.0 / fractionComplete - 1.0));
                        this.WriteProgress(new ProgressRecord(0, "Get-Treetops", "Loading " + dsmTileName + "...")
                        {
                            PercentComplete = (int)(100.0F * fractionComplete),
                            SecondsRemaining = secondsRemaining
                        });
                    }
                });

                if (SpatialReferenceExtensions.IsSameCrs(this.dsmTiles.Crs, this.dtmTiles.Crs) == false)
                {
                    throw new NotSupportedException("The DSM and DTM are currently required to be in the same CRS. Th eDSM CRS is '" + this.dsmTiles.Crs.GetName() + "' while the DTM CRS is " + this.dtmTiles.Crs.GetName() + ".");
                }
                if (this.dsmTiles.IsSameSpatialResolutionAndExtent(this.dtmTiles) == false)
                {
                    throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must be of the same size (width and height in cells) and have the same cell size (width and height in meters or feet).");
                }

                // index tiles spatially
                this.dsmTiles.BuildGrid();
                this.dtmTiles.BuildGrid();
                if ((this.dsmTiles.OrginX != this.dtmTiles.OrginX) || (this.dsmTiles.OrginY != this.dtmTiles.OrginY))
                {
                    throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same origin. The DSM origin (" + this.dsmTiles.OrginX + ", " + this.dsmTiles.OrginY + ") is offset from the DTM origin (" + this.dtmTiles.OrginX + ", " + this.dtmTiles.OrginY + ").");
                }
                if (this.dsmTiles.IsSameSpatialResolutionAndExtent(this.dsmTiles) == false)
                {
                    throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same extent and cell size.");
                }

                // find treetop candidates in all tiles
                this.WriteProgress(new ProgressRecord(0, "Get-Treetops", "Finding treetops...")
                {
                    // restart progress of UX responsiveness as first treetop detection update takes a few seconds
                    // This avoids the appearance that the end of tile loading's hung.
                    PercentComplete = 0
                });

                int tilesCompleted = 0;
                Parallel.For(0, this.dsmTiles.TileCount, parallelOptions, (int tileIndex) =>
                {
                    string dsmTilePath = this.dsmTiles[tileIndex].FilePath;
                    Debug.Assert(String.IsNullOrWhiteSpace(dsmTilePath) == false);

                    string dsmFileName = Path.GetFileName(dsmTilePath);
                    string dsmFileNameWithoutExtension = Path.GetFileNameWithoutExtension(dsmFileName);
                    string treetopTilePath = Path.Combine(this.Treetops, dsmFileNameWithoutExtension + ".gpkg");
                    int treetopCandidatesInTile = this.ProcessTile(tileIndex, treetopTilePath);
                    Interlocked.Add(ref treetopCandidates, treetopCandidatesInTile);
                    int completedTileCount = Interlocked.Increment(ref tilesCompleted);

                    if (Environment.CurrentManagedThreadId == loggingThreadID)
                    {
                        double fractionComplete = (double)completedTileCount / (double)dsmTilePaths.Count;
                        double secondsElapsed = stopwatch.Elapsed.TotalSeconds;
                        int secondsRemaining = (int)Double.Round(secondsElapsed * (1.0 / fractionComplete - 1.0));
                        this.WriteProgress(new ProgressRecord(0, "Get-Treetops", "Finding trees in " + dsmFileName + "...")
                        {
                            PercentComplete = (int)(100.0F * fractionComplete),
                            SecondsRemaining = secondsRemaining
                        });
                    }
                });
            }
            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours >= 1.0 ? "hh\\:mm\\:ss" : "mm\\:ss";
            string tileOrTiles = dsmTilePaths.Count > 1 ? "tiles" : "tile";
            this.WriteVerbose(dsmTilePaths.Count + " " + tileOrTiles + " and " + treetopCandidates.ToString("n0") + " treetop candidates in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
        }

        private string LoadTile(List<string> dsmTiles, int tileIndex)
        {
            Debug.Assert(String.IsNullOrWhiteSpace(this.Dtm) == false);
            string dsmTilePath = dsmTiles[tileIndex];
            string dsmFileName = Path.GetFileName(dsmTilePath);
            string dtmTilePath = Path.Combine(this.Dtm, dsmFileName);

            this.LoadTile(dsmTilePath, dtmTilePath);
            return dsmFileName;
        }

        private void LoadTile(string dsmFilePath, string dtmFilePath)
        {
            Raster<float> dsm = GdalCmdlet.ReadRasterFloat(dsmFilePath);
            Raster<float> dtm = GdalCmdlet.ReadRasterFloat(dtmFilePath);

            lock (this.dsmTiles)
            {
                this.dsmTiles.Add(dsm);
                this.dtmTiles.Add(dtm);
            }
        }

        private int ProcessTile(int tileIndex, string treetopFilePath)
        {
            TileSearchState tileSearch;
            if (this.Method == TreetopDetectionMethod.DsmRing)
            {
                tileSearch = new RingSearchState(this.dsmTiles.GetNeighborhood8(tileIndex, 0), this.dtmTiles.GetNeighborhood8(tileIndex, 0), String.IsNullOrWhiteSpace(this.Diagnostics) == false)
                {
                    MinimumCandidateHeight = this.MinimumHeight
                };
            }
            else
            {
                tileSearch = new(this.dsmTiles.GetNeighborhood8(tileIndex, 0), this.dtmTiles.GetNeighborhood8(tileIndex, 0))
                {
                    MinimumCandidateHeight = this.MinimumHeight
                };
            }

            // change minimum height from meters to feet if CRS uses English units
            // Assumption here is that xy and z units match, which is not necessarily enforced.
            if ((tileSearch.MinimumCandidateHeight == GetTreetops.DefaultMinimumHeight) && (tileSearch.CrsLinearUnits != 1.0F))
            {
                // possible issue: what if the desired minimum height is exactly 1.5 feet?
                tileSearch.MinimumCandidateHeight /= tileSearch.CrsLinearUnits;
            }

            // find local maxima in DSM
            //using Dataset? treetopDataset = Gdal.Open(this.Treetops, Access.GA_Update);
            //if (treetopDataset == null)
            //{
            //    OSGeo.GDAL.Driver driver = Gdal.IdentifyDriver(this.Treetops, null);
            //    driver.Create();
            //}
            using DataSource? treetopFile = File.Exists(treetopFilePath) ? Ogr.Open(treetopFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(treetopFilePath, null);
            using TreetopLayer treetopLayer = new(treetopFile, tileSearch.Dsm.Crs);
            for (int dsmIndex = 0, dsmRowIndex = 0; dsmRowIndex < tileSearch.Dsm.YSize; ++dsmRowIndex) // y for north up rasters
            {
                for (int dsmColumnIndex = 0; dsmColumnIndex < tileSearch.Dsm.XSize; ++dsmIndex, ++dsmColumnIndex) // x for north up rasters
                {
                    float dsmZ = tileSearch.Dsm.Data.Span[dsmIndex];
                    if (tileSearch.Dsm.IsNoData(dsmZ))
                    {
                        continue;
                    }

                    // read DTM interpolated to DSM resolution to get local height
                    // (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                    // (int dtmRowIndex, int dtmColumnIndex) = dtm.Transform.GetCellIndex(cellX, cellY); 
                    float dtmElevation = tileSearch.Dtm[dsmColumnIndex, dsmRowIndex]; // could use dsmIndex
                    if (tileSearch.Dtm.IsNoData(dtmElevation))
                    {
                        continue;
                    }
                    Debug.Assert(dtmElevation > -200.0F, "DTM elevation of " + dtmElevation + " is unexpectedly low.");

                    // get search radius and area for local maxima in DSM
                    // For now, use logistic quantile regression at p = 0.025 from prior Elliott State Research Forest segmentations.
                    float heightInCrsUnits = dsmZ - dtmElevation;
                    if (heightInCrsUnits < tileSearch.MinimumCandidateHeight)
                    {
                        continue;
                    }

                    bool addTreetop;
                    if (this.Method == TreetopDetectionMethod.DsmRing)
                    {
                        addTreetop = GetTreetops.RingSearch(dsmRowIndex, dsmColumnIndex, dsmZ, dtmElevation, (RingSearchState)tileSearch);
                    }
                    else
                    {
                        addTreetop = this.RadiusSearch(dsmRowIndex, dsmColumnIndex, dsmZ, dtmElevation, tileSearch);
                    }

                    // create point if this cell is a unique local maxima
                    if (addTreetop)
                    {
                        (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenterCoordinate(dsmColumnIndex, dsmRowIndex);
                        treetopLayer.Add(tileSearch.NextTreeID++, cellX, cellY, dtmElevation, heightInCrsUnits);
                    }
                }

                // perf: periodically commit patches which are many rows away and will no longer be added to
                // Could also do an immediate insert above and then upsert here once patches are fully discovered but doing so
                // appears low value.
                if ((dsmRowIndex > tileSearch.EqualHeightPatchCommitInterval) && (dsmRowIndex % tileSearch.EqualHeightPatchCommitInterval == 0))
                {
                    double maxPatchRowIndexToCommit = dsmRowIndex - tileSearch.EqualHeightPatchCommitInterval;
                    int maxPatchIndex = -1;
                    for (int patchIndex = 0; patchIndex < tileSearch.TreetopEqualHeightPatches.Count; ++patchIndex)
                    {
                        SameHeightPatch<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
                        (double centroidRowIndex, double centroidColumnIndex, float centroidElevation) = equalHeightPatch.GetCentroid();
                        if (centroidRowIndex > maxPatchRowIndexToCommit)
                        {
                            break;
                        }

                        GetTreetops.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidRowIndex, centroidColumnIndex, centroidElevation, equalHeightPatch.Height);
                        maxPatchIndex = patchIndex;
                    }

                    if (maxPatchIndex > -1)
                    {
                        tileSearch.TreetopEqualHeightPatches.RemoveRange(0, maxPatchIndex + 1);
                    }
                }
            }

            // create treetop points for equal height patches which haven't already been committed to layer
            for (int patchIndex = 0; patchIndex < tileSearch.TreetopEqualHeightPatches.Count; ++patchIndex)
            {
                SameHeightPatch<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
                (double centroidRowIndex, double centroidColumnIndex, float centroidElevation) = equalHeightPatch.GetCentroid();

                GetTreetops.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidRowIndex, centroidColumnIndex, centroidElevation, equalHeightPatch.Height);
            }

            if ((this.Method == TreetopDetectionMethod.DsmRing) && (String.IsNullOrWhiteSpace(this.Diagnostics) == false))
            {
                string? treetopFileNameWithoutExtension = Path.GetFileNameWithoutExtension(treetopFilePath);
                if (treetopFileNameWithoutExtension == null)
                {
                    throw new NotSupportedException("Treetop file path '" + treetopFilePath + " is not a path to a file.");
                }
                string diagnosticsRasterPath = Path.Combine(this.Diagnostics, treetopFileNameWithoutExtension + ".tif");

                if ((tileSearch is RingSearchState ringSearch) && (ringSearch.RingData != null))
                {
                    ringSearch.RingData.Write(diagnosticsRasterPath);
                }
                else
                {
                    throw new InvalidOperationException("Diagnostics path is set but diagnostics were not computed for ring search.");
                }
            }

            return tileSearch.NextTreeID - 1;
        }

        private bool RadiusSearch(int dsmRowIndex, int dsmColumnIndex, float dsmZ, float dtmElevation, TileSearchState searchState)
        {
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            // float searchRadiusInM = 8.59F / (1.0F + MathF.Exp((58.72F - heightInM) / 19.42F)); // logistic regression at 0.025 quantile against boostrap crown radii estimates
            // float searchRadiusInM = 6.0F / (1.0F + MathF.Exp((49.0F - heightInM) / 18.5F)); // manual retune based on segmentation
            float searchRadiusInM = Single.Min(0.055F * heightInM + 0.4F, 5.0F); // manual retune based on segmentation
            float searchRadiusInCrsUnits = searchRadiusInM / searchState.CrsLinearUnits;

            // check if point is local maxima
            float candidateZ = this.Method == TreetopDetectionMethod.ChmRadius ? heightInCrsUnits : dsmZ;
            bool dsmCellInEqualHeightPatch = false;
            bool higherCellFound = false;
            bool newEqualHeightPatchFound = false;
            int rowSearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), 1);
            for (int searchRowOffset = -rowSearchRadiusInCells; searchRowOffset <= rowSearchRadiusInCells; ++searchRowOffset)
            {
                int searchRowIndex = dsmRowIndex + searchRowOffset;
                // constrain column bounds to circular search based on row offset
                float searchRowOffsetInCrsUnits = searchRowOffset * searchState.DsmCellHeight;
                int maxColumnSearchOffsetInCells = 0;
                if (MathF.Abs(searchRowOffsetInCrsUnits) < searchRadiusInCrsUnits) // avoid NaN from Sqrt()
                {
                    float columnSearchDistanceInCrsUnits = MathF.Sqrt(searchRadiusInCrsUnits * searchRadiusInCrsUnits - searchRowOffsetInCrsUnits * searchRowOffsetInCrsUnits);
                    maxColumnSearchOffsetInCells = (int)(columnSearchDistanceInCrsUnits / searchState.DsmCellWidth + 0.5F);
                }
                if ((maxColumnSearchOffsetInCells < 1) && (Math.Abs(searchRowOffset) < 2))
                {
                    // enforce minimum search of eight immediate neighbors as checking for local maxima becomes meaningless
                    // if the search radius collapses to zero.
                    maxColumnSearchOffsetInCells = 1;
                }

                for (int searchColumnOffset = -maxColumnSearchOffsetInCells; searchColumnOffset <= maxColumnSearchOffsetInCells; ++searchColumnOffset)
                {
                    int searchColumnIndex = dsmColumnIndex + searchColumnOffset;
                    if (searchState.DsmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out float dsmSearchZ) == false)
                    {
                        continue;
                    }

                    float searchZ = dsmSearchZ;
                    if (this.Method == TreetopDetectionMethod.ChmRadius)
                    {
                        if (searchState.DtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out float dtmZ) == false)
                        {
                            continue;
                        }
                        searchZ -= dtmZ;
                    }

                    if (searchZ > candidateZ) // check of cell against itself when searchRowOffset = searchColumnOffset = 0 deemed not worth testing for but would need to be addressed if > is changed to >=
                    {
                        // some other cell within search radius is higher, so exclude this cell as a local maxima
                        // Abort local search, move to next cell.
                        higherCellFound = true;
                        break;
                    }
                    else if ((searchZ == candidateZ) && Raster.IsNeighbor8(searchRowOffset, searchColumnOffset))
                    {
                        // if a neighboring cell (eight way adjacency) is of equal height, grow an equal height patch
                        // Growth is currently cell by cell, relying on incremental and sequential search of raster. Will need
                        // to be adjusted if cell skipping is implemented.
                        // While equal height patches are most commonly two cells there may be three or more cells in a patch and,
                        // depending on how the patch is discovered during search, OnEqualHeightPatch() may be called multiple times
                        // around the DSM cell where the patch is discovered. Since the patch is created only on the first call to
                        // OnEqualHeightPatch() |= is therefore used with newEqualHeightPatchFound to avoid potentially creating both
                        // a patch and a treetop (or possibly multiple treetops, though that's unlikely).
                        dsmCellInEqualHeightPatch = true;
                        newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmRowIndex, dsmColumnIndex, candidateZ, searchRowIndex, searchColumnIndex);
                    }
                }

                if (higherCellFound)
                {
                    break;
                }
            }

            if (higherCellFound)
            {
                return false;
            }
            else if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    Debug.Assert(searchState.MostRecentEqualHeightPatch != null);
                    searchState.TreetopEqualHeightPatches.Add(searchState.MostRecentEqualHeightPatch);
                }

                return false;
            }
            else
            {
                return true;
            }
        }

        private static bool RingSearch(int dsmRowIndex, int dsmColumnIndex, float dsmZ, float dtmElevation, RingSearchState searchState)
        {
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            float searchRadiusInCrsUnits = Single.Min(0.045F * heightInM + 0.5F, 5.0F) / searchState.CrsLinearUnits;

            bool dsmCellInEqualHeightPatch = false;
            bool higherCellWithinInnerRings = false;
            bool newEqualHeightPatchFound = false;
            int maxRingIndex = Int32.Min((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), GetTreetops.Rings.Count);
            int maxInnerRingIndex = Int32.Max((int)Single.Round(maxRingIndex / 2 + 0.5F), 1);
            Span<float> maxRingHeight = stackalloc float[maxRingIndex];
            Span<float> minRingHeight = stackalloc float[maxRingIndex];
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                Ring ring = GetTreetops.Rings[ringIndex];

                float ringMinimumZ = Single.MaxValue;
                float ringMaximumZ = Single.MinValue;
                for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                {
                    int searchRowIndex = dsmRowIndex + ring.YIndices[cellIndex];
                    int searchColumnIndex = dsmColumnIndex + ring.XIndices[cellIndex];
                    if (searchState.DsmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out float searchZ) == false)
                    {
                        continue;
                    }

                    if ((searchZ > dsmZ) && (ringIndex <= maxInnerRingIndex))
                    {
                        // some other cell within search radius is higher, so exclude this cell as a local maxima
                        // Abort local search, move to next cell.
                        higherCellWithinInnerRings = true;
                        break;
                    }
                    else if ((searchZ == dsmZ) && (ringIndex == 0)) // first ring is eight neighboring cells
                    {
                        dsmCellInEqualHeightPatch = true;
                        newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmRowIndex, dsmColumnIndex, dsmZ, searchRowIndex, searchColumnIndex);
                    }

                    if (searchZ < ringMinimumZ)
                    {
                        ringMinimumZ = searchZ;
                    }
                    if (searchZ > ringMaximumZ)
                    {
                        ringMaximumZ = searchZ;
                    }
                }

                if (higherCellWithinInnerRings)
                {
                    break;
                }

                maxRingHeight[ringIndex] = ringMaximumZ;
                minRingHeight[ringIndex] = ringMinimumZ;
            }

            if (higherCellWithinInnerRings)
            {
                return false;
            }

            float netProminence = 0.0F;
            float totalRange = 0.0F;
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                float ringProminence = dsmZ - maxRingHeight[ringIndex];
                float ringRange = maxRingHeight[ringIndex] - minRingHeight[ringIndex];

                netProminence += ringProminence;
                totalRange += ringRange;
            }
            if (netProminence >= Single.MaxValue)
            {
                return false; // at least one ring (usually ring radius = 1) has no data
            }
            Debug.Assert((Single.IsNaN(netProminence) == false) && (netProminence > -100000.0F) && (netProminence < 10000.0F));

            float netProminenceNormalized = netProminence / heightInCrsUnits;

            if (searchState.RingData != null)
            {
                Debug.Assert((searchState.NetProminenceBand != null) && (searchState.RadiusBand != null) && (searchState.RangeProminenceRatioBand != null) && (searchState.TotalProminenceBand != null) && (searchState.TotalRangeBand != null));

                searchState.NetProminenceBand[dsmColumnIndex, dsmRowIndex] = netProminenceNormalized;
                searchState.RadiusBand[dsmColumnIndex, dsmRowIndex] = maxRingIndex;

                float rangeProminenceRatioNormalized = totalRange / (maxRingIndex * heightInCrsUnits * netProminence);
                searchState.RangeProminenceRatioBand[dsmColumnIndex, dsmRowIndex] = rangeProminenceRatioNormalized;
                
                float totalProminenceNormalized = netProminence / (maxRingIndex * heightInCrsUnits);
                searchState.TotalProminenceBand[dsmColumnIndex, dsmRowIndex] = totalProminenceNormalized;
                
                float totalRangeNormalized = totalRange / (maxRingIndex * heightInCrsUnits);
                searchState.TotalRangeBand[dsmColumnIndex, dsmRowIndex] = totalRangeNormalized;
            }

            if (netProminenceNormalized <= 0.02F)
            {
                return false;
            }

            if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    searchState.AddMostRecentEqualHeightPatchAsTreetop();
                }

                return false;
            }

            return true;
        }

        private class Ring
        {
            public int[] XIndices { get; private set; }
            public int[] YIndices { get; private set; }

            public Ring(int[] xIndices, int[] yIndices)
            {
                if (xIndices.Length != yIndices.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(yIndices), "X index sets must match. X indices are of length " + xIndices.Length + " but y indices are of length " + yIndices.Length + ".");
                }

                this.XIndices = xIndices;
                this.YIndices = yIndices;
            }

            public int Count
            {
                get { return this.XIndices.Length; }
            }
        }

        private class TileSearchState
        {
            public float CrsLinearUnits { get; private init; }
            public RasterBand<float> Dsm { get; private init; }
            public float DsmCellHeight { get; private init; }
            public float DsmCellWidth { get; private init; }
            public VirtualRasterNeighborhood8<float> DsmNeighborhood { get; private init; }
            public RasterBand<float> Dtm { get; private init; }
            public VirtualRasterNeighborhood8<float> DtmNeighborhood { get; private init; }

            public float MinimumCandidateHeight { get; set; }
            public int NextTreeID { get; set; }

            public int EqualHeightPatchCommitInterval { get; set; }
            public SameHeightPatch<float>? MostRecentEqualHeightPatch { get; set; }
            public List<SameHeightPatch<float>> TreetopEqualHeightPatches { get; private init; }

            public TileSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood)
            {
                this.Dsm = dsmNeighborhood.Center;

                this.CrsLinearUnits = (float)this.Dsm.Crs.GetLinearUnits(); // 1.0 if CRS uses meters, 0.3048 if CRS is in feet
                this.DsmCellHeight = MathF.Abs((float)this.Dsm.Transform.CellHeight); // ensure positive cell height values
                this.DsmCellWidth = (float)this.Dsm.Transform.CellWidth;
                this.DsmNeighborhood = dsmNeighborhood;
                this.Dtm = dtmNeighborhood.Center;
                this.DtmNeighborhood = dtmNeighborhood;

                this.MinimumCandidateHeight = GetTreetops.DefaultMinimumHeight;
                this.NextTreeID = 1;

                this.EqualHeightPatchCommitInterval = (int)(50.0F / (this.CrsLinearUnits * this.DsmCellHeight));
                this.MostRecentEqualHeightPatch = null;
                this.TreetopEqualHeightPatches = new();
            }

            public void AddMostRecentEqualHeightPatchAsTreetop()
            {
                if (this.MostRecentEqualHeightPatch == null)
                {
                    throw new InvalidOperationException("Attempt to accept the most recent equal height patch as a treetop when no patch has yet been found.");
                }

                SameHeightPatch<float> treetop = this.MostRecentEqualHeightPatch;
                treetop.ID = this.NextTreeID++;
                this.TreetopEqualHeightPatches.Add(treetop);
            }

            public bool OnEqualHeightPatch(int dsmRowIndex, int dsmColumnIndex, float candidateZ, int searchRowIndex, int searchColumnIndex)
            {
                // clear most recent patch if this is a different patch
                if ((this.MostRecentEqualHeightPatch != null) && (this.MostRecentEqualHeightPatch.Height != candidateZ))
                {
                    this.MostRecentEqualHeightPatch = null;
                }

                bool newEqualHeightPatchFound = false;
                if (this.MostRecentEqualHeightPatch == null)
                {
                    // check for existing patch
                    // Since patches are added sequentially, the patches adjacent to the current search point will be
                    // towards the ned of the list.
                    for (int patchIndex = this.TreetopEqualHeightPatches.Count - 1; patchIndex >= 0; --patchIndex)
                    {
                        SameHeightPatch<float> candidatePatch = this.TreetopEqualHeightPatches[patchIndex];
                        if (candidatePatch.Height != candidateZ)
                        {
                            continue;
                        }
                        if (candidatePatch.Contains(searchRowIndex, searchColumnIndex))
                        {
                            this.MostRecentEqualHeightPatch = candidatePatch;
                            break;
                        }
                        else if (candidatePatch.Contains(dsmRowIndex, dsmColumnIndex))
                        {
                            this.TryGetDtmValueNoDataNan(searchRowIndex, searchColumnIndex, out float dtmSearchZ);

                            this.MostRecentEqualHeightPatch = candidatePatch;
                            this.MostRecentEqualHeightPatch.Add(searchRowIndex, searchColumnIndex, dtmSearchZ);
                            break;
                        }
                    }
                    if (this.MostRecentEqualHeightPatch == null)
                    {
                        float dtmElevation = this.Dtm[dsmColumnIndex, dsmRowIndex];
                        if (this.Dtm.IsNoData(dtmElevation))
                        {
                            dtmElevation = Single.NaN;
                        }

                        this.TryGetDtmValueNoDataNan(searchRowIndex, searchColumnIndex, out float dtmSearchZ);
                        this.MostRecentEqualHeightPatch = new(candidateZ, dsmRowIndex, dsmColumnIndex, dtmElevation, searchRowIndex, searchColumnIndex, dtmSearchZ);

                        newEqualHeightPatchFound = true;
                    }
                }
                else
                {
                    this.TryGetDtmValueNoDataNan(searchRowIndex, searchColumnIndex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch.Add(searchRowIndex, searchColumnIndex, dtmSearchZ);
                }

                return newEqualHeightPatchFound;
            }

            private bool TryGetDtmValueNoDataNan(int searchRowIndex, int searchColumnIndex, out float dtmSearchZ)
            {
                if (this.DtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out dtmSearchZ) == false)
                {
                    dtmSearchZ = Single.NaN;
                }

                return true;
            }
        }

        private class RingSearchState : TileSearchState
        {
            public RasterBand<float>? NetProminenceBand { get; private init; }
            public RasterBand<float>? RadiusBand { get; private init; }
            public RasterBand<float>? RangeProminenceRatioBand { get; private init; }
            public RasterBand<float>? TotalProminenceBand { get; private init; }
            public RasterBand<float>? TotalRangeBand { get; private init; }

            public Raster<float>? RingData { get; private init; }

            public RingSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood, bool diagnostics)
                : base(dsmNeighborhood, dtmNeighborhood)
            {
                if (diagnostics)
                {
                    this.RingData = new(this.Dsm.Crs, this.Dsm.Transform, this.Dsm.XSize, this.Dsm.YSize, 5, Single.NaN);
                    this.NetProminenceBand = this.RingData.Bands[0];
                    this.RangeProminenceRatioBand = this.RingData.Bands[1];
                    this.TotalProminenceBand = this.RingData.Bands[2];
                    this.TotalRangeBand = this.RingData.Bands[3];
                    this.RadiusBand = this.RingData.Bands[4];

                    this.NetProminenceBand.Name = "net prominence normalized";
                    this.RangeProminenceRatioBand.Name = "range-prominence normalized";
                    this.TotalProminenceBand.Name = "total prominence normalized";
                    this.TotalRangeBand.Name = "total range normalized";
                    this.RadiusBand.Name = "radius";
                }
                else
                {
                    this.NetProminenceBand = null;
                    this.RangeProminenceRatioBand = null;
                    this.RingData = null;
                    this.TotalProminenceBand = null;
                    this.TotalRangeBand = null;
                }
            }
        }
    }
}