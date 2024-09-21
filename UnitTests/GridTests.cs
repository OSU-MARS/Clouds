using Mars.Clouds.Cmdlets;
using Mars.Clouds.GdalExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class GridTests
    {
        public TestContext? TestContext { get; set; }

        [TestMethod]
        public void VirtualRasterStreamReadOnly()
        {
            // mock a virtual raster
            SpatialReference crs = new(null);
            crs.ImportFromEPSG(32610);
            GridNullable<object> tileExtent = new(crs, new(0.0, 0.0, 1.0, -1.0), 1000, 1000);

            Random random = new();
            int vrtSizeX = random.Next(1, 24);
            int vrtSizeY = random.Next(1, 24);
            VirtualRaster<Raster<byte>> vrt = new();
            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
                    tileExtent.Transform.SetOrigin(1000.0 * xIndex, 1000.0 * yIndex);
                    vrt.Add(new Raster<byte>(tileExtent, [ "band1" ], Byte.MaxValue));
                }
            }

            vrt.CreateTileGrid();
            Assert.IsTrue((vrt.SizeInTilesX == vrtSizeX) && (vrt.SizeInTilesY == vrtSizeY) && (vrt.TileGrid != null));
            Assert.IsTrue((vrt.SizeInTilesX == vrt.TileGrid.SizeX) && (vrt.SizeInTilesY == vrt.TileGrid.SizeY));
            int tileCount = vrt.SizeInTilesX * vrt.SizeInTilesY;

            TileStreamPosition<Raster<byte>> vrtPosition = new(vrt.TileGrid, vrt.TileGrid.GetUnpopulatedCellMap())
            {
                TileReturnDoesNotRequirePreviousRow = random.Next(0, 2) == 1 // Next()'s upper bound is exclusive
            };
            int[] tileCompletionOrder = ArrayExtensions.CreateSequence(tileCount);
            ArrayExtensions.RandomizeOrder(tileCompletionOrder);

            RasterBandPool dataBufferPool = new();
            int previousCompletedRowIndex = vrtPosition.CompletedRowIndex;
            for (int index = 0; index < tileCompletionOrder.Length; ++index)
            {
                (int xIndex, int yIndex) = vrt.ToGridIndices(tileCompletionOrder[index]);
                vrtPosition.OnTileCompleted(xIndex, yIndex, (_, _, tile) => tile.ReturnBands(dataBufferPool));

                int firstRetainedRowIndex = vrtPosition.CompletedRowIndex;
                if (vrtPosition.TileReturnDoesNotRequirePreviousRow || (vrtPosition.CompletedRowIndex == vrtSizeY - 1))
                {
                    // if previous rows can be returned on completion then the first retained row is the first uncomplete row
                    // if all rows are completed then all tiles should be returned
                    // If both of these conditions are true, firstRetainedRowIndex should not exceed vrt.VirtualRasterSizeInTilesY
                    // since it's used as a row index.
                    ++firstRetainedRowIndex;
                }
                for (int returnedYindex = 0; returnedYindex < firstRetainedRowIndex; ++returnedYindex)
                {
                    for (int returnedXindex = 0; returnedXindex < vrtSizeX; ++returnedXindex)
                    {
                        Raster<byte>? tile = vrt[returnedXindex, returnedYindex];
                        Assert.IsTrue(tile != null, "Tile at " + returnedXindex + ", " + returnedYindex + " is null at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                        for (int bandIndex = 0; bandIndex < tile.Bands.Length; ++bandIndex)
                        {
                            Assert.IsTrue(tile.Bands[bandIndex].Data.Length == 0, "Band " + bandIndex + " of tile at " + returnedXindex + ", " + returnedYindex + " is not returned to data buffer pool at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster with tileReturnDoesNotRequirePreviousRow: " + vrtPosition.TileReturnDoesNotRequirePreviousRow + ".");
                        }
                    }
                }
                if ((firstRetainedRowIndex >= 0) && (firstRetainedRowIndex < vrtSizeY - 1))
                {
                    for (int completedXindex = 0; completedXindex < vrtSizeX; ++completedXindex)
                    {
                        Raster<byte>? tile = vrt[completedXindex, firstRetainedRowIndex];
                        Assert.IsTrue(tile != null, "Tile at " + completedXindex + ", " + firstRetainedRowIndex + " is null at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                        for (int bandIndex = 0; bandIndex < tile.Bands.Length; ++bandIndex)
                        {
                            Assert.IsTrue(tile.Bands[bandIndex].Data.Length > 0, "Band " + bandIndex + " of tile at " + completedXindex + ", " + firstRetainedRowIndex + " is returned to data buffer pool at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster with tileReturnDoesNotRequirePreviousRow: " + vrtPosition.TileReturnDoesNotRequirePreviousRow + ".");
                        }
                    }
                }

                int expectedTilesInPool = 0;
                if (vrtPosition.CompletedRowIndex >= 0) // CompletedRowIndex starts at -1
                {
                    int returnedRows = vrtPosition.CompletedRowIndex;
                    if (vrtPosition.TileReturnDoesNotRequirePreviousRow || (vrtPosition.CompletedRowIndex == vrtSizeY - 1))
                    {
                        ++returnedRows;
                    }
                    expectedTilesInPool = vrtSizeX * returnedRows;
                }
                Assert.IsTrue(dataBufferPool.BytePool.Count == expectedTilesInPool, "Expected " + expectedTilesInPool + " tiles to be returned to tile pool rather than " + dataBufferPool.BytePool.Count + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster completed to row " + vrtPosition.CompletedRowIndex + " (tile completion order " + String.Join(',', tileCompletionOrder) + ").");

                Assert.IsTrue(vrtPosition.CompletedRowIndex >= previousCompletedRowIndex, "Expected completed row index to be at least " + (previousCompletedRowIndex + 1) + " at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster with tileReturnDoesNotRequirePreviousRow: " + vrtPosition.TileReturnDoesNotRequirePreviousRow + "because one or more rows were completed but it is " + vrtPosition.CompletedRowIndex + ".");
                previousCompletedRowIndex = vrtPosition.CompletedRowIndex;
            }

            Assert.IsTrue(dataBufferPool.BytePool.Count == tileCount, "Tile pool contains " + dataBufferPool.BytePool.Count + " tiles after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
                    Assert.IsTrue(vrtPosition.IsCompleteTo(xIndex, yIndex), "Tile at " + xIndex + ", " + yIndex + " is not seen as complete after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");

                    Raster<byte>? tile = vrt[xIndex, yIndex];
                    Assert.IsTrue(tile != null, "Tile at " + xIndex + ", " + yIndex + " is null after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                    for (int bandIndex = 0; bandIndex < tile.Bands.Length; ++bandIndex)
                    {
                        Assert.IsTrue(tile.Bands[bandIndex].Data.Length == 0, "Band " + bandIndex + " of tile at " + xIndex + ", " + yIndex + " is not returned to tile pool after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                    }
                }
            }
        }
    }
}
