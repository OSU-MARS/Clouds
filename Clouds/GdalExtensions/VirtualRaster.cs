﻿using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    // could derive from Grid but naming becomes confusing as differences between tiles and cells are obscured
    // Also, indexing differs because tiles are sparse where grid is dense, so a grid index is not a tile index.
    public class VirtualRaster<TBand> where TBand : INumber<TBand>
    {
        private SpatialReference? crs;
        private Grid<Raster<TBand>?>? tileGrid;
        private int[] tileGridIndexX;
        private int[] tileGridIndexY;
        private readonly List<Raster<TBand>> tiles;

        public double TileCellSizeX { get; private set; }
        public double TileCellSizeY { get; private set; }
        public List<string> TileNames { get; private init; }
        public int TileSizeInCellsX { get; private set; }
        public int TileSizeInCellsY { get; private set; }

        public int VirtualRasterSizeInTilesX { get; private set; }
        public int VirtualRasterSizeInTilesY { get; private set; }

        public VirtualRaster()
        {
            this.crs = null;
            this.tileGrid = null;
            this.tileGridIndexX = [];
            this.tileGridIndexY = [];
            this.tiles = [];

            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileNames = [];
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
        }

        public Raster<TBand> this[int index]
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

        public GridGeoTransform TileTransform
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid.Transform; 
            }
        }

        public void Add(string tileName, Raster<TBand> tile)
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

            this.TileNames.Add(tileName);
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
                Raster<TBand> tile = this.tiles[tileIndex];
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

            GridGeoTransform tileTransform = new(minimumOriginX, maximumOriginY, tileSizeInTileUnitsX, tileSizeInTileUnitsY);
            this.tileGrid = new(this.Crs, tileTransform, this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY);
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
                Raster<TBand> tile = this.tiles[tileIndex];
                int xIndex = (int)Double.Round((tile.Transform.OriginX - minimumOriginX) / tileSizeInTileUnitsX);
                int yIndex = (int)Double.Round((tile.Transform.OriginY - maximumOriginY) / tileSizeInTileUnitsY);
                Debug.Assert(this.tileGrid[xIndex, yIndex] == null, "Unexpected attempt to place two tiles in the same position.");
                this.tileGrid[xIndex, yIndex] = tile;
                this.tileGridIndexX[tileIndex] = xIndex;
                this.tileGridIndexY[tileIndex] = yIndex;
            }
        }

        public VirtualRaster<TBand> CreateEmptyCopy()
        {
            VirtualRaster<TBand> emptyCopy = new()
            {
                crs = this.crs?.Clone()
            };

            if (this.tileGrid != null)
            {
                emptyCopy.tileGrid = new(emptyCopy.Crs, this.tileGrid.Transform, this.tileGrid.XSize, this.tileGrid.YSize);
                emptyCopy.tileGridIndexX = new int[this.TileCount];
                emptyCopy.tileGridIndexY = new int[this.TileCount];
                emptyCopy.tiles.Capacity = this.TileCount;
            }

            emptyCopy.TileCellSizeX = this.TileCellSizeX;
            emptyCopy.TileCellSizeY = this.TileCellSizeY;
            emptyCopy.TileSizeInCellsX = this.TileSizeInCellsX;
            emptyCopy.TileSizeInCellsY = this.TileSizeInCellsY;

            return emptyCopy;
        }

        private VirtualRasterNeighborhood8<TBand> GetNeighborhood8(int tileGridXindex, int tileGridYindex, int bandIndex)
        {
            Debug.Assert(this.tileGrid != null);

            Raster<TBand>? center = this.tileGrid[tileGridXindex, tileGridYindex];
            if (center == null)
            {
                throw new NotSupportedException("No tile is loaded at index (" + tileGridXindex + ", " + tileGridYindex + ").");
            }

            int northIndex = tileGridYindex - 1;
            int southIndex = tileGridYindex + 1;
            int eastIndex = tileGridXindex + 1;
            int westIndex = tileGridXindex - 1;

            Raster<TBand>? north = null;
            Raster<TBand>? northeast = null;
            Raster<TBand>? northwest = null;
            if (northIndex >= 0)
            {
                north = this.tileGrid[tileGridXindex, northIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    northeast = this.tileGrid[eastIndex, northIndex];
                }
                if (westIndex >= 0)
                {
                    northwest = this.tileGrid[westIndex, northIndex];
                }
            }

            Raster<TBand>? south = null;
            Raster<TBand>? southeast = null;
            Raster<TBand>? southwest = null;
            if (southIndex < this.VirtualRasterSizeInTilesY)
            {
                south = this.tileGrid[tileGridXindex, southIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    southeast = this.tileGrid[eastIndex, southIndex];
                }
                if (westIndex >= 0)
                {
                    southwest = this.tileGrid[westIndex, southIndex];
                }
            }

            Raster<TBand>? east = null;
            if (eastIndex < this.VirtualRasterSizeInTilesX)
            {
                east = this.tileGrid[eastIndex, tileGridYindex];
            }

            Raster<TBand>? west = null;
            if (westIndex >= 0)
            {
                west = this.tileGrid[westIndex, tileGridYindex];
            }

            return new VirtualRasterNeighborhood8<TBand>(center.Bands[bandIndex])
            {
                North = north?.Bands[bandIndex],
                Northeast = northeast?.Bands[bandIndex],
                Northwest = northwest?.Bands[bandIndex],
                South = south?.Bands[bandIndex],
                Southeast = southeast?.Bands[bandIndex],
                Southwest = southwest?.Bands[bandIndex],
                East = east?.Bands[bandIndex],
                West = west?.Bands[bandIndex]
            };
        }

        public VirtualRasterNeighborhood8<TBand> GetNeighborhood8(int tileIndex, int bandIndex)
        {
            return this.GetNeighborhood8(this.tileGridIndexX[tileIndex], this.tileGridIndexY[tileIndex], bandIndex);
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

        public bool TryGetNeighborhood8(double x, double y, int bandIndex, [NotNullWhen(true)] out VirtualRasterNeighborhood8<TBand>? neighborhood)
        {
            if (this.tileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.BuildGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.tileGrid.GetCellIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.tileGrid[tileGridXindex, tileGridYindex] == null))
            {
                // trivial cases: requested center tile location is off the grid or has no data
                neighborhood = null;
                return false;
            }

            neighborhood = this.GetNeighborhood8(tileGridXindex, tileGridYindex, bandIndex);
            return true;
        }

        public bool TryGetTile(double x, double y, string? bandName, [NotNullWhen(true)] out RasterBand<TBand>? tileBand)
        {
            if (this.tileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.BuildGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.tileGrid.GetCellIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.tileGrid[tileGridXindex, tileGridYindex] == null))
            {
                // requested center tile location is off the grid or has no data
                tileBand = null;
                return false;
            }

            Raster<TBand>? tileRaster = this.tileGrid[tileGridXindex, tileGridYindex];
            if ((tileRaster == null) || (tileRaster.TryGetBand(bandName, out tileBand) == false))
            {
                tileBand = null;
                return false;
            }

            return true;
        }
    }
}
