using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class PointGridZ : Grid
    {
        public int[] Counts { get; private init; }
        public float[] Points { get; private init; }
        public int PointsPerCell { get; private init; }

        public PointGridZ(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, int pointsPerCell)
            : base(crs, transform, xSizeInCells, ySizeInCells)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pointsPerCell, 1, nameof(pointsPerCell));

            int cellCapacity = xSizeInCells * ySizeInCells;
            this.Counts = new int[cellCapacity];
            this.Points = new float[pointsPerCell * cellCapacity];
            this.PointsPerCell = pointsPerCell;

            Array.Fill(this.Points, Single.MinValue);
        }

        private Raster<float> CreateDsm(int bands)
        {
            Raster<float> dsm = new(this.Crs, this.Transform, this.XSize, this.YSize, bands, Single.MinValue);
            if (bands == 1)
            {
                dsm.Bands[0].Name = "z";
            }
            else
            {
                for (int bandIndex = 0; bandIndex < bands; ++bandIndex)
                {
                    dsm.Bands[bandIndex].Name = "z" + (bandIndex + 1);
                }
            }
            return dsm;
        }

        public Raster<float> GetDigitalSurfaceModel(float isolationDistance)
        {
            Raster<float> dsm = this.CreateDsm(1);
            if (this.PointsPerCell == 1)
            {
                Array.Copy(this.Points, dsm.Data, this.Points.Length);
                return dsm;
            }

            for (int cellIndex = 0; cellIndex < this.Counts.Length; ++cellIndex)
            {
                int pointsInCell = this.Counts[cellIndex];
                if (pointsInCell == 0)
                {
                    dsm.Data[cellIndex] = Single.MinValue; // no data
                }
                else if (pointsInCell == 1)
                {
                    dsm.Data[cellIndex] = this.Points[this.PointsPerCell * cellIndex]; // single point, so no high noise/outlier check
                }
                else
                {
                    int cellStartIndex = this.PointsPerCell * cellIndex;
                    int pointIndex = cellStartIndex + pointsInCell - 1;
                    float zDsm = this.Points[pointIndex];
                    float zCurrent = zDsm;
                    for (; pointIndex > cellStartIndex; --pointIndex)
                    {
                        float zNext = this.Points[pointIndex - 1];
                        if (zCurrent - zNext > isolationDistance)
                        {
                            zDsm = zNext;
                        }

                        zCurrent = zNext;
                    }

                    dsm.Data[cellIndex] = zDsm;
                }
            }

            return dsm;
        }

        public Raster<float> GetUpperPoints()
        {
            Raster<float> dsm = this.CreateDsm(this.PointsPerCell);
            if (this.PointsPerCell == 1)
            {
                Array.Copy(this.Points, dsm.Data, this.Points.Length);
                return dsm;
            }

            int dsmBandOffset = dsm.XSize * dsm.YSize;
            for (int sourceCellIndex = 0; sourceCellIndex < this.Counts.Length; ++sourceCellIndex)
            {
                int cellCount = this.Counts[sourceCellIndex];
                if (cellCount == 0)
                {
                    continue;
                }

                int pointStartIndex = this.PointsPerCell * sourceCellIndex;
                int destinationCellIndex = sourceCellIndex;
                for (int pointIndex = pointStartIndex + cellCount - 1; pointIndex >= pointStartIndex; --pointIndex)
                {
                    dsm.Data[destinationCellIndex] = this.Points[pointIndex];
                    destinationCellIndex += dsmBandOffset;
                }
            }

            return dsm;
        }

        public bool TryAddUpperPoint(int xIndex, int yIndex, float zNew)
        {
            int cellIndex = this.ToCellIndex(xIndex, yIndex);
            int pointsInCell = this.Counts[cellIndex];
            int pointStartIndex = this.PointsPerCell * cellIndex;

            // for now, special case for debugging (also of interest for perf)
            //if (this.PointsPerCell == 1)
            //{
            //    float zKnown = this.Points[pointStartIndex];
            //    if (zNew > zKnown)
            //    {
            //        this.Counts[cellIndex] = 1;
            //        this.Points[pointStartIndex] = zNew;
            //        return true;
            //    }

            //    return false;
            //}

            int pointEndIndex = pointStartIndex + pointsInCell; // or first index past end of array
            for (int pointIndex = pointStartIndex; pointIndex < pointEndIndex; ++pointIndex)
            {
                float zKnown = this.Points[pointIndex];
                if (zNew < zKnown)
                {
                    if (pointsInCell < this.PointsPerCell)
                    {
                        // open slots are available: shift any higher points and insert point at current position
                        for (int destinationIndex = pointStartIndex + pointsInCell; destinationIndex > pointIndex; --destinationIndex)
                        {
                            this.Points[destinationIndex] = this.Points[destinationIndex - 1];
                        }

                        Debug.Assert(this.Points[pointIndex] >= zNew);
                        this.Counts[cellIndex] = pointsInCell + 1;
                        this.Points[pointIndex] = zNew;
                        Debug.Assert(this.Points[pointStartIndex] <= this.Points[pointStartIndex + 1]);
                    }
                    else
                    {
                        // cell's point list is full
                        // Two cases:
                        // - don't insert point as zNew is less than the lowest known z
                        // - shift all lower known points downward and insert point at current position
                        if (pointIndex == pointStartIndex)
                        {
                            return false; // zNew less than the lowest known z
                        }

                        for (int sourceIndex = pointStartIndex + 1; sourceIndex < pointIndex; ++sourceIndex)
                        {
                            this.Points[sourceIndex - 1] = this.Points[sourceIndex];
                        }

                        Debug.Assert(this.Points[pointStartIndex] <= this.Points[pointStartIndex + 1]);
                        Debug.Assert(this.Points[pointIndex - 1] <= zNew);
                        this.Points[pointIndex - 1] = zNew;
                    }
                    return true;
                }
            }

            // loop above didn't hit a return statement
            // Two cases:
            // - cell has no points
            // - point is higher than all known points
            if (pointsInCell == 0)
            {
                Debug.Assert(this.Points[pointStartIndex] <= zNew);

                this.Counts[cellIndex] = 1;
                this.Points[pointStartIndex] = zNew;
            }
            else
            {
                // Similar to above in for loop, two cases:
                // - cell has an open slot, so set point
                // - cell is full: shift all existing points and insert point at top of cell
                if (pointsInCell == this.PointsPerCell)
                {
                    Debug.Assert(this.Points[pointStartIndex] <= zNew);
                    //Debug.Assert(this.Points[pointStartIndex + 1] <= zNew); // debug guard if this.PointsPerCell == 2
                    for (int sourceIndex = pointStartIndex + 1; sourceIndex < pointEndIndex; ++sourceIndex)
                    {
                        this.Points[sourceIndex - 1] = this.Points[sourceIndex];
                    }

                    int pointIndex = pointStartIndex + pointsInCell - 1;
                    this.Points[pointIndex] = zNew;
                }
                else
                {
                    Debug.Assert(this.Points[pointStartIndex] <= zNew);
                    // Debug.Assert(this.Points[pointStartIndex + 1] <= zNew); // debug guard if this.PointsPerCell == 2
                    int pointIndex = pointStartIndex + pointsInCell;
                    this.Counts[cellIndex] = pointsInCell + 1;
                    this.Points[pointIndex] = zNew;
                }
            }

            return true; // point was inserted
        }
    }
}
