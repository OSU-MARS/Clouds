using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// 60 band raster containing standard point cloud z and intensity metrics plus distribution of returns.
    /// </summary>
    public class GridMetricsRaster : Raster<float>
    {
        // always on bands
        public RasterBand<float> N { get; private init; }
        public RasterBand<float> ZMax { get; private init; }
        public RasterBand<float> ZMean { get; private init; }
        public RasterBand<float> ZGroundMean { get; private init; }
        public RasterBand<float> ZStandardDeviation { get; private init; }
        public RasterBand<float> ZSkew { get; private init; }
        public RasterBand<float> ZKurtosis { get; private init; }
        public RasterBand<float> ZNormalizedEntropy { get; private init; }
        public RasterBand<float> PZAboveZMean { get; private init; }
        public RasterBand<float> PZAboveThreshold { get; private init; }
        public RasterBand<float> ZQuantile05 { get; private init; }
        public RasterBand<float> ZQuantile10 { get; private init; }
        public RasterBand<float> ZQuantile15 { get; private init; }
        public RasterBand<float> ZQuantile20 { get; private init; }
        public RasterBand<float> ZQuantile25 { get; private init; }
        public RasterBand<float> ZQuantile30 { get; private init; }
        public RasterBand<float> ZQuantile35 { get; private init; }
        public RasterBand<float> ZQuantile40 { get; private init; }
        public RasterBand<float> ZQuantile45 { get; private init; }
        public RasterBand<float> ZQuantile50 { get; private init; }
        public RasterBand<float> ZQuantile55 { get; private init; }
        public RasterBand<float> ZQuantile60 { get; private init; }
        public RasterBand<float> ZQuantile65 { get; private init; }
        public RasterBand<float> ZQuantile70 { get; private init; }
        public RasterBand<float> ZQuantile75 { get; private init; }
        public RasterBand<float> ZQuantile80 { get; private init; }
        public RasterBand<float> ZQuantile85 { get; private init; }
        public RasterBand<float> ZQuantile90 { get; private init; }
        public RasterBand<float> ZQuantile95 { get; private init; }
        public RasterBand<float> ZPCumulative10 { get; private init; }
        public RasterBand<float> ZPCumulative20 { get; private init; }
        public RasterBand<float> ZPCumulative30 { get; private init; }
        public RasterBand<float> ZPCumulative40 { get; private init; }
        public RasterBand<float> ZPCumulative50 { get; private init; }
        public RasterBand<float> ZPCumulative60 { get; private init; }
        public RasterBand<float> ZPCumulative70 { get; private init; }
        public RasterBand<float> ZPCumulative80 { get; private init; }
        public RasterBand<float> ZPCumulative90 { get; private init; }
        public RasterBand<float> IntensityFirstReturn { get; private init; } // Alonzo et al. 2014 http://dx.doi.org/10.1016/j.rse.2014.03.018
        public RasterBand<float> IntensityMean { get; private init; }
        public RasterBand<float> IntensityMeanAboveMedianZ { get; private init; } // Alonzo et al.
        public RasterBand<float> IntensityMeanBelowMedianZ { get; private init; } // Alonzo et al.
        public RasterBand<float> IntensityMax { get; private init; }
        public RasterBand<float> IntensityStandardDeviation { get; private init; }
        public RasterBand<float> IntensitySkew { get; private init; }
        public RasterBand<float> IntensityKurtosis { get; private init; }
        public RasterBand<float> IntensityPGround { get; private init; }
        public RasterBand<float> PFirstReturn { get; private init; }
        public RasterBand<float> PSecondReturn { get; private init; }
        public RasterBand<float> PThirdReturn { get; private init; }
        public RasterBand<float> PFourthReturn { get; private init; }
        public RasterBand<float> PFifthReturn { get; private init; }
        public RasterBand<float> PGround { get; private init; }

        // optional bands
        public RasterBand<float>? IntensityPCumulativeZQ10 { get; private init; }
        public RasterBand<float>? IntensityPCumulativeZQ30 { get; private init; }
        public RasterBand<float>? IntensityPCumulativeZQ50 { get; private init; }
        public RasterBand<float>? IntensityPCumulativeZQ70 { get; private init; }
        public RasterBand<float>? IntensityPCumulativeZQ90 { get; private init; }
        public RasterBand<float>? IntensityTotal { get; private init; }
        public RasterBand<float>? AreaOfPointBoundingBox { get; private init; }

        public GridMetricsRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize, GridMetricsSettings settings)
            : base(crs, transform, xSize, ySize, GridMetricsRaster.GetBandCount(settings), Single.NaN)
        {
            int bandIndex = 0;

            // always present bands
            this.N = this.Bands[bandIndex++];
            this.ZMax = this.Bands[bandIndex++];
            this.ZMean = this.Bands[bandIndex++];
            this.ZGroundMean = this.Bands[bandIndex++];
            this.ZStandardDeviation = this.Bands[bandIndex++];
            this.ZSkew = this.Bands[bandIndex++];
            this.ZKurtosis = this.Bands[bandIndex++];
            this.ZNormalizedEntropy = this.Bands[bandIndex++];
            this.PZAboveZMean = this.Bands[bandIndex++];
            this.PZAboveThreshold = this.Bands[bandIndex++];
            this.ZQuantile05 = this.Bands[bandIndex++];
            this.ZQuantile10 = this.Bands[bandIndex++];
            this.ZQuantile15 = this.Bands[bandIndex++];
            this.ZQuantile20 = this.Bands[bandIndex++];
            this.ZQuantile25 = this.Bands[bandIndex++];
            this.ZQuantile30 = this.Bands[bandIndex++];
            this.ZQuantile35 = this.Bands[bandIndex++];
            this.ZQuantile40 = this.Bands[bandIndex++];
            this.ZQuantile45 = this.Bands[bandIndex++];
            this.ZQuantile50 = this.Bands[bandIndex++];
            this.ZQuantile55 = this.Bands[bandIndex++];
            this.ZQuantile60 = this.Bands[bandIndex++];
            this.ZQuantile65 = this.Bands[bandIndex++];
            this.ZQuantile70 = this.Bands[bandIndex++];
            this.ZQuantile75 = this.Bands[bandIndex++];
            this.ZQuantile80 = this.Bands[bandIndex++];
            this.ZQuantile85 = this.Bands[bandIndex++];
            this.ZQuantile90 = this.Bands[bandIndex++];
            this.ZQuantile95 = this.Bands[bandIndex++];
            this.ZPCumulative10 = this.Bands[bandIndex++];
            this.ZPCumulative20 = this.Bands[bandIndex++];
            this.ZPCumulative30 = this.Bands[bandIndex++];
            this.ZPCumulative40 = this.Bands[bandIndex++];
            this.ZPCumulative50 = this.Bands[bandIndex++];
            this.ZPCumulative60 = this.Bands[bandIndex++];
            this.ZPCumulative70 = this.Bands[bandIndex++];
            this.ZPCumulative80 = this.Bands[bandIndex++];
            this.ZPCumulative90 = this.Bands[bandIndex++];
            this.IntensityFirstReturn = this.Bands[bandIndex++]; // Alonzo et al. 2014 http://dx.doi.org/10.1016/j.rse.2014.03.018
            this.IntensityMean = this.Bands[bandIndex++];
            this.IntensityMeanAboveMedianZ = this.Bands[bandIndex++]; // Alonzo et al.
            this.IntensityMeanBelowMedianZ = this.Bands[bandIndex++]; // Alonzo et al.
            this.IntensityMax = this.Bands[bandIndex++];
            this.IntensityStandardDeviation = this.Bands[bandIndex++];
            this.IntensitySkew = this.Bands[bandIndex++];
            this.IntensityKurtosis = this.Bands[bandIndex++];
            this.IntensityPGround = this.Bands[bandIndex++];
            this.PFirstReturn = this.Bands[bandIndex++];
            this.PSecondReturn = this.Bands[bandIndex++];
            this.PThirdReturn = this.Bands[bandIndex++];
            this.PFourthReturn = this.Bands[bandIndex++];
            this.PFifthReturn = this.Bands[bandIndex++];
            this.PGround = this.Bands[bandIndex++];

            this.N.Name = "n";
            this.ZGroundMean.Name = "zGroundMean";
            this.ZMax.Name = "zMax";
            this.ZMean.Name = "zMean";
            this.ZStandardDeviation.Name = "zStdDev";
            this.ZSkew.Name = "zSkew";
            this.ZKurtosis.Name = "zKurtosis";
            this.ZNormalizedEntropy.Name = "zNormalizedEntropy";
            this.PZAboveZMean.Name = "pZaboveZmean";
            this.PZAboveThreshold.Name = "pZaboveThreshold";
            this.ZQuantile05.Name = "zQ05";
            this.ZQuantile10.Name = "zQ10";
            this.ZQuantile15.Name = "zQ15";
            this.ZQuantile20.Name = "zQ20";
            this.ZQuantile25.Name = "zQ25";
            this.ZQuantile30.Name = "zQ30";
            this.ZQuantile35.Name = "zQ35";
            this.ZQuantile40.Name = "zQ40";
            this.ZQuantile45.Name = "zQ45";
            this.ZQuantile50.Name = "zQ50";
            this.ZQuantile55.Name = "zQ55";
            this.ZQuantile60.Name = "zQ60";
            this.ZQuantile65.Name = "zQ65";
            this.ZQuantile70.Name = "zQ70";
            this.ZQuantile75.Name = "zQ75";
            this.ZQuantile80.Name = "zQ80";
            this.ZQuantile85.Name = "zQ85";
            this.ZQuantile90.Name = "zQ90";
            this.ZQuantile95.Name = "zQ95";
            this.ZPCumulative10.Name = "zPcumulative10";
            this.ZPCumulative20.Name = "zPcumulative20";
            this.ZPCumulative30.Name = "zPcumulative30";
            this.ZPCumulative40.Name = "zPcumulative40";
            this.ZPCumulative50.Name = "zPcumulative50";
            this.ZPCumulative60.Name = "zPcumulative60";
            this.ZPCumulative70.Name = "zPcumulative70";
            this.ZPCumulative80.Name = "zPcumulative80";
            this.ZPCumulative90.Name = "zPcumulative90";
            this.IntensityFirstReturn.Name = "intensityFirstReturn";
            this.IntensityMean.Name = "intensityMean";
            this.IntensityMeanAboveMedianZ.Name = "intensityMeanAboveMedianZ";
            this.IntensityMeanBelowMedianZ.Name = "intensityMeanBelowMedianZ";
            this.IntensityMax.Name = "intensityMax";
            this.IntensityStandardDeviation.Name = "intensityStdDev";
            this.IntensitySkew.Name = "intensitySkew";
            this.IntensityKurtosis.Name = "intensityKurtosis";
            this.IntensityPGround.Name = "intensityPground";
            this.PFirstReturn.Name = "pFirstReturn";
            this.PSecondReturn.Name = "pSecondReturn";
            this.PThirdReturn.Name = "pThirdReturn";
            this.PFourthReturn.Name = "pFourthReturn";
            this.PFifthReturn.Name = "pFifthReturn";
            this.PGround.Name = "pGround";

            // optional bands
            this.IntensityPCumulativeZQ10 = null;
            this.IntensityPCumulativeZQ30 = null;
            this.IntensityPCumulativeZQ50 = null;
            this.IntensityPCumulativeZQ70 = null;
            this.IntensityPCumulativeZQ90 = null;
            this.IntensityTotal = null;
            this.AreaOfPointBoundingBox = null;

            if (settings.IntensityPCumulativeZQ)
            {
                this.IntensityPCumulativeZQ10 = this.Bands[bandIndex++];
                this.IntensityPCumulativeZQ30 = this.Bands[bandIndex++];
                this.IntensityPCumulativeZQ50 = this.Bands[bandIndex++];
                this.IntensityPCumulativeZQ70 = this.Bands[bandIndex++];
                this.IntensityPCumulativeZQ90 = this.Bands[bandIndex++];

                this.IntensityPCumulativeZQ10.Name = "intensityPCumulativeZQ10";
                this.IntensityPCumulativeZQ30.Name = "intensityPCumulativeZQ30";
                this.IntensityPCumulativeZQ50.Name = "intensityPCumulativeZQ50";
                this.IntensityPCumulativeZQ70.Name = "intensityPCumulativeZQ70";
                this.IntensityPCumulativeZQ90.Name = "intensityPCumulativeZQ90";
            }
            if (settings.IntensityTotal)
            {
                this.IntensityTotal = this.Bands[bandIndex++];
                this.IntensityTotal.Name = "intensityTotal";
            }
            if (settings.PointBoundingArea)
            {
                this.AreaOfPointBoundingBox = this.Bands[bandIndex++];
                this.AreaOfPointBoundingBox.Name = "areaOfPointBoundingBox";
            }
        }

        private static int GetBandCount(GridMetricsSettings settings)
        {
            int bands = 59;
            if (settings.PointBoundingArea)
            {
                ++bands;
            }

            return bands;
        }

        public void SetMetrics(PointListZirnc cell, float heightClassSizeInCrsUnits, float zThresholdInCrsUnits)
        {
            int pointCount = cell.Count;
            int cellIndex = this.N.ToCellIndex(cell.XIndex, cell.YIndex); // all bands are the same size and therefore have the same indexing
            this.N[cellIndex] = pointCount;

            if (pointCount < 1)
            {
                return; // cell contains no points; all statistics but n are undefined
            }

            // z quantiles
            // For now, quantiles are obtained with direct lookups as most likely there are thousands to tens of thousands of points in an
            // ABA cell. If needed, interpolation can be included to estimate quantiles more precisely in cells with low point counts.
            float[] sortedZ = new float[pointCount];
            cell.Z.CopyTo(sortedZ);
            Array.Sort(sortedZ);

            float zQuantile10;
            float zQuantile30;
            float zQuantile50;
            float zQuantile70;
            float zQuantile90;
            if (pointCount < 20)
            {
                // if cell has less than 20 points then n * pointCount / 20 will be zero at least some of the time
                // Negative indices result in this case due to the subtraction of 1.
                this.ZQuantile05[cellIndex] = sortedZ[Int32.Max((int)(pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile10 = sortedZ[Int32.Max((int)(2.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile10[cellIndex] = zQuantile10;
                this.ZQuantile15[cellIndex] = sortedZ[Int32.Max((int)(3.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile20[cellIndex] = sortedZ[Int32.Max((int)(4.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile25[cellIndex] = sortedZ[Int32.Max((int)(5.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile30 = sortedZ[Int32.Max((int)(6.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile30[cellIndex] = zQuantile30;
                this.ZQuantile35[cellIndex] = sortedZ[Int32.Max((int)(7.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile40[cellIndex] = sortedZ[Int32.Max((int)(8.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile45[cellIndex] = sortedZ[Int32.Max((int)(9.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile50 = sortedZ[Int32.Max((int)(10.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile50[cellIndex] = zQuantile50;
                this.ZQuantile55[cellIndex] = sortedZ[Int32.Max((int)(11.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile60[cellIndex] = sortedZ[Int32.Max((int)(12.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile65[cellIndex] = sortedZ[Int32.Max((int)(13.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile70 = sortedZ[Int32.Max((int)(14.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile70[cellIndex] = zQuantile70;
                this.ZQuantile75[cellIndex] = sortedZ[Int32.Max((int)(15.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile80[cellIndex] = sortedZ[Int32.Max((int)(16.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile85[cellIndex] = sortedZ[Int32.Max((int)(17.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile90 = sortedZ[Int32.Max((int)(18.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile90[cellIndex] = zQuantile90;
                this.ZQuantile95[cellIndex] = sortedZ[Int32.Max((int)(19.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
            }
            else
            {
                this.ZQuantile05[cellIndex] = sortedZ[pointCount / 20 - 1];
                zQuantile10 = sortedZ[2 * pointCount / 20 - 1];
                this.ZQuantile10[cellIndex] = zQuantile10;
                this.ZQuantile15[cellIndex] = sortedZ[3 * pointCount / 20 - 1];
                this.ZQuantile20[cellIndex] = sortedZ[4 * pointCount / 20 - 1];
                this.ZQuantile25[cellIndex] = sortedZ[5 * pointCount / 20 - 1];
                zQuantile30 = sortedZ[6 * pointCount / 20 - 1];
                this.ZQuantile30[cellIndex] = zQuantile30;
                this.ZQuantile35[cellIndex] = sortedZ[7 * pointCount / 20 - 1];
                this.ZQuantile40[cellIndex] = sortedZ[8 * pointCount / 20 - 1];
                this.ZQuantile45[cellIndex] = sortedZ[9 * pointCount / 20 - 1];
                zQuantile50 = sortedZ[10 * pointCount / 20 - 1];
                this.ZQuantile50[cellIndex] = zQuantile50;
                this.ZQuantile55[cellIndex] = sortedZ[11 * pointCount / 20 - 1];
                this.ZQuantile60[cellIndex] = sortedZ[12 * pointCount / 20 - 1];
                this.ZQuantile65[cellIndex] = sortedZ[13 * pointCount / 20 - 1];
                zQuantile70 = sortedZ[14 * pointCount / 20 - 1];
                this.ZQuantile70[cellIndex] = zQuantile70;
                this.ZQuantile75[cellIndex] = sortedZ[15 * pointCount / 20 - 1];
                this.ZQuantile80[cellIndex] = sortedZ[16 * pointCount / 20 - 1];
                this.ZQuantile85[cellIndex] = sortedZ[17 * pointCount / 20 - 1];
                zQuantile90 = sortedZ[18 * pointCount / 20 - 1];
                this.ZQuantile90[cellIndex] = zQuantile90;
                this.ZQuantile95[cellIndex] = sortedZ[19 * pointCount / 20 - 1];
            }
            float zMax = sortedZ[^1];
            this.ZMax[cellIndex] = zMax;

            // combined z, intensity, and return statistics pass
            // single pass max, mean, standard deviation, skew, kurtosis for both z and intensity (https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance)
            // height class setup for normalized entropy calculation
            // probability point z is above z threshold
            // intensity by z
            float zMin = sortedZ[0];
            double zGroundSum = 0.0;
            double zSum = 0.0;
            double zSumSquared = 0.0;
            double zSumCubed = 0.0;
            double zSumFourthPower = 0.0;

            double zThreshold10 = zMin + 0.10 * (zMax - zMin);
            double zThreshold20 = zMin + 0.20 * (zMax - zMin);
            double zThreshold30 = zMin + 0.30 * (zMax - zMin);
            double zThreshold40 = zMin + 0.40 * (zMax - zMin);
            double zThreshold50 = zMin + 0.50 * (zMax - zMin);
            double zThreshold60 = zMin + 0.60 * (zMax - zMin);
            double zThreshold70 = zMin + 0.70 * (zMax - zMin);
            double zThreshold80 = zMin + 0.80 * (zMax - zMin);
            double zThreshold90 = zMin + 0.90 * (zMax - zMin);
            int pointsFromZ00to10 = 0;
            int pointsFromZ10to20 = 0;
            int pointsFromZ20to30 = 0;
            int pointsFromZ30to40 = 0;
            int pointsFromZ40to50 = 0;
            int pointsFromZ50to60 = 0;
            int pointsFromZ60to70 = 0;
            int pointsFromZ70to80 = 0;
            int pointsFromZ80to90 = 0;
            int heightClasses = (int)Double.Ceiling((zMax - zMin) / heightClassSizeInCrsUnits + 0.5) + 1; // one additional height class as a numeric guard
            int[] pointCountByHeightClass = new int[heightClasses]; // leave at default of zero

            long intensityFirstReturnSum = 0;
            long intensityAboveMedianSum = 0;
            long intensityBelowMedianSum = 0;
            long intensitySum = 0;
            double intensitySumSquared = 0.0; // keep intensity moment sums as doubles as (Int32 points) * UInt16^4 requires an Int96 to avoid overflow
            double intensitySumCubed = 0.0;
            double intensitySumFourthPower = 0.0;
            UInt16 intensityMax = UInt16.MinValue;

            int groundPoints = 0;
            long intensityGroundSum = 0;
            long intensitySumZ00to10 = 0;
            long intensitySumZ10to30 = 0;
            long intensitySumZ30to50 = 0;
            long intensitySumZ50to70 = 0;
            long intensitySumZ70to90 = 0;

            Span<int> pointsByReturnNumber = stackalloc int[6];
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                double z = cell.Z[pointIndex];
                zSum += z;
                double zPower = z * z;
                zSumSquared += zPower;
                zPower *= z;
                zSumCubed += zPower;
                zPower *= z;
                zSumFourthPower += zPower;

                if (z < zThreshold10)
                {
                    ++pointsFromZ00to10;
                }
                else if (z < zThreshold20)
                {
                    ++pointsFromZ10to20;
                }
                else if (z < zThreshold30)
                {
                    ++pointsFromZ20to30;
                }
                else if (z < zThreshold40)
                {
                    ++pointsFromZ30to40;
                }
                else if (z < zThreshold50)
                {
                    ++pointsFromZ40to50;
                }
                else if (z < zThreshold60)
                {
                    ++pointsFromZ50to60;
                }
                else if (z < zThreshold70)
                {
                    ++pointsFromZ60to70;
                }
                else if (z < zThreshold80)
                {
                    ++pointsFromZ70to80;
                }
                else if (z < zThreshold90)
                {
                    ++pointsFromZ80to90;
                }

                // numeric edge case: if division produces a fractional value of nnnn.5 then integer conversion leads to nnn + 1
                // Without the inclusion of an additional guard height class above an IndexOutOfRangeException results from incrementing
                // the height class's point count. There are at least two causes of this
                // 1) nnn.5 + 0.5 produces exactly nnn + 1 in the the single precision calculation of the number of height classes 
                // 2) conversion from float to double slightly increases z, leading the double precision calculation below to round up
                //    where single precision rounds down
                int heightClass = (int)((z - zMin) / heightClassSizeInCrsUnits + 0.5F);
                ++pointCountByHeightClass[heightClass];

                UInt16 intensity = cell.Intensity[pointIndex];
                intensitySum += intensity;
                double intensityAsDouble = intensity;
                double intensitySquare = intensityAsDouble * intensityAsDouble;
                intensitySumSquared += intensitySquare;
                intensitySumCubed += intensityAsDouble * intensitySquare;
                intensitySumFourthPower += intensitySquare * intensitySquare;

                int returnNumber = cell.ReturnNumber[pointIndex]; // may be zero in noncompliant .las files
                if (returnNumber < pointsByReturnNumber.Length)
                {
                    ++pointsByReturnNumber[returnNumber];
                }

                if (intensity > intensityMax)
                {
                    intensityMax = intensity;
                }
                if (returnNumber == 1)
                {
                    intensityFirstReturnSum += intensity;
                }

                if (z < zQuantile50)
                {
                    intensityBelowMedianSum += intensity;
                }
                else
                {
                    intensityAboveMedianSum += intensity;
                }

                if (z < zQuantile10)
                {
                    intensitySumZ00to10 += intensity;
                }
                else if (z < zQuantile30)
                {
                    intensitySumZ10to30 += intensity;
                }
                else if (z < zQuantile50)
                {
                    intensitySumZ30to50 += intensity;
                }
                else if (z < zQuantile70)
                {
                    intensitySumZ50to70 += intensity;
                }
                else if (z < zQuantile90)
                {
                    intensitySumZ70to90 += intensity;
                }

                PointClassification classification = cell.Classification[pointIndex];
                if (classification == PointClassification.Ground)
                {
                    intensityGroundSum += intensity;
                    zGroundSum += z;
                    ++groundPoints;
                }
            }

            float zGroundMean = groundPoints > 0 ? (float)(zGroundSum / groundPoints) : Single.NaN;
            this.ZGroundMean[cellIndex] = zGroundMean;

            double pointCountAsDouble = (double)pointCount;
            double zMean = zSum / pointCountAsDouble;
            this.ZMean[cellIndex] = (float)zMean;
            double zVariance = zSumSquared / pointCountAsDouble - zMean * zMean;
            double zStandardDeviation = Double.Sqrt(zVariance);
            this.ZStandardDeviation[cellIndex] = (float)zStandardDeviation;
            double zSkew = (zSumCubed - 3.0 * zSumSquared * zMean + 3.0 * zSum * zMean * zMean - pointCountAsDouble * zMean * zMean * zMean) / (zVariance * zStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            this.ZSkew[cellIndex] = (float)zSkew;
            // kurtosis is subject to numerical precision effects when calculated at double precision
            // Can be changed to Int128 if needed.
            double zKurtosis = (zSumFourthPower - 4.0 * zSumCubed * zMean + 6.0 * zSumSquared * zMean * zMean - 4.0 * zSum * zMean * zMean * zMean + pointCountAsDouble * zMean * zMean * zMean * zMean) / (zVariance * zVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
            this.ZKurtosis[cellIndex] = (float)zKurtosis;

            int pointsBelowZLevel = pointsFromZ00to10;
            this.ZPCumulative10[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ10to20;
            this.ZPCumulative20[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ20to30;
            this.ZPCumulative30[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ30to40;
            this.ZPCumulative40[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ40to50;
            this.ZPCumulative50[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ50to60;
            this.ZPCumulative60[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ60to70;
            this.ZPCumulative70[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ70to80;
            this.ZPCumulative80[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ80to90;
            this.ZPCumulative90[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);

            // entropy is of debatable value: sensitive to bin width and edge positioning
            // Entropy is also not well defined if there are no points or if there's only a single height class.
            double zEntropy = 0.0F;
            for (int heightClassIndex = 0; heightClassIndex < pointCountByHeightClass.Length; ++heightClassIndex)
            {
                int pointsInClass = pointCountByHeightClass[heightClassIndex];
                if (pointsInClass > 0)
                {
                    double classProbability = (double)pointsInClass / pointCountAsDouble;
                    zEntropy += classProbability * Double.Log(classProbability);
                }
            }
            this.ZNormalizedEntropy[cellIndex] = (float)(zEntropy / Double.Log(1.0 / pointCountByHeightClass.Length));

            this.IntensityMax[cellIndex] = intensityMax;
            double intensitySumAsDouble = intensitySum;
            double intensityMean = intensitySumAsDouble / pointCountAsDouble;
            this.IntensityMean[cellIndex] = (float)intensityMean;
            double intensityVariance = intensitySumSquared / pointCountAsDouble - intensityMean * intensityMean;
            double intensityStandardDeviation = Double.Sqrt(intensityVariance);
            this.IntensityStandardDeviation[cellIndex] = (float)intensityStandardDeviation;
            double intensitySkew = (intensitySumCubed - 3.0 * intensitySumSquared * intensityMean + 3.0 * intensitySum * intensityMean * intensityMean - pointCountAsDouble * intensityMean * intensityMean * intensityMean) / (intensityVariance * intensityStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            this.IntensitySkew[cellIndex] = (float)intensitySkew;
            double intensityKurtosis = (intensitySumFourthPower - 4.0 * intensitySumCubed * intensityMean + 6.0 * intensitySumSquared * intensityMean * intensityMean - 4.0 * intensitySum * intensityMean * intensityMean * intensityMean + pointCountAsDouble * intensityMean * intensityMean * intensityMean * intensityMean) / (intensityVariance * intensityVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
            this.IntensityKurtosis[cellIndex] = (float)intensityKurtosis;

            if (this.IntensityTotal != null)
            {
                this.IntensityTotal[cellIndex] = (float)intensitySum;
            }

            int pointsBelowMedian = pointsFromZ00to10 + pointsFromZ10to20 + pointsFromZ20to30 + pointsFromZ30to40 + pointsFromZ40to50;
            int pointsAboveMedian = pointCount - pointsBelowMedian; // no pointsFromZ90to100
            this.IntensityFirstReturn[cellIndex] = (float)intensityFirstReturnSum / (float)pointsByReturnNumber[1]; // NaN due to divide by zero if no first returns
            this.IntensityMeanAboveMedianZ[cellIndex] = (float)intensityAboveMedianSum / (float)pointsAboveMedian;
            this.IntensityMeanBelowMedianZ[cellIndex] = (float)intensityBelowMedianSum / (float)pointsBelowMedian;
            this.IntensityPGround[cellIndex] = (float)(intensityGroundSum / intensitySumAsDouble);

            if (this.IntensityPCumulativeZQ10 != null)
            {
                Debug.Assert((this.IntensityPCumulativeZQ30 != null) && (this.IntensityPCumulativeZQ50 != null) && (this.IntensityPCumulativeZQ70 != null) && (this.IntensityPCumulativeZQ90 != null));

                long cumulativeIntensityFractionFromZ0 = intensitySumZ00to10;
                this.IntensityPCumulativeZQ10[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensitySumAsDouble);
                cumulativeIntensityFractionFromZ0 += intensitySumZ10to30;
                this.IntensityPCumulativeZQ30[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensitySumAsDouble);
                cumulativeIntensityFractionFromZ0 += intensitySumZ30to50;
                this.IntensityPCumulativeZQ50[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensitySumAsDouble);
                cumulativeIntensityFractionFromZ0 += intensitySumZ50to70;
                this.IntensityPCumulativeZQ70[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensitySumAsDouble);
                cumulativeIntensityFractionFromZ0 += intensitySumZ70to90;
                this.IntensityPCumulativeZQ90[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensitySumAsDouble);
            }

            // second z statistics pass now that z means are known
            int pointsAboveZMean = 0;
            int pointsAboveZThreshold = 0;
            float zCountThreshold = zThresholdInCrsUnits;
            if (Single.IsNaN(zGroundMean) == false)
            {
                zCountThreshold += zGroundMean; // if ground points have been classified, make threshold relative to ground
            }
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                float z = cell.Z[pointIndex];
                if (z > zMean)
                {
                    ++pointsAboveZMean;
                }
                if (z > zCountThreshold)
                {
                    ++pointsAboveZThreshold;
                }
            }
            this.PZAboveZMean[cellIndex] = (float)(pointsAboveZMean / pointCountAsDouble);
            this.PZAboveThreshold[cellIndex] = (float)(pointsAboveZThreshold / pointCountAsDouble);

            this.PFirstReturn[cellIndex] = (float)(pointsByReturnNumber[1] / pointCountAsDouble);
            this.PSecondReturn[cellIndex] = (float)(pointsByReturnNumber[2] / pointCountAsDouble);
            this.PThirdReturn[cellIndex] = (float)(pointsByReturnNumber[3] / pointCountAsDouble);
            this.PFourthReturn[cellIndex] = (float)(pointsByReturnNumber[4] / pointCountAsDouble);
            this.PFifthReturn[cellIndex] = (float)(pointsByReturnNumber[5] / pointCountAsDouble);
            this.PGround[cellIndex] = (float)(groundPoints / pointCountAsDouble);

            if (this.AreaOfPointBoundingBox != null)
            {
                this.AreaOfPointBoundingBox[cellIndex] = (float)((cell.XMax - cell.XMin) * (cell.YMax - cell.YMin));
            }
        }
    }
}
