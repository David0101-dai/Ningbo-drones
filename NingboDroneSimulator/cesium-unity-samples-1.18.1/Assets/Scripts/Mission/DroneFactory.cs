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

    /// <summary>
    /// Spawn a new drone at the given spawn point.
    /// The drone is placed under CesiumGeoreference, registered with
    /// CommandCenter, SwitchView, and OrderManager.
    /// </summary>
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

        // ====== Find spawn point ======
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
            Debug.LogError("[DroneFactory] No spawn point available");
            return null;
        }

        // ====== Generate name ======
        _droneCounter++;
        if (string.IsNullOrEmpty(droneName))
            droneName = $"Drone_{_droneCounter:D2}";

        // ====== Instantiate under CesiumRoot (CRITICAL for CesiumGlobeAnchor) ======
        GameObject drone = Instantiate(dronePrefab, georeference.transform);
        drone.name = $"UAV_{droneName}";

        // ====== Configure CesiumGlobeAnchor ======
        var anchor = drone.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) anchor = drone.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = spawnPoint.GetLLH();

        // ====== Configure DroneGeoNavigator ======
        var nav = drone.GetComponent<DroneGeoNavigator>();
        if (nav != null)
        {
            nav.georeference = georeference;
            nav.anchor = anchor;
            nav.cruiseSpeed = defaultSpeed;
            nav.pathGizmoColor = pathColor;
            nav.startupDelay = 0f;

            // DO NOT set waypointsParent — the drone will receive
            // paths via InjectPath from OrderManager.
            // Start() now handles waypointsParent == null gracefully.
            nav.waypointsParent = null;
        }

        // ====== Configure DroneInfo ======
        var info = drone.GetComponent<DroneInfo>();
        if (info != null)
        {
            info.displayName = droneName;
            info.uiColor = pathColor;
            info.navigator = nav;
            StartCoroutine(SetIdleNextFrame(info));
        }

        // ====== Configure DroneSpec (新增) ======
        var droneSpec = drone.GetComponent<DroneSpec>();
        if (droneSpec == null)
            droneSpec = drone.AddComponent<DroneSpec>();
        droneSpec.maxCapacity = 200;    // Solomon default
        droneSpec.currentLoad = 0;
        droneSpec.ResetFull();

        // Link DroneInfo to DroneSpec
        if (info != null)
            info.spec = droneSpec;

        // ====== Register camera target with SwitchView ======
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            Transform camTarget = drone.transform.Find("CamTarget");
            if (camTarget == null)
            {
                // Create CamTarget if prefab doesn't have one
                var ct = new GameObject("CamTarget");
                ct.transform.SetParent(drone.transform, false);
                ct.transform.localPosition = new Vector3(0, 2, 0);
                camTarget = ct.transform;
            }
            AddDroneToSwitchView(switchView, camTarget);
        }

        // ====== Register with CommandCenter ======
        if (commandCenter != null)
            commandCenter.Refresh();

        // ====== Subscribe to OrderManager ======
        if (OrderManager.Instance != null && info != null)
            OrderManager.Instance.SubscribeDrone(info);

        // Diagnostic: verify drone state after spawn
        StartCoroutine(DiagnoseAfterSpawn(droneName));

        Debug.Log($"[DroneFactory] Spawned '{droneName}' at {spawnPoint.GetDisplayName()} under {georeference.name}");
        return drone;
    }

    /// <summary>
    /// Reset drone counter. Call before re-importing a new dataset.
    /// </summary>
    public void ResetCounter()
    {
        _droneCounter = 0;
        Debug.Log("[DroneFactory] Counter reset to 0");
    }

    private System.Collections.IEnumerator DiagnoseAfterSpawn(string droneName)
    {
        // Wait 2 frames for all Start() and coroutines to complete
        yield return null;
        yield return null;

        if (commandCenter == null) yield break;
        commandCenter.Refresh(); // Re-scan after Start() has run

        if (commandCenter.TryGetInfo(droneName, out var info))
        {
            var nav = info.navigator;
            Debug.Log($"[DroneFactory DIAG] '{droneName}': " +
                      $"IsIdle={info.IsIdle()}, " +
                      $"MissionState={info.missionState}, " +
                      $"HasNoPath={nav?.HasNoPath()}, " +
                      $"Progress={nav?.GetProgress():F1}%, " +
                      $"NavEnabled={nav?.enabled}, " +
                      $"Subscribed={OrderManager.Instance != null}");
        }
        else
        {
            Debug.LogWarning($"[DroneFactory DIAG] '{droneName}' NOT FOUND in CommandCenter! " +
                             $"Available: {string.Join(", ", commandCenter.GetAllDroneNames())}");
        }

        // Also check idle drones list
        var idleList = commandCenter.GetIdleDrones();
        Debug.Log($"[DroneFactory DIAG] Idle drones: [{string.Join(", ", idleList)}]");
    }

    /// <summary>
    /// Remove a drone from the scene completely.
    /// </summary>
    public bool RemoveDrone(string droneName)
    {
        if (commandCenter == null) return false;

        // Try to find by display name first, then by object name
        DroneInfo info = null;
        if (!commandCenter.TryGetInfo(droneName, out info))
        {
            // Also try with "UAV_" prefix
            if (!commandCenter.TryGetInfo($"UAV_{droneName}", out info))
            {
                Debug.LogWarning($"[DroneFactory] Drone '{droneName}' not found");
                return false;
            }
        }

        // ====== Cancel active orders ======
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

        // ====== Remove camera target from SwitchView ======
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            Transform camTarget = info.gameObject.transform.Find("CamTarget");
            if (camTarget != null)
                RemoveDroneFromSwitchView(switchView, camTarget);
        }

        // ====== Destroy ======
        GameObject droneObj = info.gameObject;
        string displayName = info.GetName();
        Destroy(droneObj);

        StartCoroutine(RefreshNextFrame());

        Debug.Log($"[DroneFactory] Removed drone '{displayName}'");
        return true;
    }

    /// <summary>
    /// Get list of drone display names for UI dropdowns.
    /// Returns unique names only.
    /// </summary>
    public List<string> GetDroneNames()
    {
        if (commandCenter == null) return new List<string>();

        var snapshots = commandCenter.GetFleetSnapshot();
        var names = new List<string>();
        foreach (var s in snapshots)
            names.Add(s.name);
        return names;
    }

    // ================================================================
    //  SwitchView Integration
    // ================================================================

    private void AddDroneToSwitchView(SwitchView sv, Transform camTarget)
    {
        if (sv == null || camTarget == null) return;

        // Convert array to list, add, convert back
        var list = new List<Transform>(sv.droneTargets ?? new Transform[0]);
        if (!list.Contains(camTarget))
        {
            list.Add(camTarget);
            sv.droneTargets = list.ToArray();
            Debug.Log($"[DroneFactory] Camera target added (total: {sv.droneTargets.Length})");
        }
    }

    private void RemoveDroneFromSwitchView(SwitchView sv, Transform camTarget)
    {
        if (sv == null || camTarget == null) return;

        var list = new List<Transform>(sv.droneTargets ?? new Transform[0]);
        if (list.Remove(camTarget))
        {
            sv.droneTargets = list.ToArray();

            // If currently following this drone, switch to first available
            if (sv.CurrentDroneTarget == camTarget && sv.droneTargets.Length > 0)
                sv.SelectDroneByIndex(0);

            Debug.Log($"[DroneFactory] Camera target removed (total: {sv.droneTargets.Length})");
        }
    }

    // ================================================================
    //  Coroutines
    // ================================================================

    private IEnumerator SetIdleNextFrame(DroneInfo info)
    {
        yield return null; // Wait one frame for Start() to finish
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