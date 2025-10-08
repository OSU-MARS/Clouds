using System;

namespace Mars.Clouds.Las
{
    public class LasFilteringResult
    {
        public UInt64[] NumberOfPointsByReturn { get; private init; }
        public UInt64 PointsRemoved { get; private init; }
        public float ZMax { get; init; }
        public float ZMin { get; init; }

        public LasFilteringResult(ReadOnlySpan<UInt64> initialNumberOfPointsByReturn, ReadOnlySpan<UInt64> numberOfPointsRemovedByReturn) 
        {
            if (initialNumberOfPointsByReturn.Length != numberOfPointsRemovedByReturn.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfPointsRemovedByReturn), $"Initial point counts have {initialNumberOfPointsByReturn.Length} returns but removal counts have {numberOfPointsRemovedByReturn.Length} returns.");
            }

            this.NumberOfPointsByReturn = new UInt64[initialNumberOfPointsByReturn.Length];
            this.PointsRemoved = 0;
            for (int returnIndex = 0; returnIndex < initialNumberOfPointsByReturn.Length; ++returnIndex)
            {
                UInt64 pointsRemoved = numberOfPointsRemovedByReturn[returnIndex];
                this.NumberOfPointsByReturn[returnIndex] = initialNumberOfPointsByReturn[returnIndex] - pointsRemoved;
                this.PointsRemoved += pointsRemoved;
            }

            this.ZMax = Single.NaN;
            this.ZMin = Single.NaN;
        }
    }
}
