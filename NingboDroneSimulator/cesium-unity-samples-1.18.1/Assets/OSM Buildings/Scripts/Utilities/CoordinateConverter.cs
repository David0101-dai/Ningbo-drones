using UnityEngine;

// A static class that takes care of converting OSM coordinates into world Latitude and Longitude and vice versa.
namespace BeanStudio
{
    public static class CoordinateConverter
    {
        public static int long2tilex(double lon, int z)
        {
            return (int)(Mathf.Floor(((float)lon + 180.0f) / 360.0f * (1 << z)));
        }

        public static int lat2tiley(double lat, int z)
        {
            return (int)Mathf.Floor((1 - Mathf.Log(Mathf.Tan((float)lat * Mathf.Deg2Rad) + 1 / Mathf.Cos((float)lat * Mathf.Deg2Rad)) / Mathf.PI) / 2 * (1 << z));
        }

        public static double tilex2long(int x, int z)
        {
            return x / (double)(1 << z) * 360.0 - 180;
        }

        public static double tiley2lat(int y, int z)
        {
            double n = Mathf.PI - 2.0 * Mathf.PI * y / (double)(1 << z);
            return 180.0f / Mathf.PI * Mathf.Atan(0.5f * (Mathf.Exp((float)n) - Mathf.Exp((float)-n)));
        }
    }
}