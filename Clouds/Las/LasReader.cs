using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Mars.Clouds.GdalExtensions;

namespace Mars.Clouds.Las
{
    public class LasReader : IDisposable
    {
        private bool isDisposed;

        public BinaryReader BaseStream { get; private init; }

        public LasReader(Stream stream)
        {
            this.isDisposed = false;
            this.BaseStream = new(stream);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
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

                isDisposed = true;
            }
        }

        public LasHeader10 ReadHeader()
        {
            if (this.BaseStream.BaseStream.Position != 0)
            {
                this.BaseStream.BaseStream.Seek(0, SeekOrigin.Begin);
            }

            UInt32 signatureAsUInt32 = this.BaseStream.ReadUInt32();
            if (signatureAsUInt32 != 0x4653414c)
            {
                Span<byte> signatureBytes = stackalloc byte[4];
                BinaryPrimitives.TryWriteUInt32LittleEndian(signatureBytes, signatureAsUInt32);
                string signature = Encoding.UTF8.GetString(signatureBytes);
                throw new IOException("File begins with '" + signature + "'. .las files are expected to begin with \"LASF\".");;
            }

            UInt16 fileSourceID = this.BaseStream.ReadUInt16();
            GlobalEncoding globalEncoding = (GlobalEncoding)this.BaseStream.ReadUInt16();

            Span<byte> readBuffer16 = stackalloc byte[16];
            this.BaseStream.Read(readBuffer16);
            Guid projectID = new(readBuffer16);

            byte versionMajor = this.BaseStream.ReadByte();
            byte versionMinor = this.BaseStream.ReadByte();
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

            if (this.BaseStream.BaseStream.Length < lasHeader.HeaderSize)
            {
                throw new IOException("File size of " + this.BaseStream.BaseStream.Length + " bytes is less than the LAS " + versionMajor + "." + versionMinor + " header size of " + lasHeader.HeaderSize + " bytes.");
            }

            Span<byte> readBuffer32 = stackalloc byte[32];
            this.BaseStream.Read(readBuffer32);
            lasHeader.SystemIdentifier = Encoding.UTF8.GetString(readBuffer32).Trim('\0');
            this.BaseStream.Read(readBuffer32);
            lasHeader.GeneratingSoftware = Encoding.UTF8.GetString(readBuffer32).Trim('\0');

            lasHeader.FileCreationDayOfYear = this.BaseStream.ReadUInt16();
            lasHeader.FileCreationYear = this.BaseStream.ReadUInt16();
            UInt16 headerSizeInBytes = this.BaseStream.ReadUInt16();
            if (headerSizeInBytes != lasHeader.HeaderSize)
            {
                throw new IOException("Header of " + this.BaseStream.BaseStream.Length + " bytes differs from the LAS " + versionMajor + "." + versionMinor + " header size of " + lasHeader.HeaderSize + " bytes.");
            }

            lasHeader.OffsetToPointData = this.BaseStream.ReadUInt32();
            lasHeader.NumberOfVariableLengthRecords = this.BaseStream.ReadUInt32();
            lasHeader.PointDataRecordFormat = this.BaseStream.ReadByte();
            lasHeader.PointDataRecordLength = this.BaseStream.ReadUInt16();
            lasHeader.LegacyNumberOfPointRecords = this.BaseStream.ReadUInt32();
            for (int returnIndex = 0; returnIndex < lasHeader.LegacyNumberOfPointsByReturn.Length; ++returnIndex)
            {
                lasHeader.LegacyNumberOfPointsByReturn[returnIndex] = this.BaseStream.ReadUInt32();
            }
            lasHeader.XScaleFactor = this.BaseStream.ReadDouble();
            lasHeader.YScaleFactor = this.BaseStream.ReadDouble();
            lasHeader.ZScaleFactor = this.BaseStream.ReadDouble();
            lasHeader.XOffset = this.BaseStream.ReadDouble();
            lasHeader.YOffset = this.BaseStream.ReadDouble();
            lasHeader.ZOffset = this.BaseStream.ReadDouble();
            lasHeader.MaxX = this.BaseStream.ReadDouble();
            lasHeader.MinX = this.BaseStream.ReadDouble();
            lasHeader.MaxY = this.BaseStream.ReadDouble();
            lasHeader.MinY = this.BaseStream.ReadDouble();
            lasHeader.MaxZ = this.BaseStream.ReadDouble();
            lasHeader.MinZ = this.BaseStream.ReadDouble();

            if (versionMinor > 2)
            {
                ((LasHeader13)lasHeader).StartOfWaveformDataPacketRecord = this.BaseStream.ReadUInt64();
            }
            if (versionMinor > 3) 
            {
                LasHeader14 lasHeader14 = (LasHeader14)lasHeader;
                lasHeader14.StartOfFirstExtendedVariableLengthRecord = this.BaseStream.ReadUInt64();
                lasHeader14.NumberOfExtendedVariableLengthRecords = this.BaseStream.ReadUInt32();
                lasHeader14.NumberOfPointRecords = this.BaseStream.ReadUInt64();
                for (int returnIndex = 0; returnIndex < lasHeader14.NumberOfPointsByReturn.Length; ++returnIndex)
                {
                    lasHeader14.NumberOfPointsByReturn[returnIndex] = this.BaseStream.ReadUInt64();
                }
            }

            lasHeader.Validate();
            return lasHeader;
        }

