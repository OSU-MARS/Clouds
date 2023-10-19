using Mars.Clouds.GdalExtensions;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// 56 band raster containing standard ABA point cloud metrics of z and intensity statistics with distribution of returns.
    /// </summary>
    public class StandardMetricsRaster : Raster<float>
    {
        public StandardMetricsRaster(SpatialReference crs, GridGeoTransform transform, int xSize, int ySize)
            : base(crs, transform, xSize, ySize, 56)
        {
            for (int bandIndex = 0; bandIndex < this.Bands.Length; ++bandIndex)
            {
                RasterBand<float> band = this.Bands[bandIndex];
                band.SetNoDataValue(Single.NaN);
            }
            Array.Fill(this.Data, Single.NaN);

            this.AreaOfPointBoundingBox.Name = "areaOfPointBoundingBox";
            this.N.Name = "n";
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
            this.IntensityTotal.Name = "intensityTotal";
            this.IntensityMax.Name = "intensityMax";
            this.IntensityMean.Name = "intensityMean";
            this.IntensityStandardDeviation.Name = "intensityStdDev";
            this.IntensitySkew.Name = "intensitySkew";
            this.IntensityKurtosis.Name = "intensityKurtosis";
            this.IntensityPGround.Name = "pGround";
            this.IntensityPCumulativeZQ10.Name = "pCumulativeZQ10";
            this.IntensityPCumulativeZQ30.Name = "pCumulativeZQ30";
            this.IntensityPCumulativeZQ50.Name = "pCumulativeZQ50";
            this.IntensityPCumulativeZQ70.Name = "pCumulativeZQ70";
            this.IntensityPCumulativeZQ90.Name = "pCumulativeZQ90";
            this.PFirstReturn.Name = "pFirstReturn";
            this.PSecondReturn.Name = "pSecondReturn";
            this.PThirdReturn.Name = "pThirdReturn";
            this.PFourthReturn.Name = "pFourthReturn";
            this.PFifthReturn.Name = "pFifthReturn";
            this.PGround.Name = "pGround";
        }

        public RasterBand<float> AreaOfPointBoundingBox { get { return this.Bands[0]; } }
        public RasterBand<float> N { get { return this.Bands[1]; } }
        public RasterBand<float> ZMax { get { return this.Bands[2]; } }
        public RasterBand<float> ZMean { get { return this.Bands[3]; } }
        public RasterBand<float> ZStandardDeviation { get { return this.Bands[4]; } }
        public RasterBand<float> ZSkew { get { return this.Bands[5]; } }
        public RasterBand<float> ZKurtosis { get { return this.Bands[6]; } }
        public RasterBand<float> ZNormalizedEntropy { get { return this.Bands[7]; } }
        public RasterBand<float> PZAboveZMean { get { return this.Bands[8]; } }
        public RasterBand<float> PZAboveThreshold { get { return this.Bands[9]; } }
        public RasterBand<float> ZQuantile05 { get { return this.Bands[10]; } }
        public RasterBand<float> ZQuantile10 { get { return this.Bands[11]; } }
        public RasterBand<float> ZQuantile15 { get { return this.Bands[12]; } }
        public RasterBand<float> ZQuantile20 { get { return this.Bands[13]; } }
        public RasterBand<float> ZQuantile25 { get { return this.Bands[14]; } }
        public RasterBand<float> ZQuantile30 { get { return this.Bands[15]; } }
        public RasterBand<float> ZQuantile35 { get { return this.Bands[16]; } }
        public RasterBand<float> ZQuantile40 { get { return this.Bands[17]; } }
        public RasterBand<float> ZQuantile45 { get { return this.Bands[18]; } }
        public RasterBand<float> ZQuantile50 { get { return this.Bands[19]; } }
        public RasterBand<float> ZQuantile55 { get { return this.Bands[20]; } }
        public RasterBand<float> ZQuantile60 { get { return this.Bands[21]; } }
        public RasterBand<float> ZQuantile65 { get { return this.Bands[22]; } }
        public RasterBand<float> ZQuantile70 { get { return this.Bands[23]; } }
        public RasterBand<float> ZQuantile75 { get { return this.Bands[24]; } }
        public RasterBand<float> ZQuantile80 { get { return this.Bands[25]; } }
        public RasterBand<float> ZQuantile85 { get { return this.Bands[26]; } }
        public RasterBand<float> ZQuantile90 { get { return this.Bands[27]; } }
        public RasterBand<float> ZQuantile95 { get { return this.Bands[28]; } }
        public RasterBand<float> ZPCumulative10 { get { return this.Bands[29]; } }
        public RasterBand<float> ZPCumulative20 { get { return this.Bands[30]; } }
        public RasterBand<float> ZPCumulative30 { get { return this.Bands[31]; } }
        public RasterBand<float> ZPCumulative40 { get { return this.Bands[32]; } }
        public RasterBand<float> ZPCumulative50 { get { return this.Bands[33]; } }
        public RasterBand<float> ZPCumulative60 { get { return this.Bands[34]; } }
        public RasterBand<float> ZPCumulative70 { get { return this.Bands[35]; } }
        public RasterBand<float> ZPCumulative80 { get { return this.Bands[36]; } }
        public RasterBand<float> ZPCumulative90 { get { return this.Bands[37]; } }
        public RasterBand<float> IntensityTotal { get { return this.Bands[38]; } }
        public RasterBand<float> IntensityMax { get { return this.Bands[39]; } }
        public RasterBand<float> IntensityMean { get { return this.Bands[40]; } }
        public RasterBand<float> IntensityStandardDeviation { get { return this.Bands[41]; } }
        public RasterBand<float> IntensitySkew { get { return this.Bands[42]; } }
        public RasterBand<float> IntensityKurtosis { get { return this.Bands[43]; } }
        public RasterBand<float> IntensityPGround { get { return this.Bands[44]; } }
        public RasterBand<float> IntensityPCumulativeZQ10 { get { return this.Bands[45]; } }
        public RasterBand<float> IntensityPCumulativeZQ30 { get { return this.Bands[46]; } }
        public RasterBand<float> IntensityPCumulativeZQ50 { get { return this.Bands[47]; } }
        public RasterBand<float> IntensityPCumulativeZQ70 { get { return this.Bands[48]; } }
        public RasterBand<float> IntensityPCumulativeZQ90 { get { return this.Bands[49]; } }
        public RasterBand<float> PFirstReturn { get { return this.Bands[50]; } }
        public RasterBand<float> PSecondReturn { get { return this.Bands[51]; } }
        public RasterBand<float> PThirdReturn { get { return this.Bands[52]; } }
        public RasterBand<float> PFourthReturn { get { return this.Bands[53]; } }
        public RasterBand<float> PFifthReturn { get { return this.Bands[54]; } }
        public RasterBand<float> PGround { get { return this.Bands[55]; } }
    }
}
