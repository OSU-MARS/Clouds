using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using OSGeo.OGR;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LocalMaxima")]
    public class GetLocalMaxima : GdalCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(HelpMessage = "Whether or not to compress output rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each file must contain DigitalSurfaceModel's required bands.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dsm { get; set; }

        [Parameter(HelpMessage = "DSM band to find local maxima in. Default is 'dsm'.")]
        [ValidateNotNullOrWhiteSpace]
        public string DsmBand { get; set; }

        [Parameter(Position = 2, HelpMessage = "1) path to write local maxima as a raster or a vector or 2,3) path to a directory to write local maxima raster tiles to and vector local maxima tiles within.")]
        [ValidateNotNullOrEmpty]
        public string LocalMaxima { get; set; }

        public GetLocalMaxima() 
        {
            this.cancellationTokenSource = new();

            this.CompressRasters = false;
            this.Dsm = String.Empty;
            this.DsmBand = DigitalSurfaceModel.SurfaceBandName;
            this.LocalMaxima = String.Empty;
        }

        private int FindLocalMaxima(LocalMaximaState tileState)
        {
            RasterNeighborhood8<float> currentNeighborhood = tileState.CurrentNeighborhood;
            RasterBand<float> currentSurface = currentNeighborhood.Center;
            RasterBand<byte> currentRadii = tileState.CurrentMaximaRaster;
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
                for (int tileNextXindex = 1; tileNextXindex < currentSurface.SizeX; ++tileNextXindex)
                {
                    nextSurfaceZ = currentSurface[tileNextXindex, tileYindex];
                    hasNextSurfaceZ = currentSurface.IsNoData(nextSurfaceZ) == false;

                    if (hasSurfaceZ)
                    {
                        if (((hasPreviousSurfaceZ == false) || (previousSurfaceZ <= surfaceZ)) && ((hasNextSurfaceZ == false) || (nextSurfaceZ <= surfaceZ)))
                        {
                            if (GetLocalMaxima.TryGetLocalMaximaRadiusAndStatistics(tileNextXindex - 1, tileYindex, hasPreviousSurfaceZ, previousSurfaceZ, surfaceZ, hasNextSurfaceZ, nextSurfaceZ, tileState))
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
                        if (GetLocalMaxima.TryGetLocalMaximaRadiusAndStatistics(tileXIndex, tileYindex, hasPreviousSurfaceZ, previousSurfaceZ, surfaceZ, hasNextSurfaceZ, nextSurfaceZ, tileState))
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

                if (this.Stopping)
                {
                    return localMaximaFound; // no point continuing to next row
                }
            }

            return localMaximaFound;
        }

        protected override void ProcessRecord()
        {
            // setup
            int geopackageSqlBackgroundThreads = GdalCmdlet.EstimateGeopackageSqlBackgroundThreads();
            if (this.DataThreads <= geopackageSqlBackgroundThreads)
            {
                throw new ParameterOutOfRangeException(nameof(this.DataThreads), "-" + nameof(this.DataThreads) + " must be at least " + (geopackageSqlBackgroundThreads + 1) + " to allow for local maxima to be identified concurrent with " + geopackageSqlBackgroundThreads + (geopackageSqlBackgroundThreads == 1 ? " SQL thread." : " SQL threads."));
            }

            const string cmdletName = "Get-LocalMaxima";
            VirtualRaster<DigitalSurfaceModel> dsm = this.ReadVirtualRasterMetadata<DigitalSurfaceModel>(cmdletName, this.Dsm, (string dsmPrimaryBandFilePath) =>
            {
                return DigitalSurfaceModel.CreateFromPrimaryBandMetadata(dsmPrimaryBandFilePath, DigitalSurfaceModelBands.Primary | DigitalSurfaceModelBands.SourceIDSurface);
            }, this.cancellationTokenSource);
            Debug.Assert(dsm.TileGrid != null);

            bool localMaximaPathIsDirectory = GdalCmdlet.ValidateOrCreateOutputPath(this.LocalMaxima, dsm, nameof(this.Dsm), nameof(this.LocalMaxima));
            TileReadWriteStreaming<DigitalSurfaceModel, TileStreamPosition> maximaReadWrite = TileReadWriteStreaming.Create(dsm.TileGrid, localMaximaPathIsDirectory);

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
            int maxDataThreads = this.DataThreads - geopackageSqlBackgroundThreads;
            ParallelTasks findLocalMaximaTasks = new(Int32.Min(maxDataThreads, dsm.NonNullTileCount), () =>
            {
                LocalMaximaRaster? localMaximaRaster = null; // cache for reuse to offload GC
                for (int tileWriteIndex = maximaReadWrite.GetNextTileWriteIndexThreadSafe(); tileWriteIndex < maximaReadWrite.MaxTileIndex; tileWriteIndex = maximaReadWrite.GetNextTileWriteIndexThreadSafe())
                {
                    DigitalSurfaceModel? dsmTile = dsm[tileWriteIndex];
                    if (dsmTile == null) 
                    {
                        continue;
                    }

                    // assist in read until neighborhood complete or no more tiles left to read
                    if (maximaReadWrite.TryEnsureNeighborhoodRead(tileWriteIndex, dsm, this.cancellationTokenSource) == false)
                    {
                        Debug.Assert(this.cancellationTokenSource.IsCancellationRequested);
                        return; // reading was aborted
                    }

                    // create local maxima raster and vector tiles
                    string tileName = Tile.GetName(dsmTile.FilePath);
                    string rasterTilePath = maximaReadWrite.OutputPathIsDirectory ? Path.Combine(this.LocalMaxima, tileName + Constant.File.GeoTiffExtension) : this.LocalMaxima;
                    string vectorTilePath = PathExtensions.ReplaceExtension(rasterTilePath, Constant.File.GeoPackageExtension);

                    localMaximaRaster = LocalMaximaRaster.CreateRecreateOrReset(localMaximaRaster, dsmTile, rasterTilePath);
                    using DataSource localMaximaVector = OgrExtensions.CreateOrOpenForWrite(vectorTilePath);
                    LocalMaximaState tileState = new(tileName, dsm, tileWriteIndex, localMaximaRaster);

                    // find DSM maxima radii and statistics
                    int dsmMaximaFound;
                    using (LocalMaximaVector dsmMaximaPoints = LocalMaximaVector.CreateOrOverwrite(localMaximaVector, "localMaximaDsm", dsmTile, tileName))
                    {
                        // transactions stream to SQL thread for write as points are added
                        // A using block is needed as GDAL 3.10 doesn't support concurrent transactions on multiple layers, meaning the last DSM
                        // transaction needs to be committed before beginning CMM maxima location. (Same for CMM commit before starting CHM,
                        // below.)
                        // DSM is default raster output
                        tileState.CurrentMaximaVector = dsmMaximaPoints;
                        // DSM is default neighborhood
                        dsmMaximaFound = this.FindLocalMaxima(tileState);
                    }
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    // find CMM maxima radii
                    int cmmMaximaFound;
                    using (LocalMaximaVector cmmMaximaPoints = LocalMaximaVector.CreateOrOverwrite(localMaximaVector, "localMaximaCmm", dsmTile, tileName))
                    {
                        tileState.CurrentMaximaRaster = localMaximaRaster.CmmMaxima;
                        tileState.CurrentMaximaVector = cmmMaximaPoints;
                        tileState.CurrentNeighborhood = tileState.CmmNeighborhood;
                        cmmMaximaFound = this.FindLocalMaxima(tileState);
                    }
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    // find CHM maxima radii
                    int chmMaximaFound;
                    using (LocalMaximaVector chmMaximaPoints = LocalMaximaVector.CreateOrOverwrite(localMaximaVector, "localMaximaChm", dsmTile, tileName))
                    {
                        tileState.CurrentMaximaRaster = localMaximaRaster.ChmMaxima;
                        tileState.CurrentMaximaVector = chmMaximaPoints;
                        tileState.CurrentNeighborhood = tileState.ChmNeighborhood;
                        chmMaximaFound = this.FindLocalMaxima(tileState);
                    }
                    if (this.Stopping || this.cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    Debug.Assert(localMaximaRaster != null);
                    localMaximaRaster.Write(localMaximaRaster.FilePath, this.CompressRasters);

                    (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileWriteIndex);
                    lock (maximaReadWrite)
                    {
                        maximaReadWrite.OnTileWritten(tileIndexX, tileIndexY);
                        dsmMaximaTotal += dsmMaximaFound;
                        cmmMaximaTotal += cmmMaximaFound;
                        chmMaximaTotal += chmMaximaFound;
                    }
                }
            }, this.cancellationTokenSource);

            TimedProgressRecord progress = new(cmdletName, "Found local maxima in " + maximaReadWrite.TilesWritten + " of " + dsm.NonNullTileCount + " tiles (" + findLocalMaximaTasks.Count + "[+ " + geopackageSqlBackgroundThreads + (findLocalMaximaTasks.Count == 1 && geopackageSqlBackgroundThreads == 1 ? "] thread)..." : "] threads)..."));
            this.WriteProgress(progress);
            while (findLocalMaximaTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Found local maxima in " + maximaReadWrite.TilesWritten + " of " + dsm.NonNullTileCount + " tiles (" + findLocalMaximaTasks.Count + "[+" + geopackageSqlBackgroundThreads + (findLocalMaximaTasks.Count == 1 && geopackageSqlBackgroundThreads == 1 ? "] thread)..." : "] threads)...");
                progress.Update(maximaReadWrite.TilesWritten, dsm.NonNullTileCount);
                this.WriteProgress(progress);
            }

            // processing rates are constrained by GDAL load speeds but still overload the garbage collector
            // TODO: remove or reinstate Collect() after profiling: hopefully not needed with raster band pooling and vector transaction streaming
            //GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            //GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            long meanMaximaPerTile = dsmMaximaTotal / dsm.NonNullTileCount;
            progress.Stopwatch.Stop();
            this.WriteVerbose("Found " + dsmMaximaTotal.ToString("n0") + " DSM, " + cmmMaximaTotal.ToString("n0") + " CMM, and " + chmMaximaTotal.ToString("n0") + " CHM maxima within " + dsm.NonNullTileCount + " " + (dsm.NonNullTileCount > 1 ? "tiles" : "tile") + " in " + progress.Stopwatch.ToElapsedString() + " (" + meanMaximaPerTile.ToString("n0") + " DSM maxima/tile).");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            this.cancellationTokenSource.Cancel();
            base.StopProcessing();
        }

        private static bool TryGetLocalMaximaRadiusAndStatistics(int tileXindex, int tileYindex, bool hasPreviousSurfaceZ, float previousSurfaceZ, float surfaceZ, bool hasNextSurfaceZ, float nextSurfaceZ, LocalMaximaState tileState)
        {
            // check remainder of first ring: caller has checked xOffset = ±1, yOffset = 0
            RasterNeighborhood8<float> currentNeighborhood = tileState.CurrentNeighborhood;
            Span<float> maxRingZ = stackalloc float[LocalMaximaVector.RingsWithStatistics];
            Span<float> meanRingZ = stackalloc float[LocalMaximaVector.RingsWithStatistics];
            Span<float> minRingZ = stackalloc float[LocalMaximaVector.RingsWithStatistics];
            Span<float> varianceRingZ = stackalloc float[LocalMaximaVector.RingsWithStatistics];

            byte ringDataCells;
            float ringMinimumZ;
            float ringMaximumZ;
            float ringSumZ;
            float ringSumZsquared;
            if (hasPreviousSurfaceZ && hasNextSurfaceZ)
            {
                ringMaximumZ = previousSurfaceZ > nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                ringMinimumZ = previousSurfaceZ < nextSurfaceZ ? previousSurfaceZ : nextSurfaceZ;
                ringSumZ = ringMinimumZ + ringMaximumZ;
                ringSumZsquared = ringMinimumZ * ringMinimumZ + ringMaximumZ * ringMaximumZ;
                ringDataCells = 2;
            }
            else if (hasPreviousSurfaceZ)
            {
                ringMaximumZ = previousSurfaceZ;
                ringMinimumZ = previousSurfaceZ;
                ringSumZ = previousSurfaceZ;
                ringSumZsquared = previousSurfaceZ * previousSurfaceZ;
                ringDataCells = 1;
            }
            else if (hasNextSurfaceZ)
            {
                ringMaximumZ = nextSurfaceZ;
                ringMinimumZ = nextSurfaceZ;
                ringSumZ = nextSurfaceZ;
                ringSumZsquared = nextSurfaceZ * nextSurfaceZ;
                ringDataCells = 1;
            }
            else
            {
                ringMaximumZ = Single.MinValue;
                ringMinimumZ = Single.MaxValue;
                ringSumZ = 0.0F;
                ringSumZsquared = 0.0F;
                ringDataCells = 0;
            }

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
                            tileState.CurrentMaximaRaster[tileXindex, tileYindex] = 0;
                            return false;
                        }

                        ++ringDataCells;
                        if (z < ringMinimumZ)
                        {
                            ringMinimumZ = z;
                        }
                        if (z > ringMaximumZ)
                        {
                            ringMaximumZ = z;
                        }

                        ringSumZ += z;
                        ringSumZsquared += z * z;
                    }
                }
            }

            maxRingZ[0] = ringMaximumZ;
            minRingZ[0] = ringMinimumZ;
            if (ringDataCells > 0)
            {
                float ringDataCellsAsFloat = (float)ringDataCells;
                float ringMean = ringSumZ / ringDataCellsAsFloat;
                meanRingZ[0] = ringMean;
                // most rings are complete and thus do not need Bessel's 1/(N - 1) bias correction
                // For now, neglect bias correction for truncated rings at edge of available data. Without a complete neighborhood it can't
                // reliably be determined if a cell is a local maxima, making the statistical properties of edge effects most likely
                // unimportant.
                varianceRingZ[0] = ringSumZsquared / ringDataCellsAsFloat - ringMean * ringMean;
            }
            else
            {
                meanRingZ[0] = Single.NaN;
                varianceRingZ[0] = Single.NaN;
            }

            // local maxima radius is one minus the ring radius when a higher z value is encountered
            // Ring index is also one minus the ring radius so can just be returned as the local maxima radius.
            // Testing is done at progressively increasing radii for performance. It's most likely higher cells will be nearer, rather than
            // farther away, so working outwards tends to minimize the number of cells which need to be checked. Testing larger numbers of
            // adjacent cells in row major order may be more performant due to more effective use of cache lines (a 64 byte line holds 16
            // floats) but requires a more complex implementation tracking the minimum radius found along each y index. However, performance
            // investment here is well into diminishing returns as Get-LocalMaxima is limited by GDAL read speeds. DSM+CMM+CHM local maxima
            // identification runs at ~200 2000 x 2000 cell tiles per second (5950X).
            // Indices in rings are also sorted in row major order for performance.
            bool hasStatistics = tileState.CurrentMaximaVector != null;
            bool ringRadiusFound = false;
            byte ringRadiusInCells = (byte)Ring.Rings.Count; // default to no higher z value found within search radius
            for (byte ringIndex = 1; ringIndex < LocalMaximaVector.RingsWithStatistics; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];

                ringDataCells = 0;
                ringMinimumZ = Single.MaxValue;
                ringMaximumZ = Single.MinValue;
                ringSumZ = 0.0F;
                ringSumZsquared = 0.0F;
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

                        ++ringDataCells;
                        if (z < ringMinimumZ)
                        {
                            ringMinimumZ = z;
                        }
                        if (z > ringMaximumZ)
                        {
                            ringMaximumZ = z;
                        }

                        ringSumZ += z;
                        ringSumZsquared += z * z;
                    }
                }

                maxRingZ[ringIndex] = ringMaximumZ;
                minRingZ[ringIndex] = ringMinimumZ;
                if (ringDataCells > 0)
                {
                    float ringDataCellsAsFloat = (float)ringDataCells;
                    float ringMean = ringSumZ / ringDataCellsAsFloat;
                    meanRingZ[ringIndex] = ringMean;
                    varianceRingZ[ringIndex] = ringSumZsquared / ringDataCellsAsFloat - ringMean * ringMean;
                }
                else
                {
                    meanRingZ[ringIndex] = Single.NaN;
                    varianceRingZ[ringIndex] = Single.NaN;
                }
            }

            if (ringRadiusFound == false)
            {
                for (byte ringIndex = LocalMaximaVector.RingsWithStatistics; ringIndex < Ring.Rings.Count; ++ringIndex)
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

            tileState.CurrentMaximaRaster[tileXindex, tileYindex] = ringRadiusInCells;

            if (hasStatistics)
            {
                Debug.Assert((tileState.CurrentMaximaVector != null) && (tileState.DsmTile.SourceIDSurface != null)); // VS 17.8.7 nullability can't figure out hasStatistics == true means state.CurrentStatistics != null

                // surfaceZ is dsmZ, cmmZ, or chmHeight depending on current surface
                // Could therefore save one load by testing Object.ReferenceEquals(tileState.CurrentNeighborhood, tileState.DsmNeighborhood).
                (double x, double y) = tileState.DsmTile.Transform.GetCellCenter(tileXindex, tileYindex);
                UInt16 sourceID = tileState.DsmTile.SourceIDSurface[tileXindex, tileYindex];
                float dsmZ = tileState.DsmTile.Surface[tileXindex, tileYindex];
                float cmmZ = tileState.DsmTile.CanopyMaxima3[tileXindex, tileYindex];
                float chmHeight = tileState.DsmTile.CanopyHeight[tileXindex, tileYindex];
                tileState.CurrentMaximaVector.Add(sourceID, x, y, dsmZ, cmmZ, chmHeight, ringRadiusInCells, maxRingZ, meanRingZ, minRingZ, varianceRingZ);
            }

            return true;
        }

        private class LocalMaximaState
        {
            public RasterBand<byte> CurrentMaximaRaster { get; set; }
            public RasterNeighborhood8<float> CurrentNeighborhood { get; set; }
            public LocalMaximaVector? CurrentMaximaVector { get; set; }
            public RasterNeighborhood8<float> ChmNeighborhood { get; private init; }
            public RasterNeighborhood8<float> CmmNeighborhood { get; private init; }
            public RasterNeighborhood8<float> DsmNeighborhood { get; private init; }
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
                this.CurrentMaximaRaster = maximaRaster.DsmMaxima;
                this.CurrentMaximaVector = null;
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