using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    internal class VirtualRaster<TBand> where TBand : INumber<TBand>
    {
        private SpatialReference? crs;
        private SinglebandRaster<TBand>?[,] tileGrid;
        private int[] tileGridIndexX;
        private int[] tileGridIndexY;
        private readonly List<SinglebandRaster<TBand>> tiles;

        public double OrginX { get; private set; }
        public double OrginY { get; private set; }
        public double TileCellSizeX { get; private set; }
        public double TileCellSizeY { get; private set; }
        public int TileSizeInCellsX { get; private set; }
        public int TileSizeInCellsY { get; private set; }
        public int VirtualRasterSizeInTilesX { get; private set; }
        public int VirtualRasterSizeInTilesY { get; private set; }

        public VirtualRaster()
        {
            this.crs = null;
            this.tileGrid = new SinglebandRaster<TBand>[0, 0];
            this.tileGridIndexX = Array.Empty<int>();
            this.tileGridIndexY = Array.Empty<int>();
            this.tiles = new();

            this.OrginX = Double.NaN;
            this.OrginY = Double.NaN;
            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
        }

        public SinglebandRaster<TBand> this[int index]
        {
            get { return this.tiles[index]; }
        }

        public SpatialReference Crs
        {
            get
            {
                if (this.crs == null)
                {
                    throw new InvalidOperationException("Virtual raster's CRS is unknown as no tiles have been added to it. Call " + nameof(this.Add) + "() before accessing " + nameof(this.Crs) + " { get; }.");
                }
                return this.crs;
            }
        }

        public int TileCount
        {
            get { return this.tiles.Count; }
        }

        public int TileCapacity
        {
            get { return this.tiles.Capacity; }
            set { this.tiles.Capacity = value; }
        }

        public void Add(SinglebandRaster<TBand> tile)
        {
            if (this.tiles.Count == 0)
            {
                this.crs = tile.Crs;
                double tileLinearUnits = tile.Crs.GetLinearUnits();
                if (tileLinearUnits <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's unit size of " + tileLinearUnits + " m is zero or negative.");
                }

                this.TileCellSizeX = tile.Transform.CellWidth;
                this.TileCellSizeY = tile.Transform.CellHeight;
                if ((this.TileCellSizeX <= 0.0) || (this.TileCellSizeY >= 0.0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's cell size of " + this.TileCellSizeX + " by " + this.TileCellSizeY + " has zero or negative width or zero or positive height. Tiles are expected to have negative heights such that raster indices increase with southing.");
                }

                this.TileSizeInCellsX = tile.XSize;
                this.TileSizeInCellsY = tile.YSize;
                if ((tile.XSize <= 0) || (tile.YSize <= 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile's size of " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsX + " cells is zero or negative.");
                }
            }
            else if (SpatialReferenceExtensions.IsSameCrs(tile.Crs, this.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles have varying coordinate systems. Expected tile CRS to be " + this.Crs.GetName() + " but passed tile uses " + tile.Crs.GetName() + ".");
            }
            else if ((this.TileCellSizeX != tile.Transform.CellWidth) || (this.TileCellSizeY != tile.Transform.CellHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles are of varying size. Expected " + this.TileCellSizeX + " by " + this.TileCellSizeY + " cells but passed tile has " + tile.Transform.CellWidth + " by " + tile.Transform.CellHeight + " cells.");
            }

            if ((tile.XSize != this.TileSizeInCellsX) || (tile.YSize != this.TileSizeInCellsY))
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles are of varying size. Expected " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsY + " cells but passed tile is " + tile.XSize + " by " + tile.YSize + " cells.");
            }

            this.tiles.Add(tile);
        }

        public void BuildGrid()
        {
            double maximumOriginX = Double.MinValue;
            double maximumOriginY = Double.MinValue;
            double minimumOriginX = Double.MaxValue;
            double minimumOriginY = Double.MaxValue;
            for (int tileIndex = 0; tileIndex < this.tiles.Count; ++tileIndex)
            {
                SinglebandRaster<TBand> tile = this.tiles[tileIndex];
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

            Debug.Assert(this.TileCellSizeY < 0.0, "Tile indices do not increase with southing.");
            double tileSizeInTileUnitsX = this.TileCellSizeX * this.TileSizeInCellsX;
            double tileSizeInTileUnitsY = this.TileCellSizeY * this.TileSizeInCellsY;
            this.VirtualRasterSizeInTilesX = (int)Double.Round((maximumOriginX - minimumOriginX) / tileSizeInTileUnitsX) + 1;
            this.VirtualRasterSizeInTilesY = (int)Double.Round((minimumOriginY - maximumOriginY) / tileSizeInTileUnitsY) + 1;

            if ((this.tileGrid.GetLength(0) != this.VirtualRasterSizeInTilesY) || (this.tileGrid.GetLength(1) != this.VirtualRasterSizeInTilesX))
            {
                this.tileGrid = new SinglebandRaster<TBand>?[this.VirtualRasterSizeInTilesY, this.VirtualRasterSizeInTilesX];
            }
            else
            {
                Array.Clear(this.tileGrid);
            }
            if (this.tileGridIndexX.Length != this.tiles.Count)
            {
                this.tileGridIndexX = new int[this.tiles.Count];
            }
            if (this.tileGridIndexY.Length != this.tiles.Count)
            {
                this.tileGridIndexY = new int[this.tiles.Count];
            }

            for (int tileIndex = 0; tileIndex < this.tiles.Count; ++tileIndex)
            {
                SinglebandRaster<TBand> tile = this.tiles[tileIndex];
                int xIndex = (int)Double.Round((tile.Transform.OriginX - minimumOriginX) / tileSizeInTileUnitsX);
                int yIndex = (int)Double.Round((tile.Transform.OriginY - maximumOriginY) / tileSizeInTileUnitsY);
                Debug.Assert(this.tileGrid[yIndex, xIndex] == null, "Unexpected attempt to place two tiles in the same position.");
                this.tileGrid[yIndex, xIndex] = tile;
                this.tileGridIndexX[tileIndex] = xIndex;
                this.tileGridIndexY[tileIndex] = yIndex;
            }

            this.OrginX = minimumOriginX;
            this.OrginY = maximumOriginY;
        }

        public VirtualRasterNeighborhood8<TBand> GetNeighborhood8(int tileIndex)
        {
            int tileXindex = this.tileGridIndexX[tileIndex];
            int tileYindex = this.tileGridIndexY[tileIndex];

            int northIndex = tileYindex - 1;
            int southIndex = tileYindex + 1;
            int eastIndex = tileXindex + 1;
            int westIndex = tileXindex - 1;

            SinglebandRaster<TBand>? north = null;
            SinglebandRaster<TBand>? northeast = null;
            SinglebandRaster<TBand>? northwest = null;
            if (northIndex >= 0)
            {
                north = this.tileGrid[northIndex, tileXindex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    northeast = this.tileGrid[northIndex, eastIndex];
                }
                if (westIndex >= 0)
                {
                    northwest = this.tileGrid[northIndex, westIndex];
                }
            }

            SinglebandRaster<TBand>? south = null;
            SinglebandRaster<TBand>? southeast = null;
            SinglebandRaster<TBand>? southwest = null;
            if (southIndex < this.VirtualRasterSizeInTilesY)
            {
                south = this.tileGrid[southIndex, tileXindex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    southeast = this.tileGrid[southIndex, eastIndex];
                }
                if (westIndex >= 0)
                {
                    southwest = this.tileGrid[southIndex, westIndex];
                }
            }

            SinglebandRaster<TBand>? east = null;
            if (eastIndex < this.VirtualRasterSizeInTilesX)
            {
                east = this.tileGrid[tileYindex, eastIndex];
            }

            SinglebandRaster<TBand>? west = null;
            if (westIndex >= 0)
            {
                west = this.tileGrid[tileYindex, westIndex];
            }

            return new VirtualRasterNeighborhood8<TBand>(this.tiles[tileIndex])
            {
                North = north,
                Northeast = northeast,
                Northwest = northwest,
                South = south,
                Southeast = southeast,
                Southwest = southwest,
                East = east,
                West = west
            };
        }

        public bool IsSameSpatialResolutionAndExtent(VirtualRaster<TBand> other)
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
    }
}
