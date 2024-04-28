using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public static class SpatialReferenceExtensions
    {
        public static string GetLinearUnitsPlural(this SpatialReference crs)
        {
            return crs.GetLinearUnits() == 1.0 ? "m" : "feet";
        }

        public static string GetWkt(this SpatialReference crs)
        {
            if (crs.ExportToWkt(out string wkt, []) != OgrError.NONE)
            {
                throw new InvalidOperationException("Exporting CRS '" + crs.GetName() + "' to well known text failed.");
            }

            return wkt;
        }

        public static bool IsSameCrs(SpatialReference crs1, SpatialReference crs2)
        {
            if (crs1.IsSameGeogCS(crs2) != 1)
            {
                return false;
            }
            int crs1isVertical = crs1.IsVertical();
            int crs2isVertical = crs2.IsVertical();
            if ((crs1isVertical != 0) && (crs2isVertical != 0) && (crs1.IsSameVertCS(crs2) != 1))
            {
                return false;
            }

            // default case (weak but not practically avoidable): if either spatial reference lacks a vertical CRS assume they have the same vertical CRS
            return true;
        }

        public static int ParseEpsg(this SpatialReference crs)
        {
            return Int32.Parse(crs.GetAuthorityCode("PROJCS"));
        }
    }
}
