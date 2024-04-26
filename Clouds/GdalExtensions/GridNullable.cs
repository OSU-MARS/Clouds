using OSGeo.OSR;

namespace Mars.Clouds.GdalExtensions
{
    public class GridNullable<TCell> : Grid where TCell : class? // can't derive from Grid<TCell> due to C# requiring different nullability in class constraint
    {
        protected TCell?[] Data { get; private init; }

        public GridNullable(Grid extent)
            : this(extent, cloneCrsAndTransform: true)
        {
        }

        public GridNullable(Grid extent, bool cloneCrsAndTransform)
            : base(extent, cloneCrsAndTransform)
        {
            this.Data = new TCell?[this.Cells];
        }

        public GridNullable(SpatialReference crs, GridGeoTransform transform, int xSizeInCells, int ySizeInCells)
            : base(crs, transform, xSizeInCells, ySizeInCells, cloneCrsAndTransform: true)
        {
            this.Data = new TCell?[this.Cells];
        }

        public TCell? this[int cellIndex]
        {
            get { return this.Data[cellIndex]; }
            set { this.Data[cellIndex] = value; }
        }

        public TCell? this[int xIndex, int yIndex]
        {
            get { return this[this.ToCellIndex(xIndex, yIndex)]; }
            set { this.Data[this.ToCellIndex(xIndex, yIndex)] = value; }
        }

        public void Clear()
        {
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this[cellIndex] = null;
            }
        }

        /// <returns><see cref="bool"/> array whose values are true where cells are null</returns>
        public bool[,] GetUnpopulatedCellMap()
        {
            bool[,] cellMap = new bool[this.SizeX, this.SizeY];
            for (int yIndex = 0; yIndex < this.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < this.SizeX; ++xIndex)
                {
                    TCell? value = this[xIndex, yIndex];
                    if (value == null)
                    {
                        cellMap[xIndex, yIndex] = true;
                    }
                }
            }

            return cellMap;
        }
    }
}
