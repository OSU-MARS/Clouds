using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    // could derive from Grid but naming becomes confusing as differences between tiles and cells are obscured
    public abstract class VirtualRaster
    {
        private SpatialReference? crs;

        public int TileCount { get; protected set; }
        public double TileCellSizeX { get; protected set; }
        public double TileCellSizeY { get; protected set; }
        public int TileSizeInCellsX { get; protected set; }
        public int TileSizeInCellsY { get; protected set; }
        public List<int> TileCountByBand { get; protected set; }
        public List<int> TilesWithNoDataValuesByBand { get; protected set; }

        public int VirtualRasterSizeInTilesX { get; protected set; }
        public int VirtualRasterSizeInTilesY { get; protected set; }

        protected VirtualRaster()
        {
            this.crs = null;

            this.TileCount = 0;
            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileCountByBand = [];
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
            this.TilesWithNoDataValuesByBand = [];
            this.VirtualRasterSizeInTilesX = -1;
            this.VirtualRasterSizeInTilesY = -1;
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

        protected bool HasCrs
        {
            get { return this.crs != null; }
        }

        public abstract GridGeoTransform TileTransform { get; }
    }

    public class VirtualRaster<TTile> : VirtualRaster, IEnumerable<TTile> where TTile : Raster
    {
        private GridNullable<TTile?>? tileGrid;
        private readonly List<TTile> ungriddedTiles;

        public List<DataType> BandDataTypes { get; private set; }
        public List<string> BandNames { get; private set; }
        public List<List<double>> NoDataValuesByBand { get; private set; }

        public VirtualRaster()
            : base()
        {
            this.tileGrid = null;
            this.ungriddedTiles = [];

            this.BandDataTypes = [];
            this.BandNames = [];
            this.NoDataValuesByBand = [];
        }

        public VirtualRaster(LasTileGrid lasGrid)
            : this()
        {
            this.tileGrid = new(lasGrid);

            this.Crs = lasGrid.Crs.Clone();

            // no DTM information available, so no cell size information yet
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

        public override GridGeoTransform TileTransform
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid.Transform; 
            }
        }

        public (int tileIndexX, int tileIndexY) Add(TTile tile)
        {
            if ((this.tileGrid == null) && (this.ungriddedTiles.Count == 0))
            {
                // if no CRS information was specified at construction, latch CRS of first tile added
                Debug.Assert(this.HasCrs == false);
                this.Crs = tile.Crs;
            }
            else if (SpatialReferenceExtensions.IsSameCrs(tile.Crs, this.Crs) == false) // throws if this.crs is null
            {
                // all tiles must be in the same CRS
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles have varying coordinate systems. Expected tile '" + tile.FilePath + "' to have CRS " + this.Crs.GetName() + " but it is in " + tile.Crs.GetName() + ".");
            }

            if (this.BandNames.Count == 0)
            {
                // latch bands of first tile added
                foreach (RasterBand tileBand in tile.GetBands())
                {
                    this.BandDataTypes.Add(tileBand.GetGdalDataType());
                    this.BandNames.Add(tileBand.Name); // should names be checked for uniqueness or numbers inserted if null or empty?
                    List<double> noDataValues = [];
                    this.NoDataValuesByBand.Add(noDataValues);
                    this.TileCountByBand.Add(1);
                    this.TilesWithNoDataValuesByBand.Add(tileBand.HasNoDataValue ? 1 : 0);
                    if (tileBand.HasNoDataValue)
                    {
                        noDataValues.Add(tileBand.GetNoDataValueAsDouble());
                    }
                }
            }
            else
            {
                foreach (RasterBand tileBand in tile.GetBands())
                {
                    int vrtBandIndex = this.BandNames.IndexOf(tileBand.Name);
                    if (vrtBandIndex < 0)
                    {
                        // TODO: insert new band name into virtual raster's bands
                        throw new NotSupportedException("Tile '" + tile.FilePath + "' contains band '" + tileBand.Name + "', which has not been latched into in the virtual raster's list of band names.");
                    }

                    DataType thisBandDataType = this.BandDataTypes[vrtBandIndex];
                    DataType tileBandDataType = tileBand.GetGdalDataType();
                    if (thisBandDataType != tileBandDataType)
                    {
                        if (DataTypeExtensions.IsExactlyExpandable(thisBandDataType, tileBandDataType))
                        {
                            // widen band data type to accommodate new tile
                            this.BandDataTypes[vrtBandIndex] = tileBandDataType;
                        }
                        else if (DataTypeExtensions.IsExactlyExpandable(tileBandDataType, thisBandDataType) == false)
                        {
                            // tile's data type is widenable to current band type
                            // Subsequent widening of the virtual raster's band type remains compatible with the band type.
                            throw new ArgumentOutOfRangeException(nameof(tile), "Tiles have incompatible data types for band '" + this.BandNames[vrtBandIndex] + "' (index " + vrtBandIndex + "). Current type is " + thisBandDataType + " while tile has type " + tileBandDataType + "'.");
                        }
                    }

                    ++this.TileCountByBand[vrtBandIndex];

                    if (tileBand.HasNoDataValue)
                    {
                        double noDataValue = tileBand.GetNoDataValueAsDouble();
                        List<double> noDataValues = this.NoDataValuesByBand[vrtBandIndex];
                        if (noDataValues.Contains(noDataValue) == false)
                        {
                            noDataValues.Add(noDataValue);
                        }
                        ++this.TilesWithNoDataValuesByBand[vrtBandIndex];
                    }
                }
            }

            if (this.TileSizeInCellsX == -1)
            {
                // latch cell size of first tile added
                Debug.Assert(Double.IsNaN(this.TileCellSizeX) && Double.IsNaN(this.TileCellSizeY) && (this.TileSizeInCellsY == -1));
                this.TileCellSizeX = tile.Transform.CellWidth;
                this.TileCellSizeY = tile.Transform.CellHeight;
                if ((this.TileCellSizeX <= 0.0) || (Double.Abs(this.TileCellSizeY) == 0.0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile '" + tile.FilePath + "' has cell size of " + this.TileCellSizeX + " by " + this.TileCellSizeY + " has zero or negative width or zero or positive height. Tiles are expected to have negative heights such that raster indices increase with southing.");
                }

                this.TileSizeInCellsX = tile.SizeX;
                this.TileSizeInCellsY = tile.SizeY;
                if ((tile.SizeX <= 0) || (tile.SizeY <= 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(tile), "Tile '" + tile.FilePath + "' has  size of " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsX + " cells is zero or negative.");
                }
            }
            else if ((this.TileCellSizeX != tile.Transform.CellWidth) || (this.TileCellSizeY != tile.Transform.CellHeight))
            {
                // cell size must be the same across all tiles
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles have cells of differing size. Expected " + this.TileCellSizeX + " by " + this.TileCellSizeY + " cells but tile '" + tile.FilePath + "' has " + tile.Transform.CellWidth + " by " + tile.Transform.CellHeight + " cells.");
            }

            if ((tile.SizeX != this.TileSizeInCellsX) || (tile.SizeY != this.TileSizeInCellsY))
            {
                // tile size must be the same across all tiles => tile size in cells must be the same
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles are of varying size. Expected " + this.TileSizeInCellsX + " by " + this.TileSizeInCellsY + " cells but tile '" + tile.FilePath + "' is " + tile.SizeX + " by " + tile.SizeY + " cells.");
            }

            int tileIndexX = -1;
            int tileIndexY = -1;
            if (this.tileGrid != null)
            {
                (tileIndexX, tileIndexY) = this.PlaceTileInGrid(tile);
            }
            else
            {
                this.ungriddedTiles.Add(tile);
            }

            ++this.TileCount;
            return (tileIndexX, tileIndexY);
        }

        public (int[] tileIndexX, int[] tileIndexY) CreateTileGrid()
        {
            if (this.tileGrid != null)
            {
                throw new InvalidOperationException("Grid is already built."); // debatable if one time call needs to be enforced
            }
            if (this.TileCount == 0)
            {
                throw new InvalidOperationException("No tiles have been added to the virtual raster.");
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

            if (this.TileCellSizeY >= 0.0)
            {
                throw new NotSupportedException("Tile y indices do not increase with southing. Tile cell size is " + this.TileCellSizeX + " by " + this.TileCellSizeY + ".");
            }
            double tileSizeInTileUnitsX = this.TileCellSizeX * this.TileSizeInCellsX;
            double tileSizeInTileUnitsY = this.TileCellSizeY * this.TileSizeInCellsY;
            this.VirtualRasterSizeInTilesX = (int)Double.Round((maximumOriginX - minimumOriginX) / tileSizeInTileUnitsX) + 1;
            this.VirtualRasterSizeInTilesY = (int)Double.Round((minimumOriginY - maximumOriginY) / tileSizeInTileUnitsY) + 1;

            GridGeoTransform tileTransform = new(minimumOriginX, maximumOriginY, tileSizeInTileUnitsX, tileSizeInTileUnitsY);
            
            this.tileGrid = new(this.Crs, tileTransform, this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY);

            int[] tileIndexX = new int[this.ungriddedTiles.Count];
            int[] tileIndexY = new int[this.ungriddedTiles.Count];
            for (int tileIndex = 0; tileIndex < this.ungriddedTiles.Count; ++tileIndex)
            {
                (tileIndexX[tileIndex], tileIndexY[tileIndex]) = this.PlaceTileInGrid(this.ungriddedTiles[tileIndex]);
            }

            this.ungriddedTiles.Clear();
            return (tileIndexX, tileIndexY);
        }

        public VrtDataset CreateDataset()
        {
            Debug.Assert(this.tileGrid != null);

            VrtDataset vrtDataset = new()
            {
                RasterXSize = (UInt32)(this.VirtualRasterSizeInTilesX * this.TileSizeInCellsX),
                RasterYSize = (UInt32)(this.VirtualRasterSizeInTilesY * this.TileSizeInCellsY)
            };
            vrtDataset.Srs.DataAxisToSrsAxisMapping = this.Crs.IsVertical() == 1 ? [ 1, 2, 3 ] : [ 1, 2 ];
            vrtDataset.Srs.WktGeogcsOrProj = this.Crs.GetWkt();

            vrtDataset.GeoTransform.Copy(this.tileGrid.Transform); // copies tile x and y size as cell size
            vrtDataset.GeoTransform.SetCellSize(this.TileCellSizeX, this.TileCellSizeY);

            return vrtDataset;
        }

        public VirtualRasterEnumerator<TTile> GetEnumerator()
        {
            return new VirtualRasterEnumerator<TTile>(this);
        }

        public string GetExtentString()
        {
            if (this.tileGrid != null)
            {
                return this.tileGrid.GetExtentString();
            }

            return "unknown (virtual raster tile grid has not yet been built)";
        }

        public VirtualRasterNeighborhood8<TBand> GetNeighborhood8<TBand>(int tileGridIndexX, int tileGridIndexY, string? bandName) where TBand : IMinMaxValue<TBand>, INumber<TBand>
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

        /// <returns><see cref="bool"/> array whose values are true where cells are null</returns>
        /// <remarks>
        /// Necessarily same code as <see cref="GridNullable{TCell}.GetUnpopulatedCellMap()"/> due to class split.
        /// </remarks>
        public bool[,] GetUnpopulatedTileMap()
        {
            bool[,] cellMap = new bool[this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY];
            for (int yIndex = 0; yIndex < this.VirtualRasterSizeInTilesY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < this.VirtualRasterSizeInTilesX; ++xIndex)
                {
                    TTile? value = this[xIndex, yIndex];
                    if (value == null)
                    {
                        cellMap[xIndex, yIndex] = true;
                    }
                }
            }

            return cellMap;
        }

        //public string GetTileName(int tileGridIndexX, int tileGridIndexY)
        //{
        //    Debug.Assert(this.tileGrid != null);
        //    TTile? tile = this.tileGrid[tileGridIndexX, tileGridIndexY];
        //    if (tile == null)
        //    {
        //        throw new InvalidOperationException("Tile name at (" + tileGridIndexX + ", " + tileGridIndexY + ") is null.");
        //    }
        //    return Tile.GetName(tile.FilePath);
        //}

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        IEnumerator<TTile> IEnumerable<TTile>.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public bool IsSameExtentAndSpatialResolution<TTileOther>(VirtualRaster<TTileOther> other) where TTileOther : Raster
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

            Debug.Assert((this.tileGrid != null) && (other.tileGrid != null));
            if (this.tileGrid.IsSameExtentAndSpatialResolution(other.tileGrid) == false)
            {
                return false;
            }

            return true;
        }

        private (int tileIndexX, int tileIndexY) PlaceTileInGrid(TTile tile)
        {
            Debug.Assert(this.tileGrid != null);

            int tileIndexX = (int)Double.Round((tile.Transform.OriginX - this.TileTransform.OriginX) / this.TileTransform.CellWidth);
            int tileIndexY = (int)Double.Round((tile.Transform.OriginY - this.TileTransform.OriginY) / this.TileTransform.CellHeight);
            TTile? existingTile = this.tileGrid[tileIndexX, tileIndexY];
            if (existingTile != null)
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles '" + existingTile.FilePath + "' (extents " + existingTile.GetExtentString() + ") and '" + tile.FilePath + "' (" + tile.GetExtentString() + ") are both located at virtual raster position (" + tileIndexX + ", " + tileIndexY + ").");
            }
            this.tileGrid[tileIndexX, tileIndexY] = tile;

            return (tileIndexX, tileIndexY);
        }

        public void ReturnRowToObjectPool(int yIndex, ObjectPool<TTile> tilePool)
        {
            Debug.Assert(this.tileGrid != null);

            for (int xIndex = 0; xIndex < this.VirtualRasterSizeInTilesX; ++xIndex)
            {
                TTile? tile = this.tileGrid[xIndex, yIndex];
                if (tile != null)
                {
                    tilePool.Return(tile);
                }
                this.tileGrid[xIndex, yIndex] = null;
            }
        }

        public (int xIndex, int yIndex) ToGridIndices(int tileIndex)
        {
            Debug.Assert(tileIndex >= 0);
            int yIndex = tileIndex / this.VirtualRasterSizeInTilesX;
            int xIndex = tileIndex - this.VirtualRasterSizeInTilesX * yIndex;
            return (xIndex, yIndex);
        }

        public bool TryGetNeighborhood8<TBand>(double x, double y, string? bandName, [NotNullWhen(true)] out VirtualRasterNeighborhood8<TBand>? neighborhood) where TBand : IMinMaxValue<TBand>, INumber<TBand>
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

        public bool TryGetTileBand<TBand>(double x, double y, string? bandName, [NotNullWhen(true)] out RasterBand<TBand>? tileBand) where TBand : IMinMaxValue<TBand>, INumber<TBand>
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
