using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public static class SpatialReferenceExtensions
    {
        public static SpatialReference Create(SpatialReference horizontalCrs, SpatialReference verticalCrs) 
        {
            // SetCompoundCS() passes through to proj_create_compound_crs()
            // https://github.com/OSGeo/gdal/blob/master/ogr/ogrspatialreference.cpp
            // Unclear where source for proj_create_compound_crs() is, so check coordinate systems for unit consistency here.
            double horizontalUnits = horizontalCrs.GetLinearUnits();
            double verticalUnits = verticalCrs.GetLinearUnits();
            if (horizontalUnits != verticalUnits) 
            {
                throw new NotSupportedException("Horizontal coordinate system has units of " + horizontalUnits + " m while vertical coordinate system's units are " + verticalUnits + " m. Such mismatched compound coordinate systems are not currently supported.");
            }

            SpatialReference compoundCrs = new(String.Empty);
            if (compoundCrs.SetCompoundCS(horizontalCrs.GetName() + " + " + verticalCrs.GetName(), horizontalCrs, verticalCrs) != 0)
            {
                throw new GdalException("Could not create a compound spatial reference from " + horizontalCrs.GetName() + " and " + verticalCrs.GetName() + ".");
            }

            return compoundCrs;
        }

        public static SpatialReference Create(int horizontalEpsg, int verticalEpsg) 
        {
            SpatialReference horizontalCrs = new(String.Empty);
            if (horizontalCrs.ImportFromEPSG(horizontalEpsg) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalEpsg), "Could not create a spatial reference horizontal EPSG:" + horizontalEpsg + ".");
            }
            SpatialReference verticalCrs = new(String.Empty);
            if (verticalCrs.ImportFromEPSG(verticalEpsg) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(verticalEpsg), "Could not create a spatial reference from vertical EPSG:" + verticalEpsg + ".");
            }

            return SpatialReferenceExtensions.Create(horizontalCrs, verticalCrs);
        }

        public static SpatialReference CreateCompoundCrs(SpatialReference horizontalCrs, SpatialReference verticalCrs)
        {
            double horizontalLinearUnits = horizontalCrs.GetLinearUnits();
            double verticalLinearUnits = verticalCrs.GetLinearUnits();
            if (horizontalLinearUnits != verticalLinearUnits)
            {
                throw new NotSupportedException("Horizontal linear units of " + horizontalLinearUnits + " m for " + horizontalCrs.GetName() + " do not match vertical units of " + verticalLinearUnits + " m for " + verticalCrs.GetName() + ".");
            }

            return SpatialReferenceExtensions.Create(horizontalCrs, verticalCrs);
        }

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

        public static void ImportFromEpsg(this SpatialReference crs, int epsg)
        {
            if (crs.ImportFromEPSG(epsg) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(epsg), "Failed to import EPSG:" + epsg + " into GDAL spatial reference.");
            }
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
