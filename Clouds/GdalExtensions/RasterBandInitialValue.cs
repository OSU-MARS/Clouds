namespace Mars.Clouds.GdalExtensions
{
    public enum RasterBandInitialValue : byte
    {
        /// <summary>
        /// Fill newly allocated bands with no data value.
        /// </summary>
        /// <remarks>
        /// This is interchangeable with <see cref="Default"> if the no data value is the same as the default CLR value for the band's data type
        /// </remarks>
        NoData = 0,

        /// <summary>
        /// Fill newly allocated bands with default CLR value for their data type.
        /// </summary>
        Default,

        /// <summary>
        /// Leave newly allocated band data uninitialized because it will be set later.
        /// </summary>
        Unintialized
    }
}
