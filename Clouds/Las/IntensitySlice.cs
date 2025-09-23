using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    public class IntensitySlice : Raster
    {
        private const string IntensityBandName = "intensity";
        private const string PointCountBandName = "pointCount";

        public RasterBand<UInt64> Intensity { get; private init; }
        public RasterBand<UInt32> PointCount { get; private init; }

        public IntensitySlice(LasFile cloud, double cellSizeInCrsUnits, double trim)
            : base(cloud.GetSpatialReference().Clone(), cloud.GetSizeSnappedGrid(cellSizeInCrsUnits, trim), cloneCrsAndTransform: false)
        {
            this.Intensity = new(this, IntensitySlice.IntensityBandName, RasterBand.NoDataDefaultUInt64, (UInt64)0);
            this.PointCount = new(this, IntensitySlice.PointCountBandName, RasterBand.NoDataDefaultUInt32, (UInt32)0);
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Intensity;
            yield return this.PointCount;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.Intensity.GetStatistics(), this.PointCount.GetStatistics() ];
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
                    case IntensitySlice.IntensityBandName:
                        this.Intensity.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case IntensitySlice.PointCountBandName:
                        this.PointCount.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in intensity slice '" + this.FilePath + "'.");
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
            this.Intensity.ReturnData(dataBufferPool);
            this.PointCount.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(name, this.Intensity.Name, StringComparison.Ordinal))
            {
                band = this.Intensity;
            }
            else if (String.Equals(name, this.PointCount.Name, StringComparison.Ordinal))
            {
                band = this.PointCount;
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
            if (String.Equals(name, this.Intensity.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
                return true;
            }
            if (String.Equals(name, this.PointCount.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 1;
                return true;
            }

            bandIndexInFile = -1;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.Intensity.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PointCount.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string slicePath, bool compress)
        {
            using Dataset sliceDataset = this.CreateGdalRaster(slicePath, bands: 2, DataType.GDT_UInt64, compress);
            this.Intensity.Write(sliceDataset, 1);
            this.PointCount.Write(sliceDataset, 2);

            this.FilePath = slicePath;
        }

        public void WriteMean(string slicePath, bool compress)
        {
            RasterBand<UInt16> intensityMean = new(this, "intensityMean", RasterBandInitialValue.NoData, dataBufferPool: null);
            for (int cellIndex = 0; cellIndex < intensityMean.Data.Length; ++cellIndex)
            {
                UInt32 pointsInCell = this.PointCount[cellIndex];
                if (pointsInCell > 0)
                {
                    intensityMean.Data[cellIndex] = (UInt16)(this.Intensity[cellIndex] / pointsInCell); // mean intensity can be zero if scanner records points with zero intensity
                }
            }

            using Dataset sliceDataset = this.CreateGdalRaster(slicePath, bands: 1, DataType.GDT_UInt16, compress);
            intensityMean.Write(sliceDataset, 1);
        }
    }
}
