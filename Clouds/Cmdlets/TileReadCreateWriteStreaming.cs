using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Extensions;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TIntermediateData, TDestinationTile> : TileReadWrite<TIntermediateData> 
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class?
        where TDestinationTile : Raster
    {
        private readonly VirtualRaster<TDestinationTile> destinationVrt;
        private int pendingCreationIndexY;
        private int pendingIndexX;
        private int pendingIndexY;
        private readonly GridNullable<TSourceTile> sourceGrid;
        private readonly VirtualRasterTileStreamPosition<TDestinationTile> tileStreamPosition;

        public ObjectPool<TDestinationTile> TilePool { get; private init; }

        public TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, VirtualRaster<TDestinationTile> destinationVrt, int maxSimultaneouslyLoadedTiles, bool outputPathIsDirectory)
            : base(maxSimultaneouslyLoadedTiles, outputPathIsDirectory)
        {
            Debug.Assert((sourceGrid.SizeX == destinationVrt.VirtualRasterSizeInTilesX) && (sourceGrid.SizeY == destinationVrt.VirtualRasterSizeInTilesY));

            this.destinationVrt = destinationVrt;
            this.sourceGrid = sourceGrid;
            this.pendingCreationIndexY = 0;
            this.pendingIndexX = 0;
            this.pendingIndexY = 0;
            this.tileStreamPosition = new(destinationVrt, sourceGrid.GetUnpopulatedCellMap());

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
            for (int yIndex = this.pendingCreationIndexY; yIndex < this.destinationVrt.VirtualRasterSizeInTilesY; ++yIndex)
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
                    this.pendingCreationIndexY = yIndex;
                }
            }

            // see if a tile is available for write a row basis
            // In the single row case, tile writes can begin as soon as their +x neighbor's been created. This is not currently
            // handled. Neither are more complex cases where voids in the grid permit writes to start before the next row of DSM
            // tiles has completed loading.
            int dsmCreationCompletedIndexY = this.pendingCreationIndexY; // if DSM is fully created then all its tiles can be written
            if (this.pendingCreationIndexY < this.destinationVrt.VirtualRasterSizeInTilesY)
            {
                --dsmCreationCompletedIndexY; // row n of DSM isn't fully created yet so canopy maxima models for row n - 1 cannot yet be generated without edge effects
            }
            for (; this.pendingIndexY < dsmCreationCompletedIndexY; ++this.pendingIndexY)
            {
                for (; this.pendingIndexX < this.destinationVrt.VirtualRasterSizeInTilesX; ++this.pendingIndexX)
                {
                    TDestinationTile? createdTileCandidate = this.destinationVrt[this.pendingIndexX, this.pendingIndexY];
                    if (createdTileCandidate != null)
                    {
                        tileWriteIndexX = this.pendingIndexX;
                        tileWriteIndexY = this.pendingIndexY;
                        dsmTileToWrite = createdTileCandidate;

                        // advance to next grid position
                        ++this.pendingIndexX;
                        if (this.pendingIndexX >= this.destinationVrt.VirtualRasterSizeInTilesX)
                        {
                            ++this.pendingIndexY;
                            this.pendingIndexX = 0;
                        }
                        return true;
                    }
                }

                this.pendingIndexX = 0; // reset to beginning of row, next iteration of loop will increment in y
            }

            tileWriteIndexX = -1;
            tileWriteIndexY = -1;
            dsmTileToWrite = null;
            return false;
        }
    }
}
