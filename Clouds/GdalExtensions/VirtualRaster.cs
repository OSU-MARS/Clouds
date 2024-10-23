using Mars.Clouds.Las;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    // could derive from Grid but naming becomes confusing as differences between tiles and cells are obscured
    public abstract class VirtualRaster : VirtualLayer
    {
        public double TileCellSizeX { get; protected set; }
        public double TileCellSizeY { get; protected set; }
        public int TileSizeInCellsX { get; protected set; }
        public int TileSizeInCellsY { get; protected set; }
        public List<int> TileCountByBand { get; protected set; }
        public List<int> TilesWithNoDataValuesByBand { get; protected set; }

        protected VirtualRaster() 
            : base()
        {
            this.TileCellSizeX = Double.NaN;
            this.TileCellSizeY = Double.NaN;
            this.TileCountByBand = [];
            this.TileSizeInCellsX = -1;
            this.TileSizeInCellsY = -1;
            this.TilesWithNoDataValuesByBand = [];
        }

        protected VirtualRaster(VirtualRaster other)
            : base(other)
        {
            this.TileCellSizeX = other.TileCellSizeX;
            this.TileCellSizeY = other.TileCellSizeY;
            this.TileCountByBand = [];
            this.TileSizeInCellsX = other.TileSizeInCellsX;
            this.TileSizeInCellsY = other.TileSizeInCellsY;
            this.TilesWithNoDataValuesByBand = [];
        }

        public abstract Grid? TileGrid { get; }
    }

    public class VirtualRaster<TTile> : VirtualRaster where TTile : Raster
    {
        private GridNullable<TTile>? tileGrid;
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
            this.tileGrid = new(lasGrid, cloneCrsAndTransform: true);
            this.Crs = this.tileGrid.Crs;

            // no DTM information available, so no cell size information yet
            this.SizeInTilesX = lasGrid.SizeX;
            this.SizeInTilesY = lasGrid.SizeY;
        }

        protected VirtualRaster(VirtualRaster other)
            : base(other)
        {
            this.tileGrid = null;
            this.ungriddedTiles = [];

            this.BandDataTypes = [];
            this.BandNames = [];
            this.NoDataValuesByBand = [];
        }

        public TTile? this[int tileIndex]
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid[tileIndex]; 
            }
        }

        public TTile? this[int tileIndexX, int tileIndexY]
        {
            get 
            {
                Debug.Assert(this.tileGrid != null);
                return this.tileGrid[tileIndexX, tileIndexY]; 
            }
        }

        public override GridNullable<TTile>? TileGrid 
        { 
            get { return this.tileGrid; }
            // get+set must match base class for return type covariance
        }

        public override GridGeoTransform TileTransform
        {
            get 
            {
                if (this.tileGrid == null)
                {
                    throw new InvalidOperationException("Virtual raster's tile grid has not yet been created. Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TileTransform) + ".");
                }
                return this.tileGrid.Transform; 
            }
        }

        public void Add(int tileIndexX, int tileIndexY, TTile tile)
        {
            if (this.TileGrid == null)
            {
                throw new InvalidOperationException("Virtual raster's tile grid has not yet been created. Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TileTransform) + ".");
            }

            TTile? existingTile = this.TileGrid[tileIndexX, tileIndexY];
            if (existingTile != null)
            {
                throw new InvalidOperationException("Tile cannot be added at (" + tileIndexX + ", " + tileIndexY + ") because a tile is already present at that location.");
            }

            this.TileGrid[tileIndexX, tileIndexY] = tile;
            ++this.NonNullTileCount;
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

            this.AddBandMetadata(tile);

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

            ++this.NonNullTileCount;
            return (tileIndexX, tileIndexY);
        }

        private void AddBandMetadata(TTile tile)
        {
            if (this.BandNames.Count == 0)
            {
                // latch bands of first tile added
                foreach (RasterBand tileBand in tile.GetBands())
                {
                    this.BandDataTypes.Add(tileBand.GetGdalDataType());
                    this.BandNames.Add(tileBand.Name); // should names be checked for uniqueness or numbers inserted if null or empty?
                    if (tileBand.HasNoDataValue)
                    {
                        this.NoDataValuesByBand.Add([tileBand.GetNoDataValueAsDouble()]);
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
        }

        public VirtualRaster<TTileCopy> CreateEmptyCopy<TTileCopy>() where TTileCopy : Raster
        {
            VirtualRaster<TTileCopy> emptyCopy = new(this);
            if (this.tileGrid != null)
            {
                emptyCopy.tileGrid = new(emptyCopy.Crs, new(this.tileGrid.Transform), emptyCopy.SizeInTilesX, emptyCopy.SizeInTilesY, cloneCrsAndTransform: false);
            }

            return emptyCopy;
        }

        /// <summary>
        /// Infers virtual raster's grid from added tiles and places tiles into the grid.
        /// </summary>
        /// <remarks>
        /// Fails if two (or more tiles) fit in the same grid cell. If the grid is unexpectedly sparse, check that the added tiles' sizes 
        /// and origins are correct.
        /// </remarks>
        public (int[] tileIndexX, int[] tileIndexY) CreateTileGrid()
        {
            if (this.tileGrid != null)
            {
                throw new InvalidOperationException("Tile grid is already created."); // debatable if one time call needs to be enforced
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
            this.SizeInTilesX = (int)Double.Round((maximumOriginX - minimumOriginX) / tileSizeInTileUnitsX) + 1;
            this.SizeInTilesY = (int)Double.Round((minimumOriginY - maximumOriginY) / tileSizeInTileUnitsY) + 1;

            GridGeoTransform tileTransform = new(minimumOriginX, maximumOriginY, tileSizeInTileUnitsX, tileSizeInTileUnitsY);            
            this.tileGrid = new(this.Crs, tileTransform, this.SizeInTilesX, this.SizeInTilesY, cloneCrsAndTransform: false);

            int[] tileIndicesX = new int[this.ungriddedTiles.Count];
            int[] tileIndicesY = new int[this.ungriddedTiles.Count];
            for (int tileIndex = 0; tileIndex < this.ungriddedTiles.Count; ++tileIndex)
            {
                (tileIndicesX[tileIndex], tileIndicesY[tileIndex]) = this.PlaceTileInGrid(this.ungriddedTiles[tileIndex]);
            }

            this.ungriddedTiles.Clear();
            return (tileIndicesX, tileIndicesY);
        }

        public VrtDataset CreateDataset(string vrtDatasetDirectoryPath, GridNullable<List<RasterBandStatistics>>? tileBandStatistics)
        {
            return this.CreateDataset(vrtDatasetDirectoryPath, this.BandNames, tileBandStatistics);
        }

        public VrtDataset CreateDataset(string vrtDatasetDirectoryPath, List<string> vrtBandNames, GridNullable<List<RasterBandStatistics>>? tileBandStatistics)
        {
            Debug.Assert(this.tileGrid != null);

            VrtDataset vrtDataset = new()
            {
                RasterXSize = (UInt32)(this.SizeInTilesX * this.TileSizeInCellsX),
                RasterYSize = (UInt32)(this.SizeInTilesY * this.TileSizeInCellsY)
            };
            vrtDataset.Srs.DataAxisToSrsAxisMapping = this.Crs.IsVertical() == 1 ? [ 1, 2, 3 ] : [ 1, 2 ];
            vrtDataset.Srs.WktGeogcsOrProj = this.Crs.GetWkt();

            vrtDataset.GeoTransform.Copy(this.tileGrid.Transform); // copies tile x and y size as cell size
            vrtDataset.GeoTransform.SetCellSize(this.TileCellSizeX, this.TileCellSizeY);

            vrtDataset.AppendBands(vrtDatasetDirectoryPath, this, vrtBandNames, tileBandStatistics);
            return vrtDataset;
        }

        public string GetExtentString()
        {
            if (this.tileGrid != null)
            {
                return this.tileGrid.GetExtentString();
            }

            return "unknown (virtual raster tile grid has not yet been created)";
        }

        public RasterNeighborhood8<TBand> GetNeighborhood8<TBand>(int tileGridIndexX, int tileGridIndexY, string? bandName) where TBand : struct, IMinMaxValue<TBand>, INumber<TBand>
        {
            Debug.Assert(this.tileGrid != null);
            Neighborhood8<TTile> neighborhood = new(tileGridIndexX, tileGridIndexY, this.tileGrid);

            return new RasterNeighborhood8<TBand>((RasterBand<TBand>)neighborhood.Center.GetBand(bandName))
            {
                North = (RasterBand<TBand>?)neighborhood.North?.GetBand(bandName),
                Northeast = (RasterBand<TBand>?)neighborhood.Northeast?.GetBand(bandName),
                Northwest = (RasterBand<TBand>?)neighborhood.Northwest?.GetBand(bandName),
                South = (RasterBand<TBand>?)neighborhood.South?.GetBand(bandName),
                Southeast = (RasterBand<TBand>?)neighborhood.Southeast?.GetBand(bandName),
                Southwest = (RasterBand<TBand>?)neighborhood.Southwest?.GetBand(bandName),
                East = (RasterBand<TBand>?)neighborhood.East?.GetBand(bandName),
                West = (RasterBand<TBand>?)neighborhood.West?.GetBand(bandName)
            };
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
            if (this.SizeInTilesX != other.SizeInTilesX)
            {
                return false;
            }
            if (this.SizeInTilesY != other.SizeInTilesY)
            {
                return false;
            }

            Debug.Assert((this.tileGrid != null) && (other.TileGrid != null));
            if (this.tileGrid.IsSameExtentAndSpatialResolution(other.TileGrid) == false)
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

        public void RefreshBandMetadata()
        {
            if (this.tileGrid == null)
            {
                // if needed, fallback to this.ungriddedTiles can be implemented
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.RefreshBandMetadata) + "().");
            }

            this.BandDataTypes.Clear();
            this.BandNames.Clear();
            this.NoDataValuesByBand.Clear();
            this.TileCountByBand.Clear();
            this.TilesWithNoDataValuesByBand.Clear();
            for (int tileIndex = 0; tileIndex < this.tileGrid.Cells; ++tileIndex)
            {
                TTile? tile = this.tileGrid[tileIndex];
                if (tile != null)
                {
                    this.AddBandMetadata(tile);
                }
            }
        }

        public (int tileIndexX, int tileIndexY) ToGridIndices(int tileIndex)
        {
            Debug.Assert(tileIndex >= 0);
            int tileIndexY = tileIndex / this.SizeInTilesX;
            int tileIndexX = tileIndex - this.SizeInTilesX * tileIndexY;
            return (tileIndexX, tileIndexY);
        }

        public bool TryGetNeighborhood8<TBand>(double x, double y, string? bandName, [NotNullWhen(true)] out RasterNeighborhood8<TBand>? neighborhood) where TBand : struct, IMinMaxValue<TBand>, INumber<TBand>
        {
            if (this.tileGrid == null)
            {
                throw new InvalidOperationException("Call " + nameof(this.CreateTileGrid) + "() before calling " + nameof(this.TryGetNeighborhood8) + "().");
            }

            (int tileGridXindex, int tileGridYindex) = this.tileGrid.ToGridIndices(x, y);
            if ((tileGridXindex < 0) || (tileGridXindex >= this.SizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.SizeInTilesY) ||
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
            if ((tileGridXindex < 0) || (tileGridXindex >= this.SizeInTilesX) ||
                (tileGridYindex < 0) || (tileGridYindex >= this.SizeInTilesY) ||
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
