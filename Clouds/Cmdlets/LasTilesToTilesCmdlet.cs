using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using MaxRev.Gdal.Core;
using OSGeo.OSR;
using System;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public abstract class LasTilesToTilesCmdlet : LasTilesCmdlet
    {
        [Parameter(HelpMessage = "Whether or not to compress output rasters (DSM or orthoimages). Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        [Parameter(HelpMessage = "Perform all processing steps except writing output tiles to disk. This development and diagnostic switch provides insight in certain types of performance profiling and it allows evaluation of drive thermals without incurring flash write wear.")]
        public SwitchParameter NoWrite { get; set; }

        [Parameter(HelpMessage = "Enable use of unbuffered IO on Windows. This is likely to be helpful in utilizing PCIe 4.0 x4 and faster NVMe drives. Default is buffered IO.")]
        public SwitchParameter Unbuffered { get; set; }

        static LasTilesToTilesCmdlet()
        {
            GdalBase.ConfigureAll(); // see remarks in GdalCmdlet..ctor()
        }

        protected LasTilesToTilesCmdlet()
        {
            this.CompressRasters = false;
        }

        protected LasTileGrid ReadLasHeadersAndFormGrid(string cmdletName, string outputParameterName, bool outputPathIsDirectory)
        {
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName);
            if ((lasGrid.NonNullCells > 1) && (outputPathIsDirectory == false))
            {
                throw new ParameterOutOfRangeException(outputParameterName, $"-{outputParameterName} must be an existing directory when {nameof(this.Las)} indicates multiple files.");
            }

            return lasGrid;
        }

        protected static (double cellSize, int xSizeInCells, int ySizeInCells) GetRasterSizing(LasTileGrid lasGrid, double specifiedCellSize)
        {
            return LasTilesToTilesCmdlet.GetRasterSizing(lasGrid.Crs, specifiedCellSize, lasGrid.Transform.CellWidth, lasGrid.Transform.CellHeight);
        }

        // for now, a raster aligned to the CRS axes is assumed
        protected static (double cellSize, int xSizeInCells, int ySizeInCells) GetRasterSizing(SpatialReference crs, double specifiedCellSize, double rasterWidthInCrsUnits, double rasterHeightInCrsUnits)
        {
            double cellSize = specifiedCellSize;
            if (Double.IsNaN(specifiedCellSize))
            {
                double crsProjectedLinearUnitInM = crs.GetProjectedLinearUnitInM();
                cellSize = crsProjectedLinearUnitInM == 1.0 ? 0.5 : 1.5; // 0.5 m or 1.5 feet
            }

            int outputTileSizeX = (int)(rasterWidthInCrsUnits / cellSize);
            if (rasterWidthInCrsUnits - outputTileSizeX * cellSize != 0.0)
            {
                string units = crs.GetLinearUnitsName();
                throw new InvalidOperationException($"Point cloud derived raster size of {rasterWidthInCrsUnits} x {rasterHeightInCrsUnits} is not an integer multiple of the {cellSize} {units} output cell size.");
            }
            int outputTileSizeY = (int)(Double.Abs(rasterHeightInCrsUnits) / cellSize);
            if (Double.Abs(rasterHeightInCrsUnits) - outputTileSizeY * cellSize != 0.0)
            {
                string units = crs.GetLinearUnitsName();
                throw new InvalidOperationException($"Point cloud tile grid pitch of {rasterWidthInCrsUnits} x {rasterHeightInCrsUnits} is not an integer multiple of the {cellSize} {units} output cell size.");
            }

            return (cellSize, outputTileSizeX, outputTileSizeY);
        }
    }
}
