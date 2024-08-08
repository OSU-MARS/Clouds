using DocumentFormat.OpenXml.Drawing.Charts;
using Mars.Clouds.Cmdlets;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.OSR;
using System;
using System.Linq;

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
            Grid<object> tileExtent = new(crs, new(0.0, 0.0, 1.0, -1.0), 1000, 1000);

            Random random = new();
            int vrtSizeX = random.Next(1, 8);
            int vrtSizeY = random.Next(1, 8);
            VirtualRaster<Raster<byte>> vrt = [];
            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
                    tileExtent.Transform.SetOrigin(1000.0 * xIndex, 1000.0 * yIndex);
                    vrt.Add(new Raster<byte>(tileExtent, ["band1"], Byte.MaxValue));
                }
            }

            vrt.CreateTileGrid();
            Assert.IsTrue((vrt.VirtualRasterSizeInTilesX == vrtSizeX) && (vrt.VirtualRasterSizeInTilesY == vrtSizeY) && (vrt.TileGrid != null));
            Assert.IsTrue((vrt.VirtualRasterSizeInTilesX == vrt.TileGrid.SizeX) && (vrt.VirtualRasterSizeInTilesY == vrt.TileGrid.SizeY));
            int tileCount = vrt.VirtualRasterSizeInTilesX * vrt.VirtualRasterSizeInTilesY;

            TileStreamPosition<Raster<byte>> vrtPosition = new(vrt.TileGrid, vrt.TileGrid.GetUnpopulatedCellMap());
            int[] tileCompletionOrder = ArrayExtensions.CreateSequence(tileCount);
            ArrayExtensions.RandomizeOrder(tileCompletionOrder);

            RasterBandPool dataBufferPool = new();
            for (int index = 0; index < tileCompletionOrder.Length; ++index)
            {
                (int xIndex, int yIndex) = vrt.ToGridIndices(tileCompletionOrder[index]);
                vrtPosition.OnTileCompleted(xIndex, yIndex);

                if ((index > tileCompletionOrder.Length / 2) && (random.Next(0, 2) == 1))
                {
                    if (vrtPosition.TryReturnToRasterBandPool(dataBufferPool))
                    {
                        int firstRetainedRowIndex = vrtPosition.CompletedRowIndex == vrtSizeY - 1 ? vrtPosition.CompletedRowIndex + 1 : vrtPosition.CompletedRowIndex;
                        for (int returnedYindex = 0; returnedYindex < firstRetainedRowIndex; ++returnedYindex)
                        {
                            for (int returnedXindex = 0; returnedXindex < vrtSizeX; ++returnedXindex)
                            {
                                Raster<byte>? tile = vrt[returnedXindex, returnedYindex];
                                Assert.IsTrue(tile != null, "Tile at " + returnedXindex + ", " + returnedYindex + " is null at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                                for (int bandIndex = 0; bandIndex < tile.Bands.Length; ++bandIndex)
                                {
                                    Assert.IsTrue(tile.Bands[bandIndex].Data.Length == 0, "Band " + bandIndex + " of tile at " + returnedXindex + ", " + returnedYindex + " is not returned to data buffer pool at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
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
                                    Assert.IsTrue(tile.Bands[bandIndex].Data.Length > 0, "Band " + bandIndex + " of tile at " + completedXindex + ", " + firstRetainedRowIndex + " is returned to data buffer pool at index " + index + " of tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                                }
                            }
                        }

                        int expectedTilesInPool = vrtSizeX * (vrtPosition.CompletedRowIndex + (vrtPosition.CompletedRowIndex == vrtSizeY - 1 ? 1 : 0));
                        Assert.IsTrue(dataBufferPool.BytePool.Count == expectedTilesInPool, "Expected " + expectedTilesInPool + " tiles to be returned to tile pool rather than " + dataBufferPool.BytePool.Count + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster completed to row " + vrtPosition.CompletedRowIndex + " (tile completion order " + String.Join(',', tileCompletionOrder) + ").");
                    }
                }
            }

            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
                    if (vrtPosition.IsCompleteTo(xIndex, yIndex) == false)
                    {
                        Assert.Fail("Tile at " + xIndex + ", " + yIndex + " is not seen as complete after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
                    }
                }
            }

            bool tilesRemainingToReturnToPool = dataBufferPool.BytePool.Count != tileCount;
            Assert.IsTrue(vrtPosition.TryReturnToRasterBandPool(dataBufferPool) == tilesRemainingToReturnToPool);
            Assert.IsTrue(dataBufferPool.BytePool.Count == tileCount, "Tile pool contains " + dataBufferPool.BytePool.Count + " tiles after tile completion order " + String.Join(',', tileCompletionOrder) + " for " + vrtSizeX + " by " + vrtSizeY + " virtual raster.");
            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
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
