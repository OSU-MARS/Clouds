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
            this.dsmTiles = new();
            this.dtmTiles = new();

            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.Diagnostics = null;
            this.Method = TreetopDetectionMethod.DsmRadius;
            this.MinimumHeight = GetTreetops.DefaultMinimumHeight;
            // this.Treetops is mandatory
        }

        private static void AddToLayer(TreetopLayer treetopLayer, TileSearchState tileSearch, int treeID, double yIndexFractional, double xIndexFractional, float groundElevationAtBaseOfTree, float treetopElevation, double treeRadiusInCrsUnits)
        {
            (double centroidX, double centroidY) = tileSearch.Dsm.Transform.GetProjectedCoordinate(xIndexFractional, yIndexFractional);
            float treeHeightInCrsUnits = treetopElevation - groundElevationAtBaseOfTree;
            Debug.Assert((groundElevationAtBaseOfTree > -200.0F) && (groundElevationAtBaseOfTree < 30000.0F) && (treeHeightInCrsUnits < 400.0F));
            treetopLayer.Add(treeID, centroidX, centroidY, groundElevationAtBaseOfTree, treeHeightInCrsUnits, treeRadiusInCrsUnits);
        }

        private string LoadTile(List<string> dsmTiles, int tileIndex)
        {
            Debug.Assert(String.IsNullOrEmpty(this.Dtm) == false);
            string dsmTilePath = dsmTiles[tileIndex];
            string dsmFileName = Path.GetFileName(dsmTilePath);
            string dtmTilePath = Path.Combine(this.Dtm, dsmFileName);

            this.LoadTile(dsmTilePath, dtmTilePath);
            return dsmFileName;
        }

        private void LoadTile(string dsmFilePath, string dtmFilePath)
        {
            Raster<float> dsm = GdalCmdlet.ReadRaster<float>(dsmFilePath);
            Raster<float> dtm = GdalCmdlet.ReadRaster<float>(dtmFilePath);

            lock (this.dsmTiles)
            {
                this.dsmTiles.Add(dsm);
                this.dtmTiles.Add(dtm);
            }
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null) && (this.Treetops != null));
            (string ? dsmDirectoryPath, string? dsmTileSearchPattern) = GdalCmdlet.ExtractTileDirectoryPathAndSearchPattern(this.Dsm, "*.tif");

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<string> dsmTilePaths;
            int treetopCandidates = 0;
            if (dsmDirectoryPath == null)
            {
                // single tile case
                dsmTilePaths = [ this.Dsm ];
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
                    throw new ParameterOutOfRangeException(nameof(this.Dtm), "-" + nameof(this.Dtm) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }
                FileAttributes treetopPathAttributes = File.GetAttributes(this.Treetops);
                if (treetopPathAttributes.HasFlag(FileAttributes.Directory) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Treetops), "-" + nameof(this.Treetops) + " must be an existing directory when " + nameof(this.Dsm) + " indicates multiple files.");
                }

                // load all tiles
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = this.MaxThreads
                };

                this.dsmTiles.TileCapacity = dsmTilePaths.Count;
                this.dtmTiles.TileCapacity = dsmTilePaths.Capacity;
                string? mostRecentDsmTileName = null;
                int tilesLoaded = 0;
                Task loadTilesTask = Task.Run(() =>
                {
                    Parallel.For(0, dsmTilePaths.Count, parallelOptions, (int tileIndex) =>
                    {
                        // find treetops in tile
                        mostRecentDsmTileName = this.LoadTile(dsmTilePaths, tileIndex);
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
                if ((this.dsmTiles.TileTransform.OriginX != this.dtmTiles.TileTransform.OriginX) || 
                    (this.dsmTiles.TileTransform.OriginY != this.dtmTiles.TileTransform.OriginY))
                {
                    throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same origin. The DSM origin (" + this.dsmTiles.TileTransform.OriginX + ", " + this.dsmTiles.TileTransform.OriginY + ") is offset from the DTM origin (" + this.dtmTiles.TileTransform.OriginX + ", " + this.dtmTiles.TileTransform.OriginY + ").");
                }
                if (this.dsmTiles.IsSameSpatialResolutionAndExtent(this.dsmTiles) == false)
                {
                    throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same extent and cell size.");
                }

                // find treetop candidates in all tiles
                mostRecentDsmTileName = null;
                int tilesCompleted = 0;
                Task findTreetopsTask = Task.Run(() =>
                {
                    Parallel.For(0, this.dsmTiles.TileCount, parallelOptions, (int tileIndex) =>
                    {
                        string dsmTilePath = this.dsmTiles[tileIndex].FilePath;
                        Debug.Assert(String.IsNullOrWhiteSpace(dsmTilePath) == false);

                        string dsmFileName = Path.GetFileName(dsmTilePath);
                        string dsmFileNameWithoutExtension = Path.GetFileNameWithoutExtension(dsmFileName);
                        string treetopTilePath = Path.Combine(this.Treetops, dsmFileNameWithoutExtension + ".gpkg");
                        mostRecentDsmTileName = dsmFileName;

                        int treetopCandidatesInTile = this.ProcessTile(tileIndex, treetopTilePath);
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
            using DataSource treetopFile = File.Exists(treetopFilePath) ? Ogr.Open(treetopFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(treetopFilePath, null);
            using TreetopLayer treetopLayer = TreetopLayer.CreateOrOverwrite(treetopFile, tileSearch.Dsm.Crs);
            double dsmCellSize = tileSearch.Dsm.Transform.GetCellSize();
            for (int dsmIndex = 0, dsmYindex = 0; dsmYindex < tileSearch.Dsm.YSize; ++dsmYindex) // y for north up rasters
            {
                for (int dsmXindex = 0; dsmXindex < tileSearch.Dsm.XSize; ++dsmIndex, ++dsmXindex) // x for north up rasters
                {
                    float dsmZ = tileSearch.Dsm.Data.Span[dsmIndex];
                    if (tileSearch.Dsm.IsNoData(dsmZ))
                    {
                        continue;
                    }

                    // read DTM interpolated to DSM resolution to get local height
                    // (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                    // (int dtmRowIndex, int dtmColumnIndex) = dtm.Transform.GetCellIndex(cellX, cellY); 
                    float dtmElevation = tileSearch.Dtm[dsmXindex, dsmYindex]; // could use dsmIndex
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
                    int radiusInCells;
                    if (this.Method == TreetopDetectionMethod.DsmRing)
                    {
                        (addTreetop, radiusInCells) = GetTreetops.RingSearch(dsmXindex, dsmYindex, dsmZ, dtmElevation, (RingSearchState)tileSearch);
                    }
                    else
                    {
                        (addTreetop, radiusInCells) = this.RadiusSearch(dsmXindex, dsmYindex, dsmZ, dtmElevation, tileSearch);
                    }

                    // create point if this cell is a unique local maxima
                    if (addTreetop)
                    {
                        (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmXindex, dsmYindex);
                        treetopLayer.Add(tileSearch.NextTreeID++, cellX, cellY, dtmElevation, heightInCrsUnits, radiusInCells * dsmCellSize);
                    }
                }

                // perf: periodically commit patches which are many rows away and will no longer be added to
                // Could also do an immediate insert above and then upsert here once patches are fully discovered but doing so
                // appears low value.
                if ((dsmYindex > tileSearch.EqualHeightPatchCommitInterval) && (dsmYindex % tileSearch.EqualHeightPatchCommitInterval == 0))
                {
                    double maxPatchRowIndexToCommit = dsmYindex - tileSearch.EqualHeightPatchCommitInterval;
                    int maxPatchIndex = -1;
                    for (int patchIndex = 0; patchIndex < tileSearch.TreetopEqualHeightPatches.Count; ++patchIndex)
                    {
                        SameHeightPatch<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
                        (double centroidXindex, double centroidYindex, float centroidElevation, float radiusInCells) = equalHeightPatch.GetCentroid();
                        if (centroidYindex > maxPatchRowIndexToCommit)
                        {
                            break;
                        }

                        double radiusInCrsUnits = radiusInCells * dsmCellSize;
                        GetTreetops.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
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
                (double centroidXindex, double centroidYindex, float centroidElevation, float radiusInCells) = equalHeightPatch.GetCentroid();

                double radiusInCrsUnits = radiusInCells * dsmCellSize;
                GetTreetops.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
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

        private (bool addTreetop, int radiusInCells) RadiusSearch(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TileSearchState searchState)
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
            int ySearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), 1);
            for (int searchYoffset = -ySearchRadiusInCells; searchYoffset <= ySearchRadiusInCells; ++searchYoffset)
            {
                int searchYindex = dsmYindex + searchYoffset;
                // constrain column bounds to circular search based on row offset
                float searchYoffsetInCrsUnits = searchYoffset * searchState.DsmCellHeight;
                int maxXoffsetInCells = 0;
                if (MathF.Abs(searchYoffsetInCrsUnits) < searchRadiusInCrsUnits) // avoid NaN from Sqrt()
                {
                    float xSearchDistanceInCrsUnits = MathF.Sqrt(searchRadiusInCrsUnits * searchRadiusInCrsUnits - searchYoffsetInCrsUnits * searchYoffsetInCrsUnits);
                    maxXoffsetInCells = (int)(xSearchDistanceInCrsUnits / searchState.DsmCellWidth + 0.5F);
                }
                if ((maxXoffsetInCells < 1) && (Math.Abs(searchYoffset) < 2))
                {
                    // enforce minimum search of eight immediate neighbors as checking for local maxima becomes meaningless
                    // if the search radius collapses to zero.
                    maxXoffsetInCells = 1;
                }

                for (int searchXoffset = -maxXoffsetInCells; searchXoffset <= maxXoffsetInCells; ++searchXoffset)
                {
                    int searchXindex = dsmXindex + searchXoffset;
                    if (searchState.DsmNeighborhood.TryGetValue(searchXindex, searchYindex, out float dsmSearchZ) == false)
                    {
                        continue;
                    }

                    float searchZ = dsmSearchZ;
                    if (this.Method == TreetopDetectionMethod.ChmRadius)
                    {
                        if (searchState.DtmNeighborhood.TryGetValue(searchXindex, searchYindex, out float dtmZ) == false)
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
                    else if ((searchZ == candidateZ) && Raster.IsNeighbor8(searchYoffset, searchXoffset))
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
                        newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmXindex, dsmYindex, candidateZ, searchXindex, searchYindex, ySearchRadiusInCells);
                    }
                }

                if (higherCellFound)
                {
                    break;
                }
            }

            if (higherCellFound)
            {
                return (false, -1);
            }
            else if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    Debug.Assert(searchState.MostRecentEqualHeightPatch != null);
                    searchState.TreetopEqualHeightPatches.Add(searchState.MostRecentEqualHeightPatch);
                }

                return (false, -1);
            }
            else
            {
                return (true, ySearchRadiusInCells);
            }
        }

        private static (bool addTreetop, int radiusInCells) RingSearch(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, RingSearchState searchState)
        {
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            float searchRadiusInCrsUnits = Single.Min(0.045F * heightInM + 0.5F, 5.0F) / searchState.CrsLinearUnits;

            bool dsmCellInEqualHeightPatch = false;
            bool higherCellWithinInnerRings = false;
            bool newEqualHeightPatchFound = false;
            int maxRingIndex = Int32.Min((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), Ring.Rings.Count);
            int maxInnerRingIndex = Int32.Max((int)Single.Round(maxRingIndex / 2 + 0.5F), 1);
            Span<float> maxRingHeight = stackalloc float[maxRingIndex];
            Span<float> minRingHeight = stackalloc float[maxRingIndex];
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];

                float ringMinimumZ = Single.MaxValue;
                float ringMaximumZ = Single.MinValue;
                for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                {
                    int searchXindex = dsmXindex + ring.XIndices[cellIndex];
                    int searchYindex = dsmYindex + ring.YIndices[cellIndex];
                    if (searchState.DsmNeighborhood.TryGetValue(searchXindex, searchYindex, out float searchZ) == false)
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
                        newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmXindex, dsmYindex, dsmZ, searchXindex, searchYindex, maxRingIndex);
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
                return (false, -1);
            }

            float netProminence = 0.0F;
            float totalRange = 0.0F;
            int maxTallerRingRadius = -1;
            bool tallerRingRadiusFound = false;
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                float ringProminence = dsmZ - maxRingHeight[ringIndex];
                float ringRange = maxRingHeight[ringIndex] - minRingHeight[ringIndex];

                netProminence += ringProminence;
                totalRange += ringRange;
                if ((tallerRingRadiusFound == false) && (ringProminence > 0.0F))
                {
                    maxTallerRingRadius = ringIndex;
                }
                else
                {
                    tallerRingRadiusFound = true;
                }
            }
            if (netProminence >= Single.MaxValue)
            {
                return (false, -1); // at least one ring (usually ring radius = 1) has no data
            }
            Debug.Assert((Single.IsNaN(netProminence) == false) && (netProminence > -100000.0F) && (netProminence < 10000.0F));

            float netProminenceNormalized = netProminence / heightInCrsUnits;

            if (searchState.RingData != null)
            {
                Debug.Assert((searchState.NetProminenceBand != null) && (searchState.RadiusBand != null) && (searchState.RangeProminenceRatioBand != null) && (searchState.TotalProminenceBand != null) && (searchState.TotalRangeBand != null));

                searchState.NetProminenceBand[dsmXindex, dsmYindex] = netProminenceNormalized;
                searchState.RadiusBand[dsmXindex, dsmYindex] = maxRingIndex;

                float rangeProminenceRatioNormalized = totalRange / (maxRingIndex * heightInCrsUnits * netProminence);
                searchState.RangeProminenceRatioBand[dsmXindex, dsmYindex] = rangeProminenceRatioNormalized;
                
                float totalProminenceNormalized = netProminence / (maxRingIndex * heightInCrsUnits);
                searchState.TotalProminenceBand[dsmXindex, dsmYindex] = totalProminenceNormalized;
                
                float totalRangeNormalized = totalRange / (maxRingIndex * heightInCrsUnits);
                searchState.TotalRangeBand[dsmXindex, dsmYindex] = totalRangeNormalized;
            }

            if (netProminenceNormalized <= 0.02F)
            {
                return (false, -1);
            }

            if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    searchState.AddMostRecentEqualHeightPatchAsTreetop();
                }

                return (false, -1);
            }

            return (true, maxTallerRingRadius);
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
                this.TreetopEqualHeightPatches = [];
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

            public bool OnEqualHeightPatch(int dsmXindex, int dsmYindex, float candidateZ, int searchXindex, int searchYindex, int radiusInCells)
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
                        if (candidatePatch.Contains(searchYindex, searchXindex))
                        {
                            this.MostRecentEqualHeightPatch = candidatePatch;
                            break;
                        }
                        else if (candidatePatch.Contains(dsmYindex, dsmXindex))
                        {
                            this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);

                            this.MostRecentEqualHeightPatch = candidatePatch;
                            this.MostRecentEqualHeightPatch.Add(searchXindex, searchYindex, dtmSearchZ);
                            break;
                        }
                    }
                    if (this.MostRecentEqualHeightPatch == null)
                    {
                        float dtmElevation = this.Dtm[dsmXindex, dsmYindex];
                        if (this.Dtm.IsNoData(dtmElevation))
                        {
                            dtmElevation = Single.NaN;
                        }

                        this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);
                        this.MostRecentEqualHeightPatch = new(candidateZ, dsmYindex, dsmXindex, dtmElevation, searchYindex, searchXindex, dtmSearchZ, radiusInCells);

                        newEqualHeightPatchFound = true;
                    }
                }
                else
                {
                    this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch.Add(searchXindex, searchYindex, dtmSearchZ);
                }

                return newEqualHeightPatchFound;
            }

            private bool TryGetDtmValueNoDataNan(int searchXindex, int searchYindex, out float dtmSearchZ)
            {
                if (this.DtmNeighborhood.TryGetValue(searchXindex, searchYindex, out dtmSearchZ) == false)
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