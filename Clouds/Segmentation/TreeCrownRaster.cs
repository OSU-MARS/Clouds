﻿using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Segmentation
{
    public class TreeCrownRaster : Raster
    {
        public RasterBand<int> TreeID { get; private init; }

        public TreeCrownRaster(Grid extent, RasterBandPool? dataBufferPool)
            : base(extent)
        {
            this.TreeID = new(this, TreetopVector.TreeIDFieldName, RasterBand.NoDataDefaultInt32, RasterBandInitialValue.NoData, dataBufferPool);
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(this.TreeID.Name, name, StringComparison.Ordinal))
            {
                return 0;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "Band '" + name + "' is not present in a local maxima raster.");
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.TreeID;
        }

        public override List<RasterBandStatistics> GetBandStatistics()
        {
            return [ this.TreeID.GetStatistics() ];
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
                    case TreetopVector.TreeIDFieldName:
                        this.TreeID.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in local maxima raster '" + this.FilePath + "'.");
                }
            }
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBands(RasterBandPool dataBufferPool)
        {
            this.TreeID.ReturnData(dataBufferPool);
        }

        public void SegmentCrowns(TreeCrownSegmentationState segmentationState)
        {
            if ((segmentationState.DsmNeighborhood == null) || (segmentationState.ChmNeighborhood == null) ||
                (segmentationState.SlopeNeighborhood == null) || (segmentationState.AspectNeighborhood == null) ||
                (segmentationState.TreetopCostTile == null) || (segmentationState.TreetopNeighborhood == null))
            {
                throw new ArgumentOutOfRangeException(nameof(segmentationState), "Segmentation state is missing one or more neighborhoods. Call " + nameof(segmentationState.SetNeighborhoods) + "() before calling " + nameof(this.SegmentCrowns) + "().");
            }

            RasterBand<float> dsmTile = segmentationState.DsmNeighborhood.Center;
            TreetopsGrid treetopsTile = segmentationState.TreetopNeighborhood.Center;
            if ((dsmTile.Transform.OriginX != treetopsTile.Transform.OriginX) || (dsmTile.Transform.OriginY != treetopsTile.Transform.OriginY))
            {
                throw new NotSupportedException("DSM and treetop tile origins are not identical. DSM tile origin (" + dsmTile.Transform.OriginX + ", " + dsmTile.Transform.OriginY + "), treetop origin (" + treetopsTile.Transform.OriginX + ", " + treetopsTile.Transform.OriginY + ").");
            }
            if ((dsmTile.Transform.CellHeight >= 0.0) || (treetopsTile.Transform.CellHeight >= 0.0))
            {
                throw new NotSupportedException("Currently, only negative DSM and treetop cell heights are supported. DSM cell height " + dsmTile.Transform.CellHeight + ", treetop cell height " + treetopsTile.Transform.CellHeight + ".");
            }

            // reset values
            Debug.Assert(this.TreeID.HasNoDataValue);
            Array.Fill(this.TreeID.Data, this.TreeID.NoDataValue, 0, this.TreeID.Data.Length);

            // find minimum cost path for each DSM cell and mark tree IDs into cells
            int cellDsmEndIndexX = 0; // exclusive
            int cellDsmEndIndexY = 0; // exclusive
            TreeCrownCostGrid crownCosts = segmentationState.TreetopCostTile;
            for (int treetopCellIndexY = 0; treetopCellIndexY < treetopsTile.SizeY; ++treetopCellIndexY)
            {
                for (int treetopCellIndexX = 0; treetopCellIndexX < treetopsTile.SizeX; ++treetopCellIndexX)
                {
                    (double treetopCellXmin, double treetopCellXmax, double treetopCellYmin, double treetopCellYmax) = treetopsTile.Transform.GetCellExtent(treetopCellIndexX, treetopCellIndexY);
                    int cellDsmStartIndexX = cellDsmEndIndexX;
                    int cellDsmStartIndexY = cellDsmEndIndexY;
                    (cellDsmEndIndexX, cellDsmEndIndexY) = dsmTile.ToGridIndices(treetopCellXmax, treetopCellYmin);
                    if (cellDsmEndIndexX > dsmTile.SizeX)
                    {
                        cellDsmEndIndexX = dsmTile.SizeX; // since treetop grid is spanning, it's likely to extend beyond the DSM tile
                    }
                    if (cellDsmEndIndexY > dsmTile.SizeY)
                    {
                        cellDsmEndIndexY = dsmTile.SizeY;
                    }
                    // TODO: is padding needed (by one cell?) here to handle numerical edge cases?

                    // evaluate DSM rows within this treetop cell
                    crownCosts.GetNeighborhood8(treetopCellIndexX + 1, treetopCellIndexY + 1, segmentationState.TreetopNeighborhood);
                    for (int dsmIndexY = cellDsmStartIndexY; dsmIndexY < cellDsmEndIndexY; ++dsmIndexY)
                    {
                        for (int dsmIndexX = cellDsmStartIndexX; dsmIndexX < cellDsmEndIndexX; ++dsmIndexX)
                        {
                            float minimumCost = Single.PositiveInfinity;
                            int treeID = this.TreeID.NoDataValue;
                            foreach (TreeCrownCostField treetopField in crownCosts.ActiveNeighborhood)
                            {
                                float treetopCost = treetopField[dsmIndexX, dsmIndexY]; // TODO: TryGet()
                                if (treetopCost < minimumCost)
                                {
                                    minimumCost = treetopCost;
                                    treeID = treetopField.TreeID;
                                }
                            }
                            if (minimumCost < Single.PositiveInfinity)
                            {
                                this.TreeID[dsmIndexX, dsmIndexY] = treeID;
                            }
                        }
                    }
                }
            }

            // TODO: return remaining cost fields to pool
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if (String.Equals(this.TreeID.Name, name, StringComparison.Ordinal))
            {
                band = this.TreeID;
                return true;
            }

            band = null;
            return false;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.TreeID.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string crownPath, bool compress)
        {
            Debug.Assert(this.TreeID.IsNoData(RasterBand.NoDataDefaultInt32));
            using Dataset crownDataset = this.CreateGdalRasterAndSetFilePath(crownPath, 3, DataType.GDT_Byte, compress);
            this.TreeID.Write(crownDataset, 1);
            this.FilePath = crownPath;
        }
    }
}
