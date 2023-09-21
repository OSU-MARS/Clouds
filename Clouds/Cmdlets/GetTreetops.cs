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

        [Parameter(HelpMessage = "Detect treetops as local maxima in the canopy height model rather than the digital surface model.")]
        public SwitchParameter ChmMaxima { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each DSM must be a single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Maximum number of threads to use when processing tiles in parallel. Default is half of the procesor's thread count.")]
        public int MaxThreads { get; set; }

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

            this.ChmMaxima = false;
            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.MaxThreads = Environment.ProcessorCount / 2;
            this.MinimumHeight = GetTreetops.DefaultMinimumHeight;
            // this.Treetops is mandatory
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
            this.WriteVerbose(dsmTilePaths.Count + " tiles and " + treetopCandidates.ToString("n0") + " treetop candidates in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
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
            SinglebandRaster<float> dsm = GdalCmdlet.ReadSingleBandFloatRaster(dsmFilePath);
            SinglebandRaster<float> dtm = GdalCmdlet.ReadSingleBandFloatRaster(dtmFilePath);

            lock (this.dsmTiles)
            {
                this.dsmTiles.Add(dsm);
                this.dtmTiles.Add(dtm);
            }
        }

        private int ProcessTile(int tileIndex, string treetopFilePath)
        {
            SinglebandRaster<float> dsm = this.dsmTiles[tileIndex];
            VirtualRasterNeighborhood8<float> dsmNeighborhood = this.dsmTiles.GetNeighborhood8(tileIndex);
            SinglebandRaster<float> dtm = this.dtmTiles[tileIndex];
            VirtualRasterNeighborhood8<float> dtmNeighborhood = this.dtmTiles.GetNeighborhood8(tileIndex);

            // change minimum height from meters to feet if CRS uses English units
            // Assumption here is that xy and z units match, which is not necessarily enforced.
            float crsLinearUnits = (float)dsm.Crs.GetLinearUnits(); // 1.0 if CRS use meters, 0.3048 if CRS is in feet
            float minimumCandidateHeight = this.MinimumHeight;
            if ((minimumCandidateHeight == GetTreetops.DefaultMinimumHeight) && (crsLinearUnits != 1.0F))
            {
                // possible issue: what if the desired minimum height is exactly 1.5 feet?
                minimumCandidateHeight /= crsLinearUnits;
            }

            // find local maxima in DSM
            //using Dataset? treetopDataset = Gdal.Open(this.Treetops, Access.GA_Update);
            //if (treetopDataset == null)
            //{
            //    OSGeo.GDAL.Driver driver = Gdal.IdentifyDriver(this.Treetops, null);
            //    driver.Create();
            //}
            using DataSource? treetopFile = File.Exists(treetopFilePath) ? Ogr.Open(treetopFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(treetopFilePath, null);
            using TreetopLayer treetopLayer = new(treetopFile, dsm.Crs);

            float dsmCellHeight = MathF.Abs((float)dsm.Transform.CellHeight); // ensure positive cell height values
            float dsmCellWidth = (float)dsm.Transform.CellWidth;

            List<SameHeightPatch<float>> equalHeightPatches = new();
            int treeID = 1;
            for (int dsmIndex = 0, dsmRowIndex = 0; dsmRowIndex < dsm.YSize; ++dsmRowIndex) // y for north up rasters
            {
                for (int dsmColumnIndex = 0; dsmColumnIndex < dsm.XSize; ++dsmIndex, ++dsmColumnIndex) // x for north up rasters
                {
                    float dsmZ = dsm.Data[dsmIndex];
                    if (dsm.IsNoData(dsmZ))
                    {
                        continue;
                    }

                    // read DTM interpolated to DSM resolution to get local height
                    // (double cellX, double cellY) = dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                    // (int dtmRowIndex, int dtmColumnIndex) = dtm.Transform.GetCellIndex(cellX, cellY); 
                    float dtmElevation = dtm[dsmRowIndex, dsmColumnIndex];
                    if (dtm.IsNoData(dtmElevation))
                    {
                        continue;
                    }

                    // get search radius and area for local maxima in DSM
                    // For now, use logistic quantile regression at p = 0.025 from prior Elliott State Research Forest segmentations.
                    float heightInCrsUnits = dsmZ - dtmElevation;
                    if (heightInCrsUnits < minimumCandidateHeight)
                    {
                        continue;
                    }
                    float heightInM = crsLinearUnits * heightInCrsUnits;
                    // float searchRadiusInM = 8.59F / (1.0F + MathF.Exp((58.72F - heightInM) / 19.42F)); // logistic regression at 0.025 quantile against boostrap crown radii estimates
                    // float searchRadiusInM = 6.0F / (1.0F + MathF.Exp((49.0F - heightInM) / 18.5F)); // manual retune based on segmentation
                    float searchRadiusInM = Single.Min(0.055F * heightInM + 0.4F, 5.0F); // manual retune based on segmentation
                    float searchRadiusInCrsUnits = searchRadiusInM / crsLinearUnits;

                    // check if point is local maxima
                    float candidateZ = this.ChmMaxima ? heightInCrsUnits : dsmZ;
                    SameHeightPatch<float>? equalHeightPatch = null;
                    bool higherPointFound = false;
                    bool dsmCellInEqualHeightPatch = false;
                    bool newEqualHeightPatchFound = false;
                    int rowSearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / dsmCellHeight + 0.5F), 1);
                    for (int searchRowOffset = -rowSearchRadiusInCells; searchRowOffset <= rowSearchRadiusInCells; ++searchRowOffset)
                    {
                        int searchRowIndex = dsmRowIndex + searchRowOffset;
                        // constrain column bounds to circular search based on row offset
                        float searchRowOffsetInCrsUnits = searchRowOffset * dsmCellHeight;
                        int maxColumnSearchOffsetInCells = 0;
                        if (MathF.Abs(searchRowOffsetInCrsUnits) < searchRadiusInCrsUnits) // avoid NaN from Sqrt()
                        {
                            float columnSearchDistanceInCrsUnits = MathF.Sqrt(searchRadiusInCrsUnits * searchRadiusInCrsUnits - searchRowOffsetInCrsUnits * searchRowOffsetInCrsUnits);
                            maxColumnSearchOffsetInCells = (int)(columnSearchDistanceInCrsUnits / dsmCellWidth + 0.5F);
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
                            if (dsmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out float dsmSearchZ) == false)
                            {
                                continue;
                            }

                            float searchZ = dsmSearchZ;
                            bool hasDtmZ = false;
                            float dtmZ = Single.NaN;
                            if (this.ChmMaxima)
                            {
                                hasDtmZ = dtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out dtmZ);
                                if (hasDtmZ == false)
                                {
                                    continue;
                                }
                                searchZ -= dtmZ;
                            }

                            if (searchZ > candidateZ) // check of cell against itself when searchRowOffset = searchColumnOffset = 0 deemed not worth testing for but would need to be addressed if > is changed to >=
                            {
                                // some other cell within search radius is higher, so exclude this cell as a local maxima
                                // Abort local search, move to next cell.
                                higherPointFound = true;
                                break;
                            }
                            else if ((searchZ == candidateZ) && SinglebandRaster.IsNeighbor8(searchRowOffset, searchColumnOffset))
                            {
                                if ((equalHeightPatch != null) && (equalHeightPatch.Height != candidateZ))
                                {
                                    equalHeightPatch = null;
                                }

                                // if a neighboring cell (eight way adjacency) is of equal height, grow an equal height patch
                                // Growth is currently cell by cell, relying on incremental and sequential search of raster. Will need
                                // to be adjusted if cell skipping is implemented.
                                // When patch is inscribed in a circle whose radius is greater than sqrt(2) the current implementation
                                // can return multiple continguous patches. This is an unlikely case (most patches are just two cells)
                                // and can be handled by patch merging.
                                if (equalHeightPatch == null)
                                {
                                    for (int patchIndex = 0; patchIndex < equalHeightPatches.Count; ++patchIndex)
                                    {
                                        SameHeightPatch<float> candidatePatch = equalHeightPatches[patchIndex];
                                        if (candidatePatch.Contains(searchRowIndex, searchColumnIndex))
                                        {
                                            equalHeightPatch = candidatePatch;
                                            dsmCellInEqualHeightPatch = true;
                                            break;
                                        }
                                        else if (candidatePatch.Contains(dsmRowIndex, dsmColumnIndex))
                                        {
                                            equalHeightPatch = candidatePatch;

                                            if (hasDtmZ == false)
                                            {
                                                hasDtmZ = dtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out dtmZ);
                                                if (hasDtmZ == false)
                                                {
                                                    continue;
                                                }
                                            }

                                            equalHeightPatch.Add(searchRowIndex, searchColumnIndex, dtmZ);
                                            dsmCellInEqualHeightPatch = true;
                                            break;
                                        }
                                    }
                                    if (equalHeightPatch == null)
                                    {
                                        if (hasDtmZ == false)
                                        {
                                            hasDtmZ = dtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out dtmZ);
                                            if (hasDtmZ == false)
                                            {
                                                continue;
                                            }
                                        }

                                        equalHeightPatch = new(treeID++, heightInCrsUnits, dsmRowIndex, dsmColumnIndex, dtmElevation, searchRowIndex, searchColumnIndex, dtmZ);

                                        dsmCellInEqualHeightPatch = true;
                                        newEqualHeightPatchFound = true;
                                    }
                                }
                                else
                                {
                                    if (hasDtmZ == false)
                                    {
                                        hasDtmZ = dtmNeighborhood.TryGetValue(searchRowIndex, searchColumnIndex, out dtmZ);
                                        if (hasDtmZ == false)
                                        {
                                            continue;
                                        }
                                    }

                                    equalHeightPatch.Add(searchRowIndex, searchColumnIndex, dtmZ);
                                    
                                    dsmCellInEqualHeightPatch = true;
                                }
                            }
                        }

                        if (higherPointFound)
                        {
                            break;
                        }
                    }
                    if (higherPointFound)
                    {
                        continue;
                    }

                    // create point if this cell is a unique local maxima
                    if (dsmCellInEqualHeightPatch == false)
                    {
                        (double cellX, double cellY) = dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                        treetopLayer.Add(treeID++, cellX, cellY, dtmElevation, heightInCrsUnits);
                    }
                    else if (newEqualHeightPatchFound)
                    {
                        Debug.Assert(equalHeightPatch != null);
                        equalHeightPatches.Add(equalHeightPatch);
                    }
                }

                // TODO: periodically flush patches which are many rows away and will not be added to
            }

            // create points for equal height patches
            // Could also do an immediate insert above and then upsert here once patches are fully discovered but doing so
            // appears low value; the only obvious effect is each treetop's autogenerated fid would match the sequentially
            // assigned treeID.
            for (int patchIndex = 0; patchIndex < equalHeightPatches.Count; ++patchIndex)
            {
                SameHeightPatch<float> equalHeightPatch = equalHeightPatches[patchIndex];
                (double centroidRowIndex, double centroidColumnIndex, double centroidElevation) = equalHeightPatch.GetCentroid();
                (double centroidX, double centroidY) = dsm.Transform.ToProjectedCoordinate(centroidColumnIndex, centroidRowIndex);

                treetopLayer.Add(equalHeightPatch.ID, centroidX, centroidY, centroidElevation, equalHeightPatch.Height);
            }

            return treeID - 1;
        }
    }
}