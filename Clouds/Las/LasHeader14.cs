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
        public const int SupportedNumberOfReturns = 15;

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// Of type <see cref="UInt64"/> as required by the LAS 1.4 R15 specification but functionally restricted to <see cref="Int64"/> in 
        /// this implementation as C# streams do not support seeking to <see cref="UInt64"/> positions. This is only an issue for .las or 
        /// .laz files larger than 9.2 EB (exabytes), which are unlikely to occur.
        /// </remarks>
        public UInt64 StartOfFirstExtendedVariableLengthRecord { get; set; }
        public UInt32 NumberOfExtendedVariableLengthRecords { get; set; }
        public UInt64 NumberOfPointRecords { get; set; }
        public UInt64[] NumberOfPointsByReturn { get; set; }

        public LasHeader14()
        {
            this.VersionMinor = 4;
            this.HeaderSize = LasHeader14.HeaderSizeInBytes;
            this.NumberOfPointsByReturn = new UInt64[LasHeader14.SupportedNumberOfReturns];
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

            throw new InvalidDataException($"Number of point records ({this.NumberOfPointRecords:,}) is inconsistent with legacy number of point records ({this.LegacyNumberOfPointRecords:,}).");
        }

        public override UInt64[] GetNumberOfPointsByReturn()
        {
            return this.NumberOfPointsByReturn;
        }

        public override void IncrementFirstReturnCount(long returnNumbersRepaired)
        {
            this.LegacyNumberOfPointsByReturn[0] += (UInt32)returnNumbersRepaired;
            this.NumberOfPointsByReturn[0] += (UInt64)returnNumbersRepaired;
        }

        public override void SetNumberOfPointsByReturn(UInt64[] numberOfPointsByReturn)
        {
            if (numberOfPointsByReturn.Length > this.NumberOfPointsByReturn.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfPointsByReturn), $"A maximum of {this.NumberOfPointsByReturn.Length} returns is supported but {numberOfPointsByReturn.Length} returns were passed.");
            }

            UInt64 numberOfPointRecords = 0;
            for (int returnIndex = 0; returnIndex < this.NumberOfPointsByReturn.Length; ++returnIndex)
            {
                UInt64 numberOfPoints = numberOfPointsByReturn[returnIndex];
                this.NumberOfPointsByReturn[returnIndex] = numberOfPoints;
                numberOfPointRecords += numberOfPoints;
            }

            this.NumberOfPointRecords = numberOfPointRecords;

            if ((this.PointDataRecordFormat < 6) && (numberOfPointRecords <= UInt32.MaxValue))
            {
                // support backwards compatibility as permitted by LAS 1.4 R15 §2.3
                this.LegacyNumberOfPointRecords = (UInt32)numberOfPointRecords;
            }
            else
            {
                this.LegacyNumberOfPointRecords = 0;
            }

            if (this.NumberOfExtendedVariableLengthRecords > 0)
            {
                this.StartOfFirstExtendedVariableLengthRecord = this.OffsetToPointData + this.PointDataRecordLength * this.NumberOfPointRecords;
            }
            // otherwise leave this.StartOfFirstExtendedVariableLengthRecord at zero
        }

        public override void ShiftOffsetToPointData(Int64 shift)
        {
            base.ShiftOffsetToPointData(shift);
            this.StartOfFirstExtendedVariableLengthRecord = (UInt64)((Int64)this.StartOfFirstExtendedVariableLengthRecord + shift);
        }

        public override void Validate()
        {
            base.Validate();

            // if EVLRs are present, verify they're well positioned
            // If there are no EVLRs, ignore the EVLR start position as the LAS specification places no requirements on it.
            if (this.NumberOfExtendedVariableLengthRecords > 0)
            {
                UInt64 expectedEndOfPointData = this.OffsetToPointData + this.PointDataRecordLength * this.GetNumberOfPoints();
                if (this.StartOfFirstExtendedVariableLengthRecord != expectedEndOfPointData)
                {
                    // should 
                    throw new InvalidDataException($"Extended variable length records begin at an offset of {this.StartOfFirstExtendedVariableLengthRecord:n0} bytes but point data ends at {expectedEndOfPointData:n0} bytes.");
                }
            }

            // for now, do not check number of points and number of points by return for consistency as not all .las writers set
            // them correctly
            // this.NumberOfPointRecords
            // this.NumberOfPointsByReturn
            // this.LegacyNumberOfPointRecords
            // this.LegacyNumberOfPointsByReturn
        }
    }
}
