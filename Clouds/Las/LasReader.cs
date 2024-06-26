﻿using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using Mars.Clouds.GdalExtensions;

namespace Mars.Clouds.Las
{
    public class LasReader : LasStream
    {
        // basic perf profile on tiles cached in memory: ReadPoints() for xyzcs, 5950X, DDR4-3200, .NET 8, 9 tiles totaling 28.4 GB
        // batch size, points  read speed, GB/s
        // 1                   1.4-1.5
        // 2                   1.8-1.9
        // 4                   2.0-2.1
        // 8, 16, 32           2.2-2.4
        public const int ReadExactSizeInPoints = 16; // might average slightly faster than 8 or 32

        public const float ReadPointsToGridSpeedInGBs = 0.67F; // TODO: update profile
        public const float ReadPointsToXyzcsSpeedInGBs = 2.2F; // 5950X

        public bool DiscardOverrunningVlrs { get; set; }

        public LasReader(Stream stream)
            : base(stream)
        {
            this.DiscardOverrunningVlrs = false;
        }

        public static LasReader CreateForHeaderAndVlrRead(string lasPath, bool discardOverrunningVlrs)
        {
            using FileStream stream = new(lasPath, FileMode.Open, FileAccess.Read, FileShare.Read, Constant.Las.HeaderAndVlrReadBufferSizeInBytes);
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

        public static LasReader CreateForPointRead(string lasPath, long fileSizeInBytes)
        {
            return new LasReader(LasReader.CreatePointStream(lasPath, fileSizeInBytes, FileAccess.Read));
        }

        protected static FileStream CreatePointStream(string lasPath, long fileSizeInBytes, FileAccess fileAccess)
        {
            // rough scaling with file size from https://github.com/dotnet/runtime/discussions/74405#discussioncomment-3488674
            int bufferSizeInKB;
            if (fileSizeInBytes > 512 * 1024 * 1024) // > 512 MB
            {
                bufferSizeInKB = 1024;
            }
            else if (fileSizeInBytes > 64 * 1024 * 1024) // > 64 MB
            {
                bufferSizeInKB = 512;
            }
            else if (fileSizeInBytes > 8 * 1024 * 1024) // > 8 MB
            {
                bufferSizeInKB = 256;
            }
            else // ≤ 8 MB
            {
                bufferSizeInKB = 128;
            }

            FileStream stream = new(lasPath, FileMode.Open, fileAccess, FileShare.Read, bufferSizeInKB * 1024);
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

        /// <returns>false if point is classified as noise or is withdrawn</returns>
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

        public void ReadPointsToImage(LasTile lasTile, ImageRaster<UInt64> image)
        {
            LasHeader10 lasHeader = lasTile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            int redOffset = lasHeader.GetRgbOffset();
            int greenOffset = redOffset + 2;
            int blueOffset = redOffset + 4;
            int nirOffset = lasTile.Header.GetNearInfraredOffset();
            if ((nirOffset >= 0) && (image.NearInfrared == null))
            {
                // for now, fail if points have NIR data but image won't capture it
                // This can be relaxed if needed.
                throw new ArgumentOutOfRangeException(nameof(image), "Point cloud tile '" + lasTile.FilePath + "' contains near infrared data but image lacks a near infrared band to transfer the data to.");
            }
            this.MoveToPoints(lasTile);

            // read points
            // See performance notes in ReadPointsToGrid().
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            double xOffset = lasHeader.XOffset;
            double xScale = lasHeader.XScaleFactor;
            double yOffset = lasHeader.YOffset;
            double yScale = lasHeader.YScaleFactor;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            Span<byte> pointBytes = stackalloc byte[lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            int returnNumberMask = lasHeader.GetReturnNumberMask();
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                this.BaseStream.ReadExactly(pointBytes);

                byte returnNumber = (byte)(pointBytes[14] & returnNumberMask);
                if (returnNumber > 2)
                {
                    // third and subsequent returns not currently used
                    // Points marked with return number 0 (which is LAS 1.4 compliance issue) pass through this check.
                    continue;
                }
               
                bool notNoiseOrWithheld = LasReader.ReadFlags(pointBytes, pointFormat, out PointClassificationFlags classificationFlags, out ScanFlags scanFlags);
                if (notNoiseOrWithheld == false)
                {
                    // point isn't valid data => skip
                    continue;
                }

                double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                (int xIndex, int yIndex) = image.ToGridIndices(x, y);
                if ((xIndex < 0) || (yIndex < 0) || (xIndex >= image.SizeX) || (yIndex >= image.SizeY))
                {
                    // point lies outside of the DSM tile and is therefore not of interest
                    // If the DSM tile's extents are equal to or larger than the point cloud tile in all directions reaching this case is an error.
                    // For now DSM tiles with smaller extents than the point cloud tile aren't supported.
                    throw new NotSupportedException("Point at (x = " + x + ", y = " + y + " lies outside DSM tile extents (" + image.GetExtentString() + " .");
                }

                int cellIndex = image.ToCellIndex(xIndex, yIndex);
                UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..]);
                if (returnNumber == 1)
                {
                    // assume first returns are always the most representative => only first returns contribute to RGB+NIR
                    ++image.FirstReturns[cellIndex];

                    if (redOffset >= 0)
                    {
                        UInt16 red = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[redOffset..]);
                        image.Red[cellIndex] += red;
                        UInt16 green = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[greenOffset..]);
                        image.Green[cellIndex] += green;
                        UInt16 blue = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[blueOffset..]);
                        image.Blue[cellIndex] += blue;
                    }
                    if (nirOffset >= 0)
                    {
                        Debug.Assert(image.NearInfrared != null); // caught by checking
                        UInt16 nir = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[nirOffset..]);
                        image.NearInfrared[cellIndex] += nir;
                    }

