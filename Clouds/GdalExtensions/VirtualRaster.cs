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

        public int NonNullTileCount { get; protected set; }
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

            this.NonNullTileCount = 0;
            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileCountByBand = [];
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
            this.TilesWithNoDataValuesByBand = [];
            this.VirtualRasterSizeInTilesX = -1;
            this.VirtualRasterSizeInTilesY = -1;
        }

        protected VirtualRaster(VirtualRaster other)
        {
            this.crs = other.Crs.Clone();

            this.NonNullTileCount = 0;
            this.TileCellSizeX = other.TileCellSizeX;
            this.TileCellSizeY = other.TileCellSizeY;
            this.TileCountByBand = [];
            this.TileSizeInCellsX = other.TileSizeInCellsX;
            this.TileSizeInCellsY = other.TileSizeInCellsY;
            this.TilesWithNoDataValuesByBand = [];
            this.VirtualRasterSizeInTilesX = other.VirtualRasterSizeInTilesX;
            this.VirtualRasterSizeInTilesY = other.VirtualRasterSizeInTilesY;
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
        private readonly List<TTile> ungriddedTiles;

        public List<DataType> BandDataTypes { get; private set; }
        public List<string> BandNames { get; private set; }
        public List<List<double>> NoDataValuesByBand { get; private set; }
        public GridNullable<TTile>? TileGrid { get; private set; }

        public VirtualRaster()
            : base()
        {
            this.ungriddedTiles = [];

            this.BandDataTypes = [];
            this.BandNames = [];
            this.NoDataValuesByBand = [];
            this.TileGrid = null;
        }

        public VirtualRaster(LasTileGrid lasGrid)
            : this()
        {
            this.Crs = lasGrid.Crs.Clone();
            this.TileGrid = new(lasGrid);

            // no DTM information available, so no cell size information yet
            this.VirtualRasterSizeInTilesX = lasGrid.SizeX;
            this.VirtualRasterSizeInTilesY = lasGrid.SizeY;
        }

        protected VirtualRaster(VirtualRaster other)
            : base(other)
        {
            this.ungriddedTiles = [];

            this.BandDataTypes = [];
            this.BandNames = [];
            this.NoDataValuesByBand = [];
            this.TileGrid = null;
        }

        public TTile? this[int cellIndex]
        {
            get 
            {
                Debug.Assert(this.TileGrid != null);
                return this.TileGrid[cellIndex]; 
            }
        }

        public TTile? this[int xIndex, int yIndex]
        {
            get 
            {
                Debug.Assert(this.TileGrid != null);
                return this.TileGrid[xIndex, yIndex]; 
            }
        }

        public override GridGeoTransform TileTransform
        {
            get 
            {
                Debug.Assert(this.TileGrid != null);
                return this.TileGrid.Transform; 
            }
        }

        public (int tileIndexX, int tileIndexY) Add(TTile tile)
        {
            if ((this.TileGrid == null) && (this.ungriddedTiles.Count == 0))
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
                    if (tileBand.HasNoDataValue)
                    {
                        this.NoDataValuesByBand.Add([ tileBand.GetNoDataValueAsDouble() ]);
                        this.TilesWithNoDataValuesByBand.Add(1);
                    }
                    else
                    {
                        this.NoDataValuesByBand.Add([]);
                        this.TilesWithNoDataValuesByBand.Add(0);
                    }
                    this.TileCountByBand.Add(1);
                }
            }
            else
            {
                foreach (RasterBand tileBand in tile.GetBands())
                {
                    DataType tileBandDataType = tileBand.GetGdalDataType();

                    int vrtBandIndex = this.BandNames.IndexOf(tileBand.Name);
                    if (vrtBandIndex < 0)
                    {
                        Debug.Assert((this.BandDataTypes.Count == this.BandNames.Count) && (this.BandNames.Count == this.NoDataValuesByBand.Count) && (this.BandNames.Count == this.TileCountByBand.Count) && (this.BandNames.Count == this.TilesWithNoDataValuesByBand.Count));
                        vrtBandIndex = this.BandDataTypes.Count;

                        this.BandDataTypes.Add(tileBandDataType);
                        this.BandNames.Add(tileBand.Name);
                        this.NoDataValuesByBand.Add(tileBand.HasNoDataValue ? [] : [tileBand.GetNoDataValueAsDouble()]);
                        this.TileCountByBand.Add(0); // incremented below
                        this.TilesWithNoDataValuesByBand.Add(0); // incremented below
                    }
                    else
                    {
                        DataType thisBandDataType = this.BandDataTypes[vrtBandIndex];
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
            if (this.TileGrid != null)
            {
                (tileIndexX, tileIndexY) = this.PlaceTileInGrid(tile);
            }
            else
            {
                this.ungriddedTiles.Add(tile);
            }

            ++this.NonNullTileCount;
            return (tileIndexX, tileIndexY);
        }

        public VirtualRaster<TTileCopy> CreateEmptyCopy<TTileCopy>() where TTileCopy : Raster
        {
            VirtualRaster<TTileCopy> emptyCopy = new(this);
            if (this.TileGrid != null)
            {
                emptyCopy.TileGrid = new(emptyCopy.Crs, new(this.TileGrid.Transform), emptyCopy.VirtualRasterSizeInTilesX, emptyCopy.VirtualRasterSizeInTilesY, cloneCrsAndTransform: false);
            }

            return emptyCopy;
        }

        public (int[] tileIndexX, int[] tileIndexY) CreateTileGrid()
        {
            if (this.TileGrid != null)
            {
                throw new InvalidOperationException("Grid is already built."); // debatable if one time call needs to be enforced
            }
            if (this.NonNullTileCount == 0)
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
            this.TileGrid = new(this.Crs, tileTransform, this.VirtualRasterSizeInTilesX, this.VirtualRasterSizeInTilesY, cloneCrsAndTransform: false);

            int[] tileIndexX = new int[this.ungriddedTiles.Count];
            int[] tileIndexY = new int[this.ungriddedTiles.Count];
            for (int tileIndex = 0; tileIndex < this.ungriddedTiles.Count; ++tileIndex)
            {
                (tileIndexX[tileIndex], tileIndexY[tileIndex]) = this.PlaceTileInGrid(this.ungriddedTiles[tileIndex]);
            }

            this.ungriddedTiles.Clear();
            return (tileIndexX, tileIndexY);
        }

        public VrtDataset CreateDataset(string vrtDatasetDirectoryPath, GridNullable<List<RasterBandStatistics>>? tileBandStatistics)
        {
            return this.CreateDataset(vrtDatasetDirectoryPath, this.BandNames, tileBandStatistics);
        }

        public VrtDataset CreateDataset(string vrtDatasetDirectoryPath, List<string> vrtBandNames, GridNullable<List<RasterBandStatistics>>? tileBandStatistics)
        {
            Debug.Assert(this.TileGrid != null);

            VrtDataset vrtDataset = new()
            {
                RasterXSize = (UInt32)(this.VirtualRasterSizeInTilesX * this.TileSizeInCellsX),
                RasterYSize = (UInt32)(this.VirtualRasterSizeInTilesY * this.TileSizeInCellsY)
            };
            vrtDataset.Srs.DataAxisToSrsAxisMapping = this.Crs.IsVertical() == 1 ? [ 1, 2, 3 ] : [ 1, 2 ];
            vrtDataset.Srs.WktGeogcsOrProj = this.Crs.GetWkt();

            vrtDataset.GeoTransform.Copy(this.TileGrid.Transform); // copies tile x and y size as cell size
            vrtDataset.GeoTransform.SetCellSize(this.TileCellSizeX, this.TileCellSizeY);

            vrtDataset.AppendBands(vrtDatasetDirectoryPath, this, vrtBandNames, tileBandStatistics);
            return vrtDataset;
        }

        public VirtualRasterEnumerator<TTile> GetEnumerator()
        {
            return new VirtualRasterEnumerator<TTile>(this);
        }

        public string GetExtentString()
        {
            if (this.TileGrid != null)
            {
                return this.TileGrid.GetExtentString();
            }

            return "unknown (virtual raster tile grid has not yet been built)";
        }

        public VirtualRasterNeighborhood8<TBand> GetNeighborhood8<TBand>(int tileGridIndexX, int tileGridIndexY, string? bandName) where TBand : IMinMaxValue<TBand>, INumber<TBand>
        {
            Debug.Assert(this.TileGrid != null);

            TTile? center = this.TileGrid[tileGridIndexX, tileGridIndexY];
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
                north = this.TileGrid[tileGridIndexX, northIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    northeast = this.TileGrid[eastIndex, northIndex];
                }
                if (westIndex >= 0)
                {
                    northwest = this.TileGrid[westIndex, northIndex];
                }
            }

            TTile? south = null;
            TTile? southeast = null;
            TTile? southwest = null;
            if (southIndex < this.VirtualRasterSizeInTilesY)
            {
                south = this.TileGrid[tileGridIndexX, southIndex];
                if (eastIndex < this.VirtualRasterSizeInTilesX)
                {
                    southeast = this.TileGrid[eastIndex, southIndex];
                }
                if (westIndex >= 0)
                {
                    southwest = this.TileGrid[westIndex, southIndex];
                }
            }

            TTile? east = null;
            if (eastIndex < this.VirtualRasterSizeInTilesX)
            {
                east = this.TileGrid[eastIndex, tileGridIndexY];
            }

            TTile? west = null;
            if (westIndex >= 0)
            {
                west = this.TileGrid[westIndex, tileGridIndexY];
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

            Debug.Assert((this.TileGrid != null) && (other.TileGrid != null));
            if (this.TileGrid.IsSameExtentAndSpatialResolution(other.TileGrid) == false)
            {
                return false;
            }

            return true;
        }

        private (int tileIndexX, int tileIndexY) PlaceTileInGrid(TTile tile)
        {
            Debug.Assert(this.TileGrid != null);

            int tileIndexX = (int)Double.Round((tile.Transform.OriginX - this.TileTransform.OriginX) / this.TileTransform.CellWidth);
            int tileIndexY = (int)Double.Round((tile.Transform.OriginY - this.TileTransform.OriginY) / this.TileTransform.CellHeight);
            TTile? existingTile = this.TileGrid[tileIndexX, tileIndexY];
            if (existingTile != null)
            {
                throw new ArgumentOutOfRangeException(nameof(tile), "Tiles '" + existingTile.FilePath + "' (extents " + existingTile.GetExtentString() + ") and '" + tile.FilePath + "' (" + tile.GetExtentString() + ") are both located at virtual raster position (" + tileIndexX + ", " + tileIndexY + ").");
            }
            this.TileGrid[tileIndexX, tileIndexY] = tile;

            return (tileIndexX, tileIndexY);
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
            if (this.TileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.TileGrid.ToGridIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.TileGrid[tileGridXindex, tileGridYindex] == null))
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
            if (this.TileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.TileGrid.ToGridIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.VirtualRasterSizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.VirtualRasterSizeInTilesY) ||
                (this.TileGrid[tileGridXindex, tileGridYindex] == null))
            {
                // requested center tile location is off the grid or has no data
                tileBand = null;
                return false;
            }

            TTile? tile = this.TileGrid[tileGridXindex, tileGridYindex];
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
