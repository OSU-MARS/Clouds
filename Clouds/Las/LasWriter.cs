using Mars.Clouds.GdalExtensions;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mars.Clouds.Las
{
    public class LasWriter(FileStream stream) : LasStream<FileStream>(stream)
    {
        /// <param name="reader">Stream to read points and any extended records from. Caller must ensure reader is positioned at the first point in the source .las file.</param>
        /// <returns>Number of remaining points by return.</returns>
        public LasFilteringResult CopyNonNoisePoints(LasReader reader, LasFile lasFile)
        {
            LasHeader10 lasHeader = lasFile.Header;
            if (reader.BaseStream.Position != lasHeader.OffsetToPointData)
            {
                throw new ArgumentOutOfRangeException(nameof(reader), "Reader is at offset " + reader.BaseStream.Position + " rather than at the " + lasHeader.OffsetToPointData + " byte offset to the start of point data.");
            }
            if (this.BaseStream.Position != lasHeader.OffsetToPointData)
            {
                throw new InvalidOperationException(".las writer must be positioned at the start of point data (offset " + lasHeader.OffsetToPointData + " bytes) to write points. The writer is currently positioned at " + this.BaseStream.Position + " bytes.");
            }
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);

            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength;
            int returnNumberMask = pointFormat < 6 ? 0x07 : 0x0f;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;

            Span<UInt64> numberOfPointsRemovedByReturn = stackalloc UInt64[LasHeader14.SupportedNumberOfReturns];
            byte[] pointReadBuffer = new byte[LasReader.ReadBufferSizeInPoints * pointRecordLength]; // for now, assume small enough to stackalloc
            float zMin = Single.MaxValue;
            float zMax = Single.MinValue;
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadBufferSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadBufferSizeInPoints ? LasReader.ReadBufferSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * pointRecordLength;
                reader.BaseStream.ReadExactly(pointReadBuffer, 0, bytesToRead);

                int noiseOrWithheldPoints = 0;
                int pointMask = 0;
                for (int batchOffset = 0, pointFlag = 0x1; batchOffset < bytesToRead; batchOffset += pointRecordLength, pointFlag <<= 1)
                {
                    ReadOnlySpan<byte> pointBytes = pointReadBuffer.AsSpan(batchOffset, pointRecordLength);
                    bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification _);
                    if (notNoiseOrWithheld == false)
                    {
                        int returnNumber = pointBytes[14] & returnNumberMask;
                        ++numberOfPointsRemovedByReturn[returnNumber];

                        pointMask |= pointFlag;
                        ++noiseOrWithheldPoints;
                    }
                    else
                    {
                        float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..12]);
                        if (z < zMin)
                        {
                            zMin = z;
                        }
                        if (z > zMax)
                        {
                            zMax = z;
                        }
                    }
                }

                int bytesToWrite = bytesToRead;
                if (noiseOrWithheldPoints > 0)
                {
                    bytesToWrite -= noiseOrWithheldPoints * pointRecordLength;
                    if (bytesToWrite > 0)
                    {
                        int destinationOffset = 0;
                        for (int sourceOffset = 0, pointFlag = 0x1; sourceOffset < bytesToRead; sourceOffset += pointRecordLength, pointFlag <<= 1)
                        {
                            if ((pointMask & pointFlag) != 0)
                            {
                                // skip point
                                continue;
                            }

                            if (destinationOffset != sourceOffset)
                            {
                                // point is retained but needs to be shifted to an earlier position in the buffer
                                pointReadBuffer.AsSpan(sourceOffset, pointRecordLength).CopyTo(pointReadBuffer.AsSpan(destinationOffset, pointRecordLength));
                            }
                            // no bytes to move if points have been skipped yet
                            // Could use a more complex approach which tries to identify larger blocks to move within the buffer but, as most
                            // points are retained, it's likely any performance gain would be negligible.

                            destinationOffset += pointRecordLength;
                        }
                    }
                }

                if (bytesToWrite > 0)
                {
                    this.BaseStream.Write(pointReadBuffer, 0, bytesToWrite);
                }
            }

            Debug.Assert(zMax >= zMin);
            return new(lasFile.Header.GetNumberOfPointsByReturn(), numberOfPointsRemovedByReturn)
            {
                ZMax = zMax,
                ZMin = zMin
            };
        }

        /// <param name="reader">Stream to read points and any extended records from. Caller must ensure reader is positioned at the first point in the source .las file.</param>
        //public void CopyPointsAndExtendedVariableLengthRecords(LasReader reader, LasFile lasFile)
        //{
        //    if (this.BaseStream.Position != lasFile.Header.OffsetToPointData)
        //    {
        //        throw new InvalidOperationException(".las writer must be positioned at the start of point data (offset " + lasFile.Header.OffsetToPointData + " bytes) to write points. The writer is currently positioned at " + this.BaseStream.Position + " bytes.");
        //    }

        //    // points don't need to be unpacked so both uncompressed .las and compressed .laz points can be copied
        //    Span<byte> copyBuffer = stackalloc byte[512];
        //    long sourceLasFileSize = reader.BaseStream.Length;
        //    while (reader.BaseStream.Position < sourceLasFileSize)
        //    {
        //        int bytesToRead = (int)Int64.Min(sourceLasFileSize - reader.BaseStream.Position, copyBuffer.Length);
        //        reader.BaseStream.ReadExactly(copyBuffer[..bytesToRead]);

        //        this.BaseStream.Write(copyBuffer[..bytesToRead]);
        //    }
        //}

        public static LasWriter CreateForPointWrite(string lasPath)
        {
            FileStream stream = new(lasPath, FileMode.Create, FileAccess.Write, FileShare.Read, 512 * 1024);
            return new LasWriter(stream);
        }

        public static (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) GetPointCopyEditBandwidth()
        {
            // TODO: profile
            return (2.0F, 4.5F * 2.0F);
        }

        public void WriteExtendedVariableLengthRecords(LasFile lasFile)
        {
            // extended variable length records were added in LAS 1.4
            if (lasFile.Header is LasHeader14 lasHeader14)
            {
                UInt32 extendedVariableLengthRecords = lasHeader14.NumberOfExtendedVariableLengthRecords;
                if ((extendedVariableLengthRecords > 0) && (this.BaseStream.Position != (long)lasHeader14.StartOfFirstExtendedVariableLengthRecord))
                {
                    throw new InvalidOperationException(".las file stream is at position " + this.BaseStream.Position + " rather than at the file header's indicated extended variable length record offset (" + lasHeader14.StartOfFirstExtendedVariableLengthRecord + " bytes). Extended variable length records should begin immediately after the points.");
                }
                // if there are no extended variable length records then StartOfFirstExtendedVariableLengthRecord may be zero

                if (extendedVariableLengthRecords != lasFile.ExtendedVariableLengthRecords.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(lasFile), ".las file's header indicates " + extendedVariableLengthRecords + " variable length records but " + lasFile.VariableLengthRecords.Count + " records are present. This may be because the header's number of extended variable length records is set incorrectly or because the header uses a LAS version prior to 1.4.");
                }
            }
            else if (lasFile.ExtendedVariableLengthRecords.Count > 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), "LAS file version " + lasFile.Header.VersionMajor + "." + lasFile.Header.VersionMinor + " does not support extended variable length records (EVLRs) but the file has " + lasFile.ExtendedVariableLengthRecords.Count + " EVLRs.");
            }

            for (int evlrIndex = 0; evlrIndex < lasFile.ExtendedVariableLengthRecords.Count; ++evlrIndex)
            {
                ExtendedVariableLengthRecord evlr = lasFile.ExtendedVariableLengthRecords[evlrIndex];
                evlr.Write(this.BaseStream);
            }
        }

        private static void WriteFixedLength(string fixedLengthString, Span<byte> buffer, int length)
        {
            int bytesWritten = Encoding.UTF8.GetBytes(fixedLengthString, buffer);
            if (bytesWritten != length)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedLengthString), "Fixed length string '" + fixedLengthString + "' did not encode to the expected length of " + length + " bytes.");
            }
        }

        /// <remarks>
        /// If needed, seeks underlying stream to start of file.
        /// </remarks>
        public void WriteHeader(LasFile lasFile)
        {
            byte versionMinor = lasFile.Header.VersionMinor;
            if (versionMinor < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), "Legacy .las version " + lasFile.Header.VersionMinor + "." + lasFile.Header.VersionMinor + " is not supported.");
            }
            LasHeader12 lasHeader = (LasHeader12)lasFile.Header;

            Span<byte> headerBytes = stackalloc byte[lasHeader.HeaderSize];
            LasWriter.WriteFixedLength(lasHeader.FileSignature, headerBytes, 4);

            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[4..], lasHeader.FileSourceID);
            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[6..], (UInt16)lasHeader.GlobalEncoding);
            if (lasHeader.ProjectID.TryWriteBytes(headerBytes[8..]) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), "Failed to write project GUID " + lasHeader.ProjectID + " to header buffer.");
            }

            headerBytes[24] = lasHeader.VersionMajor;
            headerBytes[25] = lasHeader.VersionMinor;

            LasWriter.WriteNullTerminated(headerBytes[26..], lasHeader.SystemIdentifier, 32);
            LasWriter.WriteNullTerminated(headerBytes[58..], lasHeader.GeneratingSoftware, 32);

            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[90..], lasHeader.FileCreationDayOfYear);
            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[92..], lasHeader.FileCreationYear);
            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[94..], lasHeader.HeaderSize);

            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[96..], lasHeader.OffsetToPointData);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[100..], lasHeader.NumberOfVariableLengthRecords);
            headerBytes[104] = lasHeader.PointDataRecordFormat;
            BinaryPrimitives.WriteUInt16LittleEndian(headerBytes[105..], lasHeader.PointDataRecordLength);
            BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[107..], lasHeader.LegacyNumberOfPointRecords);
            for (int returnIndex = 0; returnIndex < lasHeader.LegacyNumberOfPointsByReturn.Length; ++returnIndex)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[(111 + 4 * returnIndex)..], lasHeader.LegacyNumberOfPointsByReturn[returnIndex]);
            }
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[131..], lasHeader.XScaleFactor);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[139..], lasHeader.YScaleFactor);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[147..], lasHeader.ZScaleFactor);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[155..], lasHeader.XOffset);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[163..], lasHeader.YOffset);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[171..], lasHeader.ZOffset);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[179..], lasHeader.MaxX);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[187..], lasHeader.MinX);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[195..], lasHeader.MaxY );
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[203..], lasHeader.MinY);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[211..], lasHeader.MaxZ);
            BinaryPrimitives.WriteDoubleLittleEndian(headerBytes[219..], lasHeader.MinZ);

            if (versionMinor > 2)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[227..], ((LasHeader13)lasHeader).StartOfWaveformDataPacketRecord);

                if (versionMinor > 3)
                {
                    LasHeader14 lasHeader14 = (LasHeader14)lasHeader;
                    BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[235..], lasHeader14.StartOfFirstExtendedVariableLengthRecord);
                    BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[243..], lasHeader14.NumberOfExtendedVariableLengthRecords);
                    BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[247..], lasHeader14.NumberOfPointRecords);
                    for (int returnIndex = 0; returnIndex < lasHeader14.NumberOfPointsByReturn.Length; ++returnIndex)
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[(255 + 8 * returnIndex)..], lasHeader14.NumberOfPointsByReturn[returnIndex]);
                    }
                }
            }

            if (this.BaseStream.Position != 0)
            {
                this.BaseStream.Seek(0, SeekOrigin.Begin);
            }
            this.BaseStream.Write(headerBytes);
            
            Debug.Assert(this.BaseStream.Position == lasFile.Header.HeaderSize);
        }

        public static void WriteNullTerminated(Span<byte> buffer, string fixedLengthString, int length)
        {
            int bytesWritten = Encoding.UTF8.GetBytes(fixedLengthString, buffer);
            if (bytesWritten > length)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedLengthString), "String '" + fixedLengthString + "' encoded to " + bytesWritten + ", which exceeds the allowed length of " + length + " bytes.");
            }
            for (int index = bytesWritten; index < length; ++index)
            {
                buffer[index] = 0;
            }
        }

        /// <param name="reader">Stream to read points from. Caller must ensure reader is positioned at the first point in the source .las file.</param>
        public LasWriteTransformedResult WriteTransformedAndRepairedPoints(LasReader reader, LasFile lasFile, CoordinateTransform scaledSourceTransform, UInt16? sourceID, bool repairClassification, bool repairReturnNumbers)
        {
            if (this.BaseStream.Position != lasFile.Header.OffsetToPointData)
            {
                throw new InvalidOperationException(".las writer must be positioned at the start of point data (offset " + lasFile.Header.OffsetToPointData + " bytes) to write points. The writer is currently positioned at " + this.BaseStream.Position + " bytes.");
            }

            LasHeader10 lasHeader = lasFile.Header;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int pointRecordLength = lasHeader.PointDataRecordLength; 
            int classificationOffset = pointFormat < 6 ? 15 : 16;

            bool hasRotationXY = scaledSourceTransform.HasRotationXY;
            bool hasTranslationXY = scaledSourceTransform.HasTranslationXY;
            bool hasTranslationZ = scaledSourceTransform.HasTranslationZ;
            double rotationXYinRadians = scaledSourceTransform.RotationXYinRadians;
            double sinRotationXY = Double.Sin(rotationXYinRadians);
            double cosRotationXY = Double.Cos(rotationXYinRadians);
            double scaledTranslationX = scaledSourceTransform.TranslationX;
            double scaledTranslationY = scaledSourceTransform.TranslationY;
            double scaledTranslationZ = scaledSourceTransform.TranslationZ;

            int maxX = Int32.MinValue;
            int maxY = Int32.MinValue;
            int minX = Int32.MaxValue;
            int minY = Int32.MaxValue;
            byte[] pointBuffer = new byte[LasReader.ReadBufferSizeInPoints * pointRecordLength];
            long returnNumbersRepaired = 0;
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadBufferSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadBufferSizeInPoints ? LasReader.ReadBufferSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * pointRecordLength;
                reader.BaseStream.ReadExactly(pointBuffer.AsSpan(0, bytesToRead));

                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += pointRecordLength)
                {
                    Span<byte> pointBytes = pointBuffer.AsSpan(batchOffset, pointRecordLength);

                    if (hasTranslationXY || hasRotationXY)
                    {
                        double xScaled = BinaryPrimitives.ReadInt32LittleEndian(pointBytes[0..4]) - scaledTranslationX;
                        double yScaled = BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..8]) - scaledTranslationY;

                        int xTransformed = (int)(xScaled * cosRotationXY - yScaled * sinRotationXY + 0.5);
                        int yTransformed = (int)(xScaled * sinRotationXY + yScaled * cosRotationXY + 0.5);

                        BinaryPrimitives.WriteInt32LittleEndian(pointBytes[0..4], xTransformed);
                        BinaryPrimitives.WriteInt32LittleEndian(pointBytes[4..8], yTransformed);

                        if (xTransformed > maxX)
                        {
                            maxX = xTransformed;
                        }
                        else if (xTransformed < minX)
                        {
                            minX = xTransformed;
                        }
                        if (yTransformed > maxY)
                        {
                            maxY = yTransformed;
                        }
                        else if (yTransformed < minY)
                        {
                            minY = yTransformed;
                        }
                    }
                    if (hasTranslationZ)
                    {
                        double zScaled = BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..12]) - scaledTranslationZ;
                        int zTransformed = (int)(zScaled + 0.5);
                        BinaryPrimitives.WriteInt32LittleEndian(pointBytes[8..12], zTransformed);
                    }
                    if (repairClassification) 
                    {
                        PointClassification classification = (PointClassification)pointBytes[classificationOffset];
                        // workaround bugs in Trion Model
                        if (classification == PointClassification.NeverClassified)
                        {
                            classification = PointClassification.Unclassified; // Trion Model v113, 115, 200, ...
                        }
                        else if (classification == PointClassification.Unclassified)
                        {
                            classification = PointClassification.Ground; // Trion Model v113, 115, 200, ...
                        }
                        else if (classification == PointClassification.HighVegetation)
                        {
                            classification = PointClassification.LowVegetation; // Trion Model v200...
                        }
                        pointBytes[classificationOffset] = (byte)classification;
                    }
                    if (repairReturnNumbers)
                    {
                        byte returnNumberAndNumberOfReturns = pointBytes[14];
                        if (pointFormat < 6)
                        {
                            int returnNumber = returnNumberAndNumberOfReturns & 0x07;
                            int numberOfReturns = returnNumberAndNumberOfReturns & 0x38;
                            int scanAndEdgeFlags = returnNumberAndNumberOfReturns & 0xc0;
                            if (returnNumber == 0)
                            {
                                returnNumber = 1;
                                ++returnNumbersRepaired;
                            }
                            if (numberOfReturns == 0)
                            {
                                numberOfReturns = 1 << 3;
                            }
                            pointBytes[14] = (byte)(scanAndEdgeFlags | returnNumber | numberOfReturns);
                        }
                        else
                        {
                            int returnNumber = returnNumberAndNumberOfReturns & 0x0f;
                            int numberOfReturns = returnNumberAndNumberOfReturns & 0xf0;
                            if (returnNumber == 0)
                            {
                                returnNumber = 1;
                                ++returnNumbersRepaired;
                            }
                            if (numberOfReturns == 0)
                            {
                                numberOfReturns = 1 << 4;
                            }
                            pointBytes[14] = (byte)(returnNumber | numberOfReturns);
                        }
                    }

                    // set source ID
                    // If needed, this can be changed to alter only source IDs which are zero.
                    if (sourceID.HasValue)
                    {
                        if (pointFormat < 6)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(pointBytes[18..20], sourceID.Value);
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(pointBytes[20..22], sourceID.Value);
                        }
                    }
                    
                    // other possible changes
                    // - intensity normalization?
                    // - set return number and number of returns if synthetic return numbers set in header (needed to bring GeoSLAM outputs into LAS R15 compliance)
                    // - convert from scan time in seconds to LAS 1.4 R15 compliant GPS time
                }

                this.BaseStream.Write(pointBuffer, 0, bytesToRead);
            }

            return new LasWriteTransformedResult()
            {
                MaxX = lasHeader.XOffset + lasHeader.XScaleFactor * maxX,
                MaxY = lasHeader.YOffset + lasHeader.YScaleFactor * maxY,
                MinX = lasHeader.XOffset + lasHeader.XScaleFactor * minX,
                MinY = lasHeader.YOffset + lasHeader.YScaleFactor * minY,
                ReturnNumbersRepaired = returnNumbersRepaired
            };
        }

        public void WriteVariableLengthRecordsAndUserData(LasFile lasFile)
        {
            if (lasFile.Header.NumberOfVariableLengthRecords != lasFile.VariableLengthRecords.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), ".las file's header indicates " + lasFile.Header.NumberOfVariableLengthRecords + " variable length records but " + lasFile.VariableLengthRecords.Count + (lasFile.VariableLengthRecords.Count > 1 ? " records are present." : " record is present."));
            }
            if (this.BaseStream.Position != lasFile.Header.HeaderSize)
            {
                throw new InvalidOperationException(".las file stream is at position " + this.BaseStream.Position + " rather than at the file header's indicated size (" + lasFile.Header.HeaderSize + " bytes). Variable length records should begin immediately after the header.");
            }

            for (int vlrIndex = 0; vlrIndex < lasFile.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecord vlr = lasFile.VariableLengthRecords[vlrIndex];
                vlr.Write(this.BaseStream);
            }

            if (lasFile.BytesAfterVariableLengthRecords.Length > 0)
            {
                this.BaseStream.Write(lasFile.BytesAfterVariableLengthRecords);
            }

            if (this.BaseStream.Position != lasFile.Header.OffsetToPointData)
            {
                throw new InvalidOperationException(".las file stream is at position " + this.BaseStream.Position + " rather than at the file header's indicated point offset (" + lasFile.Header.OffsetToPointData + " bytes). A valid variable length record write needs to end at the beginning of the point data.");
            }
        }
    }
}
