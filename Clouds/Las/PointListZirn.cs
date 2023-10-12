using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mars.Clouds.Las
{
    public class PointListZirn
    {
        public List<PointClassification> Classification { get; set; }
        public List<UInt16> Intensity { get; private init; }
        public List<byte> ReturnNumber { get; private init; }
        public double XMax { get; set; }
        public double XMin { get; set; }
        public double YMax { get; set; }
        public double YMin { get; set; }
        public List<float> Z { get; private init; }

        public PointListZirn() 
        {
            this.Classification = new();
            this.Intensity = new();
            this.ReturnNumber = new();
            this.XMax = Double.MinValue;
            this.XMin = Double.MaxValue;
            this.YMax = Double.MinValue;
            this.YMin = Double.MaxValue;
            this.Z = new();
        }

        public int Count
        {
            get 
            { 
                Debug.Assert((this.Intensity.Count == this.ReturnNumber.Count) && (this.Intensity.Count == this.Z.Count));
                return this.Intensity.Count; 
            }
        }

        public void GetStandardMetrics(StandardMetricsRaster abaMetrics, float heightClassSizeInCrsUnits, float zThresholdInCrsUnits, int xIndex, int yIndex)
        {
            int pointCount = this.Count;
            if (pointCount < 1)
            {
                throw new NotSupportedException("Cell has no metrics as no points have been loaded into it (x index in ABA raster = " + xIndex + ", y index = " + yIndex + ").");
            }

            int cellIndex = abaMetrics.N.ToCellIndex(xIndex, yIndex); // all bands are the same size and therefore have the same indexing
            abaMetrics.AreaOfPointBoundingBox[cellIndex] = (float)((this.XMax - this.XMin) * (this.YMax - this.YMin));
            abaMetrics.N[cellIndex] = pointCount;

            // z quantiles
            float[] sortedZ = new float[pointCount];
            this.Z.CopyTo(sortedZ);
            Array.Sort(sortedZ);
            abaMetrics.ZQuantile05[cellIndex] = sortedZ[pointCount / 20 - 1];
            float zQuantile10 = sortedZ[2 * pointCount / 20 - 1];
            abaMetrics.ZQuantile10[cellIndex] = zQuantile10;
            abaMetrics.ZQuantile15[cellIndex] = sortedZ[3 * pointCount / 20 - 1];
            abaMetrics.ZQuantile20[cellIndex] = sortedZ[4 * pointCount / 20 - 1];
            abaMetrics.ZQuantile25[cellIndex] = sortedZ[5 * pointCount / 20 - 1];
            float zQuantile30 = sortedZ[6 * pointCount / 20 - 1];
            abaMetrics.ZQuantile30[cellIndex] = zQuantile30;
            abaMetrics.ZQuantile35[cellIndex] = sortedZ[7 * pointCount / 20 - 1];
            abaMetrics.ZQuantile40[cellIndex] = sortedZ[8 * pointCount / 20 - 1];
            abaMetrics.ZQuantile45[cellIndex] = sortedZ[9 * pointCount / 20 - 1];
            float zQuantile50 = sortedZ[10 * pointCount / 20 - 1];
            abaMetrics.ZQuantile50[cellIndex] = zQuantile50;
            abaMetrics.ZQuantile55[cellIndex] = sortedZ[11 * pointCount / 20 - 1];
            abaMetrics.ZQuantile60[cellIndex] = sortedZ[12 * pointCount / 20 - 1];
            abaMetrics.ZQuantile65[cellIndex] = sortedZ[13 * pointCount / 20 - 1];
            float zQuantile70 = sortedZ[14 * pointCount / 20 - 1];
            abaMetrics.ZQuantile70[cellIndex] = zQuantile70;
            abaMetrics.ZQuantile75[cellIndex] = sortedZ[15 * pointCount / 20 - 1];
            abaMetrics.ZQuantile80[cellIndex] = sortedZ[16 * pointCount / 20 - 1];
            abaMetrics.ZQuantile85[cellIndex] = sortedZ[17 * pointCount / 20 - 1];
            float zQuantile90 = sortedZ[18 * pointCount / 20 - 1];
            abaMetrics.ZQuantile90[cellIndex] = zQuantile90;
            abaMetrics.ZQuantile95[cellIndex] = sortedZ[19 * pointCount / 20 - 1];
            float zMax = sortedZ[^1];
            abaMetrics.ZMax[cellIndex] = zMax;

            // combined z and intensity statistics pass
            // single pass max, mean, standard deviation, skew, kurtosis for both z and intensity (https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance)
            // height class setup for normalized entropy calculation
            // probability point z is above z threshold
            // intensity by z
            float zMin = sortedZ[0];
            double zSum = 0.0;
            double zSumSquared = 0.0;
            double zSumCubed = 0.0;
            double zSumFourthPower = 0.0;

            float zThreshold10 = zMin + 0.10F * (zMax - zMin);
            float zThreshold20 = zMin + 0.20F * (zMax - zMin);
            float zThreshold30 = zMin + 0.30F * (zMax - zMin);
            float zThreshold40 = zMin + 0.40F * (zMax - zMin);
            float zThreshold50 = zMin + 0.50F * (zMax - zMin);
            float zThreshold60 = zMin + 0.60F * (zMax - zMin);
            float zThreshold70 = zMin + 0.70F * (zMax - zMin);
            float zThreshold80 = zMin + 0.80F * (zMax - zMin);
            float zThreshold90 = zMin + 0.90F * (zMax - zMin);
            int pointsFromZ00to10 = 0;
            int pointsFromZ10to20 = 0;
            int pointsFromZ20to30 = 0;
            int pointsFromZ30to40 = 0;
            int pointsFromZ40to50 = 0;
            int pointsFromZ50to60 = 0;
            int pointsFromZ60to70 = 0;
            int pointsFromZ70to80 = 0;
            int pointsFromZ80to90 = 0;
            int pointsAboveZThreshold = 0;
            int heightClasses = (int)Single.Ceiling((zMax - zMin) / heightClassSizeInCrsUnits + 0.5F);
            int[] pointCountByHeightClass = new int[heightClasses]; // leave at default of zero

            UInt16 intensityMax = UInt16.MinValue;
            double intensitySum = 0.0; // keep intensity sums as doubles as (Int32 points) * UInt16^4 requires an Int96 to avoid overflow
            double intensitySumSquared = 0.0;
            double intensitySumCubed = 0.0;
            double intensitySumFourthPower = 0.0;

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

                if (z > zThresholdInCrsUnits)
                {
                    ++pointsAboveZThreshold;
                }
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
                    ++groundPoints;
                }
            }

            double pointCountAsDouble = (double)pointCount;
            double zMean = zSum / pointCountAsDouble;
            abaMetrics.ZMean[cellIndex] = (float)zMean;
            double zVariance = zSumSquared / pointCountAsDouble - zMean * zMean;
            double zStandardDeviation = Double.Sqrt(zVariance);
            abaMetrics.ZStandardDeviation[cellIndex] = (float)zStandardDeviation;
            double zSkew = (zSumCubed - 3.0 * zSumSquared * zMean + 3.0 * zSum * zMean * zMean - pointCountAsDouble * zMean * zMean * zMean) / (zVariance * zStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            abaMetrics.ZSkew[cellIndex] = (float)zSkew;
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

            abaMetrics.PZAboveThreshold[cellIndex] = (float)(pointsAboveZThreshold / pointCountAsDouble);

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

            // second z statistics pass now that z mean is known
            int pointsAboveZMean = 0;
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                float z = this.Z[pointIndex];
                if (z > zMean)
                {
                    ++pointsAboveZMean;
                }
            }
            abaMetrics.PZAboveZMean[cellIndex] = (float)(pointsAboveZMean / pointCountAsDouble);

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
