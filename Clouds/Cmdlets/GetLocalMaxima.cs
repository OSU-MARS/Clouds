using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using OSGeo.OGR;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LocalMaxima")]
    public class GetLocalMaxima : GdalCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to compress output rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each DSM must be a single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(HelpMessage = "DSM band to find local maxima in. Default is 'dsm'.")]
        [ValidateNotNullOrWhiteSpace]
        public string DsmBand { get; set; }

        [Parameter(Position = 2, HelpMessage = "1) path to write local maxima as a raster or a vector or 2,3) path to a directory to local maxima raster and vector tiles to.")]
        [ValidateNotNullOrEmpty]
        public string LocalMaxima { get; set; }

        public GetLocalMaxima() 
        {
            this.CompressRasters = false;
            this.Dsm = String.Empty;
            this.DsmBand = DigitalSurfaceModel.SurfaceBandName;
            this.LocalMaxima = String.Empty;
        }

        private static int FindLocalMaxima(LocalMaximaState state)
        {
            VirtualRasterNeighborhood8<float> currentNeighborhood = state.CurrentNeighborhood;
            RasterBand<float> currentSurface = currentNeighborhood.Center;
            RasterBand<byte> currentRadii = state.CurrentRadii;
            int localMaximaFound = 0;
            for (int tileYindex = 0; tileYindex < currentSurface.SizeY; ++tileYindex)
            {
                // test cells 0..n-1 in row
                // Since ~95% of cells are not local maxima at any radius, test neighbors in same row before calling GetLocalMaximaRadius() for more
                // extensive checking. Similarly, GetLocalMaximaRadius() checks the remaining six neighbors (queen adjacency) before testing at larger
                // radii.
                bool hasPreviousSurfaceZ = currentNeighborhood.TryGetValue(-1, tileYindex, out float previousSurfaceZ);
                float surfaceZ = currentSurface[0, tileYindex];
                bool hasSurfaceZ = currentSurface.IsNoData(surfaceZ) == false;
                float nextSurfaceZ;
                bool hasNextSurfaceZ;
                for (int tileNextXindex = 1; tileNextXindex < currentSurface.SizeX - 1; ++tileNextXindex)
                {
                    nextSurfaceZ = currentSurface[tileNextXindex, tileYindex];
                    hasNextSurfaceZ = currentSurface.IsNoData(nextSurfaceZ) == false;

                    if (hasSurfaceZ)
                    {
                        if (((hasPreviousSurfaceZ == false) || (previousSurfaceZ <= surfaceZ)) && ((hasNextSurfaceZ == false) || (nextSurfaceZ <= surfaceZ)))
                        {
                            float ring1minimumZ;
                            float ring1maximumZ;
                            if (hasPreviousSurfaceZ && hasNextSurfaceZ)
                            {
                                ring1maximumZ = previousSurfaceZ > nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                                ring1minimumZ = previousSurfaceZ < nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                            }
                            else if (hasPreviousSurfaceZ)
                            {
                                ring1maximumZ = previousSurfaceZ;
                                ring1minimumZ = previousSurfaceZ;
                            }
                            else if (hasNextSurfaceZ)
                            {
                                ring1maximumZ = nextSurfaceZ;
                                ring1minimumZ = nextSurfaceZ;
                            }
                            else
                            {
                                ring1maximumZ = Single.MinValue;
                                ring1minimumZ = Single.MaxValue;
                            }
                            if (GetLocalMaxima.TryGetLocalMaximaRadiusAndStatistics(tileNextXindex - 1, tileYindex, surfaceZ, ring1maximumZ, ring1minimumZ, state))
                            {
                                ++localMaximaFound;
                            }
                        }
                        else
                        {
                            currentRadii[tileNextXindex - 1, tileYindex] = 0; // not a local maximum as either previous or next z is higher
                            // not a maxima so nothing for vector output
                        }
                    } // else no z value for cell so local maxima radius is undefined; leave as no data

                    // advance
                    previousSurfaceZ = surfaceZ;
                    hasPreviousSurfaceZ = hasSurfaceZ;
                    surfaceZ = nextSurfaceZ;
                    hasSurfaceZ = hasNextSurfaceZ;
                }

                // last cell in row
                if (hasSurfaceZ)
                {
                    hasNextSurfaceZ = currentNeighborhood.TryGetValue(currentSurface.SizeX, tileYindex, out nextSurfaceZ);
                    int tileXIndex = currentSurface.SizeX - 1;
                    if (((hasPreviousSurfaceZ == false) || (previousSurfaceZ <= surfaceZ)) && ((hasNextSurfaceZ == false) || (nextSurfaceZ <= surfaceZ)))
                    {
                        float ring1minimumZ;
                        float ring1maximumZ;
                        if (hasPreviousSurfaceZ && hasNextSurfaceZ)
                        {
                            ring1maximumZ = previousSurfaceZ > nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                            ring1minimumZ = previousSurfaceZ < nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                        }
                        else if (hasPreviousSurfaceZ)
                        {
                            ring1maximumZ = previousSurfaceZ;
                            ring1minimumZ = previousSurfaceZ;
                        }
                        else if (hasNextSurfaceZ)
                        {
                            ring1maximumZ = nextSurfaceZ;
                            ring1minimumZ = nextSurfaceZ;
                        }
                        else
                        {
                            ring1maximumZ = Single.MinValue;
                            ring1minimumZ = Single.MaxValue;
                        }
                        if (GetLocalMaxima.TryGetLocalMaximaRadiusAndStatistics(tileXIndex, tileYindex, ring1maximumZ, ring1minimumZ, surfaceZ, state))
                        {
                            ++localMaximaFound;
                        }
                    }
                    else
                    {
                        currentRadii[tileXIndex, tileYindex] = 0;
                        // not a maxima so nothing for vector output
                    }
                } // else no z value for cell so local maxima radius is undefined; leave as no data
            }

            return localMaximaFound;
        }

        protected override void ProcessRecord()
        {
            // setup
            Stopwatch stopwatch = Stopwatch.StartNew();
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRaster<DigitalSurfaceModel>("Get-LocalMaxima", this.Dsm, readData: false);
            TileReadWriteStreaming<DigitalSurfaceModel> maximaReadWrite = new(dsm, Directory.Exists(this.LocalMaxima));
            if ((dsm.TileCount > 1) && (maximaReadWrite.OutputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.LocalMaxima), "-" + nameof(this.LocalMaxima) + " must be an existing directory when -" + nameof(this.Dsm) + " indicates multiple files.");
            }

            // find local maxima in all tiles
            // Assume read is from flash (NVMe, SSD) and there's no distinct constraint on the number of read threads or a data
            // locality advantage.
            // perf baseline (5950X, SN770 on CPU lanes, 2024-06-12 build)
            //          tiles   maxima   compute threads   time, min   memory, GB  write speed, MB/s
            // debug    562     99.7M    16                16.8        ~5.5        ~32
            // release  562     99.7M    14                17.1        ~5.2        ~33
            long dsmMaximaTotal = 0;
            long cmmMaximaTotal = 0;
            long chmMaximaTotal = 0;
            int geopackageSqlBackgroundThreads = this.MaxThreads / 8; // for now, best guess/crude estimate
            int maxComputeThreads = this.MaxThreads - geopackageSqlBackgroundThreads;
            ParallelTasks findLocalMaximaTasks = new(Int32.Min(maxComputeThreads, dsm.TileCount), () =>
            {
                LocalMaximaRaster? localMaximaRaster = null; // cache for reuse to offload GC
                for (int tileIndex = maximaReadWrite.GetNextTileWriteIndexThreadSafe(); tileIndex < maximaReadWrite.MaxTileIndex; tileIndex = maximaReadWrite.GetNextTileWriteIndexThreadSafe())
                {
                    DigitalSurfaceModel? dsmTile = dsm[tileIndex];
                    if (dsmTile == null) 
                    {
                        continue;
                    }

                    // assist in read until neighborhood complete or no more tiles left to read
                    // If necessary, the outer while loop spin waits for other threads to complete neighborhood read.
                    int maxNeighborhoodIndex = maximaReadWrite.GetMaximumIndexNeighborhood8(tileIndex);
                    while (maximaReadWrite.IsReadCompleteTo(maxNeighborhoodIndex) == false)
                    {
                        if (maximaReadWrite.TileReadIndex < maximaReadWrite.MaxTileIndex) // guard against integer overflow of read index if a significant amount of spin waiting occurs
                        {
                            for (int tileDataReadIndex = maximaReadWrite.GetNextTileReadIndexThreadSafe(); tileDataReadIndex < maximaReadWrite.MaxTileIndex; tileDataReadIndex = maximaReadWrite.GetNextTileReadIndexThreadSafe())
                            {
                                DigitalSurfaceModel? tileToRead = dsm[tileDataReadIndex];
                                if (tileToRead == null)
                                {
                                    continue;
                                }

                                // must load DSM tile at given index even if it's beyond the necessary neighborhood
                                // Otherwise some tiles would not get loaded.
                                tileToRead.Read(DigitalSufaceModelBands.Required | DigitalSufaceModelBands.SourceIDSurface, maximaReadWrite.TilePool);
                                if (this.Stopping || maximaReadWrite.CancellationTokenSource.IsCancellationRequested)
                                {
                                    return;
                                }

                                lock (maximaReadWrite)
                                {
                                    maximaReadWrite.OnTileRead(tileDataReadIndex);
                                }
                                if (maximaReadWrite.IsReadCompleteTo(maxNeighborhoodIndex))
                                {
                                    break;
                                }
                            }
                        }
                    }

                    // create local maxima raster and vector tiles
                    string tileName = Tile.GetName(dsmTile.FilePath);
                    localMaximaRaster = LocalMaximaRaster.CreateRecreateOrReset(localMaximaRaster, dsmTile);
                    localMaximaRaster.FilePath = this.LocalMaxima;
                    if (maximaReadWrite.OutputPathIsDirectory)
                    {
                        localMaximaRaster.FilePath = Path.Combine(this.LocalMaxima, tileName + Constant.File.GeoTiffExtension);
                    }

                    string vectorTilePath = this.LocalMaxima;
                    if (maximaReadWrite.OutputPathIsDirectory)
                    {
                        vectorTilePath = Path.Combine(vectorTilePath, tileName + Constant.File.GeoPackageExtension);
                    }
                    using DataSource localMaximaVector = OgrExtensions.Open(vectorTilePath);
                    using LocalMaximaVector dsmMaximaPoints = LocalMaximaVector.CreateOrOverwrite(localMaximaVector, dsmTile, "dsmMaxima");

                    // find DSM maxima radii and statistics
                    LocalMaximaState state = new(tileName, dsm, tileIndex, localMaximaRaster)
                    {
                        CurrentStatistics = dsmMaximaPoints
                    };
                    int dsmMaximaFound = GetLocalMaxima.FindLocalMaxima(state);
                    if (this.Stopping || maximaReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    // find CMM maxima radii
                    state.CurrentNeighborhood = state.CmmNeighborhood;
                    state.CurrentStatistics = null;
                    state.CurrentRadii = localMaximaRaster.CmmMaxima;
                    int cmmMaximaFound = GetLocalMaxima.FindLocalMaxima(state);
                    if (this.Stopping || maximaReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    // find CHM maxima radii
                    state.CurrentNeighborhood = state.ChmNeighborhood;
                    state.CurrentRadii = localMaximaRaster.ChmMaxima;
                    int chmMaximaFound = GetLocalMaxima.FindLocalMaxima(state);
                    if (this.Stopping || maximaReadWrite.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    Debug.Assert(localMaximaRaster != null);
                    localMaximaRaster.Write(localMaximaRaster.FilePath, this.CompressRasters);

                    lock (maximaReadWrite)
                    {
                        maximaReadWrite.OnTileWritten(tileIndex);
                        dsmMaximaTotal += dsmMaximaFound;
                        cmmMaximaTotal += cmmMaximaFound;
                        chmMaximaTotal += chmMaximaFound;
                    }
                }
            }, new());

            TimedProgressRecord progress = new("Get-LocalMaxima", "Found local maxima in " + maximaReadWrite.TilesWritten + " of " + dsm.TileCount + " tiles...");
            this.WriteProgress(progress);
            while (findLocalMaximaTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Found local maxima in " + maximaReadWrite.TilesWritten + " of " + dsm.TileCount + " tiles...";
                progress.Update(maximaReadWrite.TilesWritten, dsm.TileCount);
                this.WriteProgress(progress);
            }

            // processing rates are constrained by GDAL load speeds but still overload the garbage collector
            // 5950X 16 thread read speed is ~1.8 GB/s (SN770 on CPU lanes).
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            long meanMaximaPerTile = dsmMaximaTotal / dsm.TileCount;
            this.WriteVerbose("Found " + dsmMaximaTotal.ToString("n0") + " DSM, " + cmmMaximaTotal.ToString("n0") + " CMM, and " + chmMaximaTotal.ToString("n0") + " CHM maxima within " + dsm.TileCount + " " + (dsm.TileCount > 1 ? "tiles" : "tile") + " in " + stopwatch.ToElapsedString() + " (" + meanMaximaPerTile.ToString("n0") + " maxima/tile).");
            base.ProcessRecord();
        }

        private static bool TryGetLocalMaximaRadiusAndStatistics(int tileXindex, int tileYindex, float surfaceZ, float ring1maximumZ, float ring1minimumZ, LocalMaximaState state)
        {
            // check remainder of first ring: caller has checked xOffset = ±1, yOffset = 0
            VirtualRasterNeighborhood8<float> currentNeighborhood = state.CurrentNeighborhood;
            Span<float> maxRingZ = stackalloc float[LocalMaximaVector.RingCount];
            Span<float> minRingZ = stackalloc float[LocalMaximaVector.RingCount];
            float ringMaximumZ = ring1maximumZ;
            float ringMinimumZ = ring1minimumZ;
            for (int yOffset = -1; yOffset < 2; yOffset += 2)
            {
                int cellYindex = tileYindex + yOffset;
                for (int xOffset = -1; xOffset < 2; ++xOffset)
                {
                    int cellXindex = tileXindex + xOffset;
                    if (currentNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                    {
                        if (z > surfaceZ)
                        {
                            state.CurrentRadii[tileXindex, tileYindex] = 0;
                            return false;
                        }

                        if (z < ringMinimumZ)
                        {
                            ringMinimumZ = z;
                        }
                        if (z > ringMaximumZ)
                        {
                            ringMaximumZ = z;
                        }
                    }
                }
            }

            maxRingZ[0] = ringMaximumZ;
            minRingZ[0] = ringMinimumZ;

            // local maxima radius is one minus the ring radius when a higher z value is encountered
            // Ring index is also one minus the ring radius so can just be returned as the local maxima radius.
            // Testing is done at progressively increasing radii for performance. It's most likely higher cells will be nearer, rather than
            // farther away, so working outwards tends to minimize the number of cells which need to be checked. Testing larger numbers of
            // adjacent cells in row major order may be more performant due to more effective use of cache lines (a 64 byte line holds 16
            // floats) but requires a more complex implementation tracking the minimum radius found along each y index. However, performance
            // investment here is well into diminishing returns as Get-LocalMaxima is limited by GDAL read speeds. DSM+CMM+CHM local maxima
            // identification runs at ~200 2000 x 2000 cell tiles per second (5950X).
            // Indices in rings are also sorted in row major order for performance.
            bool hasStatistics = state.CurrentStatistics != null;
            bool ringRadiusFound = false;
            byte ringRadiusInCells = (byte)Ring.Rings.Count; // default to no higher z value found within search radius
            for (byte ringIndex = 1; ringIndex < LocalMaximaVector.RingCount; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];
                ringMinimumZ = Single.MaxValue;
                ringMaximumZ = Single.MinValue;

                for (int offsetIndex = 0; offsetIndex < ring.Count; ++offsetIndex)
                {
                    int cellYindex = tileYindex + ring.XIndices[offsetIndex];
                    int cellXindex = tileXindex + ring.YIndices[offsetIndex];
                    if (currentNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                    {
                        if (z > surfaceZ)
                        {
                            if (ringRadiusFound == false)
                            {
                                ringRadiusInCells = ringIndex;
                                ringRadiusFound = true;
                            }

                            // ring minimum and maximum are needed only if vector output is enabled
                            if (hasStatistics == false)
                            {
                                break;
                            }
                        }

                        if (z < ringMinimumZ)
                        {
                            ringMinimumZ = z;
                        }
                        if (z > ringMaximumZ)
                        {
                            ringMaximumZ = z;
                        }
                    }
                }

                maxRingZ[ringIndex] = ringMaximumZ;
                minRingZ[ringIndex] = ringMinimumZ;
            }

            if (ringRadiusFound == false)
            {
                for (byte ringIndex = LocalMaximaVector.RingCount; ringIndex < Ring.Rings.Count; ++ringIndex)
                {
                    Ring ring = Ring.Rings[ringIndex];
                    for (int offsetIndex = 0; offsetIndex < ring.Count; ++offsetIndex)
                    {
                        int cellYindex = tileYindex + ring.XIndices[offsetIndex];
                        int cellXindex = tileXindex + ring.YIndices[offsetIndex];
                        if (currentNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                        {
                            if (z > surfaceZ)
                            {
                                ringRadiusInCells = ringIndex;
                                ringRadiusFound = true;
                                break;
                            }
                        }
                    }

                    if (ringRadiusFound)
                    {
                        break;
                    }
                }
            }

            state.CurrentRadii[tileXindex, tileYindex] = ringRadiusInCells;

            if (hasStatistics)
            {
                Debug.Assert((state.CurrentStatistics != null) && (state.DsmTile.SourceIDSurface != null)); // VS 17.8.7 nullability can't figure out hasStatistics == true means state.CurrentStatistics != null

                (double x, double y) = state.DsmTile.Transform.GetCellCenter(tileXindex, tileYindex);
                UInt16 sourceID = state.DsmTile.SourceIDSurface[tileXindex, tileYindex];
                float cmmZ = state.DsmTile.CanopyMaxima3[tileXindex, tileYindex];
                float chmHeight = state.DsmTile.CanopyHeight[tileXindex, tileYindex];
                state.CurrentStatistics.Add(sourceID, x, y, chmHeight, surfaceZ, cmmZ, ringRadiusInCells, maxRingZ, minRingZ);
            }

            return true;
        }

        private class LocalMaximaState
        {
            public RasterBand<byte> CurrentRadii { get; set; }
            public VirtualRasterNeighborhood8<float> CurrentNeighborhood { get; set; }
            public LocalMaximaVector? CurrentStatistics { get; set; }
            public VirtualRasterNeighborhood8<float> ChmNeighborhood { get; private init; }
            public VirtualRasterNeighborhood8<float> CmmNeighborhood { get; private init; }
            public VirtualRasterNeighborhood8<float> DsmNeighborhood { get; private init; }
            public DigitalSurfaceModel DsmTile { get; private init; }
            public LocalMaximaRaster MaximaRaster { get; private init; }
            public string TileName { get; private init; }

            public LocalMaximaState(string tileName, VirtualRaster<DigitalSurfaceModel> dsm, int tileIndex, LocalMaximaRaster maximaRaster)
            {
                DigitalSurfaceModel? dsmTile = dsm[tileIndex];
                if ((dsmTile == null) || (dsmTile.Surface.Data.Length != dsmTile.Cells) || (dsmTile.CanopyMaxima3.Data.Length != dsmTile.Cells) || (dsmTile.CanopyHeight.Data.Length != dsmTile.Cells) || (dsmTile.SourceIDSurface == null) || (dsmTile.SourceIDSurface.Data.Length != dsmTile.Cells))
                {
                    throw new ArgumentOutOfRangeException(nameof(dsm), "DSM tile at index " + tileIndex + " is missing or not fully loaded.");
                }

                (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileIndex);
                this.CurrentRadii = maximaRaster.DsmMaxima;
                this.CurrentStatistics = null;
                this.CurrentNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.SurfaceBandName);
                this.ChmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.CanopyHeightBandName);
                this.CmmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.CanopyMaximaBandName);
                this.DsmNeighborhood = this.CurrentNeighborhood;
                this.DsmTile = dsmTile;
                this.MaximaRaster = maximaRaster;
                this.TileName = tileName;

                // TODO: also check neighborhoods are fully loaded (in debug builds?)
            }
        }
    }
}
