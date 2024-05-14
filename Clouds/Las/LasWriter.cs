using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Mars.Clouds.Las
{
    public class LasWriter : LasStream
    {
        public const float RegisterSpeedInGBs = 2.0F;

        public LasWriter(Stream stream)
            : base(stream)
        {
        }

        public static LasWriter CreateForPointWrite(string lasPath)
        {
            FileStream stream = new(lasPath, FileMode.Create, FileAccess.Write, FileShare.Read, 512 * 1024);
            return new LasWriter(stream);
        }

        private static void WriteFixedLength(string fixedLengthString, Span<byte> buffer, int length)
        {
            int bytesWritten = Encoding.UTF8.GetBytes(fixedLengthString, buffer);
            if (bytesWritten != length)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedLengthString), "Fixed length string '" + fixedLengthString + "' did not encode to the expected length of " + length + " bytes.");
            }
        }

        public void WriteHeader(LasFile lasFile)
        {
            if (this.BaseStream.Position != 0)
            {
                throw new InvalidOperationException(".las writer must be positioned at the beginning of a .las file stream to write a header. Writer is currently at position " + this.BaseStream.Position + ".");
            }

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
                    BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[8..], lasHeader14.StartOfFirstExtendedVariableLengthRecord);
                    BinaryPrimitives.WriteUInt32LittleEndian(headerBytes[16..], lasHeader14.NumberOfExtendedVariableLengthRecords);
                    BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[20..], lasHeader14.NumberOfPointRecords);
                    for (int returnIndex = 0; returnIndex < lasHeader14.NumberOfPointsByReturn.Length; ++returnIndex)
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(headerBytes[(28 + 8 * returnIndex)..], lasHeader14.NumberOfPointsByReturn[returnIndex]);
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
        public void WritePointsWithSourceID(LasReader reader, LasFile lasFile, UInt16 sourceID)
        {
            if (this.BaseStream.Position != lasFile.Header.OffsetToPointData)
            {
                throw new InvalidOperationException(".las writer must be positioned at the start of point data (offset " + lasFile.Header.OffsetToPointData + " bytes) to write points. The writer is currently positioned at " + this.BaseStream.Position + " bytes.");
            }

            LasHeader10 lasHeader = lasFile.Header;
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int sourceIDoffset = pointFormat < 6 ? 18 : 20;

            byte[] pointBuffer = new byte[LasReader.ReadExactSizeInPoints * lasHeader.PointDataRecordLength];
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadExactSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadExactSizeInPoints ? LasReader.ReadExactSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * lasHeader.PointDataRecordLength;
                reader.BaseStream.ReadExactly(pointBuffer.AsSpan(0, bytesToRead));

                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += lasHeader.PointDataRecordLength)
                {
                    Span<byte> pointBytes = pointBuffer.AsSpan(batchOffset);

                    // set source ID
                    // If needed, this can be changed to alter only source IDs which are zero.
                    BinaryPrimitives.WriteUInt16LittleEndian(pointBytes[sourceIDoffset..], sourceID);
                    // other possible changes
                    // - intensity normalization?
                    // - set return number and number of returns if synthetic return numbers set in header (needed to bring GeoSLAM outputs into LAS R15 compliance)
                    // - convert from scan time in seconds to LAS 1.4 R15 compliant GPS time
                }

                this.BaseStream.Write(pointBuffer, 0, bytesToRead);
            }
        }

        public void WriteVariableLengthRecords(LasFile lasFile)
        {
            if (this.BaseStream.Position != lasFile.Header.HeaderSize)
            {
                throw new InvalidOperationException(".las file stream is at position " + this.BaseStream.Position + " rather than at the file header's indicated size (" + lasFile.Header.HeaderSize + " bytes). Variable length records should begin immediately after the header.");
            }

            for (int vlrIndex = 0; vlrIndex < lasFile.VariableLengthRecords.Count; ++vlrIndex)
            {
                VariableLengthRecord vlr = lasFile.VariableLengthRecords[vlrIndex];
                vlr.Write(this.BaseStream);
            }

            if (this.BaseStream.Position != lasFile.Header.OffsetToPointData)
            {
                throw new InvalidOperationException(".las file stream is at position " + this.BaseStream.Position + " rather than at the file header's indicated point offset (" + lasFile.Header.OffsetToPointData + " bytes). A valid variable length record write needs to end at the beginning of the point data.");
            }
        }
    }
}
