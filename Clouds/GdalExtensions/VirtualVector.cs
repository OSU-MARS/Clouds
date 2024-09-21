using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.GdalExtensions
{
    public class VirtualVector<TTile> : VirtualLayer where TTile : class
    {
        public GridNullable<TTile> TileGrid { get; private set; }

        public VirtualVector(Grid tileGrid)
        {
            // no vector tiles have been added to the virtual vector yet, so leave this.NonNullTileCount at zero
            this.SizeInTilesX = tileGrid.SizeX;
            this.SizeInTilesY = tileGrid.SizeY;
            this.TileGrid = new(tileGrid, cloneCrsAndTransform: true);
        }

        public TTile? this[int tileIndex]
        {
            get { return this.TileGrid[tileIndex]; }
        }
        
        public TTile? this[int tileIndexX, int tileIndexY]
        {
            get { return this.TileGrid[tileIndexX, tileIndexY]; }
        }

        public override GridGeoTransform TileTransform
        {
            get
            {
                return this.TileGrid.Transform;
            }
        }

        public void Add(int tileIndexX, int tileIndexY, TTile tile)
        {
            TTile? existingTile = this.TileGrid[tileIndexX, tileIndexY];
            if (existingTile != null)
            {
                throw new InvalidOperationException("Tile cannot be added at (" + tileIndexX + ", " + tileIndexY + ") because a tile is already present at that location.");
            }

            this.TileGrid[tileIndexX, tileIndexY] = tile;
            ++this.NonNullTileCount;
        }

        public bool TryRemoveAt(int tileIndexX, int tileIndexY, [NotNullWhen(true)] out TTile? tile)
        {
            tile = this.TileGrid[tileIndexX, tileIndexY];
            if (tile != null)
            {
                this.TileGrid[tileIndexX, tileIndexY] = null;
                --this.NonNullTileCount;
                return true;
            }

            return false;
        }
    }
}
