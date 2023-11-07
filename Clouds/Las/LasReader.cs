using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Laz;

namespace Mars.Clouds.Las
{
    public class LasReader : IDisposable
    {
        private bool isDisposed;

        public Stream BaseStream { get; private init; }

        public LasReader(Stream stream)
        {
            this.isDisposed = false;
            this.BaseStream = stream;
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    this.BaseStream.Dispose();
                }

                this.isDisposed = true;
            }
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

            lasHeader.Validate();
            return lasHeader;
        }

        public void ReadPointsToGridZirn(LasTile tile, Grid<PointListZirnc> abaGrid)
        {
            LasHeader10 lasHeader = tile.Header;
            if (tile.IsPointFormatCompressed())
            {
                throw new ArgumentOutOfRangeException(nameof(tile), ".laz files are not currently supported.");
            }
            if (this.BaseStream.Position != lasHeader.OffsetToPointData)
            {
                this.BaseStream.Seek(lasHeader.OffsetToPointData, SeekOrigin.Begin);
            }

            // cell capacity to a reasonable fraction of the tile's average density
            // Effort here is just to mitigate the amount of time spent in initial doubling of each ABA cell's point arrays.
            double cellArea = abaGrid.Transform.GetCellArea();
            double tileArea = tile.GridExtent.GetArea();
            UInt64 tilePoints = lasHeader.GetNumberOfPoints();
            double nominalMeanPointsPerCell = tilePoints * cellArea / tileArea;
            int cellInitialPointCapacity = Int32.Min((int)(0.35 * nominalMeanPointsPerCell), (int)tilePoints); // edge case: guard against overallocation when tile is smaller than cell
            (int abaXindexMin, int abaXindexMaxInclusive, int abaYindexMin, int abaYindexMaxInclusive) = abaGrid.GetIntersectingCellIndices(tile.GridExtent);
            for (int abaYindex = abaYindexMin; abaYindex <= abaYindexMaxInclusive; ++abaYindex)
            {
                for (int abaXindex = abaXindexMin; abaXindex <= abaXindexMaxInclusive; ++abaXindex)
                {
                    PointListZirnc? abaCell = abaGrid[abaXindex, abaYindex];
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
            byte pointFormat = lasHeader.PointDataRecordFormat;
            if (pointFormat > 10)
            {
                throw new NotSupportedException("Unhandled point data record format " + pointFormat + ".");
            }

            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
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

                PointClassification classification;
                if (pointFormat < 6)
                {
                    classification = (PointClassification)(pointBytes[15] & 0x1f);
                }
                else
                {
                    classification = (PointClassification)pointBytes[16];
                }
                if ((classification == PointClassification.HighNoise) || (classification == PointClassification.LowNoise))
                {
                    continue; // exclude noise points from consideration
                }

                double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                (int xIndex, int yIndex) = abaGrid.Transform.GetCellIndices(x, y);
                if ((xIndex < 0) || (yIndex < 0) || (xIndex >= abaGrid.XSize) || (yIndex >= abaGrid.YSize))
                {
                    // point lies outside of the ABA grid and is therefore not of interest
                    // In cases of partial overlap, this reader could be extended to take advantage of spatially indexed LAS files.
                    continue;
                }
                PointListZirnc? abaCell = abaGrid[xIndex, yIndex];
                if (abaCell == null)
                {
                    // point lies within ABA grid but is not within a cell of interest
                    continue;
                }

                if (x < abaCell.XMin)
                {
                    abaCell.XMin = x;
                }
                if (x > abaCell.XMax)
                {
                    abaCell.XMax = x;
                }
                if (y < abaCell.YMin)
                {
                    abaCell.YMin = y;
                }
                if (y > abaCell.YMax)
                {
                    abaCell.YMax = y;
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
                    PointListZirnc? abaCell = abaGrid[abaXindex, abaYindex];
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

        public void ReadVariableLengthRecords(LasFile lasFile)
        {
            LasHeader10 lasHeader = lasFile.Header;
            if (lasHeader.NumberOfVariableLengthRecords > 0)
            {
                if (this.BaseStream.Position != lasHeader.HeaderSize)
                {
                    this.BaseStream.Seek(lasHeader.HeaderSize, SeekOrigin.Begin);
                }

                lasFile.VariableLengthRecords.Capacity += (int)lasHeader.NumberOfVariableLengthRecords;
                this.ReadVariableLengthRecords(lasFile, readExtendedRecords: false);
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

                    lasFile.VariableLengthRecords.Capacity += (int)header14.NumberOfExtendedVariableLengthRecords;
                    this.ReadVariableLengthRecords(lasFile, readExtendedRecords: true);
                }
            }
        }

        private void ReadVariableLengthRecords(LasFile lasFile, bool readExtendedRecords)
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException(nameof(LasReader));
            }

            Span<byte> vlrBytes = stackalloc byte[readExtendedRecords ? 60 : 54];
            for (int recordIndex = 0; recordIndex < lasFile.Header.NumberOfVariableLengthRecords; ++recordIndex)
            {
                this.BaseStream.ReadExactly(vlrBytes);
                UInt16 reserved = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes);
                string userID = Encoding.UTF8.GetString(vlrBytes.Slice(2, 16)).Trim('\0');
                UInt16 recordID = BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[18..]);
                UInt64 recordLengthAfterHeader = readExtendedRecords ? BinaryPrimitives.ReadUInt64LittleEndian(vlrBytes[20..]) : BinaryPrimitives.ReadUInt16LittleEndian(vlrBytes[20..]);
                string description = Encoding.UTF8.GetString(vlrBytes.Slice(readExtendedRecords ? 28 : 22, 32)).Trim('\0');

                // create specific object if record has a well known type
                long endOfRecordPosition = this.BaseStream.Position + (long)recordLengthAfterHeader;
                UInt16 recordLengthAfterHeader16 = (UInt16)recordLengthAfterHeader;
                VariableLengthRecordBase? vlr = null;
                switch (userID)
                {
                    case LasFile.LasfProjection:
                        if (recordID == 2111)
                        {
                            throw new NotImplementedException("OgcMathTransformWktRecord");
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
                        else if (recordID == 34736)
                        {
                            throw new NotImplementedException("GeoDoubleParamsTagRecord");
                        }
                        else if (recordID == 34737)
                        {
                            throw new NotImplementedException("GeoAsciiParamsTagRecord");
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
                        else if (recordID == 4)
                        {
                            throw new NotImplementedException("ExtraBytesRecord");
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
                    if (readExtendedRecords)
                    {
                        vlr = new ExtendedVariableLengthRecord(reserved, userID, recordID, recordLengthAfterHeader, description, data);
                    }
                    else
                    {
                        vlr = new VariableLengthRecord(reserved, userID, recordID, recordLengthAfterHeader16, description, data);
                    }
                }

                lasFile.VariableLengthRecords.Add(vlr);

                // skip any unused bytes at end of record
                if (this.BaseStream.Position != endOfRecordPosition)
                {
                    this.BaseStream.Seek(endOfRecordPosition, SeekOrigin.Begin);
                }
            }
        }
    }
}
