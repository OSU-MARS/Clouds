using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.Las
{
    public class ImageRaster
    {
        public const string DiagnosticDirectoryPointCounts = "nPoints";
        public const string DiagnosticDirectoryScanAngle = "scanAngle";

        public const string RedBandName = "red";
        public const string GreenBandName = "green";
        public const string BlueBandName = "blue";
        public const string NearInfraredBandName = "nearInfrared";
        public const string IntensityFirstReturnBandName = "intensityFirstReturn";
        public const string IntensitySecondReturnBandName = "intensitySecondReturn";
        public const string ScanAngleMeanAbsoluteBandName = "scanAngleMeanAbsolute";
        public const string FirstReturnsBandName = "firstReturns";
        public const string SecondReturnsBandName = "secondReturns";
    }

    public class ImageRaster<TPixel> : Raster where TPixel : struct, IMinMaxValue<TPixel>, INumber<TPixel>, IUnsignedNumber<TPixel>
    {
        // since ImageRasters are persistent processing objects and tiles near infrared content may vary, ImageRaster instances may
        // have memory allocated for an NIR band that's not in use for the current tile
        // The NIR band could be newed and nulled to match the tile but doing so would partially defeat the purpose of reusing
        // ImageRaster instances to offload the GC.
        private bool nearInfraredInUse;

        // imagery
        public RasterBand<TPixel> Red { get; private init; }
        public RasterBand<TPixel> Green { get; private init; }
        public RasterBand<TPixel> Blue { get; private init; }
        public RasterBand<TPixel>? NearInfrared { get; private set; }
        public RasterBand<TPixel> IntensityFirstReturn { get; private init; }
        public RasterBand<TPixel> IntensitySecondReturn { get; private init; }

        public RasterBand<float> ScanAngleMeanAbsolute { get; private init; }
        public RasterBand<TPixel> FirstReturns { get; private init; }
        public RasterBand<TPixel> SecondReturns { get; private init; }

        // constructor from point cloud tile and cell size for accumulation of mean point RGB[+NIR] intensity values in pixels
        public ImageRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, bool includeNearInfrared)
            : base(crs, transform, xSize, ySize)
        {
            this.nearInfraredInUse = includeNearInfrared;

            TPixel noDataValue = RasterBand<TPixel>.GetDefaultNoDataValue();
            this.Red = new(this, ImageRaster.RedBandName, noDataValue, RasterBandInitialValue.Default);
            this.Green = new(this, ImageRaster.GreenBandName, noDataValue, RasterBandInitialValue.Default);
            this.Blue = new(this, ImageRaster.BlueBandName, noDataValue, RasterBandInitialValue.Default);
            this.NearInfrared = includeNearInfrared ? new(this, ImageRaster.NearInfraredBandName, noDataValue, RasterBandInitialValue.Default) : null;
            this.IntensityFirstReturn = new(this, ImageRaster.IntensityFirstReturnBandName, noDataValue, RasterBandInitialValue.Default);
            this.IntensitySecondReturn = new(this, ImageRaster.IntensitySecondReturnBandName, noDataValue, RasterBandInitialValue.Default);

            this.ScanAngleMeanAbsolute = new(this, ImageRaster.ScanAngleMeanAbsoluteBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.Default);
            this.FirstReturns = new(this, ImageRaster.FirstReturnsBandName, RasterBandInitialValue.Default); // count of zero is valid: do not set a no data value
            this.SecondReturns = new(this, ImageRaster.SecondReturnsBandName, RasterBandInitialValue.Default);
        }

        //public void Add(LasFile lasFile, PointList<PointBatchXyirnRgbn> tilePoints)
        //{
        //    LasHeader10 lasHeader = lasFile.Header;
        //    double xOffset = lasHeader.XOffset;
        //    double xScale = lasHeader.XScaleFactor;
        //    double yOffset = lasHeader.YOffset;
        //    double yScale = lasHeader.YScaleFactor;

        //    for (int pointBatchIndex = 0; pointBatchIndex < tilePoints.Count; ++pointBatchIndex)
        //    {
        //        PointBatchXyirnRgbn pointBatch = tilePoints[pointBatchIndex];
        //        for (int pointIndex = 0; pointIndex < pointBatch.Count; ++pointIndex)
        //        {
        //            double x = xOffset + xScale * pointBatch.X[pointIndex];
        //            double y = yOffset + yScale * pointBatch.Y[pointIndex];
        //            (int xIndex, int yIndex) = this.ToGridIndices(x, y);
        //            if ((xIndex < 0) || (yIndex < 0) || (xIndex >= this.SizeX) || (yIndex >= this.SizeY))
        //            {
        //                // point lies outside of the tile and is therefore not of interest
        //                throw new NotSupportedException("Point at x = " + x + ", y = " + y + " lies outside image extents (" + this.GetExtentString() + ") and thus has off image indices " + xIndex + ", " + yIndex + ".");
        //            }

        //            int cellIndex = this.ToCellIndex(xIndex, yIndex);
        //            this.Red[cellIndex] += TPixel.CreateChecked(pointBatch.Red[pointIndex]);
        //            this.Green[cellIndex] += TPixel.CreateChecked(pointBatch.Red[pointIndex]);
        //            this.Blue[cellIndex] += TPixel.CreateChecked(pointBatch.Red[pointIndex]);

        //            TPixel intensity = TPixel.CreateChecked(pointBatch.Intensity[pointIndex]);
        //            byte returnNumber = pointBatch.ReturnNumber[pointIndex];
        //            if (returnNumber == 1)
        //            {
        //                // assume first returns are always the most representative => only first returns contribute to RGB+NIR
        //                ++this.FirstReturns[cellIndex];

        //                if (this.nearInfraredInUse)
        //                {
        //                    this.NearInfrared![cellIndex] += TPixel.CreateChecked(pointBatch.NearInfrared[pointIndex]);
        //                }

        //                this.IntensityFirstReturn[cellIndex] += intensity;
        //            }
        //            else if (returnNumber == 2)
        //            {
        //                ++this.SecondReturns[cellIndex];
        //                this.IntensitySecondReturn[cellIndex] += intensity;
        //            }

        //            float scanAngleInDegrees = pointBatch.ScanAngleInDegrees[pointIndex];
        //            if (scanAngleInDegrees < 0.0F)
        //            {
        //                scanAngleInDegrees = -scanAngleInDegrees;
        //            }
        //            this.MeanAbsoluteScanAngle[cellIndex] += scanAngleInDegrees;
        //        }
        //    }
        //}

        public static ImageRaster<TPixel> CreateRecreateOrReset(ImageRaster<TPixel>? image, SpatialReference lasTileCrs, LasTile lasTile, double cellSize, int sizeX, int sizeY, string filePath)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, cellSize, cellSize);
            bool useNearInfrared = lasTile.Header.PointsHaveNearInfrared;
            if ((image == null) || (image.SizeX != sizeX) || (image.SizeY != sizeY))
            {
                return new(lasTileCrs, lasTileTransform, sizeX, sizeY, useNearInfrared)
                {
                    FilePath = filePath
                };
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(image.Crs, lasTileCrs));
            image.FilePath = filePath;
            image.Transform.Copy(lasTileTransform);

            image.Red.Fill(default);
            image.Green.Fill(default);
            image.Blue.Fill(default);
            image.nearInfraredInUse = useNearInfrared;
            if (useNearInfrared)
            {
                image.NearInfrared ??= new(image, "nearInfrared", RasterBand<TPixel>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
                image.NearInfrared.Fill(default);
            }
            image.IntensityFirstReturn.Fill(default);
            image.IntensitySecondReturn.Fill(default);

            image.ScanAngleMeanAbsolute.Fill(default);
            image.FirstReturns.Fill(default);
            image.SecondReturns.Fill(default);
            return image;
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Red;
            yield return this.Green;
            yield return this.Blue;
            if (this.nearInfraredInUse)
            {
                Debug.Assert(this.NearInfrared != null);
                yield return this.NearInfrared;
            }

            yield return this.IntensityFirstReturn;
            yield return this.IntensitySecondReturn;
            yield return this.ScanAngleMeanAbsolute;

            yield return this.FirstReturns;
            yield return this.SecondReturns;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            List<RasterBandStatistics> bandStatistics = [ this.Red.GetStatistics(), this.Green.GetStatistics(), this.Blue.GetStatistics() ];
            if (this.nearInfraredInUse)
            {
                Debug.Assert(this.NearInfrared != null);
                bandStatistics.Add(this.NearInfrared.GetStatistics());
            }

            bandStatistics.Add(this.IntensityFirstReturn.GetStatistics());
            bandStatistics.Add(this.IntensitySecondReturn.GetStatistics());
            bandStatistics.Add(this.ScanAngleMeanAbsolute.GetStatistics());

            bandStatistics.Add(this.FirstReturns.GetStatistics());
            bandStatistics.Add(this.SecondReturns.GetStatistics());
            return bandStatistics;
        }

        /// <summary>
        /// Convert accumulated red, green, blue, NIR, first return intensity, and second return intensity sums to averages.
        /// </summary>
        public void OnPointAdditionComplete()
        {
            // all bands are of the same type and ones bearing no data should all have the same no data value
            float floatNoDataValue = this.ScanAngleMeanAbsolute.NoDataValue;
            TPixel integerNoDataValue = this.Red.NoDataValue;
            Debug.Assert((this.Green.NoDataValue == integerNoDataValue) && (this.Blue.NoDataValue == integerNoDataValue) && ((this.NearInfrared == null) || (this.NearInfrared.NoDataValue == integerNoDataValue)) && (this.IntensityFirstReturn.NoDataValue == integerNoDataValue) && (this.IntensitySecondReturn.NoDataValue == integerNoDataValue));

            for (int index = 0; index < this.Cells; ++index)
            {
                TPixel firstReturns = this.FirstReturns[index];
                if (firstReturns != TPixel.Zero)
                {
                    this.Red[index] /= firstReturns;
                    this.Green[index] /= firstReturns;
                    this.Blue[index] /= firstReturns;
                    if (this.nearInfraredInUse)
                    {
                        Debug.Assert(this.NearInfrared != null);
                        this.NearInfrared[index] /= firstReturns;
                    }
                    this.IntensityFirstReturn[index] /= firstReturns;
                }
                else
                {
                    this.Red[index] = integerNoDataValue;
                    this.Green[index] = integerNoDataValue;
                    this.Blue[index] = integerNoDataValue;
                    if (this.nearInfraredInUse)
                    {
                        Debug.Assert(this.NearInfrared != null);
                        this.NearInfrared[index] = integerNoDataValue;
                    }
                    this.IntensityFirstReturn[index] = integerNoDataValue;
                }

                TPixel secondReturns = this.SecondReturns[index];
                if (secondReturns != TPixel.Zero)
                {
                    this.IntensitySecondReturn[index] /= secondReturns;
                }
                else
                {
                    this.IntensitySecondReturn[index] = integerNoDataValue;
                }

                float firstAndSecondReturns = Single.CreateChecked(firstReturns + secondReturns);
                if (firstAndSecondReturns != 0.0F)
                {
                    this.ScanAngleMeanAbsolute[index] /= firstAndSecondReturns;
                }
                else
                {
                    this.ScanAngleMeanAbsolute[index] = floatNoDataValue;
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
                    case ImageRaster.RedBandName:
                        this.Red.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.GreenBandName:
                        this.Green.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.BlueBandName:
                        this.Blue.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.NearInfraredBandName:
                        if (this.nearInfraredInUse)
                        {
                            Debug.Assert(this.NearInfrared != null);
                            this.NearInfrared.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case ImageRaster.IntensityFirstReturnBandName:
                        this.IntensityFirstReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.IntensitySecondReturnBandName:
                        this.IntensitySecondReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.ScanAngleMeanAbsoluteBandName:
                        this.ScanAngleMeanAbsolute.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.FirstReturnsBandName:
                        this.FirstReturns.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case ImageRaster.SecondReturnsBandName:
                        this.SecondReturns.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in image raster '" + this.FilePath + "'.");
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
            this.Red.ReturnData(dataBufferPool);
            this.Green.ReturnData(dataBufferPool);
            this.Blue.ReturnData(dataBufferPool);
            this.NearInfrared?.ReturnData(dataBufferPool);
            this.IntensityFirstReturn?.ReturnData(dataBufferPool);
            this.IntensitySecondReturn?.ReturnData(dataBufferPool);
            this.ScanAngleMeanAbsolute?.ReturnData(dataBufferPool);
            this.FirstReturns?.ReturnData(dataBufferPool);
            this.SecondReturns?.ReturnData(dataBufferPool);
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || String.Equals(name, this.Red.Name, StringComparison.Ordinal))
            {
                band = this.Red;
            }
            else if (String.Equals(name, this.Green.Name, StringComparison.Ordinal))
            {
                band = this.Green;
            }
            else if (String.Equals(name, this.Blue.Name, StringComparison.Ordinal))
            {
                band = this.Blue;
            }
            else if (this.nearInfraredInUse && String.Equals(name, this.NearInfrared!.Name, StringComparison.Ordinal))
            {
                band = this.NearInfrared;
            }
            else if (String.Equals(name, this.IntensityFirstReturn.Name, StringComparison.Ordinal))
            {
                band = this.IntensityFirstReturn;
            }
            else if (String.Equals(name, this.IntensitySecondReturn.Name, StringComparison.Ordinal))
            {
                band = this.IntensitySecondReturn;
            }
            else if (String.Equals(name, this.ScanAngleMeanAbsolute.Name, StringComparison.Ordinal))
            {
                band = this.ScanAngleMeanAbsolute;
            }
            else if (String.Equals(name, this.FirstReturns.Name, StringComparison.Ordinal))
            {
                band = this.FirstReturns;
            }
            else if (String.Equals(name, this.SecondReturns.Name, StringComparison.Ordinal))
            {
                band = this.SecondReturns;
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
            if (String.Equals(name, this.Red.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
                return true;
            }
            if (String.Equals(name, this.Green.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 1;
                return true;
            }
            if (String.Equals(name, this.Blue.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 2;
                return true;
            }

            int nearInfraredOffset = 0;
            if (this.nearInfraredInUse)
            {
                Debug.Assert(this.NearInfrared != null);
                if (String.Equals(name, this.NearInfrared.Name, StringComparison.Ordinal))
                {
                    bandIndexInFile = 3;
                    return true;
                }

                nearInfraredOffset = 1;
            }

            if (String.Equals(name, this.IntensityFirstReturn.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 3 + nearInfraredOffset;
                return true;
            }
            if (String.Equals(name, this.IntensitySecondReturn.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 4 + nearInfraredOffset;
                return true;
            }

            if (String.Equals(name, this.ScanAngleMeanAbsolute.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 5 + nearInfraredOffset;
                return true;
            }

            if (String.Equals(name, this.FirstReturns.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 6 + nearInfraredOffset;
                return true;
            }
            if (String.Equals(name, this.SecondReturns.Name, StringComparison.Ordinal))
            {
                bandIndexInFile = 7 + nearInfraredOffset;
                return true;
            }

            bandIndexInFile = -1;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.Red.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.Green.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.Blue.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            if (this.nearInfraredInUse)
            {
                Debug.Assert(this.NearInfrared != null);
                this.NearInfrared.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            }
            this.IntensityFirstReturn?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensitySecondReturn?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ScanAngleMeanAbsolute?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.FirstReturns?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.SecondReturns?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string imagePath, bool compress)
        {
            this.Write(imagePath, 16, compress);
        }

        public void Write(string imagePath, int bitsPerRgbNirIntensityPixel, bool compress)
        {
            DataType gdalImagePixelType = bitsPerRgbNirIntensityPixel switch
            {
                16 => DataType.GDT_UInt16,
                32 => DataType.GDT_UInt32,
                64 => DataType.GDT_UInt64,
                _ => throw new ArgumentOutOfRangeException(nameof(bitsPerRgbNirIntensityPixel), bitsPerRgbNirIntensityPixel + " bits per pixel is not supported. 16, 32, and 64 bits per pixel are supported.")
            };

            int nearInfraredOffset = this.nearInfraredInUse ? 1 : 0;
            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(imagePath, bands: 5 + nearInfraredOffset, gdalImagePixelType, compress);
            this.Red.Write(dsmDataset, 1);
            this.Green.Write(dsmDataset, 2);
            this.Blue.Write(dsmDataset, 3);
            if (this.nearInfraredInUse) 
            {
                Debug.Assert(this.NearInfrared != null);
                this.NearInfrared.Write(dsmDataset, 4);
            }
            this.IntensityFirstReturn.Write(dsmDataset, 4 + nearInfraredOffset);
            this.IntensitySecondReturn.Write(dsmDataset, 5 + nearInfraredOffset);

            string scanAngleTilePath = Raster.GetComponentFilePath(imagePath, ImageRaster.DiagnosticDirectoryScanAngle, createDiagnosticDirectory: true);
            using Dataset scanAngleDataset = this.CreateGdalRasterAndSetFilePath(scanAngleTilePath, 1, DataType.GDT_Float32, compress);
            this.ScanAngleMeanAbsolute.Write(scanAngleDataset, 1);

            string pointCountTilePath = Raster.GetComponentFilePath(imagePath, ImageRaster.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
            DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(this.FirstReturns, this.SecondReturns);
            using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountTilePath, 2, pointCountBandType, compress);
            this.FirstReturns.Write(pointCountDataset, 1);
            this.SecondReturns.Write(pointCountDataset, 2);

            this.FilePath = imagePath;
        }
    }
}