        public void ReadPointsToGridZirn(LasTile tile, Grid<PointListZirnc> abaGrid)
        {
            LasHeader10 lasHeader = tile.Header;
            if (this.BaseStream.BaseStream.Position != lasHeader.OffsetToPointData)
            {
                this.BaseStream.BaseStream.Seek(lasHeader.OffsetToPointData, SeekOrigin.Begin);
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
            byte pointFormat = lasHeader.PointDataRecordFormat;
            long unusedBytesPerPartiallyReadPoint = lasHeader.PointDataRecordLength - 8; // x+y = 2*4
            long unusedBytesPerFullyReadPoint = lasHeader.PointDataRecordLength - 16; // x+y+z + intensity + return + classification = 3*4 + 2 + 1 + 1
            if (pointFormat > 10)
            {
                throw new NotSupportedException("Unhandled point data record format " + pointFormat + ".");
            }
            else if (pointFormat > 5)
            {
                --unusedBytesPerFullyReadPoint; // x+y+z + intensity + return + classification flags + classification = 3*4 + 2 + 1 + 1 + 1
            }

            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            double xOffset = lasHeader.XOffset;
            double xScale = lasHeader.XScaleFactor;
            double yOffset = lasHeader.YOffset;
            double yScale = lasHeader.YScaleFactor;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;
            int returnNumberMask = lasHeader.GetReturnNumberMask();
            for (UInt64 pointIndex = 0; pointIndex < numberOfPoints; ++pointIndex)
            {
                double x = xOffset + xScale * this.BaseStream.ReadInt32();
                double y = yOffset + yScale * this.BaseStream.ReadInt32();
                (int xIndex, int yIndex) = abaGrid.Transform.GetCellIndex(x, y);
                if ((xIndex < 0) || (yIndex < 0) || (xIndex >= abaGrid.XSize) || (yIndex >= abaGrid.YSize))
                {
                    // point lies outside of the ABA grid and is therefore not of interest
                    // In cases of partial overlap, this reader could be extended to take advantage of spatially indexed LAS files.
                    this.BaseStream.BaseStream.Seek(unusedBytesPerPartiallyReadPoint, SeekOrigin.Current);
                    continue;
                }
                PointListZirnc? abaCell = abaGrid[xIndex, yIndex];
                if (abaCell == null)
                {
                    // point lies within ABA grid but is not within a cell of interest
                    this.BaseStream.BaseStream.Seek(unusedBytesPerPartiallyReadPoint, SeekOrigin.Current);
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

                float z = zOffset + zScale * this.BaseStream.ReadInt32();
                abaCell.Z.Add(z);

                UInt16 intensity = this.BaseStream.ReadUInt16();
                abaCell.Intensity.Add(intensity);

                byte returnNumber = (byte)(this.BaseStream.ReadByte() & returnNumberMask);
                abaCell.ReturnNumber.Add(returnNumber);

                PointClassification classification;
                if (pointFormat < 6)
                {
                    classification = (PointClassification)(this.BaseStream.ReadByte() & 0x1f);
                }
                else
                {
                    this.BaseStream.BaseStream.Seek(1, SeekOrigin.Current); // skip classification flags
                    classification = (PointClassification)this.BaseStream.ReadByte();
                }
                abaCell.Classification.Add(classification);

                this.BaseStream.BaseStream.Seek(unusedBytesPerFullyReadPoint, SeekOrigin.Current);
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
                if (this.BaseStream.BaseStream.Position != lasHeader.HeaderSize)
                {
                    this.BaseStream.BaseStream.Seek(lasHeader.HeaderSize, SeekOrigin.Begin);
                }

                lasFile.VariableLengthRecords.Capacity += (int)lasHeader.NumberOfVariableLengthRecords;
                this.ReadVariableLengthRecords(lasFile, readExtendedRecords: false);
            }

            if (lasHeader.VersionMinor >= 4)
            {
                LasHeader14 header14 = (LasHeader14)lasHeader;
                if (header14.NumberOfExtendedVariableLengthRecords > 0)
                {
                    if (this.BaseStream.BaseStream.Position != (long)header14.StartOfFirstExtendedVariableLengthRecord)
                    {
                        this.BaseStream.BaseStream.Seek((long)header14.StartOfFirstExtendedVariableLengthRecord, SeekOrigin.Begin);
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

            Span<byte> readBuffer16 = stackalloc byte[16];
            Span<byte> readBuffer32 = stackalloc byte[32];
            for (int recordIndex = 0; recordIndex < lasFile.Header.NumberOfVariableLengthRecords; ++recordIndex)
            {
                UInt16 reserved = this.BaseStream.ReadUInt16();
                this.BaseStream.Read(readBuffer16);
                string userID = Encoding.UTF8.GetString(readBuffer16).Trim('\0');
                UInt16 recordID = this.BaseStream.ReadUInt16();
                UInt64 recordLengthAfterHeader = readExtendedRecords ? this.BaseStream.ReadUInt64() : this.BaseStream.ReadUInt16();
                this.BaseStream.Read(readBuffer32);
                string description = Encoding.UTF8.GetString(readBuffer32).Trim('\0');

                // create specific object if record has a well known type
                long endOfRecordPosition = this.BaseStream.BaseStream.Position + (long)recordLengthAfterHeader;
                VariableLengthRecordBase? vlr = null;
                if (String.Equals(userID, LasFile.LasfProjection, StringComparison.Ordinal))
                {
                    UInt16 recordLengthAfterHeader16 = (UInt16)recordLengthAfterHeader;
                    if (recordID == 2111)
                    {
                        throw new NotImplementedException("OgcMathTransformWktRecord");
                    }
                    else if (recordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID)
                    {
                        OgcCoordinateSystemWktRecord wkt = new(new(Encoding.UTF8.GetString(this.BaseStream.ReadBytes(recordLengthAfterHeader16))))
                        {
                            Reserved = reserved,
                            RecordLengthAfterHeader = recordLengthAfterHeader16,
                            Description = description
                        };
                        vlr = wkt;
                    }
                    else if (recordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID)
                    {
                        GeoKeyDirectoryTagRecord crs = new()
                        {
                            Reserved = reserved,
                            RecordLengthAfterHeader = recordLengthAfterHeader16,
                            Description = description,
                            KeyDirectoryVersion = this.BaseStream.ReadUInt16(),
                            KeyRevision = this.BaseStream.ReadUInt16(),
                            MinorRevision = this.BaseStream.ReadUInt16(),
                            NumberOfKeys = this.BaseStream.ReadUInt16()
                        };
                        for (int keyEntryIndex = 0; keyEntryIndex < crs.NumberOfKeys; ++keyEntryIndex)
                        {
                            crs.KeyEntries.Add(new()
                            {
                                KeyID = this.BaseStream.ReadUInt16(),
                                TiffTagLocation = this.BaseStream.ReadUInt16(),
                                Count = this.BaseStream.ReadUInt16(),
                                ValueOrOffset = this.BaseStream.ReadUInt16()
                            });
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
                }
                else if (String.Equals(userID, LasFile.LasfSpec, StringComparison.Ordinal))
                {
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
                }

                // otherwise, create a general container for the record which the caller can parse
                if (vlr == null)
                {
                    if (readExtendedRecords)
                    {
                        vlr = new ExtendedVariableLengthRecord()
                        {
                            Reserved = reserved,
                            UserID = userID,
                            RecordID = recordID,
                            RecordLengthAfterHeader = recordLengthAfterHeader,
                            Description = description,
                            Data = BaseStream.ReadBytes((int)recordLengthAfterHeader)
                        };
                    }
                    else
                    {
                        UInt16 recordLengthAfterHeader16 = (UInt16)recordLengthAfterHeader;
                        vlr = new VariableLengthRecord()
                        {
                            Reserved = reserved,
                            UserID = userID,
                            RecordID = recordID,
                            RecordLengthAfterHeader = recordLengthAfterHeader16,
                            Description = description,
                            Data = BaseStream.ReadBytes(recordLengthAfterHeader16)
                        };
                    }
                }

                lasFile.VariableLengthRecords.Add(vlr);

                // skip any unused bytes at end of record
                if (this.BaseStream.BaseStream.Position != endOfRecordPosition)
                {
                    this.BaseStream.BaseStream.Seek(endOfRecordPosition, SeekOrigin.Begin);
                }
            }
        }
    }
}