                    image.Intensity[cellIndex] += intensity;
                }
                else if (returnNumber == 2)
                {
                    image.SecondReturns[cellIndex] += 1;
                    image.IntensitySecondReturn[cellIndex] += intensity;
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
            int gpstimeOffset = lasHeader.GetGpstimePointOffset();
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            double xOffset = lasHeader.XOffset;
            double xScale = lasHeader.XScaleFactor;
            double yOffset = lasHeader.YOffset;
            double yScale = lasHeader.YScaleFactor;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            Span<byte> pointBytes = stackalloc byte[lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                this.BaseStream.ReadExactly(pointBytes);
                double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                (int xIndex, int yIndex) = scanMetrics.ToGridIndices(x, y);
                if ((xIndex < 0) || (yIndex < 0) || (xIndex >= scanMetrics.SizeX) || (yIndex >= scanMetrics.SizeY))
                {
                    // point lies outside of the assigned metrics grid and is therefore not of interest
                    continue;
                }

                int cellIndex = scanMetrics.ToCellIndex(xIndex, yIndex);
                bool notNoiseOrWithheld = LasReader.ReadFlags(pointBytes, pointFormat, out PointClassificationFlags classificationFlags, out ScanFlags scanFlags);
                if (notNoiseOrWithheld)
                {
                    ++scanMetrics.AcceptedPoints[cellIndex];

                    float scanAngle;
                    if (openedFile.Header.PointDataRecordFormat < 6)
                    {
                        scanAngle = (sbyte)pointBytes[16];
                    }
                    else
                    {
                        scanAngle = 0.006F * BinaryPrimitives.ReadInt16LittleEndian(pointBytes[18..]);
                    }

                    // TODO: mean absolute scan angle
                    scanMetrics.ScanAngleMeanAbsolute[cellIndex] += MathF.Abs(scanAngle);
                    if (scanMetrics.ScanAngleMin[cellIndex] > scanAngle)
                    {
                        scanMetrics.ScanAngleMin[cellIndex] = scanAngle;
                    }
                    if (scanMetrics.ScanAngleMax[cellIndex] < scanAngle)
                    {
                        scanMetrics.ScanAngleMax[cellIndex] = scanAngle;
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

                    if (gpstimeOffset >= 0)
                    {
                        // TODO: GPS week time origin or adjusted standard GPS time origin (https://geozoneblog.wordpress.com/2013/10/31/when-was-lidar-point-collected/)
                        // tile.Header.GlobalEncoding
                        // unset: LAS 1.0 and 1.1 (always) or 1.2+ with GPS week time - needs Sunday midnight as origin
                        // AdjustedStandardGpsTime: standard GPS time origin of 1980-01-06T00:00:00 + 1 Gs = UTC origin 2011-09-14T01:46:25 due to 15 leap seconds = GPS time (TAI) origin 2011-09-14 01:46:40
                        // subsequent leap seconds: June 30 2012 + 2016, December 31 2016
                        // What is most useful set of output formats, assuming concurrent point and imagery acquisition? Time of day (hours), solar time, solar azimuth and elevation, ... ?
                        // WGS84 coordinate projection to obtain longitude and latitude? (https://stackoverflow.com/questions/71528556/transform-local-coordinates-to-wgs84-and-back-in-c-sharp, https://guideving.blogspot.com/2010/08/sun-position-in-c.html)
                        // What goes in C# and what in R?
                        double gpstime = BinaryPrimitives.ReadDoubleLittleEndian(pointBytes[gpstimeOffset..]);
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

        public PointList<PointBatchXyzcs> ReadPoints(LasTile openedTile, ObjectPool<PointBatchXyzcs> pointBatchPool)
        {
            LasHeader10 lasHeader = openedTile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            PointList<PointBatchXyzcs> tilePoints = new(openedTile, pointBatchPool);
            this.MoveToPoints(openedTile);

            // read points
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;

            int pointBatchIndex = 0;
            PointBatchXyzcs pointBatch = tilePoints[pointBatchIndex];
            //GCHandle classificationHandle = GCHandle.Alloc(pointBatch.Classification, GCHandleType.Pinned);
            //UInt32* batchClassificationAddress = (UInt32*)classificationHandle.AddrOfPinnedObject();
            //GCHandle xHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
            //int* batchXaddress = (int*)xHandle.AddrOfPinnedObject();
            //GCHandle yHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
            //int* batchYaddress = (int*)yHandle.AddrOfPinnedObject();
            //GCHandle zHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
            //int* batchZaddress = (int*)zHandle.AddrOfPinnedObject();
            //GCHandle sourceIDhandle = GCHandle.Alloc(pointBatch.SourceID, GCHandleType.Pinned);
            //UInt64* batchSourceIDaddress = (UInt64*)sourceIDhandle.AddrOfPinnedObject();

            int pointIndexInBatch = 0;
            //int xyzStoreIndex = 0;
            //int classificationStoreIndex = 0;
            //int sourceIDstoreIndex = 0;

            //UInt64 classificationBuffer64 = 0;
            //Vector128<UInt64> classificationBuffer128 = Vector128<UInt64>.Zero;
            //Vector128<int> xBuffer128 = Vector128<int>.Zero;
            //Vector128<int> yBuffer128 = Vector128<int>.Zero;
            //Vector128<int> zBuffer128 = Vector128<int>.Zero;
            //UInt64 sourceIDbuffer64 = 0;
            //Vector128<UInt64> sourceIDbuffer128 = Vector128<UInt64>.Zero;

            Span<byte> pointReadBuffer = stackalloc byte[LasReader.ReadExactSizeInPoints * pointRecordLength]; // for now, assume small enough to stackalloc
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadExactSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadExactSizeInPoints ? LasReader.ReadExactSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * pointRecordLength;
                this.BaseStream.ReadExactly(pointReadBuffer[..bytesToRead]);

                // ~400 MB/s slower to unroll this loop than to leave it rolled
                // BinaryPrimitives.ReadInt32LittleEndian(), BitConverter.ToInt32(), and shifting bytes all bench to near identical performance
                // MemoryMarshal.Read<int>() may be slightly slower than BinaryPrimitives.ReadInt32LittleEndian(). Benchmarking also shows no benefit
                // (and suggests maybe some detriment) to MemoryMarshal.Cast<byte, int>() rather than BinaryPrimitives.ReadInt32LittleEndian().
                // 
                // Since batch contains eight points Avx2.GatherVector256() and Store() could be used in the typical case no points are noise or
                // withheld. Testing suggests batching stores, at least, can offer speed increases but realizing meaningful gains in a complete
                // implementation appears difficult. Of the various SIMD combinations possible within the commented out code, the most effective
                // appears to be batching just x, y, and z, which increases read speeds by perhaps ~100 MB/s. Profiling shows batching classification
                // and source ID most likely results in decreased read speeds.
                //
                // Because stores and buffer reads are misaligned, gathering x, y, and z from pointReadBuffer is not practical (pointReadBuffer could
                // easily be allocated on the heap and pinned for access, however).
                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += pointRecordLength)
                {
                    ReadOnlySpan<byte> pointBytes = pointReadBuffer[batchOffset..];
                    bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                    if (notNoiseOrWithheld == false)
                    {
                        continue;
                    }

                    //classificationBuffer64 = (classificationBuffer64 << 8) | (byte)classification;
                    pointBatch.Classification[pointIndexInBatch] = classification;
                    //xBuffer128 = AvxExtensions.ShuffleInAndUp(BinaryPrimitives.ReadInt32LittleEndian(pointBytes), xBuffer128);
                    //yBuffer128 = AvxExtensions.ShuffleInAndUp(BinaryPrimitives.ReadInt32LittleEndian(pointBytes), yBuffer128);
                    //zBuffer128 = AvxExtensions.ShuffleInAndUp(BinaryPrimitives.ReadInt32LittleEndian(pointBytes), zBuffer128);
                    pointBatch.X[pointIndexInBatch] = BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                    pointBatch.Y[pointIndexInBatch] = BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                    pointBatch.Z[pointIndexInBatch] = BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..]);
                    // somehow ~200 MB/s faster to inline this than to call a static function, even with [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    pointBatch.SourceID[pointIndexInBatch] = pointFormat < 6 ? BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[18..]) : BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[20..]);
                    //UInt16 sourceID = pointFormat < 6 ? BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[18..]) : BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[20..]);
                    //sourceIDbuffer64 = (sourceIDbuffer64 << 16) | sourceID;
                    ++pointIndexInBatch;
                    //++xyzStoreIndex;

                    // noise and withheld points result in pointIndexInBatch not being aligned with batchOffset
                    // Transition to next batch can therefore occur on at (literally) point.
                    //if (xyzStoreIndex == 4)
                    //{
                    //    Avx2.Store(batchXaddress, xBuffer128);
                    //    batchXaddress += 4;
                    //    Avx2.Store(batchYaddress, yBuffer128);
                    //    batchYaddress += 4;
                    //    Avx2.Store(batchZaddress, zBuffer128);
                    //    batchZaddress += 4;
                    //    xyzStoreIndex = 0;
                    //
                    //    sourceIDbuffer128 = AvxExtensions.ShuffleInAndUp(sourceIDbuffer64, sourceIDbuffer128);
                    //    sourceIDstoreIndex += 4;
                    //}
                    //if (sourceIDstoreIndex == 8)
                    //{
                    //    Avx2.Store(batchSourceIDaddress, sourceIDbuffer128);
                    //    batchSourceIDaddress += 2;
                    //    sourceIDstoreIndex = 0;
                    //
                    //    classificationBuffer128 = AvxExtensions.ShuffleInAndUp(classificationBuffer64, classificationBuffer128);
                    //    classificationBuffer64 = 0;
                    //    classificationStoreIndex += 8;
                    //
                    //}
                    //if (classificationStoreIndex == 16)
                    //{
                    //    Avx2.Store(batchClassificationAddress, classificationBuffer128);
                    //    batchClassificationAddress += 4;
                    //    classificationStoreIndex = 0;
                    //}
                    if (pointIndexInBatch == pointBatch.Capacity)
                    {
                        //classificationHandle.Free();
                        //xHandle.Free();
                        //yHandle.Free();
                        //zHandle.Free();
                        //sourceIDhandle.Free();

                        pointBatch.Count = pointIndexInBatch;
                        pointBatch = tilePoints[++pointBatchIndex];
                        pointIndexInBatch = 0;

                        //classificationHandle = GCHandle.Alloc(pointBatch.Classification, GCHandleType.Pinned);
                        //batchClassificationAddress = (UInt32*)classificationHandle.AddrOfPinnedObject();
                        //xHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
                        //batchXaddress = (int*)xHandle.AddrOfPinnedObject();
                        //yHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
                        //batchYaddress = (int*)yHandle.AddrOfPinnedObject();
                        //zHandle = GCHandle.Alloc(pointBatch.X, GCHandleType.Pinned);
                        //batchZaddress = (int*)zHandle.AddrOfPinnedObject();
                        //sourceIDhandle = GCHandle.Alloc(pointBatch.SourceID, GCHandleType.Pinned);
                        //batchSourceIDaddress = (UInt64*)sourceIDhandle.AddrOfPinnedObject();
                    }
                }
            }

            // TODO if using SIMD: last store
            //classificationHandle.Free();
            //xHandle.Free();
            //yHandle.Free();
            //zHandle.Free();
            //sourceIDhandle.Free();

            // batch size typically leaves unused capacity at ends of arrays
            // Noise or withheld points also result in unused capacity.
            // Pull batches from object pool on demand to avoid need to trim unused batches from tile points list? For now an unused batch
            // at the end is possible, though fairly unlikely.
            pointBatch.Count = pointIndexInBatch;
            return tilePoints;
        }

        public void ReadPointsToGrid(LasTile openedFile, Grid<PointListZirnc> metricsGrid)
        {
            LasHeader10 lasHeader = openedFile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            this.MoveToPoints(openedFile);

            // set cell capacity to a reasonable fraction of the tile's average density
            // Effort here is just to mitigate the amount of time spent in initial doubling of each ABA cell's point arrays.
            int cellInitialPointCapacity = LasReader.EstimateCellInitialPointCapacity(openedFile, metricsGrid);
            (int abaXindexMin, int abaXindexMaxInclusive, int abaYindexMin, int abaYindexMaxInclusive) = metricsGrid.GetIntersectingCellIndices(openedFile.GridExtent);
            for (int abaYindex = abaYindexMin; abaYindex <= abaYindexMaxInclusive; ++abaYindex)
            {
                for (int abaXindex = abaXindexMin; abaXindex <= abaXindexMaxInclusive; ++abaXindex)
                {
                    PointListZirnc? abaCell = metricsGrid[abaXindex, abaYindex];
                    if (abaCell != null)
                    {
                        if (abaCell.Capacity == 0) // if cell already has some allocation, assume it's overlapped by another tile and do nothing
                        {
                            abaCell.Capacity = cellInitialPointCapacity;
                        }
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
            double xOffset = lasHeader.XOffset;
            double xScale = lasHeader.XScaleFactor;
            double yOffset = lasHeader.YOffset;
            double yScale = lasHeader.YScaleFactor;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            int returnNumberMask = lasHeader.GetReturnNumberMask();
            Span<byte> pointBytes = stackalloc byte[lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                this.BaseStream.ReadExactly(pointBytes);
                bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                if (notNoiseOrWithheld == false)
                {
                    continue;
                }

                double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                (int xIndex, int yIndex) = metricsGrid.ToGridIndices(x, y);
                if ((xIndex < 0) || (yIndex < 0) || (xIndex >= metricsGrid.SizeX) || (yIndex >= metricsGrid.SizeY))
                {
                    // point lies outside of the ABA grid and is therefore not of interest
                    // In cases of partial overlap, this reader could be extended to take advantage of spatially indexed LAS files.
                    continue;
                }
                PointListZirnc? abaCell = metricsGrid[xIndex, yIndex];
                if (abaCell == null)
                {
                    // point lies within ABA grid but is not within a cell of interest
                    continue;
                }

                if (x < abaCell.PointXMin)
                {
                    abaCell.PointXMin = x;
                }
                if (x > abaCell.PointXMax)
                {
                    abaCell.PointXMax = x;
                }
                if (y < abaCell.PointYMin)
                {
                    abaCell.PointYMin = y;
                }
                if (y > abaCell.PointYMax)
                {
                    abaCell.PointYMax = y;
                }

                float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..]);
                abaCell.Z.Add(z);

                UInt16 intensity = BinaryPrimitives.ReadUInt16LittleEndian(pointBytes[12..]);
                abaCell.Intensity.Add(intensity);

                byte returnNumber = (byte)(pointBytes[14] & returnNumberMask);
                abaCell.ReturnNumber.Add(returnNumber);

                abaCell.Classification.Add(classification);
            }

            // increment ABA cell tile load counts
            // Could include this in the initial loop, though that's not strictly proper.
            for (int abaYindex = abaYindexMin; abaYindex <= abaYindexMaxInclusive; ++abaYindex)
            {
                for (int abaXindex = abaXindexMin; abaXindex <= abaXindexMaxInclusive; ++abaXindex)
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
