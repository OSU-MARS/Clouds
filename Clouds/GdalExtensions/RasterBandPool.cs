using Mars.Clouds.DiskSpd;
using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using System;
using System.Diagnostics;

namespace Mars.Clouds.GdalExtensions
{
    /// <summary>
    /// Pools of raster band data buffers by data type.
    /// </summary>
    /// <remarks>
    /// Currently leaves all array length management to the caller. Does not integrate with <see cref="System.Buffers.ArrayPool{T}.Shared"/>
    /// as its implementation is thread local (albeit not documented as such).
    /// </remarks>
    public class RasterBandPool
    {
        public ObjectPool<byte[]> BytePool { get; set; }
        public ObjectPool<double[]> DoublePool { get; set; }
        public ObjectPool<float[]> FloatPool { get; set; }
        public ObjectPool<sbyte[]> Int8Pool { get; set; }
        public ObjectPool<Int16[]> Int16Pool { get; set; }
        public ObjectPool<Int32[]> Int32Pool { get; set; }
        public ObjectPool<Int64[]> Int64Pool { get; set; }
        public ObjectPool<UInt16[]> UInt16Pool { get; set; }
        public ObjectPool<UInt32[]> UInt32Pool { get; set; }
        public ObjectPool<UInt64[]> UInt64Pool { get; set; }

        public RasterBandPool()
        {
            this.BytePool = new();
            this.DoublePool = new();
            this.FloatPool = new();
            this.Int8Pool = new();
            this.Int16Pool = new();
            this.Int32Pool = new();
            this.Int64Pool = new();
            this.UInt16Pool = new();
            this.UInt32Pool = new();
            this.UInt64Pool = new();
        }

        public void Return(DataType gdalType, Array dataBuffer)
        {
            switch (gdalType)
            {
                case DataType.GDT_Byte:
                    byte[]? byteBuffer = dataBuffer as byte[];
                    Debug.Assert(byteBuffer != null);
                    this.BytePool.Return(byteBuffer);
                    break;
                case DataType.GDT_Float32:
                    float[]? floatBuffer = dataBuffer as float[];
                    Debug.Assert(floatBuffer != null);
                    this.FloatPool.Return(floatBuffer);
                    break;
                case DataType.GDT_Float64:
                    double[]? doubleBuffer = dataBuffer as double[];
                    Debug.Assert(doubleBuffer != null);
                    this.DoublePool.Return(doubleBuffer);
                    break;
                case DataType.GDT_Int8:
                    sbyte[]? int8buffer = dataBuffer as sbyte[];
                    Debug.Assert(int8buffer != null);
                    this.Int8Pool.Return(int8buffer);
                    break;
                case DataType.GDT_Int16:
                    Int16[]? int16buffer = dataBuffer as Int16[];
                    Debug.Assert(int16buffer != null);
                    this.Int16Pool.Return(int16buffer);
                    break;
                case DataType.GDT_Int32:
                    Int32[]? int32buffer = dataBuffer as Int32[];
                    Debug.Assert(int32buffer != null);
                    this.Int32Pool.Return(int32buffer);
                    break;
                case DataType.GDT_Int64:
                    Int64[]? int64buffer = dataBuffer as Int64[];
                    Debug.Assert(int64buffer != null);
                    this.Int64Pool.Return(int64buffer);
                    break;
                case DataType.GDT_UInt16:
                    UInt16[]? uint16buffer = dataBuffer as UInt16[];
                    Debug.Assert(uint16buffer != null);
                    this.UInt16Pool.Return(uint16buffer);
                    break;
                case DataType.GDT_UInt32:
                    UInt32[]? uint32buffer = dataBuffer as UInt32[];
                    Debug.Assert(uint32buffer != null);
                    this.UInt32Pool.Return(uint32buffer);
                    break;
                case DataType.GDT_UInt64:
                    UInt64[]? uint64buffer = dataBuffer as UInt64[];
                    Debug.Assert(uint64buffer != null);
                    this.UInt64Pool.Return(uint64buffer);
                    break;
                default:
                    throw new NotSupportedException("Unhandled data type " + gdalType + ".");
            }
        }
    }
}
