using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Extensions;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TDestinationTile> : TileReadWrite
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class?
        where TDestinationTile : Raster
    {
        private int creationCompletedIndexY;
        private readonly VirtualRaster<TDestinationTile> destinationVrt;
        private int pendingWriteIndexX;
        private int pendingWriteIndexY;
        private readonly GridNullable<TSourceTile> sourceGrid;
        private readonly VirtualRasterTileStreamPosition<TDestinationTile> tileStreamPosition;

        public long CellsWritten { get; set; } // 32 bit integer overflows on moderate to large datasets
        public ObjectPool<TDestinationTile> TilePool { get; private init; }

        public TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, VirtualRaster<TDestinationTile> destinationVrt, bool outputPathIsDirectory)
            : base(outputPathIsDirectory)
        {
            Debug.Assert((sourceGrid.SizeX == destinationVrt.VirtualRasterSizeInTilesX) && (sourceGrid.SizeY == destinationVrt.VirtualRasterSizeInTilesY));

            this.creationCompletedIndexY = -1;
            this.destinationVrt = destinationVrt;
            this.pendingWriteIndexX = 0;
            this.pendingWriteIndexY = 0;
            this.sourceGrid = sourceGrid;
            this.tileStreamPosition = new(destinationVrt, sourceGrid.GetUnpopulatedCellMap());

            this.CellsWritten = 0;
            this.TilePool = new();
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, TDestinationTile tileWritten)
        {
            this.tileStreamPosition.OnTileCompleted(tileWriteIndexX, tileWriteIndexY);
            this.tileStreamPosition.TryReturnTilesToObjectPool(this.TilePool);
            this.CellsWritten += tileWritten.Cells;
            ++this.TilesWritten;
        }

        public bool TryGetNextTileWriteIndex(out int tileWriteIndexX, out int tileWriteIndexY, [NotNullWhen(true)] out TDestinationTile? dsmTileToWrite)
        {
            // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
            // Similar to code in VirtualRasterTileStreamPosition.OnTileWritten().
            for (int yIndex = this.creationCompletedIndexY + 1; yIndex < this.destinationVrt.VirtualRasterSizeInTilesY; ++yIndex)
            {
                bool dsmRowIncompletelyCreated = false; // unlikely, but maybe no tiles to create in row
                for (int xIndex = 0; xIndex < this.destinationVrt.VirtualRasterSizeInTilesX; ++xIndex)
                {
                    if (this.sourceGrid[xIndex, yIndex] != null) // point cloud tile grid fully populated before point reads start
                    {
                        if (this.destinationVrt[xIndex, yIndex] == null)
                        {
                            dsmRowIncompletelyCreated = true;
                            break;
                        }
                    }
                }

                if (dsmRowIncompletelyCreated)
                {
                    break;
                }
                else
                {
                    Debug.Assert(this.pendingWriteIndexY <= yIndex);
                    this.creationCompletedIndexY = yIndex;
                }
            }

            // see if a tile is available for write a row basis
            // In the single row case, tile writes can begin as soon as their +x neighbor's been created. This is not currently
            // handled. Neither are more complex cases where voids in the grid permit writes to start before the next row of DSM
            // tiles has completed loading.
            int maxWriteableIndexY = this.creationCompletedIndexY; // if all tiles are created then all tiles can be written
            if (this.creationCompletedIndexY < this.destinationVrt.VirtualRasterSizeInTilesY - 1)
            {
                --maxWriteableIndexY; // row n of DSM isn't fully created yet so canopy maxima models for row n - 1 cannot yet be generated without edge effects
            }
            for (; this.pendingWriteIndexY <= maxWriteableIndexY; ++this.pendingWriteIndexY)
            {
                for (; this.pendingWriteIndexX < this.destinationVrt.VirtualRasterSizeInTilesX; ++this.pendingWriteIndexX)
                {
                    TDestinationTile? createdTileCandidate = this.destinationVrt[this.pendingWriteIndexX, this.pendingWriteIndexY];
                    if (createdTileCandidate == null)
                    {
                        // since row is completed a null cell in the virtual raster indicates there's no tile to write at this position
                        continue;
                    }

                    tileWriteIndexX = this.pendingWriteIndexX;
                    tileWriteIndexY = this.pendingWriteIndexY;
                    dsmTileToWrite = createdTileCandidate;

                    // advance to next grid position
                    ++this.pendingWriteIndexX;
                    if (this.pendingWriteIndexX >= this.destinationVrt.VirtualRasterSizeInTilesX)
                    {
                        ++this.pendingWriteIndexY;
                        this.pendingWriteIndexX = 0;
                    }
                    return true;
                }

                this.pendingWriteIndexX = 0; // reset to beginning of row, next iteration of loop will increment in y
            }

            tileWriteIndexX = -1;
            tileWriteIndexY = -1;
            dsmTileToWrite = null;
            return false;
        }
    }
}
