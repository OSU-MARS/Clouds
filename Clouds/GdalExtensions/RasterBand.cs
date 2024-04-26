using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
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

        protected RasterBand(Dataset rasterDataset, Band gdalBand)
            : base(rasterDataset.GetSpatialRef(), new GridGeoTransform(rasterDataset), gdalBand.XSize, gdalBand.YSize, cloneCrsAndTransform: false)
        {
            this.NoDataIsNaN = false;
            this.HasNoDataValue = false;
            this.Name = gdalBand.GetDescription();
        }

        protected RasterBand(string name, Raster raster)
            : base(raster, cloneCrsAndTransform: false) // RasterBands all share same CRS and geotransform by reference
        {
            this.NoDataIsNaN = false;
            this.HasNoDataValue = false;
            this.Name = name;
        }

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
        public abstract GCHandle GetPinnedDataHandle<TOutput>(TOutput noDataValue) where TOutput : INumber<TOutput>;

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

        public abstract bool IsNoData(int xIndex, int yIndex);
    }

    public class RasterBand<TBand> : RasterBand where TBand : INumber<TBand>
    {
        public TBand[] Data { get; private init; }
        public TBand NoDataValue { get; private set; }

        public RasterBand(Dataset rasterDataset, Band gdalBand)
            : base(rasterDataset, gdalBand)
        {
            DataType gdalDataType = RasterBand.GetGdalDataType<TBand>();
            if (gdalBand.DataType != gdalDataType)
            {
                string[] sourceFiles = gdalBand.GetDataset().GetFileList();
                string message = "A RasterBand<" + typeof(TBand).Name + "> cannot be loaded " + (sourceFiles.Length > 0 ? "from '" + sourceFiles[0] + "'" : String.Empty) + " because band '" + gdalBand.GetDescription() + "' is of type " + gdalBand.DataType + ".";
                throw new NotSupportedException(message);
            }

            this.SetNoDataValue(gdalBand);
            this.Data = new TBand[gdalBand.XSize * gdalBand.YSize];

            // for now, load all raster data on creation
            // In some use cases or failure paths it's desirable to load the band lazily but this isn't currently supported.
            // See https://stackoverflow.com/questions/15958830/c-sharp-generics-cast-generic-type-to-value-type for discussion of
            // C# generics' limitations in recognizing TBand[] as float[], int[], ...
            GCHandle dataPin = GCHandle.Alloc(this.Data, GCHandleType.Pinned);
            try
            {
                CPLErr gdalErrorCode = gdalBand.ReadRaster(xOff: 0, yOff: 0, xSize: this.SizeX, ySize: this.SizeY, buffer: dataPin.AddrOfPinnedObject(), buf_xSize: this.SizeX, buf_ySize: this.SizeY, buf_type: gdalBand.DataType, pixelSpace: 0, lineSpace: 0);
                GdalException.ThrowIfError(gdalErrorCode, nameof(gdalBand.ReadRaster));
            }
            finally
            {
                dataPin.Free();
            }
        }

        public RasterBand(Band gdalBand, Raster raster)
            : this(gdalBand.GetDescription(), raster)
        {
            this.SetNoDataValue(gdalBand);
        }

        public RasterBand(string name, Raster raster, TBand noDataValue)
            : base(name, raster)
        {
            this.Data = new TBand[this.SizeX * this.SizeY];
            this.HasNoDataValue = true;
            this.NoDataIsNaN = TBand.IsNaN(noDataValue);
            this.NoDataValue = noDataValue;
        }

        public RasterBand(string name, Raster raster)
            : this(name, raster, RasterBand<TBand>.GetDefaultNoDataValue())
        {
            // change this.HasNoDataValue back to false as the caller did not specify a no data value
            // Leave default no data value in case caller sets HasNoDataValue but not NoDataValue.
            this.HasNoDataValue = false;
        }

        public TBand this[int cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TBand this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this[this.ToCellIndex(xIndex, yIndex)] = value; }
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

        public override GCHandle GetPinnedDataHandle<TOutput>(TOutput outputNoDataValue)
        {
            DataType thisDataType = this.GetGdalDataType();
            DataType outputDataType = RasterBand.GetGdalDataType<TOutput>();
            if (thisDataType == outputDataType)
            {
                return GCHandle.Alloc(this.Data, GCHandleType.Pinned);
            }

            bool outputNoDataIsNaN = TOutput.IsNaN(outputNoDataValue);
            TOutput[] retypedData = new TOutput[this.Cells];
            if (this.HasNoDataValue)
            {
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

        public (TBand value, byte mask) GetValueMaskZero(int xIndex, int yIndex)
        {
            TBand value = this[xIndex, yIndex];
            if (this.IsNoData(value))
            {
                return (TBand.Zero, 0);
            }

            return (value, 1);
        }

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
    }
}
