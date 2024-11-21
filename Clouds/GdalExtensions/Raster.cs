using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class Raster : Grid
    {
        protected static readonly string[] DefaultGeoTiffCompressionOptions;

        public string FilePath { get; set; }

        static Raster()
        {
            Raster.DefaultGeoTiffCompressionOptions = [ "COMPRESS=DEFLATE", "PREDICTOR=2", "ZLEVEL=9" ];
        }

        protected Raster(Dataset rasterDataset)
            : this(rasterDataset.GetSpatialRef(), new(rasterDataset), rasterDataset.RasterXSize, rasterDataset.RasterYSize)
        {
            this.FilePath = rasterDataset.GetFirstFile(); // for now, assume primary source file is always the first file in the raster's sources
        }

        protected Raster(Grid transformAndExtent)
            : this(transformAndExtent.Crs, transformAndExtent.Transform, transformAndExtent.SizeX, transformAndExtent.SizeY)
        {
        }

        protected Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : this(crs, transform, xSize, ySize, cloneCrsAndTransform: true)
        {
        }

        protected Raster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, bool cloneCrsAndTransform)
            : base(crs, transform, xSize, ySize, cloneCrsAndTransform)
        {
            this.FilePath = String.Empty;
        }

        public static Raster Create(string filePath, Dataset rasterDataset, bool readData)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Dataset contains no raster bands.");
            }
            using Band gdalBand1 = rasterDataset.GetRasterBand(1);
            Raster raster = gdalBand1.DataType switch
            {
                DataType.GDT_Byte => new Raster<byte>(rasterDataset, readData),
                DataType.GDT_Float32 => new Raster<float>(rasterDataset, readData),
                DataType.GDT_Float64 => new Raster<double>(rasterDataset, readData),
                DataType.GDT_Int8 => new Raster<sbyte>(rasterDataset, readData),
                DataType.GDT_Int16 => new Raster<Int16>(rasterDataset, readData),
                DataType.GDT_Int32 => new Raster<Int32>(rasterDataset, readData),
                DataType.GDT_Int64 => new Raster<Int64>(rasterDataset, readData),
                DataType.GDT_UInt16 => new Raster<UInt16>(rasterDataset, readData),
                DataType.GDT_UInt32 => new Raster<UInt32>(rasterDataset, readData),
                DataType.GDT_UInt64 => new Raster<UInt64>(rasterDataset, readData),
                DataType.GDT_Unknown => throw new NotSupportedException("Raster data type is unknown (" + gdalBand1.DataType + ")."),
                //DataType.GDT_CFloat32 or
                //DataType.GDT_CFloat64 or
                //DataType.GDT_CInt16 or
                //DataType.GDT_CInt32 or
                _ => throw new NotSupportedException("Unhandled raster data type " + gdalBand1.DataType + ".")
            };

            raster.FilePath = filePath;
            return raster;
        }

        protected Dataset CreateGdalRasterAndSetFilePath(string rasterPath, int bands, DataType cellDataType, bool compress)
        {
            using Driver rasterDriver = GdalExtensions.GetDriverByExtension(rasterPath);
            if (File.Exists(rasterPath))
            {
                // no overwrite option in GTiff.Create(), likely also the case for other drivers
                // This will throw a permission denied ApplicationException with 0x80131600 = -2146232832 if the raster file is
                // - incomplete
                // - marked read only
                try
                {
                    CPLErr gdalErrorCode = rasterDriver.Delete(rasterPath);
                    GdalException.ThrowIfError(gdalErrorCode, nameof(rasterDriver.Delete));
                }
                catch (ApplicationException gdalDeletionError)
                {
                    if (gdalDeletionError.HResult != -2146232832)
                    {
                        throw;
                    }

                    File.Delete(rasterPath);
                }
            }

            string[] rasterDriverOptions = compress ? Raster.DefaultGeoTiffCompressionOptions : [];
            Dataset rasterDataset = rasterDriver.Create(rasterPath, this.SizeX, this.SizeY, bands, cellDataType, rasterDriverOptions); // caller is responsible for disposal
            rasterDataset.SetGeoTransform(this.Transform.GetPadfTransform());
            rasterDataset.SetSpatialRef(this.Crs);

            this.FilePath = rasterPath;
            return rasterDataset;
        }

        public RasterBand GetBand(string? name)
        {
            if (this.TryGetBand(name, out RasterBand? band) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
            }

            return band;
        }

        public abstract IEnumerable<RasterBand> GetBands();

        /// <summary>
        /// Get path to a file with additional bands in a subdirectory below a raster's primary file with its main bands.
        /// </summary>
        protected static string GetComponentFilePath(string primaryFilePath, string diagnosticDirectoryName, bool createDiagnosticDirectory)
        {
            string? directoryPath = Path.GetDirectoryName(primaryFilePath);
            if (directoryPath == null)
            {
                throw new ArgumentOutOfRangeException(nameof(primaryFilePath), "Primary raster file path'" + primaryFilePath + "' does not contain a directory.");
            }
            string? fileName = Path.GetFileName(primaryFilePath);
            if (fileName == null)
            {
                throw new ArgumentOutOfRangeException(nameof(primaryFilePath), "Primary raster file path '" + primaryFilePath + "' does not contain a file name.");
            }

            string diagnosticDirectoryPath = Path.Combine(directoryPath, diagnosticDirectoryName);
            if (Directory.Exists(diagnosticDirectoryPath) == false)
            {
                if (createDiagnosticDirectory)
                {
                    Directory.CreateDirectory(diagnosticDirectoryPath);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(diagnosticDirectoryName), "Directory '" + directoryPath + "' does not have a '" + diagnosticDirectoryName + "' subdirectory for diagnostic tiles.");
                }
            }

            string diagnosticFilePath = Path.Combine(diagnosticDirectoryPath, fileName);
            return diagnosticFilePath;
        }

        public abstract List<RasterBandStatistics> GetBandStatistics();

        // eight-way immediate adjacency
        public static bool IsNeighbor8(int rowOffset, int columnOffset)
        {
            // exclude all cells with Euclidean grid distance >= 2.0
            int absRowOffset = Math.Abs(rowOffset);
            if (absRowOffset > 1)
            {
                return false;
            }

            int absColumnOffset = Math.Abs(columnOffset);
            if (absColumnOffset > 1)
            {
                return false;
            }

            // remaining nine possibilities have 0.0 <= Euclidean grid distance <= sqrt(2.0) and 0 <= Manhattan distance <= 2
            // Of these, only the self case (row offset = column offset = 0) needs to be excluded.
            return (absRowOffset + absColumnOffset) > 0;
        }

        public abstract void ReadBandData();

        public abstract void Reset(string filePath, Dataset rasterDataset, bool readData);

        public abstract void ReturnBandData(RasterBandPool dataBufferPool);

        public abstract bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band);

        public abstract bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile);

        public abstract void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool);

        public abstract void Write(string rasterPath, bool compress);
    }

    /// <summary>
    /// A default raster implementation where all bands are of the same type.
    /// </summary>
    public class Raster<TBand> : Raster where TBand : IMinMaxValue<TBand>, INumber<TBand>
    {
        // private byte[]? buffer; // for performance testing

        public RasterBand<TBand>[] Bands { get; private init; }

        public Raster(Dataset rasterDataset, bool readData)
            : base(rasterDataset)
        {
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster has no bands.");
            }

            // allocate data and create bands
            // Commented block is for performance testing. GDAL's Dataset.ReadRaster() offers the opportunity to read all bands into a
            // caller provided buffer at once rather than one at a time and may thus have different performance characteristics than
            // reading bands individually. As of GDAL 3.8.3, however, there appears to be little to no difference between the two
            // approaches.
            //if (readData)
            //{
            //    int[] bandMap = new int[rasterDataset.RasterCount];
            //    for (int index = 0; index < bandMap.Length; ++index)
            //    {
            //        bandMap[index] = index + 1;
            //    }

            //    DataType gdalDataType = RasterBand.GetGdalDataType<TBand>();
            //    byte[] buffer = new byte[rasterDataset.RasterCount * rasterDataset.RasterXSize * rasterDataset.RasterYSize * DataTypeExtensions.GetSizeInBytes(gdalDataType)];

            //    GCHandle dataPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            //    try
            //    {
            //        CPLErr gdalErrorCode = rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: rasterDataset.RasterXSize, ySize: rasterDataset.RasterYSize, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: rasterDataset.RasterXSize, buf_ySize: rasterDataset.RasterYSize, buf_type: gdalDataType, bandCount: rasterDataset.RasterCount, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
            //        GdalException.ThrowIfError(gdalErrorCode, nameof(rasterDataset.ReadRaster));
            //    }
            //    finally
            //    {
            //        dataPin.Free();
            //    }
            //}

            // also check bands for consistency
            this.Bands = new RasterBand<TBand>[rasterDataset.RasterCount];
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                if (this.SizeX != gdalBand.XSize)
                {
                    throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + gdalBand.XSize + " by " + gdalBand.YSize + " cells.");
                }
                if (this.SizeY != gdalBand.YSize)
                {
                    throw new NotSupportedException("Previous bands are " + this.SizeX + " by " + this.SizeY + " cells but band " + gdalBandIndex + " is " + gdalBand.XSize + " by " + gdalBand.YSize + " cells.");
                }
                this.Bands[bandIndex] = new(rasterDataset, gdalBand, readData);
            }
        }

        public Raster(Grid transformAndExtent, string[] bandNames, TBand noDataValue)
            : base(transformAndExtent)
        {
            if (bandNames.Length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bandNames), "Rasters must have at least one band but " + bandNames.Length + " band names were specified.");
            }

            this.Bands = new RasterBand<TBand>[bandNames.Length];
            for (int bandIndex = 0; bandIndex < bandNames.Length; ++bandIndex)
            {
                this.Bands[bandIndex] = new(this, bandNames[bandIndex], noDataValue, RasterBandInitialValue.NoData);
            }
        }

        public static Raster<TBand> CreateFromBandMetadata(string rasterPath)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            Raster<TBand> raster = new(rasterDataset, readData: false)
            {
                FilePath = rasterPath
            };

            rasterDataset.FlushCache();
            return raster;
        }

        //public static Raster<TBand> CreateRecreateOrReset(Raster<TBand>? raster, string rasterPath, Dataset rasterDataset, bool readData)
        //{
        //    if ((raster == null) || (raster.SizeX != rasterDataset.RasterXSize) || (raster.SizeY != rasterDataset.RasterYSize) || (raster.Bands.Length != rasterDataset.RasterCount))
        //    {
        //        return new Raster<TBand>(rasterDataset, readData) // if needed, bands can be expanded or reduced
        //        {
        //            FilePath = rasterPath,
        //        };
        //    }

        //    raster.Crs = rasterDataset.GetSpatialRef();
        //    raster.FilePath = rasterPath;
        //    raster.Transform = new(rasterDataset);
        //    // raster.SizeX already handled
        //    // raster.SizeY already handled

        //    for (int bandIndex = 0; bandIndex < raster.Bands.Length; ++bandIndex)
        //    {
        //        int gdalBandIndex = bandIndex + 1;
        //        RasterBand band = raster.Bands[bandIndex];
        //        band.Reset(raster.Crs, raster.Transform, rasterDataset.GetRasterBand(gdalBandIndex), readData);
        //    }

        //    return raster;
        //}

        public new RasterBand<TBand> GetBand(string? name)
        {
            if (this.TryGetBand(name, out RasterBand<TBand>? band) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
            }

            return band;
        }

        public override IEnumerable<RasterBand<TBand>> GetBands()
        {
            return this.Bands;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            List<RasterBandStatistics> bandStatistics = new(this.Bands.Length);
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                bandStatistics.Add(this.Bands[bandIndex].GetStatistics());
            }

            return bandStatistics;
        }

        public override void ReadBandData()
        {
            using Dataset rasterDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                this.Bands[bandIndex].ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
            }

            rasterDataset.FlushCache();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            if ((this.Bands.Length != rasterDataset.RasterCount) || (this.SizeX != rasterDataset.RasterXSize) || (this.SizeY != rasterDataset.RasterYSize))
            {
                throw new NotSupportedException(nameof(rasterDataset));
            }

            this.Crs = rasterDataset.GetSpatialRef();
            this.FilePath = filePath;
            this.Transform.CopyFrom(rasterDataset);
            // this.SizeX already checked
            // this.SizeY already checked

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                this.Bands[bandIndex].Reset(this.Crs, this.Transform, gdalBand, readData);
            }

            // incomplete stub for performance testing: read entire raster at once
            // Whole raster read increases DDR bandwidth by 50% compared to per band read when GDAL's defaulted to GTIFF_DIRECT_IO = NO,
            // reduces single threaded read rates by 60% when GTIFF_DIRECT_IO = YES.
            //DataType thisDataType = RasterBand.GetGdalDataType<TBand>();
            //int bytesPerCell = thisDataType.GetSizeInBytes();
            //int requiredBufferSizeInBytes = rasterDataset.RasterCount * rasterDataset.RasterXSize * rasterDataset.RasterYSize * bytesPerCell;
            //if ((this.buffer == null) || (this.buffer.Length <= requiredBufferSizeInBytes))
            //{
            //    this.buffer = new byte[requiredBufferSizeInBytes];
            //}

            //int[] bandMap = new int[rasterDataset.RasterCount];
            //for (int bandIndex = 0; bandIndex < rasterDataset.RasterCount; ++bandIndex)
            //{
            //    bandMap[bandIndex] = bandIndex + 1;
            //}

            //GCHandle dataPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            //try
            //{
            //    // read entire band from GDAL's cache at once
            //    CPLErr gdalErrorCode2 = rasterDataset.ReadRaster(xOff: 0, yOff: 0, xSize: rasterDataset.RasterXSize, ySize: rasterDataset.RasterYSize, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: rasterDataset.RasterXSize, buf_ySize: rasterDataset.RasterYSize, buf_type: thisDataType, rasterDataset.RasterCount, bandMap, pixelSpace: 0, lineSpace: 0, bandSpace: 0);
            //    GdalException.ThrowIfError(gdalErrorCode2, nameof(rasterDataset.ReadRaster));
            //}
            //finally
            //{
            //    dataPin.Free();
            //}
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                this.Bands[bandIndex].ReturnData(dataBufferPool);
            }
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (this.TryGetBand(name, out RasterBand<TBand>? typedBand))
            {
                band = typedBand;
                return true;
            }

            band = null;
            return false;
        }

        public bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand<TBand>? band)
        {
            if (name == null)
            {
                Debug.Assert(this.Bands.Length > 0);
                band = this.Bands[0];
                return true;
            }

            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> candidateBand = this.Bands[bandIndex];
                if (String.Equals(candidateBand.Name, name, StringComparison.Ordinal))
                {
                    band = candidateBand;
                    return true;
                }
            }

            band = null;
            return false;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> candidateBand = this.Bands[bandIndex];
                if (String.Equals(candidateBand.Name, name, StringComparison.Ordinal))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
            }

            bandIndexInFile = -1;
            return false;
        }

        //public bool TryGetNoDataValue(out TBand noDataValue)
        //{
        //    int bandsWithNoData = 0;
        //    noDataValue = TBand.Zero;
        //    for (int bandIndex = 0; bandIndex < this.BandCount; ++bandIndex)
        //    {
        //        RasterBand<TBand> band = this.Bands[bandIndex];
        //        if (band.HasNoDataValue)
        //        {
        //            if (bandsWithNoData == 0)
        //            {
        //                noDataValue = band.NoDataValue;
        //            }
        //            else if (band.IsNoData(noDataValue) == false)
        //            {
        //                throw new NotSupportedException("Raster bands have different no data values. At least " + band.NoDataValue + " on band " + band.Name + "(band " + (bandIndex + 1) + ") and " + noDataValue + " on a lower numbered band.");
        //            }

        //            ++bandsWithNoData;
        //        }
        //    }

        //    if ((bandsWithNoData != 0) && (bandsWithNoData != this.BandCount))
        //    {
        //        throw new NotSupportedException("Raster has " + bandsWithNoData + " bands with no data values and " + (this.BandCount - bandsWithNoData) + " bands without no data values. Whether or not there is a raster level no data value is therefore not well defined.");
        //    }
        //    return bandsWithNoData > 0;
        //}

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                this.Bands[bandIndex].TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
        }

        public override void Write(string rasterPath, bool compress)
        {
            // all bands have the same type, so no need for type conversion to meet GDAL (and GeoTIFF) single type constraints
            DataType gdalDataType = RasterBand.GetGdalDataType<TBand>();
            using Dataset rasterDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, this.Bands.Length, gdalDataType, compress);
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<TBand> band = this.Bands[bandIndex];
                int gdalbandIndex = bandIndex + 1;
                band.Write(rasterDataset, gdalbandIndex);
            }
        }
    }
}
