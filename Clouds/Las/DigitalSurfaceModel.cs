using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster<float>
    {
        public RasterBand<float> CanopyHeight { get; private init; }
        public RasterBand<float> SurfaceModel { get; private init; }
        public RasterBand<float> Layer1 { get; private init; }
        public RasterBand<float> Layer2 { get; private init; }
        public RasterBand<float> RegionSize { get; private init; }

        public DigitalSurfaceModel(Grid<PointListZ> grid, RasterBand<float> dtmTile, float gapDistance, int layersToLog)
            : base(grid.Crs, grid.Transform, grid.XSize, grid.YSize, 1 + Int32.Max(2, layersToLog) + 2, Raster<float>.GetDefaultNoDataValue())
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gapDistance, 0.0F, nameof(gapDistance));
            ArgumentOutOfRangeException.ThrowIfLessThan(layersToLog, 2, nameof(layersToLog));

            this.SurfaceModel = this.Bands[0];
            this.SurfaceModel.Name = "dsm";
            this.Layer1 = this.Bands[1];
            this.Layer2 = this.Bands[2];
            for (int bandIndex = 1; bandIndex < this.BandCount - 2; ++bandIndex)
            {
                this.Bands[bandIndex].Name = "layer" + bandIndex; // ones based layer naming
            }
            this.CanopyHeight = this.Bands[this.BandCount - 2];
            this.CanopyHeight.Name = "chm";
            this.RegionSize = this.Bands[this.BandCount - 1];
            this.RegionSize.Name = "regionSize";

            // find layers, set default DSM as the highest z in each cell, and set default CHM
            // This loop also sorts each cell's points from highest to lowest z.
            float dsmNoData = this.SurfaceModel.NoDataValue;
            List<int> maxIndexByLayer = [];
            List<float> maxZbyLayer = [];
            for (int cellIndex = 0; cellIndex < this.CellsPerBand; ++cellIndex)
            {
                PointListZ? cellPoints = grid[cellIndex];
                if ((cellPoints == null) || (cellPoints.Count == 0))
                {
                    this.SurfaceModel[cellIndex] = dsmNoData;
                    continue;
                }

                cellPoints.Z.Sort(); // ascending order
                cellPoints.Z.Reverse(); // flip to descending
                float zMax = cellPoints.Z[0];
                float zPrevious = zMax;
                for (int pointIndex = 1; pointIndex < cellPoints.Count; ++pointIndex)
                {
                    float z = cellPoints.Z[pointIndex];
                    float zDelta = zPrevious - z;
                    if (zDelta > gapDistance)
                    {
                        maxIndexByLayer.Add(pointIndex);
                        maxZbyLayer.Add(z);
                    }

                    zPrevious = z;
                }

                this.SurfaceModel[cellIndex] = zMax;

                float dtmZ = dtmTile[cellIndex];
                this.CanopyHeight[cellIndex] = zMax - dtmZ;

                if (layersToLog > 0)
                {
                    this.Bands[1][cellIndex] = zMax;
                    int maxLayerIndex = Int32.Min(layersToLog, maxZbyLayer.Count) - 1;
                    for (int additionalLayerIndex = 0; additionalLayerIndex < maxLayerIndex; ++additionalLayerIndex)
                    {
                        this.Bands[additionalLayerIndex + 2][cellIndex] = maxZbyLayer[additionalLayerIndex];
                    }
                }

                maxZbyLayer.Clear();
            }

            // grow regions
            for (int yIndex = 0; yIndex < this.YSize; ++yIndex)
            {
                for (int xIndex = 0; xIndex < this.XSize; ++xIndex)
                {
                    int cellIndex = this.ToCellIndex(xIndex, yIndex);
                }
            }
        }
    }
}
