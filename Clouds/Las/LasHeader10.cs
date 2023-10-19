﻿using System;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Header of a version 1.0, 1.1, or 1.2 .las file.
    /// </summary>
    public class LasHeader10
    {
        public const int HeaderSizeInBytes = 227;

        /// <summary>
        /// LASF.
        /// </summary>
        /// <remarks>
        /// Required.
        /// </remarks>
        public string FileSignature { get; private init; }

        /// <summary>
        /// Project ID formed from GUID data 1, 2, 3, and 4.
        /// </summary>
        /// <remarks>
        /// Optional. Referred to as just GUID data 1, 2, 3, and 4 in LAS 1.0.
        /// </remarks>
        public Guid ProjectID { get; set; }

        /// <summary>
        /// Major version of .las or .laz file.
        /// </summary>
        public byte VersionMajor { get; private init; }

        /// <summary>
        /// Minor version of .las or .laz file.
        /// </summary>
        public byte VersionMinor { get; protected init; }

        /// <summary>
        /// UTF8 string describing the hardware or processing operations which originated the .las or .laz file.
        /// </summary>
        public string SystemIdentifier { get; set; }

        /// <summary>
        /// ASCII string describing the software which generated the .las or .laz file.
        /// </summary>
        public string GeneratingSoftware { get; set; }

        /// <summary>
        /// Julian day of year in which the .las or .laz file was created in the UTC+0 time zone (GMT).
        /// </summary>
        public UInt16 FileCreationDayOfYear { get; set; }

        /// <summary>
        /// Year in which a .las or .laz  file was created. Commonly CE but calendar is not specified.
        /// </summary>
        public UInt16 FileCreationYear { get; set; }

        /// <summary>
        /// Serialized size of this header in bytes.
        /// </summary>
        public UInt16 HeaderSize { get; protected init; }

        /// <summary>
        /// Location of the start of point data in this .las or .laz file relative to the start of the file, bytes.
        /// </summary>
        public UInt32 OffsetToPointData { get; set; }

        /// <summary>
        /// Number of variable length records between the .las or .laz file's header and point data.
        /// </summary>
        public UInt32 NumberOfVariableLengthRecords { get; set; }

        /// <summary>
        /// Type of points contained in .las or .laz file. See LAS 1.4 R15 for details.
        /// </summary>
        public byte PointDataRecordFormat { get; set; }

        /// <summary>
        /// Size of each point record, bytes.
        /// </summary>
        public UInt16 PointDataRecordLength { get; set; }

        /// <summary>
        /// Number of points contained in .las or .laz file for point formats 0-5. Formats 6+ use <see cref="LasHeader14.NumberOfPointRecords"/>
        /// and <see cref="LegacyNumberOfPointRecords"/> should be zero. See also <see cref="GetNumberOfPoints"/>.
        /// </summary>
        public UInt32 LegacyNumberOfPointRecords { get; set; }

        /// <summary>
        /// Number of points by return for point formats 0-5. Formats 6+ use <see cref="LasHeader14.NumberOfPointsByReturn"/>.
        /// </summary>
        public UInt32[] LegacyNumberOfPointsByReturn { get; set; }

        /// <summary>
        /// Scale factor for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double XScaleFactor { get; set; }

        /// <summary>
        /// Scale factor for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double YScaleFactor { get; set; }

        /// <summary>
        /// Scale factor for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double ZScaleFactor { get; set; }

        /// <summary>
        /// Offset for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double XOffset { get; set; }

        /// <summary>
        /// Offset for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double YOffset { get; set; }

        /// <summary>
        /// Offset for transforming points from .las or .laz file local coordinates to coordinate system positions.
        /// </summary>
        public double ZOffset { get; set; }

        /// <summary>
        /// Maximum x value of any point in the file's coordinate system.
        /// </summary>
        public double MaxX { get; set; }

        /// <summary>
        /// Minimum x value of any point in the file's coordinate system.
        /// </summary>
        public double MinX { get; set; }

        /// <summary>
        /// Maximum x value of any point in the file's coordinate system.
        /// </summary>
        public double MaxY { get; set; }

        /// <summary>
        /// Minimum y value of any point in the file's coordinate system.
        /// </summary>
        public double MinY { get; set; }

        /// <summary>
        /// Maximum x value of any point in the file's coordinate system.
        /// </summary>
        public double MaxZ { get; set; }

        /// <summary>
        /// Minimum z value of any point in the file's coordinate system.
        /// </summary>
        public double MinZ { get; set; }

        public LasHeader10()
        {
            this.FileSignature = LasFile.Signature;
            this.SystemIdentifier = String.Empty;
            this.GeneratingSoftware = String.Empty;
            this.VersionMajor = 1;
            this.VersionMinor = 0;
            this.HeaderSize = LasHeader10.HeaderSizeInBytes;
            this.LegacyNumberOfPointsByReturn = new UInt32[5];
        }

        public (double xCentroid, double yCentroid) GetCentroidXY()
        {
            double xCentroid = 0.5 * (this.MinX + this.MaxX);
            double yCentroid = 0.5 * (this.MinY + this.MaxY);
            return (xCentroid, yCentroid);
        }

        public double GetArea()
        {
            return (this.MaxX - this.MinX) * (this.MaxY - this.MinY);
        }

        public virtual UInt64 GetNumberOfPoints()
        {
            return this.LegacyNumberOfPointRecords;
        }

        public byte GetReturnNumberMask()
        {
            if (this.PointDataRecordFormat <= 5)
            {
                return 0x07; // returns 0-8 for point types 0-5
            }
            if (this.PointDataRecordFormat <= 10)
            {
                return 0x0f; // returns 0-15 for point types 6-10
            }

            throw new NotSupportedException("Unhandled point data record format " + this.PointDataRecordFormat + ".");
        }

        public virtual void Validate()
        {
            if (this.SystemIdentifier.Length > 32)
            {
                throw new InvalidDataException("SystemIdentifier");
            }
            if (this.GeneratingSoftware.Length > 32) 
            {
                throw new InvalidDataException("GeneratingSoftware");
            }

            // for now, assume CE
            if ((this.FileCreationDayOfYear == 0) || (this.FileCreationDayOfYear > 366))
            {
                // if needed, can also check for leap years
                throw new InvalidDataException("FileCreationDayOfYear");
            }
            if ((this.FileCreationYear < 2003) || (this.FileCreationYear > 3000))
            {
                throw new InvalidDataException("FileCreationYear");
            }

            if (this.OffsetToPointData < this.HeaderSize)
            {
                throw new InvalidDataException("Header's offset to point data of " + this.OffsetToPointData + " bytes is less than the LAS " + this.VersionMajor + "." + this.VersionMinor + " header size of " + this.HeaderSize + " bytes.");
            }
            // currently permissive: allows any point format with any LAS version
            // Can be made more restrictive if needed.
            if (this.PointDataRecordFormat > 10) 
            {
                throw new InvalidDataException("PointDataRecordFormat");
            }

            UInt16 minimumPointDataRecordLength = this.PointDataRecordFormat switch
            {
                0 => 20, // LAS 1.4 R15, Tables 7, 10-
                1 => 28,
                2 => 26,
                3 => 34,
                4 => 57,
                5 => 63,
                6 => 30,
                7 => 36,
                8 => 38,
                9 => 59,
                10 => 57,
                _ => throw new NotSupportedException("Unhandled point data record format " + this.PointDataRecordFormat + ".")
            };
            if (this.PointDataRecordLength < minimumPointDataRecordLength) 
            {
                throw new InvalidDataException("PointDataRecordLength");
            }

            // for now, do not check number of points and number of points by return for consistency as not all .las writers set
            // them correctly
            // this.LegacyNumberOfPointRecords
            // this.LegacyNumberOfPointsByReturn
            if ((Double.IsFinite(this.XScaleFactor) == false) || (this.XScaleFactor <= 0.0))
            {
                throw new InvalidDataException("XScaleFactor");
            }
            if ((Double.IsFinite(this.YScaleFactor) == false) || (this.YScaleFactor <= 0.0))
            {
                throw new InvalidDataException("YScaleFactor");
            }
            if ((Double.IsFinite(this.ZScaleFactor) == false) || (this.ZScaleFactor <= 0.0))
            {
                throw new InvalidDataException("ZScaleFactor");
            }
            if (Double.IsFinite(this.XOffset) == false)
            {
                throw new InvalidDataException("XOffset");
            }
            if (Double.IsFinite(this.YOffset) == false) 
            {
                throw new InvalidDataException("YOffset");
            }
            if (Double.IsFinite(this.ZOffset) == false) 
            {
                throw new InvalidDataException("ZOffset");
            }
            if (Double.IsFinite(this.MaxX) == false) 
            {
                throw new InvalidDataException("MaxX");
            }
            if ((Double.IsFinite(this.MinX) == false) || (this.MinX > this.MaxX))
            {
                throw new InvalidDataException("MinX");
            }
            if (Double.IsFinite(this.MaxY) == false)
            {
                throw new InvalidDataException("MaxY");
            }
            if ((Double.IsFinite(this.MinY) == false) || (this.MinY > this.MaxY))
            {
                throw new InvalidDataException("MinY");
            }
            if (Double.IsFinite(this.MaxZ) == false)
            {
                throw new InvalidDataException("MaxZ");
            }
            if ((Double.IsFinite(this.MinZ) == false) || (this.MinZ > this.MaxZ))
            {
                throw new InvalidDataException("MinZ");
            }
        }
    }
}
