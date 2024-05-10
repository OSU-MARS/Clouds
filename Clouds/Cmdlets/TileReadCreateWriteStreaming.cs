using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Mars.Clouds.GdalExtensions;

namespace Mars.Clouds.Cmdlets
{
    public class TileReadCreateWriteStreaming<TSourceGrid, TSourceTile, TIntermediateData, TDestinationTile> : TileReadWrite<TIntermediateData> 
        where TSourceGrid : GridNullable<TSourceTile>
        where TSourceTile : class?
        where TDestinationTile : Raster
    {
        private readonly bool[,] completedWriteMap;
        private int pendingCreationIndexY;
        private int pendingFreeIndexY;
        private int pendingWriteCompletionIndexY;
        private int pendingWriteIndexX;
        private int pendingWriteIndexY;

        private readonly GridNullable<TSourceTile> sourceGrid;
        private readonly VirtualRaster<TDestinationTile> destinationVrt;

        public TileReadCreateWriteStreaming(GridNullable<TSourceTile> sourceGrid, VirtualRaster<TDestinationTile> destinationVrt, int maxSimultaneouslyLoadedTiles, bool outputPathIsDirectory)
            : base(maxSimultaneouslyLoadedTiles, outputPathIsDirectory)
        {
            this.completedWriteMap = sourceGrid.GetUnpopulatedCellMap();
            this.pendingCreationIndexY = 0;
            this.pendingFreeIndexY = 0;
            this.pendingWriteCompletionIndexY = 0;
            this.pendingWriteIndexX = 0;
            this.pendingWriteIndexY = 0;

            this.destinationVrt = destinationVrt;
            this.sourceGrid = sourceGrid;
        }

        public void OnTileWritten(int tileWriteIndexX, int tileWriteIndexY, TDestinationTile tileWritten)
        {
            this.completedWriteMap[tileWriteIndexX, tileWriteIndexY] = true;

            // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
            // Similar to code in TryGetNextTileWriteIndex().
            for (; this.pendingWriteCompletionIndexY < this.destinationVrt.VirtualRasterSizeInTilesY; ++this.pendingWriteCompletionIndexY)
            {
                bool dsmRowIncompletelyWritten = false;
                for (int xIndex = 0; xIndex < this.destinationVrt.VirtualRasterSizeInTilesX; ++xIndex)
                {
                    if (this.completedWriteMap[xIndex, this.pendingWriteCompletionIndexY] == false)
                    {
                        dsmRowIncompletelyWritten = true;
                        break;
                    }
                }

                if (dsmRowIncompletelyWritten)
                {
                    break;
                }
            }

            int dsmWriteCompletedIndexY = this.pendingWriteCompletionIndexY; // if DSM is fully written then all its tiles can be freed
            if (this.pendingWriteCompletionIndexY < this.destinationVrt.VirtualRasterSizeInTilesY)
            {
                --dsmWriteCompletedIndexY; // row n of DSM isn't fully written so row n - 1 cannot be written without impairing canopy maxima model creation
            }
            for (; this.pendingFreeIndexY < dsmWriteCompletedIndexY; ++this.pendingFreeIndexY)
            {
                this.destinationVrt.SetRowToNull(this.pendingFreeIndexY);
            }

            this.CellsWritten += tileWritten.Cells;
            ++this.TilesWritten;
        }

        public bool TryGetNextTileWriteIndex(out int tileWriteIndexX, out int tileWriteIndexY, [NotNullWhen(true)] out TDestinationTile? dsmTileToWrite)
        {
            Debug.Assert((this.sourceGrid.SizeX == this.destinationVrt.VirtualRasterSizeInTilesX) && (this.sourceGrid.SizeY == this.destinationVrt.VirtualRasterSizeInTilesY));

            // scan to see if creation of one or more rows of DSM tiles has been completed since the last call
            // Similar to code in OnTileWritten().
            for (; this.pendingCreationIndexY < this.destinationVrt.VirtualRasterSizeInTilesY; ++this.pendingCreationIndexY)
            {
                bool dsmRowIncompletelyCreated = false; // unlikely, but maybe no tiles to create in row
                for (int xIndex = 0; xIndex < this.destinationVrt.VirtualRasterSizeInTilesX; ++xIndex)
                {
                    if (this.sourceGrid[xIndex, this.pendingCreationIndexY] != null) // point cloud tile grid fully populated before point reads start
                    {
                        if (this.destinationVrt[xIndex, this.pendingCreationIndexY] == null)
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
            for (; this.pendingWriteIndexY < dsmCreationCompletedIndexY; ++this.pendingWriteIndexY)
            {
                for (; this.pendingWriteIndexX < this.destinationVrt.VirtualRasterSizeInTilesX; ++this.pendingWriteIndexX)
                {
                    TDestinationTile? createdTileCandidate = this.destinationVrt[this.pendingWriteIndexX, this.pendingWriteIndexY];
                    if (createdTileCandidate != null)
                    {
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
