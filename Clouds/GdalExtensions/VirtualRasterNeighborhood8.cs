using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Mars.Clouds.GdalExtensions
{
    internal class VirtualRasterNeighborhood8<TBand> where TBand : INumber<TBand>
    {
        public RasterBand<TBand> Center { get; private init; }
        public RasterBand<TBand>? North { get; init; }
        public RasterBand<TBand>? Northeast { get; init; }
        public RasterBand<TBand>? Northwest { get; init; }
        public RasterBand<TBand>? South { get; init; }
        public RasterBand<TBand>? Southeast { get; init; }
        public RasterBand<TBand>? Southwest { get; init; }
        public RasterBand<TBand>? East { get; init; }
        public RasterBand<TBand>? West { get; init; }

        public VirtualRasterNeighborhood8(RasterBand<TBand> center)
        {
            this.Center = center;
        }

        public bool TryGetValue(int searchRowIndex, int searchColumnIndex, [NotNullWhen(true)] out TBand? value)
        {
            value = default;
            if (searchRowIndex < 0)
            {
                if (searchColumnIndex < 0)
                {
                    if (this.Northwest == null)
                    {
                        return false;
                    }
                    int northwestRowSearchIndex = this.Northwest.YSize + searchRowIndex;
                    int northwestColumnSearchIndex = this.Northwest.XSize + searchColumnIndex;
                    value = this.Northwest[northwestRowSearchIndex, northwestColumnSearchIndex];
                    if (this.Northwest.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (searchColumnIndex >= this.Center.XSize)
                {
                    if (this.Northeast == null)
                    {
                        return false;
                    }
                    int northeastRowSearchIndex = this.Northeast.YSize + searchRowIndex;
                    int northeastColumnSearchIndex = searchColumnIndex - this.Center.XSize;
                    value = this.Northeast[northeastRowSearchIndex, northeastColumnSearchIndex];
                    if (this.Northeast.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    if (this.North == null)
                    {
                        return false;
                    }
                    int northRowSearchIndex = this.North.YSize + searchRowIndex;
                    value = this.North[northRowSearchIndex, searchColumnIndex];
                    if (this.North.IsNoData(value))
                    {
                        return false;
                    }
                }
            }
            else if (searchRowIndex >= this.Center.YSize)
            {
                int southRowSearchIndex = searchRowIndex - this.Center.YSize;
                if (searchColumnIndex < 0)
                {
                    if (this.Southwest == null)
                    {
                        return false;
                    }
                    int westColumnSearchIndex = this.Southwest.XSize + searchColumnIndex;
                    value = this.Southwest[southRowSearchIndex, westColumnSearchIndex];
                    if (this.Southwest.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (searchColumnIndex >= this.Center.XSize)
                {
                    if (this.Southeast == null)
                    {
                        return false;
                    }
                    int eastColumnSearchIndex = searchColumnIndex - this.Center.XSize;
                    value = this.Southeast[southRowSearchIndex, eastColumnSearchIndex];
                    if (this.Southeast.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    if (this.South == null)
                    {
                        return false;
                    }
                    value = this.South[southRowSearchIndex, searchColumnIndex];
                    if (this.South.IsNoData(value))
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (searchColumnIndex < 0)
                {
                    if (this.West == null)
                    {
                        return false;
                    }
                    int westColumnIndex = this.West.XSize + searchColumnIndex;
                    value = this.West[searchRowIndex, westColumnIndex];
                    if (this.West.IsNoData(value))
                    {
                        return false;
                    }
                }
                else if (searchColumnIndex >= this.Center.XSize)
                {
                    if (this.East == null)
                    {
                        return false;
                    }
                    int eastColumnIndex = searchColumnIndex - this.Center.XSize;
                    value = this.East[searchRowIndex, eastColumnIndex];
                    if (this.East.IsNoData(value))
                    {
                        return false;
                    }
                }
                else
                {
                    // mainline case
                    value = this.Center[searchRowIndex, searchColumnIndex];
                    if (this.Center.IsNoData(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
