using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Laz;

namespace Mars.Clouds.Las
{
    public class LasReader : LasStream<FileStream>
    {
        // for unbuffered IO
        private const int SectorSizeInBytes = 4096;
        private const int FloatingPointReadBufferSizeInBytes = (1024 + 1) * LasReader.SectorSizeInBytes; // 1M 4096 byte sectors = 4 MB + 1 sector for alignment

        // use 8k as default size of .las metadata reads
        // Header bytes plus a GeoTIFF CRS VLR is only ~512 bytes but recent-ish .las/.laz files often use 5+ kB wkt compound CRS VLRs. Wkt
        // VLRs thus round up to two 4096 byte sectors for read.
        public const int HeaderAndVlrReadBufferSizeInBytes = 8 * 1024; // for unit tests

        public bool DiscardOverrunningVlrs { get; set; }
        public bool Unbuffered { get; private init; }

        public LasReader(FileStream stream)
            : base(stream)
        {
            this.DiscardOverrunningVlrs = false;
            this.Unbuffered = false;
        }

        /// <summary>
        /// Create <see cref="LasReader"/> with small IO buffer size for efficient header and variable length record reads.
        /// </summary>
        public static LasReader CreateForHeaderAndVlrRead(string lasPath, bool discardOverrunningVlrs, bool enableAsync = false)
        {
            FileOptions fileOptions = enableAsync ? FileOptions.Asynchronous : FileOptions.None;
            FileStream stream = new(lasPath, FileMode.Open, FileAccess.Read, FileShare.Read, LasReader.HeaderAndVlrReadBufferSizeInBytes, fileOptions);
            LasReader reader = new(stream)
            {
                DiscardOverrunningVlrs = discardOverrunningVlrs
            };

            return reader;
        }

        public static LasReader CreateForPointRead(string lasPath)
        {
            FileInfo cloudFileInfo = new(lasPath);
            return LasReader.CreateForPointRead(lasPath, cloudFileInfo.Length);
        }

        public static LasReader CreateForPointRead(string lasPath, bool discardOverrunningVlrs)
        {
            LasReader reader = LasReader.CreateForPointRead(lasPath);
            reader.DiscardOverrunningVlrs = discardOverrunningVlrs;
            return reader;
        }

        public static LasReader CreateForPointRead(string lasPath, long fileSizeInBytes, bool unbuffered = false, bool enableAsync = false)
        {
            return new LasReader(LasReader.CreatePointStream(lasPath, fileSizeInBytes, FileAccess.Read, unbuffered, enableAsync))
            {
                Unbuffered = unbuffered
            };
        }

        protected static FileStream CreatePointStream(string lasPath, long fileSizeInBytes, FileAccess fileAccess, bool unbuffered, bool enableAsync)
        {
            if (enableAsync && unbuffered)
            {
                throw new NotSupportedException("Support for unbuffered, asynchronous IO has not been implemented.");
            }

            // rough scaling with file size derived from https://github.com/dotnet/runtime/discussions/74405#discussioncomment-3488674
            // Buffer sizes are increased from results in #74405 as doing so yields modest performance gains with NVMes and shows no
            // penalty with hard drives. Consistent with #74405, profiling shows FileOptions.SequentialScan has either no effect or
            // is slightly detrimental 
            FileOptions fileOptions = enableAsync ? FileOptions.Asynchronous : FileOptions.None;
            int bufferSizeInBytes;
            if (unbuffered)
            {
                // FILE_FLAG_NO_BUFFERING = 0x20000000
                fileOptions |= (FileOptions)0x20000000;
                // with FILE_FLAG_NO_BUFFERING caller reads into buffer from System.Runtime.InteropServices.NativeMemory.AlignedAlloc(, 4096)
                bufferSizeInBytes = 0;
            }
            else
            {
                if (fileSizeInBytes > 512 * 1024 * 1024) // > 512 MB
                {
                    bufferSizeInBytes = 4 * 1024 * 1024; // little to no throughput increase from 2 MB, larger sizes decrease NVMe throughput
                }
                else if (fileSizeInBytes > 64 * 1024 * 1024) // > 64 MB
                {
                    bufferSizeInBytes = 2 * 1024 * 1024;
                }
                else if (fileSizeInBytes > 8 * 1024 * 1024) // > 8 MB
                {
                    bufferSizeInBytes = 1024 * 1024;
                }
                else // ≤ 8 MB
                {
                    bufferSizeInBytes = 512 * 1024;
                }
            }

            FileStream stream = new(lasPath, FileMode.Open, fileAccess, FileShare.Read, bufferSizeInBytes, fileOptions);
            return stream;
        }

        private static int EstimateCellInitialPointCapacity(LasTile tile, Grid metricsGrid)
        {
            double cellArea = metricsGrid.Transform.GetCellArea();
            double tileArea = tile.GridExtent.GetArea();
            UInt64 tilePoints = tile.Header.GetNumberOfPoints();

            // edge case: guard against overallocation when tile is smaller than cell
            if (tileArea <= cellArea)
            {
                return (int)tilePoints; // for now, assume tile is completely enclosed within cell
            }

            // find nearest power of two that's less than mean number of points per cell 
            // Minimum is List<T>'s default capacity of four.
            int meanPointsPerCell = (int)(tilePoints * cellArea / tileArea);
            int cellInitialPointCapacity = 4;
            for (int nextPowerOfTwo = 2 * cellInitialPointCapacity; nextPowerOfTwo < meanPointsPerCell; nextPowerOfTwo *= 2)
            {
                cellInitialPointCapacity = nextPowerOfTwo;
            }

            return cellInitialPointCapacity;
        }

        public static (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) GetHeaderBandwidth()
        {
            // TODO: profile
            return (HardwareCapabilities.HardDriveDefaultTransferRateInGBs, 5.0F * HardwareCapabilities.HardDriveDefaultTransferRateInGBs);
        }

        public static (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) GetPointsToGridMetricsBandwidth()
        {
            // TODO: update profiling data
            return (0.67F, 4.5F * 0.67F);
        }

        public static (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) GetPointsToImageBandwidth(bool unbuffered)
        {
            // SN770 or SN850X on 5950X CPU lanes
            return unbuffered ? (1.0F, 4.3F) : (1.2F, 7.1F);
        }

        public static (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) GetPointsToDsmBandwidth(DigitalSurfaceModelBands dsmBands, bool unbuffered)
        {
            // SN850X on 5950X CPU lanes, aerial and ground means accumulated
            // If needed, additional band combinations can be profiled.
            if ((dsmBands & DigitalSurfaceModelBands.Subsurface) == DigitalSurfaceModelBands.Subsurface)
            {
                // DigitalSufaceModelBands.Default | DigitalSufaceModelBands.Subsurface | DigitalSufaceModelBands.ReturnNumberSurface
                // For now, assume slow enough buffered-unbuffered doesn't matter.
                return (1.0F, 4.2F);
            }

            // DigitalSufaceModelBands.Default
            return unbuffered ? (1.77F, 4.80F) : (2.27F, 6.40F);
        }

        /// <returns>false if point is classified as noise or is withdrawn</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ReadClassification(ReadOnlySpan<byte> pointBytes, int pointFormat, out PointClassification classification)
        {
            if (pointFormat < 6)
            {
                // bits 0-4: classification, 5: synthetic, 6: key point, 7: withheld
                byte classificationAndFlags = pointBytes[15];
                classification = (PointClassification)(classificationAndFlags & 0x1f);
                if ((classificationAndFlags & 0x80) != 0)
                {
                    return false; // withheld (LAS 1.4 R15 Table 8)
                }
            }
            else
            {
                classification = (PointClassification)pointBytes[16];
                byte classificationFlags = pointBytes[15]; // 0: synthetic, 1: key point, 2: withheld, 3: overlap, 4-5: scanner channel, 6: scan direction, 7: edge of flight line
                if ((classificationFlags & (byte)PointClassificationFlags.Withheld) != 0)
                {
                    return false;
                }
            }
            if ((classification == PointClassification.HighNoise) || (classification == PointClassification.LowNoise))
            {
                return false; // exclude noise points from consideration
            }

            return true;
        }

        /// <returns>false if point is classified as noise or is withdrawn</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ReadFlags(ReadOnlySpan<byte> pointBytes, int pointFormat, out PointClassificationFlags classificationFlags, out ScanFlags scanFlags)
        {
            PointClassification classification;
            if (pointFormat < 6)
            {
                // bits 0-4: classification, 5: synthetic, 6: key point, 7: withheld
                byte classificationAndFlags = pointBytes[15];
                classification = (PointClassification)(classificationAndFlags & 0x1f);
                classificationFlags = (PointClassificationFlags)((classificationAndFlags & 0xe0) >> 5);
                scanFlags = (ScanFlags)((pointBytes[14] & 0xc0) >> 6);
            }
            else
            {
                classification = (PointClassification)pointBytes[16];
                byte flags = pointBytes[15];
                classificationFlags = (PointClassificationFlags)(flags & 0x0f); // 0: synthetic, 1: key point, 2: withheld, 3: overlap, 4-5: scanner channel, 6: scan direction, 7: edge of flight line
                scanFlags = (ScanFlags)((flags & 0xc0) >> 6);
            }

            if ((classificationFlags & PointClassificationFlags.Withheld) != 0)
            {
                return false;
            }
            if ((classification == PointClassification.HighNoise) || (classification == PointClassification.LowNoise))
            {
                return false;
            }

            return true;
        }

