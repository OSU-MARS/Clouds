using Mars.Clouds.Las;
using System.Diagnostics;
using System;
using System.Management.Automation;
using System.Threading.Tasks;
using Mars.Clouds.GdalExtensions;
using System.IO;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Orthoimages")]
    public class GetOrthoimages : LasTilesToTilesCmdlet
    {
        [Parameter(HelpMessage = "Number of unsigned bits per channel in output tiles. Can be 16, 32, or 64. Default is 16, which is likely to occasionally result in points' RGB, NIR, and possibly intensity values of 65535 being reduced to 65534 to disambiguate them from no data values.")]
        [ValidateRange(16, 64)] // could also use [ValidateSet] but string conversion is required
        public int BitDepth { get; set; }

        [Parameter(HelpMessage = "Size of an orthoimage pixel in the point clouds' CRS units. Must be an integer multiple of the tile size. Default is 0.5 m for metric point clouds and 1.5 feet for point clouds with English units.")]
        public double CellSize { get; set; }

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write image to as a GeoTIFF or 2,3) path to a directory to write image tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Image { get; set; }

        public GetOrthoimages() 
        {
            this.BitDepth = 16;
            this.CellSize = -1.0;
            this.MaxThreads = 8; // for now, default to a single read thread and seven write threads
        }

        protected override void ProcessRecord() 
        {
            if ((this.BitDepth != 16) && (this.BitDepth != 32) && (this.BitDepth != 64))
            {
                throw new ParameterOutOfRangeException(nameof(this.BitDepth), this.BitDepth + " bit depth is not supported. Bit depth must be 16, 32, or 64 bits per channel.");
            }
            Debug.Assert(this.Image != null);
            if (this.MaxThreads < 8)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least eight.");
            }

            string cmdletName = "Get-Orthoimages";
            bool imagePathIsDirectory = Directory.Exists(this.Image);
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName, nameof(this.Image), imagePathIsDirectory);

            (int imageTileSizeX, int imageTileSizeY) = this.SetCellSize(lasGrid);
            ImageTileReadWrite imageReadWrite = new(this.MaxPointTiles, imageTileSizeX, imageTileSizeY, imagePathIsDirectory);

            // start single reader and multiple writers
            // Profiling is desirable for tuning here. For Int32 writes through GDAL 3.8.3 a single read thread from a 3.5 drive at
            // ~260 MB/s creates cyclic activity across typically 3-6 write threads. Using seven writers is nominally overkill but provides
            // compute margin so the 3.5 stays fully utilized. For faster source devices (NVMe, SSD, RAID) multiple reader threads might
            // needed, though read thread unbinding appears likely to permit ~1:10 read:write thread ratios. For now, if higher thread
            // counts are permitted only additional write threads are created.
            // TODO: support writing uncompressed Int32 images as they're likely to be transcoded to UInt16
            Task[] orthoimageTasks = new Task[this.MaxThreads];
            int readThreads = 1; // for now; see above
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                orthoimageTasks[readThread] = Task.Run(() => this.ReadLasTiles(lasGrid, this.ReadTile, imageReadWrite), imageReadWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = readThreads; workerThread < orthoimageTasks.Length; ++workerThread)
            {
                orthoimageTasks[workerThread] = Task.Run(() => this.WriteTiles<ImageRaster<UInt64>, ImageTileReadWrite>(this.WriteTile, imageReadWrite), imageReadWrite.CancellationTokenSource.Token);
            }

            TimedProgressRecord progress = this.WaitForLasReadTileWriteTasks(cmdletName, orthoimageTasks, lasGrid, imageReadWrite);

            progress.Stopwatch.Stop();
            string elapsedTimeFormat = progress.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Found brightnesses of " + imageReadWrite.CellsWritten.ToString("n0") + " pixels in " + imageReadWrite.TilesRead + " point cloud tiles in " + progress.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (imageReadWrite.TilesWritten / progress.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        private ImageRaster<UInt64> ReadTile(LasTile lasTile, ImageTileReadWrite imageReadWrite)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
            ImageRaster<UInt64> imageTile = new(lasTile.GetSpatialReference(), lasTileTransform, imageReadWrite.TileSizeX, imageReadWrite.TileSizeY, lasTile.Header.PointsHaveNearInfrared());
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToImage(lasTile, imageTile);

            return imageTile;
        }

        private (int tileSizeX, int tileSizeY) SetCellSize(LasTileGrid lasGrid)
        {
            if (this.CellSize < 0.0)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.CellSize = crsLinearUnits == 1.0 ? 0.5 : 1.5; // 0.5 m or 1.5 feet
            }

            int outputTileSizeX = (int)(lasGrid.Transform.CellWidth / this.CellSize);
            if (lasGrid.Transform.CellWidth - outputTileSizeX * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }
            int outputTileSizeY = (int)(Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize);
            if (Double.Abs(lasGrid.Transform.CellHeight) - outputTileSizeY * this.CellSize != 0.0)
            {
                string units = lasGrid.Crs.GetLinearUnitsName();
                throw new ParameterOutOfRangeException(nameof(this.CellSize), "Point cloud tile grid pitch of " + lasGrid.Transform.CellWidth + " x " + lasGrid.Transform.CellHeight + " is not an integer multiple of the " + this.CellSize + " " + units + " output cell size.");
            }

            return (outputTileSizeX, outputTileSizeY);
        }

        private int WriteTile(string tileName, ImageRaster<UInt64> imageTile, ImageTileReadWrite imageReadWrite)
        {
            Debug.Assert(this.Image != null);

            // do normalization and no data setting off deserialization thread for throughput
            imageTile.OnPointAdditionComplete();

            // convert from 64 bit accumulation to shallower bit depth for more appropriate disk size
            // UInt16 would be preferred but isn't supported by GDAL 3.8.3 C# bindings
            string imageTilePath = imageReadWrite.OutputPathIsDirectory ? Path.Combine(this.Image, tileName + Constant.File.GeoTiffExtension) : this.Image;
            switch (this.BitDepth)
            {
                case 16:
                    // will likely fail since source data is UInt16; divide by two to avoid Int16 overflows?
                    ImageRaster<UInt16> imageTile16 = imageTile.PackToUInt16();
                    imageTile16.Write(imageTilePath, this.CompressRasters);
                    break;
                case 32:
                    ImageRaster<UInt32> imageTile32 = imageTile.PackToUInt32();
                    imageTile32.Write(imageTilePath, this.CompressRasters); // 32 bit integer deflate compression appears to be somewhat expensive
                    break;
                default:
                    throw new NotSupportedException("Unhandled depth of " + this.BitDepth + " bits per channel.");
            }

            return imageTile.Cells;
        }

        private class ImageTileReadWrite : TileReadWrite<ImageRaster<UInt64>>
        {
            public int TileSizeX { get; private init; }
            public int TileSizeY { get; private init; }

            public ImageTileReadWrite(int maxSimultaneouslyLoadedTiles, int tileSizeX, int tileSizeY, bool outputPathIsDirectory)
                : base(maxSimultaneouslyLoadedTiles, outputPathIsDirectory)
            {
                this.TileSizeX = tileSizeX;
                this.TileSizeY = tileSizeY;
            }
        }
    }
}
