﻿using Mars.Clouds.GdalExtensions;
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
        public const string AcceptedPointsBandName = "acceptedPoints";
        public const string ScanAngleMeanAbsoluteBandName = "scanAngleMeanAbsolute";
        public const string ScanDirectionMeanBandName = "scanDirection";
        public const string ScanAngleMinBandName = "scanAngleMin";
        public const string ScanAngleMaxBandName = "scanAngleMax";
        public const string NoiseOrWithheldBandName = "noiseOrWithheld";
        public const string EdgeOfFlightLineBandName = "edgeOfFlightLine";
        public const string OverlapBandName = "overlap";
        public const string GpstimeMinBandName = "gpstimeMin";
        public const string GpstimeMeanBandName = "gpstimeMean";
        public const string GpstimeMaxBandName = "gpstimeMax";

        public RasterBand<UInt32> AcceptedPoints { get; private init; } // number of accepted points (total number of points in cell is Points + NoiseOrWithheld)
        public RasterBand<float> ScanAngleMeanAbsolute { get; private init; }
        public RasterBand<UInt32> NoiseOrWithheld { get; private init; } // number of rejected or skipped points
        public RasterBand<double> GpstimeMean { get; private init; }
        public RasterBand<float> ScanDirectionMean { get; private init; }
        public RasterBand<float> ScanAngleMin { get; private init; }
        public RasterBand<float> ScanAngleMax { get; private init; }
        public RasterBand<double> GpstimeMin { get; private init; }
        public RasterBand<double> GpstimeMax { get; private init; }
        public RasterBand<UInt32> EdgeOfFlightLine { get; private init; }
        public RasterBand<UInt32> Overlap { get; private init; }

        public ScanMetricsRaster(Grid extent)
            : base(extent)
        {
            // no data for min and max scan angles and GPS times should arguably be Double.MaxValue and Double.MinValue
            // However, GDAL's default TIFFTAG_GDAL_NODATA profile supports only one no data value per raster.
            this.AcceptedPoints = new(this, ScanMetricsRaster.AcceptedPointsBandName, RasterBand.NoDataDefaultUInt32, RasterBandInitialValue.Default);
            this.ScanAngleMeanAbsolute = new(this, ScanMetricsRaster.ScanAngleMeanAbsoluteBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.Default);
            this.NoiseOrWithheld = new(this, ScanMetricsRaster.NoiseOrWithheldBandName, RasterBand.NoDataDefaultUInt32, RasterBandInitialValue.Default);
            this.GpstimeMean = new(this, ScanMetricsRaster.GpstimeMeanBandName, RasterBand.NoDataDefaultDouble, RasterBandInitialValue.Default);
            this.ScanDirectionMean = new(this, ScanMetricsRaster.ScanDirectionMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.Default);
            this.ScanAngleMin = new(this, ScanMetricsRaster.ScanAngleMinBandName, RasterBand.NoDataDefaultFloat, Single.MaxValue);
            this.ScanAngleMax = new(this, ScanMetricsRaster.ScanAngleMaxBandName, RasterBand.NoDataDefaultFloat, Single.MinValue);
            this.GpstimeMin = new(this, ScanMetricsRaster.GpstimeMinBandName, RasterBand.NoDataDefaultDouble, Double.MaxValue);
            this.GpstimeMax = new(this, ScanMetricsRaster.GpstimeMaxBandName, RasterBand.NoDataDefaultDouble, Double.MinValue);
            this.EdgeOfFlightLine = new(this, ScanMetricsRaster.EdgeOfFlightLineBandName, RasterBand.NoDataDefaultUInt32, RasterBandInitialValue.Default);
            this.Overlap = new(this, ScanMetricsRaster.OverlapBandName, RasterBand.NoDataDefaultUInt32, RasterBandInitialValue.Default);
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

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return AcceptedPoints;
            yield return ScanAngleMeanAbsolute;
            yield return NoiseOrWithheld;
            yield return GpstimeMean;
            yield return ScanDirectionMean;
            yield return ScanAngleMin;
            yield return ScanAngleMax;
            yield return GpstimeMin;
            yield return GpstimeMax;
            yield return EdgeOfFlightLine;
            yield return Overlap;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.AcceptedPoints.GetStatistics(),
                     this.ScanAngleMeanAbsolute.GetStatistics(),
                     this.NoiseOrWithheld.GetStatistics(),
                     this.GpstimeMean.GetStatistics(),
                     this.ScanDirectionMean.GetStatistics(),
                     this.ScanAngleMin.GetStatistics(),
                     this.ScanAngleMax.GetStatistics(),
                     this.GpstimeMin.GetStatistics(),
                     this.GpstimeMax.GetStatistics(),
                     this.EdgeOfFlightLine.GetStatistics(),
                     this.Overlap.GetStatistics() ];
        }

        public override void ReadBandData()
        {
            using Dataset rasterDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            for (int gdalBandIndex = 1; gdalBandIndex <= rasterDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case ScanMetricsRaster.AcceptedPointsBandName:
                        this.AcceptedPoints.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.ScanAngleMeanAbsoluteBandName:
                        this.ScanAngleMeanAbsolute.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.NoiseOrWithheldBandName:
                        this.NoiseOrWithheld.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.GpstimeMeanBandName:
                        this.GpstimeMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.ScanDirectionMeanBandName:
                        this.ScanDirectionMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.ScanAngleMinBandName:
                        this.ScanAngleMin.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.ScanAngleMaxBandName:
                        this.ScanAngleMax.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.GpstimeMinBandName:
                        this.GpstimeMin.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.GpstimeMaxBandName:
                        this.GpstimeMax.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.EdgeOfFlightLineBandName:
                        this.EdgeOfFlightLine.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ScanMetricsRaster.OverlapBandName:
                        this.Overlap.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in local maxima raster '" + this.FilePath + "'.");
                }
            }

            rasterDataset.FlushCache();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            this.AcceptedPoints.ReturnData(dataBufferPool);
            this.ScanAngleMeanAbsolute.ReturnData(dataBufferPool);
            this.NoiseOrWithheld.ReturnData(dataBufferPool);
            this.GpstimeMean.ReturnData(dataBufferPool);
            this.ScanDirectionMean.ReturnData(dataBufferPool);
            this.ScanAngleMin.ReturnData(dataBufferPool);
            this.ScanAngleMax.ReturnData(dataBufferPool);
            this.GpstimeMin.ReturnData(dataBufferPool);
            this.GpstimeMax.ReturnData(dataBufferPool);
            this.EdgeOfFlightLine.ReturnData(dataBufferPool);
            this.Overlap.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || (String.Equals(name, this.AcceptedPoints.Name, StringComparison.Ordinal)))
            {
                band = this.AcceptedPoints;
            }
            else if (String.Equals(name, this.ScanAngleMeanAbsolute.Name, StringComparison.Ordinal))
            {
                band = this.ScanAngleMeanAbsolute;
            }
            else if (String.Equals(name, this.NoiseOrWithheld.Name, StringComparison.Ordinal))
            {
                band = this.NoiseOrWithheld;
            }
            else if (String.Equals(name, this.GpstimeMean.Name, StringComparison.Ordinal))
            {
                band = this.GpstimeMean;
            }
            else if (String.Equals(name, this.ScanDirectionMean.Name, StringComparison.Ordinal))
            {
                band = this.ScanDirectionMean;
            }
            else if (String.Equals(name, this.ScanAngleMin.Name, StringComparison.Ordinal))
            {
                band = this.ScanAngleMin;
            }
            else if (String.Equals(name, this.ScanAngleMax.Name, StringComparison.Ordinal))
            {
                band = this.ScanAngleMax;
            }
            else if (String.Equals(name, this.GpstimeMin.Name, StringComparison.Ordinal))
            {
                band = this.GpstimeMin;
            }
            else if (String.Equals(name, this.GpstimeMax.Name, StringComparison.Ordinal))
            {
                band = this.GpstimeMax;
            }
            else if (String.Equals(name, this.EdgeOfFlightLine.Name, StringComparison.Ordinal))
            {
                band = this.EdgeOfFlightLine;
            }
            else if (String.Equals(name, this.Overlap.Name, StringComparison.Ordinal))
            {
                band = this.Overlap;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            if (String.Equals(this.AcceptedPoints.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
            }
            else if (String.Equals(this.ScanAngleMeanAbsolute.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 1;
            }
            else if (String.Equals(this.NoiseOrWithheld.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 2;
            }
            else if (String.Equals(this.GpstimeMean.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 3;
            }
            else if (String.Equals(this.ScanDirectionMean.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 4;
            }
            else if (String.Equals(this.ScanAngleMin.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 5;
            }
            else if (String.Equals(this.ScanAngleMax.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 6;
            }
            else if (String.Equals(this.GpstimeMin.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 7;
            }
            else if (String.Equals(this.GpstimeMax.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 8;
            }
            else if (String.Equals(this.EdgeOfFlightLine.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 9;
            }
            else if (String.Equals(this.Overlap.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 10;
            }
            else
            {
                bandIndexInFile = -1;
                return false;
            }

            return true;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.AcceptedPoints.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ScanAngleMeanAbsolute.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.NoiseOrWithheld.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.GpstimeMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ScanDirectionMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ScanAngleMin.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ScanAngleMax.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.GpstimeMin.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.GpstimeMax.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.EdgeOfFlightLine.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.Overlap.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string rasterPath, bool compress)
        {
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.GpstimeMin.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMean.IsNoData(RasterBand.NoDataDefaultDouble) && this.GpstimeMax.IsNoData(RasterBand.NoDataDefaultDouble));

            using Dataset rasterDataset = this.CreateGdalRaster(rasterPath, 11, DataType.GDT_Float64, compress);
            this.AcceptedPoints.Write(rasterDataset, 1);
            this.ScanAngleMeanAbsolute.Write(rasterDataset, 2);
            this.NoiseOrWithheld.Write(rasterDataset, 3);
            this.GpstimeMean.Write(rasterDataset, 4);
            this.ScanDirectionMean.Write(rasterDataset, 5);
            this.ScanAngleMin.Write(rasterDataset, 6);
            this.ScanAngleMax.Write(rasterDataset, 7);
            this.GpstimeMin.Write(rasterDataset, 8);
            this.GpstimeMax.Write(rasterDataset, 9);
            this.EdgeOfFlightLine.Write(rasterDataset, 10);
            this.Overlap.Write(rasterDataset, 11);

            this.FilePath = rasterPath;
        }
    }
}