using Mars.Clouds.GdalExtensions;
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

        protected TreeCrownRaster(Dataset crownDataset, bool readData)
            : base(crownDataset) 
        {
            if (crownDataset.RasterCount != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(crownDataset), "Raster '" + this.FilePath + "' has " + crownDataset.RasterCount + " bands. Currently only single band rasters are supported.");
            }
            
            this.TreeID = new(crownDataset, crownDataset.GetRasterBand(1), readData);
            // should band name be tested against TreetopVector.TreeIDFieldName?
        }

        public TreeCrownRaster(Grid extent, RasterBandPool? dataBufferPool)
            : base(extent)
        {
            this.TreeID = new(this, TreetopVector.TreeIDFieldName, RasterBand.NoDataDefaultInt32, RasterBandInitialValue.NoData, dataBufferPool);
        }

        public static TreeCrownRaster CreateFromBandMetadata(string crownRasterPath)
        {
            using Dataset crownDataset = Gdal.Open(crownRasterPath, Access.GA_ReadOnly);
            TreeCrownRaster crownRaster = new(crownDataset, readData: false);
            Debug.Assert(String.Equals(crownRaster.FilePath, crownRasterPath));
            crownDataset.FlushCache();
            return crownRaster;
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
            using Dataset crownDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            for (int gdalBandIndex = 1; gdalBandIndex <= crownDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = crownDataset.GetRasterBand(gdalBandIndex);
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

            crownDataset.FlushCache();
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override void ReturnBandData(RasterBandPool dataBufferPool)
        {
            this.TreeID.ReturnData(dataBufferPool);
        }

        public void SegmentCrowns(TreeCrownSegmentationState segmentationState)
        {
            if ((segmentationState.DsmNeighborhood == null) || (segmentationState.ChmNeighborhood == null) ||
                (segmentationState.SlopeNeighborhood == null) || (segmentationState.AspectNeighborhood == null) ||
                (segmentationState.TreetopCostTile == null) || (segmentationState.TreetopNeighborhood == null))
            {
                throw new ArgumentOutOfRangeException(nameof(segmentationState), "Segmentation state is missing one or more neighborhoods. Call " + nameof(segmentationState.SetNeighborhoodsAndCellSize) + "() before calling " + nameof(this.SegmentCrowns) + "().");
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

            // clear any previous crown segmentation
            this.TreeID.FillNoData();

            // find minimum cost path for each DSM cell and mark tree IDs into cells
            int cellDsmEndIndexY = 0; // exclusive
            TreeCrownCostGrid crownCosts = segmentationState.TreetopCostTile;
            for (int treetopCellIndexY = 0; treetopCellIndexY < treetopsTile.SizeY; ++treetopCellIndexY)
            {
                int cellDsmEndIndexX = 0; // exclusive, needs resetting at start of each row
                int cellDsmStartIndexY = cellDsmEndIndexY; // inclusive
                for (int treetopCellIndexX = 0; treetopCellIndexX < treetopsTile.SizeX; ++treetopCellIndexX)
                {
                    (double treetopCellXmin, double treetopCellXmax, double treetopCellYmin, double treetopCellYmax) = treetopsTile.Transform.GetCellExtent(treetopCellIndexX, treetopCellIndexY);
                    int cellDsmStartIndexX = cellDsmEndIndexX; // inclusive
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

                    segmentationState.DsmStartIndexX = cellDsmStartIndexX;
                    segmentationState.DsmStartIndexY = cellDsmStartIndexY;
                    segmentationState.DsmEndIndexX = cellDsmEndIndexX;
                    segmentationState.DsmEndIndexY = cellDsmEndIndexY;

                    // evaluate DSM rows within this treetop cell
                    crownCosts.EnumerateCostFields(treetopCellIndexX, treetopCellIndexY, segmentationState);
                    for (int dsmIndexY = cellDsmStartIndexY; dsmIndexY < cellDsmEndIndexY; ++dsmIndexY)
                    {
                        for (int dsmIndexX = cellDsmStartIndexX; dsmIndexX < cellDsmEndIndexX; ++dsmIndexX)
                        {
                            float minimumCost = Single.PositiveInfinity;
                            int treeID = this.TreeID.NoDataValue;
                            foreach (TreeCrownCostField treetopField in crownCosts.ActiveFields)
                            {
                                if (treetopField.TryGetCellCost(dsmIndexX, dsmIndexY, out float treetopCost))
                                {
                                    if (treetopCost < minimumCost)
                                    {
                                        minimumCost = treetopCost;
                                        treeID = treetopField.TreeID;
                                    }
                                }
                            }
                            if (minimumCost < Single.PositiveInfinity)
                            {
                                this.TreeID[dsmIndexX, dsmIndexY] = treeID;
                            }
                        }
                    }

                    // cost fields in northwest corner are no longer needed
                    crownCosts.Return(treetopCellIndexX - 1, treetopCellIndexY - 1, segmentationState);
                }
            }

            // return last two rows of cost fields to pool
            for (int treetopCellIndexY = treetopsTile.SizeY - 2; treetopCellIndexY < treetopsTile.SizeY; ++treetopCellIndexY)
            {
                for (int treetopCellIndexX = 0; treetopCellIndexX < treetopsTile.SizeX; ++treetopCellIndexX)
                {
                    crownCosts.Return(treetopCellIndexX, treetopCellIndexY, segmentationState);
                }
            }
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

        public override bool TryGetBandLocation(string name, [NotNullWhen(true)] out string? bandFilePath, out int bandIndexInFile)
        {
            bandFilePath = this.FilePath;
            if (String.Equals(this.TreeID.Name, name, StringComparison.Ordinal))
            {
                bandIndexInFile = 0;
            }
            else
            {
                bandIndexInFile = -1;
                return false;
            }

            return true;
        }

        public override void TryTakeOwnershipOfDataBuffers(RasterBandPool dataBufferPool)
        {
            this.TreeID.TryTakeOwnershipOfDataBuffer(dataBufferPool);
        }

        public override void Write(string crownPath, bool compress)
        {
            Debug.Assert(this.TreeID.IsNoData(RasterBand.NoDataDefaultInt32));
            using Dataset crownDataset = this.CreateGdalRasterAndSetFilePath(crownPath, 1, DataType.GDT_Int32, compress);
            this.TreeID.Write(crownDataset, 1);
            this.FilePath = crownPath;
        }
    }
}