        public LasHeader10 ReadHeader()
        {
            if (this.BaseStream.Position != 0)
            {
                this.BaseStream.Seek(0, SeekOrigin.Begin);
            }

            Span<byte> headerBytes = stackalloc byte[LasHeader10.HeaderSizeInBytes];
            this.BaseStream.ReadExactly(headerBytes);

            UInt32 signatureAsUInt32 = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes);
            if (signatureAsUInt32 != 0x4653414c)
            {
                Span<byte> signatureBytes = stackalloc byte[4];
                BinaryPrimitives.TryWriteUInt32LittleEndian(signatureBytes, signatureAsUInt32);
                string signature = Encoding.UTF8.GetString(signatureBytes);
                throw new IOException("File begins with '" + signature + "'. .las files are expected to begin with \"LASF\".");;
            }

            UInt16 fileSourceID = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[4..]);
            GlobalEncoding globalEncoding = (GlobalEncoding)BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[6..]);

            Guid projectID = new(headerBytes.Slice(8, 16));

            byte versionMajor = headerBytes[24];
            byte versionMinor = headerBytes[25];
            if ((versionMajor != 1) || (versionMinor > 4))
            {
                throw new NotSupportedException("Unknown .las file version " + versionMajor + "." + versionMinor + ".");
            }

            LasHeader10 lasHeader = versionMinor switch
            {
                0 => new LasHeader10()
                    {
                        ProjectID = projectID
                    },
                1 => new LasHeader11()
                    {
                        FileSourceID = fileSourceID,
                        ProjectID = projectID,
                    },
                2 => new LasHeader12()
                    {
                        FileSourceID = fileSourceID,
                        GlobalEncoding = globalEncoding,
                        ProjectID = projectID
                    },
                3 => new LasHeader13()
                    {
                        FileSourceID = fileSourceID,
                        GlobalEncoding = globalEncoding,
                        ProjectID = projectID
                    },
                4 => new LasHeader14()
                    {
                        FileSourceID = fileSourceID,
                        GlobalEncoding = globalEncoding,
                        ProjectID = projectID
                    },
                _ => throw new NotSupportedException("Unhandled .las file version " + versionMajor + "." + versionMinor + " in header creation.")
            };

            if (this.BaseStream.Length < lasHeader.HeaderSize)
            {
                throw new IOException("File size of " + this.BaseStream.Length + " bytes is less than the LAS " + versionMajor + "." + versionMinor + " header size of " + lasHeader.HeaderSize + " bytes.");
            }

            lasHeader.SystemIdentifier = Encoding.UTF8.GetString(headerBytes.Slice(26, 32)).Trim('\0');
            lasHeader.GeneratingSoftware = Encoding.UTF8.GetString(headerBytes.Slice(58, 32)).Trim('\0');

            lasHeader.FileCreationDayOfYear = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[90..]);
            lasHeader.FileCreationYear = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[92..]);

            UInt16 headerSizeInBytes = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[94..]);
            if (headerSizeInBytes != lasHeader.HeaderSize)
            {
                throw new IOException("Header of " + this.BaseStream.Length + " bytes differs from the LAS " + versionMajor + "." + versionMinor + " header size of " + lasHeader.HeaderSize + " bytes.");
            }

            lasHeader.OffsetToPointData = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[96..]);
            lasHeader.NumberOfVariableLengthRecords = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[100..]);
            lasHeader.PointDataRecordFormat = headerBytes[104];
            lasHeader.PointDataRecordLength = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[105..]);
            lasHeader.LegacyNumberOfPointRecords = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[107..]);
            for (int returnIndex = 0; returnIndex < lasHeader.LegacyNumberOfPointsByReturn.Length; ++returnIndex)
            {
                lasHeader.LegacyNumberOfPointsByReturn[returnIndex] = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[(111 + 4 * returnIndex)..]);
            }
            lasHeader.XScaleFactor = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[131..]);
            lasHeader.YScaleFactor = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[139..]);
            lasHeader.ZScaleFactor = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[147..]);
            lasHeader.XOffset = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[155..]);
            lasHeader.YOffset = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[163..]);
            lasHeader.ZOffset = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[171..]);
            lasHeader.MaxX = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[179..]);
            lasHeader.MinX = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[187..]);
            lasHeader.MaxY = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[195..]);
            lasHeader.MinY = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[203..]);
            lasHeader.MaxZ = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[211..]);
            lasHeader.MinZ = BinaryPrimitives.ReadDoubleLittleEndian(headerBytes[219..]);

            if (versionMinor > 2)
            {
                this.BaseStream.ReadExactly(headerBytes[..(lasHeader.HeaderSize - headerBytes.Length)]);
                ((LasHeader13)lasHeader).StartOfWaveformDataPacketRecord = BinaryPrimitives.ReadUInt64LittleEndian(headerBytes);

                if (versionMinor > 3) 
                {
                    LasHeader14 lasHeader14 = (LasHeader14)lasHeader;
                    lasHeader14.StartOfFirstExtendedVariableLengthRecord = BinaryPrimitives.ReadUInt64LittleEndian(headerBytes[8..]);
                    lasHeader14.NumberOfExtendedVariableLengthRecords = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[16..]);
                    lasHeader14.NumberOfPointRecords = BinaryPrimitives.ReadUInt64LittleEndian(headerBytes[20..]);
                    for (int returnIndex = 0; returnIndex < lasHeader14.NumberOfPointsByReturn.Length; ++returnIndex)
                    {
                        lasHeader14.NumberOfPointsByReturn[returnIndex] = BinaryPrimitives.ReadUInt64LittleEndian(headerBytes[(28 + 8 * returnIndex)..]);
                    }
                }
            }

            if (this.BaseStream.Position != lasHeader.HeaderSize)
            {
                throw new InvalidDataException(".las file header read failed. Ending position of read is " + this.BaseStream.Position + " which is inconsistent with the indicated header size of " + lasHeader.HeaderSize + " bytes.");
            }
            return lasHeader;
        }

        public void ReadPointsToDsm(LasTile openedTile, DigitalSurfaceModel dsmTile, ref byte[]? pointReadBuffer, ref float[]? subsurfaceBuffer)
        {
            LasReader.ThrowOnUnsupportedPointFormat(openedTile.Header);
            if (dsmTile.AerialPoints == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmTile), "One or both of DSM's aerial point count band has not been created.");
            }

            bool dsmHasSubsurface = dsmTile.Subsurface != null;
            int subsurfaceBufferLength = DigitalSurfaceModel.SubsurfaceBufferDepth * dsmTile.Cells;
            if (dsmHasSubsurface && ((subsurfaceBuffer == null) || (subsurfaceBuffer.Length != subsurfaceBufferLength)))
            {
                subsurfaceBuffer = GC.AllocateUninitializedArray<float>(subsurfaceBufferLength);
            }

            if (this.Unbuffered)
            {
                this.ReadPointsToDsmUnbuffered(openedTile, dsmTile, ref pointReadBuffer, subsurfaceBuffer);
            }
            else
            {
                this.ReadPointsToDsmBuffered(openedTile, dsmTile, ref pointReadBuffer, subsurfaceBuffer);
            }
        }

        private void ReadPointsToDsmBuffered(LasTile openedTile, DigitalSurfaceModel dsmTile, ref byte[]? pointReadBuffer, float[]? subsurfaceBuffer)
        {
            Debug.Assert((dsmTile.AerialPoints != null) && (dsmTile.SourceIDSurface != null));

            LasHeader10 lasHeader = openedTile.Header;
            this.MoveToPoints(openedTile);

            // read points
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;
            int returnNumberMask = lasHeader.GetReturnNumberMask(); // not necessarily used

            LasToGridTransform lasToDsmTransformXY = new(lasHeader, dsmTile);
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;

            int pointBufferLength = LasReader.ReadBufferSizeInPoints * pointRecordLength;
            if ((pointReadBuffer == null) || (pointReadBuffer.Length != pointBufferLength))
            {
                pointReadBuffer = GC.AllocateUninitializedArray<byte>(pointBufferLength);
            }

            bool dsmHasAerialMean = dsmTile.AerialMean != null;
            bool dsmHasGroundMean = dsmTile.GroundMean != null;
            bool dsmHasReturnNumber = dsmTile.ReturnNumberSurface != null;
            bool dsmHasSubsurface = dsmTile.Subsurface != null;
            Debug.Assert((dsmHasGroundMean == false) || (dsmTile.GroundPoints != null));

            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadBufferSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadBufferSizeInPoints ? LasReader.ReadBufferSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * pointRecordLength;
                this.BaseStream.ReadExactly(pointReadBuffer.AsSpan(0, bytesToRead));

                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += pointRecordLength)
                {
                    ReadOnlySpan<byte> pointBytes = pointReadBuffer.AsSpan(batchOffset, pointRecordLength);
                    bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                    if (notNoiseOrWithheld == false)
                    {
                        continue;
                    }

                    if (lasToDsmTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                    {
                        // point lies outside of the DSM tile and is therefore not of interest
                        // If the DSM tile's extents are equal to or larger than the point cloud tile in all directions reaching this case is an error.
                        // For now DSM tiles with smaller extents than the point cloud tile aren't supported.
                        double x = lasHeader.XOffset + lasHeader.XScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]);
                        double y = lasHeader.YOffset + lasHeader.YScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]);
                        throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside DSM tile extents (" + dsmTile.GetExtentString() + ") and thus has off tile indices " + xIndex + ", " + yIndex + ".");
                    }

                    Int64 cellIndex = dsmTile.ToCellIndex(xIndex, yIndex);
                    float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..12]);
                    if ((classification == PointClassification.Ground) && dsmHasGroundMean)
                    {
                        // TODO: support PointClassification.{ Rail, RoadSurface, IgnoredGround }
                        UInt32 groundPoints = dsmTile.GroundPoints![cellIndex];
                        if (groundPoints == 0)
                        {
                            dsmTile.GroundMean![cellIndex] = z;
                        }
                        else
                        {
                            dsmTile.GroundMean![cellIndex] += z;
                        }

                        dsmTile.GroundPoints[cellIndex] = groundPoints + 1;
                    }
                    else
                    {
                        // for now, assume all non-ground point types are aerial (noise and withheld points are excluded above)
                        // PointClassification.{ NeverClassified , Unclassified, LowVegetation, MediumVegetation, HighVegetation, Building,
                        //                       ModelKeyPoint, OverlapPoint, WireGuard, WireConductor, TransmissionTower, WireStructureConnector,
                        //                       BridgeDeck, OverheadStructure, Snow, TemporalExclusion }
                        // PointClassification.Water is currently also treated as non-ground, which is debatable.
                        float dsmZ = dsmTile.Surface[cellIndex];
                        UInt32 aerialPoints = dsmTile.AerialPoints[cellIndex];
                        if ((aerialPoints == 0) || (dsmZ < z))
                        {
                            // current point is either 1) the first aerial point read for this cell or 2) higher than the existing DSM
                            dsmTile.Surface[cellIndex] = z;
                            dsmTile.SourceIDSurface[cellIndex] = BinaryPrimitives.ReadUInt16LittleEndian(pointFormat < 6 ? pointBytes[18..20] : pointBytes[20..22]);
                            if (dsmHasReturnNumber)
                            {
                                dsmTile.ReturnNumberSurface![cellIndex] = (byte)(pointBytes[14] & returnNumberMask);
                            }
                        }

                        dsmTile.AerialPoints[cellIndex] = aerialPoints + 1;
                        if (dsmHasSubsurface && (aerialPoints > 0)) // first aerial point always goes to DSM
                        {
                            dsmTile.MaybeInsertSubsurfacePoint((int)cellIndex, dsmZ, (int)aerialPoints, z, subsurfaceBuffer!);
                        }
                        if (dsmHasAerialMean)
                        {
                            if (aerialPoints == 0)
                            {
                                dsmTile.AerialMean![cellIndex] = z;
                            }
                            else
                            {
                                dsmTile.AerialMean![cellIndex] += z;
                            }
                        }
                    }
                }
            }
        }

        private unsafe void ReadPointsToDsmUnbuffered(LasTile lasTile, DigitalSurfaceModel dsmTile, ref byte[]? pointReadBuffer, float[]? subsurfaceBuffer)
        {
            Debug.Assert((dsmTile.AerialPoints != null) && (dsmTile.SourceIDSurface != null));

            LasHeader10 lasHeader = lasTile.Header;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;
            int returnNumberMask = lasHeader.GetReturnNumberMask(); // not necessarily used

            bool hasRgb = lasHeader.PointsHaveRgb;
            bool hasNearInfrared = lasHeader.PointsHaveNearInfrared;

            LasToGridTransform lasToDsmTransformXY = new(lasHeader, dsmTile);
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;

            bool dsmHasAerialMean = dsmTile.AerialMean != null;
            bool dsmHasGroundMean = dsmTile.GroundMean != null;
            bool dsmHasReturnNumber = dsmTile.ReturnNumberSurface != null;
            bool dsmHasSubsurface = dsmTile.Subsurface != null;
            Debug.Assert((dsmHasGroundMean == false) || (dsmTile.GroundPoints != null));

            if ((pointReadBuffer == null) || (pointReadBuffer.Length != LasReader.FloatingPointReadBufferSizeInBytes))
            {
                pointReadBuffer = new byte[LasReader.FloatingPointReadBufferSizeInBytes];
            }
            byte[] pointSpanBuffer = new byte[pointRecordLength]; // CS9080 if stackallocked because of https://github.com/dotnet/roslyn/issues/43591
            fixed (byte* pointReadBufferUnalignedStart = pointReadBuffer)
            {
                byte* pointReadBufferAlignedStart = (byte*)(LasReader.SectorSizeInBytes * ((nuint)pointReadBufferUnalignedStart / LasReader.SectorSizeInBytes));
                if (pointReadBufferAlignedStart < pointReadBufferUnalignedStart)
                {
                    pointReadBufferAlignedStart += LasReader.SectorSizeInBytes;
                }
                Span<byte> alignedReadBuffer = new(pointReadBufferAlignedStart, LasReader.FloatingPointReadBufferSizeInBytes - LasReader.SectorSizeInBytes);

                long endOfPointsPosition = Int64.Min(this.BaseStream.Length, LasReader.SectorSizeInBytes * ((lasHeader.OffsetToPointData + (long)numberOfPoints * (long)pointRecordLength) / LasReader.SectorSizeInBytes + 1));
                long readPosition = LasReader.SectorSizeInBytes * (lasHeader.OffsetToPointData / LasReader.SectorSizeInBytes);
                this.BaseStream.Position = readPosition;

                int firstPointInBufferIndex = (int)(lasHeader.OffsetToPointData - readPosition);
                while (readPosition < endOfPointsPosition)
                {
                    long bytesRemainingToRead = endOfPointsPosition - readPosition;
                    int bytesRead;
                    if (bytesRemainingToRead > alignedReadBuffer.Length)
                    {
                        // first or intermediate read
                        this.BaseStream.ReadExactly(alignedReadBuffer);
                        bytesRead = alignedReadBuffer.Length;
                        //bytesRead = RandomAccess.Read(this.BaseStream.SafeFileHandle, alignedReadBuffer, readPosition);
                        //if (bytesRead != alignedReadBuffer.Length)
                        //{
                        //    // TODO: handle reads past the end of the last point
                        //    throw new NotSupportedException("Expected read at position " + readPosition + " to return " + alignedReadBuffer.Length + " bytes but " + bytesRead + " bytes were read.");
                        //}
                    }
                    else
                    {
                        // last read: to end of sector containing the last point or the end of the file, whichever comes first
                        bytesRead = this.BaseStream.Read(alignedReadBuffer);
                        //bytesRead = RandomAccess.Read(this.BaseStream.SafeFileHandle, alignedReadBuffer, readPosition);
                        if (bytesRead != bytesRemainingToRead)
                        {
                            // TODO: handle reads past the end of the last point
                            throw new NotSupportedException("Last read in file did not end exactly at the end of point records. This will be caused by extended variable length records or can be caused by the read not returning as many bytes as expected.");
                        }
                    }

                    // reads are most likely to align with points
                    // Second and subsequent reads of point data therefore likely read the bytes needed to complete a point whose initial bytes
                    // were obtained in the previous read. It's easier (and likely faster) to merge bytes from the previous and current reads into
                    // one span than to have point parsing look into two different ReadOnlySpan<byte>s.
                    if (firstPointInBufferIndex < 0)
                    {
                        int bytesToCompleteFirstPoint = pointRecordLength + firstPointInBufferIndex;
                        alignedReadBuffer[..bytesToCompleteFirstPoint].CopyTo(pointSpanBuffer.AsSpan(-firstPointInBufferIndex));
                    }

                    // number of points read into the main buffer plus the point in the span buffer, if present
                    int pointsInBuffers = (bytesRead - firstPointInBufferIndex) / pointRecordLength;
                    // take the total number of point bytes available and offset it into place on the current main read buffer
                    // Indexing is from the the start of span buffer as it partially overlaps the beginning of the main buffer.
                    int lastPointStartIndexInBuffer = pointRecordLength * (pointsInBuffers - 1) + firstPointInBufferIndex;
                    for (int bufferIndex = firstPointInBufferIndex; bufferIndex < lastPointStartIndexInBuffer; bufferIndex += pointRecordLength)
                    {
                        ReadOnlySpan<byte> pointBytes;
                        if (bufferIndex >= 0)
                        {
                            pointBytes = alignedReadBuffer.Slice(bufferIndex, pointRecordLength);
                        }
                        else
                        {
                            pointBytes = pointSpanBuffer;
                        }

                        bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                        if (notNoiseOrWithheld == false)
                        {
                            // point isn't valid data => skip
                            continue;
                        }

                        if (lasToDsmTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                        {
                            // point lies outside of the DSM tile and is therefore not of interest
                            // If the DSM tile's extents are equal to or larger than the point cloud tile in all directions reaching this case is an error.
                            // For now DSM tiles with smaller extents than the point cloud tile aren't supported.
                            double x = lasHeader.XOffset + lasHeader.XScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]);
                            double y = lasHeader.YOffset + lasHeader.YScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]);
                            throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside DSM tile extents (" + dsmTile.GetExtentString() + ") and thus has off tile indices " + xIndex + ", " + yIndex + ".");
                        }

                        Int64 cellIndex = dsmTile.ToCellIndex(xIndex, yIndex);
                        float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..12]);
                        if ((classification == PointClassification.Ground) && dsmHasGroundMean)
                        {
                            // TODO: support PointClassification.{ Rail, RoadSurface, IgnoredGround }
                            UInt32 groundPoints = dsmTile.GroundPoints![cellIndex];
                            if (groundPoints == 0)
                            {
                                dsmTile.GroundMean![cellIndex] = z;
                            }
                            else
                            {
                                dsmTile.GroundMean![cellIndex] += z;
                            }

                            dsmTile.GroundPoints[cellIndex] = groundPoints + 1;
                        }
                        else
                        {
                            // for now, assume all non-ground point types are aerial (noise and withheld points are excluded above)
                            // PointClassification.{ NeverClassified , Unclassified, LowVegetation, MediumVegetation, HighVegetation, Building,
                            //                       ModelKeyPoint, OverlapPoint, WireGuard, WireConductor, TransmissionTower, WireStructureConnector,
                            //                       BridgeDeck, OverheadStructure, Snow, TemporalExclusion }
                            // PointClassification.Water is currently also treated as non-ground, which is debatable
                            float dsmZ = dsmTile.Surface[cellIndex];
                            UInt32 aerialPoints = dsmTile.AerialPoints[cellIndex];
                            if ((dsmZ < z) || (aerialPoints == 0))
                            {
                                dsmTile.Surface[cellIndex] = z;
                                dsmTile.SourceIDSurface[cellIndex] = BinaryPrimitives.ReadUInt16LittleEndian(pointFormat < 6 ? pointBytes[18..20] : pointBytes[20..22]);
                                if (dsmHasReturnNumber)
                                {
                                    dsmTile.ReturnNumberSurface![cellIndex] = (byte)(pointBytes[14] & returnNumberMask);
                                }
                            }

                            dsmTile.AerialPoints[cellIndex] = aerialPoints + 1;
                            if (dsmHasSubsurface && (aerialPoints > 0)) // first aerial point always goes to DSM
                            {
                                dsmTile.MaybeInsertSubsurfacePoint((int)cellIndex, dsmZ, (int)aerialPoints, z, subsurfaceBuffer!);
                            }
                            if (dsmHasAerialMean)
                            {
                                if (aerialPoints == 0)
                                {
                                    dsmTile.AerialMean![cellIndex] = z;
                                }
                                else
                                {
                                    dsmTile.AerialMean![cellIndex] += z;
                                }
                            }
                        }
                    }

                    int bytesOfNextPointRead = bytesRead - lastPointStartIndexInBuffer - pointRecordLength;
                    if (bytesOfNextPointRead > 0)
                    {
                        int nextPointStartIndex = alignedReadBuffer.Length - bytesOfNextPointRead;
                        alignedReadBuffer[nextPointStartIndex..].CopyTo(pointSpanBuffer);
                    }

                    firstPointInBufferIndex = -bytesOfNextPointRead;
                    readPosition += bytesRead;
                }
            }
        }

        #region ReadPointsToImage() asynchronous FileStream at queue depth 2
        //public void ReadPointsToImage(LasTile lasTile, ImageRaster<UInt64> imageTile, ArrayPool<byte> readBufferPool)
        //{
        //    if (this.BaseStream.IsAsync == false)
        //    {
        //        throw new InvalidOperationException("This implementation of " + nameof(this.ReadPointsToImage) + " uses asynchronous IO but this " + nameof(LasReader) + " was created with a stream enabled only for synchronous IO. Specify asynchronous rather than synchronous when creating the reader.");
        //    }

        //    LasHeader10 lasHeader = lasTile.Header;
        //    LasReader.ThrowOnUnsupportedPointFormat(lasHeader);

        //    bool hasRgb = lasHeader.PointsHaveRgb;
        //    bool hasNearInfrared = lasHeader.PointsHaveNearInfrared;
        //    if (hasNearInfrared)
        //    {
        //        if (imageTile.NearInfrared == null)
        //        {
        //            // for now, fail if points have NIR data but image won't capture it
        //            // This can be relaxed if needed.
        //            throw new ArgumentOutOfRangeException(nameof(imageTile), "Point cloud tile '" + lasTile.FilePath + "' contains near infrared data but image lacks a near infrared band to transfer the data to.");
        //        }
        //    }
        //    else
        //    {
        //        if (imageTile.NearInfrared != null)
        //        {
        //            // for now, fail if image has an NIR band but points lack data to populate it
        //            // This can be relaxed if needed.
        //            throw new ArgumentOutOfRangeException(nameof(imageTile), "Point cloud tile '" + lasTile.FilePath + "' lacks near infrared data but image lacks a near infrared band to transfer the data to.");
        //        }
        //    }

        //    this.MoveToPoints(lasTile);

        //    // read points
        //    // Profiling with RGB+NIR tiles, SN770 on 5950X CPU lanes, Get-Orthoimages with read semaphore but without conversion to averages or 
        //    // writing tiles to disk, 1+ MB FileStream buffers.
        //    //                                                              total throughput, GB/s
        //    // FileStream buffering      read size           queue depth    1 thread     2 threads    4 threads
        //    // 4 MB + Windows            1 MB                2              1.1          1.1          1.0
        //    UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
        //    byte pointFormat = lasHeader.PointDataRecordFormat;
        //    int pointRecordLength = lasHeader.PointDataRecordLength;
        //    int returnNumberMask = lasHeader.GetReturnNumberMask();
        //    double xOffset = lasHeader.XOffset;
        //    double xScale = lasHeader.XScaleFactor;
        //    double yOffset = lasHeader.YOffset;
        //    double yScale = lasHeader.YScaleFactor;

        //    int readBufferSizeInPoints = readBufferPool.ArrayLength / pointRecordLength;
        //    byte[] pointReadBufferCurrent;
        //    byte[] pointReadBufferNext;
        //    byte[] pointReadBufferNextNext;
        //    lock (readBufferPool)
        //    {
        //        pointReadBufferCurrent = readBufferPool.TryGetOrAllocateUninitialized();
        //        pointReadBufferNext = readBufferPool.TryGetOrAllocateUninitialized();
        //        pointReadBufferNextNext = readBufferPool.TryGetOrAllocateUninitialized();
        //    }
        //    UInt64 readBufferSizeInPointsUInt64 = (UInt64)readBufferSizeInPoints;

        //    UInt64 pointReadsRemainingToInitiate = numberOfPoints;
        //    int pointsToInitiate = pointReadsRemainingToInitiate >= readBufferSizeInPointsUInt64 ? readBufferSizeInPoints : (int)pointReadsRemainingToInitiate;
        //    int bytesToInitiate = pointsToInitiate * pointRecordLength;
        //    Task readCurrentTask = this.BaseStream.ReadExactlyAsync(pointReadBufferCurrent, 0, bytesToInitiate).AsTask();
        //    pointReadsRemainingToInitiate -= (UInt64)pointsToInitiate;

        //    pointsToInitiate = pointReadsRemainingToInitiate >= readBufferSizeInPointsUInt64 ? readBufferSizeInPoints : (int)pointReadsRemainingToInitiate;
        //    bytesToInitiate = pointsToInitiate * pointRecordLength;
        //    Task readNextTask = this.BaseStream.ReadExactlyAsync(pointReadBufferNext, 0, bytesToInitiate).AsTask();
        //    pointReadsRemainingToInitiate -= (UInt64)pointsToInitiate;

        //    for (UInt64 pointReadsCompleted = 0; pointReadsCompleted < numberOfPoints; pointReadsCompleted += readBufferSizeInPointsUInt64)
        //    {
        //        Task? readNextNextTask = null;
        //        //Task<int>? readNextNextTask = null;
        //        if (pointReadsRemainingToInitiate > 0)
        //        {
        //            pointsToInitiate = pointReadsRemainingToInitiate >= readBufferSizeInPointsUInt64 ? readBufferSizeInPoints : (int)pointReadsRemainingToInitiate;
        //            bytesToInitiate = pointsToInitiate * pointRecordLength;
        //            readNextNextTask = this.BaseStream.ReadExactlyAsync(pointReadBufferNextNext, 0, bytesToInitiate).AsTask();
        //            pointReadsRemainingToInitiate -= (UInt64)pointsToInitiate;
        //        }

        //        readCurrentTask.GetAwaiter().GetResult();
        //        for (int batchOffset = 0; batchOffset < bytesToInitiate; batchOffset += pointRecordLength)
        //        {
        //            ReadOnlySpan<byte> pointBytes = pointReadBufferCurrent.AsSpan(batchOffset, pointRecordLength);

        //            byte returnNumber = (byte)(pointBytes[14] & returnNumberMask);
        //            if (returnNumber > 2)
        //            {
        //                // third and subsequent returns not currently used
        //                // Points marked with return number 0 (which is LAS 1.4 compliance issue) pass through this check.
        //                continue;
        //            }

        //            bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification _);
        //            if (notNoiseOrWithheld == false)
        //            {
        //                // point isn't valid data => skip
        //                continue;
        //            }

        //            double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
        //            double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
        //            (int xIndex, int yIndex) = imageTile.ToGridIndices(x, y);
        //            if ((xIndex < 0) || (yIndex < 0) || (xIndex >= imageTile.SizeX) || (yIndex >= imageTile.SizeY))
        //            {
        //                // point lies outside of the DSM tile and is therefore not of interest
        //                // If the DSM tile's extents are equal to or larger than the point cloud tile in all directions reaching this case is an error.
        //                // For now DSM tiles with smaller extents than the point cloud tile aren't supported.
        //                throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside image extents (" + imageTile.GetExtentString() + ") and thus has off image indices " + xIndex + ", " + yIndex + ".");
        //            }

        //            int cellIndex = imageTile.ToCellIndex(xIndex, yIndex);
        //            UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..]);
        //            if (returnNumber == 1)
        //            {
        //                // assume first returns are always the most representative => only first returns contribute to RGB+NIR
        //                ++imageTile.FirstReturns[cellIndex];

        //                if (hasRgb)
        //                {
        //                    UInt16 red;
        //                    UInt16 green;
        //                    UInt16 blue;
        //                    if (pointFormat < 6)
        //                    {
        //                        red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[28..]);
        //                        green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..]);
        //                        blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..]);
        //                    }
        //                    else
        //                    {
        //                        red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..]);
        //                        green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..]);
        //                        blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[34..]);
        //                    }
        //                    imageTile.Red[cellIndex] += red;
        //                    imageTile.Green[cellIndex] += green;
        //                    imageTile.Blue[cellIndex] += blue;
        //                }

        //                if (hasNearInfrared)
        //                {
        //                    UInt16 nir = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[36..]);
        //                    imageTile.NearInfrared![cellIndex] += nir;
        //                }

        //                imageTile.IntensityFirstReturn[cellIndex] += intensity;
        //            }
        //            else if (returnNumber == 2)
        //            {
        //                ++imageTile.SecondReturns[cellIndex];
        //                imageTile.IntensitySecondReturn[cellIndex] += intensity;
        //            }

        //            float scanAngleInDegrees;
        //            if (pointFormat < 6)
        //            {
        //                scanAngleInDegrees = (sbyte)pointBytes[16];
        //            }
        //            else
        //            {
        //                scanAngleInDegrees = 0.006F * BinaryPrimitives.ReadInt16LittleEndian(pointBytes[18..]);
        //            }
        //            if (scanAngleInDegrees < 0.0F)
        //            {
        //                scanAngleInDegrees = -scanAngleInDegrees;
        //            }
        //            imageTile.ScanAngleMeanAbsolute[cellIndex] += scanAngleInDegrees;
        //        }

        //        byte[] pointReadBufferSwapPointer = pointReadBufferCurrent;
        //        pointReadBufferCurrent = pointReadBufferNext;
        //        pointReadBufferNext = pointReadBufferNextNext;
        //        pointReadBufferNextNext = pointReadBufferSwapPointer;
        //        //Memory<byte> pointReadMemorySwapPointer = pointReadMemoryCurrent;
        //        //pointReadMemoryCurrent = pointReadMemoryNext;
        //        //pointReadMemoryNext = pointReadMemoryNextNext;
        //        //pointReadMemoryNextNext = pointReadMemorySwapPointer;
        //        readCurrentTask = readNextTask;
        //        if (readNextNextTask != null)
        //        {
        //            readNextTask = readNextNextTask;
        //        }
        //    }

        //    lock (readBufferPool)
        //    {
        //        readBufferPool.Return(pointReadBufferCurrent);
        //        readBufferPool.Return(pointReadBufferNext);
        //        readBufferPool.Return(pointReadBufferNextNext);
        //    }
        //}
        #endregion

        public void ReadPointsToImage(LasTile openedTile, ImageRaster<UInt64> imageTile, ref byte[]? pointReadBuffer)
        {
            if (this.BaseStream.IsAsync)
            {
                throw new InvalidOperationException("This implementation of " + nameof(this.ReadPointsToImage) + " uses synchronous IO but this " + nameof(LasReader) + " was created with a stream enabled for asynchronous IO. Specify synchronous rather than asynchronous when creating the reader.");
            }

            LasHeader10 lasHeader = openedTile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);

            if (lasHeader.PointsHaveNearInfrared)
            {
                if (imageTile.NearInfrared == null)
                {
                    // for now, fail if points have NIR data but image won't capture it
                    // This can be relaxed if needed.
                    throw new ArgumentOutOfRangeException(nameof(imageTile), "Point cloud tile '" + openedTile.FilePath + "' contains near infrared data but image lacks a near infrared band to transfer the data to.");
                }
            }
            else
            {
                if (imageTile.NearInfrared != null)
                {
                    // for now, fail if image has an NIR band but points lack data to populate it
                    // This can be relaxed if needed.
                    throw new ArgumentOutOfRangeException(nameof(imageTile), "Point cloud tile '" + openedTile.FilePath + "' lacks near infrared data but image lacks a near infrared band to transfer the data to.");
                }
            }

            this.MoveToPoints(openedTile);
            if (this.Unbuffered)
            {
                this.ReadPointsToImageUnbuffered(openedTile, imageTile, ref pointReadBuffer);
            }
            else
            {
                this.ReadPointsToImageBuffered(openedTile, imageTile, ref pointReadBuffer);
            }
        }

        private void ReadPointsToImageBuffered(LasTile openedTile, ImageRaster<UInt64> imageTile, ref byte[]? pointReadBuffer)
        {
            LasHeader10 lasHeader = openedTile.Header;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;
            bool hasRgb = lasHeader.PointsHaveRgb;
            bool hasNearInfrared = lasHeader.PointsHaveNearInfrared;
            int returnNumberMask = lasHeader.GetReturnNumberMask();

            LasToGridTransform lasToImageTransformXY = new(lasHeader, imageTile);

            int pointBufferLength = LasReader.ReadBufferSizeInPoints * pointRecordLength;
            if ((pointReadBuffer == null) || (pointReadBuffer.Length != pointBufferLength))
            {
                pointReadBuffer = new byte[pointBufferLength];
            }
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadBufferSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadBufferSizeInPoints ? LasReader.ReadBufferSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * pointRecordLength;
                this.BaseStream.ReadExactly(pointReadBuffer, 0, bytesToRead);

                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += pointRecordLength)
                {
                    ReadOnlySpan<byte> pointBytes = pointReadBuffer.AsSpan(batchOffset, pointRecordLength);

                    int returnNumber = pointBytes[14] & returnNumberMask;
                    if (returnNumber > 2)
                    {
                        // third and subsequent returns not currently used
                        // Points marked with return number 0 (which is LAS 1.4 compliance issue) pass through this check.
                        continue;
                    }

                    bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification _);
                    if (notNoiseOrWithheld == false)
                    {
                        // point isn't valid data => skip
                        continue;
                    }

                    if (lasToImageTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                    {
                        double x = lasHeader.XOffset + lasHeader.XScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]);
                        double y = lasHeader.YOffset + lasHeader.YScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]);
                        throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside image tile extents (" + imageTile.GetExtentString() + ") and thus has off tile indices " + xIndex + ", " + yIndex + ".");
                    }

                    Int64 cellIndex = imageTile.ToCellIndex(xIndex, yIndex);
                    UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..]);
                    if (returnNumber == 1)
                    {
                        // assume first returns are always the most representative => only first returns contribute to RGB+NIR
                        ++imageTile.FirstReturns[cellIndex];

                        if (hasRgb)
                        {
                            UInt16 red;
                            UInt16 green;
                            UInt16 blue;
                            if (pointFormat < 6)
                            {
                                red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[28..30]);
                                green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..32]);
                                blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..34]);
                            }
                            else
                            {
                                red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..32]);
                                green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..34]);
                                blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[34..36]);
                            }
                            imageTile.Red[cellIndex] += red;
                            imageTile.Green[cellIndex] += green;
                            imageTile.Blue[cellIndex] += blue;
                        }

                        if (hasNearInfrared)
                        {
                            UInt16 nir = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[36..38]);
                            imageTile.NearInfrared![cellIndex] += nir;
                        }

                        imageTile.IntensityFirstReturn[cellIndex] += intensity;
                    }
                    else if (returnNumber == 2)
                    {
                        imageTile.SecondReturns[cellIndex] += 1;
                        imageTile.IntensitySecondReturn[cellIndex] += intensity;
                    }

                    float scanAngleInDegrees;
                    if (pointFormat < 6)
                    {
                        scanAngleInDegrees = (sbyte)pointBytes[16];
                    }
                    else
                    {
                        scanAngleInDegrees = 0.006F * BinaryPrimitives.ReadInt16LittleEndian(pointBytes[18..20]);
                    }
                    if (scanAngleInDegrees < 0.0F)
                    {
                        scanAngleInDegrees = -scanAngleInDegrees;
                    }
                    imageTile.ScanAngleMeanAbsolute[cellIndex] += scanAngleInDegrees;
                }
            }
        }

        private unsafe void ReadPointsToImageUnbuffered(LasTile openedTile, ImageRaster<UInt64> imageTile, ref byte[]? pointReadBuffer)
        {
            LasHeader10 lasHeader = openedTile.Header;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;

            bool hasRgb = lasHeader.PointsHaveRgb;
            bool hasNearInfrared = lasHeader.PointsHaveNearInfrared;
            int returnNumberMask = lasHeader.GetReturnNumberMask();

            LasToGridTransform lasToImageTransformXY = new(lasHeader, imageTile);

            if ((pointReadBuffer == null) || (pointReadBuffer.Length != LasReader.FloatingPointReadBufferSizeInBytes))
            {
                pointReadBuffer = new byte[LasReader.FloatingPointReadBufferSizeInBytes];
            }
            byte[] pointSpanBuffer = new byte[pointRecordLength]; // CS9080 if stackallocked because of https://github.com/dotnet/roslyn/issues/43591
            fixed (byte* pointReadBufferUnalignedStart = pointReadBuffer)
            {
                byte* pointReadBufferAlignedStart = (byte *)(LasReader.SectorSizeInBytes * ((nuint)pointReadBufferUnalignedStart / LasReader.SectorSizeInBytes));
                if (pointReadBufferAlignedStart < pointReadBufferUnalignedStart)
                {
                    pointReadBufferAlignedStart += LasReader.SectorSizeInBytes;
                }
                Span<byte> alignedReadBuffer = new(pointReadBufferAlignedStart, LasReader.FloatingPointReadBufferSizeInBytes - LasReader.SectorSizeInBytes);

                long endOfPointsPosition = Int64.Min(this.BaseStream.Length, LasReader.SectorSizeInBytes * ((lasHeader.OffsetToPointData + (long)numberOfPoints * (long)pointRecordLength) / LasReader.SectorSizeInBytes + 1));
                long readPosition = LasReader.SectorSizeInBytes * (lasHeader.OffsetToPointData / LasReader.SectorSizeInBytes);
                this.BaseStream.Position = readPosition;

                int firstPointInBufferIndex = (int)(lasHeader.OffsetToPointData - readPosition);
                while (readPosition < endOfPointsPosition)
                {
                    long bytesRemainingToRead = endOfPointsPosition - readPosition;
                    int bytesRead;
                    if (bytesRemainingToRead > alignedReadBuffer.Length)
                    {
                        // first or intermediate read
                        this.BaseStream.ReadExactly(alignedReadBuffer);
                        bytesRead = alignedReadBuffer.Length;
                        //bytesRead = RandomAccess.Read(this.BaseStream.SafeFileHandle, alignedReadBuffer, readPosition);
                        //if (bytesRead != alignedReadBuffer.Length)
                        //{
                        //    // TODO: handle reads past the end of the last point
                        //    throw new NotSupportedException("Expected read at position " + readPosition + " to return " + alignedReadBuffer.Length + " bytes but " + bytesRead + " bytes were read.");
                        //}
                    }
                    else
                    {
                        // last read: to end of sector containing the last point or the end of the file, whichever comes first
                        bytesRead = this.BaseStream.Read(alignedReadBuffer);
                        //bytesRead = RandomAccess.Read(this.BaseStream.SafeFileHandle, alignedReadBuffer, readPosition);
                        if (bytesRead != bytesRemainingToRead)
                        {
                            // TODO: handle reads past the end of the last point
                            throw new NotSupportedException("Last read in file did not end exactly at the end of point records. This will be caused by extended variable length records or can be caused by the read not returning as many bytes as expected.");
                        }
                    }

                    // reads are most likely to align with points
                    // Second and subsequent reads of point data therefore likely read the bytes needed to complete a point whose initial bytes
                    // were obtained in the previous read. It's easier (and likely faster) to merge bytes from the previous and current reads into
                    // one span than to have point parsing look into two different ReadOnlySpan<byte>s.
                    if (firstPointInBufferIndex < 0)
                    {
                        int bytesToCompleteFirstPoint = pointRecordLength + firstPointInBufferIndex;
                        alignedReadBuffer[..bytesToCompleteFirstPoint].CopyTo(pointSpanBuffer.AsSpan(-firstPointInBufferIndex));
                    }

                    // number of points read into the main buffer plus the point in the span buffer, if present
                    int pointsInBuffers = (bytesRead - firstPointInBufferIndex) / pointRecordLength;
                    // take the total number of point bytes available and offset it into place on the current main read buffer
                    // Indexing is from the the start of span buffer as it partially overlaps the beginning of the main buffer.
                    int lastPointStartIndexInBuffer = pointRecordLength * (pointsInBuffers - 1) + firstPointInBufferIndex;
                    for (int bufferIndex = firstPointInBufferIndex; bufferIndex < lastPointStartIndexInBuffer; bufferIndex += pointRecordLength)
                    {
                        ReadOnlySpan<byte> pointBytes;
                        if (bufferIndex >= 0)
                        {
                            pointBytes = alignedReadBuffer.Slice(bufferIndex, pointRecordLength);
                        }
                        else
                        {
                            pointBytes = pointSpanBuffer;
                        }

                        int returnNumber = pointBytes[14] & returnNumberMask;
                        if (returnNumber > 2)
                        {
                            // third and subsequent returns not currently used
                            // Points marked with return number 0 (which is LAS 1.4 compliance issue) pass through this check.
                            continue;
                        }

                        bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification _);
                        if (notNoiseOrWithheld == false)
                        {
                            // point isn't valid data => skip
                            continue;
                        }

                        if (lasToImageTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                        {
                            double x = lasHeader.XOffset + lasHeader.XScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]);
                            double y = lasHeader.YOffset + lasHeader.YScaleFactor * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]);
                            throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside image tile extents (" + imageTile.GetExtentString() + ") and thus has off tile indices " + xIndex + ", " + yIndex + ".");
                        }

                        Int64 cellIndex = imageTile.ToCellIndex(xIndex, yIndex);
                        UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..14]);
                        if (returnNumber == 1)
                        {
                            // assume first returns are always the most representative => only first returns contribute to RGB+NIR
                            ++imageTile.FirstReturns[cellIndex];

                            if (hasRgb)
                            {
                                UInt16 red;
                                UInt16 green;
                                UInt16 blue;
                                if (pointFormat < 6)
                                {
                                    red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[28..30]);
                                    green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..32]);
                                    blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..34]);
                                }
                                else
                                {
                                    red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[30..32]);
                                    green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[32..34]);
                                    blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[34..36]);
                                }
                                imageTile.Red[cellIndex] += red;
                                imageTile.Green[cellIndex] += green;
                                imageTile.Blue[cellIndex] += blue;
                            }

                            if (hasNearInfrared)
                            {
                                UInt16 nir = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[36..38]);
                                imageTile.NearInfrared![cellIndex] += nir;
                            }

                            imageTile.IntensityFirstReturn[cellIndex] += intensity;
                        }
                        else if (returnNumber == 2)
                        {
                            imageTile.SecondReturns[cellIndex] += 1;
                            imageTile.IntensitySecondReturn[cellIndex] += intensity;
                        }

                        float scanAngleInDegrees;
                        if (pointFormat < 6)
                        {
                            scanAngleInDegrees = (sbyte)pointBytes[16];
                        }
                        else
                        {
                            scanAngleInDegrees = 0.006F * BinaryPrimitives.ReadInt16LittleEndian(pointBytes[18..20]);
                        }
                        if (scanAngleInDegrees < 0.0F)
                        {
                            scanAngleInDegrees = -scanAngleInDegrees;
                        }
                        imageTile.ScanAngleMeanAbsolute[cellIndex] += scanAngleInDegrees;
                    }

                    int bytesOfNextPointRead = bytesRead - lastPointStartIndexInBuffer - pointRecordLength;
                    if (bytesOfNextPointRead > 0)
                    {
                        int nextPointStartIndex = alignedReadBuffer.Length - bytesOfNextPointRead;
                        alignedReadBuffer[nextPointStartIndex..].CopyTo(pointSpanBuffer);
                    }

                    firstPointInBufferIndex = -bytesOfNextPointRead;
                    readPosition += bytesRead;
                }
            }
        }

        public void ReadPointsToGrid(LasTile openedFile, Grid<PointListZirnc> metricsGrid)
        {
            LasHeader10 lasHeader = openedFile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            this.MoveToPoints(openedFile);

            // set cell capacity to a reasonable fraction of the tile's average density
            // Effort here is just to mitigate the amount of time spent in initial doubling of each ABA cell's point arrays.
            int cellInitialPointCapacity = LasReader.EstimateCellInitialPointCapacity(openedFile, metricsGrid);
            (int metricsIndexXmin, int metricsIndexXmaxInclusive, int metricsIndexYmin, int metricsIndexYmaxInclusive) = metricsGrid.GetIntersectingCellIndices(openedFile.GridExtent);
            for (int metricsIndexY = metricsIndexYmin; metricsIndexY <= metricsIndexYmaxInclusive; ++metricsIndexY)
            {
                for (int metricsIndexX = metricsIndexXmin; metricsIndexX <= metricsIndexXmaxInclusive; ++metricsIndexX)
                {
                    PointListZirnc metricsCell = metricsGrid[metricsIndexX, metricsIndexY];
                    if (metricsCell.Capacity == 0) // if cell already has allocated arrays leave them in place
                    {
                        metricsCell.Capacity = cellInitialPointCapacity;
                    }
                }
            }

            // read points
            // This implementation is currently synchronous as both synchronous and overlapped asynchronous reads hold an 18 TB IronWolf
            // Pro at a steady ~260 MB/s (ST18000NT001, 285 MB/s sustained transfer rate) under Windows 10 22H2, suggesting little ability
            // to improve on OS prefetching given the 128 kB to 1 MB buffer sizes used by LasTile.CreatePointReader(). Since SSDs and NVMes
            // offer lower read latencies additional read threads appear likely to be of limited benefit. If a tile is cached in memory
            // effective read speeds approach 600 MB/s (AMD Ryzen 5950X), suggesting a single LasReader can saturate a SATA III link (or
            // a RAID1 of two 3.5 drives). With NVMes multiple threads could read different parts of a tile's points concurrently but instead
            // applying those threads to different tiles should favor less lock contention in transferring those points to ABA grid cells.
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int returnNumberMask = lasHeader.GetReturnNumberMask();

            LasToGridTransform lasToMetricsTransformXY = new(lasHeader, metricsGrid);

            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            Span<byte> pointBytes = stackalloc byte[lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                this.BaseStream.ReadExactly(pointBytes);
                bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                if (notNoiseOrWithheld == false)
                {
                    continue;
                }

                if (lasToMetricsTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                {
                    // point lies outside of the ABA grid and is therefore not of interest
                    // In cases of partial overlap, this reader could be extended to take advantage of spatially indexed LAS files.
                    continue;
                }

                Int64 cellIndex = metricsGrid.ToCellIndex(xIndex, yIndex);
                PointListZirnc? abaCell = metricsGrid[cellIndex];
                if (abaCell == null)
                {
                    // point lies within ABA grid but is not within a cell of interest
                    continue;
                }

                float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..12]);
                abaCell.Z.Add(z);

                UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..14]);
                abaCell.Intensity.Add(intensity);

                byte returnNumber = (byte)(pointBytes[14] & returnNumberMask);
                abaCell.ReturnNumber.Add(returnNumber);

                abaCell.Classification.Add(classification);
            }

            // increment ABA cell tile load counts
            // Could include this in the initial loop, though that's not strictly proper.
            for (int abaYindex = metricsIndexYmin; abaYindex <= metricsIndexYmaxInclusive; ++abaYindex)
            {
                for (int abaXindex = metricsIndexXmin; abaXindex <= metricsIndexXmaxInclusive; ++abaXindex)
                {
                    PointListZirnc? abaCell = metricsGrid[abaXindex, abaYindex];
                    if (abaCell != null)
                    {
                        ++abaCell.TilesLoaded;
                        // useful breakpoint in debugging loaded-intersected issues between tiles
                        //if ((abaCell.TilesLoaded > 1) || (abaCell.TilesIntersected > 1))
                        //{
                        //    int q = 0;
                        //}
                    }
                }
            }
        }

        public void ReadPointsToGrid(LasFile openedFile, ScanMetricsRaster scanMetrics)
        {
            LasHeader10 lasHeader = openedFile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            this.MoveToPoints(openedFile);

            // read points
            // See performance notes in ReadPointsToGrid().
            bool hasGpsTime = lasHeader.PointsHaveGpsTime;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;

            LasToGridTransform lasToMetricsTransformXY = new(lasHeader, scanMetrics);

            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            Span<byte> pointBytes = stackalloc byte[lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                this.BaseStream.ReadExactly(pointBytes);
                if (lasToMetricsTransformXY.TryToOnGridIndices(pointBytes, out Int64 xIndex, out Int64 yIndex) == false)
                {
                    // point lies outside of the assigned metrics grid and is therefore not of interest
                    continue;
                }

                Int64 cellIndex = scanMetrics.ToCellIndex(xIndex, yIndex);
                bool notNoiseOrWithheld = LasReader.ReadFlags(pointBytes, pointFormat, out PointClassificationFlags classificationFlags, out ScanFlags scanFlags);
                if (notNoiseOrWithheld)
                {
                    ++scanMetrics.AcceptedPoints[cellIndex];

                    float scanAngleInDegrees;
                    if (pointFormat < 6)
                    {
                        scanAngleInDegrees = (sbyte)pointBytes[16];
                    }
                    else
                    {
                        scanAngleInDegrees = 0.006F * BinaryPrimitives.ReadInt16LittleEndian(pointBytes[18..]);
                    }

                    float absoluteScanAngleInDegrees = scanAngleInDegrees < 0.0F ? -scanAngleInDegrees : scanAngleInDegrees;
                    scanMetrics.ScanAngleMeanAbsolute[cellIndex] += absoluteScanAngleInDegrees;
                    if (scanMetrics.ScanAngleMin[cellIndex] > scanAngleInDegrees)
                    {
                        scanMetrics.ScanAngleMin[cellIndex] = scanAngleInDegrees;
                    }
                    if (scanMetrics.ScanAngleMax[cellIndex] < scanAngleInDegrees)
                    {
                        scanMetrics.ScanAngleMax[cellIndex] = scanAngleInDegrees;
                    }

                    // scanMetrics.NoiseOrWithheld[cellIndex] += 0.0F; // nothing to do
                    if ((scanFlags & ScanFlags.ScanDirection) != 0)
                    {
                        scanMetrics.ScanDirectionMean[cellIndex] += 1.0F;
                    }
                    if ((scanFlags & ScanFlags.EdgeOfFlightLine) != 0)
                    {
                        ++scanMetrics.EdgeOfFlightLine[cellIndex];
                    }
                    if ((scanFlags & ScanFlags.Overlap) != 0)
                    {
                        ++scanMetrics.Overlap[cellIndex];
                    }

                    if (hasGpsTime)
                    {
                        // TODO: GPS week time origin or adjusted standard GPS time origin (https://geozoneblog.wordpress.com/2013/10/31/when-was-lidar-point-collected/)
                        // tile.Header.GlobalEncoding
                        // unset: LAS 1.0 and 1.1 (always) or 1.2+ with GPS week time - needs Sunday midnight as origin
                        // AdjustedStandardGpsTime: standard GPS time origin of 1980-01-06T00:00:00 + 1 Gs = UTC origin 2011-09-14T01:46:25 due to 15 leap seconds = GPS time (TAI) origin 2011-09-14 01:46:40
                        // subsequent leap seconds: June 30 2012 + 2016, December 31 2016
                        // What is most useful set of output formats, assuming concurrent point and imagery acquisition? Time of day (hours), solar time, solar azimuth and elevation, ... ?
                        // WGS84 coordinate projection to obtain longitude and latitude? (https://stackoverflow.com/questions/71528556/transform-local-coordinates-to-wgs84-and-back-in-c-sharp, https://guideving.blogspot.com/2010/08/sun-position-in-c.html)
                        // What goes in C# and what in R?
                        double gpstime;
                        if (pointFormat < 6)
                        {
                            gpstime = BinaryPrimitives.ReadDoubleLittleEndian(pointBytes[20..]);
                        }
                        else
                        {
                            gpstime = BinaryPrimitives.ReadDoubleLittleEndian(pointBytes[22..]);
                        }
                        scanMetrics.GpstimeMean[cellIndex] += gpstime;
                        if (scanMetrics.GpstimeMin[cellIndex] > gpstime)
                        {
                            scanMetrics.GpstimeMin[cellIndex] = gpstime;
                        }
                        if (scanMetrics.GpstimeMax[cellIndex] < gpstime)
                        {
                            scanMetrics.GpstimeMax[cellIndex] = gpstime;
                        }
                    }
                }
                else
                {
                    ++scanMetrics.NoiseOrWithheld[cellIndex];
                }
            }
        }

        public void ReadVariableAndExtendedVariableLengthRecords(LasFile openedFile)
        {
            LasHeader10 lasHeader = openedFile.Header;
            if (lasHeader.NumberOfVariableLengthRecords > 0)
            {
                if (this.BaseStream.Position != lasHeader.HeaderSize)
                {
                    this.BaseStream.Seek(lasHeader.HeaderSize, SeekOrigin.Begin);
                }

                openedFile.VariableLengthRecords.Capacity += (int)lasHeader.NumberOfVariableLengthRecords;
                this.ReadVariableLengthRecordsAndTrailingBytes(openedFile);
            }

            if (lasHeader.VersionMinor >= 4)
            {
                LasHeader14 header14 = (LasHeader14)lasHeader;
                if (header14.NumberOfExtendedVariableLengthRecords > 0)
                {
                    if (this.BaseStream.Position != (long)header14.StartOfFirstExtendedVariableLengthRecord)
                    {
                        this.BaseStream.Seek((long)header14.StartOfFirstExtendedVariableLengthRecord, SeekOrigin.Begin);
                    }

                    openedFile.VariableLengthRecords.Capacity += (int)header14.NumberOfExtendedVariableLengthRecords;
                    this.ReadExtendedVariableLengthRecords(openedFile); // trailing bytes not currently supported
                }
            }

            bool vlrCountMismatch = this.DiscardOverrunningVlrs ? openedFile.VariableLengthRecords.Count > openedFile.Header.NumberOfVariableLengthRecords : openedFile.VariableLengthRecords.Count != openedFile.Header.NumberOfVariableLengthRecords;
            if (vlrCountMismatch)
            {
                throw new InvalidDataException(".las file header indicates " + openedFile.Header.NumberOfVariableLengthRecords + " variable length records should be present but " + openedFile.VariableLengthRecords.Count + " records were read.");
            }

            UInt32 numberOfExtendedVariableLengthRecords = 0;
            if (openedFile.Header is LasHeader14 lasHeader14)
            {
                numberOfExtendedVariableLengthRecords = lasHeader14.NumberOfExtendedVariableLengthRecords;
            }
            if (numberOfExtendedVariableLengthRecords != openedFile.ExtendedVariableLengthRecords.Count)
            {
                throw new InvalidDataException(".las file header indicates " + numberOfExtendedVariableLengthRecords + " extended variable length records should be present but " + openedFile.ExtendedVariableLengthRecords.Count + " extended records were read.");
            }
        }

        private void ReadExtendedVariableLengthRecords(LasFile openedFile)
        {
            Span<byte> evlrBytes = stackalloc byte[ExtendedVariableLengthRecord.HeaderSizeInBytes];
            for (int recordIndex = 0; recordIndex < openedFile.Header.NumberOfVariableLengthRecords; ++recordIndex)
            {
                this.BaseStream.ReadExactly(evlrBytes);
                UInt16 reserved = BinaryPrimitives.ReadUInt16LittleEndian(evlrBytes);
                string userID = Encoding.UTF8.GetString(evlrBytes.Slice(2, 16)).Trim('\0');
                UInt16 recordID = BinaryPrimitives.ReadUInt16LittleEndian(evlrBytes[18..]);
                UInt64 recordLengthAfterHeader = BinaryPrimitives.ReadUInt64LittleEndian(evlrBytes[20..]) ;
                string description = Encoding.UTF8.GetString(evlrBytes.Slice(28, 32)).Trim('\0');

                // create specific object if record has a well known type
                long endOfRecordPosition = this.BaseStream.Position + (long)recordLengthAfterHeader;

                byte[] data = new byte[(int)recordLengthAfterHeader];
                this.BaseStream.ReadExactly(data);
                ExtendedVariableLengthRecordUntyped vlr = new(reserved, userID, recordID, recordLengthAfterHeader, description, data); ;
                openedFile.ExtendedVariableLengthRecords.Add(vlr);

                // skip any unused bytes at end of record
                if (this.BaseStream.Position != endOfRecordPosition)
                {
                    this.BaseStream.Seek(endOfRecordPosition, SeekOrigin.Begin);
                }
            }
        }

        private void ReadVariableLengthRecordsAndTrailingBytes(LasFile openedFile)
        {
            if (this.BaseStream.Position != openedFile.Header.HeaderSize)
            {
                throw new InvalidOperationException("Stream is positioned at " + this.BaseStream.Position + " rather than at the end of the .las file's header (" + openedFile.Header.HeaderSize + " bytes).");
            }

            Span<byte> vlrBytes = stackalloc byte[VariableLengthRecord.HeaderSizeInBytes];
            for (int recordIndex = 0; recordIndex < openedFile.Header.NumberOfVariableLengthRecords; ++recordIndex)
            {
                // not enough bytes remaining before points for read of a variable length header's public header block
                if ((this.BaseStream.Position + VariableLengthRecord.HeaderSizeInBytes) >= openedFile.Header.OffsetToPointData)
                {
                    // workaround for Applied Imagery's QT Modler's tendency to set the number of variable length records too high by one record
                    if (this.DiscardOverrunningVlrs)
                    {
                        continue; // extra bytes will be placed into BytesAfterVariableLengthRecords
                    }
                    else
                    {
                        throw new InvalidDataException(".las file's variable length records extend into the point data segment. Expected variable length records to end at " + openedFile.Header.OffsetToPointData + " bytes but variable length record " + recordIndex + "'s header extends to " + (this.BaseStream.Position + VariableLengthRecord.HeaderSizeInBytes) + " bytes.");
                    }
                }

                this.BaseStream.ReadExactly(vlrBytes);
                UInt16 reserved = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
                string userID = Encoding.UTF8.GetString(vlrBytes.Slice(2, 16)).Trim('\0');
                UInt16 recordID = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[18..]);
                UInt64 recordLengthAfterHeader = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[20..]);
                string description = Encoding.UTF8.GetString(vlrBytes.Slice(22, 32)).Trim('\0');

                // create specific object if record has a well known type
                long endOfRecordPositionExclusive = this.BaseStream.Position + (long)recordLengthAfterHeader;
                if (endOfRecordPositionExclusive > openedFile.Header.OffsetToPointData)
                {
                    // workaround for Applied Imagery's QT Modler's tendency to set the number of variable length records too high by one record
                    if (this.DiscardOverrunningVlrs)
                    {
                        this.BaseStream.Seek(-VariableLengthRecord.HeaderSizeInBytes, SeekOrigin.Current); // move back to end of VLR
                        continue; // extra bytes will be placed into BytesAfterVariableLengthRecords
                    }
                    else
                    {
                        throw new InvalidDataException(".las file's variable length records extend into the point data segment. Expected variable length records to end at " + openedFile.Header.OffsetToPointData + " bytes but variable length record " + recordIndex + " extends to " + endOfRecordPositionExclusive + " bytes.");
                    }
                }

                UInt16 recordLengthAfterHeader16 = (UInt16)recordLengthAfterHeader;
                VariableLengthRecord? vlr = null;
                switch (userID)
                {
                    case LasFile.LasfProjection:
                        if (recordID == OgcMathTransformWktRecord.LasfProjectionRecordID)
                        {
                            byte[] wktBytes = new byte[recordLengthAfterHeader16]; // untested
                            this.BaseStream.ReadExactly(wktBytes);
                            OgcMathTransformWktRecord wkt = new(reserved, recordLengthAfterHeader16, description, wktBytes);
                            vlr = wkt;
                        }
                        else if (recordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID)
                        {
                            byte[] wktBytes = new byte[recordLengthAfterHeader16]; // assume too long for stackalloc
                            this.BaseStream.ReadExactly(wktBytes);
                            OgcCoordinateSystemWktRecord wkt = new(reserved, recordLengthAfterHeader16, description, wktBytes);
                            vlr = wkt;
                        }
                        else if (recordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID)
                        {
                            this.BaseStream.ReadExactly(vlrBytes[..GeoKeyDirectoryTagRecord.CoreSizeInBytes]);
                            GeoKeyDirectoryTagRecord crs = new(reserved, recordLengthAfterHeader16, description, vlrBytes);
                            for (int keyEntryIndex = 0; keyEntryIndex < crs.NumberOfKeys; ++keyEntryIndex)
                            {
                                this.BaseStream.ReadExactly(vlrBytes[..GeoKeyEntry.SizeInBytes]);
                                crs.KeyEntries.Add(new(vlrBytes));
                            }
                            vlr = crs;
                        }
                        else if (recordID == GeoDoubleParamsTagRecord.LasfProjectionRecordID)
                        {
                            byte[] stringBytes = new byte[recordLengthAfterHeader16]; // untested
                            this.BaseStream.ReadExactly(stringBytes);
                            GeoDoubleParamsTagRecord crs = new(reserved, recordLengthAfterHeader16, description, stringBytes);
                            vlr = crs;
                        }
                        else if (recordID == GeoAsciiParamsTagRecord.LasfProjectionRecordID)
                        {
                            byte[] stringBytes = new byte[recordLengthAfterHeader16]; // probably not too long for stackalloc, can be made switchable if needed
                            this.BaseStream.ReadExactly(stringBytes);
                            GeoAsciiParamsTagRecord crs = new(reserved, recordLengthAfterHeader16, description, stringBytes);
                            vlr = crs;
                        }
                        break;
                    case LasFile.LasfSpec:
                        if (recordID == 0)
                        {
                            throw new NotImplementedException("ClassificationLookupRecord");
                        }
                        else if (recordID == 3)
                        {
                            throw new NotImplementedException("TextAreaDescriptionRecord");
                        }
                        else if (recordID == ExtraBytesRecord.LasfSpecRecordID)
                        {
                            byte[] extraBytesData = new byte[recordLengthAfterHeader16]; // probably not too long for stackalloc but CA2014
                            this.BaseStream.ReadExactly(extraBytesData);
                            ExtraBytesRecord extraBytes = new(reserved, description, extraBytesData);
                            vlr = extraBytes;
                        }
                        else if (recordID == 7)
                        {
                            throw new NotImplementedException("SupersededRecord");
                        }
                        else if ((recordID > 99) && (recordID < 355))
                        {
                            throw new NotImplementedException("WaveformPacketDescriptorRecord");
                        }
                        break;
                    case LazVariableLengthRecord.LazEncodingUserID:
                        this.BaseStream.ReadExactly(vlrBytes[..LazVariableLengthRecord.CoreSizeInBytes]);
                        LazVariableLengthRecord laz = new(reserved, recordLengthAfterHeader16, description, vlrBytes);
                        for (int itemIndex = 0; itemIndex < laz.NumItems; ++itemIndex)
                        {
                            this.BaseStream.ReadExactly(vlrBytes[..LazVariableLengthRecord.ItemSizeInBytes]);
                            laz.ReadItem(itemIndex, vlrBytes);
                        }
                        vlr = laz;
                        break;
                    default:
                        // leave vlr null
                        break;
                }

                // otherwise, create a general container for the record which the caller can parse
                if (vlr == null)
                {
                    byte[] data = new byte[(int)recordLengthAfterHeader];
                    this.BaseStream.ReadExactly(data);
                    vlr = new VariableLengthRecordUntyped(reserved, userID, recordID, recordLengthAfterHeader16, description, data);
                }

                openedFile.VariableLengthRecords.Add(vlr);

                // skip any unused bytes at end of record
                if (this.BaseStream.Position != endOfRecordPositionExclusive)
                {
                    this.BaseStream.Seek(endOfRecordPositionExclusive, SeekOrigin.Begin);
                }
            }

            if (this.BaseStream.Position < openedFile.Header.OffsetToPointData)
            {
                // capture any extra bytes between the end of the variable length records and the start of point data
                // These shouldn't do anything and should be droppable as unused padding but, for example, .laz files actually require two trailing bytes
                // to work (both are zero). So any extra bytes present are captured here in case subsequent code needs to flow them through to another file
                // or has some other use for them.
                // See also https://github.com/ASPRSorg/LAS/issues/37
                //          https://github.com/ASPRSorg/LAS/issues/91
                //          https://github.com/ASPRSorg/LAS/issues/145
                int bytesAfterVariableLengthRecords = (int)(openedFile.Header.OffsetToPointData - this.BaseStream.Position);
                openedFile.BytesAfterVariableLengthRecords = new byte[bytesAfterVariableLengthRecords];
                this.BaseStream.ReadExactly(openedFile.BytesAfterVariableLengthRecords);
            }
        }

        public static void ThrowOnUnsupportedPointFormat(LasHeader10 lasHeader)
        {
            byte pointFormat = lasHeader.PointDataRecordFormat;
            if (pointFormat > 10)
            {
                throw new NotSupportedException("Unhandled point data record format " + pointFormat + ".");
            }
        }
    }
}
