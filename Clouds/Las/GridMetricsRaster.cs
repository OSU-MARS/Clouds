using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    /// <summary>
    /// 40+ band raster containing standard point cloud z and intensity metrics plus distribution of returns.
    /// </summary>
    public class GridMetricsRaster : Raster
    {
        #region band names
        public const string AcceptedPointsBandName = "acceptedPoints";
        public const string ZMaxBandName = "zMax";
        public const string ZMeanBandName = "zMean";
        public const string ZGroundMeanBandName = "zGroundMean";
        public const string ZStandardDeviationBandName = "zStdDev";
        public const string ZSkewBandName = "zSkew";
        public const string ZNormalizedEntropyBandName = "zNormalizedEntropy";
        public const string PZAboveZMeanBandName = "pZaboveZmean";
        public const string PZAboveThresholdBandName = "pZaboveThreshold";
        public const string ZQuantile05BandName = "zQ05";
        public const string ZQuantile10BandName = "zQ10";
        public const string ZQuantile15BandName = "zQ15";
        public const string ZQuantile20BandName = "zQ20";
        public const string ZQuantile25BandName = "zQ25";
        public const string ZQuantile30BandName = "zQ30";
        public const string ZQuantile35BandName = "zQ35";
        public const string ZQuantile40BandName = "zQ40";
        public const string ZQuantile45BandName = "zQ45";
        public const string ZQuantile50BandName = "zQ50";
        public const string ZQuantile55BandName = "zQ55";
        public const string ZQuantile60BandName = "zQ60";
        public const string ZQuantile65BandName = "zQ65";
        public const string ZQuantile70BandName = "zQ70";
        public const string ZQuantile75BandName = "zQ75";
        public const string ZQuantile80BandName = "zQ80";
        public const string ZQuantile85BandName = "zQ85";
        public const string ZQuantile90BandName = "zQ90";
        public const string ZQuantile95BandName = "zQ95";
        public const string IntensityFirstReturnBandName = "intensityFirstReturn";
        public const string IntensityMeanBandName = "intensityMean";
        public const string IntensityMeanAboveMedianZBandName = "intensityMeanAboveMedianZ";
        public const string IntensityMeanBelowMedianZBandName = "intensityMeanBelowMedianZ";
        public const string IntensityMaxBandName = "intensityMax";
        public const string IntensityStandardDeviationBandName = "intensityStdDev";
        public const string IntensitySkewBandName = "intensitySkew";
        public const string IntensityQuantile10BandName = "intensityQ10";
        public const string IntensityQuantile20BandName = "intensityQ20";
        public const string IntensityQuantile30BandName = "intensityQ30";
        public const string IntensityQuantile40BandName = "intensityQ40";
        public const string IntensityQuantile50BandName = "intensityQ50";
        public const string IntensityQuantile60BandName = "intensityQ60";
        public const string IntensityQuantile70BandName = "intensityQ70";
        public const string IntensityQuantile80BandName = "intensityQ80";
        public const string IntensityQuantile90BandName = "intensityQ90";
        public const string PFirstReturnBandName = "pFirstReturn";
        public const string PSecondReturnBandName = "pSecondReturn";
        public const string PThirdReturnBandName = "pThirdReturn";
        public const string PFourthReturnBandName = "pFourthReturn";
        public const string PFifthReturnBandName = "pFifthReturn";
        public const string PGroundBandName = "pGround";

        public const string IntensityPCumulativeZQ10BandName = "intensityPCumulativeZQ10";
        public const string IntensityPCumulativeZQ30BandName = "intensityPCumulativeZQ30";
        public const string IntensityPCumulativeZQ50BandName = "intensityPCumulativeZQ50";
        public const string IntensityPCumulativeZQ70BandName = "intensityPCumulativeZQ70";
        public const string IntensityPCumulativeZQ90BandName = "intensityPCumulativeZQ90";
        public const string IntensityPGroundBandName = "intensityPground";
        public const string IntensityTotalBandName = "intensityTotal";
        public const string IntensityKurtosisBandName = "intensityKurtosis";
        public const string ZKurtosisBandName = "zKurtosis";

        public const string ZPCumulative10BandName = "zPcumulative10";
        public const string ZPCumulative20BandName = "zPcumulative20";
        public const string ZPCumulative30BandName = "zPcumulative30";
        public const string ZPCumulative40BandName = "zPcumulative40";
        public const string ZPCumulative50BandName = "zPcumulative50";
        public const string ZPCumulative60BandName = "zPcumulative60";
        public const string ZPCumulative70BandName = "zPcumulative70";
        public const string ZPCumulative80BandName = "zPcumulative80";
        public const string ZPCumulative90BandName = "zPcumulative90";
        #endregion

        private PointListZirnc?[] pendingCells;

        // always on bands
        public RasterBand<float> AcceptedPoints { get; private init; }
        public RasterBand<float> ZMax { get; private init; }
        public RasterBand<float> ZMean { get; private init; }
        public RasterBand<float> ZGroundMean { get; private init; }
        public RasterBand<float> ZStandardDeviation { get; private init; }
        public RasterBand<float> ZSkew { get; private init; }
        public RasterBand<float> ZNormalizedEntropy { get; private init; }
        public RasterBand<float> PZAboveZMean { get; private init; }
        public RasterBand<float> PZAboveThreshold { get; private init; }
        public RasterBand<float> ZQuantile10 { get; private init; }
        public RasterBand<float> ZQuantile20 { get; private init; }
        public RasterBand<float> ZQuantile30 { get; private init; }
        public RasterBand<float> ZQuantile40 { get; private init; }
        public RasterBand<float> ZQuantile50 { get; private init; }
        public RasterBand<float> ZQuantile60 { get; private init; }
        public RasterBand<float> ZQuantile70 { get; private init; }
        public RasterBand<float> ZQuantile80 { get; private init; }
        public RasterBand<float> ZQuantile90 { get; private init; }
        public RasterBand<float> IntensityFirstReturn { get; private init; } // Alonzo et al. 2014 http://dx.doi.org/10.1016/j.rse.2014.03.018, Shi et al. 2018 https://doi.org/10.1016/j.isprsjprs.2018.02.002
        public RasterBand<float> IntensityMean { get; private init; }
        public RasterBand<float> IntensityMeanAboveMedianZ { get; private init; } // Alonzo et al.
        public RasterBand<float> IntensityMeanBelowMedianZ { get; private init; } // Alonzo et al.
        public RasterBand<float> IntensityMax { get; private init; }
        public RasterBand<float> IntensityStandardDeviation { get; private init; }
        public RasterBand<float> IntensitySkew { get; private init; }
        public RasterBand<float> IntensityQuantile10 { get; private init; }
        public RasterBand<float> IntensityQuantile20 { get; private init; }
        public RasterBand<float> IntensityQuantile30 { get; private init; }
        public RasterBand<float> IntensityQuantile40 { get; private init; }
        public RasterBand<float> IntensityQuantile50 { get; private init; }
        public RasterBand<float> IntensityQuantile60 { get; private init; }
        public RasterBand<float> IntensityQuantile70 { get; private init; }
        public RasterBand<float> IntensityQuantile80 { get; private init; }
        public RasterBand<float> IntensityQuantile90 { get; private init; }
        public RasterBand<float> PFirstReturn { get; private init; }
        public RasterBand<float> PSecondReturn { get; private init; }
        public RasterBand<float> PThirdReturn { get; private init; }
        public RasterBand<float> PFourthReturn { get; private init; }
        public RasterBand<float> PFifthReturn { get; private init; }
        public RasterBand<float> PGround { get; private init; }

        // optional bands
        public RasterBand<float>? IntensityKurtosis { get; private set; }
        public RasterBand<float>? IntensityPCumulativeZQ10 { get; private set; }
        public RasterBand<float>? IntensityPCumulativeZQ30 { get; private set; }
        public RasterBand<float>? IntensityPCumulativeZQ50 { get; private set; }
        public RasterBand<float>? IntensityPCumulativeZQ70 { get; private set; }
        public RasterBand<float>? IntensityPCumulativeZQ90 { get; private set; }
        public RasterBand<float>? IntensityPGround { get; private set; }
        public RasterBand<float>? IntensityTotal { get; private set; }
        public RasterBand<float>? ZKurtosis { get; private set; }
        public RasterBand<float>? ZPCumulative10 { get; private set; }
        public RasterBand<float>? ZPCumulative20 { get; private set; }
        public RasterBand<float>? ZPCumulative30 { get; private set; }
        public RasterBand<float>? ZPCumulative40 { get; private set; }
        public RasterBand<float>? ZPCumulative50 { get; private set; }
        public RasterBand<float>? ZPCumulative60 { get; private set; }
        public RasterBand<float>? ZPCumulative70 { get; private set; }
        public RasterBand<float>? ZPCumulative80 { get; private set; }
        public RasterBand<float>? ZPCumulative90 { get; private set; }
        public RasterBand<float>? ZQuantile05 { get; private set; }
        public RasterBand<float>? ZQuantile15 { get; private set; }
        public RasterBand<float>? ZQuantile25 { get; private set; }
        public RasterBand<float>? ZQuantile35 { get; private set; }
        public RasterBand<float>? ZQuantile45 { get; private set; }
        public RasterBand<float>? ZQuantile55 { get; private set; }
        public RasterBand<float>? ZQuantile65 { get; private set; }
        public RasterBand<float>? ZQuantile75 { get; private set; }
        public RasterBand<float>? ZQuantile85 { get; private set; }
        public RasterBand<float>? ZQuantile95 { get; private set; }

        public GridMetricsRaster(Grid transformAndExtent, GridMetricsSettings settings)
            : this(transformAndExtent.Crs, transformAndExtent.Transform, transformAndExtent.SizeX, transformAndExtent.SizeY, settings, dataBufferPool: null, cloneCrsAndTransform: true)
        {
        }

        public GridMetricsRaster(SpatialReference crs, LasTile lasTile, double cellSize, int xSizeInCells, int ySizeInCells, GridMetricsSettings settings, RasterBandPool? dataBufferPool)
            : this(crs.Clone(), new(lasTile.GridExtent.XMin, lasTile.GridExtent.YMax, cellSize, -cellSize), xSizeInCells, ySizeInCells, settings, dataBufferPool, cloneCrsAndTransform: false)
        {
        }

        protected GridMetricsRaster(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells, GridMetricsSettings settings, RasterBandPool? dataBufferPool, bool cloneCrsAndTransform)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform)
        {
            this.pendingCells = [];

            // always present bands
            float noDataValue = RasterBand.NoDataDefaultFloat;
            this.AcceptedPoints = new(this, GridMetricsRaster.AcceptedPointsBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZMax = new(this, GridMetricsRaster.ZMaxBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZMean = new(this, GridMetricsRaster.ZMeanBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZGroundMean = new(this, GridMetricsRaster.ZGroundMeanBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZStandardDeviation = new(this, GridMetricsRaster.ZStandardDeviationBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZSkew = new(this, GridMetricsRaster.ZSkewBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZNormalizedEntropy = new(this, GridMetricsRaster.ZNormalizedEntropyBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PZAboveZMean = new(this, GridMetricsRaster.PZAboveZMeanBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PZAboveThreshold = new(this, GridMetricsRaster.PZAboveThresholdBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile10 = new(this, GridMetricsRaster.ZQuantile10BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile20 = new(this, GridMetricsRaster.ZQuantile20BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile30 = new(this, GridMetricsRaster.ZQuantile30BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile40 = new(this, GridMetricsRaster.ZQuantile40BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile50 = new(this, GridMetricsRaster.ZQuantile50BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile60 = new(this, GridMetricsRaster.ZQuantile60BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile70 = new(this, GridMetricsRaster.ZQuantile70BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile80 = new(this, GridMetricsRaster.ZQuantile80BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.ZQuantile90 = new(this, GridMetricsRaster.ZQuantile90BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityFirstReturn = new(this, GridMetricsRaster.IntensityFirstReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool); // Alonzo et al. 2014 http://dx.doi.org/10.1016/j.rse.2014.03.018
            this.IntensityMean = new(this, GridMetricsRaster.IntensityMeanBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityMeanAboveMedianZ = new(this, GridMetricsRaster.IntensityMeanAboveMedianZBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool); // Alonzo et al.
            this.IntensityMeanBelowMedianZ = new(this, GridMetricsRaster.IntensityMeanBelowMedianZBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool); // Alonzo et al.
            this.IntensityMax = new(this, GridMetricsRaster.IntensityMaxBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityStandardDeviation = new(this, GridMetricsRaster.IntensityStandardDeviationBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensitySkew = new(this, GridMetricsRaster.IntensitySkewBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile10 = new(this, GridMetricsRaster.IntensityQuantile10BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile20 = new(this, GridMetricsRaster.IntensityQuantile20BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile30 = new(this, GridMetricsRaster.IntensityQuantile30BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile40 = new(this, GridMetricsRaster.IntensityQuantile40BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile50 = new(this, GridMetricsRaster.IntensityQuantile50BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile60 = new(this, GridMetricsRaster.IntensityQuantile60BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile70 = new(this, GridMetricsRaster.IntensityQuantile70BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile80 = new(this, GridMetricsRaster.IntensityQuantile80BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.IntensityQuantile90 = new(this, GridMetricsRaster.IntensityQuantile90BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PFirstReturn = new(this, GridMetricsRaster.PFirstReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PSecondReturn = new(this, GridMetricsRaster.PSecondReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PThirdReturn = new(this, GridMetricsRaster.PThirdReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PFourthReturn = new(this, GridMetricsRaster.PFourthReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PFifthReturn = new(this, GridMetricsRaster.PFifthReturnBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            this.PGround = new(this, GridMetricsRaster.PGroundBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);

            // optional bands
            this.IntensityPCumulativeZQ10 = null;
            this.IntensityPCumulativeZQ30 = null;
            this.IntensityPCumulativeZQ50 = null;
            this.IntensityPCumulativeZQ70 = null;
            this.IntensityPCumulativeZQ90 = null;
            this.IntensityPGround = null;
            this.IntensityTotal = null;
            this.IntensityKurtosis = null;
            this.ZKurtosis = null;
            if (settings.IntensityPCumulativeZQ)
            {
                this.IntensityPCumulativeZQ10 = new(this, GridMetricsRaster.IntensityPCumulativeZQ10BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.IntensityPCumulativeZQ30 = new(this, GridMetricsRaster.IntensityPCumulativeZQ30BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.IntensityPCumulativeZQ50 = new(this, GridMetricsRaster.IntensityPCumulativeZQ50BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.IntensityPCumulativeZQ70 = new(this, GridMetricsRaster.IntensityPCumulativeZQ70BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.IntensityPCumulativeZQ90 = new(this, GridMetricsRaster.IntensityPCumulativeZQ90BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if (settings.IntensityPGround)
            {
                this.IntensityPGround = new(this, GridMetricsRaster.IntensityPGroundBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if (settings.IntensityTotal)
            {
                this.IntensityTotal = new(this, GridMetricsRaster.IntensityTotalBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }
            if (settings.Kurtosis)
            {
                this.IntensityKurtosis = new(this, GridMetricsRaster.IntensityKurtosisBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZKurtosis = new(this, GridMetricsRaster.ZKurtosisBandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }

            this.ZPCumulative10 = null;
            this.ZPCumulative20 = null;
            this.ZPCumulative30 = null;
            this.ZPCumulative40 = null;
            this.ZPCumulative50 = null;
            this.ZPCumulative60 = null;
            this.ZPCumulative70 = null;
            this.ZPCumulative80 = null;
            this.ZPCumulative90 = null;
            if (settings.ZPCumulative)
            {
                this.ZPCumulative10 = new(this, GridMetricsRaster.ZPCumulative10BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative20 = new(this, GridMetricsRaster.ZPCumulative20BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative30 = new(this, GridMetricsRaster.ZPCumulative30BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative40 = new(this, GridMetricsRaster.ZPCumulative40BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative50 = new(this, GridMetricsRaster.ZPCumulative50BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative60 = new(this, GridMetricsRaster.ZPCumulative60BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative70 = new(this, GridMetricsRaster.ZPCumulative70BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative80 = new(this, GridMetricsRaster.ZPCumulative80BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZPCumulative90 = new(this, GridMetricsRaster.ZPCumulative90BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }

            this.ZQuantile05 = null;
            this.ZQuantile15 = null;
            this.ZQuantile25 = null;
            this.ZQuantile35 = null;
            this.ZQuantile45 = null;
            this.ZQuantile55 = null;
            this.ZQuantile65 = null;
            this.ZQuantile75 = null;
            this.ZQuantile85 = null;
            this.ZQuantile95 = null;
            if (settings.ZQFives)
            {
                this.ZQuantile05 = new(this, GridMetricsRaster.ZQuantile05BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile15 = new(this, GridMetricsRaster.ZQuantile15BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile25 = new(this, GridMetricsRaster.ZQuantile25BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile35 = new(this, GridMetricsRaster.ZQuantile35BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile45 = new(this, GridMetricsRaster.ZQuantile45BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile55 = new(this, GridMetricsRaster.ZQuantile55BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile65 = new(this, GridMetricsRaster.ZQuantile65BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile75 = new(this, GridMetricsRaster.ZQuantile75BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile85 = new(this, GridMetricsRaster.ZQuantile85BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
                this.ZQuantile95 = new(this, GridMetricsRaster.ZQuantile95BandName, noDataValue, RasterBandInitialValue.NoData, dataBufferPool);
            }
        }

        public bool AccumulatePointsThreadSafe(int cellIndexX, int cellIndexY, PointListZirnc pointsInCell, ObjectPool<PointListZirnc> pointListPool, [NotNullWhen(true)] out PointListZirnc? completedPointsInCell)
        {
            if (this.pendingCells.Length == 0)
            {
                this.pendingCells = new PointListZirnc?[this.Cells];
            }

            int cellIndex = this.ToCellIndex(cellIndexX, cellIndexY);
            PointListZirnc? accumulatedPoints = this.pendingCells[cellIndex];
            if (accumulatedPoints == null)
            {
                lock (pointListPool)
                {
                    accumulatedPoints = this.pendingCells[cellIndex];
                    if (accumulatedPoints == null)
                    {
                        if (pointListPool.TryGet(out accumulatedPoints) == false)
                        {
                            accumulatedPoints = new();
                        }
                        this.pendingCells[cellIndex] = accumulatedPoints;
                    }
                }
            }

            bool cellCompleted = false;
            lock (accumulatedPoints)
            {
                cellCompleted = accumulatedPoints.AddPointsFromTile(pointsInCell);
                if (cellCompleted)
                {
                    this.pendingCells[cellIndex] = null; // clear index for release back to pool
                }
            }

            completedPointsInCell = cellCompleted ? accumulatedPoints : null;
            return cellCompleted;
        }

        private int GetBandCount()
        {
            int bandCount = 41; // required bands

            // optional bands
            if (this.IntensityPCumulativeZQ10 != null)
            {
                ++bandCount;
            }
            if (this.IntensityPCumulativeZQ30 != null)
            {
                ++bandCount;
            }
            if (this.IntensityPCumulativeZQ50 != null)
            {
                ++bandCount;
            }
            if (this.IntensityPCumulativeZQ70 != null)
            {
                ++bandCount;
            }
            if (this.IntensityPCumulativeZQ90 != null)
            {
                ++bandCount;
            }
            if (this.IntensityTotal != null)
            {
                ++bandCount;
            }
            if (this.IntensityKurtosis != null)
            {
                ++bandCount;
            }
            if (this.ZKurtosis != null)
            {
                ++bandCount;
            }

            if (this.ZPCumulative10 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative20 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative30 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative40 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative50 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative60 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative70 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative80 != null)
            {
                ++bandCount;
            }
            if (this.ZPCumulative90 != null)
            {
                ++bandCount;
            }

            if (this.ZQuantile05 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile15 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile25 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile35 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile45 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile55 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile65 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile75 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile85 != null)
            {
                ++bandCount;
            }
            if (this.ZQuantile95 != null)
            {
                ++bandCount;
            }

            return bandCount;
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.AcceptedPoints;
            yield return this.ZMax;
            yield return this.ZMean;
            yield return this.ZGroundMean;
            yield return this.ZStandardDeviation;
            yield return this.ZSkew;
            yield return this.ZNormalizedEntropy;
            yield return this.PZAboveZMean;
            yield return this.PZAboveThreshold;
            if (this.ZQuantile05 != null)
            {
                yield return this.ZQuantile05;
            }
            yield return this.ZQuantile10;
            if (this.ZQuantile15 != null)
            {
                yield return this.ZQuantile15;
            }
            yield return this.ZQuantile20;
            if (this.ZQuantile25 != null)
            {
                yield return this.ZQuantile25;
            }
            yield return this.ZQuantile30;
            if (this.ZQuantile35 != null)
            {
                yield return this.ZQuantile35;
            }
            yield return this.ZQuantile40;
            if (this.ZQuantile45 != null)
            {
                yield return this.ZQuantile45;
            }
            yield return this.ZQuantile50;
            if (this.ZQuantile55 != null)
            {
                yield return this.ZQuantile55;
            }
            yield return this.ZQuantile60;
            if (this.ZQuantile65 != null)
            {
                yield return this.ZQuantile65;
            }
            yield return this.ZQuantile70;
            if (this.ZQuantile75 != null)
            {
                yield return this.ZQuantile75;
            }
            yield return this.ZQuantile80;
            if (this.ZQuantile85 != null)
            {
                yield return this.ZQuantile85;
            }
            yield return this.ZQuantile90;
            if (this.ZQuantile95 != null)
            {
                yield return this.ZQuantile95;
            }
            yield return this.IntensityFirstReturn;
            yield return this.IntensityMean;
            yield return this.IntensityMeanAboveMedianZ;
            yield return this.IntensityMeanBelowMedianZ;
            yield return this.IntensityMax;
            yield return this.IntensityStandardDeviation;
            yield return this.IntensitySkew;
            yield return this.IntensityQuantile10;
            yield return this.IntensityQuantile20;
            yield return this.IntensityQuantile30;
            yield return this.IntensityQuantile40;
            yield return this.IntensityQuantile50;
            yield return this.IntensityQuantile60;
            yield return this.IntensityQuantile70;
            yield return this.IntensityQuantile80;
            yield return this.IntensityQuantile90;
            yield return this.PFirstReturn;
            yield return this.PSecondReturn;
            yield return this.PThirdReturn;
            yield return this.PFourthReturn;
            yield return this.PFifthReturn;
            yield return this.PGround;

            // optional bands
            if (this.IntensityPCumulativeZQ10 != null)
            {
                yield return this.IntensityPCumulativeZQ10;
            }
            if (this.IntensityPCumulativeZQ30 != null)
            {
                yield return this.IntensityPCumulativeZQ30;
            }
            if (this.IntensityPCumulativeZQ50 != null)
            {
                yield return this.IntensityPCumulativeZQ50;
            }
            if (this.IntensityPCumulativeZQ70 != null)
            {
                yield return this.IntensityPCumulativeZQ70;
            }
            if (this.IntensityPCumulativeZQ90 != null)
            {
                yield return this.IntensityPCumulativeZQ90;
            }
            if (this.IntensityPGround != null)
            {
                yield return this.IntensityPGround;
            }
            if (this.IntensityTotal != null)
            {
                yield return this.IntensityTotal;
            }
            if (this.IntensityKurtosis != null)
            {
                yield return this.IntensityKurtosis;
            }
            if (this.ZKurtosis != null)
            {
                yield return this.ZKurtosis;
            }

            if (this.ZPCumulative10 != null)
            {
            yield return this.ZPCumulative10;
            }
            if (this.ZPCumulative20 != null)
            {
                yield return this.ZPCumulative20;
            }
            if (this.ZPCumulative30 != null)
            {
                yield return this.ZPCumulative30;
            }
            if (this.ZPCumulative40 != null)
            {
                yield return this.ZPCumulative40;
            }
            if (this.ZPCumulative50 != null)
            {
                yield return this.ZPCumulative50;
            }
            if (this.ZPCumulative60 != null)
            {
                yield return this.ZPCumulative60;
            }
            if (this.ZPCumulative70 != null)
            {
                yield return this.ZPCumulative70;
            }
            if (this.ZPCumulative80 != null)
            {
                yield return this.ZPCumulative80;
            }
            if (this.ZPCumulative90 != null)
            {
                yield return this.ZPCumulative90;
            }
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            List<RasterBandStatistics> bandStatistics = new(this.GetBandCount())
            {
                this.AcceptedPoints.GetStatistics(),
                this.ZMax.GetStatistics(),
                this.ZMean.GetStatistics(),
                this.ZGroundMean.GetStatistics(),
                this.ZStandardDeviation.GetStatistics(),
                this.ZSkew.GetStatistics(),
                this.ZNormalizedEntropy.GetStatistics(),
                this.PZAboveZMean.GetStatistics(),
                this.PZAboveThreshold.GetStatistics()
            };
            if (this.ZQuantile05 != null)
            {
                bandStatistics.Add(this.ZQuantile05.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile10.GetStatistics());
            if (this.ZQuantile15 != null)
            {
                bandStatistics.Add(this.ZQuantile15.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile20.GetStatistics());
            if (this.ZQuantile25 != null)
            {
                bandStatistics.Add(this.ZQuantile25.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile30.GetStatistics());
            if (this.ZQuantile35 != null)
            {
                bandStatistics.Add(this.ZQuantile35.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile40.GetStatistics());
            if (this.ZQuantile45 != null)
            {
                bandStatistics.Add(this.ZQuantile45.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile50.GetStatistics());
            if (this.ZQuantile55 != null)
            {
                bandStatistics.Add(this.ZQuantile55.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile60.GetStatistics());
            if (this.ZQuantile65 != null)
            {
                bandStatistics.Add(this.ZQuantile65.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile70.GetStatistics());
            if (this.ZQuantile75 != null)
            {
                bandStatistics.Add(this.ZQuantile75.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile80.GetStatistics());
            if (this.ZQuantile85 != null)
            {
                bandStatistics.Add(this.ZQuantile85.GetStatistics());
            }
            bandStatistics.Add(this.ZQuantile90.GetStatistics());
            if (this.ZQuantile95 != null)
            {
                bandStatistics.Add(this.ZQuantile95.GetStatistics());
            }
            bandStatistics.Add(this.IntensityFirstReturn.GetStatistics());
            bandStatistics.Add(this.IntensityMean.GetStatistics());
            bandStatistics.Add(this.IntensityMeanAboveMedianZ.GetStatistics());
            bandStatistics.Add(this.IntensityMeanBelowMedianZ.GetStatistics());
            bandStatistics.Add(this.IntensityMax.GetStatistics());
            bandStatistics.Add(this.IntensityStandardDeviation.GetStatistics());
            bandStatistics.Add(this.IntensitySkew.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile10.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile20.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile30.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile40.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile50.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile60.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile70.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile80.GetStatistics());
            bandStatistics.Add(this.IntensityQuantile90.GetStatistics());
            bandStatistics.Add(this.PFirstReturn.GetStatistics());
            bandStatistics.Add(this.PSecondReturn.GetStatistics());
            bandStatistics.Add(this.PThirdReturn.GetStatistics());
            bandStatistics.Add(this.PFourthReturn.GetStatistics());
            bandStatistics.Add(this.PFifthReturn.GetStatistics());
            bandStatistics.Add(this.PGround.GetStatistics());

            // optional bands
            if (this.IntensityPCumulativeZQ10 != null)
            {
                bandStatistics.Add(this.IntensityPCumulativeZQ10.GetStatistics());
            }
            if (this.IntensityPCumulativeZQ30 != null)
            {
                bandStatistics.Add(this.IntensityPCumulativeZQ30.GetStatistics());
            }
            if (this.IntensityPCumulativeZQ50 != null)
            {
                bandStatistics.Add(this.IntensityPCumulativeZQ50.GetStatistics());
            }
            if (this.IntensityPCumulativeZQ70 != null)
            {
                bandStatistics.Add(this.IntensityPCumulativeZQ70.GetStatistics());
            }
            if (this.IntensityPCumulativeZQ90 != null)
            {
                bandStatistics.Add(this.IntensityPCumulativeZQ90.GetStatistics());
            }
            if (this.IntensityPGround != null)
            {
                bandStatistics.Add(this.IntensityPGround.GetStatistics());
            }
            if (this.IntensityTotal != null)
            {
                bandStatistics.Add(this.IntensityTotal.GetStatistics());
            }
            if (this.IntensityKurtosis != null)
            {
                bandStatistics.Add(this.IntensityKurtosis.GetStatistics());
            }
            if (this.ZKurtosis != null)
            {
                bandStatistics.Add(this.ZKurtosis.GetStatistics());
            }

            if (this.ZPCumulative10 != null)
            {
                bandStatistics.Add(this.ZPCumulative10.GetStatistics());
            }
            if (this.ZPCumulative20 != null)
            {
                bandStatistics.Add(this.ZPCumulative20.GetStatistics());
            }
            if (this.ZPCumulative30 != null)
            {
                bandStatistics.Add(this.ZPCumulative30.GetStatistics());
            }
            if (this.ZPCumulative40 != null)
            {
                bandStatistics.Add(this.ZPCumulative40.GetStatistics());
            }
            if (this.ZPCumulative50 != null)
            {
                bandStatistics.Add(this.ZPCumulative50.GetStatistics());
            }
            if (this.ZPCumulative60 != null)
            {
                bandStatistics.Add(this.ZPCumulative60.GetStatistics());
            }
            if (this.ZPCumulative70 != null)
            {
                bandStatistics.Add(this.ZPCumulative70.GetStatistics());
            }
            if (this.ZPCumulative80 != null)
            {
                bandStatistics.Add(this.ZPCumulative80.GetStatistics());
            }
            if (this.ZPCumulative90 != null)
            {
                bandStatistics.Add(this.ZPCumulative90.GetStatistics());
            }

            return bandStatistics;
        }

        public override void ReadBandData()
        {
            using Dataset metricsDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            Debug.Assert((this.SizeX == metricsDataset.RasterXSize) && (this.SizeY == metricsDataset.RasterYSize) && SpatialReferenceExtensions.IsSameCrs(this.Crs, metricsDataset.GetSpatialRef()));
            for (int gdalBandIndex = 1; gdalBandIndex <= metricsDataset.RasterCount; ++gdalBandIndex)
            {
                Band gdalBand = metricsDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case GridMetricsRaster.AcceptedPointsBandName:
                        this.AcceptedPoints.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZMaxBandName:
                        this.ZMax.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZMeanBandName:
                        this.ZMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZGroundMeanBandName:
                        this.ZGroundMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZStandardDeviationBandName:
                        this.ZStandardDeviation.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZSkewBandName:
                        this.ZSkew.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZNormalizedEntropyBandName:
                        this.ZNormalizedEntropy.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PZAboveZMeanBandName:
                        this.PZAboveZMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PZAboveThresholdBandName:
                        this.PZAboveThreshold.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile05BandName:
                        if (this.ZQuantile05 == null)
                        {
                            this.ZQuantile05 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile05.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile10BandName:
                        this.ZQuantile10.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile15BandName:
                        if (this.ZQuantile15 == null)
                        {
                            this.ZQuantile15 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile15.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile20BandName:
                        this.ZQuantile20.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile25BandName:
                        if (this.ZQuantile25 == null)
                        {
                            this.ZQuantile25 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile25.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile30BandName:
                        this.ZQuantile30.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile35BandName:
                        if (this.ZQuantile35 == null)
                        {
                            this.ZQuantile35 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile35.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile40BandName:
                        this.ZQuantile40.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile45BandName:
                        if (this.ZQuantile45 == null)
                        {
                            this.ZQuantile45 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile45.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile50BandName:
                        this.ZQuantile50.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile55BandName:
                        if (this.ZQuantile55 == null)
                        {
                            this.ZQuantile55 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile55.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile60BandName:
                        this.ZQuantile60.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile65BandName:
                        if (this.ZQuantile65 == null)
                        {
                            this.ZQuantile65 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile65.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile70BandName:
                        this.ZQuantile70.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile75BandName:
                        if (this.ZQuantile75 == null)
                        {
                            this.ZQuantile75 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile75.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile80BandName:
                        this.ZQuantile80.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile85BandName:
                        if (this.ZQuantile85 == null)
                        {
                            this.ZQuantile85 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile85.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZQuantile90BandName:
                        this.ZQuantile90.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.ZQuantile95BandName:
                        if (this.ZQuantile95 == null)
                        {
                            this.ZQuantile95 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZQuantile95.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityFirstReturnBandName:
                        this.IntensityFirstReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityMeanBandName:
                        this.IntensityMean.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityMeanAboveMedianZBandName:
                        this.IntensityMeanAboveMedianZ.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityMeanBelowMedianZBandName:
                        this.IntensityMeanBelowMedianZ.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityMaxBandName:
                        this.IntensityMax.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityStandardDeviationBandName:
                        this.IntensityStandardDeviation.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensitySkewBandName:
                        this.IntensitySkew.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile10BandName:
                        this.IntensityQuantile10.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile20BandName:
                        this.IntensityQuantile20.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile30BandName:
                        this.IntensityQuantile30.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile40BandName:
                        this.IntensityQuantile40.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile50BandName:
                        this.IntensityQuantile50.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile60BandName:
                        this.IntensityQuantile60.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile70BandName:
                        this.IntensityQuantile70.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile80BandName:
                        this.IntensityQuantile80.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.IntensityQuantile90BandName:
                        this.IntensityQuantile90.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PFirstReturnBandName:
                        this.PFirstReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PSecondReturnBandName:
                        this.PSecondReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PThirdReturnBandName:
                        this.PThirdReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PFourthReturnBandName:
                        this.PFourthReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PFifthReturnBandName:
                        this.PFifthReturn.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case GridMetricsRaster.PGroundBandName:
                        this.PGround.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;

                    case GridMetricsRaster.IntensityPCumulativeZQ10BandName:
                        if (this.IntensityPCumulativeZQ10 == null)
                        {
                            this.IntensityPCumulativeZQ10 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ10.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityPCumulativeZQ30BandName:
                        if (this.IntensityPCumulativeZQ30 == null)
                        {
                            this.IntensityPCumulativeZQ30 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ30.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityPCumulativeZQ50BandName:
                        if (this.IntensityPCumulativeZQ50 == null)
                        {
                            this.IntensityPCumulativeZQ50 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ50.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityPCumulativeZQ70BandName:
                        if (this.IntensityPCumulativeZQ70 == null)
                        {
                            this.IntensityPCumulativeZQ70 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ70.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityPCumulativeZQ90BandName:
                        if (this.IntensityPCumulativeZQ90 == null)
                        {
                            this.IntensityPCumulativeZQ90 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ90.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityPGroundBandName:
                        if (this.IntensityPGround == null)
                        {
                            this.IntensityPGround = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPGround.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityTotalBandName:
                        if (this.IntensityPCumulativeZQ10 == null)
                        {
                            this.IntensityPCumulativeZQ10 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ10.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.IntensityKurtosisBandName:
                        if (this.IntensityPCumulativeZQ10 == null)
                        {
                            this.IntensityPCumulativeZQ10 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ10.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZKurtosisBandName:
                        if (this.IntensityPCumulativeZQ10 == null)
                        {
                            this.IntensityPCumulativeZQ10 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.IntensityPCumulativeZQ10.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;

                    case GridMetricsRaster.ZPCumulative10BandName:
                        if (this.ZPCumulative10 == null)
                        {
                            this.ZPCumulative10 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative10.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative20BandName:
                        if (this.ZPCumulative20 == null)
                        {
                            this.ZPCumulative20 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative20.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative30BandName:
                        if (this.ZPCumulative30 == null)
                        {
                            this.ZPCumulative30 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative30.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative40BandName:
                        if (this.ZPCumulative40 == null)
                        {
                            this.ZPCumulative40 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative40.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative50BandName:
                        if (this.ZPCumulative50 == null)
                        {
                            this.ZPCumulative50 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative50.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative60BandName:
                        if (this.ZPCumulative60 == null)
                        {
                            this.ZPCumulative60 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative60.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative70BandName:
                        if (this.ZPCumulative70 == null)
                        {
                            this.ZPCumulative70 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative70.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative80BandName:
                        if (this.ZPCumulative80 == null)
                        {
                            this.ZPCumulative80 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative80.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;
                    case GridMetricsRaster.ZPCumulative90BandName:
                        if (this.ZPCumulative90 == null)
                        {
                            this.ZPCumulative90 = new(metricsDataset, gdalBand, readData: true);
                        }
                        else
                        {
                            this.ZPCumulative90.Read(metricsDataset, this.Crs, gdalBand);
                        }
                        break;

                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in grid metrics raster '" + this.FilePath + ".");
                }
            }

            metricsDataset.FlushCache();
        }

        public void Reset(string filePath, LasTile lasTile)
        {
            if (this.Transform.CellHeight >= 0.0)
            {
                throw new NotSupportedException("Positive cell heights are not currently supported.");
            }
            this.FilePath = filePath;
            this.Transform.SetOrigin(lasTile.GridExtent.XMin, lasTile.GridExtent.YMax);

            this.AcceptedPoints.FillNoData();
            this.ZMax.FillNoData();
            this.ZMean.FillNoData();
            this.ZGroundMean.FillNoData();
            this.ZStandardDeviation.FillNoData();
            this.ZSkew.FillNoData();
            this.ZNormalizedEntropy.FillNoData();
            this.PZAboveZMean.FillNoData();
            this.PZAboveThreshold.FillNoData();
            this.ZQuantile05?.FillNoData();
            this.ZQuantile10.FillNoData();
            this.ZQuantile15?.FillNoData();
            this.ZQuantile20.FillNoData();
            this.ZQuantile25?.FillNoData();
            this.ZQuantile30.FillNoData();
            this.ZQuantile35?.FillNoData();
            this.ZQuantile40.FillNoData();
            this.ZQuantile45?.FillNoData();
            this.ZQuantile50.FillNoData();
            this.ZQuantile55?.FillNoData();
            this.ZQuantile60.FillNoData();
            this.ZQuantile65?.FillNoData();
            this.ZQuantile70.FillNoData();
            this.ZQuantile75?.FillNoData();
            this.ZQuantile80.FillNoData();
            this.ZQuantile85?.FillNoData();
            this.ZQuantile90.FillNoData();
            this.ZQuantile95?.FillNoData();
            this.IntensityFirstReturn.FillNoData();
            this.IntensityMean.FillNoData();
            this.IntensityMeanAboveMedianZ.FillNoData();
            this.IntensityMeanBelowMedianZ.FillNoData();
            this.IntensityMax.FillNoData();
            this.IntensityStandardDeviation.FillNoData();
            this.IntensitySkew.FillNoData();
            this.IntensityQuantile10.FillNoData();
            this.IntensityQuantile20.FillNoData();
            this.IntensityQuantile30.FillNoData();
            this.IntensityQuantile40.FillNoData();
            this.IntensityQuantile50.FillNoData();
            this.IntensityQuantile60.FillNoData();
            this.IntensityQuantile70.FillNoData();
            this.IntensityQuantile80.FillNoData();
            this.IntensityQuantile90.FillNoData();
            this.PFirstReturn.FillNoData();
            this.PSecondReturn.FillNoData();
            this.PThirdReturn.FillNoData();
            this.PFourthReturn.FillNoData();
            this.PFifthReturn.FillNoData();
            this.PGround.FillNoData();

            // optional bands
            this.IntensityPCumulativeZQ10?.FillNoData();
            this.IntensityPCumulativeZQ30?.FillNoData();
            this.IntensityPCumulativeZQ50?.FillNoData();
            this.IntensityPCumulativeZQ70?.FillNoData();
            this.IntensityPCumulativeZQ90?.FillNoData();
            this.IntensityPGround?.FillNoData();
            this.IntensityTotal?.FillNoData();
            this.IntensityKurtosis?.FillNoData();
            this.ZKurtosis?.FillNoData();

            this.ZPCumulative10?.FillNoData();
            this.ZPCumulative20?.FillNoData();
            this.ZPCumulative30?.FillNoData();
            this.ZPCumulative40?.FillNoData();
            this.ZPCumulative50?.FillNoData();
            this.ZPCumulative60?.FillNoData();
            this.ZPCumulative70?.FillNoData();
            this.ZPCumulative80?.FillNoData();
            this.ZPCumulative90?.FillNoData();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException();
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            this.AcceptedPoints.ReturnData(dataBufferPool);
            this.ZMax.ReturnData(dataBufferPool);
            this.ZMean.ReturnData(dataBufferPool);
            this.ZGroundMean.ReturnData(dataBufferPool);
            this.ZStandardDeviation.ReturnData(dataBufferPool);
            this.ZSkew.ReturnData(dataBufferPool);
            this.ZNormalizedEntropy.ReturnData(dataBufferPool);
            this.PZAboveZMean.ReturnData(dataBufferPool);
            this.PZAboveThreshold.ReturnData(dataBufferPool);
            this.ZQuantile05?.ReturnData(dataBufferPool);
            this.ZQuantile10.ReturnData(dataBufferPool);
            this.ZQuantile15?.ReturnData(dataBufferPool);
            this.ZQuantile20.ReturnData(dataBufferPool);
            this.ZQuantile25?.ReturnData(dataBufferPool);
            this.ZQuantile30.ReturnData(dataBufferPool);
            this.ZQuantile35?.ReturnData(dataBufferPool);
            this.ZQuantile40.ReturnData(dataBufferPool);
            this.ZQuantile45?.ReturnData(dataBufferPool);
            this.ZQuantile50.ReturnData(dataBufferPool);
            this.ZQuantile55?.ReturnData(dataBufferPool);
            this.ZQuantile60.ReturnData(dataBufferPool);
            this.ZQuantile65?.ReturnData(dataBufferPool);
            this.ZQuantile70.ReturnData(dataBufferPool);
            this.ZQuantile75?.ReturnData(dataBufferPool);
            this.ZQuantile80.ReturnData(dataBufferPool);
            this.ZQuantile85?.ReturnData(dataBufferPool);
            this.ZQuantile90.ReturnData(dataBufferPool);
            this.ZQuantile95?.ReturnData(dataBufferPool);
            this.IntensityFirstReturn.ReturnData(dataBufferPool);
            this.IntensityMean.ReturnData(dataBufferPool);
            this.IntensityMeanAboveMedianZ.ReturnData(dataBufferPool);
            this.IntensityMeanBelowMedianZ.ReturnData(dataBufferPool);
            this.IntensityMax.ReturnData(dataBufferPool);
            this.IntensityStandardDeviation.ReturnData(dataBufferPool);
            this.IntensitySkew.ReturnData(dataBufferPool);
            this.IntensityQuantile10.ReturnData(dataBufferPool);
            this.IntensityQuantile20.ReturnData(dataBufferPool);
            this.IntensityQuantile30.ReturnData(dataBufferPool);
            this.IntensityQuantile40.ReturnData(dataBufferPool);
            this.IntensityQuantile50.ReturnData(dataBufferPool);
            this.IntensityQuantile60.ReturnData(dataBufferPool);
            this.IntensityQuantile70.ReturnData(dataBufferPool);
            this.IntensityQuantile80.ReturnData(dataBufferPool);
            this.IntensityQuantile90.ReturnData(dataBufferPool);
            this.PFirstReturn.ReturnData(dataBufferPool);
            this.PSecondReturn.ReturnData(dataBufferPool);
            this.PThirdReturn.ReturnData(dataBufferPool);
            this.PFourthReturn.ReturnData(dataBufferPool);
            this.PFifthReturn.ReturnData(dataBufferPool);
            this.PGround.ReturnData(dataBufferPool);

            // optional bands besides 5% z quantiles
            this.IntensityPCumulativeZQ10?.ReturnData(dataBufferPool);
            this.IntensityPCumulativeZQ30?.ReturnData(dataBufferPool);
            this.IntensityPCumulativeZQ50?.ReturnData(dataBufferPool);
            this.IntensityPCumulativeZQ70?.ReturnData(dataBufferPool);
            this.IntensityPCumulativeZQ90?.ReturnData(dataBufferPool);
            this.IntensityPGround?.ReturnData(dataBufferPool);
            this.IntensityTotal?.ReturnData(dataBufferPool);
            this.IntensityKurtosis?.ReturnData(dataBufferPool);
            this.ZKurtosis?.ReturnData(dataBufferPool);

            this.ZPCumulative10?.ReturnData(dataBufferPool);
            this.ZPCumulative20?.ReturnData(dataBufferPool);
            this.ZPCumulative30?.ReturnData(dataBufferPool);
            this.ZPCumulative40?.ReturnData(dataBufferPool);
            this.ZPCumulative50?.ReturnData(dataBufferPool);
            this.ZPCumulative60?.ReturnData(dataBufferPool);
            this.ZPCumulative70?.ReturnData(dataBufferPool);
            this.ZPCumulative80?.ReturnData(dataBufferPool);
            this.ZPCumulative90?.ReturnData(dataBufferPool);
        }

        public void SetMetrics(int cellIndexX, int cellIndexY, PointListZirnc pointsInCell, RasterNeighborhood8<float> dtmNeighborhood, float heightClassSizeInCrsUnits, float zThresholdInCrsUnits, ref float[]? sortedZ, ref UInt16[]? sortedIntensity)
        {
            int cellIndex = this.ToCellIndex(cellIndexX, cellIndexY);

            int pointCount = pointsInCell.Count;
            this.AcceptedPoints[cellIndex] = pointCount;

            (double cellXmin, double cellXmax, double cellYmin, double cellYmax) = this.Transform.GetCellExtent(cellIndexX, cellIndexY);
            (int dtmXindexMin, int dtmXindexMax, int dtmYindexMin, int dtmYindexMax) = dtmNeighborhood.Center.GetIntersectingCellIndices(cellXmin, cellXmax, cellYmin, cellYmax);
            Debug.Assert((dtmXindexMax > dtmXindexMin) && (dtmYindexMax > dtmYindexMin), "Expected at least one DTM cell to match grid metrics cell.");
            float zGroundSum = 0.0F;
            int dtmCells = 0;
            for (int dtmYindex = dtmYindexMin; dtmYindex < dtmYindexMax; ++dtmYindex)
            {
                for (int dtmXindex = dtmXindexMin; dtmXindex < dtmXindexMax; ++dtmXindex)
                {
                    if (dtmNeighborhood.TryGetValue(dtmXindex, dtmYindex, out float dtmZ) == false)
                    {
                        continue;
                    }

                    zGroundSum += dtmZ;
                    ++dtmCells;
                }
            }
            if (dtmCells > 0)
            {
                this.ZGroundMean[cellIndex] = zGroundSum / dtmCells;
            }

            if (pointCount < 1)
            {
                return; // cell contains no points; all statistics but n are undefined
            }

            // z quantiles
            // For now, quantiles are obtained with direct lookups as most likely there are thousands to tens of thousands of points in an
            // ABA cell. If needed, interpolation can be included to estimate quantiles more precisely in cells with low point counts.
            if ((sortedZ == null) || (sortedZ.Length < pointCount))
            {
                sortedZ = new float[2 * pointCount];
            }
            pointsInCell.Z.CopyTo(sortedZ);
            Array.Sort(sortedZ, 0, pointCount);

            int zQuantile50index;
            float zQuantile10;
            float zQuantile30;
            float zQuantile50;
            float zQuantile70;
            float zQuantile90;
            if (pointCount < 20)
            {
                // if cell has less than 20 points then n * pointCount / 20 will be zero at least some of the time
                // Negative indices result in this case due to the subtraction of 1.
                if (this.ZQuantile05 != null)
                {
                    this.ZQuantile05[cellIndex] = sortedZ[Int32.Max((int)(pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                zQuantile10 = sortedZ[Int32.Max((int)(2.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile10[cellIndex] = zQuantile10;
                if (this.ZQuantile15 != null)
                {
                    this.ZQuantile15[cellIndex] = sortedZ[Int32.Max((int)(3.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                this.ZQuantile20[cellIndex] = sortedZ[Int32.Max((int)(4.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                if (this.ZQuantile25 != null)
                {
                    this.ZQuantile25[cellIndex] = sortedZ[Int32.Max((int)(5.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                zQuantile30 = sortedZ[Int32.Max((int)(6.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile30[cellIndex] = zQuantile30;
                if (this.ZQuantile35 != null)
                {
                    this.ZQuantile35[cellIndex] = sortedZ[Int32.Max((int)(7.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                this.ZQuantile40[cellIndex] = sortedZ[Int32.Max((int)(8.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                if (this.ZQuantile45 != null)
                {
                    this.ZQuantile45[cellIndex] = sortedZ[Int32.Max((int)(9.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                zQuantile50index = Int32.Max((int)(10.0F * pointCount / 20.0F + 0.5F) - 1, 0);
                zQuantile50 = sortedZ[zQuantile50index];
                this.ZQuantile50[cellIndex] = zQuantile50;
                if (this.ZQuantile55 != null)
                {
                    this.ZQuantile55[cellIndex] = sortedZ[Int32.Max((int)(11.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                this.ZQuantile60[cellIndex] = sortedZ[Int32.Max((int)(12.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                if (this.ZQuantile65 != null)
                {
                    this.ZQuantile65[cellIndex] = sortedZ[Int32.Max((int)(13.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                zQuantile70 = sortedZ[Int32.Max((int)(14.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile70[cellIndex] = zQuantile70;
                if (this.ZQuantile75 != null)
                {
                    this.ZQuantile75[cellIndex] = sortedZ[Int32.Max((int)(15.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                this.ZQuantile80[cellIndex] = sortedZ[Int32.Max((int)(16.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                if (this.ZQuantile85 != null)
                {
                    this.ZQuantile85[cellIndex] = sortedZ[Int32.Max((int)(17.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
                zQuantile90 = sortedZ[Int32.Max((int)(18.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                this.ZQuantile90[cellIndex] = zQuantile90;
                if (this.ZQuantile95 != null)
                {
                    this.ZQuantile95[cellIndex] = sortedZ[Int32.Max((int)(19.0F * pointCount / 20.0F + 0.5F) - 1, 0)];
                }
            }
            else
            {
                if (this.ZQuantile05 != null)
                {
                    this.ZQuantile05[cellIndex] = sortedZ[pointCount / 20 - 1];
                }
                zQuantile10 = sortedZ[2 * pointCount / 20 - 1];
                this.ZQuantile10[cellIndex] = zQuantile10;
                if (this.ZQuantile15 != null)
                {
                    this.ZQuantile15[cellIndex] = sortedZ[3 * pointCount / 20 - 1];
                }
                this.ZQuantile20[cellIndex] = sortedZ[4 * pointCount / 20 - 1];
                if (this.ZQuantile25 != null)
                {
                    this.ZQuantile25[cellIndex] = sortedZ[5 * pointCount / 20 - 1];
                }
                zQuantile30 = sortedZ[6 * pointCount / 20 - 1];
                this.ZQuantile30[cellIndex] = zQuantile30;
                if (this.ZQuantile35 != null)
                {
                    this.ZQuantile35[cellIndex] = sortedZ[7 * pointCount / 20 - 1];
                }
                this.ZQuantile40[cellIndex] = sortedZ[8 * pointCount / 20 - 1];
                if (this.ZQuantile45 != null)
                {
                    this.ZQuantile45[cellIndex] = sortedZ[9 * pointCount / 20 - 1];
                }
                zQuantile50index = 10 * pointCount / 20 - 1;
                zQuantile50 = sortedZ[zQuantile50index];
                this.ZQuantile50[cellIndex] = zQuantile50;
                if (this.ZQuantile55 != null)
                {
                    this.ZQuantile55[cellIndex] = sortedZ[11 * pointCount / 20 - 1];
                }
                this.ZQuantile60[cellIndex] = sortedZ[12 * pointCount / 20 - 1];
                if (this.ZQuantile65 != null)
                {
                    this.ZQuantile65[cellIndex] = sortedZ[13 * pointCount / 20 - 1];
                }
                zQuantile70 = sortedZ[14 * pointCount / 20 - 1];
                this.ZQuantile70[cellIndex] = zQuantile70;
                if (this.ZQuantile75 != null)
                {
                    this.ZQuantile75[cellIndex] = sortedZ[15 * pointCount / 20 - 1];
                }
                this.ZQuantile80[cellIndex] = sortedZ[16 * pointCount / 20 - 1];
                if (this.ZQuantile85 != null)
                {
                    this.ZQuantile85[cellIndex] = sortedZ[17 * pointCount / 20 - 1];
                }
                zQuantile90 = sortedZ[18 * pointCount / 20 - 1];
                this.ZQuantile90[cellIndex] = zQuantile90;
                if (this.ZQuantile95 != null)
                {
                    this.ZQuantile95[cellIndex] = sortedZ[19 * pointCount / 20 - 1];
                }
            }
            float zMax = sortedZ[pointCount - 1];
            this.ZMax[cellIndex] = zMax;

            // combined z, intensity, and return statistics pass
            // single pass max, mean, standard deviation, skew, kurtosis for both z and intensity (https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance)
            // height class setup for normalized entropy calculation
            // probability point z is above z threshold
            // intensity by z
            float zMin = sortedZ[0];
            // double zGroundSum = 0.0; // calculation of mean ground elevation in cell disabled due to use of DTM
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
                double z = pointsInCell.Z[pointIndex];
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

                UInt16 intensity = pointsInCell.Intensity[pointIndex];
                intensitySum += intensity;
                double intensityAsDouble = intensity;
                double intensitySquare = intensityAsDouble * intensityAsDouble;
                intensitySumSquared += intensitySquare;
                intensitySumCubed += intensityAsDouble * intensitySquare;
                intensitySumFourthPower += intensitySquare * intensitySquare;

                int returnNumber = pointsInCell.ReturnNumber[pointIndex]; // may be zero in noncompliant .las files
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

                PointClassification classification = pointsInCell.Classification[pointIndex];
                if (classification == PointClassification.Ground)
                {
                    intensityGroundSum += intensity;
                    // zGroundSum += z;
                    ++groundPoints;
                }
            }

            // should skew and kurtosis be left as NaN rather than going to ±Inf if there aren't enough points in the cell to calculate them?
            double pointCountAsDouble = (double)pointCount;
            double zMean = zSum / pointCountAsDouble;
            this.ZMean[cellIndex] = (float)zMean;
            double zVariance = zSumSquared / pointCountAsDouble - zMean * zMean;
            double zStandardDeviation = Double.Sqrt(zVariance);
            this.ZStandardDeviation[cellIndex] = (float)zStandardDeviation;
            double zSkew = (zSumCubed - 3.0 * zSumSquared * zMean + 3.0 * zSum * zMean * zMean - pointCountAsDouble * zMean * zMean * zMean) / (zVariance * zStandardDeviation) * pointCountAsDouble / (pointCountAsDouble - 1.0) / (pointCountAsDouble - 2.0);
            this.ZSkew[cellIndex] = (float)zSkew;

            if (this.ZKurtosis != null)
            {
                // kurtosis is subject to numerical precision effects when calculated at double precision
                // Can be changed to Int128 if needed.
                double zKurtosis = (zSumFourthPower - 4.0 * zSumCubed * zMean + 6.0 * zSumSquared * zMean * zMean - 4.0 * zSum * zMean * zMean * zMean + pointCountAsDouble * zMean * zMean * zMean * zMean) / (zVariance * zVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
                this.ZKurtosis[cellIndex] = (float)zKurtosis;
            }

            if (this.ZPCumulative10 != null)
            {
                Debug.Assert((this.ZPCumulative20 != null) && (this.ZPCumulative30 != null) && (this.ZPCumulative40 != null) && (this.ZPCumulative50 != null) && (this.ZPCumulative60 != null) && (this.ZPCumulative70 != null) && (this.ZPCumulative80 != null) && (this.ZPCumulative90 != null));

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
            }

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
            if (this.IntensityKurtosis != null)
            {
                double intensityKurtosis = (intensitySumFourthPower - 4.0 * intensitySumCubed * intensityMean + 6.0 * intensitySumSquared * intensityMean * intensityMean - 4.0 * intensitySum * intensityMean * intensityMean * intensityMean + pointCountAsDouble * intensityMean * intensityMean * intensityMean * intensityMean) / (intensityVariance * intensityVariance) * pointCountAsDouble * (pointCountAsDouble + 1.0) / ((pointCountAsDouble - 1.0) * (pointCountAsDouble - 2.0) * (pointCountAsDouble - 3.0));
                this.IntensityKurtosis[cellIndex] = (float)intensityKurtosis;
            }

            if (this.IntensityTotal != null)
            {
                this.IntensityTotal[cellIndex] = (float)intensitySum;
            }

            int pointsBelowMedian = zQuantile50index;
            int pointsAboveMedian = pointCount - pointsBelowMedian;
            this.IntensityFirstReturn[cellIndex] = (float)intensityFirstReturnSum / (float)pointsByReturnNumber[1]; // NaN due to divide by zero if no first returns
            this.IntensityMeanAboveMedianZ[cellIndex] = (float)intensityAboveMedianSum / (float)pointsAboveMedian;
            this.IntensityMeanBelowMedianZ[cellIndex] = (float)intensityBelowMedianSum / (float)pointsBelowMedian;

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
            if (this.IntensityPGround != null)
            {
                this.IntensityPGround[cellIndex] = (float)(intensityGroundSum / intensitySumAsDouble);
            }

            // second z statistics pass now that z means are known
            int pointsAboveZMean = 0;
            int pointsAboveZThreshold = 0;
            float zCountThreshold = zThresholdInCrsUnits;
            if (dtmCells > 0)
            {
                zCountThreshold += this.ZGroundMean[cellIndex]; // make threshold relative to DTM
            }
            for (int pointIndex = 0; pointIndex < pointCount; ++pointIndex)
            {
                float z = pointsInCell.Z[pointIndex];
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

            // intensity quantiles
            if (this.IntensityQuantile10 != null)
            {
                if ((sortedIntensity == null) || (sortedIntensity.Length < pointCount))
                {
                    sortedIntensity = new UInt16[2 * pointCount];
                }
                pointsInCell.Intensity.CopyTo(sortedIntensity);
                Array.Sort(sortedIntensity, 0, pointCount);

                if (pointCount < 10)
                {
                    // if cell has less than 20 points then n * pointCount / 20 will be zero at least some of the time
                    // Negative indices result in this case due to the subtraction of 1.
                    this.IntensityQuantile10[cellIndex] = sortedIntensity[Int32.Max((int)(1.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile20[cellIndex] = sortedIntensity[Int32.Max((int)(2.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile30[cellIndex] = sortedIntensity[Int32.Max((int)(3.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile40[cellIndex] = sortedIntensity[Int32.Max((int)(4.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile50[cellIndex] = sortedIntensity[Int32.Max((int)(5.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile60[cellIndex] = sortedIntensity[Int32.Max((int)(6.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile70[cellIndex] = sortedIntensity[Int32.Max((int)(7.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile80[cellIndex] = sortedIntensity[Int32.Max((int)(8.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                    this.IntensityQuantile90[cellIndex] = sortedIntensity[Int32.Max((int)(9.0F * pointCount / 10.0F + 0.5F) - 1, 0)];
                }
                else
                {
                    this.IntensityQuantile10[cellIndex] = sortedIntensity[1 * pointCount / 10 - 1];
                    this.IntensityQuantile20[cellIndex] = sortedIntensity[2 * pointCount / 10 - 1];
                    this.IntensityQuantile30[cellIndex] = sortedIntensity[3 * pointCount / 10 - 1];
                    this.IntensityQuantile40[cellIndex] = sortedIntensity[4 * pointCount / 10 - 1];
                    this.IntensityQuantile50[cellIndex] = sortedIntensity[5 * pointCount / 10 - 1];
                    this.IntensityQuantile60[cellIndex] = sortedIntensity[6 * pointCount / 10 - 1];
                    this.IntensityQuantile70[cellIndex] = sortedIntensity[7 * pointCount / 10 - 1];
                    this.IntensityQuantile80[cellIndex] = sortedIntensity[8 * pointCount / 10 - 1];
                    this.IntensityQuantile90[cellIndex] = sortedIntensity[9 * pointCount / 10 - 1];
                }
            }
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || (String.Equals(this.AcceptedPoints.Name, name, StringComparison.Ordinal)))
            {
                band = this.AcceptedPoints;
            }
            else if (String.Equals(this.ZMax.Name, name, StringComparison.Ordinal))
            {
                band = this.ZMax;
            }
            else if (String.Equals(this.ZMean.Name, name, StringComparison.Ordinal))
            {
                band = this.ZMean;
            }
            else if (String.Equals(this.ZGroundMean.Name, name, StringComparison.Ordinal))
            {
                band = this.ZGroundMean;
            }
            else if (String.Equals(this.ZStandardDeviation.Name, name, StringComparison.Ordinal))
            {
                band = this.ZStandardDeviation;
            }
            else if (String.Equals(this.ZSkew.Name, name, StringComparison.Ordinal))
            {
                band = this.ZSkew;
            }
            else if (String.Equals(this.ZNormalizedEntropy.Name, name, StringComparison.Ordinal))
            {
                band = this.ZNormalizedEntropy;
            }
            else if (String.Equals(this.PZAboveZMean.Name, name, StringComparison.Ordinal))
            {
                band = this.PZAboveZMean;
            }
            else if (String.Equals(this.PZAboveThreshold.Name, name, StringComparison.Ordinal))
            {
                band = this.PZAboveThreshold;
            }
            else if ((this.ZQuantile05 != null) && String.Equals(this.ZQuantile05.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile05;
            }
            else if (String.Equals(this.ZQuantile10.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile10;
            }
            else if ((this.ZQuantile15 != null) && String.Equals(this.ZQuantile15.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile15;
            }
            else if (String.Equals(this.ZQuantile20.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile20;
            }
            else if ((this.ZQuantile25 != null) && String.Equals(this.ZQuantile25.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile25;
            }
            else if (String.Equals(this.ZQuantile30.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile30;
            }
            else if ((this.ZQuantile35 != null) && String.Equals(this.ZQuantile35.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile35;
            }
            else if (String.Equals(this.ZQuantile40.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile40;
            }
            else if ((this.ZQuantile45 != null) && String.Equals(this.ZQuantile45.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile45;
            }
            else if (String.Equals(this.ZQuantile50.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile50;
            }
            else if ((this.ZQuantile55 != null) && String.Equals(this.ZQuantile55.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile55;
            }
            else if (String.Equals(this.ZQuantile60.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile60;
            }
            else if ((this.ZQuantile65 != null) && String.Equals(this.ZQuantile65.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile65;
            }
            else if (String.Equals(this.ZQuantile70.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile70;
            }
            else if ((this.ZQuantile75 != null) && String.Equals(this.ZQuantile75.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile75;
            }
            else if (String.Equals(this.ZQuantile80.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile80;
            }
            else if ((this.ZQuantile85 != null) && String.Equals(this.ZQuantile85.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile85;
            }
            else if (String.Equals(this.ZQuantile90.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile90;
            }
            else if ((this.ZQuantile95 != null) && String.Equals(this.ZQuantile95.Name, name, StringComparison.Ordinal))
            {
                band = this.ZQuantile95;
            }
            else if (String.Equals(this.IntensityFirstReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityFirstReturn;
            }
            else if (String.Equals(this.IntensityMean.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityMean;
            }
            else if (String.Equals(this.IntensityMeanAboveMedianZ.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityMeanAboveMedianZ;
            }
            else if (String.Equals(this.IntensityMeanBelowMedianZ.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityMeanBelowMedianZ;
            }
            else if (String.Equals(this.IntensityMax.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityMax;
            }
            else if (String.Equals(this.IntensityStandardDeviation.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityStandardDeviation;
            }
            else if (String.Equals(this.IntensityMax.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityMax;
            }
            else if (String.Equals(this.IntensitySkew.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensitySkew;
            }
            else if (String.Equals(this.IntensityQuantile10.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile10;
            }
            else if (String.Equals(this.IntensityQuantile20.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile20;
            }
            else if (String.Equals(this.IntensityQuantile30.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile30;
            }
            else if (String.Equals(this.IntensityQuantile40.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile40;
            }
            else if (String.Equals(this.IntensityQuantile50.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile50;
            }
            else if (String.Equals(this.IntensityQuantile60.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile60;
            }
            else if (String.Equals(this.IntensityQuantile70.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile70;
            }
            else if (String.Equals(this.IntensityQuantile80.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile80;
            }
            else if (String.Equals(this.IntensityQuantile90.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityQuantile90;
            }
            else if (String.Equals(this.PFirstReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.PFirstReturn;
            }
            else if (String.Equals(this.PSecondReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.PSecondReturn;
            }
            else if (String.Equals(this.PThirdReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.PThirdReturn;
            }
            else if (String.Equals(this.PFourthReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.PFourthReturn;
            }
            else if (String.Equals(this.PFifthReturn.Name, name, StringComparison.Ordinal))
            {
                band = this.PFifthReturn;
            }
            else if (String.Equals(this.PGround.Name, name, StringComparison.Ordinal))
            {
                band = this.PGround;
            }
            else if ((this.IntensityPCumulativeZQ10 != null) && String.Equals(this.IntensityPCumulativeZQ10.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPCumulativeZQ10;
            }
            else if ((this.IntensityPCumulativeZQ30 != null) && String.Equals(this.IntensityPCumulativeZQ30.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPCumulativeZQ30;
            }
            else if ((this.IntensityPCumulativeZQ50 != null) && String.Equals(this.IntensityPCumulativeZQ50.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPCumulativeZQ50;
            }
            else if ((this.IntensityPCumulativeZQ70 != null) && String.Equals(this.IntensityPCumulativeZQ70.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPCumulativeZQ70;
            }
            else if ((this.IntensityPCumulativeZQ90 != null) && String.Equals(this.IntensityPCumulativeZQ90.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPCumulativeZQ90;
            }
            else if ((this.IntensityPGround != null) && String.Equals(this.IntensityPGround.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityPGround;
            }
            else if ((this.IntensityTotal != null) && String.Equals(this.IntensityTotal.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityTotal;
            }
            else if ((this.IntensityKurtosis != null) && String.Equals(this.IntensityKurtosis.Name, name, StringComparison.Ordinal))
            {
                band = this.IntensityKurtosis;
            }
            else if ((this.ZKurtosis != null) && String.Equals(this.ZKurtosis.Name, name, StringComparison.Ordinal))
            {
                band = this.ZKurtosis;
            }
            else if ((this.ZPCumulative10 != null) && String.Equals(this.ZPCumulative10.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative10;
            }
            else if ((this.ZPCumulative20 != null) && String.Equals(this.ZPCumulative20.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative20;
            }
            else if ((this.ZPCumulative30 != null) && String.Equals(this.ZPCumulative30.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative30;
            }
            else if ((this.ZPCumulative40 != null) && String.Equals(this.ZPCumulative40.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative40;
            }
            else if ((this.ZPCumulative50 != null) && String.Equals(this.ZPCumulative50.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative50;
            }
            else if ((this.ZPCumulative60 != null) && String.Equals(this.ZPCumulative60.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative60;
            }
            else if ((this.ZPCumulative70 != null) && String.Equals(this.ZPCumulative70.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative70;
            }
            else if ((this.ZPCumulative80 != null) && String.Equals(this.ZPCumulative80.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative80;
            }
            else if ((this.ZPCumulative90 != null) && String.Equals(this.ZPCumulative90.Name, name, StringComparison.Ordinal))
            {
                band = this.ZPCumulative90;
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
            if ((name == null) || String.Equals(name, this.AcceptedPoints.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 0;
                return true;
            }
            if (String.Equals(name, this.ZMax.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 1;
                return true;
            }
            if (String.Equals(name, this.ZMean.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 2;
                return true;
            }
            if (String.Equals(name, this.ZGroundMean.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 3;
                return true;
            }
            if (String.Equals(name, this.ZStandardDeviation.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 4;
                return true;
            }
            if (String.Equals(name, this.ZSkew.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 5;
                return true;
            }
            if (String.Equals(name, this.ZNormalizedEntropy.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 6;
                return true;
            }
            if (String.Equals(name, this.PZAboveZMean.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 7;
                return true;
            }
            if (String.Equals(name, this.PZAboveThreshold.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = 8;
                return true;
            }

            int bandIndex = 9;
            if (this.ZQuantile05 != null)
            {
                if (String.Equals(name, this.ZQuantile05.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile10.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile15 != null)
            {
                if (String.Equals(name, this.ZQuantile15.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile20.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile25 != null)
            {
                if (String.Equals(name, this.ZQuantile25.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile30.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile35 != null)
            {
                if (String.Equals(name, this.ZQuantile35.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile40.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile45 != null)
            {
                if (String.Equals(name, this.ZQuantile45.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile50.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile55 != null)
            {
                if (String.Equals(name, this.ZQuantile55.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile60.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile65 != null)
            {
                if (String.Equals(name, this.ZQuantile65.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile70.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile75 != null)
            {
                if (String.Equals(name, this.ZQuantile75.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile80.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile85 != null)
            {
                if (String.Equals(name, this.ZQuantile85.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.ZQuantile90.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (this.ZQuantile95 != null)
            {
                if (String.Equals(name, this.ZQuantile95.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (String.Equals(name, this.IntensityFirstReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityMean.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityMeanAboveMedianZ.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityMeanBelowMedianZ.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityMax.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityStandardDeviation.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensitySkew.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile10.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile20.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile30.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile40.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile50.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile60.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile70.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile80.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.IntensityQuantile90.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PFirstReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PSecondReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PThirdReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PFourthReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PFifthReturn.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;
            if (String.Equals(name, this.PGround.Name, StringComparison.OrdinalIgnoreCase))
            {
                bandIndexInFile = bandIndex;
                return true;
            }
            ++bandIndex;

            if (this.IntensityPCumulativeZQ10 != null)
            {
                if (String.Equals(name, this.IntensityPCumulativeZQ10.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityPCumulativeZQ30 != null)
            {
                if (String.Equals(name, this.IntensityPCumulativeZQ30.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityPCumulativeZQ50 != null)
            {
                if (String.Equals(name, this.IntensityPCumulativeZQ50.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityPCumulativeZQ70 != null)
            {
                if (String.Equals(name, this.IntensityPCumulativeZQ70.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityPCumulativeZQ90 != null)
            {
                if (String.Equals(name, this.IntensityPCumulativeZQ90.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityPGround != null)
            {
                if (String.Equals(name, this.IntensityPGround.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityTotal != null)
            {
                if (String.Equals(name, this.IntensityTotal.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.IntensityKurtosis != null)
            {
                if (String.Equals(name, this.IntensityKurtosis.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZKurtosis != null)
            {
                if (String.Equals(name, this.ZKurtosis.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }

            if (this.ZPCumulative10 != null)
            {
                if (String.Equals(name, this.ZPCumulative10.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative20 != null)
            {
                if (String.Equals(name, this.ZPCumulative20.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative30 != null)
            {
                if (String.Equals(name, this.ZPCumulative30.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative40 != null)
            {
                if (String.Equals(name, this.ZPCumulative40.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative50 != null)
            {
                if (String.Equals(name, this.ZPCumulative50.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative60 != null)
            {
                if (String.Equals(name, this.ZPCumulative60.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative70 != null)
            {
                if (String.Equals(name, this.ZPCumulative70.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative80 != null)
            {
                if (String.Equals(name, this.ZPCumulative80.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
                ++bandIndex;
            }
            if (this.ZPCumulative90 != null)
            {
                if (String.Equals(name, this.ZPCumulative90.Name, StringComparison.OrdinalIgnoreCase))
                {
                    bandIndexInFile = bandIndex;
                    return true;
                }
            }

            bandIndexInFile = -1;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.AcceptedPoints.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZMax.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZGroundMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZStandardDeviation.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZSkew.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZNormalizedEntropy.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PZAboveZMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PZAboveThreshold.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile05?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile10.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile15?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile20.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile25?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile30.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile35?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile40.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile45?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile50.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile55?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile60.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile65?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile70.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile75?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile80.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile85?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile90.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZQuantile95?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityFirstReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityMean.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityMeanAboveMedianZ.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityMeanBelowMedianZ.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityMax.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityStandardDeviation.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensitySkew.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile10.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile20.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile30.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile40.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile50.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile60.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile70.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile80.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityQuantile90.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PFirstReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PSecondReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PThirdReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PFourthReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PFifthReturn.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.PGround.TryTakeOwnershipOfDataBuffer(dataBufferPool);

            // optional bands
            this.IntensityPCumulativeZQ10?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityPCumulativeZQ30?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityPCumulativeZQ50?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityPCumulativeZQ70?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityPCumulativeZQ90?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityPGround?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityTotal?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.IntensityKurtosis?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZKurtosis?.TryTakeOwnershipOfDataBuffer(dataBufferPool);

            this.ZPCumulative10?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative20?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative30?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative40?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative50?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative60?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative70?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative80?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
            this.ZPCumulative90?.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string rasterPath, bool compress)
        {
            int gdalBandIndex = 0;
            using Dataset rasterDataset = this.CreateGdalRasterAndSetFilePath(rasterPath, this.GetBandCount(), DataType.GDT_Float32, compress);
            this.AcceptedPoints.Write(rasterDataset, ++gdalBandIndex);
            this.ZMax.Write(rasterDataset, ++gdalBandIndex);
            this.ZMean.Write(rasterDataset, ++gdalBandIndex);
            this.ZGroundMean.Write(rasterDataset, ++gdalBandIndex);
            this.ZStandardDeviation.Write(rasterDataset, ++gdalBandIndex);
            this.ZSkew.Write(rasterDataset, ++gdalBandIndex);
            this.ZNormalizedEntropy.Write(rasterDataset, ++gdalBandIndex);
            this.PZAboveZMean.Write(rasterDataset, ++gdalBandIndex);
            this.PZAboveThreshold.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile05?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile10.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile15?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile20.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile25?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile30.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile35?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile40.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile45?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile50.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile55?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile60.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile65?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile70.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile75?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile80.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile85?.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile90.Write(rasterDataset, ++gdalBandIndex);
            this.ZQuantile95?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityFirstReturn.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityMean.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityMeanAboveMedianZ.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityMeanBelowMedianZ.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityMax.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityStandardDeviation.Write(rasterDataset, ++gdalBandIndex);
            this.IntensitySkew.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile10.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile20.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile30.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile40.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile50.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile60.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile70.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile80.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityQuantile90.Write(rasterDataset, ++gdalBandIndex);
            this.PFirstReturn.Write(rasterDataset, ++gdalBandIndex);
            this.PSecondReturn.Write(rasterDataset, ++gdalBandIndex);
            this.PThirdReturn.Write(rasterDataset, ++gdalBandIndex);
            this.PFourthReturn.Write(rasterDataset, ++gdalBandIndex);
            this.PFifthReturn.Write(rasterDataset, ++gdalBandIndex);
            this.PGround.Write(rasterDataset, ++gdalBandIndex);

            // optional bands
            this.IntensityPCumulativeZQ10?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityPCumulativeZQ30?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityPCumulativeZQ50?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityPCumulativeZQ70?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityPCumulativeZQ90?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityPGround?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityTotal?.Write(rasterDataset, ++gdalBandIndex);
            this.IntensityKurtosis?.Write(rasterDataset, ++gdalBandIndex);
            this.ZKurtosis?.Write(rasterDataset, ++gdalBandIndex);

            this.ZPCumulative10?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative20?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative30?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative40?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative50?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative60?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative70?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative80?.Write(rasterDataset, ++gdalBandIndex);
            this.ZPCumulative90?.Write(rasterDataset, ++gdalBandIndex);
        }
    }
}
