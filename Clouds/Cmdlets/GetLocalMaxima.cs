using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using Mars.Clouds.Segmentation;
using OSGeo.OGR;
using System;
using System.Collections.Generic;
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

        [Parameter(HelpMessage = "Name of bands with DSM height values in surface raster. Default is [ \"dsm\", \"cmm3\", \"chm\" ].")]
        public List<string> DsmBands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to a directory containing DTM tiles whose file names match the point cloud tiles. Each DTM must be a single precision floating point raster with ground surface heights in the same CRS as the point cloud tiles.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating mean ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(Position = 2, HelpMessage = "1) path to write local maxima as a raster or a vector or 2,3) path to a directory to local maxima raster and vector tiles to.")]
        [ValidateNotNullOrEmpty]
        public string LocalMaxima { get; set; }

        public GetLocalMaxima() 
        {
            this.CompressRasters = false;
            this.Dsm = String.Empty;
            this.DsmBands = [ "dsm", "cmm3", "chm" ];
            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.LocalMaxima = String.Empty;
        }

        private static void FindLocalMaxima(LocalMaximaState state)
        {
            VirtualRasterNeighborhood8<float> dsmNeighborhood = state.DsmNeighborhood;
            RasterBand<float> dsmTile = state.DsmTile;
            RasterBand<byte> maximaRadii = state.MaximaRadii;
            for (int tileYindex = 0; tileYindex < dsmTile.SizeY; ++tileYindex)
            {
                // test cells 0..n-1 in row
                // Since ~95% of cells are not local maxima at any radius, test neighbors in same row before calling GetLocalMaximaRadius() for more
                // extensive checking. Similarly, GetLocalMaximaRadius() checks the remaining six neighbors (queen adjacency) before testing at larger
                // radii.
                bool hasPreviousDsmZ = dsmNeighborhood.TryGetValue(-1, tileYindex, out float previousDsmZ);
                float dsmZ = dsmTile[0, tileYindex];
                bool hasDsmZ = dsmTile.IsNoData(dsmZ) == false;
                float nextDsmZ;
                bool hasNextDsmZ;
                for (int tileNextXindex = 1; tileNextXindex < dsmTile.SizeX - 1; ++tileNextXindex)
                {
                    nextDsmZ = dsmTile[tileNextXindex, tileYindex];
                    hasNextDsmZ = dsmTile.IsNoData(nextDsmZ) == false;

                    if (hasDsmZ)
                    {
                        if (((hasPreviousDsmZ == false) || (previousDsmZ <= dsmZ)) && ((hasNextDsmZ == false) || (nextDsmZ <= dsmZ)))
                        {
                            GetLocalMaxima.GetLocalMaximaRadiusAndStatistics(tileNextXindex - 1, tileYindex, dsmZ, state);
                        }
                        else
                        {
                            maximaRadii[tileNextXindex - 1, tileYindex] = 0; // not a local maximum as either previous or next z is higher
                            // not a maxima so nothing for vector output
                        }
                    } // else no z value for cell so local maxima radius is undefined; leave as no data

                    // advance
                    previousDsmZ = dsmZ;
                    hasPreviousDsmZ = hasDsmZ;
                    dsmZ = nextDsmZ;
                    hasDsmZ = hasNextDsmZ;
                }

                // last cell in row
                if (hasDsmZ)
                {
                    hasNextDsmZ = dsmNeighborhood.TryGetValue(dsmTile.SizeX, tileYindex, out nextDsmZ);
                    int tileXIndex = dsmTile.SizeX - 1;
                    if (((hasPreviousDsmZ == false) || (previousDsmZ <= dsmZ)) && ((hasNextDsmZ == false) || (nextDsmZ <= dsmZ)))
                    {
                        GetLocalMaxima.GetLocalMaximaRadiusAndStatistics(tileXIndex, tileYindex, dsmZ, state);
                    }
                    else
                    {
                        maximaRadii[tileXIndex, tileYindex] = 0;
                        // not a maxima so nothing for vector output
                    }
                } // else no z value for cell so local maxima radius is undefined; leave as no data
            }
        }

        private static void GetLocalMaximaRadiusAndStatistics(int tileXindex, int tileYindex, float dsmZ, LocalMaximaState state)
        {
            // check remainder of first ring: caller has checked xOffset = ±1, yOffset = 0
            VirtualRasterNeighborhood8<float> dsmNeighborhood = state.DsmNeighborhood;
            for (int yOffset = -1; yOffset < 2; yOffset += 2)
            {
                int cellYindex = tileYindex + yOffset;
                for (int xOffset = -1; xOffset < 2; ++xOffset)
                {
                    int cellXindex = tileXindex + xOffset;
                    if (dsmNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                    {
                        if (z > dsmZ)
                        {
                            state.MaximaRadii[tileXindex, tileYindex] = 0;
                            return;
                        }
                    }
                }
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
            Span<float> maxRingZ = stackalloc float[LocalMaximaLayer.RingCount];
            Span<float> minRingZ = stackalloc float[LocalMaximaLayer.RingCount];
            bool ringRadiusFound = false;
            byte ringRadiusInCells = (byte)Ring.Rings.Count; // default to no higher z value found within search radius
            for (byte ringIndex = 1; ringIndex < LocalMaximaLayer.RingCount; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];
                float ringMinimumZ = Single.MaxValue;
                float ringMaximumZ = Single.MinValue;

                for (int offsetIndex = 0; offsetIndex < ring.Count; ++offsetIndex)
                {
                    int cellYindex = tileYindex + ring.XIndices[offsetIndex];
                    int cellXindex = tileXindex + ring.YIndices[offsetIndex];
                    if (dsmNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                    {
                        if (z > dsmZ)
                        {
                            ringRadiusInCells = ringIndex;
                            ringRadiusFound = true;
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
                for (byte ringIndex = LocalMaximaLayer.RingCount; ringIndex < Ring.Rings.Count; ++ringIndex)
                {
                    Ring ring = Ring.Rings[ringIndex];
                    for (int offsetIndex = 0; offsetIndex < ring.Count; ++offsetIndex)
                    {
                        int cellYindex = tileYindex + ring.XIndices[offsetIndex];
                        int cellXindex = tileXindex + ring.YIndices[offsetIndex];
                        if (dsmNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                        {
                            if (z > dsmZ)
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

            state.MaximaRadii[tileXindex, tileYindex] = ringRadiusInCells;

            (double x, double y) = state.DsmTile.Transform.GetCellCenter(tileXindex, tileYindex);
            float elevation = state.DtmTile[tileXindex, tileYindex];
            state.MaximaStatistics.Add(x, y, elevation, dsmZ, ringRadiusInCells, maxRingZ, minRingZ);
        }

        protected override void ProcessRecord()
        {
            // setup
            Stopwatch stopwatch = Stopwatch.StartNew();
            VirtualRaster<Raster<float>> dsm = this.ReadVirtualRaster<Raster<float>>("Get-LocalMaxima", this.Dsm);
            TileReadWrite tileReadWrite = new(Directory.Exists(this.LocalMaxima))
            {
                TilesRead = dsm.TileCount
            };
            if ((dsm.TileCount > 1) && (tileReadWrite.OutputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(nameof(this.LocalMaxima), "-" + nameof(this.LocalMaxima) + " must be an existing directory when -" + nameof(this.Dsm) + " indicates multiple files.");
            }

            string[] localMaximaBandNames = new string[this.DsmBands.Count];
            for (int bandIndex = 0; bandIndex < this.DsmBands.Count; ++bandIndex)
            {
                string bandName = this.DsmBands[bandIndex];
                if (dsm.BandNames.Contains(bandName) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.DsmBands), "-" + nameof(this.DsmBands) + " includes band '" + bandName + "' but this band is not present in DSM '" + this.Dsm + "'.");
                }

                localMaximaBandNames[bandIndex] = bandName + "LocalMaximaRadiusInCells";
            }

            // find local maxima in all tiles
            // Assume read is from flash (NVMe, SSD) and there's no distinct constraint on the number of read threads or a data
            // locality advantage.
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            ParallelTasks findLocalMaximaTasks = new(Int32.Min(this.MaxThreads, dsm.TileCount), () =>
            {
                Raster<byte>? localMaximaRaster = null; // cache for reuse to offload GC
                RasterBand<float>? dtmTile = null;
                for (int tileIndex = tileReadWrite.GetNextTileWriteIndexThreadSafe(); tileIndex < dsm.TileCount; tileIndex = tileReadWrite.GetNextTileWriteIndexThreadSafe())
                {
                    Raster<float>? dsmTile = dsm[tileIndex];
                    if (dsmTile == null) 
                    {
                        continue;
                    }

                    // load DTM tile
                    string tileName = Tile.GetName(dsmTile.FilePath);
                    string dtmTilePath = dtmPathIsDirectory ? LasTilesCmdlet.GetRasterTilePath(this.Dtm, tileName) : this.Dtm;
                    dtmTile = RasterBand<float>.CreateOrLoad(dtmTilePath, this.DtmBand, dtmTile);

                    // create local maxima raster and vector tiles
                    localMaximaRaster = Raster<byte>.CreateRecreateOrReset(localMaximaRaster, dsmTile, localMaximaBandNames, Byte.MaxValue);
                    localMaximaRaster.FilePath = this.LocalMaxima;
                    if (tileReadWrite.OutputPathIsDirectory)
                    {
                        localMaximaRaster.FilePath = Path.Combine(this.LocalMaxima, tileName + Constant.File.GeoTiffExtension);
                    }

                    string vectorTilePath = this.LocalMaxima;
                    if (tileReadWrite.OutputPathIsDirectory)
                    {
                        vectorTilePath = Path.Combine(vectorTilePath, tileName + Constant.File.GeoPackageExtension);
                    }
                    using DataSource localMaximaVector = OgrExtensions.Open(vectorTilePath);

                    (int tileIndexX, int tileIndexY) = dsm.ToGridIndices(tileIndex);
                    for (int bandIndex = 0; bandIndex < this.DsmBands.Count; ++bandIndex)
                    {
                        string bandName = this.DsmBands[bandIndex];
                        VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, bandName);

                        RasterBand<byte> maximaRadii = localMaximaRaster.Bands[bandIndex];
                        using LocalMaximaLayer maximaPoints = LocalMaximaLayer.CreateOrOverwrite(localMaximaVector, dsmTile, tileName);

                        LocalMaximaState state = new(tileName, dsmNeighborhood, dtmTile, maximaRadii, maximaPoints);
                        GetLocalMaxima.FindLocalMaxima(state);

                        if (this.Stopping || tileReadWrite.CancellationTokenSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    Debug.Assert(localMaximaRaster != null);
                    localMaximaRaster.Write(localMaximaRaster.FilePath, this.CompressRasters);
                    tileReadWrite.IncrementTilesWrittenThreadSafe();
                }
            });

            TimedProgressRecord progress = new("Get-LocalMaxima", "Found local maxima in " + tileReadWrite.TilesWritten + " of " + dsm.TileCount + " tiles...");
            this.WriteProgress(progress);
            while (findLocalMaximaTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Found local maxima in " + tileReadWrite.TilesWritten + " of " + dsm.TileCount + " tiles...";
                progress.Update(tileReadWrite.TilesWritten, dsm.TileCount);
                this.WriteProgress(progress);
            }

            // processing rates are constrained by GDAL load speeds but still overload the garbage collector
            // 5950X 16 thread read speed is ~1.8 GB/s (SN770 on CPU lanes).
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            this.WriteVerbose(dsm.TileCount + " " + (dsm.TileCount > 1 ? "tiles" : "tile") + " in " + stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }

        private class LocalMaximaState
        {
            public VirtualRasterNeighborhood8<float> DsmNeighborhood { get; private init; }
            public RasterBand<float> DsmTile { get; private init; }
            public RasterBand<float> DtmTile { get; private init; }
            public RasterBand<byte> MaximaRadii { get; private init; }
            public LocalMaximaLayer MaximaStatistics { get; private init; }
            public string TileName { get; private init; }

            public LocalMaximaState(string tileName, VirtualRasterNeighborhood8<float> dsmNeighborhood, RasterBand<float> dtmTile, RasterBand<byte> maximaRadii, LocalMaximaLayer maximaStatistics)
            {
                this.DsmNeighborhood = dsmNeighborhood;
                this.DsmTile = dsmNeighborhood.Center;
                this.DtmTile = dtmTile;
                this.MaximaRadii = maximaRadii;
                this.MaximaStatistics = maximaStatistics;
                this.TileName = tileName;
            }
        }
    }
}
