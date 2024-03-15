using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    // double is needed to accurately average gpstime but is otherwise overkill
    // If needed a double accumulator class can be implemented with downcoversion to float when writing to disk.
    public class ScanMetricsRaster : Raster
    {
        private readonly SortedList<string, RasterBand> bandsByName;

        public RasterBand<UInt32> AcceptedPoints { get; private init; } // number of accepted points (total number of points in cell is Points + NoiseOrWithheld)
        public RasterBand<UInt32> EdgeOfFlightLine { get; private init; }
        public RasterBand<double> GpstimeMin { get; private init; }
        public RasterBand<double> GpstimeMean { get; private init; }
        public RasterBand<double> GpstimeMax { get; private init; }
        public RasterBand<UInt32> NoiseOrWithheld { get; private init; } // number of rejected or skipped points
        public RasterBand<UInt32> Overlap { get; private init; }
        public RasterBand<float> ScanAngleMeanAbsolute { get; private init; }
        public RasterBand<float> ScanAngleMin { get; private init; }
        public RasterBand<float> ScanAngleMax { get; private init; }
        public RasterBand<float> ScanDirectionMean { get; private init; }

        public ScanMetricsRaster(Raster cellDefinitions)
            : base(cellDefinitions)
        {
            // no data for min and max scan angles and GPS times should arguably be Double.MaxValue and Double.MinValue
            // However, GDAL's default TIFFTAG_GDAL_NODATA profile supports only one no data value per raster.
            this.AcceptedPoints = new("acceptedPoints", this, RasterBand.NoDataDefaultUInt32);
            this.ScanAngleMeanAbsolute = new("scanAngleMeanAbsolute", this, RasterBand.NoDataDefaultFloat);
            this.ScanDirectionMean = new("scanDirection", this, RasterBand.NoDataDefaultFloat);
            this.ScanAngleMin = new("scanAngleMin", this, RasterBand.NoDataDefaultFloat);
            this.ScanAngleMax = new("scanAngleMax", this, RasterBand.NoDataDefaultFloat);
            this.NoiseOrWithheld = new("noiseOrWithheld", this, RasterBand.NoDataDefaultUInt32);
            this.EdgeOfFlightLine = new("edgeOfFlightLine", this, RasterBand.NoDataDefaultUInt32);
            this.Overlap = new("overlap", this, RasterBand.NoDataDefaultUInt32);
            this.GpstimeMin = new("gpstimeMin", this, RasterBand.NoDataDefaultDouble);
            this.GpstimeMean = new("gpstimeMean", this, RasterBand.NoDataDefaultDouble);
            this.GpstimeMax = new("gpstimeMax", this, RasterBand.NoDataDefaultDouble);

            this.bandsByName = new() { { this.AcceptedPoints.Name, this.AcceptedPoints },
                                       { this.ScanAngleMeanAbsolute.Name, this.ScanAngleMeanAbsolute },
                                       { this.ScanDirectionMean.Name, this.ScanDirectionMean },
                                       { this.ScanAngleMin.Name, this.ScanAngleMin },
                                       { this.ScanAngleMax.Name, this.ScanAngleMax },
                                       { this.NoiseOrWithheld.Name, this.NoiseOrWithheld },
                                       { this.EdgeOfFlightLine.Name, this.EdgeOfFlightLine },
                                       { this.Overlap.Name, this.Overlap },
                                       { this.GpstimeMin.Name, this.GpstimeMin },
                                       { this.GpstimeMean.Name, this.GpstimeMean },
                                       { this.GpstimeMax.Name, this.GpstimeMax } };

            //this.N.Data.Span.Clear(); // leave at default (0.0F)
            //this.ScanAngleMean.Data.Span.Clear(); // leave at default (0.0F)
            //this.ScanDirection.Data.Span.Clear(); // leave at default (0.0F)
            Array.Fill(this.ScanAngleMin.Data, Single.MaxValue);
            Array.Fill(this.ScanAngleMax.Data, Single.MinValue);
            //this.NoiseOrWithheld.Data.Span.Clear(); // leave at default (0.0F)
            //this.EdgeOfFlightLine.Data.Span.Clear(); // leave at default (0.0F)
            //this.Overlap.Data.Span.Clear(); // leave at default (0.0F)
            Array.Fill(this.GpstimeMin.Data, Double.MaxValue);
            //this.GpstimeMean.Data.Span.Clear(); // leave at default (0.0F)
            Array.Fill(this.GpstimeMax.Data, Double.MinValue);
        }

        public void OnPointAdditionComplete()
        {
            Debug.Assert(this.EdgeOfFlightLine.IsNoData(RasterBand.NoDataDefaultUInt32) && this.NoiseOrWithheld.IsNoData(RasterBand.NoDataDefaultUInt32) && this.Overlap.IsNoData(RasterBand.NoDataDefaultUInt32) &&
                         this.GpstimeMax.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMean.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMin.IsNoData(RasterBand.NoDataDefaultDouble) &&
                         this.ScanAngleMin.IsNoData(RasterBand.NoDataDefaultFloat) && this.ScanAngleMeanAbsolute.IsNoData(RasterBand.NoDataDefaultFloat) && this.ScanAngleMax.IsNoData(RasterBand.NoDataDefaultFloat));
            for (int index = 0; index < this.Cells; ++index)
            {
                UInt32 pointsInCell = this.AcceptedPoints[index];
                if (pointsInCell > 0)
                {
                    float pointsInCellAsFloat = (float)pointsInCell;
                    // this.EdgeOfFlightLine[index] is not an average
                    // this.NoiseOrWithheld[index] is not an average
                    // this.Overlap[index] is not an average
                    // this.ScanAngleMax[index] is not an average
                    this.ScanAngleMeanAbsolute[index] /= pointsInCellAsFloat;
                    // this.ScanAngleMin[index] is not an average
                    this.ScanDirectionMean[index] /= pointsInCellAsFloat;

                    double pointsInCellAsDouble = (double)pointsInCell;
                    // this.GpstimeMax[index] is not an average
                    this.GpstimeMean[index] /= pointsInCellAsDouble;
                    // this.GpstimeMin[index] is not an average
                }
                else
                {
                    // this.EdgeOfFlightLine[index] remains at zero
                    // this.NoiseOrWithheld[index] remains at zero
                    // this.Overlap[index] remains at zero
                    this.ScanAngleMax[index] = RasterBand.NoDataDefaultFloat;
                    this.ScanAngleMeanAbsolute[index] = RasterBand.NoDataDefaultFloat;
                    this.ScanAngleMin[index] = RasterBand.NoDataDefaultFloat;
                    this.ScanDirectionMean[index] = RasterBand.NoDataDefaultFloat;
                    this.GpstimeMax[index] = RasterBand.NoDataDefaultDouble;
                    this.GpstimeMean[index] = RasterBand.NoDataDefaultDouble;
                    this.GpstimeMin[index] = RasterBand.NoDataDefaultDouble;
                }
            }
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (name == null)
            {
                band = this.AcceptedPoints;
                return true;
            }

            return this.bandsByName.TryGetValue(name, out band);
        }

        public override void Write(string rasterPath, bool compress)
        {
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.GpstimeMin.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMean.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMax.IsNoData(RasterBand.NoDataDefaultDouble));

            using Dataset rasterDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, this.bandsByName.Count, DataType.GDT_Float64, compress);
            this.WriteBand(rasterDataset, this.AcceptedPoints, 1);
            this.WriteBand(rasterDataset, this.ScanAngleMeanAbsolute, 2);
            this.WriteBand(rasterDataset, this.NoiseOrWithheld, 3);
            this.WriteBand(rasterDataset, this.GpstimeMean, 4);
            this.WriteBand(rasterDataset, this.ScanDirectionMean, 5);
            this.WriteBand(rasterDataset, this.ScanAngleMin, 6);
            this.WriteBand(rasterDataset, this.ScanAngleMax, 7);
            this.WriteBand(rasterDataset, this.GpstimeMin, 8);
            this.WriteBand(rasterDataset, this.GpstimeMax, 9);
            this.WriteBand(rasterDataset, this.EdgeOfFlightLine, 10);
            this.WriteBand(rasterDataset, this.Overlap, 11);
        }
    }
}