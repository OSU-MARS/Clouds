using OSGeo.OSR;

namespace Mars.Clouds.GdalExtensions
{
    public static class SpatialReferenceExtensions
    {
        public static bool IsSameCrs(SpatialReference crs1, SpatialReference crs2)
        {
            if (crs1.IsSameGeogCS(crs2) != 1)
            {
                return false;
            }
            int thisIsVertical = crs1.IsVertical();
            int otherIsVertical = crs2.IsVertical();
            if (thisIsVertical != otherIsVertical)
            {
                return false;
            }
            if ((thisIsVertical != 0) && (otherIsVertical != 0) && (crs1.IsSameVertCS(crs2) != 1))
            {
                return false;
            }

            // default case (weak but not practically avoidable): if neither the DSM or other has a vertical CRS assume they have the same vertical CRS
            return true;
        }
    }
}
