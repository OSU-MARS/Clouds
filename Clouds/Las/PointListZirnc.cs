using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// List of points with Z, intensity (I), return number (RN), and classification (C; ZIRNC).
    /// </summary>
    public class PointListZirnc
    {
        public int TilesLoaded { get; set; }
        public int TilesIntersected { get; private init; }
        public int XIndex { get; private init; }
        public int YIndex { get; private init; }

        public List<PointClassification> Classification { get; set; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }
        public double XMax { get; set; }
        public double XMin { get; set; }
        public double YMax { get; set; }
        public double YMin { get; set; }
        public List<float> Z { get; private init; }

        public PointListZirnc(int xIndex, int yIndex, int tilesIntersected)
        {
            this.TilesLoaded = 0;
            this.TilesIntersected = tilesIntersected;
            this.XIndex = xIndex;
            this.YIndex = yIndex;

            this.Classification = [];
            this.Intensity = [];
            this.ReturnNumber = [];
            this.XMax = Double.MinValue;
            this.XMin = Double.MaxValue;
            this.YMax = Double.MinValue;
            this.YMin = Double.MaxValue;
            this.Z = [];
        }

        public int Capacity
        {
            get
            {
                Debug.Assert((this.Intensity.Capacity == this.ReturnNumber.Capacity) && (this.Intensity.Capacity == this.Z.Capacity));
                return this.Intensity.Capacity;
            }
            set
            {
                this.Classification.Capacity = value;
                this.Intensity.Capacity = value;
                this.ReturnNumber.Capacity = value;
                this.Z.Capacity = value;
            }
        }

        public int Count
        {
            get 
            { 
                Debug.Assert((this.Intensity.Count == this.ReturnNumber.Count) && (this.Intensity.Count == this.Z.Count));
                return this.Intensity.Count; 
            }
        }

        public void ClearAndRelease()
        {
            this.TilesLoaded = 0;
            // no changes to this.TilesIntersected, XIndex, YIndex, XMax, XMin, YMax, YMin

            this.Classification.Clear();
            this.Classification.Capacity = 0;
            this.Intensity.Clear();
            this.Intensity.Capacity = 0;
            this.ReturnNumber.Clear();
            this.ReturnNumber.Capacity = 0;
            this.Z.Clear();
            this.Z.Capacity = 0;
        }

        public void GetStandardMetrics(StandardMetricsRaster abaMetrics, float heightClassSizeInCrsUnits, float zThresholdInCrsUnits)
        {
            int pointCount = this.Count;
            int cellIndex = abaMetrics.N.ToCellIndex(this.XIndex, this.YIndex); // all bands are the same size and therefore have the same indexing
            abaMetrics.N[cellIndex] = pointCount;

            if (pointCount < 1)
            {
                return; // cell contains no points; all statistics but n are undefined
            }

            abaMetrics.AreaOfPointBoundingBox[cellIndex] = (float)((this.XMax - this.XMin) * (this.YMax - this.YMin));

            // z quantiles
            // For now, quantiles are obtained with direct lookups as most likely there are thousands to tens of thousands of points in an
            // ABA cell. If needed, interpolation can be included to estimate quantiles more precisely in cells with low point counts.
            float[] sortedZ = new float[pointCount];
            this.Z.CopyTo(sortedZ);
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
                abaMetrics.ZQuantile05[cellIndex] = sortedZ[Int32.Max((int)(pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile10 = sortedZ[Int32.Max((int)(2.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile10[cellIndex] = zQuantile10;
                abaMetrics.ZQuantile15[cellIndex] = sortedZ[Int32.Max((int)(3.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile20[cellIndex] = sortedZ[Int32.Max((int)(4.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile25[cellIndex] = sortedZ[Int32.Max((int)(5.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile30 = sortedZ[Int32.Max((int)(6.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile30[cellIndex] = zQuantile30;
                abaMetrics.ZQuantile35[cellIndex] = sortedZ[Int32.Max((int)(7.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile40[cellIndex] = sortedZ[Int32.Max((int)(8.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile45[cellIndex] = sortedZ[Int32.Max((int)(9.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile50 = sortedZ[Int32.Max((int)(10.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile50[cellIndex] = zQuantile50;
                abaMetrics.ZQuantile55[cellIndex] = sortedZ[Int32.Max((int)(11.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile60[cellIndex] = sortedZ[Int32.Max((int)(12.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile65[cellIndex] = sortedZ[Int32.Max((int)(13.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile70 = sortedZ[Int32.Max((int)(14.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile70[cellIndex] = zQuantile70;
                abaMetrics.ZQuantile75[cellIndex] = sortedZ[Int32.Max((int)(15.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile80[cellIndex] = sortedZ[Int32.Max((int)(16.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile85[cellIndex] = sortedZ[Int32.Max((int)(17.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                zQuantile90 = sortedZ[Int32.Max((int)(18.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                abaMetrics.ZQuantile90[cellIndex] = zQuantile90;
                abaMetrics.ZQuantile95[cellIndex] = sortedZ[Int32.Max((int)(19.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
            }
            else
            {
                abaMetrics.ZQuantile05[cellIndex] = sortedZ[pointCount / 20 - 1];
                zQuantile10 = sortedZ[2 * pointCount / 20 - 1];
                abaMetrics.ZQuantile10[cellIndex] = zQuantile10;
                abaMetrics.ZQuantile15[cellIndex] = sortedZ[3 * pointCount / 20 - 1];
                abaMetrics.ZQuantile20[cellIndex] = sortedZ[4 * pointCount / 20 - 1];
                abaMetrics.ZQuantile25[cellIndex] = sortedZ[5 * pointCount / 20 - 1];
                zQuantile30 = sortedZ[6 * pointCount / 20 - 1];
                abaMetrics.ZQuantile30[cellIndex] = zQuantile30;
                abaMetrics.ZQuantile35[cellIndex] = sortedZ[7 * pointCount / 20 - 1];
                abaMetrics.ZQuantile40[cellIndex] = sortedZ[8 * pointCount / 20 - 1];
                abaMetrics.ZQuantile45[cellIndex] = sortedZ[9 * pointCount / 20 - 1];
                zQuantile50 = sortedZ[10 * pointCount / 20 - 1];
                abaMetrics.ZQuantile50[cellIndex] = zQuantile50;
                abaMetrics.ZQuantile55[cellIndex] = sortedZ[11 * pointCount / 20 - 1];
                abaMetrics.ZQuantile60[cellIndex] = sortedZ[12 * pointCount / 20 - 1];
                abaMetrics.ZQuantile65[cellIndex] = sortedZ[13 * pointCount / 20 - 1];
                zQuantile70 = sortedZ[14 * pointCount / 20 - 1];
                abaMetrics.ZQuantile70[cellIndex] = zQuantile70;
                abaMetrics.ZQuantile75[cellIndex] = sortedZ[15 * pointCount / 20 - 1];
                abaMetrics.ZQuantile80[cellIndex] = sortedZ[16 * pointCount / 20 - 1];
                abaMetrics.ZQuantile85[cellIndex] = sortedZ[17 * pointCount / 20 - 1];
                zQuantile90 = sortedZ[18 * pointCount / 20 - 1];
                abaMetrics.ZQuantile90[cellIndex] = zQuantile90;
                abaMetrics.ZQuantile95[cellIndex] = sortedZ[19 * pointCount / 20 - 1];
            }
            float zMax = sortedZ[^1];
            abaMetrics.ZMax[cellIndex] = zMax;

            // combined z and intensity statistics pass
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

            UInt16 intensityMax = UInt16.MinValue;
            double intensitySum = 0.0;
            double intensitySumSquared = 0.0;
            double intensitySumCubed = 0.0;
            double intensitySumFourthPower = 0.0; // keep intensity sums as doubles as (Int32 points) * UInt16^4 requires an Int96 to avoid overflow

            int groundPoints = 0;
            long intensityGroundSum = 0;
            long intensitySumZ00to10 = 0;
            long intensitySumZ10to30 = 0;
            long intensitySumZ30to50 = 0;
            long intensitySumZ50to70 = 0;
            long intensitySumZ70to90 = 0;
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                double z = this.Z[pointIndex];
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

                UInt16 intensity = this.Intensity[pointIndex];
                double intensityAsDouble = intensity;
                intensitySum += intensityAsDouble;
                double intensityPower = intensityAsDouble * intensityAsDouble;
                intensitySumSquared += intensityPower;
                intensityPower *= intensity;
                intensitySumCubed += intensityPower;
                intensityPower *= intensity;
                intensitySumFourthPower += intensityPower;

                if (intensity > intensityMax)
                {
                    intensityMax = intensity;
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

                PointClassification classification = this.Classification[pointIndex];
                if (classification == PointClassification.Ground)
                {
                    intensityGroundSum += intensity;
                    zGroundSum += z;
                    ++groundPoints;
                }
            }

            float zGroundMean = groundPoints > 0 ? (float)(zGroundSum / groundPoints) : Single.NaN;
            abaMetrics.ZGroundMean[cellIndex] = zGroundMean;

            double pointCountAsDouble = (double)pointCount;
            double zMean = zSum / pointCountAsDouble;
            abaMetrics.ZMean[cellIndex] = (float)zMean;
            double zVariance = zSumSquared / pointCountAsDouble - zMean * zMean;
            double zStandardDeviation = Double.Sqrt(zVariance);
            abaMetrics.ZStandardDeviation[cellIndex] = (float)zStandardDeviation;
            double zSkew = (zSumCubed - 3.0 * zSumSquared * zMean + 3.0 * zSum * zMean * zMean - pointCountAsDouble * zMean * zMean * zMean) / (zVariance * zStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            abaMetrics.ZSkew[cellIndex] = (float)zSkew;
            // kurtosis is subject to numerical precision effects when calculated at double precision
            // Can be changed to Int128 if needed.
            double zKurtosis = (zSumFourthPower - 4.0 * zSumCubed * zMean + 6.0 * zSumSquared * zMean * zMean - 4.0 * zSum * zMean * zMean * zMean + pointCountAsDouble * zMean * zMean * zMean * zMean) / (zVariance * zVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
            abaMetrics.ZKurtosis[cellIndex] = (float)zKurtosis;

            int pointsBelowZLevel = pointsFromZ00to10;
            abaMetrics.ZPCumulative10[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ10to20;
            abaMetrics.ZPCumulative20[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ20to30;
            abaMetrics.ZPCumulative30[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ30to40;
            abaMetrics.ZPCumulative40[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ40to50;
            abaMetrics.ZPCumulative50[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ50to60;
            abaMetrics.ZPCumulative60[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ60to70;
            abaMetrics.ZPCumulative70[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ70to80;
            abaMetrics.ZPCumulative80[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);
            pointsBelowZLevel += pointsFromZ80to90;
            abaMetrics.ZPCumulative90[cellIndex] = (float)(pointsBelowZLevel / pointCountAsDouble);

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
            abaMetrics.ZNormalizedEntropy[cellIndex] = (float)(zEntropy / Double.Log(1.0 / pointCountByHeightClass.Length));

            abaMetrics.IntensityMax[cellIndex] = intensityMax;
            double intensityTotalAsDouble = (double)intensitySum;
            abaMetrics.IntensityTotal[cellIndex] = (float)intensityTotalAsDouble;
            double intensityMean = intensityTotalAsDouble / pointCountAsDouble;
            abaMetrics.IntensityMean[cellIndex] = (float)intensityMean;
            double intensityVariance = intensitySumSquared / pointCountAsDouble - intensityMean * intensityMean;
            double intensityStandardDeviation = Double.Sqrt(intensityVariance);
            abaMetrics.IntensityStandardDeviation[cellIndex] = (float)intensityStandardDeviation;
            double intensitySkew = (intensitySumCubed - 3.0 * intensitySumSquared * intensityMean + 3.0 * intensitySum * intensityMean * intensityMean - pointCountAsDouble * intensityMean * intensityMean * intensityMean) / (intensityVariance * intensityStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            abaMetrics.IntensitySkew[cellIndex] = (float)intensitySkew;
            double intensityKurtosis = (intensitySumFourthPower - 4.0 * intensitySumCubed * intensityMean + 6.0 * intensitySumSquared * intensityMean * intensityMean - 4.0 * intensitySum * intensityMean * intensityMean * intensityMean + pointCountAsDouble * intensityMean * intensityMean * intensityMean * intensityMean) / (intensityVariance * intensityVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
            abaMetrics.IntensityKurtosis[cellIndex] = (float)intensityKurtosis;
            abaMetrics.IntensityPGround[cellIndex] = (float)(intensityGroundSum / intensityTotalAsDouble);

            long cumulativeIntensityFractionFromZ0 = intensitySumZ00to10;
            abaMetrics.IntensityPCumulativeZQ10[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensityTotalAsDouble);
            cumulativeIntensityFractionFromZ0 += intensitySumZ10to30;
            abaMetrics.IntensityPCumulativeZQ30[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensityTotalAsDouble);
            cumulativeIntensityFractionFromZ0 += intensitySumZ30to50;
            abaMetrics.IntensityPCumulativeZQ50[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensityTotalAsDouble);
            cumulativeIntensityFractionFromZ0 += intensitySumZ50to70;
            abaMetrics.IntensityPCumulativeZQ70[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensityTotalAsDouble);
            cumulativeIntensityFractionFromZ0 += intensitySumZ70to90;
            abaMetrics.IntensityPCumulativeZQ90[cellIndex] = (float)(cumulativeIntensityFractionFromZ0 / intensityTotalAsDouble);

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
                float z = this.Z[pointIndex];
                if (z > zMean)
                {
                    ++pointsAboveZMean;
                }
                if (z > zCountThreshold)
                {
                    ++pointsAboveZThreshold;
                }
            }
            abaMetrics.PZAboveZMean[cellIndex] = (float)(pointsAboveZMean / pointCountAsDouble);
            abaMetrics.PZAboveThreshold[cellIndex] = (float)(pointsAboveZThreshold / pointCountAsDouble);

            // return number statistics
            Span<int> pointsByReturnNumber = stackalloc int[6];
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                int returnNumber = this.ReturnNumber[pointIndex];
                if (returnNumber < pointsByReturnNumber.Length)
                {
                    ++pointsByReturnNumber[returnNumber];
                }
            }

            abaMetrics.PFirstReturn[cellIndex] = (float)(pointsByReturnNumber[1] / pointCountAsDouble);
            abaMetrics.PSecondReturn[cellIndex] = (float)(pointsByReturnNumber[2] / pointCountAsDouble);
            abaMetrics.PThirdReturn[cellIndex] = (float)(pointsByReturnNumber[3] / pointCountAsDouble);
            abaMetrics.PFourthReturn[cellIndex] = (float)(pointsByReturnNumber[4] / pointCountAsDouble);
            abaMetrics.PFifthReturn[cellIndex] = (float)(pointsByReturnNumber[5] / pointCountAsDouble);
            abaMetrics.PGround[cellIndex] = (float)(groundPoints / pointCountAsDouble);
        }
    }
}
