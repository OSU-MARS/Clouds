using System;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.GdalExtensions
{
    public static class RasterBandExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float InterpolateBilinear(this RasterBand<float> band, double x, double y)
        {
            (double xIndexFractional, double yIndexFractional) = band.Transform.ToFractionalIndices(x, y);
            int xIndex0 = (int)xIndexFractional;
            int xIndex1 = xIndex0 + 1;
            int yIndex0 = (int)yIndexFractional;
            int yIndex1 = yIndex0 + 1;
            if ((xIndex0 < 0) || (xIndex1 >= band.SizeX))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"Bilinear interpolation cannot be performed as coordinate ({x}, {y}) does not lie within grid extents {band.GetExtentString()}.");
            }
            if ((yIndex0 < 0) || (yIndex1 >= band.SizeY))
            {
                throw new ArgumentOutOfRangeException(nameof(y), $"Bilinear interpolation cannot be performed as coordinate ({x}, {y}) does not lie within grid extents {band.GetExtentString()}.");
            }

            int cellIndexX0Y0 = band.ToCellIndex(xIndex0, yIndex0);
            float valueX0Y0 = band.Data[cellIndexX0Y0];
            float valueX1Y0 = band.Data[cellIndexX0Y0 + 1];
            int cellIndexX0Y1 = cellIndexX0Y0 + band.SizeX;
            float valueX0Y1 = band.Data[cellIndexX0Y1];
            float valueX1Y1 = band.Data[cellIndexX0Y1 + 1];

            float xFraction = (float)xIndexFractional - (float)xIndex0;
            float inverseXfraction = 1.0F - xFraction;
            float yFraction = (float)yIndexFractional - (float)yIndex0;
            float inverseYfraction = 1.0F - yFraction;

            float interpolatedValue = inverseXfraction * (inverseYfraction * valueX0Y0 + yFraction * valueX0Y1) +
                                      xFraction * (inverseYfraction * valueX1Y0 + yFraction * valueX1Y1);
            return interpolatedValue;
        }
    }
}
