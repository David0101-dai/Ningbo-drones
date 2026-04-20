// Assets/Scripts/Data/SolomonImporter.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class SolomonImporter : MonoBehaviour
{
    public static SolomonImporter Instance;

    [Header("References")]
    public LocationManager locationManager;
    public OrderManager orderManager;
    public DroneFactory droneFactory;
    public DroneCommandCenter commandCenter;
    public CesiumGeoreference georeference;

    [Header("Coordinate Mapping - Ningbo Default")]
    public double centerLongitude = 121.5500;
    public double centerLatitude = 29.8700;
    public double scaleMetersPerUnit = 50.0;
    public double flightHeight = 20.0;

    [Header("Import Settings")]
    public bool clearBeforeImport = true;
    public bool autoSpawnDrones = true;
    public int maxAutoSpawnDrones = 0;

    private static readonly string NL = System.Environment.NewLine;

    [Header("State")]
    [SerializeField] private string _lastImportName = "";
    [SerializeField] private int _lastCustomerCount = 0;

    private SolomonDataset _currentDataset;
    public SolomonDataset CurrentDataset => _currentDataset;

    public System.Action<SolomonDataset> OnDatasetImported;
    public System.Action<string> OnImportStatus;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (!locationManager) locationManager = FindObjectOfType<LocationManager>();
        if (!orderManager) orderManager = FindObjectOfType<OrderManager>();
        if (!droneFactory) droneFactory = FindObjectOfType<DroneFactory>();
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
    }

    // ================================================================
    //  Main Import Pipeline
    // ================================================================

    public bool ImportFromFile(string filePath)
    {
        EmitStatus("Parsing file: " + System.IO.Path.GetFileName(filePath) + "...");

        SolomonDataset dataset = null;
        try
        {
            dataset = SolomonParser.ParseFile(filePath);
        }
        catch (System.Exception e)
        {
            DLog.Error("General", "[SolomonImporter] EXCEPTION parsing file: " + e);
            EmitStatus("[!] Exception parsing file: " + e.Message);
            return false;
        }

        if (dataset == null || dataset.CustomerCount == 0)
        {
            EmitStatus("[!] Failed to parse file or no customers found");
            return false;
        }

        return ImportDataset(dataset);
    }

    public bool ImportDataset(SolomonDataset dataset)
    {
        Debug.Log("========== [SolomonImporter] BEGIN IMPORT: " + dataset.name + " ==========");

        // ★ FIX #1: Block auto-assign at the very start — and NEVER unblock inside ClearExistingData
        if (OrderManager.Instance != null)
            OrderManager.Instance.SetSolomonImportActive(true);

        _lastImportName = dataset.name;
        _lastCustomerCount = dataset.CustomerCount;

        EmitStatus("Importing '" + dataset.name + "': " + dataset.CustomerCount +
                   " customers, " + dataset.vehicleCount + " vehicles (cap=" +
                   dataset.vehicleCapacity + ")");

        // ── Step 1: Coordinate Mapping ──
        try
        {
            ApplyCoordinateMapping(dataset);
            Debug.Log("[SolomonImporter] Step 1 OK: Coordinate mapping applied");
        }
        catch (System.Exception e)
        {
            DLog.Error("General", "[SolomonImporter] EXCEPTION in Step 1 (CoordinateMapping): " + e);
            EmitStatus("[!] Failed at coordinate mapping: " + e.Message);
            return false;
        }

        // ── Step 2: Clear Existing Data ──
        if (clearBeforeImport)
        {
            ClearExistingData();
        }

        Debug.Log("[SolomonImporter] Step 2 OK: Existing data cleared");

        // Refresh location manager to purge any zombie references from Destroy()
        if (locationManager != null)
            locationManager.RefreshPoints();

        _currentDataset = dataset;

        // ── Step 3: Create Depot ──
        string depotName = "Depot_" + dataset.name;
        try
        {
            CreateDepot(dataset);
            Debug.Log("[SolomonImporter] Step 3 OK: CreateDepot called for '" + depotName + "'");
        }
        catch (System.Exception e)
        {
            DLog.Error("General", "[SolomonImporter] EXCEPTION in Step 3 (CreateDepot): " + e);
            EmitStatus("[!] Failed to create depot: " + e.Message);
            return false;
        }

        // Verify depot actually exists
        try
        {
            if (locationManager != null)
            {
                var depotCheck = locationManager.GetPointByName(depotName);
                if (depotCheck == null)
                {
                    var spawns = locationManager.GetSpawnPoints();
                    if (spawns != null && spawns.Count > 0)
                    {
                        depotName = spawns[0].GetDisplayName();
                        DLog.Warn("General", "[SolomonImporter] Depot not found, using fallback: " + depotName);
                    }
                    else
                    {
                        DLog.Error("General", "[SolomonImporter] CRITICAL: No depot and no spawn points!");
                        EmitStatus("[!] No depot found. Import failed.");
                        return false;
                    }
                }
                else
                {
                    Debug.Log("[SolomonImporter] Step 3 VERIFIED: Depot '" + depotName + "' exists");
                }
            }
        }
        catch (System.Exception e)
        {
            DLog.Warn("General", "[SolomonImporter] Depot verification (non-fatal): " + e.Message);
        }

        // ── Step 4: Create Customer Orders ──
        int ordersCreated = 0;
        try
        {
            ordersCreated = CreateCustomerOrders(dataset);
            Debug.Log("[SolomonImporter] Step 4 OK: Created " + ordersCreated + " orders");
        }
        catch (System.Exception e)
        {
            DLog.Error("General", "[SolomonImporter] EXCEPTION in Step 4 (CreateCustomerOrders): " + e);
            EmitStatus("[!] Failed to create orders: " + e.Message);
            return false;
        }

        // Verify orders exist
        if (orderManager != null)
        {
            int totalOrders = orderManager.TotalCount;
            int pendingOrders = orderManager.PendingCount;
            Debug.Log("[SolomonImporter] Step 4 VERIFIED: OrderManager has " +
                      totalOrders + " total, " + pendingOrders + " pending");
            if (totalOrders == 0)
            {
                DLog.Error("General", "[SolomonImporter] WARNING: No orders in OrderManager after creation!");
            }
        }

        // ── Step 5: Auto-spawn Drones ──
        int dronesSpawned = 0;
        if (autoSpawnDrones)
        {
            try
            {
                dronesSpawned = SpawnDrones(dataset, depotName);
                Debug.Log("[SolomonImporter] Step 5 OK: Spawned " + dronesSpawned + " drones");
            }
            catch (System.Exception e)
            {
                DLog.Error("General", "[SolomonImporter] EXCEPTION in Step 5 (SpawnDrones): " + e);
                EmitStatus("[!] Failed to spawn drones: " + e.Message);
            }
        }

        // ── Step 5b: Final refresh ──
        try
        {
            if (commandCenter != null)
                commandCenter.Refresh();
            StartCoroutine(RefreshCommandCenterNextFrame());
        }
        catch (System.Exception e)
        {
            DLog.Warn("General", "[SolomonImporter] Warning in post-spawn refresh: " + e.Message);
        }

        // ── Step 6: Start simulation clock ──
        try
        {
            if (SimClock.Instance != null)
                SimClock.Instance.StartSimulation(0f);
        }
        catch (System.Exception e)
        {
            DLog.Warn("General", "[SolomonImporter] Warning starting SimClock: " + e.Message);
        }

        // ── Final Summary ──
        string summary = "[OK] Import complete!" + NL +
                         "  Dataset: " + dataset.name + NL +
                         "  Customers: " + dataset.CustomerCount + NL +
                         "  Orders created: " + ordersCreated + NL +
                         "  Drones spawned: " + dronesSpawned + NL +
                         "  Total demand: " + dataset.TotalDemand + NL +
                         "  Time horizon: " + dataset.TimeHorizon.ToString("F0") + NL +
                         "  Area: " + centerLatitude.ToString("F4") + ", " +
                         centerLongitude.ToString("F4");

        EmitStatus(summary);
        Debug.Log("[SolomonImporter] " + summary);
        Debug.Log("========== [SolomonImporter] END IMPORT: " + dataset.name + " ==========");

        // ★ NOTE: SetSolomonImportActive remains TRUE here!
        // Solomon orders stay blocked until Dispatch or explicit Clear.

        OnDatasetImported?.Invoke(dataset);
        return true;
    }

    // ================================================================
    //  Step 1: Coordinate Mapping
    // ================================================================

    private void ApplyCoordinateMapping(SolomonDataset dataset)
    {
        if (dataset.mapping != null)
        {
            centerLongitude = dataset.mapping.centerLongitude;
            centerLatitude = dataset.mapping.centerLatitude;
            scaleMetersPerUnit = dataset.mapping.scaleMetersPerUnit;
            flightHeight = dataset.mapping.flightHeightMeters;
        }
        else
        {
            dataset.mapping = new SolomonDataset.CoordinateMapping
            {
                centerLongitude = centerLongitude,
                centerLatitude = centerLatitude,
                scaleMetersPerUnit = scaleMetersPerUnit,
                flightHeightMeters = flightHeight
            };
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        if (dataset.depot != null)
        {
            minX = Mathf.Min(minX, dataset.depot.x);
            maxX = Mathf.Max(maxX, dataset.depot.x);
            minY = Mathf.Min(minY, dataset.depot.y);
            maxY = Mathf.Max(maxY, dataset.depot.y);
        }

        foreach (var c in dataset.customers)
        {
            minX = Mathf.Min(minX, c.x);
            maxX = Mathf.Max(maxX, c.x);
            minY = Mathf.Min(minY, c.y);
            maxY = Mathf.Max(maxY, c.y);
        }

        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        double metersPerDegreeLon = 111320.0 *
            System.Math.Cos(centerLatitude * System.Math.PI / 180.0);
        double metersPerDegreeLat = 110540.0;

        double desiredAGL = flightHeight;
        double estimatedGroundElevation = 5.0;
        double uniformWgs84Height = estimatedGroundElevation + desiredAGL;

        if (dataset.depot != null)
            MapCustomerToLLH(dataset.depot, centerX, centerY,
                             metersPerDegreeLon, metersPerDegreeLat, uniformWgs84Height);

        foreach (var c in dataset.customers)
            MapCustomerToLLH(c, centerX, centerY,
                             metersPerDegreeLon, metersPerDegreeLat, uniformWgs84Height);
    }

    private void MapCustomerToLLH(SolomonCustomer c, float centerX, float centerY,
        double metersPerDegreeLon, double metersPerDegreeLat, double uniformHeight)
    {
        double offsetMetersX = (c.x - centerX) * scaleMetersPerUnit;
        double offsetMetersY = (c.y - centerY) * scaleMetersPerUnit;

        c.longitude = centerLongitude + offsetMetersX / metersPerDegreeLon;
        c.latitude = centerLatitude + offsetMetersY / metersPerDegreeLat;
        c.height = uniformHeight;
    }

    // ================================================================
    //  Step 2: Clear Existing Data
    // ================================================================

    private void ClearExistingData()
    {
        EmitStatus("Clearing previous simulation data...");

        _currentDataset = null;

        // Refresh CommandCenter first
        if (commandCenter != null)
        {
            try { commandCenter.Refresh(); }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] Refresh before clear: " + e.Message); }
        }

        // 1. Unsubscribe all drone events
        try { UnsubscribeAllDroneEvents(); }
        catch (System.Exception e)
        { DLog.Warn("General", "[SolomonImporter] UnsubscribeAll: " + e.Message); }

        // 2. Clear route dispatcher
        if (RouteDispatcher.Instance != null)
        {
            try { RouteDispatcher.Instance.ClearAll(); }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] RouteDispatcher.ClearAll: " + e.Message); }
        }

        // 3. Clear VehicleRouter last solution
        if (VehicleRouter.Instance != null)
        {
            try
            {
                var field = typeof(VehicleRouter).GetField("_lastSolution",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (field != null)
                    field.SetValue(VehicleRouter.Instance, null);
            }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] VehicleRouter clear: " + e.Message); }
        }

        // 4. Clear all orders
        if (orderManager != null)
        {
            try { orderManager.ClearAllOrders(); }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] ClearAllOrders: " + e.Message); }
        }

        // 5. Clear MissionTracker
        if (MissionTracker.Instance != null)
        {
            try { MissionTracker.Instance.StartMission("", ""); }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] MissionTracker: " + e.Message); }
        }

        // 6. Remove ALL drones
        if (droneFactory != null)
        {
            try
            {
                int removed = droneFactory.RemoveAllDronesImmediate();
                droneFactory.ResetCounter();
                Debug.Log("[SolomonImporter] Removed " + removed + " drones");
            }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] RemoveAllDrones: " + e.Message); }
        }

        // 7. Refresh CommandCenter AGAIN
        if (commandCenter != null)
        {
            try
            {
                commandCenter.Refresh();
                Debug.Log("[SolomonImporter] Post-clear registry: " +
                          commandCenter.DroneCount + " drones");
            }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] Post-clear refresh: " + e.Message); }
        }

        // 8. Remove Solomon-created location points
        if (locationManager != null)
        {
            try
            {
                int pointsRemoved = locationManager.RemovePointsWhere(p =>
                {
                    if (p == null) return false;
                    string n = p.GetDisplayName();
                    if (string.IsNullOrEmpty(n)) return false;
                    if (n.StartsWith("Depot_")) return true;
                    if (n.StartsWith("C") && n.Length >= 4)
                    {
                        string numPart = n.Substring(1);
                        return int.TryParse(numPart, out _);
                    }
                    return false;
                });
                Debug.Log("[SolomonImporter] Removed " + pointsRemoved + " location points");

                // ★ FIX #2: Force refresh to purge destroyed-but-not-yet-removed references
                locationManager.RefreshPoints();
            }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] RemovePoints: " + e.Message); }
        }

        // 9. Reset SimClock
        if (SimClock.Instance != null)
        {
            try { SimClock.Instance.StopSimulation(); }
            catch (System.Exception e)
            { DLog.Warn("General", "[SolomonImporter] SimClock: " + e.Message); }
        }

        // 10. Reset speed
        try
        {
            var speedController = FindObjectOfType<SimSpeedController>();
            if (speedController != null) speedController.ResetSpeed();
        }
        catch (System.Exception e)
        { DLog.Warn("General", "[SolomonImporter] SpeedController: " + e.Message); }

        // 11. Clean up SwitchView
        try
        {
            var switchView = FindObjectOfType<SwitchView>();
            if (switchView != null)
            {
                switchView.CleanNullTargets();
                if (switchView.droneTargets == null || switchView.droneTargets.Length == 0)
                {
                    if (switchView.sideView)
                    { switchView.sideView.Follow = null; switchView.sideView.LookAt = null; }
                    if (switchView.rearChase)
                    { switchView.rearChase.Follow = null; switchView.rearChase.LookAt = null; }
                }
            }
        }
        catch (System.Exception e)
        { DLog.Warn("General", "[SolomonImporter] SwitchView cleanup: " + e.Message); }

        Debug.Log("[SolomonImporter] ClearExistingData COMPLETE");

        // ★ FIX #1: DO NOT call SetSolomonImportActive(false) here!
        // During import: the flag was set true by ImportDataset and must STAY true.
        // For explicit user "Clear" actions, use ClearAndResetImport() instead.
    }

    /// <summary>
    /// ★ FIX #3: Call this from UI "Clear" button — NOT from inside ImportDataset.
    /// Clears all data AND unlocks auto-assign for manual orders.
    /// </summary>
    public void ClearAndResetImport()
    {
        ClearExistingData();
        _currentDataset = null;

        if (OrderManager.Instance != null)
            OrderManager.Instance.SetSolomonImportActive(false);

        Debug.Log("[SolomonImporter] ClearAndResetImport: import flag released");
    }

    private void UnsubscribeAllDroneEvents()
    {
#if UNITY_2023_1_OR_NEWER
        var allInfos = FindObjectsByType<DroneInfo>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var allInfos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in allInfos)
        {
            if (info == null) continue;
            try
            {
                if (OrderManager.Instance != null)
                    OrderManager.Instance.UnsubscribeDrone(info);
            }
            catch { }
        }
    }

    private IEnumerator RefreshCommandCenterNextFrame()
    {
        yield return null;

        if (commandCenter != null)
            commandCenter.Refresh();

        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            switchView.CleanNullTargets();
            if (switchView.droneTargets != null && switchView.droneTargets.Length > 0)
                switchView.SelectDroneByIndex(0);
            switchView.EnsureCameraHasTarget();
        }

        Debug.Log("[SolomonImporter] Post-spawn refresh complete. Drones: " +
                  (commandCenter != null ? commandCenter.DroneCount : 0));
    }

    // ================================================================
    //  Step 3: Create Depot
    // ================================================================

    private void CreateDepot(SolomonDataset dataset)
    {
        if (dataset.depot == null)
        {
            DLog.Error("General", "[SolomonImporter] CreateDepot: dataset.depot is NULL!");
            return;
        }
        if (locationManager == null)
        {
            DLog.Error("General", "[SolomonImporter] CreateDepot: locationManager is NULL!");
            return;
        }

        string depotName = "Depot_" + dataset.name;

        try
        {
            var existing = locationManager.GetPointByName(depotName);
            if (existing != null)
            {
                DLog.Warn("General", "[SolomonImporter] Depot '" + depotName +
                                 "' already exists after clear! Removing...");
                locationManager.RemovePointsWhere(p =>
                    p != null && p.GetDisplayName() == depotName);
                locationManager.RefreshPoints();
            }
        }
        catch (System.Exception e)
        {
            DLog.Warn("General", "[SolomonImporter] Error checking existing depot: " + e.Message);
        }

        double3 depotLLH = dataset.depot.GetLLH();
        Debug.Log("[SolomonImporter] Creating depot '" + depotName + "' at LLH(" +
                  depotLLH.x.ToString("F6") + ", " + depotLLH.y.ToString("F6") + ", " +
                  depotLLH.z.ToString("F1") + ")");

        locationManager.CreatePointFromMapPick(
            depotName,
            LocationPoint.PointType.SpawnPoint,
            depotLLH
        );
    }

    // ================================================================
    //  Step 4: Create Customer Orders
    // ================================================================

    private int CreateCustomerOrders(SolomonDataset dataset)
    {
        if (orderManager == null)
        {
            DLog.Error("General", "[SolomonImporter] CreateCustomerOrders: orderManager is NULL!");
            return 0;
        }
        if (locationManager == null)
        {
            DLog.Error("General", "[SolomonImporter] CreateCustomerOrders: locationManager is NULL!");
            return 0;
        }

        int created = 0;

        double3 depotLLH = dataset.depot != null
            ? dataset.depot.GetLLH()
            : new double3(centerLongitude, centerLatitude, flightHeight);

        foreach (var customer in dataset.customers)
        {
            try
            {
                string pointName = "C" + customer.id.ToString("D3");
                locationManager.CreatePointFromMapPick(
                    pointName,
                    LocationPoint.PointType.DeliveryPoint,
                    customer.GetLLH()
                );

                string orderId = "S-" + dataset.name + "-" + customer.id.ToString("D3");
                string description = "C" + customer.id + " [D=" + customer.demand + "]";

                var order = new DeliveryOrder(
                    orderId,
                    depotLLH,
                    customer.GetLLH(),
                    customer.demand,
                    customer.readyTime,
                    customer.dueDate,
                    customer.serviceTime,
                    description
                );

                order.customerNumber = customer.id;
                order.pickupPointName = "Depot_" + dataset.name;
                order.deliveryPointName = pointName;

                orderManager.AddExternalOrder(order);
                created++;
            }
            catch (System.Exception e)
            {
                DLog.Error("General", "[SolomonImporter] Error creating order for customer " +
                               customer.id + ": " + e);
            }
        }

        Debug.Log("[SolomonImporter] Created " + created + "/" +
                  dataset.CustomerCount + " orders");
        return created;
    }

    // ================================================================
    //  Step 5: Auto-spawn Drones
    // ================================================================

    private int SpawnDrones(SolomonDataset dataset, string depotName)
    {
        if (droneFactory == null)
        {
            DLog.Error("General", "[SolomonImporter] SpawnDrones: droneFactory is NULL!");
            return 0;
        }

        int count = maxAutoSpawnDrones > 0
            ? Mathf.Min(maxAutoSpawnDrones, dataset.vehicleCount)
            : Mathf.Min(dataset.vehicleCount, 10);

        // ★ FIX #2b: Re-lookup depot FRESH, skip destroyed references
        if (locationManager != null)
        {
            locationManager.RefreshPoints(); // purge zombies

            var depot = locationManager.GetPointByName(depotName);
            if (depot == null || !depot)      // Unity null check: catches destroyed objects
            {
                DLog.Warn("General", "[SolomonImporter] Depot '" + depotName +
                          "' not found, trying fallback...");
                var spawns = locationManager.GetSpawnPoints();
                // Filter out any destroyed spawn points
                spawns?.RemoveAll(p => p == null || !p);
                if (spawns != null && spawns.Count > 0)
                {
                    depotName = spawns[0].GetDisplayName();
                    DLog.Warn("General", "[SolomonImporter] Using fallback spawn: " + depotName);
                }
                else
                {
                    DLog.Error("General", "[SolomonImporter] No valid spawn point! Cannot spawn drones.");
                    EmitStatus("[!] No spawn point found. Drones not created.");
                    return 0;
                }
            }
        }

        Color[] colors = new Color[]
        {
            Color.cyan, Color.green, Color.yellow,
            new Color(1f, 0.5f, 0f), Color.magenta,
            Color.red, Color.blue, Color.white,
            new Color(0.5f, 1f, 0.5f), new Color(1f, 0.5f, 1f)
        };

        if (commandCenter != null)
            commandCenter.Refresh();

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            string droneName = "V" + (i + 1).ToString("D2");
            Color color = colors[i % colors.Length];

            try
            {
                var drone = droneFactory.SpawnDrone(droneName, depotName, color);
                if (drone != null)
                {
                    var spec = drone.GetComponent<DroneSpec>();
                    if (spec != null)
                    {
                        spec.maxCapacity = dataset.vehicleCapacity;
                        spec.currentLoad = 0;
                    }
                    spawned++;
                }
                else
                {
                    DLog.Error("General", "[SolomonImporter] SpawnDrone returned null for '" +
                                   droneName + "' at '" + depotName + "'");
                }
            }
            catch (System.Exception e)
            {
                DLog.Error("General", "[SolomonImporter] Exception spawning drone '" +
                               droneName + "': " + e);
            }
        }

        Debug.Log("[SolomonImporter] Spawned " + spawned + "/" + count + " drones at " + depotName);

        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            switchView.CleanNullTargets();
            switchView.EnsureCameraHasTarget();
        }

        return spawned;
    }

    private int SpawnDrones(SolomonDataset dataset)
    {
        return SpawnDrones(dataset, "Depot_" + dataset.name);
    }

    // ================================================================
    //  Public Queries
    // ================================================================

    public string GetImportSummary()
    {
        if (_currentDataset == null)
            return "No dataset imported";

        return "Dataset: " + _currentDataset.name + NL +
               "Customers: " + _currentDataset.CustomerCount + NL +
               "Vehicles: " + _currentDataset.vehicleCount +
               " (cap=" + _currentDataset.vehicleCapacity + ")" + NL +
               "Total Demand: " + _currentDataset.TotalDemand + NL +
               "Time Horizon: " + _currentDataset.TimeHorizon.ToString("F0") + NL +
               "Mapped to: (" + centerLatitude.ToString("F4") + ", " +
               centerLongitude.ToString("F4") + ")";
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void EmitStatus(string msg)
    {
        Debug.Log("[SolomonImporter] " + msg);
        OnImportStatus?.Invoke(msg);
    }
}