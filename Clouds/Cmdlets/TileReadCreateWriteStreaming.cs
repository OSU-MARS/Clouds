using System;
using System.Diagnostics.CodeAnalysis;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Extensions;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TCreatedTile> : TileReadWrite
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class
        where TCreatedTile : Raster
    {
        private readonly TileStreamPosition<TSourceTile> tileReadPosition;
        private readonly TileStreamPosition<TCreatedTile> tileCreatePosition;
        private readonly TileStreamPosition<TCreatedTile> tileWritePosition;

        public long CellsWritten { get; set; } // 32 bit integer overflows on moderate to large datasets
        public RasterBandPool RasterBandPool { get; private init; }
        public int TilesCreated { get; set; }
        public bool TileCreationDoesNotRequirePreviousRow { get; init; }
        public int TileWritesInitiated { get; set; }

        public TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, VirtualRaster<TCreatedTile> destinationVrt, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            if ((sourceGrid.SizeX != destinationVrt.VirtualRasterSizeInTilesX) || (sourceGrid.SizeY != destinationVrt.VirtualRasterSizeInTilesY))
            {
                throw new ArgumentOutOfRangeException(nameof(destinationVrt), "Source grid and destination virtual raster must be the same size. The source grid is (" + sourceGrid.SizeX + ", " + sourceGrid.SizeY + ") tiles and the virtual raster is (" + destinationVrt.VirtualRasterSizeInTilesX + ", " + destinationVrt.VirtualRasterSizeInTilesY + ").");
            }
            if (destinationVrt.TileGrid == null)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationVrt), "Virtual raster's grid must be created before tiles can be streamed from it.");
            }

            bool[,] unpopulatedTileMap = sourceGrid.GetUnpopulatedCellMap();
            this.tileReadPosition = new(sourceGrid, unpopulatedTileMap);
            this.tileCreatePosition = new(destinationVrt.TileGrid, ArrayExtensions.DeepClone(unpopulatedTileMap));
            this.tileWritePosition = new(destinationVrt.TileGrid, ArrayExtensions.DeepClone(unpopulatedTileMap));

            this.CellsWritten = 0;
            this.RasterBandPool = new();
            this.TilesCreated = 0;
            this.TileCreationDoesNotRequirePreviousRow = false;
            this.TileWritesInitiated = 0;
        }

        public void OnTileCreated(int tileCreateIndexX, int tileCreateIndexY)
        {
            this.tileCreatePosition.OnTileCompleted(tileCreateIndexX, tileCreateIndexY);
            // return of source tiles must be restricted by destination tile creation
            // If left unconstrained, source tiles can be returned to pool before all referencing destination tiles are created.
            int maxReadReturnRowIndex = this.tileCreatePosition.CompletedRowIndex;
            if (this.TileCreationDoesNotRequirePreviousRow == false)
            {
                --maxReadReturnRowIndex;
            }
            this.tileReadPosition.TryReturnToRasterBandPool(this.RasterBandPool, maxReadReturnRowIndex);
            ++this.TilesCreated;
        }

        public void OnTileRead(int tileReadIndexX, int tileReadIndexY)
        {
            this.tileReadPosition.OnTileCompleted(tileReadIndexX, tileReadIndexY);
            ++this.TilesRead;
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, TCreatedTile tileWritten)
        {
            this.tileWritePosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY);
            this.tileWritePosition.TryReturnToRasterBandPool(this.RasterBandPool);
            this.CellsWritten += tileWritten.Cells;
            ++this.TilesWritten;
        }

        public bool TryGetNextTileCreation(out int tileCreateIndexX, out int tileCreateIndexY, [NotNullWhen(true)] out TSourceTile? sourceTile)
        {
            return this.tileReadPosition.TryGetNextTile(out tileCreateIndexX, out tileCreateIndexY, out sourceTile);
        }

        public bool TryGetNextTileToWrite(out int tileWriteIndexX, out int tileWriteIndexY, [NotNullWhen(true)] out TCreatedTile? tileToWrite)
        {
            return this.tileCreatePosition.TryGetNextTile(out tileWriteIndexX, out tileWriteIndexY, out tileToWrite);
        }
    }
}
