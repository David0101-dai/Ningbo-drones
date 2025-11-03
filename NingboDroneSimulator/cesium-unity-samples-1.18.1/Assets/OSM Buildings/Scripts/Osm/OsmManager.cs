using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace BeanStudio
{
    public class OsmManager : MonoBehaviour
    {
        public static OsmManager Instance;

        // Reference to the runtime mesh class. See the class file to learn more about it.
        [SerializeField] RuntimeMesh runtimeMesh;

        // API endpoints for fetching OSM buildings data and tilemap imagery data.
        private const string OSM_BUILDING_API_ENDPOINT = "https://data.osmbuildings.org/0.2/anonymous/tile/";
        private const string OSM_TILEMAP_API_ENDPOINT = "http://a.tile.openstreetmap.org/";

        // Latitude and Longitude and their respective numeric multiplicative factors to convert Lat and Lon into meters.
        [SerializeField] double lat;
        private const float LATITUDE_INTO_KM = 110.574f;
        [SerializeField] double lon;
        private const float LONGITUDE_INTO_KM = 111.320f;
        // If on, the buildings are created according to their actual Lat Lon locations converted into meters.
        // If off, the positions are recalculated for the building to appear in the center of the scene.
        [SerializeField] bool bKeepRealWorldLocation = true;
        // Zoom level of OSM buildings. Ranges from 0 to 24.
        [Range(0, 24)] [SerializeField] int zoom;

        // Instance of the collection that is being currently rendered.
        Collection currentCollection;
        public Collection GetCurrentCollection { get { return currentCollection; } }

        // Callback that is executed when data is fetched and a new collection is created.
        public event System.Action<Collection> OnCreatedNewCollection;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
        }

        void Start()
        {
            // This method gets the OSM buildings data.
            GetOsmBuildingData(zoom, CoordinateConverter.long2tilex(lon, zoom), CoordinateConverter.lat2tiley(lat, zoom));

            // This is commented because OSM recommends hosting this service on your own servers.
            // This method gets the tilemap imagery from OSM tile server. Uncomment this to fetch the tilemap.
            //GetTileMapData(zoom, CoordinateConverter.long2tilex(lon, zoom), CoordinateConverter.lat2tiley(lat, zoom));
        }

        public void GetOsmBuildingData(int Z, double X, double Y)
        {
            StartCoroutine(SendHttpRequest(RequestType.Building, OSM_BUILDING_API_ENDPOINT + Z + "/" + X + "/" + Y + ".json"));
        }

        public void GetTileMapData(int Z, double X, double Y)
        {
            StartCoroutine(SendHttpRequest(RequestType.TileMap, OSM_TILEMAP_API_ENDPOINT + Z + "/" + X + "/" + Y + ".png"));
        }

        IEnumerator SendHttpRequest(RequestType reqType, string uri)
        {
            switch (reqType)
            {
                default:
                case RequestType.None:
                    Debug.LogError("Request type not mentioned!");
                    break;
                case RequestType.Building:
                    using (UnityWebRequest request = UnityWebRequest.Get(uri))
                    {
                        yield return request.SendWebRequest();
                        if (request.isNetworkError || request.isHttpError)
                            Debug.Log("Error received. Code: " + request.responseCode);
                        else
                            HandleBuildingData(request.downloadHandler.text);
                    }
                    break;
                case RequestType.TileMap:
                    using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(uri))
                    {
                        yield return request.SendWebRequest();
                        if (request.isNetworkError || request.isHttpError)
                            Debug.Log("Error received. Code: " + request.responseCode);
                        else
                            HandleTileMapData(((DownloadHandlerTexture)request.downloadHandler).texture);
                    }
                    break;
            }
        }

        void HandleBuildingData(string response)
        {
            Debug.Log(response);
            JSONObject responseData = (JSONObject)JSON.Parse(response);
            ContructNewCollection(responseData);
        }

        void HandleTileMapData(Texture texture)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.transform.SetParent(transform);
            plane.transform.position = new Vector3((float)(lon * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)lat) * 1000f), 0, (float)(lat * LATITUDE_INTO_KM * 1000f));
            plane.transform.localScale = new Vector3((float)(LONGITUDE_INTO_KM), 1f, (float)(LATITUDE_INTO_KM));
            plane.GetComponent<MeshRenderer>().material = new Material(Resources.Load("M_DefaultMaterial", typeof(Material)) as Material);
            plane.GetComponent<MeshRenderer>().material.SetTexture("_MainTex", texture);
        }

        // Contructing a new collection class based on the JSON response from OSM.
        Collection ContructNewCollection(JSONObject responseData)
        {
            Collection newCollection = new Collection();
            newCollection.type = responseData["type"];
            newCollection.features = new Collection.Feature[responseData["features"].Count];
            for (int i = 0; i < responseData["features"].Count; i++)
            {
                Collection.Feature feature = new Collection.Feature();
                feature.id = responseData["features"][i]["id"];
                feature.type = responseData["features"][i]["type"];

                Collection.Feature.Properties properties = new Collection.Feature.Properties();
                properties.height = responseData["features"][i]["properties"]["height"];
                properties.type = responseData["features"][i]["properties"]["type"];

                feature.properties = properties;

                Collection.Feature.Geometry geometry = new Collection.Feature.Geometry();
                geometry.type = responseData["features"][i]["geometry"]["type"];
                for (int j = 0; j < responseData["features"][i]["geometry"]["coordinates"].Count; j++)
                {
                    geometry.coordinates = new Collection.Feature.Geometry.Coordinates[responseData["features"][i]["geometry"]["coordinates"][j].Count - 1];
                    for (int k = 0; k < responseData["features"][i]["geometry"]["coordinates"][j].Count - 1; k++)
                    {
                        Collection.Feature.Geometry.Coordinates coordinates = new Collection.Feature.Geometry.Coordinates();
                        coordinates.lat = responseData["features"][i]["geometry"]["coordinates"][j][k][1];
                        coordinates.lon = responseData["features"][i]["geometry"]["coordinates"][j][k][0];
                        geometry.coordinates[k] = coordinates;
                    }
                }

                feature.geometry = geometry;
                newCollection.features[i] = feature;

                CreateElement(feature);
            }

            // Event is called to notify that a new collection has been created. You can subscribe to this
            // event if you want to do something when a new collection instance is creted.
            // To subscribe to this event from another class, say OsmManager.Instance.OnCreatedNewCollection += YourFunctionName();
            if (OnCreatedNewCollection != null)
                OnCreatedNewCollection(newCollection);

            currentCollection = newCollection;

            return newCollection;
        }

        void CreateElement(Collection.Feature buildingData)
        {
            GameObject building = new GameObject();
            building.name = "Building_" + buildingData.id;
            building.transform.SetParent(transform);

            RuntimeMesh Floor = Instantiate(runtimeMesh, building.transform);
            Floor.gameObject.name = "Floor";
            RuntimeMesh Roof = Instantiate(runtimeMesh, building.transform);
            Roof.gameObject.name = "Roof";

            for (int i = 0; i < buildingData.geometry.coordinates.Length; i++)
            {
                Floor.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, 0, (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                Roof.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, buildingData.properties.height, (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));

                RuntimeMesh Wall = Instantiate(runtimeMesh, building.transform);
                Wall.gameObject.name = "Wall_" + i;
                Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, 0, (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, buildingData.properties.height, (float)(buildingData.geometry.coordinates[i].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));

                if (i == buildingData.geometry.coordinates.Length - 1)
                {
                    Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[0].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[0].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, buildingData.properties.height, (float)(buildingData.geometry.coordinates[0].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                    Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[0].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[0].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, 0, (float)(buildingData.geometry.coordinates[0].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                }
                else
                {
                    Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i + 1].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i + 1].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, buildingData.properties.height, (float)(buildingData.geometry.coordinates[i + 1].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                    Wall.vertexCoordinates.Add(new Vector3((float)(buildingData.geometry.coordinates[i + 1].lon - (bKeepRealWorldLocation ? 0.0f : lon)) * LONGITUDE_INTO_KM * Mathf.Cos(Mathf.Deg2Rad * (float)(buildingData.geometry.coordinates[i + 1].lat - (bKeepRealWorldLocation ? 0.0f : lat))) * 1000f, 0, (float)(buildingData.geometry.coordinates[i + 1].lat - (bKeepRealWorldLocation ? 0.0f : lat)) * LATITUDE_INTO_KM * 1000f));
                }
            }

            CameraController.Instance.transform.position = Floor.vertexCoordinates[0];
        }

        enum RequestType
        {
            None,
            Building,
            TileMap,
        }
    }
}