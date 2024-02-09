using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
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

        [Parameter(Mandatory = true, HelpMessage = "1) path to a single digital terrain model (DTM) raster to estimate DSM height above ground from or 2,3) path to a directory containing DTM tiles whose file names match the DSM tiles. Each DSM must be a  single band, single precision floating point raster whose band contains surface heights in its coordinate reference system's units.")]
        [ValidateNotNullOrEmpty]
        public string? Dtm { get; set; }

        [Parameter(HelpMessage = "Number of DTM band to use in calculating mean ground elevations. Default is 1 (the first band).")]
        [ValidateRange(1, 500)] // arbitrary upper bound
        public int DtmBand { get; set; }

        [Parameter(HelpMessage = "Vertical distance beyond which groups of points are considered distinct layers. Default is 10 m for metric point clouds and 30 feet for point clouds with English units.")]
        [ValidateRange(0.0F, 500.0F)] // arbitrary upper bound
        public float LayerSeparation { get; set; }

        [Parameter(HelpMessage = "Maximum number of uppermost points to track in each cell for diagnostics. Default is zero.")]
        [ValidateRange(0, 100)] // arbitrary upper bound
        public int UpperPoints { get; set; }

        public GetDsm()
        {
            // this.Dsm is mandatory
            // this.Dtm is mandatory
            this.DtmBand = 1;
            this.LayerSeparation = -1.0F;
            // leave this.MaxThreads at default for DTM read
            this.UpperPoints = 0;
        }

        protected override void ProcessRecord()
        {
            Debug.Assert((this.Dsm != null) && (this.Dtm != null));
            if (this.MaxThreads < 2)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxThreads), "-" + nameof(this.MaxThreads) + " must be at least two.");
            }

            bool dsmPathIsDirectory = Directory.Exists(this.Dsm);
            (LasTileGrid lasGrid, int dsmTileSizeX, int dsmTileSizeY) = this.ReadLasHeadersAndCellSize(nameof(this.Dsm), dsmPathIsDirectory);
            VirtualRaster<float> dtm = this.ReadVirtualRaster("Get-Dsm", this.Dtm);
            if (SpatialReferenceExtensions.IsSameCrs(lasGrid.Crs, dtm.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasGrid.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }

            if (this.LayerSeparation < 0.0F)
            {
                double crsLinearUnits = lasGrid.Crs.GetLinearUnits();
                this.LayerSeparation = crsLinearUnits == 1.0 ? 10.0F : 30.0F; // 10 m or 30 feet
            }

            VirtualRaster<float> dsm = dtm.CreateEmptyCopy();
            DsmReadWrite dsmReadWrite = new(dsm, dtm, this.MaxTiles, dsmPathIsDirectory);
            Task[] dsmTasks = new Task[2];
            int readThreads = dsmTasks.Length / 2;
            for (int readThread = 0; readThread < readThreads; ++readThread)
            {
                dsmTasks[readThread] = Task.Run(() => this.ReadTiles(lasGrid, this.ReadTile, dsmReadWrite), dsmReadWrite.CancellationTokenSource.Token);
            }
            for (int workerThread = readThreads; workerThread < dsmTasks.Length; ++workerThread)
            {
                dsmTasks[workerThread] = Task.Run(() => this.WriteTiles<Grid<PointListZ>, DsmReadWrite>(this.WriteTile, dsmReadWrite), dsmReadWrite.CancellationTokenSource.Token);
            }

            this.WaitForTasks("Get-Dsm", dsmTasks, lasGrid, dsmReadWrite);

            string elapsedTimeFormat = dsmReadWrite.Stopwatch.Elapsed.TotalHours > 1.0 ? "h\\:mm\\:ss" : "mm\\:ss";
            this.WriteVerbose("Found DSM elevations for " + dsmReadWrite.CellsWritten.ToString("#,#,#,0") + " cells from " + lasGrid.NonNullCells + " LAS tiles in " + dsmReadWrite.Stopwatch.Elapsed.ToString(elapsedTimeFormat) + ": " + (dsmReadWrite.TilesWritten / dsmReadWrite.Stopwatch.Elapsed.TotalSeconds).ToString("0.0") + " tiles/s.");
            base.ProcessRecord();
        }

        private Grid<PointListZ> ReadTile(LasTile lasTile, DsmReadWrite dsmReadWrite)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, this.CellSize, this.CellSize);
            Grid<PointListZ> tilePointZ = new(lasTile.GetSpatialReference(), lasTileTransform, dsmReadWrite.TileSizeX, dsmReadWrite.TileSizeY);
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToGrid(lasTile, tilePointZ, dropGroundPoints: true);

            return tilePointZ;
        }

        private int WriteTile(string tileName, Grid<PointListZ> dsmTilePointZ, DsmReadWrite dsmReadWrite)
        {
            Debug.Assert(this.Dsm != null);

            (double tileCenterX, double tileCenterY) = dsmTilePointZ.GetCentroid();
            if (dsmReadWrite.Dtm.TryGetTile(tileCenterX, tileCenterY, this.DtmBand, out RasterBand<float>? dtmTile) == false)
            {
                throw new InvalidOperationException("DSM generation failed. Could not find underlying DTM tile at (" + tileCenterX + ", " + tileCenterY + ").");
            }
            if (dsmTilePointZ.IsSameExtent(dtmTile) == false)
            {
                throw new InvalidOperationException("DSM generation failed. Extent of DTM tile at (" + tileCenterX + ", " + tileCenterY + ") does not match extent of DSM tile (DTM extent " + dtmTile.GetExtentString() + ", DSM extent " + dsmTilePointZ.GetExtent() + ").");
            }

            DigitalSurfaceModel dsmTile = new(dsmTilePointZ, dtmTile, this.LayerSeparation, this.UpperPoints);

            string dsmTilePath = dsmReadWrite.OutputPathIsDirectory ? Path.Combine(this.Dsm, tileName + Constant.File.GeoTiffExtension) : this.Dsm;
            dsmTile.Write(dsmTilePath);

            return dsmTile.CellsPerBand;
        }

        private class DsmReadWrite : TileReadWrite<Grid<PointListZ>>
        {
            public VirtualRaster<float> Dsm { get; private init; }
            public VirtualRaster<float> Dtm { get; private init; }

            public DsmReadWrite(VirtualRaster<float> dsm, VirtualRaster<float> dtm, int maxSimultaneouslyLoadedTiles, bool dsmPathIsDirectory)
                : base(maxSimultaneouslyLoadedTiles, dsm.TileSizeInCellsX, dsm.TileSizeInCellsY, dsmPathIsDirectory)
            {
                this.Dsm = dsm;
                this.Dtm = dtm;
            }
        }
    }
}
