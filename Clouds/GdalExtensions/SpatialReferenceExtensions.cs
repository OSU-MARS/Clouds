using OSGeo.GDAL;
using OSGeo.OSR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public static class SpatialReferenceExtensions
    {
        public static SpatialReference Create(int epsg)
        {
            SpatialReference crs = new(String.Empty);
            CPLErr gdalError = (CPLErr)crs.ImportFromEPSG(epsg);
            if (gdalError != CPLErr.CE_None)
            {
                throw new ArgumentOutOfRangeException(nameof(epsg), "Could not create a spatial reference from EPSG:" + epsg + ". EPSG import failed with GDAL error code " + gdalError + ".");
            }

            return crs;
        }

        public static SpatialReference CreateCompound(int horizontalEpsg, int verticalEpsg)
        {
            SpatialReference horizontalCrs = SpatialReferenceExtensions.Create(horizontalEpsg);
            SpatialReference verticalCrs = SpatialReferenceExtensions.Create(verticalEpsg);
            return SpatialReferenceExtensions.CreateCompound(horizontalCrs, verticalCrs);
        }

        /// <summary>
        /// Create a compound coordinate system.
        /// </summary>
        /// <remarks>
        /// No matching constraint is placed on the horizontal and vertical units as callers require an ability to 
        /// create inconsistent coordinate systems.
        /// </remarks>
        public static SpatialReference CreateCompound(SpatialReference horizontalCrs, SpatialReference verticalCrs) 
        {
            // SetCompoundCS() passes through to proj_create_compound_crs()
            // https://github.com/OSGeo/gdal/blob/master/ogr/ogrspatialreference.cpp
            // Unclear where source for proj_create_compound_crs().
            SpatialReference compoundCrs = new(String.Empty);
            CPLErr gdalError = (CPLErr)compoundCrs.SetCompoundCS(horizontalCrs.GetName() + " + " + verticalCrs.GetName(), horizontalCrs, verticalCrs);
            if (gdalError != CPLErr.CE_None)
            {
                throw new GdalException("Creating a compound spatial reference from " + horizontalCrs.GetName() + " and " + verticalCrs.GetName() + " failed with GDAL error code " + gdalError + ".");
            }

            return compoundCrs;
        }

        public static double GetProjectedLinearUnitInM(this SpatialReference crs)
        {
            return crs.GetTargetLinearUnits(Constant.Gdal.TargetLinearUnitsProjectedCrs);
        }

        public static string GetWkt(this SpatialReference crs)
        {
            if (crs.ExportToWkt(out string wkt, []) != OgrError.NONE)
            {
                throw new InvalidOperationException("Exporting CRS '" + crs.GetName() + "' to well known text failed.");
            }

            return wkt;
        }

        public static double GetVerticalLinearUnitInM(this SpatialReference crs)
        {
            return crs.GetTargetLinearUnits(Constant.Gdal.TargetLinearUnitsVerticalCrs);
        }

        public static void ImportFromEpsg(this SpatialReference crs, int epsg)
        {
            CPLErr gdalError = (CPLErr)crs.ImportFromEPSG(epsg);
            if (gdalError != CPLErr.CE_None)
            {
                throw new ArgumentOutOfRangeException(nameof(epsg), "Importing EPSG:" + epsg + " into GDAL spatial reference failed with GDAL error code" + gdalError + ".");
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
