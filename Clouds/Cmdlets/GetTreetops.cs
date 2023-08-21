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
            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.MaxThreads = Environment.ProcessorCount / 2;
            this.MinimumHeight = GetTreetops.DefaultMinimumHeight;
            // this.Treetops is mandatory
        }

        // eight-way immediate adjacency
        private static bool IsNeighbor8(int rowOffset, int columnOffset)
        {
            // exclude all cells with Euclidean grid distance >= 2.0
            int absRowOffset = Math.Abs(rowOffset);
            if (absRowOffset > 1)
            {
                return false;
            }

            int absColumnOffset = Math.Abs(columnOffset);
            if (absColumnOffset > 1)
            {
                return false;
            }

            // remaining nine possibilities have 0.0 <= Euclidean grid distance <= sqrt(2.0) and 0 <= Manhattan distance <= 2
            // Of these, only the self case needs to be excluded.
            return (absRowOffset > 0) || (absColumnOffset > 0);
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

            // single tile case
            if (dsmDirectoryPath == null)
            {
                this.ProcessTile(this.Dsm, this.Dtm, this.Treetops);
                return;
            }
            
            // multi-tile case
            Debug.Assert(dsmTileSearchPattern != null);
            List<string> dsmTiles = Directory.EnumerateFiles(dsmDirectoryPath, dsmTileSearchPattern, SearchOption.TopDirectoryOnly).ToList();
            if (dsmTiles.Count < 1)
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

            int loggingThreadID = Environment.CurrentManagedThreadId;
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = this.MaxThreads
            };
            Stopwatch stopwatch = Stopwatch.StartNew();
            int tilesCompleted = 0;
            Parallel.For(0, dsmTiles.Count, parallelOptions, (int tileIndex) =>
            {
                // find treetops in tile
                string dsmTilePath = dsmTiles[tileIndex];
                string dsmFileName = Path.GetFileName(dsmTilePath);
                string dsmFileNameWithoutExtension = Path.GetFileNameWithoutExtension(dsmFileName);
                string dtmTilePath = Path.Combine(this.Dtm, dsmFileName);
                string treetopTilePath = Path.Combine(this.Treetops, dsmFileNameWithoutExtension + ".gpkg");
                this.ProcessTile(dsmTilePath, dtmTilePath, treetopTilePath);
                int completedTileCount = Interlocked.Increment(ref tilesCompleted);

                // update progress
                // PowerShell allows writing only from the thread which entered ProcessRecord(). A lightweight solution
                // to log only from that thread, though doing so reduces the frequency of progress updates and is fragile to
                // Parallel.For() not using PowerShell's entry thread.
                if (Environment.CurrentManagedThreadId == loggingThreadID)
                {
                    double fractionComplete = (double)completedTileCount / (double)dsmTiles.Count;
                    double secondsElapsed = stopwatch.Elapsed.TotalSeconds;
                    double secondsRemaining = secondsElapsed * (1.0 / fractionComplete - 1.0);
                    this.WriteProgress(new ProgressRecord(0, "Get-Treetops", dsmFileName)
                    {
                        PercentComplete = (int)(100.0F * fractionComplete)
                    });
                }
            });
            stopwatch.Stop();

            string elapsedTimeFormat = stopwatch.Elapsed.TotalHours >= 1.0 ? "hh\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose(dsmTiles.Count + " tiles in " + stopwatch.Elapsed.ToString(elapsedTimeFormat) + ".");
        }

        private void ProcessTile(string dsmFilePath, string dtmFilePath, string treetopFilePath)
        {
            SinglebandRaster<float> dsm = GdalCmdlet.ReadSingleBandFloatRaster(dsmFilePath);
            SinglebandRaster<float> dtm = GdalCmdlet.ReadSingleBandFloatRaster(dtmFilePath);

            if (dsm.Crs.IsSameGeogCS(dtm.Crs) != 1)
            {
                throw new NotSupportedException("DSM CRS is '" + dsm.Crs.GetName() + "' while DTM CRS is " + dtm.Crs.GetName() + ".");
            }
            int dsmIsVertical = dsm.Crs.IsVertical();
            int dtmIsVertical = dtm.Crs.IsVertical();
            if (dsmIsVertical != dtmIsVertical)
            {
                throw new NotSupportedException("DSM and DTM must either both have a vertical CRS or both lack vertical CRS information.");
            }
            if ((dsmIsVertical != 0) && (dtmIsVertical != 0) && (dsm.Crs.IsSameVertCS(dtm.Crs) != 1))
            {
                throw new NotSupportedException("DSM and DTM have mismatched vertical coordinate systems.");
            }
            // default case (weak but unavoidable): if neither the DSM or DTM has a vertical CRS assume they have the same vertical CRS

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
            float dsmNoDataValue = dsm.NoDataValue;
            bool dsmNoDataIsNaN = Single.IsNaN(dsmNoDataValue);
            float dtmNoDataValue = dtm.NoDataValue;
            bool dtmNoDataIsNaN = Single.IsNaN(dtmNoDataValue);

            List<SameHeightPatch<float>> equalHeightPatches = new();

            for (int dsmIndex = 0, dsmRowIndex = 0, treeID = 1; dsmRowIndex < dsm.YSize; ++dsmRowIndex) // y for north up rasters
            {
                for (int dsmColumnIndex = 0; dsmColumnIndex < dsm.XSize; ++dsmIndex, ++dsmColumnIndex) // x for north up rasters
                {
                    (double cellX, double cellY) = dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                    float dsmZ = dsm.Data[dsmIndex];
                    if ((dsmNoDataIsNaN && Single.IsNaN(dsmZ)) || (dsmZ == dsmNoDataValue)) // have to test with IsNaN() since float.NaN == float.NaN = false
                    {
                        continue;
                    }

                    // interpolate DTM onto DSM to get local height
                    // Special case for integer only?
                    (int dtmRowIndex, int dtmColumnIndex) = dtm.Transform.GetCellIndex(cellX, cellY); 
                    float dtmElevation = dtm[dtmRowIndex, dtmColumnIndex];
                    if ((dtmNoDataIsNaN && Single.IsNaN(dtmElevation)) || (dtmElevation == dtmNoDataValue))
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
                    float searchRadiusInM = 8.59F / (1.0F + MathF.Exp((58.72F - heightInM) / 19.42F));
                    float searchRadiusInCrsUnits = searchRadiusInM / crsLinearUnits;

                    int rowSearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / dsmCellHeight + 0.5F), 1);
                    int minimumRowOffset = -Math.Min(rowSearchRadiusInCells, dsmRowIndex);
                    int maximumRowOffset = Math.Min(rowSearchRadiusInCells, dsm.YSize - dsmRowIndex - 1);

                    // check if point is local maxima
                    SameHeightPatch<float>? equalHeightPatch = null;
                    bool higherPointFound = false;
                    bool inEqualHeightPatch = false;
                    bool newEqualHeightPatchFound = false;
                    for (int searchRowOffset = minimumRowOffset; searchRowOffset <= maximumRowOffset; ++searchRowOffset)
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
                        int minimumColumnOffset = -Math.Min(maxColumnSearchOffsetInCells, dsmColumnIndex);
                        int maximumColumnOffset = Math.Min(maxColumnSearchOffsetInCells, dsm.XSize - dsmColumnIndex - 1);

                        for (int searchColumnOffset = minimumColumnOffset; searchColumnOffset <= maximumColumnOffset; ++searchColumnOffset)
                        {
                            int searchColumnIndex = dsmColumnIndex + searchColumnOffset;
                            float dsmSearchZ = dsm[searchRowIndex, searchColumnIndex];
                            if (dsmSearchZ > dsmZ) // check of cell against itself when searchRowOffset = searchColumnOffset = 0 deemed not worth testing for but would need to be addressed if > is changed to >=
                            {
                                // some other cell within search radius is higher, so exclude this cell as a local maxima
                                // Abort local search, move to next cell.
                                higherPointFound = true;
                                break;
                            }
                            else if ((dsmSearchZ == dsmZ) && GetTreetops.IsNeighbor8(searchRowOffset, searchColumnOffset))
                            {
                                if ((equalHeightPatch != null) && (equalHeightPatch.Height != dsmZ))
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
                                            inEqualHeightPatch = true;
                                            break;
                                        }
                                        else if (candidatePatch.Contains(dsmRowIndex, dsmColumnIndex)) 
                                        {
                                            equalHeightPatch = candidatePatch;
                                            equalHeightPatch.Add(searchRowIndex, searchColumnIndex, dtm[searchRowIndex, searchColumnIndex]);
                                            inEqualHeightPatch = true;
                                            break;
                                        }
                                    }
                                    if (equalHeightPatch == null)
                                    {
                                        equalHeightPatch = new(treeID++, heightInCrsUnits, dsmRowIndex, dsmColumnIndex, dtmElevation, searchRowIndex, searchColumnIndex, dtm[searchRowIndex, searchColumnIndex]);
                                        inEqualHeightPatch = true;
                                        newEqualHeightPatchFound = true;
                                    }
                                }
                                else
                                {
                                    equalHeightPatch.Add(searchRowIndex, searchColumnIndex, dtm[searchRowIndex, searchColumnIndex]);
                                    inEqualHeightPatch = true;
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
                    if (inEqualHeightPatch == false)
                    {
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
                (double centroidIndexX, double centroidIndexY, double centroidElevation) = equalHeightPatch.GetCentroid();
                (double centroidX, double centroidY) = dsm.Transform.ToProjectedCoordinate(centroidIndexX, centroidIndexY);

                treetopLayer.Add(equalHeightPatch.ID, centroidX, centroidY, centroidElevation, equalHeightPatch.Height);
            }
        }
    }
}