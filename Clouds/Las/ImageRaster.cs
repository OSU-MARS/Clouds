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
    public class ImageRaster<TPixel> : Raster where TPixel : IMinMaxValue<TPixel>, INumber<TPixel>, IUnsignedNumber<TPixel>
    {
        private const string DiagnosticDirectoryPointCounts = "nPoints";
        private const string DiagnosticDirectoryScanAngle = "scanAngle";

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
            this.Red = new(this, "red", noDataValue, RasterBandInitialValue.Default);
            this.Green = new(this, "green", noDataValue, RasterBandInitialValue.Default);
            this.Blue = new(this, "blue", noDataValue, RasterBandInitialValue.Default);
            this.NearInfrared = includeNearInfrared ? new(this, "nearInfrared", noDataValue, RasterBandInitialValue.Default) : null;
            this.IntensityFirstReturn = new(this, "intensityFirstReturn", noDataValue, RasterBandInitialValue.Default);
            this.IntensitySecondReturn = new(this, "intensitySecondReturn", noDataValue, RasterBandInitialValue.Default);

            this.ScanAngleMeanAbsolute = new(this, "scanAngleMeanAbsolute", RasterBand.NoDataDefaultFloat, RasterBandInitialValue.Default);
            this.FirstReturns = new(this, "firstReturns", RasterBandInitialValue.Default); // count of zero is valid: do not set a no data value
            this.SecondReturns = new(this, "secondReturns", RasterBandInitialValue.Default);
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

        public static ImageRaster<TPixel> CreateRecreateOrReset(ImageRaster<TPixel>? image, SpatialReference lasTileCrs, LasTile lasTile, double cellSize, int sizeX, int sizeY)
        {
            GridGeoTransform lasTileTransform = new(lasTile.GridExtent, cellSize, cellSize);
            bool useNearInfrared = lasTile.Header.PointsHaveNearInfrared;
            if ((image == null) || (image.SizeX != sizeX) || (image.SizeY != sizeY))
            {
                return new(lasTileCrs, lasTileTransform, sizeX, sizeY, useNearInfrared);
            }

            Debug.Assert(SpatialReferenceExtensions.IsSameCrs(image.Crs, lasTileCrs));
            image.Transform.Copy(lasTileTransform);

            Array.Fill(image.Red.Data, default);
            Array.Fill(image.Green.Data, default);
            Array.Fill(image.Blue.Data, default);
            image.nearInfraredInUse = useNearInfrared;
            if (useNearInfrared)
            {
                image.NearInfrared ??= new(image, "nearInfrared", RasterBand<TPixel>.GetDefaultNoDataValue(), RasterBandInitialValue.Default);
                Array.Fill(image.NearInfrared.Data, default);
            }
            Array.Fill(image.IntensityFirstReturn.Data, default);
            Array.Fill(image.IntensitySecondReturn.Data, default);

            Array.Fill(image.ScanAngleMeanAbsolute.Data, default);
            Array.Fill(image.FirstReturns.Data, default);
            Array.Fill(image.SecondReturns.Data, default);
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

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.Red.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.Green.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(name, this.Blue.Name, StringComparison.Ordinal))
            {
                return 2;
            }

            int nearInfraredOffset = 0;
            if (this.nearInfraredInUse)
            {
                Debug.Assert(this.NearInfrared != null);
                if (String.Equals(name, this.NearInfrared.Name, StringComparison.Ordinal))
                {
                    return 3;
                }

                nearInfraredOffset = 1;
            }

            if (String.Equals(name, this.IntensityFirstReturn.Name, StringComparison.Ordinal))
            {
                return 3 + nearInfraredOffset;
            }
            if (String.Equals(name, this.IntensitySecondReturn.Name, StringComparison.Ordinal))
            {
                return 4 + nearInfraredOffset;
            }

            if (String.Equals(name, this.ScanAngleMeanAbsolute.Name, StringComparison.Ordinal))
            {
                return 5 + nearInfraredOffset;
            }

            if (String.Equals(name, this.FirstReturns.Name, StringComparison.Ordinal))
            {
                return 6 + nearInfraredOffset;
            }
            if (String.Equals(name, this.SecondReturns.Name, StringComparison.Ordinal))
            {
                return 7 + nearInfraredOffset;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
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

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
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

            string scanAngleTilePath = Raster.GetDiagnosticFilePath(imagePath, ImageRaster<TPixel>.DiagnosticDirectoryScanAngle, createDiagnosticDirectory: true);
            using Dataset scanAngleDataset = this.CreateGdalRasterAndSetFilePath(scanAngleTilePath, 1, DataType.GDT_Float32, compress);
            this.ScanAngleMeanAbsolute.Write(scanAngleDataset, 1);

            string pointCountTilePath = Raster.GetDiagnosticFilePath(imagePath, ImageRaster<TPixel>.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
            DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(this.FirstReturns, this.SecondReturns);
            using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountTilePath, 2, pointCountBandType, compress);
            this.FirstReturns.Write(pointCountDataset, 1);
            this.SecondReturns.Write(pointCountDataset, 2);

            this.FilePath = imagePath;
        }
    }
}