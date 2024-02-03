using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Reflection.Metadata;
using System.Threading.Tasks;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Dsm")]
    public class GetDsm : LasTilesToTilesCmdlet
    {
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "1) path to write DSM to as a GeoTIFF or 2,3) path to a directory to write DSM tiles to.")]
        [ValidateNotNullOrEmpty]
        public string? Dsm { get; set; }

        [Parameter(HelpMessage = "Isolation distance beyond which topmost points are declared high noise or outliers and excluded from DSM, has no effect if -UpperPoints is 1 and is ignored if -WriteUpperPoints is set. Default is 15 m for metric point clouds and 50 feet for point clouds with English units. Currently, rejection does not consider a cell's neighbors so, if a DSM cell has a single point, that point is always used.")]
        [ValidateRange(0.0F, 500.0F)] // arbitrary upper bound
        public float Isolation { get; set; }

        [Parameter(HelpMessage = "Number of uppermost points to track in each cell to enable outlier rejection. Default is 10 points, meaning up to the nine uppermost points can be declared isolated and excluded from the DSM.")]
        [ValidateRange(1, 100)] // arbitrary upper bound
        public int UpperPoints { get; set; }

        [Parameter(HelpMessage = "If set, write a DSM raster with -UpperPoints containing the sorted points in each cell.")]
        public SwitchParameter WriteUpperPoints { get; set; }

        public GetDsm()
        {
            // this.Dsm is mandatory
            this.Isolation = -1.0F;
            this.MaxThreads = 2;
            this.UpperPoints = 10;
            this.WriteUpperPoints = false;
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Dsm != null);
            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            (LasTileGrid lasGrid, int dsmTileSizeX, int dsmTileSizeY) = this.ReadLasHeadersAndCellSize(nameof(this.Dsm), dsmPathIsDirectory);
            if (this.Isolation < 0.0F)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.Isolation = crsLinearUnits == 1.0 ? 15.0F : 50.0F; // 15 m or 50 feet
            }
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            TileReadWrite<PointGridZ> dsmReadWrite = new(this.MaxTiles, dsmTileSizeX, dsmTileSizeY, dsmPathIsDirectory);
            Task[] dsmTasks = new Task[this.MaxThreads];
            int readThreads = this.MaxThreads / 2;
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                dsmTasks[readThread] = Task.Run(() => this.ReadTiles(lasGrid, this.ReadTile, dsmReadWrite), dsmReadWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = readThreads; workerThread < dsmTasks.Length; ++workerThread)
            {
                dsmTasks[workerThread] = Task.Run(() => this.WriteTiles(this.WriteTile, dsmReadWrite), dsmReadWrite.CancellationTokenSource.Token);
            }

            this.WaitForTasks("Get-Dsm", dsmTasks, lasGrid, dsmReadWrite);

            string elapsedTimeFormat = dsmReadWrite.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Found DSM elevations for " + dsmReadWrite.CellsWritten.ToString("#,#,#,0") + " cells from " + lasGrid.NonNullCells + " LAS tiles in " + dsmReadWrite.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (dsmReadWrite.TilesWritten / dsmReadWrite.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        private PointGridZ ReadTile(LasTile lasTile, TileReadWrite<PointGridZ> dsmReadWrite)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
            PointGridZ dsmTilePointZ = new(lasTile.GetSpatialReference(), lasTileTransform, dsmReadWrite.TileSizeX, dsmReadWrite.TileSizeY, this.UpperPoints);
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadUpperPointsToGrid(lasTile, dsmTilePointZ);

            return dsmTilePointZ;
        }

        private int WriteTile(string tileName, PointGridZ dsmTilePointZ, TileReadWrite<PointGridZ> dsmReadWrite)
        {
            Debug.Assert(this.Dsm != null);

            Raster<float> dsmTile;
            if (this.WriteUpperPoints)
            {
                dsmTile = dsmTilePointZ.GetUpperPoints();
            }
            else
            {
                dsmTile = dsmTilePointZ.GetDigitalSurfaceModel(this.Isolation);
            }

            string dsmTilePath = dsmReadWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
            dsmTile.Write(dsmTilePath);

            return dsmTile.CellsPerBand;
        }
    }
}
