using Mars.Clouds.Las;
using OSGeo.OSR;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    public class VirtualRaster
    {
        private SpatialReference? crs;

        public double TileCellSizeX { get; protected set; }
        public double TileCellSizeY { get; protected set; }
        public int TileSizeInCellsX { get; protected set; }
        public int TileSizeInCellsY { get; protected set; }

        public int VirtualRasterSizeInTilesX { get; protected set; }
        public int VirtualRasterSizeInTilesY { get; protected set; }

        protected VirtualRaster()
        {
            this.crs = null;

            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
        }

        public SpatialReference Crs
        {
            get
            {
                if (this.crs == null)
                {
                    throw new InvalidOperationException("Virtual raster's CRS is unknown as no tiles have been added to it. Call Add() before accessing " + nameof(this.Crs) + " { get; }.");
                }
                return this.crs;
            }
            protected set { this.crs = value; }
        }
    }

    // could derive from Grid but naming becomes confusing as differences between tiles and cells are obscured
    // Also, indexing differs because tiles are sparse where grid is dense, so a grid index is not a tile index.
    public class VirtualRaster<TTile> : VirtualRaster, IEnumerable<TTile> where TTile : Raster
    {
        private GridNullable<TTile?>? tileGrid;
        private string?[,]? tileNames;
        private readonly List<string> ungriddedTileNames;
        private readonly List<TTile> ungriddedTiles;

        public int TileCount { get; private set; }

        public VirtualRaster()
        {
            this.tileGrid = null;
            this.tileNames = null;
            this.ungriddedTileNames = [];
            this.ungriddedTiles = [];
        }

        public VirtualRaster(LasTileGrid lasGrid)
        {
            this.tileGrid = new(lasGrid);
            this.tileNames = new string?[this.tileGrid.SizeX, this.tileGrid.SizeY];
            this.ungriddedTileNames = [];
            this.ungriddedTiles = [];

            this.Crs = lasGrid.Crs.Clone();
            this.TileCellSizeX = Double.NaN; // no DTM information available, so no cell size information yet
            this.TileCellSizeY = Double.NaN;
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
            this.VirtualRasterSizeInTilesX = lasGrid.SizeX;
            this.VirtualRasterSizeInTilesY = lasGrid.SizeY;
        }

        public TTile? this[int cellIndex]
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid[cellIndex]; 
            }
        }

        public TTile? this[int xIndex, int yIndex]
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid[xIndex, yIndex]; 
            }
        }

        public GridGeoTransform TileTransform
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid.Transform; 
            }
        }

        public (int tileIndexX, int tileIndexY) Add(string tileName, TTile tile)
        {
            if ((this.tileGrid == null) && (this.ungriddedTiles.Count == 0))
            {
                // if no CRS information was specified at construction, latch CRS of first tile added
                this.Crs = tile.Crs;
                double tileLinearUnits = tile.Crs.GetLinearUnits();
                if (tileLinearUnits <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's unit size of " + tileLinearUnits + " m is zero or negative.");
                }
            }
            else if (SpatialReferenceExtensions.IsSameCrs(tile.Crs, this.Crs) == false)
            {
                // all tiles must be in the same CRS
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles have varying coordinate systems. Expected tile CRS to be " + this.Crs.GetName() + " but passed tile uses " + tile.Crs.GetName() + ".");
            }

            if (this.TileSizeInCellsX == -1)
            {
                // latch cell size first tile added
                Debug.Assert(Double.IsNaN(this.TileCellSizeX) && Double.IsNaN(this.TileCellSizeY) && (this.TileSizeInCellsY == -1));

                this.TileCellSizeX = tile.Transform.CellWidth;
                this.TileCellSizeY = tile.Transform.CellHeight;
                if ((this.TileCellSizeX <= 0.0) || (this.TileCellSizeY >= 0.0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's cell size of " + this.TileCellSizeX + " by " + this.TileCellSizeY + " has zero or negative width or zero or positive height. Tiles are expected to have negative heights such that raster indices increase with southing.");
                }

                this.TileSizeInCellsX = tile.SizeX;
                this.TileSizeInCellsY = tile.SizeY;
                if ((tile.SizeX <= 0) || (tile.SizeY <= 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's size of " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsX + " cells is zero or negative.");
                }
            }
            else if ((this.TileCellSizeX != tile.Transform.CellWidth) || (this.TileCellSizeY != tile.Transform.CellHeight))
            {
                // cell size must be the same across all tiles
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles are of varying size. Expected " + this.TileCellSizeX + " by " + this.TileCellSizeY + " cells but passed tile has " + tile.Transform.CellWidth + " by " + tile.Transform.CellHeight + " cells.");
            }

            if ((tile.SizeX != this.TileSizeInCellsX) || (tile.SizeY != this.TileSizeInCellsY))
            {
                // tile size must be the same across all tiles => tile size in cells must be the same
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles are of varying size. Expected " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsY + " cells but passed tile is " + tile.SizeX + " by " + tile.SizeY + " cells.");
            }

            int tileIndexX = -1;
            int tileIndexY = -1;
            if (this.tileGrid != null)
            {
                (tileIndexX, tileIndexY) = this.PlaceTileInGrid(tileName, tile);
            }
            else
            {
                this.ungriddedTileNames.Add(tileName);
                this.ungriddedTiles.Add(tile);
            }

            ++this.TileCount;
            return (tileIndexX, tileIndexY);
        }

        public void CreateTileGrid()
        {
            if (this.tileGrid != null)
            {
                throw new InvalidOperationException("Grid is already built."); // debatable if one time call needs to be enforced
            }

            double maximumOriginX = Double.MinValue;
            double maximumOriginY = Double.MinValue;
            double minimumOriginX = Double.MaxValue;
            double minimumOriginY = Double.MaxValue;
            for (int tileIndex = 0; tileIndex < this.ungriddedTiles.Count; ++tileIndex)
            {
                TTile tile = this.ungriddedTiles[tileIndex];
                if (maximumOriginX < tile.Transform.OriginX)
                {
                    maximumOriginX = tile.Transform.OriginX;
                }
                if (maximumOriginY < tile.Transform.OriginY)
                {
                    maximumOriginY = tile.Transform.OriginY;
                }
                if (minimumOriginX > tile.Transform.OriginX)
                {
                    minimumOriginX = tile.Transform.OriginX;
                }
                if (minimumOriginY > tile.Transform.OriginY)
                {
                    minimumOriginY = tile.Transform.OriginY;
                }
            }

            Debug.Assert(this.TileCellSizeY < 0.0, "Tile y indices do not increase with southing.");
            double tileSizeInTileUnitsX = this.TileCellSizeX * this.TileSizeInCellsX;
            double tileSizeInTileUnitsY = this.TileCellSizeY * this.TileSizeInCellsY;
            this.VirtualRasterSizeInTilesX = (int)Double.Round((maximumOriginX - minimumOriginX) / tileSizeInTileUnitsX) + 1;
            this.VirtualRasterSizeInTilesY = (int)Double.Round((minimumOriginY - maximumOriginY) / tileSizeInTileUnitsY) + 1;

            GridGeoTransform tileTransform = new(minimumOriginX, maximumOriginY, tileSizeInTileUnitsX, tileSizeInTileUnitsY);
            
            this.tileGrid = new(this.Crs, tileTransform, this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY);
            this.tileNames = new string[this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY];

            for (int tileIndex = 0; tileIndex < this.ungriddedTiles.Count; ++tileIndex)
            {
                this.PlaceTileInGrid(this.ungriddedTileNames[tileIndex], this.ungriddedTiles[tileIndex]);
            }

            this.ungriddedTileNames.Clear();
            this.ungriddedTiles.Clear();
        }

        public VirtualRasterEnumerator<TTile> GetEnumerator()
        {
            return new VirtualRasterEnumerator<TTile>(this);
        }

        public VirtualRasterNeighborhood8<TBand> GetNeighborhood8<TBand>(int tileGridIndexX, int tileGridIndexY, string? bandName) where TBand : INumber<TBand>
        {
            Debug.Assert(this.tileGrid != null);

            TTile? center = this.tileGrid[tileGridIndexX, tileGridIndexY];
            if (center == null)
            {
                throw new NotSupportedException("No tile is loaded at index (" + tileGridIndexX + ", " + tileGridIndexY + ").");
            }

            int northIndex = tileGridIndexY - 1;
            int southIndex = tileGridIndexY + 1;
            int eastIndex = tileGridIndexX + 1;
            int westIndex = tileGridIndexX - 1;

            TTile? north = null;
            TTile? northeast = null;
            TTile? northwest = null;
            if (northIndex >= 0)
            {
                north = this.tileGrid[tileGridIndexX, northIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    northeast = this.tileGrid[eastIndex, northIndex];
                }
                if (westIndex >= 0)
                {
                    northwest = this.tileGrid[westIndex, northIndex];
                }
            }

            TTile? south = null;
            TTile? southeast = null;
            TTile? southwest = null;
            if (southIndex < this.VirtualRasterSizeInTilesY)
            {
                south = this.tileGrid[tileGridIndexX, southIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    southeast = this.tileGrid[eastIndex, southIndex];
                }
                if (westIndex >= 0)
                {
                    southwest = this.tileGrid[westIndex, southIndex];
                }
            }

            TTile? east = null;
            if (eastIndex < this.VirtualRasterSizeInTilesX)
            {
                east = this.tileGrid[eastIndex, tileGridIndexY];
            }

            TTile? west = null;
            if (westIndex >= 0)
            {
                west = this.tileGrid[westIndex, tileGridIndexY];
            }

            return new VirtualRasterNeighborhood8<TBand>((RasterBand<TBand>)center.GetBand(bandName))
            {
                North = (RasterBand<TBand>?)north?.GetBand(bandName),
                Northeast = (RasterBand<TBand>?)northeast?.GetBand(bandName),
                Northwest = (RasterBand<TBand>?)northwest?.GetBand(bandName),
                South = (RasterBand<TBand>?)south?.GetBand(bandName),
                Southeast = (RasterBand<TBand>?)southeast?.GetBand(bandName),
                Southwest = (RasterBand<TBand>?)southwest?.GetBand(bandName),
                East = (RasterBand<TBand>?)east?.GetBand(bandName),
                West = (RasterBand<TBand>?)west?.GetBand(bandName)
            };
        }

        public string GetTileName(int tileGridIndexX, int tileGridIndexY)
        {
            Debug.Assert(this.tileNames != null);
            string? tileName = this.tileNames[tileGridIndexX, tileGridIndexY];
            if (tileName == null)
            {
                throw new InvalidOperationException("Tile name at (" + tileGridIndexX + ", " + tileGridIndexY + ") is null.");
            }
            return tileName;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        IEnumerator<TTile> IEnumerable<TTile>.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public bool IsSameSpatialResolutionAndExtent<TTileOther>(VirtualRaster<TTileOther> other) where TTileOther : Raster
        {
            if (this.TileCellSizeX != other.TileCellSizeX)
            {
                return false;
            }
            if (this.TileCellSizeY != other.TileCellSizeY)
            {
                return false;
            }
            if (this.TileSizeInCellsX != other.TileSizeInCellsX)
            {
                return false;
            }
            if (this.TileSizeInCellsY != other.TileSizeInCellsY)
            {
                return false;
            }
            if (this.VirtualRasterSizeInTilesX != other.VirtualRasterSizeInTilesX)
            {
                return false;
            }
            if (this.VirtualRasterSizeInTilesY != other.VirtualRasterSizeInTilesY)
            {
                return false;
            }

            return true;
        }

        private (int tileIndexX, int tileIndexY) PlaceTileInGrid(string tileName, TTile tile)
        {
            Debug.Assert((this.tileGrid != null) && (this.tileNames != null));

            int tileIndexX = (int)Double.Round((tile.Transform.OriginX - this.TileTransform.OriginX) / this.TileTransform.CellWidth);
            int tileIndexY = (int)Double.Round((tile.Transform.OriginY - this.TileTransform.OriginY) / this.TileTransform.CellHeight);
            Debug.Assert(this.tileGrid[tileIndexX, tileIndexY] == null, "Unexpected attempt to place two tiles in the same position.");
            this.tileGrid[tileIndexX, tileIndexY] = tile;
            this.tileNames[tileIndexX, tileIndexY] = tileName;

            return (tileIndexX, tileIndexY);
        }

        public (int xIndex, int yIndex) ToGridIndices(int tileIndex)
        {
            Debug.Assert(tileIndex >= 0);
            int yIndex = tileIndex / this.VirtualRasterSizeInTilesX;
            int xIndex = tileIndex - this.VirtualRasterSizeInTilesX * yIndex;
            return (xIndex, yIndex);
        }

        public void SetRowToNull(int yIndex)
        {
            Debug.Assert(this.tileGrid != null);

            for (int xIndex = 0; xIndex < this.VirtualRasterSizeInTilesX; ++xIndex)
            {
                this.tileGrid[xIndex, yIndex] = null;
            }
        }

        public bool TryGetNeighborhood8<TBand>(double x, double y, string? bandName, [NotNullWhen(true)] out VirtualRasterNeighborhood8<TBand>? neighborhood) where TBand : INumber<TBand>
        {
            if (this.tileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.tileGrid.ToGridIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.tileGrid[tileGridXindex, tileGridYindex] == null))
            {
                // trivial cases: requested center tile location is off the grid or has no data
                neighborhood = null;
                return false;
            }

            neighborhood = this.GetNeighborhood8<TBand>(tileGridXindex, tileGridYindex, bandName);
            return true;
        }

        public bool TryGetTileBand<TBand>(double x, double y, string? bandName, [NotNullWhen(true)] out RasterBand<TBand>? tileBand) where TBand : INumber<TBand>
        {
            if (this.tileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.tileGrid.ToGridIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.tileGrid[tileGridXindex, tileGridYindex] == null))
            {
                // requested center tile location is off the grid or has no data
                tileBand = null;
                return false;
            }

            TTile? tile = this.tileGrid[tileGridXindex, tileGridYindex];
            if ((tile == null) || (tile.TryGetBand(bandName, out RasterBand? untypedTileBand) == false) || ((untypedTileBand is RasterBand<TBand>) == false))
            {
                tileBand = null;
                return false;
            }

            tileBand = (RasterBand<TBand>)untypedTileBand;
            return true;
        }
    }
}
