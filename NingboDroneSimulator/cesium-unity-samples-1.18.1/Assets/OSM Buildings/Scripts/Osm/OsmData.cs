using System.Collections.Generic;
using UnityEngine;


// This class contains the data that obtained from the osmbuildings.org
// and mirrors the structure of the same.
namespace BeanStudio
{
    [System.Serializable]
    public class Collection
    {
        public string type;
        public Feature[] features;

        [System.Serializable]
        public class Feature
        {
            public string id;
            public string type;
            public Properties properties;
            public Geometry geometry;

            [System.Serializable]
            public class Properties
            {
                public float height;
                public string type;
            }

            [System.Serializable]
            public class Geometry
            {
                public string type;
                public Coordinates[] coordinates;

                [System.Serializable]
                public class Coordinates
                {
                    public double lat;
                    public double lon;
                }
            }
        }
    }
}