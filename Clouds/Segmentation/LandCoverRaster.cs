using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Segmentation
{
    public class LandCoverRaster : Raster
    {
        public RasterBand<byte> Classification { get; private set; }

        protected LandCoverRaster(Dataset coverDataset, bool readData)
            : base(coverDataset)
        {
            if (coverDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(coverDataset), $"Raster '{this.FilePath}' has {coverDataset.RasterCount} bands. Currently only single band rasters are supported.");
            }

            this.Classification = new(coverDataset, coverDataset.GetRasterBand(1), readData);
            // should band name be checked?
        }

        public static LandCoverRaster CreateFromBandMetadata(string coverPath)
        {
            using Dataset coverDataset = Gdal.Open(coverPath, Access.GA_ReadOnly);
            LandCoverRaster crownRaster = new(coverDataset, readData: false);
            Debug.Assert(String.Equals(crownRaster.FilePath, coverPath));
            coverDataset.FlushCache();
            return crownRaster;
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Classification;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.Classification.GetStatistics() ];
        }

        public override void ReadBandData()
        {
            using Dataset coverDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            if (coverDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(coverDataset), $"Raster '{this.FilePath}' has {coverDataset.RasterCount} bands. Currently only single band rasters are supported.");
            }

            this.Classification.ReadDataAssumingSameCrsTransformSizeAndNoData(coverDataset.GetRasterBand(1));
            coverDataset.FlushCache();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException();
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            this.Classification.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(this.Classification.Name, name, StringComparison.Ordinal))
            {
                band = this.Classification;
                return true;
            }

            band = null;
            return false;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            if (String.Equals(this.Classification.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
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
            this.Classification.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string coverPath, bool compress)
        {
            using Dataset coverDataset = this.CreateGdalRaster(coverPath, 1, DataType.GDT_Byte, compress);
            this.Classification.Write(coverDataset, 1);
            this.FilePath = coverPath;
        }
    }
}
