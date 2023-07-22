namespace Mars.Clouds.GdalExtensions
{
    // https://github.com/OSGeo/gdal/blob/master/ogr/ogr_core.h
    internal static class OgrError
    {
        public const int NONE = 0;                      /**< Success */
        public const int NOT_ENOUGH_DATA = 1;           /**< Not enough data to deserialize */
        public const int NOT_ENOUGH_MEMORY = 2;         /**< Not enough memory */
        public const int UNSUPPORTED_GEOMETRY_TYPE = 3; /**< Unsupported geometry type */
        public const int UNSUPPORTED_OPERATION = 4;     /**< Unsupported operation */
        public const int CORRUPT_DATA = 5;              /**< Corrupt data */
        public const int FAILURE = 6;                   /**< Failure */
        public const int UNSUPPORTED_SRS = 7;           /**< Unsupported SRS */
        public const int INVALID_HANDLE = 8;            /**< Invalid handle */
        public const int NON_EXISTING_FEATURE = 9;      /**< Non existing feature. Added in GDAL 2.0 */
    }
}
