using Mars.Clouds.Extensions;
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

        public void SegmentCrowns(TreetopsGrid treetopsGrid, TreeCrownSegmentationState segmentationState)
        {
            // build initial set of cost fields
            ObjectPool<TreeCrownCostField> fieldPool = segmentationState.FieldPool;
            Treetops treetops = treetopsGrid[0, 0];
            List<TreeCrownCostField> treetopFields = new(treetops.Count);
            for (int treetopIndex = 0; treetopIndex < treetops.Count; ++treetopIndex)
            {
                if (fieldPool.TryGet(out TreeCrownCostField? treetopField) == false)
                {
                    treetopField = new();
                }

                treetopField.MoveToTreetopAndRecalculate(treetops.XIndex[treetopIndex], treetops.YIndex[treetopIndex], treetops.ID[treetopIndex], (float)(treetops.Elevation[treetopIndex] + treetops.Height[treetopIndex]), segmentationState);
                treetopFields.Add(treetopField);
            }

            // find minimum cost path for each cell and mark by tree ID
            Debug.Assert(this.TreeID.HasNoDataValue);
            Array.Fill(this.TreeID.Data, this.TreeID.NoDataValue, 0, this.TreeID.Data.Length);

            for (int dsmIndexY = 0; dsmIndexY < this.SizeY; ++dsmIndexY)
            {
                for (int dsmIndexX = 0; dsmIndexX < this.SizeX; ++dsmIndexX)
                {
                    float minimumCost = Single.PositiveInfinity;
                    int treeID = this.TreeID.NoDataValue;
                    for (int treetopIndex = 0; treetopIndex < treetops.Count; ++treetopIndex)
                    {
                        TreeCrownCostField treetopField = treetopFields[treetopIndex];
                        float treetopCost = treetopField[dsmIndexX, dsmIndexY];
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

            // return remaining fields to pool
            for (int treetopIndex = 0; treetopIndex < treetopFields.Count; ++treetopIndex)
            {
                fieldPool.Return(treetopFields[treetopIndex]);
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
