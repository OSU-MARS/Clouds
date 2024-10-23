using DocumentFormat.OpenXml.Presentation;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mars.Clouds.GdalExtensions
{
    public abstract class RasterBand : Grid
    {
        public const byte NoDataDefaultByte = Byte.MaxValue;
        public const double NoDataDefaultDouble = Double.NaN;
        public const float NoDataDefaultFloat = Single.NaN;
        public const Int16 NoDataDefaultInt16 = Int16.MinValue;
        public const Int32 NoDataDefaultInt32 = Int32.MinValue;
        public const Int64 NoDataDefaultInt64 = Int64.MinValue;
        public const sbyte NoDataDefaultSByte = SByte.MaxValue;
        public const UInt16 NoDataDefaultUInt16 = UInt16.MaxValue;
        public const UInt32 NoDataDefaultUInt32 = UInt32.MaxValue;
        public const UInt64 NoDataDefaultUInt64 = UInt64.MaxValue;

        protected bool NoDataIsNaN { get; set; }

        public bool HasNoDataValue { get; protected set; }
        public string Name { get; set; }

        // band is loaded from file on disk
        protected RasterBand(Dataset rasterDataset, Band gdalBand)
            : base(rasterDataset.GetSpatialRef(), new GridGeoTransform(rasterDataset), gdalBand.XSize, gdalBand.YSize, cloneCrsAndTransform: false)
        {
            this.NoDataIsNaN = false;
            this.HasNoDataValue = false;
            this.Name = gdalBand.GetDescription();
        }

        // band is created in memory
        protected RasterBand(Raster raster, string name)
            : base(raster, cloneCrsAndTransform: false) // RasterBands all share same CRS and geotransform by reference
        {
            this.NoDataIsNaN = false;
            this.HasNoDataValue = false;
            this.Name = name;
        }

        public abstract bool HasData { get; }

        public static double GetDefaultNoDataValueAsDouble(DataType gdalType)
        {
            return gdalType switch
            {
                DataType.GDT_Byte => RasterBand.NoDataDefaultByte,
                DataType.GDT_Float32 => RasterBand.NoDataDefaultFloat,
                DataType.GDT_Float64 => RasterBand.NoDataDefaultDouble,
                DataType.GDT_Int8 => RasterBand.NoDataDefaultSByte,
                DataType.GDT_Int16 => RasterBand.NoDataDefaultInt16,
                DataType.GDT_Int32 => RasterBand.NoDataDefaultInt32,
                DataType.GDT_Int64 => RasterBand.NoDataDefaultInt64,
                DataType.GDT_UInt16 => RasterBand.NoDataDefaultUInt16,
                DataType.GDT_UInt32 => RasterBand.NoDataDefaultUInt32,
                DataType.GDT_UInt64 => RasterBand.NoDataDefaultUInt64,
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and GDT_TypeCount not currently reachable
                _ => throw new NotSupportedException("Unhandled data type " + gdalType + ".")
            };
        }

        public abstract DataType GetGdalDataType();
        public abstract double GetNoDataValueAsDouble();

        public static DataType GetGdalDataType<TBand>() where TBand : INumber<TBand>
        {
            return Type.GetTypeCode(typeof(TBand)) switch
            {
                TypeCode.Byte => DataType.GDT_Byte,
                TypeCode.Double => DataType.GDT_Float64,
                TypeCode.Int16 => DataType.GDT_Int16,
                TypeCode.Int32 => DataType.GDT_Int32,
                TypeCode.Int64 => DataType.GDT_Int64,
                TypeCode.SByte => DataType.GDT_Int8,
                TypeCode.Single => DataType.GDT_Float32,
                TypeCode.UInt16 => DataType.GDT_UInt16,
                TypeCode.UInt32 => DataType.GDT_UInt32,
                TypeCode.UInt64 => DataType.GDT_UInt64,
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and GDT_TypeCount not currently supported
                _ => throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".")
            };
        }

        public abstract RasterBandStatistics GetStatistics();

        public abstract bool IsNoData(int xIndex, int yIndex);

        public void Read(Dataset rasterDataset)
        {
            this.Read(rasterDataset, rasterDataset.GetSpatialRef());
        }

        public void Read(Dataset rasterDataset, SpatialReference crs)
        {
            // update CRS and transform
            this.Crs = crs;
            this.Transform.CopyFrom(rasterDataset);
            // update data and no data
            this.ReadDataInSameCrsAndTransform(rasterDataset);
        }

        public void Read(string rasterPath)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            this.Read(rasterDataset);
            // leave multiband datasets in cache as .tif interleave makes it very likely other bands were read into the cache
            // Probably if this band's being read other bands will be read too.
            // Could flush cache for single band datasets, if needed.
        }

        public abstract void ReadDataInSameCrsAndTransform(Dataset rasterDataset);

        protected static void ReadDataAssumingSameCrsTransformSizeAndNoData<TBand>(Band gdalBand, DataType bufferDataType, TBand[] buffer) where TBand : INumber<TBand>
        {
            GCHandle dataPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                // read entire band from GDAL's cache at once
                CPLErr gdalErrorCode2 = gdalBand.ReadRaster(xOff: 0, yOff: 0, xSize: gdalBand.XSize, ySize: gdalBand.YSize, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: gdalBand.XSize, buf_ySize: gdalBand.YSize, buf_type: bufferDataType, pixelSpace: 0, lineSpace: 0);
                GdalException.ThrowIfError(gdalErrorCode2, nameof(gdalBand.ReadRaster));

                // read entire band in block y size increments
                // GDAL documentation recommends this approach for performance but's no faster than entire band reads and increases DDR bandwidth
                // demand. Reading many blocks, but not the entire band, hasn't been profiled to see if there's any advantage.
                //gdalBand.GetBlockSize(out int blockXsize, out int blockYsize);
                //nint dataPointer = dataPin.AddrOfPinnedObject();
                //nint yBlockedBufferStrideInBytes = gdalBand.XSize * blockYsize * gdalBand.DataType.GetSizeInBytes();
                //for (int yOffset = 0; yOffset < gdalBand.YSize; dataPointer += yBlockedBufferStrideInBytes, yOffset += blockYsize)
                //{
                //    CPLErr gdalErrorCode = gdalBand.ReadRaster(xOff: 0, yOff: yOffset, xSize: gdalBand.XSize, ySize: blockYsize, buffer: dataPointer, buf_xSize: gdalBand.XSize, buf_ySize: blockYsize, buf_type: bufferDataType, pixelSpace: 0, lineSpace: 0);
                //    GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.ReadRaster));
                //}

                // for debugging: check consistency of blocked and whole band reads
                //int cells = gdalBand.XSize * gdalBand.YSize;
                //TBand[] buffer2 = new TBand[cells];
                //GCHandle dataPin2 = GCHandle.Alloc(buffer2, GCHandleType.Pinned);
                //CPLErr gdalErrorCode2 = gdalBand.ReadRaster(xOff: 0, yOff: 0, xSize: gdalBand.XSize, ySize: gdalBand.YSize, buffer: dataPin2.AddrOfPinnedObject(), buf_xSize: gdalBand.XSize, buf_ySize: gdalBand.YSize, buf_type: bufferDataType, pixelSpace: 0, lineSpace: 0);
                //GdalException.ThrowIfError(gdalErrorCode2, nameof(gdalBand.ReadRaster));
                //dataPin2.Free();
                //for (int index = 0; index < cells; ++index)
                //{
                //    if (buffer[index] != buffer2[index])
                //    {
                //        int q = 0;
                //    }
                //}
            }
            finally
            {
                dataPin.Free();
            }
        }

        public abstract void ReturnData(RasterBandPool dataBufferPool);

        //  require derived classes to override as this.NoDataIsNaN can't set when data type is not known
        public abstract void Reset(SpatialReference crs, GridGeoTransform transform, Band gdalBand, bool readData);

        public static double ResolveSignedIntegerNoDataValue(List<double> candidateNoDataValues, double minValue, double maxValue)
        {
            Debug.Assert((candidateNoDataValues.Count > 1) && (Int64.MinValue <= minValue) && (minValue < maxValue) && (maxValue <= Int64.MaxValue));

            // find extent of candidate values
            double maxCandidateValue = Double.MinValue;
            double minCandidateValue = Double.MaxValue;
            for (int valueIndex = 0; valueIndex < candidateNoDataValues.Count; ++valueIndex)
            {
                double noDataValue = candidateNoDataValues[valueIndex];
                if ((noDataValue < minValue) || (noDataValue > maxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(candidateNoDataValues), "Candidate no data value " + noDataValue + " is not in the interval [ " + minValue + ", " + maxValue + "].");
                }

                if (noDataValue < minCandidateValue)
                {
                    minCandidateValue = noDataValue;
                }
                if (noDataValue > maxCandidateValue)
                {
                    maxCandidateValue = noDataValue;
                }
            }

            // if candidate values favor a low side convention, resolve to the most negative available value
            if (maxCandidateValue < 0.0)
            {
                return minCandidateValue;
            }
            // presume high side convention
            if (minCandidateValue > 0.0)
            {
                return maxCandidateValue;
            }

            throw new NotSupportedException("Unable to determine signed integer no data value assignment convention. Candidate signed integer no data values range from " + minCandidateValue + " to " + maxCandidateValue + " within limits [" + minValue + ", " + maxValue + "].");
        }

        public static double ResolveUnsignedIntegerNoDataValue(List<double> candidateNoDataValues, double maxValue)
        {
            Debug.Assert((candidateNoDataValues.Count > 1) && (0.0 <= maxValue) && (maxValue <= UInt64.MaxValue));

            double maxCandidateValue = Double.MinValue;
            double minCandidateValue = Double.MaxValue;
            for (int valueIndex = 0; valueIndex < candidateNoDataValues.Count; ++valueIndex)
            {
                double noDataValue = candidateNoDataValues[valueIndex];
                if ((noDataValue < 0.0) || (noDataValue > maxValue))
                {
                    throw new ArgumentOutOfRangeException(nameof(candidateNoDataValues), "Candidate no data value " + noDataValue + " is not in the interval [ 0.0, " + maxValue + "].");
                }

                if (noDataValue > maxCandidateValue)
                {
                    maxCandidateValue = noDataValue;
                }
                if (noDataValue < minCandidateValue)
                {
                    minCandidateValue = noDataValue;
                }
            }

            // for now, assume high side no data convention if multiple values are present
            // A low side convention would set all values to zero, in which case resolution is not needed.
            return maxCandidateValue;
        }

        public abstract bool TryTakeOwnershipOfDataBuffer(RasterBandPool pool);
    }

    public class RasterBand<TBand> : RasterBand where TBand : IMinMaxValue<TBand>, INumber<TBand>
    {
        public TBand[] Data { get; private set; }
        public TBand NoDataValue { get; private set; }

        // band is loaded from a GDAL dataset
        public RasterBand(Dataset rasterDataset, Band gdalBand, bool readData)
            : base(rasterDataset, gdalBand)
        {
            long totalCells = (long)rasterDataset.RasterXSize * (long)rasterDataset.RasterYSize;
            if (totalCells > Array.MaxLength)
            {
                throw new NotSupportedException("Raster '" + rasterDataset.GetFirstFile() + "' has " + totalCells.ToString("n0") + " cells, which exceeds the maximum supported size of " + Array.MaxLength.ToString("n0") + " cells.");
            }

            DataType thisDataType = RasterBand.GetGdalDataType<TBand>();
            if ((gdalBand.DataType != thisDataType) && (DataTypeExtensions.IsExactlyExpandable(gdalBand.DataType, thisDataType) == false))
            {
                // debatable if this error should be thrown when loadData is false
                // For now, assume it's preferable not to defer detection.
                string message = "A RasterBand<" + typeof(TBand).Name + "> cannot be loaded from '" + rasterDataset.GetFirstFile() + "' because band '" + gdalBand.GetDescription() + "' is of type " + gdalBand.DataType + ".";
                throw new NotSupportedException(message);
            }

            this.SetNoDataValue(gdalBand);

            this.Data = [];
            if (readData)
            {
                this.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, thisDataType);
            }
        }

        // band is created in memory
        public RasterBand(Raster raster, string name, RasterBandInitialValue initialValue)
            : this(raster, name, initialValue, null)
        {
        }

        public RasterBand(Raster raster, string name, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool)
            : this(raster, name, RasterBand<TBand>.GetDefaultNoDataValue(), initialValue, dataBufferPool)
        {
            // revert no data status but leave default no data value in place in case HasNoDataValue is later set to true
            this.HasNoDataValue = false;
        }

        public RasterBand(Raster raster, string name, TBand noDataValue, RasterBandInitialValue initialValue)
            : this(raster, name, noDataValue, initialValue, null)
        {
        }

        public RasterBand(Raster raster, string name, TBand noDataValue, RasterBandInitialValue initialValue, RasterBandPool? dataBufferPool)
            : base(raster, name)
        {
            if ((dataBufferPool != null) && this.TryTakeOwnershipOfDataBuffer(dataBufferPool))
            {
                switch (initialValue)
                {
                    case RasterBandInitialValue.Default:
                        Array.Fill(this.Data, default);
                        break;
                    case RasterBandInitialValue.NoData:
                        Array.Fill(this.Data, noDataValue);
                        break;
                    case RasterBandInitialValue.Unintialized:
                        // nothing to do
                        break;
                    default:
                        throw new NotSupportedException("Unhandled initial band data option " + initialValue + ".");
                }

            }
            else
            {
                switch (initialValue)
                {
                    case RasterBandInitialValue.Default:
                        this.Data = new TBand[this.Cells];
                        break;
                    case RasterBandInitialValue.NoData:
                        this.Data = GC.AllocateUninitializedArray<TBand>(this.Cells);
                        Array.Fill(this.Data, noDataValue);
                        break;
                    case RasterBandInitialValue.Unintialized:
                        this.Data = GC.AllocateUninitializedArray<TBand>(this.Cells);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled initial band data option " + initialValue + ".");
                }
            }

            this.HasNoDataValue = true;
            this.NoDataIsNaN = TBand.IsNaN(noDataValue);
            this.NoDataValue = noDataValue;
        }

        public RasterBand(Raster raster, string name, TBand noDataValue, TBand? initialValue)
            : this(raster, name, noDataValue, RasterBandInitialValue.Unintialized)
        {
            if (noDataValue == default)
            {
                this.Data = new TBand[this.Cells];
            }
            else
            {
                this.Data = GC.AllocateUninitializedArray<TBand>(this.Cells);
                Array.Fill(this.Data, initialValue);
            }

            this.HasNoDataValue = true;
            this.NoDataIsNaN = TBand.IsNaN(noDataValue);
            this.NoDataValue = noDataValue;
        }

        public TBand this[int cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TBand this[Int64 cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TBand this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this[this.ToCellIndex(xIndex, yIndex)] = value; }
        }

        public TBand this[Int64 xIndex, Int64 yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this[this.ToCellIndex(xIndex, yIndex)] = value; }
        }

        public override bool HasData 
        { 
            get { return this.Data.Length > 0; }
        }

        public static RasterBand<TBand> CreateOrLoad(string rasterPath, string? bandNameForCreate, RasterBand<TBand>? band)
        {
            if (band == null)
            {
                return RasterBand<TBand>.Read(rasterPath, bandNameForCreate);
            }
            else
            {
                band.Read(rasterPath);
            }

            return band;
        }

        public static TBand GetDefaultNoDataValue()
        {
            return Type.GetTypeCode(typeof(TBand)) switch
            {
                TypeCode.Byte => TBand.CreateChecked(RasterBand.NoDataDefaultByte),
                TypeCode.Double => TBand.CreateChecked(RasterBand.NoDataDefaultDouble),
                TypeCode.Int16 => TBand.CreateChecked(RasterBand.NoDataDefaultInt16),
                TypeCode.Int32 => TBand.CreateChecked(RasterBand.NoDataDefaultInt32),
                TypeCode.Int64 => TBand.CreateChecked(RasterBand.NoDataDefaultInt64),
                TypeCode.SByte => TBand.CreateChecked(RasterBand.NoDataDefaultSByte),
                TypeCode.Single => TBand.CreateChecked(RasterBand.NoDataDefaultFloat),
                TypeCode.UInt16 => TBand.CreateChecked(RasterBand.NoDataDefaultUInt16),
                TypeCode.UInt32 => TBand.CreateChecked(RasterBand.NoDataDefaultUInt32),
                TypeCode.UInt64 => TBand.CreateChecked(RasterBand.NoDataDefaultUInt64),
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and GDT_TypeCount not currently reachable
                _ => throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".")
            };
        }

        public override DataType GetGdalDataType()
        {
            return RasterBand.GetGdalDataType<TBand>();
        }

        public override double GetNoDataValueAsDouble()
        {
            if (this.HasNoDataValue)
            {
                return Double.CreateChecked(this.NoDataValue);
            }

            throw new InvalidOperationException("No data value requested but band does not have a no data value.");
        }

        private GCHandle GetPinnedDataHandleWithRetypedNoData<TOutput>(TOutput outputNoDataValue) where TOutput : INumber<TOutput>
        {
            TypeCode thisDataType = Type.GetTypeCode(typeof(TBand));
            TypeCode outputDataType = Type.GetTypeCode(typeof(TOutput));
            if (thisDataType == outputDataType)
            {
                // no type conversion needed
                return GCHandle.Alloc(this.Data, GCHandleType.Pinned);
            }

            // type conversion needed
            TOutput[] retypedData = new TOutput[this.Cells];
            if (this.HasNoDataValue)
            {
                bool outputNoDataIsNaN = TOutput.IsNaN(outputNoDataValue);
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    TBand value = this.Data[cellIndex];
                    if (this.IsNoData(value))
                    {
                        retypedData[cellIndex] = outputNoDataValue;
                    }
                    else
                    {
                        TOutput outputValue = TOutput.CreateChecked(value);
                        bool valueCollapsesToNoData = outputNoDataIsNaN ? TOutput.IsNaN(outputValue) : outputValue == outputNoDataValue; // same as IsNoData()
                        if (valueCollapsesToNoData)
                        {
                            throw new NotSupportedException("Data value " + value + " in cell " + cellIndex + " converts to " + outputValue + ", which is the same as the no data value " + outputNoDataValue + ".");
                        }

                        retypedData[cellIndex] = outputValue;
                    }
                }
            }
            else
            {
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    retypedData[cellIndex] = TOutput.CreateChecked(this.Data[cellIndex]);
                }
            }

            return GCHandle.Alloc(retypedData, GCHandleType.Pinned);
        }

        public override RasterBandStatistics GetStatistics()
        {
            switch (Type.GetTypeCode(typeof(TBand)))
            {
                case TypeCode.Byte:
                    byte[]? byteData = this.Data as byte[];
                    Debug.Assert(byteData != null);
                    return new(byteData, this.HasNoDataValue, Byte.CreateChecked(this.NoDataValue));
                case TypeCode.Double:
                    double[]? doubleData = this.Data as double[];
                    Debug.Assert(doubleData != null);
                    return new(doubleData, this.HasNoDataValue, Double.CreateChecked(this.NoDataValue));
                case TypeCode.Int16:
                    Int16[]? int16data = this.Data as Int16[];
                    Debug.Assert(int16data != null);
                    return new(int16data, this.HasNoDataValue, Int16.CreateChecked(this.NoDataValue));
                case TypeCode.Int32:
                    Int32[]? int32data = this.Data as Int32[];
                    Debug.Assert(int32data != null);
                    return new(int32data, this.HasNoDataValue, Int32.CreateChecked(this.NoDataValue));
                case TypeCode.Int64:
                    Int64[]? int64data = this.Data as Int64[];
                    Debug.Assert(int64data != null);
                    return new(int64data, this.HasNoDataValue, Int64.CreateChecked(this.NoDataValue));
                case TypeCode.SByte:
                    sbyte[]? sbyteData = this.Data as sbyte[];
                    Debug.Assert(sbyteData != null);
                    return new(sbyteData, this.HasNoDataValue, SByte.CreateChecked(this.NoDataValue));
                case TypeCode.Single:
                    float[]? floatData = this.Data as float[];
                    Debug.Assert(floatData != null);
                    return new(floatData, this.HasNoDataValue, Single.CreateChecked(this.NoDataValue));
                case TypeCode.UInt16:
                    UInt16[]? uint16data = this.Data as UInt16[];
                    Debug.Assert(uint16data != null);
                    return new(uint16data, this.HasNoDataValue, UInt16.CreateChecked(this.NoDataValue));
                case TypeCode.UInt32:
                    UInt32[]? uint32data = this.Data as UInt32[];
                    Debug.Assert(uint32data != null);
                    return new(uint32data, this.HasNoDataValue, UInt32.CreateChecked(this.NoDataValue));
                case TypeCode.UInt64:
                    UInt64[]? uint64data = this.Data as UInt64[];
                    Debug.Assert(uint64data != null);
                    return new(uint64data, this.HasNoDataValue, UInt64.CreateChecked(this.NoDataValue));
                // complex numbers (GDT_CInt16, 32, CFloat32, 64) and GDT_TypeCount not currently supported
                default:
                    throw new NotSupportedException("Unhandled data type " + Type.GetTypeCode(typeof(TBand)) + ".");
            }
        }

        public (TBand value, byte mask) GetValueMaskZero(int xIndex, int yIndex)
        {
            TBand value = this[xIndex, yIndex];
            if (this.IsNoData(value))
            {
                return (TBand.Zero, 0);
            }

            return (value, 1);
        }

        public bool IsNoData(Band gdalBand)
        {
            if (this.HasNoDataValue)
            {
                gdalBand.GetNoDataValue(out double otherNoDataValue, out int otherHasNoDataValue);
                if (otherHasNoDataValue == 0)
                {
                    return false;
                }

                TBand otherNoDataAsBandValue = TBand.CreateChecked(otherNoDataValue);
                return this.NoDataIsNaN ? TBand.IsNaN(otherNoDataAsBandValue) : this.NoDataValue == otherNoDataAsBandValue;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNoData(TBand value)
        {
            if (this.HasNoDataValue)
            {
                return this.NoDataIsNaN ? TBand.IsNaN(value) : this.NoDataValue == value; // have to test with IsNaN() since float.NaN == float.NaN = false
            }
            return false;
        }

        public override bool IsNoData(int xIndex, int yIndex)
        {
            if (this.HasNoDataValue)
            {
                return this.IsNoData(this[xIndex, yIndex]);
            }
            return false;
        }

        public void Read(Dataset rasterDataset, SpatialReference crs, Band gdalBand)
        {
            this.Crs = crs;
            this.Transform.CopyFrom(rasterDataset);
            this.SizeX = rasterDataset.RasterXSize;
            this.SizeY = rasterDataset.RasterYSize;
            this.SetNoDataValue(gdalBand); // also update no data

            DataType thisDataType = RasterBand.GetGdalDataType<TBand>();
            this.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, thisDataType); // update data
        }

        public static RasterBand<TBand> Read(string rasterPath, string? bandName)
        {
            using Dataset rasterDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly); // see cache management remarks in RasterBand.Read()
            if (rasterDataset.RasterCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(rasterPath), "Raster '" + rasterPath + "' contains no bands.");
            }

            if (bandName == null)
            {
                using Band gdalBand1 = rasterDataset.GetRasterBand(1);
                return new RasterBand<TBand>(rasterDataset, gdalBand1, readData: true);
            }

            for (int bandIndex = 0; bandIndex < rasterDataset.RasterCount; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                if (String.Equals(gdalBand.GetDescription(), bandName, StringComparison.Ordinal))
                {
                    return new RasterBand<TBand>(rasterDataset, gdalBand, readData: true);
                }
            }

            throw new ArgumentOutOfRangeException(nameof(bandName), "Raster '" + rasterPath + "' does not contain a band named '" + bandName + "'.");
        }

        public override void ReadDataInSameCrsAndTransform(Dataset rasterDataset)
        {
            if ((this.SizeX != rasterDataset.RasterXSize) || (this.SizeY != rasterDataset.RasterYSize))
            {
                throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster is " + rasterDataset.RasterXSize + " by " + rasterDataset.RasterYSize + " cells but band is " + this.SizeX + " by " + this.SizeY + " cells.");
            }

            for (int bandIndex = 0; bandIndex < rasterDataset.RasterCount; ++bandIndex)
            {
                int gdalBandIndex = bandIndex + 1;
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                if (String.Equals(bandName, this.Name, StringComparison.Ordinal))
                {
                    DataType thisDataType = RasterBand.GetGdalDataType<TBand>();
                    this.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, thisDataType); // update data
                    this.SetNoDataValue(gdalBand); // also update no data
                    return;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(rasterDataset), "Raster does not contain a band named '" + this.Name + "'.");
        }

        public void ReadDataAssumingSameCrsTransformSizeAndNoData(Band gdalBand)
        {
            this.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, RasterBand.GetGdalDataType<TBand>());
        }

        private void ReadDataAssumingSameCrsTransformSizeAndNoData(Band gdalBand, DataType thisDataType)
        {
            // callers check (or ensure) x and y sizes match
            if (this.Data.Length != this.Cells)
            {
                // no need to zero data as it's filled by the following ReadData() or Convert() call
                // (Assume raster is large enough allocate uninitialized outperforms new.)
                this.Data = GC.AllocateUninitializedArray<TBand>(this.Cells);
            }

            if (gdalBand.DataType == thisDataType)
            {
                RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, thisDataType, this.Data);
            }
            else
            {
                // no data values are left unchanged
                // TODO: evaluate GDAL's data type translation in its RasterIO()
                switch (gdalBand.DataType)
                {
                    case DataType.GDT_Byte:
                        byte[] bufferUInt8 = GC.AllocateUninitializedArray<byte>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferUInt8);
                        DataTypeExtensions.Convert(bufferUInt8, this.Data);
                        break;
                    case DataType.GDT_Int8:
                        sbyte[] bufferInt8 = GC.AllocateUninitializedArray<sbyte>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferInt8);
                        DataTypeExtensions.Convert(bufferInt8, this.Data);
                        break;
                    case DataType.GDT_Int16:
                        Int16[] bufferInt16 = GC.AllocateUninitializedArray<Int16>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferInt16);
                        DataTypeExtensions.Convert(bufferInt16, this.Data);
                        break;
                    case DataType.GDT_Int32:
                        Int32[] bufferInt32 = GC.AllocateUninitializedArray<Int32>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferInt32);
                        DataTypeExtensions.Convert(bufferInt32, this.Data);
                        break;
                    case DataType.GDT_UInt16:
                        UInt16[] bufferUInt16 = GC.AllocateUninitializedArray<UInt16>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferUInt16);
                        DataTypeExtensions.Convert(bufferUInt16, this.Data);
                        break;
                    case DataType.GDT_UInt32:
                        UInt32[] bufferUInt32 = GC.AllocateUninitializedArray<UInt32>(this.Cells);
                        RasterBand.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand, gdalBand.DataType, bufferUInt32);
                        DataTypeExtensions.Convert(bufferUInt32, this.Data);
                        break;
                    default:
                        throw new NotSupportedException("Cannot expand band source data type " + gdalBand.DataType + " to " + thisDataType + ".");
                }
            }
        }

        public override void ReturnData(RasterBandPool dataBufferPool)
        {
            if (this.Data.Length > 0)
            {
                dataBufferPool.Return(this.GetGdalDataType(), this.Data);
                this.Data = [];
            }
            // otherwise no data to return
        }

        public override void Reset(SpatialReference crs, GridGeoTransform transform, Band gdalBand, bool readData)
        {
            this.Crs = crs;
            this.Name = gdalBand.GetDescription();
            this.Transform = transform;
            this.SetNoDataValue(gdalBand); // this.HasNoDataValue, NoDataValue, NoDataIsNaN

            if (readData)
            {
                this.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
            }
        }

        [MemberNotNull(nameof(RasterBand<TBand>.NoDataValue))]
        private void SetNoDataValue(Band gdalBand)
        {
            gdalBand.GetNoDataValue(out double noDataValue, out int hasNoDataValue);

            this.HasNoDataValue = hasNoDataValue != 0;
            if (this.HasNoDataValue)
            {
                this.NoDataValue = TBand.CreateChecked(noDataValue);
            }
            else
            {
                this.NoDataValue = RasterBand<TBand>.GetDefaultNoDataValue();
            }
            this.NoDataIsNaN = TBand.IsNaN(this.NoDataValue);
        }

        public void SetNoDataValue(TBand noDataValue)
        {
            this.HasNoDataValue = true;
            this.NoDataIsNaN = TBand.IsNaN(this.NoDataValue);
            this.NoDataValue = noDataValue;
        }

        public void Slice(int xOrigin, int yOrigin, int sizeX, int sizeY, Span<TBand> slice)
        {
            int xMax = xOrigin + sizeX; // exclusive
            int yMax = yOrigin + sizeY; // exclusive
            if ((xOrigin < 0) || (xMax > this.SizeX) || (yOrigin < 0) || (yMax > this.SizeY))
            {
                throw new ArgumentException("Slice origin (" + xOrigin + ", " + yOrigin + ") and dimensions (" + sizeX + " by " + sizeY + ") do not fit within raster band dimensions (" + this.SizeX + ", " + this.SizeY + ".");
            }

            int sliceDestinationIndex = 0;
            int sliceRowStartIndex = this.ToCellIndex(xOrigin, yOrigin);
            for (int yIndex = yOrigin; yIndex < yMax; ++yIndex)
            {
                this.Data.AsSpan(sliceRowStartIndex, sizeX).CopyTo(slice[sliceDestinationIndex..]);
                sliceDestinationIndex += sizeX;
                sliceRowStartIndex += this.SizeX;
            }
        }

        public bool TryGetMaximumValue(out TBand maximumValue)
        {
            maximumValue = TBand.MinValue;
            if (this.HasNoDataValue)
            {
                maximumValue = this.NoDataValue;
            }

            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                TBand value = this[cellIndex];
                if (this.IsNoData(value))
                {
                    continue;
                }

                if (this.IsNoData(maximumValue) || (value > maximumValue))
                {
                    maximumValue = value;
                }
            }

            return this.IsNoData(maximumValue) == false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int indexX, int indexY, out TBand value)
        {
            value = this[indexX, indexY];
            return this.IsNoData(value) == false;
        }

        [MemberNotNullWhen(true, nameof(RasterBand<TBand>.Data))] 
        public override bool TryTakeOwnershipOfDataBuffer(RasterBandPool pool)
        {
            if ((this.Data != null) && (this.Data.Length > 0)) // called from constructor so this.Data may be null
            {
                return false;
            }

            DataType gdalType = this.GetGdalDataType();
            switch (gdalType)
            {
                case DataType.GDT_Byte:
                    if (pool.BytePool.TryGet(out byte[]? byteBuffer))
                    {
                        TBand[]? retypedBuffer = byteBuffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Float32:
                    if (pool.FloatPool.TryGet(out float[]? floatBuffer))
                    {
                        TBand[]? retypedBuffer = floatBuffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Float64:
                    if (pool.DoublePool.TryGet(out double[]? doubleBuffer))
                    {
                        TBand[]? retypedBuffer = doubleBuffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Int8:
                    if (pool.Int8Pool.TryGet(out sbyte[]? int8buffer))
                    {
                        TBand[]? retypedBuffer = int8buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Int16:
                    if (pool.Int16Pool.TryGet(out Int16[]? int16buffer))
                    {
                        TBand[]? retypedBuffer = int16buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Int32:
                    if (pool.Int32Pool.TryGet(out Int32[]? int32buffer))
                    {
                        TBand[]? retypedBuffer = int32buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_Int64:
                    if (pool.Int64Pool.TryGet(out Int64[]? int64buffer))
                    {
                        TBand[]? retypedBuffer = int64buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_UInt16:
                    if (pool.UInt16Pool.TryGet(out UInt16[]? UInt16buffer))
                    {
                        TBand[]? retypedBuffer = UInt16buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_UInt32:
                    if (pool.UInt32Pool.TryGet(out UInt32[]? UInt32buffer))
                    {
                        TBand[]? retypedBuffer = UInt32buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                case DataType.GDT_UInt64:
                    if (pool.UInt64Pool.TryGet(out UInt64[]? UInt64buffer))
                    {
                        TBand[]? retypedBuffer = UInt64buffer as TBand[];
                        Debug.Assert(retypedBuffer != null);
                        this.Data = retypedBuffer;
                        return true;
                    }
                    break;
                default:
                    throw new NotSupportedException("Unhandled data type " + gdalType + ".");
            }

            return false;
        }

        public void Write(Dataset rasterDataset, int gdalBandIndex)
        {
            Debug.Assert(this.HasData);

            using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
            gdalBand.SetDescription(this.Name);

            GCHandle dataPin;
            DataType thisGdalDataType = this.GetGdalDataType();
            if (thisGdalDataType == gdalBand.DataType)
            {
                // no type conversion necessary: pass through any no data value and pin existing data array
                if (this.HasNoDataValue)
                {
                    CPLErr gdalErrorCode = gdalBand.SetNoDataValue(this.GetNoDataValueAsDouble());
                    GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                }
                dataPin = GCHandle.Alloc(this.Data, GCHandleType.Pinned);
            }
            else
            {
                // type conversion needed: currently only integer compactions are supported
                bool noDataIsSaturating = false; // true if saturating from below (signed integers) or from above (unsigned integers)
                switch (gdalBand.DataType)
                {
                    case DataType.GDT_Int8:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = (this.NoDataValue < TBand.CreateChecked(SByte.MinValue)) || (TBand.CreateChecked(SByte.MaxValue) < this.NoDataValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(SByte.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        sbyte[] bufferInt8 = GC.AllocateUninitializedArray<sbyte>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferInt8, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferInt8, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_Int16:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = (this.NoDataValue < TBand.CreateChecked(Int16.MinValue)) || (TBand.CreateChecked(Int16.MaxValue) < this.NoDataValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(Int16.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        Int16[] bufferInt16 = GC.AllocateUninitializedArray<Int16>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferInt16, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferInt16, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_Int32:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = (this.NoDataValue < TBand.CreateChecked(Int32.MinValue)) || (TBand.CreateChecked(Int32.MaxValue) < this.NoDataValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(Int32.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        Int32[] bufferInt32 = GC.AllocateUninitializedArray<Int32>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferInt32, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferInt32, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_Byte:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = this.NoDataValue > TBand.CreateChecked(Byte.MaxValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(Byte.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        byte[] bufferUInt8 = GC.AllocateUninitializedArray<byte>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferUInt8, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferUInt8, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_UInt16:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = this.NoDataValue > TBand.CreateChecked(UInt16.MaxValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(UInt16.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        UInt16[] bufferUInt16 = GC.AllocateUninitializedArray<UInt16>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferUInt16, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferUInt16, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_UInt32:
                        if (this.HasNoDataValue)
                        {
                            noDataIsSaturating = this.NoDataValue > TBand.CreateChecked(UInt32.MaxValue);
                            CPLErr gdalErrorCode = gdalBand.SetNoDataValue(UInt32.CreateSaturating(this.NoDataValue));
                            GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.SetNoDataValue));
                        }
                        UInt32[] bufferUInt32 = GC.AllocateUninitializedArray<UInt32>(this.Cells);
                        DataTypeExtensions.Pack(this.Data, bufferUInt32, noDataIsSaturating);
                        dataPin = GCHandle.Alloc(bufferUInt32, GCHandleType.Pinned);
                        break;
                    case DataType.GDT_Int64: // not a compaction target, should be Int64 passthrough
                    case DataType.GDT_Float32: // not compatable
                    case DataType.GDT_Float64: // lossy compaction not currently supported (is AVX convertible; vcvtpd2ps)
                    case DataType.GDT_UInt64: // not a compaction target, should be UInt64 passthrough
                    default:
                        throw new NotSupportedException("Unhandled conversion of " + thisGdalDataType + " raster band to " + gdalBand.DataType + ".");
                }
            }

            // write data with unchanged type or with type compacted to match raster band
            try
            {
                CPLErr gdalErrorCode = gdalBand.WriteRaster(xOff: 0, yOff: 0, xSize: this.SizeX, ySize: this.SizeY, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: this.SizeX, buf_ySize: this.SizeY, buf_type: gdalBand.DataType, pixelSpace: 0, lineSpace: 0);
                GdalException.ThrowIfError(gdalErrorCode, nameof(rasterDataset.WriteRaster));
            }
            finally
            {
                dataPin.Free();
            }
        }
    }
}
