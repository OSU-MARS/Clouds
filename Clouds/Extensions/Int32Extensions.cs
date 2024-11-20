namespace Mars.Clouds.Extensions
{
    public static class Int32Extensions
    {
        public static int Min(int value1, int value2, int value3)
        {
            int minValue = value1;
            if (value2 < minValue)
            {
                minValue = value2;
            }
            if (value3 < minValue)
            {
                minValue = value3;
            }

            return minValue;
        }
    }
}
