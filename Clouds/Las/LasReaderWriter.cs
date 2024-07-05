using System.Buffers.Binary;
using System;
using System.IO;
using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;

namespace Mars.Clouds.Las
{
    public class LasReaderWriter(FileStream stream) : LasReader(stream)
    {
        public const float FindUnclassifiedNoisePointsSpeedInGBs = 2.0F; // TODO: get benchmark values

        public static new LasReaderWriter CreateForPointRead(string lasPath, long fileSizeInBytes)
        {
            return new LasReaderWriter(LasReader.CreatePointStream(lasPath, fileSizeInBytes, FileAccess.ReadWrite, useAsync: true));
        }

        public int TryFindUnclassifiedNoise(LasTile lasTile, RasterBand<float> dtmTile, float highNoiseThreshold, float lowNoiseThreshold)
        {
            SpatialReference lasTileCrs = lasTile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(lasTileCrs, dtmTile.Crs) == false)
            {
                throw new NotSupportedException("The point clouds and DTM are currently required to be in the same CRS. The point cloud CRS is '" + lasTileCrs.GetName() + "' while the DTM CRS is " + dtmTile.Crs.GetName() + ".");
            }

            LasHeader10 lasHeader = lasTile.Header;
            LasReader.ThrowOnUnsupportedPointFormat(lasHeader);
            this.MoveToPoints(lasTile);

            // read points
            UInt64 numberOfPoints = lasHeader.GetNumberOfPoints();
            byte pointFormat = lasHeader.PointDataRecordFormat;
            int classificationOffset = pointFormat < 6 ? 15 : 16;
            double xOffset = lasHeader.XOffset;
            double xScale = lasHeader.XScaleFactor;
            double yOffset = lasHeader.YOffset;
            double yScale = lasHeader.YScaleFactor;
            float zOffset = (float)lasHeader.ZOffset;
            float zScale = (float)lasHeader.ZScaleFactor;

            Span<byte> pointReadBuffer = stackalloc byte[LasReader.ReadExactSizeInPoints * lasHeader.PointDataRecordLength]; // for now, assume small enough to stackalloc
            int pointsReclassified = 0;
            for (UInt64 lasPointIndex = 0; lasPointIndex < numberOfPoints; lasPointIndex += LasReader.ReadExactSizeInPoints)
            {
                UInt64 pointsRemainingToRead = numberOfPoints - lasPointIndex;
                int pointsToRead = pointsRemainingToRead >= LasReader.ReadExactSizeInPoints ? LasReader.ReadExactSizeInPoints : (int)pointsRemainingToRead;
                int bytesToRead = pointsToRead * lasHeader.PointDataRecordLength;
                this.BaseStream.ReadExactly(pointReadBuffer[..bytesToRead]);

                for (int batchOffset = 0; batchOffset < bytesToRead; batchOffset += lasHeader.PointDataRecordLength)
                {
                    ReadOnlySpan<byte> pointBytes = pointReadBuffer[batchOffset..];
                    bool notNoiseOrWithheld = LasReader.ReadClassification(pointBytes, pointFormat, out PointClassification classification);
                    if (notNoiseOrWithheld == false)
                    {
                        continue;
                    }

                    double x = xOffset + xScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes);
                    double y = yOffset + yScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[4..]);
                    (int xIndex, int yIndex) = dtmTile.ToInteriorGridIndices(x, y);
                    float dtmZ = dtmTile[xIndex, yIndex];

                    float z = zOffset + zScale * BinaryPrimitives.ReadInt32LittleEndian(pointBytes[8..]);
                    PointClassification newClassification;
                    if (z > dtmZ + highNoiseThreshold) 
                    {
                        newClassification = PointClassification.HighNoise;
                    }
                    else if (z < dtmZ + lowNoiseThreshold)
                    {
                        newClassification = PointClassification.LowNoise;
                    }
                    else
                    {
                        continue;
                    }

                    byte classificationByte = (byte)newClassification;
                    if (pointFormat < 6)
                    {
                        byte classificationAndFlags = pointBytes[15];
                        classificationByte |= (byte)(classificationAndFlags & 0xe0);
                    }
                    long startOfNextBatchPosition = this.BaseStream.Position;
                    this.BaseStream.Seek(-bytesToRead + batchOffset + classificationOffset, SeekOrigin.Current);
                    this.BaseStream.WriteByte(classificationByte);
                    this.BaseStream.Seek(startOfNextBatchPosition, SeekOrigin.Begin);
                    ++pointsReclassified;
                }
            }

            return pointsReclassified;
        }
    }
}
