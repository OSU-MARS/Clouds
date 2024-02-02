using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds.Las
{
    // double is needed to accurately average gpstime but is otherwise overkill
    // If needed a double accumulator class can be implemented with downcoversion to float when writing to disk.
    public class ScanMetricsRaster : Raster<double>
    {
        public RasterBand<double> N { get; private init; }
        public RasterBand<double> ScanAngleMean { get; private init; }
        public RasterBand<double> ScanDirection { get; private init; }
        public RasterBand<double> ScanAngleMin { get; private init; }
        public RasterBand<double> ScanAngleMax { get; private init; }
        public RasterBand<double> NoiseOrWithheld { get; private init; }
        public RasterBand<double> EdgeOfFlightLine { get; private init; }
        public RasterBand<double> Overlap { get; private init; }
        public RasterBand<double> GpstimeMin { get; private init; }
        public RasterBand<double> GpstimeMean { get; private init; }
        public RasterBand<double> GpstimeMax { get; private init; }

        public ScanMetricsRaster(Raster cellDefinitions)
            : base(cellDefinitions.Crs, cellDefinitions.Transform, cellDefinitions.XSize, cellDefinitions.YSize, 11)
        {
            int bandIndex = 0;

            this.N = this.Bands[bandIndex++];
            this.ScanAngleMean = this.Bands[bandIndex++];
            this.ScanDirection = this.Bands[bandIndex++];
            this.ScanAngleMin = this.Bands[bandIndex++];
            this.ScanAngleMax = this.Bands[bandIndex++];
            this.NoiseOrWithheld = this.Bands[bandIndex++];
            this.EdgeOfFlightLine = this.Bands[bandIndex++];
            this.Overlap = this.Bands[bandIndex++];
            this.GpstimeMin = this.Bands[bandIndex++];
            this.GpstimeMean = this.Bands[bandIndex++];
            this.GpstimeMax = this.Bands[bandIndex++];

            this.N.Name = "n";
            this.ScanAngleMean.Name = "scanAngleMean";
            this.ScanDirection.Name = "scanDirection";
            this.ScanAngleMin.Name = "scanAngleMin";
            this.ScanAngleMax.Name = "scanAngleMax";
            this.NoiseOrWithheld.Name = "noiseOrWithheld";
            this.EdgeOfFlightLine.Name = "edgeOfFlightLine";
            this.Overlap.Name = "overlap";
            this.GpstimeMin.Name = "gpstimeMin";
            this.GpstimeMean.Name = "gpstimeMean";
            this.GpstimeMax.Name = "gpstimeMax";

            // passing a no data value to base..ctor() fills all bands with the no data value, which isn't especially useful here
            // No data for min scan angle should arguably be Double.MaxValue but GeoTIFF supported only one no data value per raster.
            this.SetNoDataOnAllBands(Double.MinValue);

            //this.N.Data.Span.Clear(); // leave at default (0.0F)
            //this.ScanAngleMean.Data.Span.Clear(); // leave at default (0.0F)
            //this.ScanDirection.Data.Span.Clear(); // leave at default (0.0F)
            this.ScanAngleMin.Data.Span.Fill(Double.MaxValue);
            this.ScanAngleMax.Data.Span.Fill(Double.MinValue);
            //this.NoiseOrWithheld.Data.Span.Clear(); // leave at default (0.0F)
            //this.EdgeOfFlightLine.Data.Span.Clear(); // leave at default (0.0F)
            //this.Overlap.Data.Span.Clear(); // leave at default (0.0F)
            this.GpstimeMin.Data.Span.Fill(Double.MaxValue);
            //this.GpstimeMean.Data.Span.Clear(); // leave at default (0.0F)
            this.GpstimeMax.Data.Span.Fill(Double.MinValue);
        }

        public void OnPointAdditionComplete()
        {
            double scanAngleMinNoData = this.ScanAngleMin.NoDataValue;
            double scanAngleMeanNoData = this.ScanAngleMean.NoDataValue;
            double gpstimeMinNoData = this.GpstimeMin.NoDataValue;
            double gpstimeMeanNoData = this.GpstimeMean.NoDataValue;

            for (int index = 0; index < this.CellsPerBand; ++index)
            {
                double nPointsInCell = this.N[index];
                if (nPointsInCell != 0.0F)
                {
                    this.ScanAngleMean[index] /= nPointsInCell;
                    this.GpstimeMean[index] /= nPointsInCell;
                }
                else
                {
                    this.ScanAngleMean[index] = scanAngleMeanNoData;
                    this.ScanAngleMin[index] = scanAngleMinNoData;
                    this.GpstimeMean[index] = gpstimeMeanNoData;
                    this.GpstimeMin[index] = gpstimeMinNoData;
                }
            }
        }
    }
}