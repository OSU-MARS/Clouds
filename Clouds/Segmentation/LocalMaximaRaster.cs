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
        public RasterBand<byte> ChmMaxima { get; private init; }
        public RasterBand<byte> CmmMaxima { get; private init; }
        public RasterBand<byte> DsmMaxima { get; private init; }

        public LocalMaximaRaster(Grid extent) 
            : base(extent)
        {
            this.ChmMaxima = new(this, "chmMaximaRadiusInCells", RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData);
            this.CmmMaxima = new(this, "cmmMaximaRadiusInCells", RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData);
            this.DsmMaxima = new(this, "dsmMaximaRadiusInCells", RasterBand.NoDataDefaultByte, RasterBandInitialValue.NoData);
        }

        public static LocalMaximaRaster CreateRecreateOrReset(LocalMaximaRaster? raster, Grid extent)
        {
            if ((raster == null) || (raster.SizeX != extent.SizeX) || (raster.SizeY != extent.SizeY))
            {
                return new(extent);
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(raster.Crs, extent.Crs));
            raster.Transform.Copy(extent.Transform);
            Array.Fill(raster.DsmMaxima.Data, raster.DsmMaxima.NoDataValue);
            Array.Fill(raster.CmmMaxima.Data, raster.CmmMaxima.NoDataValue);
            Array.Fill(raster.ChmMaxima.Data, raster.ChmMaxima.NoDataValue);
            return raster;
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(this.DsmMaxima.Name, name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(this.CmmMaxima.Name, name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(this.ChmMaxima.Name, name, StringComparison.Ordinal))
            {
                return 2;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "Band '" + name + "' is not present in a local maxima raster.");
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.DsmMaxima; 
            yield return this.CmmMaxima; 
            yield return this.ChmMaxima;
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBands(RasterBandPool dataBufferPool)
        {
            this.DsmMaxima.ReturnData(dataBufferPool);
            this.CmmMaxima.ReturnData(dataBufferPool);
            this.ChmMaxima.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (String.Equals(this.DsmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.DsmMaxima;
                return true;
            }
            if (String.Equals(this.CmmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.CmmMaxima;
                return true;
            }
            if (String.Equals(this.ChmMaxima.Name, name, StringComparison.Ordinal))
            {
                band = this.ChmMaxima;
                return true;
            }

            band = null;
            return false;
        }

        public override void Write(string maximaPath, bool compress)
        {
            Debug.Assert(this.DsmMaxima.IsNoData(RasterBand.NoDataDefaultByte) && this.CmmMaxima.IsNoData(RasterBand.NoDataDefaultByte) && this.ChmMaxima.IsNoData(RasterBand.NoDataDefaultByte));
            using Dataset maximaDataset = this.CreateGdalRasterAndSetFilePath(maximaPath, 3, DataType.GDT_Byte, compress);
            this.DsmMaxima.Write(maximaDataset, 1);
            this.CmmMaxima.Write(maximaDataset, 2);
            this.ChmMaxima.Write(maximaDataset, 3);
            this.FilePath = maximaPath;
        }
    }
}
