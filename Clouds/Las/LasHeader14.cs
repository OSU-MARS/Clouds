using System;
using System.IO;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// Header of a version 1.4 .las file.
    /// </summary>
    public class LasHeader14 : LasHeader13
    {
        public new const int HeaderSizeInBytes = 375;

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// Of type <see cref="UInt64"/> in LAS specification but functionally restricted to <see cref="Int64"/> in this implementation as C# \
        /// streams do not support seeking to <see cref="UInt64"/> positions. This is only an issue for .las or .laz files larger than 9.2 EB 
        /// (exabytes), which are unlikely to occur.
        /// </remarks>
        public UInt64 StartOfFirstExtendedVariableLengthRecord { get; set; }
        public UInt32 NumberOfExtendedVariableLengthRecords { get; set; }
        public UInt64 NumberOfPointRecords { get; set; }
        public UInt64[] NumberOfPointsByReturn { get; set; }

        public LasHeader14()
        {
            this.VersionMinor = 4;
            this.HeaderSize = LasHeader14.HeaderSizeInBytes;
            this.NumberOfPointsByReturn = new UInt64[15];
        }

        public override UInt64 GetNumberOfPoints()
        {
            // in LAS 1.4 R15 compliant fles the number of point records should be set and legacy number of point records should be zero
            // However, not all writers are compliant so, conservatively, return whichever of the two is plausibly available.
            if (this.NumberOfPointRecords == this.LegacyNumberOfPointRecords)
            {
                return this.NumberOfPointRecords;
            }
            else if (this.LegacyNumberOfPointRecords == 0)
            {
                return this.NumberOfPointRecords;
            }

            throw new InvalidDataException("Number of point records (" + this.NumberOfPointRecords.ToString(",") + ") is inconsistent with legacy number of point records (" + this.LegacyNumberOfPointRecords.ToString(",") + ").");
        }

        public override void Validate()
        {
            base.Validate();

            UInt64 expectedEndOfPointData = this.OffsetToPointData + this.PointDataRecordLength * this.GetNumberOfPoints();
            // can't check StartOfFirstExtendedVariableLengthRecord is set when extended variable length records are present
            if ((this.StartOfFirstExtendedVariableLengthRecord != 0) && (this.StartOfFirstExtendedVariableLengthRecord != expectedEndOfPointData))
            {
                throw new InvalidDataException("StartOfFirstExtendedVariableLengthRecord");
            }

            // for now, do not check number of points and number of points by return for consistency as not all .las writers set
            // them correctly
            // this.NumberOfPointRecords
            // this.NumberOfPointsByReturn
        }
    }
}
