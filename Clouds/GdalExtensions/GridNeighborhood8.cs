using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public class GridNeighborhood8<TGrid, TCell>(int indexX, int indexY, GridNullable<TGrid> grid) : Neighborhood8<TGrid>(indexX, indexY, grid) where TGrid : Grid<TCell>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int xIndex, int yIndex, [NotNullWhen(true)] out TCell? value)
        {
            value = default;
            if (yIndex < 0)
            {
                if (xIndex < 0)
                {
                    if (this.Northwest == null)
                    {
                        return false;
                    }
                    int northwestYindex = this.Northwest.SizeY + yIndex;
                    int northwestXindex = this.Northwest.SizeX + xIndex;
                    value = this.Northwest[northwestXindex, northwestYindex];
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.Northeast == null)
                    {
                        return false;
                    }
                    int northeastYindex = this.Northeast.SizeY + yIndex;
                    int northeastXindex = xIndex - this.Center.SizeX;
                    value = this.Northeast[northeastXindex, northeastYindex];
                }
                else
                {
                    if (this.North == null)
                    {
                        return false;
                    }
                    int northYindex = this.North.SizeY + yIndex;
                    value = this.North[xIndex, northYindex];
                }
            }
            else if (yIndex >= this.Center.SizeY)
            {
                int southYindex = yIndex - this.Center.SizeY;
                if (xIndex < 0)
                {
                    if (this.Southwest == null)
                    {
                        return false;
                    }
                    int westXindex = this.Southwest.SizeX + xIndex;
                    value = this.Southwest[westXindex, southYindex];
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.Southeast == null)
                    {
                        return false;
                    }
                    int eastXindex = xIndex - this.Center.SizeX;
                    value = this.Southeast[eastXindex, southYindex];
                }
                else
                {
                    if (this.South == null)
                    {
                        return false;
                    }
                    value = this.South[xIndex, southYindex];
                }
            }
            else
            {
                if (xIndex < 0)
                {
                    if (this.West == null)
                    {
                        return false;
                    }
                    int westXindex = this.West.SizeX + xIndex;
                    value = this.West[westXindex, yIndex];
                }
                else if (xIndex >= this.Center.SizeX)
                {
                    if (this.East == null)
                    {
                        return false;
                    }
                    int eastXindex = xIndex - this.Center.SizeX;
                    value = this.East[eastXindex, yIndex];
                }
                else
                {
                    // mainline case
                    value = this.Center[xIndex, yIndex];
                }
            }

            return value != null;
        }
    }
}