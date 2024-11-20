using Mars.Clouds.Cmdlets;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Segmentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.OSR;
using System;
using System.Numerics;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class GridTests
    {
        public TestContext? TestContext { get; set; }

        /// <summary>
        /// Creates a dense virtual raster with tiles of the specified size.
        /// </summary>
        private static VirtualRaster<Raster<byte>> MockVirtualRaster(int vrtSizeX, int vrtSizeY, double cellSizeInCrsUnits, int tileSizeInCells)
        {
            // mock a virtual raster
            SpatialReference crs = new(null);
            crs.ImportFromEPSG(32610);
            GridNullable<object> tileExtent = new(crs, new(0.0, 0.0, cellSizeInCrsUnits, -cellSizeInCrsUnits), tileSizeInCells, tileSizeInCells);

            double tileSizeInCrsUnits = cellSizeInCrsUnits * tileSizeInCells;
            VirtualRaster<Raster<byte>> vrt = new();
            for (int yIndex = 0; yIndex < vrtSizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < vrtSizeX; ++xIndex)
                {
                    tileExtent.Transform.SetOrigin(tileSizeInCrsUnits * xIndex, tileSizeInCrsUnits * yIndex);
                    vrt.Add(new Raster<byte>(tileExtent, [ "band1" ], noDataValue: Byte.MaxValue)); // clones CRS and transform
                }
            }

            vrt.CreateTileGrid();
            return vrt;
        }

        [TestMethod]
        public void NeighborhoodSlices()
        {
            const int tileSizeInCells = 10;
            VirtualRaster<Raster<byte>> vrt = GridTests.MockVirtualRaster(vrtSizeX: 3, vrtSizeY: 3, cellSizeInCrsUnits: 1.0, tileSizeInCells);
            RasterNeighborhood8<byte> neighborhood = vrt.GetNeighborhood8<byte>(tileGridIndexX: 1, tileGridIndexY: 1, bandName: null);
            Assert.IsTrue((neighborhood.Northwest != null) && (neighborhood.North != null) && (neighborhood.Northeast != null) &&
                          (neighborhood.West != null) && (neighborhood.East != null) &&
                          (neighborhood.Southwest != null) && (neighborhood.South != null) && (neighborhood.Southeast != null));
            neighborhood.Northwest.Fill((byte)RasterDirection.Northwest);
            neighborhood.North.Fill((byte)RasterDirection.North);
            neighborhood.Northeast.Fill((byte)RasterDirection.Northeast);
            neighborhood.West.Fill((byte)RasterDirection.West);
            neighborhood.Center.Fill((byte)RasterDirection.None);
            neighborhood.East.Fill((byte)RasterDirection.East);
            neighborhood.Southwest.Fill((byte)RasterDirection.Southwest);
            neighborhood.South.Fill((byte)RasterDirection.South);
            neighborhood.Southeast.Fill((byte)RasterDirection.Southeast);

            byte[] wholeCenterSlice = GC.AllocateUninitializedArray<byte>(tileSizeInCells * tileSizeInCells);
            neighborhood.Slice(xOrigin: 0, yOrigin: 0, sizeX: tileSizeInCells, sizeY: tileSizeInCells, wholeCenterSlice);
            Assert.IsTrue(wholeCenterSlice.AllValuesAre((byte)RasterDirection.None));

            Span<byte> northwestSlice = stackalloc byte[5 * 6];
            Span<byte> northSlice = stackalloc byte[2 * 2];
            Span<byte> northeastSlice = stackalloc byte[8 * 6];
            Span<byte> westSlice = stackalloc byte[4 * 6];
            Span<byte> centerSlice = stackalloc byte[4 * 4];
            Span<byte> eastSlice = stackalloc byte[6 * 7];
            Span<byte> southwestSlice = stackalloc byte[4 * 5];
            Span<byte> southSlice = stackalloc byte[8 * 10];
            Span<byte> southeastSlice = stackalloc byte[8 * 6];

            northwestSlice.Fill(byte.MaxValue);
            northSlice.Fill(byte.MaxValue);
            northeastSlice.Fill(byte.MaxValue);
            westSlice.Fill(byte.MaxValue);
            centerSlice.Fill(byte.MaxValue);
            eastSlice.Fill(byte.MaxValue);
            southwestSlice.Fill(byte.MaxValue);
            southSlice.Fill(byte.MaxValue);
            southeastSlice.Fill(byte.MaxValue);

            neighborhood.Slice(xOrigin: -2, yOrigin: -3, sizeX: 5, sizeY: 6, northwestSlice);
            neighborhood.Slice(xOrigin: 8, yOrigin: -1, sizeX: 2, sizeY: 2, northSlice);
            neighborhood.Slice(xOrigin: 7, yOrigin: -4, sizeX: 8, sizeY: 6, northeastSlice);

            neighborhood.Slice(xOrigin: -1, yOrigin: 0, sizeX: 4, sizeY: 6, westSlice);
            neighborhood.Slice(xOrigin: 3, yOrigin: 3, sizeX: 4, sizeY: 4, centerSlice);
            neighborhood.Slice(xOrigin: 5, yOrigin: 3, sizeX: 6, sizeY: 7, eastSlice);

            neighborhood.Slice(xOrigin: -3, yOrigin: 9, sizeX: 4, sizeY: 5, southwestSlice);
            neighborhood.Slice(xOrigin: 0, yOrigin: 8, sizeX: 8, sizeY: 10, southSlice);
            neighborhood.Slice(xOrigin: 4, yOrigin: 6, sizeX: 8, sizeY: 6, southeastSlice);

            Span<RasterDirection> northwestExpected = [ RasterDirection.Northwest, RasterDirection.Northwest, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                        RasterDirection.Northwest, RasterDirection.Northwest, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                        RasterDirection.Northwest, RasterDirection.Northwest, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                        RasterDirection.West, RasterDirection.West, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                        RasterDirection.West, RasterDirection.West, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                        RasterDirection.West, RasterDirection.West, RasterDirection.None, RasterDirection.None, RasterDirection.None ];
            Span<RasterDirection> northExpected = [ RasterDirection.North, RasterDirection.North,
                                                    RasterDirection.None, RasterDirection.None ];
            Span<RasterDirection> northeastExpected = [ RasterDirection.North, RasterDirection.North, RasterDirection.North, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast,
                                                        RasterDirection.North, RasterDirection.North, RasterDirection.North, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast,
                                                        RasterDirection.North, RasterDirection.North, RasterDirection.North, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast,
                                                        RasterDirection.North, RasterDirection.North, RasterDirection.North, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast, RasterDirection.Northeast,
                                                        RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                        RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East ];
            Span<RasterDirection> westExpected = [ RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None,
                                                   RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None,
                                                   RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None,
                                                   RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None,
                                                   RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None,
                                                   RasterDirection.West, RasterDirection.West, RasterDirection.West, RasterDirection.None ];
            Span<RasterDirection> centerExpected = stackalloc RasterDirection[4 * 4];
            centerExpected.Fill(RasterDirection.None);
            Span<RasterDirection> eastExpected = [ RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East,
                                                   RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East, RasterDirection.East, RasterDirection.East ];
            Span<RasterDirection> southwestExpected = [ RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.None,
                                                        RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.None,
                                                        RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.South,
                                                        RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.South,
                                                        RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.Southwest, RasterDirection.South ];
            Span<RasterDirection> southExpected = [ RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                    RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South,
                                                    RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South ];
            Span<RasterDirection> southeastExpected = [ RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East,
                                                        RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East,
                                                        RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East,
                                                        RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.None, RasterDirection.East, RasterDirection.East,
                                                        RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.Southeast, RasterDirection.Southeast,
                                                        RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.South, RasterDirection.Southeast, RasterDirection.Southeast ];

            GridTests.VerifyElementsEqual(northwestExpected, northwestSlice);
            GridTests.VerifyElementsEqual(northExpected, northSlice);
            GridTests.VerifyElementsEqual(northeastExpected, northeastSlice);
            GridTests.VerifyElementsEqual(westExpected, westSlice);
            GridTests.VerifyElementsEqual(centerExpected, centerSlice);
            GridTests.VerifyElementsEqual(eastExpected, eastSlice);
            GridTests.VerifyElementsEqual(southwestExpected, southwestSlice);
            GridTests.VerifyElementsEqual(southExpected, southSlice);
            GridTests.VerifyElementsEqual(southeastExpected, southeastSlice);
        }

        private static bool VerifyElementsEqual(ReadOnlySpan<RasterDirection> expected, ReadOnlySpan<byte> actual)
        {
            if (expected.Length != actual.Length)
            {
                return false;
            }

            for (int index = 0; index < expected.Length; ++index)
            {
                if ((byte)expected[index] != actual[index])
                {
                    return false;
                }
            }

            return true;
        }

        [TestMethod]
        public void VirtualRasterStreamReadOnly()
        {
            // mock a virtual raster
            Random random = new();
            int vrtSizeX = random.Next(1, 24);
            int vrtSizeY = random.Next(1, 24);
            VirtualRaster<Raster<byte>> vrt = GridTests.MockVirtualRaster(vrtSizeX, vrtSizeY, cellSizeInCrsUnits: 1.0, tileSizeInCells: 1000);
            Assert.IsTrue((vrt.SizeInTilesX == vrtSizeX) && (vrt.SizeInTilesY == vrtSizeY) && (vrt.TileGrid != null));
            Assert.IsTrue((vrt.SizeInTilesX == vrt.TileGrid.SizeX) && (vrt.SizeInTilesY == vrt.TileGrid.SizeY));
            int tileCount = vrt.SizeInTilesX * vrt.SizeInTilesY;
            Assert.IsTrue(vrt.NonNullTileCount == tileCount);

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
                vrtPosition.OnTileCompleted(xIndex, yIndex, (_, _, tile) => tile.ReturnBandData(dataBufferPool));

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
