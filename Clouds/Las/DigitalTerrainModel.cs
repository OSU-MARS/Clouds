using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    public class DigitalTerrainModel : Raster
    {
        public const string PointCountBandName = "nPoints";
        public const string ZBandName = "Z";

        public RasterBand<UInt32> PointCount { get; private init; }
        public RasterBand<float> Z { get; private init; }

        // constructor from point cloud tile and cell size for accumulation of mean point RGB[+NIR] intensity values in pixels
        public DigitalTerrainModel(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize, cloneCrsAndTransform: true)
        {
            this.PointCount = new(this, DigitalTerrainModel.PointCountBandName, 0, RasterBandInitialValue.Default);
            this.Z = new(this, DigitalTerrainModel.ZBandName, RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
        }

        public static DigitalTerrainModel CreateRecreateOrReset(DigitalTerrainModel? dtm, SpatialReference crs, GridGeoTransform dtmTransform, int sizeX, int sizeY, string filePath)
        {
            if ((dtm == null) || (dtm.SizeX != sizeX) || (dtm.SizeY != sizeY) || (SpatialReferenceExtensions.IsSameCrs(dtm.Crs, crs) == false))
            {
                return new(crs, dtmTransform, sizeX, sizeY)
                {
                    FilePath = filePath
                };
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(dtm.Crs, crs));
            dtm.FilePath = filePath;
            dtm.Transform.Copy(dtmTransform);

            dtm.PointCount.Fill(0);
            dtm.Z.Fill(default);
            return dtm;
        }

        public int FillNoDataFromCardinalAndOrdinalLinearDistances(int maxCardinalSearchDistanceInRasterCells, ref RasterBand<float>? noDataFilled, ref RasterBand<float>? buffer)
        {
            if (maxCardinalSearchDistanceInRasterCells < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCardinalSearchDistanceInRasterCells), $"Search distance must be a positive integer, not {maxCardinalSearchDistanceInRasterCells} raster cells.");
            }
            if ((noDataFilled == null) || (noDataFilled.SizeX != this.SizeX) || (noDataFilled.SizeY != this.SizeY))
            {
                noDataFilled = new RasterBand<float>(this, "dtmZinterpolated", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Unintialized);
            }
            if ((buffer == null) || (buffer.SizeX != this.SizeX) || (buffer.SizeY != this.SizeY))
            {
                buffer = new RasterBand<float>(this, "dtmZbuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Unintialized);
            }

            // interpolate values
            int maxFillIterations = Int32.Min(this.SizeX, this.SizeY) / maxCardinalSearchDistanceInRasterCells + 1;
            Debug.Assert(maxFillIterations > 0, $"{maxFillIterations} is less than one.");

            noDataFilled.CopyAllValuesFrom(this.Z);
            RasterBand<float> sourceBuffer = noDataFilled;
            RasterBand<float> destinationBuffer = buffer;

            int maxOrdinalSearchDistanceInRasterCells = (int)Single.Round(maxCardinalSearchDistanceInRasterCells / 1.414214F); // diagonal search distance
            int noDataValuesFound = 0;
            int previousNoDataValues = -1;
            for (int fillIteration = 0; fillIteration < maxFillIterations; ++fillIteration)
            {
                destinationBuffer.CopyAllValuesFrom(sourceBuffer);
                noDataValuesFound = 0;
                for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
                {
                    for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                    {
                        float z = sourceBuffer[cellIndex];
                        if (sourceBuffer.IsNoData(z))
                        {
                            float zSumInverseDistanceWeighted = 0.0F;
                            float inverseDistanceSum = 0.0F;

                            // search for western neighbor
                            int minSearchXindex = Int32.Max(0, xIndex - maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - 1, searchXindex = xIndex - 1; searchXindex >= minSearchXindex; --searchCellIndex, --searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    Debug.Assert(deltaX > 0);
                                    float searchDistanceReciprocal = 1.0F / deltaX;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northwestern neighbor
                            // Could possibly micro-optimize by looking at the minimum number of x or y steps.
                            minSearchXindex = Int32.Max(0, xIndex - maxOrdinalSearchDistanceInRasterCells);
                            int minSearchYindex = Int32.Max(0, yIndex - maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex - 1, searchYindex = yIndex - 1; (searchXindex >= minSearchXindex) && (searchYindex >= minSearchYindex); --searchXindex, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    Debug.Assert((deltaX > 0) && (deltaX == yIndex - searchYindex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 1.0F / (1.414214F * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northern neighbor
                            minSearchYindex = Int32.Max(0, yIndex - maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - this.SizeX, searchYindex = yIndex - 1; searchYindex >= minSearchYindex; searchCellIndex -= this.SizeX, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaY = yIndex - searchYindex;
                                    Debug.Assert(deltaY > 0);
                                    float searchDistanceReciprocal = 1.0F / deltaY;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northeastern neighbor
                            int maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxOrdinalSearchDistanceInRasterCells);
                            minSearchYindex = Int32.Max(0, yIndex - maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex + 1, searchYindex = yIndex - 1; (searchXindex < maxSearchXindex) && (searchYindex >= minSearchYindex); ++searchXindex, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    Debug.Assert((deltaX > 0) && (deltaX == yIndex - searchYindex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 1.0F / (1.414214F * deltaX); // sqrt(Δx * Δx + Δy * Δy) = sqrt(2Δx²)
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for eastern neighbor
                            maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + 1, searchXindex = xIndex + 1; searchXindex < maxSearchXindex; ++searchCellIndex, ++searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    Debug.Assert(deltaX > 0);
                                    float searchDistanceReciprocal = 1.0F / deltaX;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southwestern neighbor
                            minSearchXindex = Int32.Max(0, xIndex - maxOrdinalSearchDistanceInRasterCells);
                            int maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex - 1, searchYindex = yIndex + 1; (searchXindex >= minSearchXindex) && (searchYindex < maxSearchYindex); --searchXindex, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    Debug.Assert((deltaX > 0) && (deltaX == searchYindex - yIndex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 1.0F / (1.414214F * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southern neighbor
                            maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + this.SizeX, searchYindex = yIndex + 1; searchYindex < maxSearchYindex; searchCellIndex += this.SizeX, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaY = searchYindex - yIndex;
                                    Debug.Assert(deltaY > 0);
                                    float searchDistanceReciprocal = 1.0F / deltaY;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southeastern neighbor
                            maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxOrdinalSearchDistanceInRasterCells);
                            maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex + 1, searchYindex = yIndex + 1; (searchXindex < maxSearchXindex) && (searchYindex < maxSearchYindex); ++searchXindex, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    Debug.Assert((deltaX > 0) && (deltaX == searchYindex - yIndex), "Diagonal search distances not matched.");
                                    float searchDistanceReciprocal = 1.0F / (1.414214F * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // copy interpolated value to buffer
                            if (inverseDistanceSum > 0.0F)
                            {
                                destinationBuffer[cellIndex] = zSumInverseDistanceWeighted / inverseDistanceSum;
                            }
                            else
                            {
                                ++noDataValuesFound;
                            }
                        }

                        ++cellIndex;
                    }
                }
                
                (sourceBuffer, destinationBuffer) = (destinationBuffer, sourceBuffer);
                if ((noDataValuesFound == 0) || (previousNoDataValues == noDataValuesFound))
                {
                    break;
                }

                previousNoDataValues = noDataValuesFound;
            }

            // if the last interpolation pass's destination wasn't the output buffer, which is now swapped into the source buffer position, then the
            // output buffer needs to be updated to point to the final result
            if (Object.ReferenceEquals(noDataFilled, destinationBuffer))
            {
                (noDataFilled, buffer) = (sourceBuffer, destinationBuffer);
            }

            // for debugging leaks of no data cells
            //for (int cellIndex = 0; cellIndex < noDataFilled.Cells; ++cellIndex)
            //{
            //    Debug.Assert(noDataFilled.IsNoData(noDataFilled[cellIndex]) == false, $"Cell at index {cellIndex} should have data but instead continues to contain a no data value.");
            //}

            return noDataValuesFound;
        }

        public int FillNoDataFromCardinalAndOrdinalSquaredDistances(int maxCardinalSearchDistanceInRasterCells, ref RasterBand<float>? noDataFilled, ref RasterBand<float>? buffer)
        {
            if (maxCardinalSearchDistanceInRasterCells < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCardinalSearchDistanceInRasterCells), $"Search distance must be a positive integer, not {maxCardinalSearchDistanceInRasterCells} raster cells.");
            }
            if ((noDataFilled == null) || (noDataFilled.SizeX != this.SizeX) || (noDataFilled.SizeY != this.SizeY))
            {
                noDataFilled = new RasterBand<float>(this, "dtmZinterpolated", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
            }
            if ((buffer == null) || (buffer.SizeX != this.SizeX) || (buffer.SizeY != this.SizeY))
            {
                buffer = new RasterBand<float>(this, "dtmZbuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Unintialized);
            }

            // interpolate values
            int maxFillIterations = Int32.Min(this.SizeX, this.SizeY) / maxCardinalSearchDistanceInRasterCells + 1;
            Debug.Assert(maxFillIterations > 0, $"{maxFillIterations} is less than one.");

            noDataFilled.CopyAllValuesFrom(this.Z);
            RasterBand<float> sourceBuffer = noDataFilled;
            RasterBand<float> destinationBuffer = buffer;

            int maxOrdinalSearchDistanceInRasterCells = (int)Single.Round(maxCardinalSearchDistanceInRasterCells / 1.414214F); // diagonal search distance
            int noDataValuesFound = 0;
            int previousNoDataValues = -1;
            for (int fillIteration = 0; fillIteration < maxFillIterations; ++fillIteration)
            {
                destinationBuffer.CopyAllValuesFrom(sourceBuffer);
                noDataValuesFound = 0;
                for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
                {
                    for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                    {
                        float z = sourceBuffer[cellIndex];
                        if (sourceBuffer.IsNoData(z))
                        {
                            float zSumInverseDistanceWeighted = 0.0F;
                            float inverseDistanceSum = 0.0F;

                            // search for western neighbor
                            int minSearchXindex = Int32.Max(0, xIndex - maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - 1, searchXindex = xIndex - 1; searchXindex >= minSearchXindex; --searchCellIndex, --searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    float searchDistanceReciprocal = 1.0F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northwestern neighbor
                            minSearchXindex = Int32.Max(0, xIndex - maxOrdinalSearchDistanceInRasterCells);
                            int minSearchYindex = Int32.Max(0, yIndex - maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex - 1, searchYindex = yIndex - 1; (searchXindex >= minSearchXindex) && (searchYindex >= minSearchYindex); --searchXindex, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    Debug.Assert((deltaX > 0) && (deltaX == yIndex - searchYindex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 0.5F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northern neighbor
                            minSearchYindex = Int32.Max(0, yIndex - maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - this.SizeX, searchYindex = yIndex - 1; searchYindex >= minSearchYindex; searchCellIndex -= this.SizeX, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaY = yIndex - searchYindex;
                                    float searchDistanceReciprocal = 1.0F / (deltaY * deltaY);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northeastern neighbor
                            int maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxOrdinalSearchDistanceInRasterCells);
                            minSearchYindex = Int32.Max(0, yIndex - maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex + 1, searchYindex = yIndex - 1; (searchXindex < maxSearchXindex) && (searchYindex >= minSearchYindex); ++searchXindex, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    Debug.Assert((deltaX > 0) && (deltaX == yIndex - searchYindex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 0.5F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for eastern neighbor
                            maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + 1, searchXindex = xIndex + 1; searchXindex < maxSearchXindex; ++searchCellIndex, ++searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    float searchDistanceReciprocal = 1.0F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southwestern neighbor
                            minSearchXindex = Int32.Max(0, xIndex - maxOrdinalSearchDistanceInRasterCells);
                            int maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex - 1, searchYindex = yIndex + 1; (searchXindex >= minSearchXindex) && (searchYindex < maxSearchYindex); --searchXindex, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = xIndex - searchXindex;
                                    Debug.Assert((deltaX > 0) && (deltaX == searchYindex - yIndex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 0.5F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southern neighbor
                            maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxCardinalSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + this.SizeX, searchYindex = yIndex + 1; searchYindex < maxSearchYindex; searchCellIndex += this.SizeX, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaY = searchYindex - yIndex;
                                    float searchDistanceReciprocal = 1.0F / (deltaY * deltaY);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southeastern neighbor
                            maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxOrdinalSearchDistanceInRasterCells);
                            maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxOrdinalSearchDistanceInRasterCells);
                            for (int searchXindex = xIndex + 1, searchYindex = yIndex + 1; (searchXindex < maxSearchXindex) && (searchYindex < maxSearchYindex); ++searchXindex, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchXindex, searchYindex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    int deltaX = searchXindex - xIndex;
                                    Debug.Assert((deltaX > 0) && (deltaX == searchYindex - yIndex), "Diagonal search distances not matched");
                                    float searchDistanceReciprocal = 0.5F / (deltaX * deltaX);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // copy interpolated value to buffer
                            if (inverseDistanceSum > 0.0F)
                            {
                                destinationBuffer[cellIndex] = zSumInverseDistanceWeighted / inverseDistanceSum;
                            }
                            else 
                            {
                                ++noDataValuesFound;
                            }
                        }

                        ++cellIndex;
                    }
                }

                (sourceBuffer, destinationBuffer) = (destinationBuffer, sourceBuffer);
                if ((noDataValuesFound == 0) || (previousNoDataValues == noDataValuesFound))
                {
                    break;
                }

                previousNoDataValues = noDataValuesFound;
            }

            if (Object.ReferenceEquals(noDataFilled, destinationBuffer))
            {
                (noDataFilled, buffer) = (sourceBuffer, destinationBuffer);
            }

            return noDataValuesFound;
        }

        public int FillNoDataFromCardinalLinearDistances(int maxSearchDistanceInRasterCells, ref RasterBand<float>? noDataFilled, ref RasterBand<float>? buffer)
        {
            if (maxSearchDistanceInRasterCells < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSearchDistanceInRasterCells), $"Search distance must be a positive integer, not {maxSearchDistanceInRasterCells} raster cells.");
            }
            if ((noDataFilled == null) || (noDataFilled.SizeX != this.SizeX) || (noDataFilled.SizeY != this.SizeY))
            {
                noDataFilled = new RasterBand<float>(this, "dtmZinterpolated", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
            }
            if ((buffer == null) || (buffer.SizeX != this.SizeX) || (buffer.SizeY != this.SizeY))
            {
                buffer = new RasterBand<float>(this, "dtmZbuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Unintialized);
            }

            // interpolate values
            int maxFillIterations = Int32.Min(this.SizeX, this.SizeY) / maxSearchDistanceInRasterCells + 1;
            Debug.Assert(maxFillIterations > 0, $"{maxFillIterations} is less than one.");

            noDataFilled.CopyAllValuesFrom(this.Z);
            RasterBand<float> sourceBuffer = noDataFilled;
            RasterBand<float> destinationBuffer = noDataFilled;

            int noDataValuesFound = 0;
            int previousNoDataValues = 0;
            for (int fillIteration = 0; fillIteration < maxFillIterations; ++fillIteration)
            {
                destinationBuffer.CopyAllValuesFrom(sourceBuffer);
                noDataValuesFound = 0;
                for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
                {
                    for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                    {
                        float z = sourceBuffer[cellIndex];
                        if (sourceBuffer.IsNoData(z))
                        {
                            float zSumInverseDistanceWeighted = 0.0F;
                            float inverseDistanceSum = 0.0F;

                            // search for western neighbor
                            int minSearchXindex = Int32.Max(0, xIndex - maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - 1, searchXindex = xIndex - 1; searchXindex >= minSearchXindex; --searchCellIndex, --searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (xIndex - searchXindex);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for eastern neighbor
                            int maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + 1, searchXindex = xIndex + 1; searchXindex < maxSearchXindex; ++searchCellIndex, ++searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (searchXindex - xIndex);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northern neighbor
                            int minSearchYindex = Int32.Max(0, yIndex - maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - this.SizeX, searchYindex = yIndex - 1; searchYindex >= minSearchYindex; searchCellIndex -= this.SizeX, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (yIndex - searchYindex);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southern neighbor
                            int maxSearchYindex = Int32.Min(this.SizeY, yIndex + maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + this.SizeX, searchYindex = yIndex + 1; searchYindex < maxSearchYindex; searchCellIndex += this.SizeX, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (searchYindex - yIndex);
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // copy interpolated value to buffer
                            if (inverseDistanceSum > 0.0F)
                            {
                                destinationBuffer[cellIndex] = zSumInverseDistanceWeighted / inverseDistanceSum;
                            }
                            else
                            {
                                ++noDataValuesFound;
                            }
                        }

                        ++cellIndex;
                    }
                }

                (sourceBuffer, destinationBuffer) = (destinationBuffer, sourceBuffer);
                if ((noDataValuesFound == 0) || (previousNoDataValues == noDataValuesFound))
                {
                    break;
                }

                previousNoDataValues = noDataValuesFound;
            }

            if (Object.ReferenceEquals(noDataFilled, destinationBuffer))
            {
                (noDataFilled, buffer) = (sourceBuffer, destinationBuffer);
            }

            return noDataValuesFound;
        }

        // near identical to FillNoDataFromCardinalLinearDistance
        public int FillNoDataFromCardinalSquaredDistances(int maxSearchDistanceInRasterCells, ref RasterBand<float>? noDataFilled, ref RasterBand<float>? buffer)
        {
            if (maxSearchDistanceInRasterCells < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSearchDistanceInRasterCells), $"Search distance must be a positive integer, not {maxSearchDistanceInRasterCells} raster cells.");
            }
            if ((noDataFilled == null) || (noDataFilled.SizeX != this.SizeX) || (noDataFilled.SizeY != this.SizeY))
            {
                noDataFilled = new RasterBand<float>(this, "zBuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
            }
            if ((buffer == null) || (buffer.SizeX != this.SizeX) || (buffer.SizeY != this.SizeY))
            {
                buffer = new RasterBand<float>(this, "dtmZbuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Unintialized);
            }

            // interpolate values
            int maxFillIterations = Int32.Min(this.SizeX, this.SizeY) / maxSearchDistanceInRasterCells + 1;
            Debug.Assert(maxFillIterations > 0, $"{maxFillIterations} is less than one.");

            noDataFilled.CopyAllValuesFrom(this.Z);
            RasterBand<float> sourceBuffer = noDataFilled;
            RasterBand<float> destinationBuffer = buffer;

            int noDataValuesFound = 0;
            int previousNoDataValues = 0;
            for (int fillIteration = 0; fillIteration < maxFillIterations; ++fillIteration)
            {
                destinationBuffer.CopyAllValuesFrom(sourceBuffer);
                noDataValuesFound = 0;
                for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
                {
                    for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                    {
                        float z = sourceBuffer[cellIndex];
                        if (sourceBuffer.IsNoData(z))
                        {
                            float zSumInverseDistanceWeighted = 0.0F;
                            float inverseDistanceSum = 0.0F;

                            // search for western neighbor
                            int minSearchXindex = Int32.Max(0, xIndex - maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - 1, searchXindex = xIndex - 1; searchXindex >= minSearchXindex; --searchCellIndex, --searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (xIndex - searchXindex);
                                    searchDistanceReciprocal *= searchDistanceReciprocal;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for eastern neighbor
                            int maxSearchXindex = Int32.Min(this.SizeX, xIndex + maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + 1, searchXindex = xIndex + 1; searchXindex < maxSearchXindex; ++searchCellIndex, ++searchXindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (searchXindex - xIndex);
                                    searchDistanceReciprocal *= searchDistanceReciprocal;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for northern neighbor
                            int minSearchYindex = Int32.Max(0, yIndex - maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex - this.SizeX, searchYindex = yIndex - 1; searchYindex >= minSearchYindex; searchCellIndex -= this.SizeX, --searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (yIndex - searchYindex);
                                    searchDistanceReciprocal *= searchDistanceReciprocal;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // search for southern neighbor
                            int maxSearchYindex = Int32.Min(this.SizeY - 1, yIndex + maxSearchDistanceInRasterCells);
                            for (int searchCellIndex = cellIndex + this.SizeX, searchYindex = yIndex + 1; searchYindex <= maxSearchYindex; searchCellIndex += this.SizeX, ++searchYindex)
                            {
                                float zSearchValue = sourceBuffer[searchCellIndex];
                                if (sourceBuffer.IsNoData(zSearchValue) == false)
                                {
                                    float searchDistanceReciprocal = 1.0F / (searchYindex - yIndex);
                                    searchDistanceReciprocal *= searchDistanceReciprocal;
                                    zSumInverseDistanceWeighted += searchDistanceReciprocal * zSearchValue;
                                    inverseDistanceSum += searchDistanceReciprocal;
                                    break;
                                }
                            }

                            // copy interpolated value to buffer
                            if (inverseDistanceSum > 0.0F)
                            {
                                destinationBuffer[cellIndex] = zSumInverseDistanceWeighted / inverseDistanceSum;
                            }
                            else
                            {
                                ++noDataValuesFound;
                            }
                        }
                            
                        ++cellIndex;
                    }
                }

                if ((noDataValuesFound == 0) || (previousNoDataValues == noDataValuesFound))
                {
                    break;
                }

                previousNoDataValues = noDataValuesFound;
            }

            if (Object.ReferenceEquals(noDataFilled, destinationBuffer))
            {
                (noDataFilled, buffer) = (sourceBuffer, destinationBuffer);
            }

            return noDataValuesFound;
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Z;
            yield return this.PointCount;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.Z.GetStatistics(), this.PointCount.GetStatistics() ];
        }

        /// <summary>
        /// Convert accumulated ground elevation sums to average ground height of LiDAR hits within each cell.
        /// </summary>
        public void OnPointAdditionComplete()
        {
            float zNoData = this.Z.NoDataValue;
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                float accumulatedZ = this.Z[cellIndex];
                if (this.Z.IsNoData(accumulatedZ) == false)
                {
                    UInt32 pointsInCell = this.PointCount[cellIndex];
                    if (pointsInCell > 0)
                    {
                        this.Z[cellIndex] = accumulatedZ / pointsInCell;
                    }
                    else
                    {
                        this.Z[cellIndex] = zNoData;
                    }
                }
            }
        }

        public override void ReadBandData()
        {
            using Dataset rasterDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            for (int gdalBandIndex = 1; gdalBandIndex <= rasterDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = rasterDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalTerrainModel.ZBandName:
                        this.Z.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case DigitalTerrainModel.PointCountBandName:
                        this.PointCount.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException($"Unhandled band '{bandName}' in image raster '{this.FilePath}'.");
                }
            }

            rasterDataset.FlushCache();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            this.Z.ReturnData(dataBufferPool);
            this.PointCount.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(name, this.Z.Name, StringComparison.Ordinal))
            {
                band = this.Z;
            }
            else if (String.Equals(name, this.PointCount.Name, StringComparison.Ordinal))
            {
                band = this.PointCount;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            if (String.Equals(name, this.Z.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
                return true;
            }
            if (String.Equals(name, this.PointCount.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 1;
                return true;
            }

            bandIndexInFile = -1;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.Z.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PointCount.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string dtmPath, bool compress)
        {
            this.Write(dtmPath, includePointCount: true, compress);
        }

        public void Write(string dtmPath, bool includePointCount, bool compress)
        {
            int bands = includePointCount ? 2 : 1;
            using Dataset dsmDataset = this.CreateGdalRaster(dtmPath, bands, DataType.GDT_Float32, compress);
            this.Z.Write(dsmDataset, 1);
            if (includePointCount)
            {
                this.PointCount.Write(dsmDataset, 2);
            }

            this.FilePath = dtmPath;
        }
    }
}