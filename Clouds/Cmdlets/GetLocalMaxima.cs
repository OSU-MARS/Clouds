using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Segmentation;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "LocalMaxima")]
    public class GetLocalMaxima : GdalCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to compress output rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "1) path to a single digital surface model (DSM) raster to locate treetops within, 2) wildcarded path to a set of DSM tiles to process, or 3) path to a directory of DSM GeoTIFF files (.tif extension) to process. Each DSM must be a single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(HelpMessage = "Band number of DSM height values in surface raster. Default is 1.")]
        [ValidateRange(1, 100)] // arbitrary upper bound
        public int DsmBand { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "1) path to write local maxima radii to as as a raster or 2,3) path to a directory to local maxima tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? LocalMaxima { get; set; }

        public GetLocalMaxima() 
        {
            this.CompressRasters = false;
            // this.Dsm is mandatory
            this.DsmBand = 1;
            // this.LocalMaxima is mandatory
        }

        private static Raster<byte> FindLocalMaxima(VirtualRasterNeighborhood8<float> dsmNeighborhood)
        {
            RasterBand<float> dsmTile = dsmNeighborhood.Center;
            Raster<byte> localMaxima = new(dsmTile, [ "localMaximaRadiusInCells" ], Byte.MaxValue);
            RasterBand<byte> localMaximaTile = localMaxima.Bands[0];
            localMaximaTile.Name = "localMaximaRadius";

            byte ringsCount = (byte)Ring.Rings.Count;
            for (int tileYindex = 0; tileYindex < dsmTile.YSize; ++tileYindex) 
            {
                for (int tileXindex = 0; tileXindex < dsmTile.XSize; ++tileXindex) 
                {
                    float dsmZ = dsmTile[tileXindex, tileYindex];
                    if (dsmTile.IsNoData(dsmZ))
                    {
                        continue; // no z value for cell so local maxima radius is undefined; leave as no data
                    }

                    bool higherCellFound = false;
                    byte localMaximaRadius = ringsCount;
                    for (byte ringIndex = 0; ringIndex < ringsCount; ++ringIndex)
                    {
                        Ring ring = Ring.Rings[ringIndex];
                        for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                        {
                            int cellXindex = tileXindex + ring.XIndices[cellIndex];
                            int cellYindex = tileYindex + ring.YIndices[cellIndex];
                            if (dsmNeighborhood.TryGetValue(cellXindex, cellYindex, out float z))
                            {
                                if (z > dsmZ)
                                {
                                    higherCellFound = true;
                                    localMaximaRadius = ringIndex; // higher cell in this ring, so local maxima radius is ring index
                                    break;
                                }
                            }
                        }

                        if (higherCellFound)
                        {
                            break;
                        }
                    }

                    localMaximaTile[tileXindex, tileYindex] = localMaximaRadius;
                }
            }

            return localMaxima;
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.LocalMaxima != null));

            Stopwatch stopwatch = Stopwatch.StartNew();
            VirtualRaster<float> dsm = this.ReadVirtualRaster("Get-LocalMaxima", this.Dsm);
            int dsmBandIndex = this.DsmBand - 1;

            // find local maxima in all tiles
            string? mostRecentDsmTileName = null;
            int tilesCompleted = 0;
            if (dsm.TileCount == 1)
            {
                // single tile case
                VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8(0, dsmBandIndex);
                Raster<byte> localMaximaTile = GetLocalMaxima.FindLocalMaxima(dsmNeighborhood);

                string localMaximaTilePath = Directory.Exists(this.LocalMaxima) ? Path.Combine(this.LocalMaxima, dsm.TileNames[0] + Constant.File.GeoTiffExtension) : this.LocalMaxima;
                localMaximaTile.Write(localMaximaTilePath, this.CompressRasters);
                ++tilesCompleted;
            }
            else
            {
                // multi-tile case
                if (Directory.Exists(this.LocalMaxima) == false)
                {
                    throw new ParameterOutOfRangeException(nameof(this.LocalMaxima), "-" + nameof(this.LocalMaxima) + " must be an existing directory when -" + nameof(this.Dsm) + " indicates multiple files.");
                }

                // load all tiles
                // Assume read is from flash (NVMe, SSD) and there's no distinct constraint on the number of read threads or a data
                // locality advantage.
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = Int32.Min(this.MaxThreads, dsm.TileCount)
                };

                Task findLocalMaximaTask = Task.Run(() =>
                {
                    Parallel.For(0, dsm.TileCount, (int tileIndex) =>
                    {
                        VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8(tileIndex, dsmBandIndex);
                        Raster<byte> localMaximaTile = GetLocalMaxima.FindLocalMaxima(dsmNeighborhood);

                        string dsmTileName = dsm.TileNames[tileIndex];
                        string localMaximaTilePath = Path.Combine(this.LocalMaxima, dsmTileName + Constant.File.GeoTiffExtension);
                        localMaximaTile.Write(localMaximaTilePath, this.CompressRasters);
                        mostRecentDsmTileName = dsmTileName;
                    });
                });

                ProgressRecord progressRecord = new(0, "Get-LocalMaxima", "placeholder");
                while (findLocalMaximaTask.Wait(Constant.DefaultProgressInterval) == false)
                {
                    float fractionComplete = (float)tilesCompleted / (float)dsm.TileCount;
                    progressRecord.StatusDescription = mostRecentDsmTileName != null ? "Finding local maxima in " + mostRecentDsmTileName + "..." : "Finding treetops...";
                    progressRecord.PercentComplete = (int)(100.0F * fractionComplete);
                    progressRecord.SecondsRemaining = fractionComplete > 0.0F ? (int)Double.Round(stopwatch.Elapsed.TotalSeconds * (1.0F / fractionComplete - 1.0F)) : 0;
                    this.WriteProgress(progressRecord);
                }
            }

            stopwatch.Stop();
            string tileOrTiles = dsm.TileCount > 1 ? "tiles" : "tile";
            this.WriteVerbose(dsm.TileCount + " " + tileOrTiles + " in " + stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }
    }
}
