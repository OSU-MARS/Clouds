﻿using Mars.Clouds.Las;
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

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write image to as a GeoTIFF or 2,3) path to a directory to write image tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Image { get; set; }

        public GetOrthoimages() 
        {
            this.BitDepth = 16;
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
            (LasTileGrid lasGrid, int imageTileSizeX, int imageTileSizeY) = this.ReadLasHeadersAndCellSize(cmdletName, nameof(this.Image), imagePathIsDirectory);

            TileReadWrite<ImageRaster<UInt64>> imageReadWrite = new(this.MaxTiles, imageTileSizeX, imageTileSizeY, imagePathIsDirectory);

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
                orthoimageTasks[readThread] = Task.Run(() => this.ReadTiles(lasGrid, this.ReadTile, imageReadWrite), imageReadWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = readThreads; workerThread < orthoimageTasks.Length; ++workerThread)
            {
                orthoimageTasks[workerThread] = Task.Run(() => this.WriteTiles<ImageRaster<UInt64>, TileReadWrite<ImageRaster<UInt64>>>(this.WriteTile, imageReadWrite), imageReadWrite.CancellationTokenSource.Token);
            }

            this.WaitForTasks(cmdletName, orthoimageTasks, lasGrid, imageReadWrite);

            string elapsedTimeFormat = imageReadWrite.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Found brightnesses of " + imageReadWrite.CellsWritten.ToString("#,#,#,0") + " pixels in " + imageReadWrite.TilesLoaded + " LAS tiles in " + imageReadWrite.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (imageReadWrite.TilesWritten / imageReadWrite.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        private ImageRaster<UInt64> ReadTile(LasTile lasTile, TileReadWrite<ImageRaster<UInt64>> imageReadWrite)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
            ImageRaster<UInt64> imageTile = new(lasTile.GetSpatialReference(), lasTileTransform, imageReadWrite.TileSizeX, imageReadWrite.TileSizeY, UInt64.MaxValue);
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToImage(lasTile, imageTile);

            return imageTile;
        }

        private int WriteTile(string tileName, ImageRaster<UInt64> imageTile, TileReadWrite<ImageRaster<UInt64>> imageReadWrite)
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
                    ImageRaster<UInt16> imageTile16 = imageTile.AsUInt16();
                    imageTile16.Write(imageTilePath, this.CompressRasters);
                    break;
                case 32:
                    ImageRaster<UInt32> imageTile32 = imageTile.AsUInt32();
                    imageTile32.Write(imageTilePath, this.CompressRasters); // 32 bit integer deflate compression appears to be somewhat expensive
                    break;
                default:
                    throw new NotSupportedException("Unhandled depth of " + this.BitDepth + " bits per channel.");
            }

            return imageTile.Cells;
        }
    }
}
