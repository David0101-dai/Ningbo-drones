// Assets/Scripts/Mission/DroneFactory.cs
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;

public class DroneFactory : MonoBehaviour
{
    public static DroneFactory Instance;

    [Header("Drone Template")]
    public GameObject dronePrefab;

    [Header("References")]
    public DroneCommandCenter commandCenter;
    public LocationManager locationManager;
    public CesiumGeoreference georeference;

    [Header("Defaults")]
    public double defaultSpeed = 5.0;

    private int _droneCounter = 0;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!locationManager) locationManager = FindObjectOfType<LocationManager>();
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
    }

    public void ResetCounter()
    {
        _droneCounter = 0;
        Debug.Log("[DroneFactory] Counter reset to 0");
    }

    public int RemoveAllDronesImmediate()
    {
#if UNITY_2023_1_OR_NEWER
        var allInfos = FindObjectsByType<DroneInfo>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var allInfos = FindObjectsOfType<DroneInfo>(true);
#endif

        var switchView = FindObjectOfType<SwitchView>();
        int count = 0;

        foreach (var info in allInfos)
        {
            if (info == null) continue;

            if (OrderManager.Instance != null)
                OrderManager.Instance.UnsubscribeDrone(info);

            if (switchView != null)
            {
                Transform camTarget = info.transform.Find("CamTarget");
                if (camTarget != null)
                {
                    var list = new List<Transform>(
                        switchView.droneTargets ?? new Transform[0]);
                    list.Remove(camTarget);
                    switchView.droneTargets = list.ToArray();
                }
            }

            var nav = info.navigator;
            if (nav != null)
            {
                nav.SetStop(DroneGeoNavigator.StopReason.External, true);
                nav.enabled = false;
            }

            GameObject go = info.gameObject;

            // ★ FIX: Always use DestroyImmediate in Editor play mode
            // to prevent naming conflicts when a new batch of drones
            // is spawned in the same frame after clearing.
#if UNITY_EDITOR
            DestroyImmediate(go);
#else
            Destroy(go);
#endif

            count++;
        }

        if (switchView != null)
        {
            switchView.droneTargets = new Transform[0];
            if (switchView.sideView)
            {
                switchView.sideView.Follow = null;
                switchView.sideView.LookAt = null;
            }
            if (switchView.rearChase)
            {
                switchView.rearChase.Follow = null;
                switchView.rearChase.LookAt = null;
            }
        }

        if (commandCenter != null)
            commandCenter.Refresh();

        Debug.Log($"[DroneFactory] Removed {count} drones (immediate)");
        return count;
    }

    public GameObject SpawnDrone(string droneName, string spawnPointName, Color pathColor)
    {
        if (dronePrefab == null)
        {
            Debug.LogError("[DroneFactory] No drone prefab assigned");
            return null;
        }

        if (georeference == null)
        {
            Debug.LogError("[DroneFactory] No CesiumGeoreference found");
            return null;
        }

        LocationPoint spawnPoint = null;
        if (!string.IsNullOrEmpty(spawnPointName) && locationManager != null)
            spawnPoint = locationManager.GetPointByName(spawnPointName);

        if (spawnPoint == null && locationManager != null)
        {
            var spawns = locationManager.GetSpawnPoints();
            if (spawns.Count > 0) spawnPoint = spawns[0];
        }

        if (spawnPoint == null)
        {
            Debug.LogError($"[DroneFactory] No spawn point available " +
                           $"(looking for '{spawnPointName}')");
            return null;
        }

        _droneCounter++;
        if (string.IsNullOrEmpty(droneName))
            droneName = $"Drone_{_droneCounter:D2}";

        if (commandCenter != null)
        {
            commandCenter.Refresh();
            if (commandCenter.TryGetInfo(droneName, out _))
            {
                Debug.LogWarning($"[DroneFactory] Drone '{droneName}' already exists! " +
                                 $"Appending counter suffix.");
                droneName = $"{droneName}_{_droneCounter}";
            }
        }

        GameObject drone = Instantiate(dronePrefab, georeference.transform);
        drone.name = $"UAV_{droneName}";

        var anchor = drone.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) anchor = drone.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = spawnPoint.GetLLH();

        var nav = drone.GetComponent<DroneGeoNavigator>();
        if (nav != null)
        {
            nav.georeference = georeference;
            nav.anchor = anchor;
            nav.cruiseSpeed = defaultSpeed;
            nav.pathGizmoColor = pathColor;
            nav.startupDelay = 0f;
            nav.waypointsParent = null;
        }

        var info = drone.GetComponent<DroneInfo>();
        if (info != null)
        {
            info.displayName = droneName;
            info.uiColor = pathColor;
            info.navigator = nav;
            StartCoroutine(SetIdleNextFrame(info));
        }

        var droneSpec = drone.GetComponent<DroneSpec>();
        if (droneSpec == null)
            droneSpec = drone.AddComponent<DroneSpec>();
        droneSpec.maxCapacity = 200;
        droneSpec.currentLoad = 0;
        droneSpec.ResetFull();

        if (info != null)
            info.spec = droneSpec;

        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            Transform camTarget = drone.transform.Find("CamTarget");
            if (camTarget == null)
            {
                var ct = new GameObject("CamTarget");
                ct.transform.SetParent(drone.transform, false);
                ct.transform.localPosition = new Vector3(0, 2, 0);
                camTarget = ct.transform;
            }
            AddDroneToSwitchView(switchView, camTarget);
        }

        if (commandCenter != null)
            commandCenter.Refresh();

        if (OrderManager.Instance != null && info != null)
            OrderManager.Instance.SubscribeDrone(info);

        if (RouteDispatcher.Instance != null && info != null)
            RouteDispatcher.Instance.SubscribeDrone(info);

        Debug.Log($"[DroneFactory] Spawned '{droneName}' at " +
                  $"{spawnPoint.GetDisplayName()} under {georeference.name}");
        return drone;
    }

    public bool RemoveDrone(string droneName)
    {
        if (commandCenter == null) return false;

        DroneInfo info = null;
        if (!commandCenter.TryGetInfo(droneName, out info))
        {
            if (!commandCenter.TryGetInfo($"UAV_{droneName}", out info))
            {
                Debug.LogWarning($"[DroneFactory] Drone '{droneName}' not found");
                return false;
            }
        }

        if (OrderManager.Instance != null)
        {
            string infoName = info.GetName();
            OrderManager.Instance.UnsubscribeDrone(info);

            foreach (var order in OrderManager.Instance.AllOrders)
            {
                if (order.assignedDrone == infoName &&
                    order.status != DeliveryOrder.OrderStatus.Completed &&
                    order.status != DeliveryOrder.OrderStatus.Failed)
                {
                    order.status = DeliveryOrder.OrderStatus.Failed;
                    order.assignedDrone = "";
                }
            }
        }

        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            Transform camTarget = info.gameObject.transform.Find("CamTarget");
            if (camTarget != null)
                RemoveDroneFromSwitchView(switchView, camTarget);
        }

        GameObject droneObj = info.gameObject;
        string displayName = info.GetName();
        Destroy(droneObj);

        StartCoroutine(RefreshNextFrame());

        Debug.Log($"[DroneFactory] Removed drone '{displayName}'");
        return true;
    }

    public List<string> GetDroneNames()
    {
        if (commandCenter == null) return new List<string>();

        var snapshots = commandCenter.GetFleetSnapshot();
        var names = new List<string>();
        foreach (var s in snapshots)
            names.Add(s.name);
        return names;
    }

    private void AddDroneToSwitchView(SwitchView sv, Transform camTarget)
    {
        if (sv == null || camTarget == null) return;
        sv.CleanNullTargets();

        var list = new List<Transform>(sv.droneTargets ?? new Transform[0]);
        if (!list.Contains(camTarget))
        {
            list.Add(camTarget);
            sv.droneTargets = list.ToArray();
            Debug.Log($"[DroneFactory] Camera target added " +
                      $"(total: {sv.droneTargets.Length})");
        }
    }

    private void RemoveDroneFromSwitchView(SwitchView sv, Transform camTarget)
    {
        if (sv == null || camTarget == null) return;

        var list = new List<Transform>(sv.droneTargets ?? new Transform[0]);
        if (list.Remove(camTarget))
        {
            sv.droneTargets = list.ToArray();

            if (sv.CurrentDroneTarget == camTarget &&
                sv.droneTargets.Length > 0)
                sv.SelectDroneByIndex(0);

            Debug.Log($"[DroneFactory] Camera target removed " +
                      $"(total: {sv.droneTargets.Length})");
        }
    }

    private IEnumerator SetIdleNextFrame(DroneInfo info)
    {
        yield return null;
        if (info != null)
        {
            info.missionState = DroneInfo.MissionState.Idle;
            info.currentRouteName = "";
        }
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        if (commandCenter != null)
            commandCenter.Refresh();
    }
}