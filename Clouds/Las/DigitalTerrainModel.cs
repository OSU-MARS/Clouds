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

        public static DigitalTerrainModel CreateRecreateOrReset(DigitalTerrainModel? dtm, LasFile lasFile, SpatialReference crs, double cellSize, int sizeX, int sizeY, string filePath)
        {
            GridGeoTransform dtmTransform = new(lasFile, cellSize, cellSize);
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

        public void FillNoDataFromCardinalDistance(ref RasterBand<float>? buffer)
        {
            if ((buffer == null) || (buffer.SizeX != this.SizeX) || (buffer.SizeY != this.SizeY))
            {
                buffer = new RasterBand<float>(this, "zBuffer", RasterBand<float>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
            }

            // interpolate values
            for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                {
                    if (this.Z.IsNoData(this.Z[cellIndex]))
                    {
                        float zSum = 0.0F;
                        float searchReciprocalSum = 0.0F;

                        // search for western neighbor
                        for (int searchCellIndex = cellIndex - 1, searchXindex = xIndex - 1; searchXindex >= 0; --searchCellIndex, --searchXindex)
                        {
                            float zSearchValue = this.Z[searchCellIndex];
                            if (this.Z.IsNoData(zSearchValue) == false)
                            {
                                float searchDistanceReciprocal = 1.0F / (xIndex - searchXindex);
                                zSum += searchDistanceReciprocal * zSearchValue;
                                searchReciprocalSum += searchDistanceReciprocal;
                                break;
                            }
                        }

                        // search for eastern neighbor
                        for (int searchCellIndex = cellIndex + 1, searchXindex = xIndex + 1; searchXindex < this.SizeX; ++searchCellIndex, ++searchXindex)
                        {
                            float zSearchValue = this.Z[searchCellIndex];
                            if (this.Z.IsNoData(zSearchValue) == false)
                            {
                                float searchDistanceReciprocal = 1.0F / (searchXindex - xIndex);
                                zSum += searchDistanceReciprocal * zSearchValue;
                                searchReciprocalSum += searchDistanceReciprocal;
                                break;
                            }
                        }

                        // search for northern neighbor
                        for (int searchCellIndex = cellIndex - this.SizeX, searchYindex = yIndex - 1; searchYindex >= 0; searchCellIndex -= this.SizeX, --searchYindex)
                        {
                            float zSearchValue = this.Z[searchCellIndex];
                            if (this.Z.IsNoData(zSearchValue) == false)
                            {
                                float searchDistanceReciprocal = 1.0F / (yIndex - searchYindex);
                                zSum += searchDistanceReciprocal * zSearchValue;
                                searchReciprocalSum += searchDistanceReciprocal;
                                break;
                            }
                        }

                        // search for southern neighbor
                        for (int searchCellIndex = cellIndex + this.SizeX, searchYindex = yIndex + 1; searchYindex < this.SizeY; searchCellIndex += this.SizeX, ++searchYindex)
                        {
                            float zSearchValue = this.Z[searchCellIndex];
                            if (this.Z.IsNoData(zSearchValue) == false)
                            {
                                float searchDistanceReciprocal = 1.0F / (searchYindex - yIndex);
                                zSum += searchDistanceReciprocal * zSearchValue;
                                searchReciprocalSum += searchDistanceReciprocal;
                                break;
                            }
                        }

                        // copy interpolated value to buffer
                        buffer[cellIndex] = zSum / searchReciprocalSum;
                    }
                }
            }

            // copy interpolated values from buffer
            for (int cellIndex = 0, yIndex = 0; yIndex < this.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                {
                    if (this.Z.IsNoData(this.Z[cellIndex]))
                    {
                        // could check and only assign if buffer has a value but checking requires buffer be cleared before each pass
                        // So more memory traffic would be generated than not checking.
                        this.Z[cellIndex] = buffer[cellIndex];
                    }
                }
            }
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
        /// Convert accumulated red, green, blue, NIR, first return intensity, and second return intensity sums to averages.
        /// </summary>
        public void OnPointAdditionComplete()
        {
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                float accumulatedZ = this.Z[cellIndex];
                if (this.Z.IsNoData(accumulatedZ) == false)
                {
                    UInt32 pointsInCell = this.PointCount[cellIndex];
                    Debug.Assert(pointsInCell > 0);
                    this.Z[cellIndex] = accumulatedZ / pointsInCell;
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