using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "GridMetrics")]
    public class GetGridMetrics : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "Raster defining grid over which ABA (area based approach) point cloud metrics are calculated. Metrics are not caculated for cells outside the point cloud or which have a no data value in the raster.")]
        [ValidateNotNullOrEmpty]
        public string? AbaCells { get; set; }

        [Parameter(Mandatory = true, HelpMessage = ".las file to measure load speed on.")]
        [ValidateNotNullOrEmpty]
        public string? Las { get; set; }

        protected override void ProcessRecord()
        {
            Debug.Assert((String.IsNullOrWhiteSpace(this.AbaCells) == false) && (String.IsNullOrWhiteSpace(this.Las) == false));
            Stopwatch stopwatch = new();
            stopwatch.Start();

            using Dataset gridCellDefinitionDataset = Gdal.Open(this.AbaCells, Access.GA_ReadOnly);
            Raster<UInt16> gridCellDefinitions = new(gridCellDefinitionDataset);

            using FileStream stream = new(this.Las, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.SequentialScan);
            using LasReader lasReader = new(stream);
            LasFile lasFile = lasReader.ReadHeader();
            lasReader.ReadVariableLengthRecords(lasFile);

            int gridEpsg = Int32.Parse(gridCellDefinitions.Crs.GetAuthorityCode("PROJCS"));
            int lasEpsg = lasFile.GetProjectedCoordinateSystemEpsg();
            if (gridEpsg != lasEpsg)
            {
                throw new NotSupportedException("ABA grid coordinate system (EPSG:" + gridEpsg + ") differs from the .las file's coordinate system (EPSG:" + lasEpsg + "). Currently, the two inputs are required to be in the same coordinate system.");
            }

            this.WriteProgress(new ProgressRecord(0, "Get-GridMetrics", "Loading " + Path.GetFileName(this.Las) + "..."));

            Grid<PointListZirn> abaGrid = new(gridCellDefinitions);
            lasReader.ReadPointsToGridZirn(lasFile, abaGrid);

            stopwatch.Stop();
            int abaGridCellsWithPoints = 0;
            for (int yIndex = 0; yIndex < abaGrid.YSize; ++yIndex)
            {
                for (int xIndex = 0; xIndex < abaGrid.XSize; ++xIndex)
                {
                    PointListZirn? abaCell = abaGrid[xIndex, yIndex];
                    if ((abaCell != null) && (abaCell.Count > 0))
                    {
                        ++abaGridCellsWithPoints;
                    }
                }
            }

            FileInfo fileInfo = new(this.Las);
            UInt64 pointsRead = lasFile.Header.GetNumberOfPoints();
            float megabytesRead = fileInfo.Length / (1024.0F * 1024.0F);
            float gigabytesRead = megabytesRead / 1024.0F;
            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            this.WriteVerbose("Gridded " + gigabytesRead.ToString("0.00") + " GB with " + pointsRead.ToString("#,#") + " points into " + abaGridCellsWithPoints + " grid cells in " + elapsedSeconds.ToString("0.000") + " s: " + (pointsRead / (1E6 * elapsedSeconds)).ToString("0.00") + " Mpoints/s, " + (megabytesRead / elapsedSeconds).ToString("0.0") + " MB/s.");

            stopwatch.Restart();
            ProgressRecord gridMetricsProgress = new(0, "Get-GridMetrics", "Calculating grid metrics...")
            {
                PercentComplete = 0
            };
            this.WriteProgress(gridMetricsProgress);

            StandardMetricsRaster abaMetrics = new(abaGrid.Crs, abaGrid.Transform, abaGrid.XSize, abaGrid.YSize);
            float crsLinearUnits = (float)lasFile.GetSpatialReference().GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 920.52F + 2.0F / crsLinearUnits;
            int abaGridCellsWithCalculatedMetrics = 0;
            for (int yIndex = 0; yIndex < abaGrid.YSize; ++yIndex)
            {
                for (int xIndex = 0; xIndex < abaGrid.XSize; ++xIndex)
                {
                    PointListZirn? abaCell = abaGrid[xIndex, yIndex];
                    if ((abaCell != null) && (abaCell.Count > 0))
                    {
                        abaCell.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold, xIndex, yIndex);

                        ++abaGridCellsWithCalculatedMetrics;
                        double fractionComplete = (double)abaGridCellsWithCalculatedMetrics / (double)abaGridCellsWithPoints;
                        gridMetricsProgress.PercentComplete = (int)(100.0 * fractionComplete);
                        gridMetricsProgress.SecondsRemaining = (int)(stopwatch.Elapsed.TotalSeconds / fractionComplete);
                        this.WriteProgress(gridMetricsProgress);
                    }
                }
            }

            stopwatch.Stop();
            elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            this.WriteVerbose("Calculated metrics for " + abaGridCellsWithPoints + " cells in " + elapsedSeconds.ToString("0.000") + " s: " + (abaGridCellsWithPoints / elapsedSeconds).ToString("0.0") + " cells/s.");
            this.WriteObject(abaMetrics);
            base.ProcessRecord();
        }
    }
}
