using System;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.GdalExtensions
{
    public class Neighborhood8<T> where T : class
    {
        // need both private set to update and init for public construction
        private T center;
        private T? north;
        private T? northeast;
        private T? northwest;
        private T? south;
        private T? southeast;
        private T? southwest;
        private T? east;
        private T? west;

        protected Neighborhood8(T center)
        {
            this.center = center;
            this.north = null;
            this.northeast = null;
            this.northwest = null;
            this.south = null;
            this.southeast = null;
            this.southwest = null;
            this.east = null;
            this.west = null;
        }

        public Neighborhood8(int indexX, int indexY, Grid<T> grid)
        {
            this.MoveTo(indexX, indexY, grid);
        }

        public Neighborhood8(int indexX, int indexY, GridNullable<T> grid)
        {
            this.MoveTo(indexX, indexY, grid);
        }

        public T Center
        {
            get { return this.center; }
            protected set { this.center = value; }
        }

        public T? North
        {
            get { return this.north; }
            init { this.north = value; }
        }

        public T? Northeast
        {
            get { return this.northeast; }
            init { this.northeast = value; }
        }
        
        public T? Northwest
        { 
            get { return this.northwest; } 
            init { this.northwest = value; }
        }
        
        public T? South 
        { 
            get { return this.south; } 
            init { this.south = value; }
        }
        
        public T? Southeast 
        { 
            get { return this.southeast; } 
            init { this.southeast = value; }
        }
        
        public T? Southwest 
        { 
            get { return this.southwest; } 
            init { this.southwest = value; }
        }
        
        public T? East 
        { 
            get { return this.east; } 
            init { this.east = value; }
        }

        public T? West 
        {
            get { return this.west; }
            init { this.west = value; }
        }

        // identical to MoveTo() but unfortunately no way to avoid repeating the code due to limitations of C# 12 generics
        // See GridNullable.cs for further discussion.
        [MemberNotNull(nameof(Neighborhood8<T>.center))]
        public void MoveTo(int indexX, int indexY, Grid<T> grid)
        {
            T? center = grid[indexX, indexY];
            if (center == null)
            {
                throw new NotSupportedException($"Cell at ({indexX}, {indexY}) does not contain an object. Neighborhood would lack a center.");
            }

            this.center = center;

            int northIndex = indexY - 1;
            int southIndex = indexY + 1;
            int eastIndex = indexX + 1;
            int westIndex = indexX - 1;

            this.north = null;
            this.northeast = null;
            this.northwest = null;
            if (northIndex >= 0)
            {
                this.north = grid[indexX, northIndex];
                if (eastIndex < grid.SizeX)
                {
                    this.northeast = grid[eastIndex, northIndex];
                }
                if (westIndex >= 0)
                {
                    this.northwest = grid[westIndex, northIndex];
                }
            }

            this.south = null;
            this.southeast = null;
            this.southwest = null;
            if (southIndex < grid.SizeY)
            {
                this.south = grid[indexX, southIndex];
                if (eastIndex < grid.SizeX)
                {
                    this.southeast = grid[eastIndex, southIndex];
                }
                if (westIndex >= 0)
                {
                    this.southwest = grid[westIndex, southIndex];
                }
            }

            this.east = null;
            if (eastIndex < grid.SizeX)
            {
                this.east = grid[eastIndex, indexY];
            }

            this.west = null;
            if (westIndex >= 0)
            {
                this.west = grid[westIndex, indexY];
            }
        }

        [MemberNotNull(nameof(Neighborhood8<T>.center))]
        public void MoveTo(int indexX, int indexY, GridNullable<T> grid)
        {
            T? center = grid[indexX, indexY];
            if (center == null)
            {
                throw new NotSupportedException($"Cell at ({indexX}, {indexY}) does not contain an object. Neighborhood would lack a center.");
            }

            this.center = center;

            int northIndex = indexY - 1;
            int southIndex = indexY + 1;
            int eastIndex = indexX + 1;
            int westIndex = indexX - 1;

            this.north = null;
            this.northeast = null;
            this.northwest = null;
            if (northIndex >= 0)
            {
                this.north = grid[indexX, northIndex];
                if (eastIndex < grid.SizeX)
                {
                    this.northeast = grid[eastIndex, northIndex];
                }
                if (westIndex >= 0)
                {
                    this.northwest = grid[westIndex, northIndex];
                }
            }

            this.south = null;
            this.southeast = null;
            this.southwest = null;
            if (southIndex < grid.SizeY)
            {
                this.south = grid[indexX, southIndex];
                if (eastIndex < grid.SizeX)
                {
                    this.southeast = grid[eastIndex, southIndex];
                }
                if (westIndex >= 0)
                {
                    this.southwest = grid[westIndex, southIndex];
                }
            }

            this.east = null;
            if (eastIndex < grid.SizeX)
            {
                this.east = grid[eastIndex, indexY];
            }

            this.west = null;
            if (westIndex >= 0)
            {
                this.west = grid[westIndex, indexY];
            }
        }
    }
}
