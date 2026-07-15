using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Segmentation
{
    public class LocalMaximaRaster : Raster
    {
        public const string ChmMaximaBandName = "chmMaximaRadiusInCells";
        public const string CmmMaximaBandName = "cmmMaximaRadiusInCells";
        public const string DsmMaximaBandName = "dsmMaximaRadiusInCells";

        public RasterBand<byte> ChmMaxima { get; private init; }
        public RasterBand<byte>? CmmMaxima { get; private set; }
        public RasterBand<byte> DsmMaxima { get; private init; }

        public LocalMaximaRaster(Grid extent, bool includeCmm) 
            : base(extent)
        {
            this.ChmMaxima = new(this, LocalMaximaRaster.ChmMaximaBandName, RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData);
            this.CmmMaxima = includeCmm ? new(this, LocalMaximaRaster.CmmMaximaBandName, RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData) : null;
            this.DsmMaxima = new(this, LocalMaximaRaster.DsmMaximaBandName, RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData);
        }

        public static LocalMaximaRaster CreateRecreateOrReset(LocalMaximaRaster? raster, Grid extent, string filePath, bool includeCmm)
        {
            if ((raster == null) || (raster.SizeX != extent.SizeX) || (raster.SizeY != extent.SizeY))
            {
                return new(extent, includeCmm)
                {
                    FilePath = filePath
                };
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(raster.Crs, extent.Crs));
            raster.FilePath = filePath;
            raster.Transform.Copy(extent.Transform);
            raster.DsmMaxima.FillWithNoData();
            raster.ChmMaxima.FillWithNoData();
            raster.CmmMaxima?.FillWithNoData();
            return raster;
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.DsmMaxima;
            yield return this.ChmMaxima;
            if (this.CmmMaxima != null)
            {
                yield return this.CmmMaxima;
            }
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            List<RasterBandStatistics> bandStatistics = [ this.DsmMaxima.GetStatistics(), this.ChmMaxima.GetStatistics() ];
            if (this.CmmMaxima != null)
            {
                bandStatistics.Add(this.CmmMaxima.GetStatistics());
            }

            return bandStatistics;
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
                    case LocalMaximaRaster.ChmMaximaBandName:
                        this.ChmMaxima.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case LocalMaximaRaster.CmmMaximaBandName:
                        if (this.CmmMaxima == null)
                        {
                            this.CmmMaxima = new(rasterDataset, gdalBand, true);
                        }
                        else
                        {
                            this.CmmMaxima.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case LocalMaximaRaster.DsmMaximaBandName:
                        this.DsmMaxima.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException($"Unhandled band '{bandName}' in local maxima raster '{this.FilePath}'.");
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
            this.DsmMaxima.ReturnData(dataBufferPool);
            this.ChmMaxima.ReturnData(dataBufferPool);
            this.CmmMaxima?.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(this.DsmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmMaxima;
                return true;
            }
            if (String.Equals(this.ChmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.ChmMaxima;
                return true;
            }
            if ((this.CmmMaxima != null) && String.Equals(this.CmmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmMaxima;
                return true;
            }

            band = null;
            return false;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            if (String.Equals(this.DsmMaxima.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
            }
            else if (String.Equals(this.ChmMaxima.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 1;
            }
            else if ((this.CmmMaxima != null) && String.Equals(this.CmmMaxima.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 2;
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
            this.DsmMaxima.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ChmMaxima.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.CmmMaxima?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string maximaPath, bool compress)
        {
            Debug.Assert(this.DsmMaxima.IsNoData(RasterBand.NoDataDefaultByte) && this.ChmMaxima.IsNoData(RasterBand.NoDataDefaultByte));
            
            bool hasCmm = this.CmmMaxima != null;
            using Dataset maximaDataset = this.CreateGdalRaster(maximaPath, hasCmm ? 3 : 2, DataType.GDT_Byte, compress);

            this.DsmMaxima.Write(maximaDataset, 1);
            this.ChmMaxima.Write(maximaDataset, 2);
            if (hasCmm)
            {
                Debug.Assert((this.CmmMaxima != null) && this.CmmMaxima.IsNoData(RasterBand.NoDataDefaultByte));
                this.CmmMaxima.Write(maximaDataset, 3);
            }
            this.FilePath = maximaPath;
        }
    }
}
