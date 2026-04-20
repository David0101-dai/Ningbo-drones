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

        // ★ FIX: First pass — unsubscribe ALL events and stop all navigators
        //   This prevents callbacks from firing during destruction
        foreach (var info in allInfos)
        {
            if (info == null) continue;

            // Unsubscribe from all event systems
            if (OrderManager.Instance != null)
                OrderManager.Instance.UnsubscribeDrone(info);

            // Stop navigator immediately
            var nav = info.navigator;
            if (nav != null)
            {
                nav.SetStop(DroneGeoNavigator.StopReason.External, true);
                nav.enabled = false;
            }
        }

        // ★ FIX: Second pass — destroy all GameObjects
        foreach (var info in allInfos)
        {
            if (info == null) continue;

            GameObject go = info.gameObject;

#if UNITY_EDITOR
            DestroyImmediate(go);
#else
            Destroy(go);
#endif
            count++;
        }

        // ★ FIX: Force clear SwitchView targets
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

        // ★ FIX: Refresh CommandCenter to clear all stale references
        if (commandCenter != null)
            commandCenter.Refresh();

        Debug.Log("[DroneFactory] Removed " + count + " drones (immediate)");
        return count;
    }

    public GameObject SpawnDrone(string droneName, string spawnPointName, Color pathColor)
    {
        if (dronePrefab == null)
        {
            DLog.Error("General","[DroneFactory] No drone prefab assigned");
            return null;
        }

        if (georeference == null)
        {
            // ★ FIX: Try to find georeference again (might have been lost)
            georeference = FindObjectOfType<CesiumGeoreference>();
            if (georeference == null)
            {
                DLog.Error("General","[DroneFactory] No CesiumGeoreference found");
                return null;
            }
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
            DLog.Error("General","[DroneFactory] No spawn point available " +
                           "(looking for '" + spawnPointName + "')");
            return null;
        }

        _droneCounter++;
        if (string.IsNullOrEmpty(droneName))
            droneName = "Drone_" + _droneCounter.ToString("D2");

        // ★ FIX: Only check name collision, don't call full Refresh here
        //   (Refresh is expensive and already done before the spawn loop)
        if (commandCenter != null && commandCenter.TryGetInfo(droneName, out _))
        {
            // Also check with UAV_ prefix
            string fullName = "UAV_" + droneName;
            if (commandCenter.TryGetInfo(fullName, out _))
            {
                DLog.Warn("General","[DroneFactory] Drone '" + droneName +
                                 "' already exists! Appending counter suffix.");
                droneName = droneName + "_" + _droneCounter;
            }
        }

        GameObject drone = Instantiate(dronePrefab, georeference.transform);
        drone.name = "UAV_" + droneName;

        var anchor = drone.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) anchor = drone.AddComponent<CesiumGlobeAnchor>();

        // ★ FIX: Validate LLH before assigning
        double3 spawnLLH = spawnPoint.GetLLH();
        if (spawnLLH.x == 0 && spawnLLH.y == 0)
        {
            DLog.Error("General","[DroneFactory] Spawn point '" + spawnPointName +
                           "' has invalid LLH (0,0)! Using default.");
            spawnLLH = new double3(121.55, 29.87, 25.0);
        }
        anchor.longitudeLatitudeHeight = spawnLLH;

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

        // ★ FIX: Register drone immediately (don't call full Refresh)
        if (commandCenter != null)
            commandCenter.Refresh();

        if (OrderManager.Instance != null && info != null)
            OrderManager.Instance.SubscribeDrone(info);

        if (RouteDispatcher.Instance != null && info != null)
            RouteDispatcher.Instance.SubscribeDrone(info);

        Debug.Log("[DroneFactory] Spawned '" + droneName + "' at " +
                  spawnPoint.GetDisplayName() + " (LLH: " +
                  spawnLLH.x.ToString("F4") + ", " + spawnLLH.y.ToString("F4") +
                  ", " + spawnLLH.z.ToString("F1") + ")");
        return drone;
    }

    public bool RemoveDrone(string droneName)
    {
        if (commandCenter == null) return false;

        DroneInfo info = null;
        if (!commandCenter.TryGetInfo(droneName, out info))
        {
            if (!commandCenter.TryGetInfo("UAV_" + droneName, out info))
            {
                DLog.Warn("General","[DroneFactory] Drone '" + droneName + "' not found");
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

        Debug.Log("[DroneFactory] Removed drone '" + displayName + "'");
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