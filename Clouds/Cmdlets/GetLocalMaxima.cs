using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
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
        [ValidateNotNullOrWhiteSpace]
        public string? Dsm { get; set; }

        [Parameter(HelpMessage = "Name of band with DSM height values in surface raster. Default is \"dsm\".")]
        public string? DsmBand { get; set; }

        [Parameter(Mandatory = true, Position = 2, HelpMessage = "1) path to write local maxima radii to as as a raster or 2,3) path to a directory to local maxima tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? LocalMaxima { get; set; }

        public GetLocalMaxima() 
        {
            this.CompressRasters = false;
            // this.Dsm is mandatory
            this.DsmBand = "dsm";
            // this.LocalMaxima is mandatory
        }

        private static Raster<byte> FindLocalMaxima(VirtualRasterNeighborhood8<float> dsmNeighborhood)
        {
            RasterBand<float> dsmTile = dsmNeighborhood.Center;
            Raster<byte> localMaxima = new(dsmTile, [ "localMaximaRadiusInCells" ], Byte.MaxValue);
            RasterBand<byte> localMaximaTile = localMaxima.Bands[0];
            localMaximaTile.Name = "localMaximaRadius";

            byte ringsCount = (byte)Ring.Rings.Count;
            for (int tileYindex = 0; tileYindex < dsmTile.SizeY; ++tileYindex) 
            {
                for (int tileXindex = 0; tileXindex < dsmTile.SizeX; ++tileXindex) 
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
            VirtualRaster<Raster<float>> dsm = this.ReadVirtualRaster<Raster<float>>("Get-LocalMaxima", this.Dsm);

            // find local maxima in all tiles
            string? mostRecentDsmTileName = null;
            int tilesCompleted = 0;
            if (dsm.TileCount == 1)
            {
                // single tile case
                VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileGridIndexX: 0, tileGridIndexY: 0, this.DsmBand);
                Raster<byte> localMaximaTile = GetLocalMaxima.FindLocalMaxima(dsmNeighborhood);

                string localMaximaTilePath = Directory.Exists(this.LocalMaxima) ? Path.Combine(this.LocalMaxima, dsm.GetTileName(0, 0) + Constant.File.GeoTiffExtension) : this.LocalMaxima;
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
                VirtualRasterEnumerator<Raster<float>> tileEnumerator = dsm.GetEnumerator();
                ParallelTasks findLocalMaximaTasks = new(Int32.Min(this.MaxThreads, dsm.TileCount), () =>
                {
                    while (tileEnumerator.MoveNextThreadSafe())
                    {
                        (int tileIndexX, int tileIndexY) = tileEnumerator.GetGridIndices();
                        VirtualRasterNeighborhood8<float> dsmNeighborhood = dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.DsmBand);
                        Raster<byte> localMaximaTile = GetLocalMaxima.FindLocalMaxima(dsmNeighborhood);

                        string dsmTileName = Tile.GetName(tileEnumerator.Current.FilePath);
                        string localMaximaTilePath = Path.Combine(this.LocalMaxima, dsmTileName + Constant.File.GeoTiffExtension);
                        localMaximaTile.Write(localMaximaTilePath, this.CompressRasters);
                        mostRecentDsmTileName = dsmTileName;
                    }
                });

                TimedProgressRecord progress = new("Get-LocalMaxima", "placeholder");
                while (findLocalMaximaTasks.WaitAll(Constant.DefaultProgressInterval) == false)
                {
                    progress.StatusDescription = mostRecentDsmTileName != null ? "Finding local maxima in " + mostRecentDsmTileName + "..." : "Finding treetops...";
                    progress.Update(tilesCompleted, dsm.TileCount);
                    this.WriteProgress(progress);
                }
            }

            string tileOrTiles = dsm.TileCount > 1 ? "tiles" : "tile";
            this.WriteVerbose(dsm.TileCount + " " + tileOrTiles + " in " + stopwatch.ToElapsedString() + ".");
            base.ProcessRecord();
        }
    }
}
