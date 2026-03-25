// Assets/Scripts/Data/SolomonImporter.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

/// <summary>
/// Imports a Solomon dataset into the simulation:
/// - Maps coordinates to real-world LLH
/// - Creates LocationPoints (Depot as SpawnPoint, Customers as DeliveryPoints)
/// - Creates DeliveryOrders with time windows and demand
/// - Optionally auto-spawns drones
/// </summary>
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
    [Tooltip("Center longitude of target area")]
    public double centerLongitude = 121.5500;
    [Tooltip("Center latitude of target area")]
    public double centerLatitude = 29.8700;
    [Tooltip("Meters per Solomon coordinate unit")]
    public double scaleMetersPerUnit = 50.0;
    [Tooltip("Desired flight height above ground level (meters). China regulation: <120m AGL")]
    public double flightHeight = 20.0;  // ← 改为 20m AGL

    [Header("Import Settings")]
    [Tooltip("Clear existing points and orders before import")]
    public bool clearBeforeImport = true;
    [Tooltip("Auto-spawn drones at depot")]
    public bool autoSpawnDrones = true;
    [Tooltip("Max drones to auto-spawn (0 = use dataset vehicle count)")]
    public int maxAutoSpawnDrones = 0;

    [Header("State")]
    [SerializeField] private string _lastImportName = "";
    [SerializeField] private int _lastCustomerCount = 0;

    // Last imported dataset (available for other systems)
    private SolomonDataset _currentDataset;
    public SolomonDataset CurrentDataset => _currentDataset;

    // ====== Events ======
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

    /// <summary>
    /// Import a Solomon dataset from file path.
    /// This is the main entry point.
    /// </summary>
    public bool ImportFromFile(string filePath)
    {
        EmitStatus($"Parsing file: {System.IO.Path.GetFileName(filePath)}...");

        var dataset = SolomonParser.ParseFile(filePath);
        if (dataset == null || dataset.CustomerCount == 0)
        {
            EmitStatus("[!] Failed to parse file or no customers found");
            return false;
        }

        return ImportDataset(dataset);
    }

    /// <summary>
    /// Import a pre-parsed Solomon dataset.
    /// </summary>
    public bool ImportDataset(SolomonDataset dataset)
    {
        if (dataset == null) return false;

        _currentDataset = dataset;
        _lastImportName = dataset.name;
        _lastCustomerCount = dataset.CustomerCount;

        EmitStatus($"Importing '{dataset.name}': {dataset.CustomerCount} customers, " +
                   $"{dataset.vehicleCount} vehicles (cap={dataset.vehicleCapacity})");

        // Step 1: Apply coordinate mapping
        ApplyCoordinateMapping(dataset);

        // Step 2: Clear existing data if needed
        if (clearBeforeImport)
        {
            ClearExistingData();
        }

        // Step 3: Create depot as SpawnPoint
        CreateDepot(dataset);

        // Step 4: Create all customers as delivery points + orders
        int ordersCreated = CreateCustomerOrders(dataset);

        // Step 5: Auto-spawn drones
        int dronesSpawned = 0;
        if (autoSpawnDrones)
        {
            dronesSpawned = SpawnDrones(dataset);
        }

        // Step 6: Start simulation clock
        if (SimClock.Instance != null)
        {
            SimClock.Instance.StartSimulation(0f);
        }

        string summary = $"[OK] Import complete!\\n" +
                         $"  Dataset: {dataset.name}\\n" +
                         $"  Customers: {dataset.CustomerCount}\\n" +
                         $"  Orders created: {ordersCreated}\\n" +
                         $"  Drones spawned: {dronesSpawned}\\n" +
                         $"  Total demand: {dataset.TotalDemand}\\n" +
                         $"  Time horizon: {dataset.TimeHorizon:F0}\\n" +
                         $"  Area: {centerLatitude:F4}, {centerLongitude:F4}";

        EmitStatus(summary);
        Debug.Log($"[SolomonImporter] {summary}");

        OnDatasetImported?.Invoke(dataset);
        return true;
    }

// Assets/Scripts/Data/SolomonImporter.cs
// 替换整个 ApplyCoordinateMapping() 和 MapCustomerToLLH() 方法

// ================================================================
//  Step 1: Coordinate Mapping (FIXED for Earth curvature)
// ================================================================

    private void ApplyCoordinateMapping(SolomonDataset dataset)
    {
        // Use dataset mapping if provided, otherwise use our defaults
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

        // Find coordinate bounds to center the mapping
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

        // Map each point: Solomon (x,y) → LLH
        double metersPerDegreeLon = 111320.0 * System.Math.Cos(centerLatitude * System.Math.PI / 180.0);
        double metersPerDegreeLat = 110540.0;

        // ====== KEY FIX: Compute depot ground-level WGS84 height ======
        // All flight points use the SAME WGS84 height = depot's ground elevation + desired AGL
        // This keeps all drones at the same "visual altitude" regardless of Earth curvature.
        //
        // For Ningbo city center, ground elevation ≈ 4-8m above WGS84 ellipsoid.
        // We use a fixed AGL (Above Ground Level) offset.

        double desiredAGL = flightHeight; // User-configured, default 20m

        // First, map depot to get its lon/lat
        double depotLon = centerLongitude;
        double depotLat = centerLatitude;
        if (dataset.depot != null)
        {
            double offsetX = (dataset.depot.x - centerX) * scaleMetersPerUnit;
            double offsetY = (dataset.depot.y - centerY) * scaleMetersPerUnit;
            depotLon = centerLongitude + offsetX / metersPerDegreeLon;
            depotLat = centerLatitude + offsetY / metersPerDegreeLat;
        }

        // Estimate ground elevation at depot (simple model for flat cities)
        // Ningbo average ground elevation: ~5m above WGS84
        // For more accuracy, you could query Cesium terrain height at runtime.
        double estimatedGroundElevation = 5.0; // meters above WGS84 ellipsoid

        // The WGS84 height that gives us desiredAGL above ground at the depot
        double uniformWgs84Height = estimatedGroundElevation + desiredAGL;

        Debug.Log($"[SolomonImporter] Height strategy: ground≈{estimatedGroundElevation:F1}m + " +
                $"AGL={desiredAGL:F1}m = WGS84 height {uniformWgs84Height:F1}m (uniform for all points)");

        if (dataset.depot != null)
            MapCustomerToLLH(dataset.depot, centerX, centerY,
                            metersPerDegreeLon, metersPerDegreeLat, uniformWgs84Height);

        foreach (var c in dataset.customers)
            MapCustomerToLLH(c, centerX, centerY,
                            metersPerDegreeLon, metersPerDegreeLat, uniformWgs84Height);

        // Log spatial extent
        double extentMetersX = (maxX - minX) * scaleMetersPerUnit;
        double extentMetersY = (maxY - minY) * scaleMetersPerUnit;
        Debug.Log($"[SolomonImporter] Mapped {dataset.CustomerCount + 1} points to " +
                $"({centerLatitude:F4}, {centerLongitude:F4}), " +
                $"scale={scaleMetersPerUnit}m/unit, " +
                $"extent={extentMetersX:F0}m × {extentMetersY:F0}m, " +
                $"flight AGL={desiredAGL:F0}m");
    }

    private void MapCustomerToLLH(SolomonCustomer c, float centerX, float centerY,
        double metersPerDegreeLon, double metersPerDegreeLat, double uniformHeight)
    {
        double offsetMetersX = (c.x - centerX) * scaleMetersPerUnit;
        double offsetMetersY = (c.y - centerY) * scaleMetersPerUnit;

        c.longitude = centerLongitude + offsetMetersX / metersPerDegreeLon;
        c.latitude = centerLatitude + offsetMetersY / metersPerDegreeLat;

        // ====== KEY FIX: Use uniform WGS84 height for ALL points ======
        // This ensures all drones fly at the same visual altitude
        // regardless of Earth curvature across the mapped area.
        c.height = uniformHeight;
    }



    // ================================================================
    //  Step 2: Clear Existing Data (FIXED)
    // ================================================================

    private void ClearExistingData()
    {
        EmitStatus("Clearing previous simulation data...");

        // 1. Clear route dispatcher state FIRST (stops all active flights)
        if (RouteDispatcher.Instance != null)
            RouteDispatcher.Instance.ClearAll();

        // 2. Clear VehicleRouter last solution
        if (VehicleRouter.Instance != null)
        {
            var field = typeof(VehicleRouter).GetField("_lastSolution",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (field != null)
                field.SetValue(VehicleRouter.Instance, null);
        }

        // 3. Clear all orders (also stops drones with active orders)
        if (orderManager != null)
            orderManager.ClearAllOrders();

        // 4. Clear MissionTracker
        if (MissionTracker.Instance != null)
            MissionTracker.Instance.StartMission("", "");

        // 5. Remove ALL drones using the new immediate method
        //    This handles both factory-spawned AND pre-placed drones.
        //    No need for a separate RemovePreplacedDrones() anymore.
        if (droneFactory != null)
        {
            int removed = droneFactory.RemoveAllDronesImmediate();
            droneFactory.ResetCounter();
            EmitStatus($"Removed {removed} drones");
        }

        // 6. Remove Solomon-created location points
        //    (Depot_xxx and Cxxx points from previous import)
        if (locationManager != null)
        {
            int pointsRemoved = locationManager.RemovePointsWhere(p =>
            {
                string n = p.GetDisplayName();
                return n.StartsWith("Depot_") ||
                    (n.StartsWith("C") && n.Length == 4 &&
                    int.TryParse(n.Substring(1), out _));
            });
            EmitStatus($"Removed {pointsRemoved} location points");
        }

        // 7. Reset SimClock
        if (SimClock.Instance != null)
            SimClock.Instance.StopSimulation();

        // 8. Reset speed to 1x
        var speedController = FindObjectOfType<SimSpeedController>();
        if (speedController != null)
            speedController.ResetSpeed();

        EmitStatus("Clear complete — ready for new import");
    }

    private System.Collections.IEnumerator RefreshAfterClear()
    {
        yield return null; // Wait for Destroy to take effect

        if (commandCenter != null)
            commandCenter.Refresh();

        // Ensure camera has target (might be empty temporarily)
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            switchView.CleanNullTargets();
            // Don't call EnsureCameraHasTarget here — drones haven't been spawned yet
            // Cameras will detach temporarily, which is fine
            if (switchView.droneTargets == null || switchView.droneTargets.Length == 0)
            {
                if (switchView.sideView) { switchView.sideView.Follow = null; switchView.sideView.LookAt = null; }
                if (switchView.rearChase) { switchView.rearChase.Follow = null; switchView.rearChase.LookAt = null; }
            }
        }
    }

    private System.Collections.IEnumerator RefreshCommandCenterNextFrame()
    {
        yield return null;
        if (commandCenter != null)
            commandCenter.Refresh();

        // Also refresh SwitchView
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null && switchView.droneTargets != null && switchView.droneTargets.Length > 0)
            switchView.SelectDroneByIndex(0);
    }

    // ================================================================
    //  Step 3: Create Depot
    // ================================================================

    private void CreateDepot(SolomonDataset dataset)
    {
        if (dataset.depot == null || locationManager == null) return;

        string depotName = $"Depot_{dataset.name}";
        locationManager.CreatePointFromMapPick(
            depotName,
            LocationPoint.PointType.SpawnPoint,
            dataset.depot.GetLLH()
        );

        Debug.Log($"[SolomonImporter] Created depot '{depotName}' at " +
                  $"({dataset.depot.latitude:F4}, {dataset.depot.longitude:F4})");
    }

    // ================================================================
    //  Step 4: Create Customer Orders
    // ================================================================

    private int CreateCustomerOrders(SolomonDataset dataset)
    {
        if (orderManager == null || locationManager == null) return 0;

        int created = 0;

        // The depot is both pickup (warehouse) and the return point
        double3 depotLLH = dataset.depot != null
            ? dataset.depot.GetLLH()
            : new double3(centerLongitude, centerLatitude, flightHeight);

        foreach (var customer in dataset.customers)
        {
            // Create delivery point for this customer
            string pointName = $"C{customer.id:D3}";
            locationManager.CreatePointFromMapPick(
                pointName,
                LocationPoint.PointType.DeliveryPoint,
                customer.GetLLH()
            );

            // Create order: Depot → Customer
            string orderId = $"S-{dataset.name}-{customer.id:D3}";
            string description = $"C{customer.id} [D={customer.demand}]";

            var order = new DeliveryOrder(
                orderId,
                depotLLH,           // Pickup from depot
                customer.GetLLH(),  // Deliver to customer
                customer.demand,
                customer.readyTime,
                customer.dueDate,
                customer.serviceTime,
                description
            );

            order.customerNumber = customer.id;
            order.pickupPointName = $"Depot_{dataset.name}";
            order.deliveryPointName = pointName;

            orderManager.AddExternalOrder(order);
            created++;
        }

        Debug.Log($"[SolomonImporter] Created {created} orders from {dataset.CustomerCount} customers");
        return created;
    }

    // ================================================================
    //  Step 5: Auto-spawn Drones
    // ================================================================

    private int SpawnDrones(SolomonDataset dataset)
    {
        if (droneFactory == null) return 0;

        int count = maxAutoSpawnDrones > 0
            ? Mathf.Min(maxAutoSpawnDrones, dataset.vehicleCount)
            : Mathf.Min(dataset.vehicleCount, 10); // Cap at 10 for performance

        string depotName = $"Depot_{dataset.name}";
        int spawned = 0;

        Color[] colors = new Color[]
        {
            Color.cyan, Color.green, Color.yellow,
            new Color(1f, 0.5f, 0f), Color.magenta,
            Color.red, Color.blue, Color.white,
            new Color(0.5f, 1f, 0.5f), new Color(1f, 0.5f, 1f)
        };

        for (int i = 0; i < count; i++)
        {
            string droneName = $"V{(i + 1):D2}";
            Color color = colors[i % colors.Length];

            var drone = droneFactory.SpawnDrone(droneName, depotName, color);
            if (drone != null)
            {
                // Set capacity from dataset
                var spec = drone.GetComponent<DroneSpec>();
                if (spec != null)
                {
                    spec.maxCapacity = dataset.vehicleCapacity;
                    spec.currentLoad = 0;
                }
                spawned++;
            }
        }

        Debug.Log($"[SolomonImporter] Spawned {spawned}/{count} drones at {depotName}");

        // ★ 新增：确保摄像机跟踪新生成的无人机 ★
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            switchView.CleanNullTargets();
            switchView.EnsureCameraHasTarget();
        }

        return spawned;
    }

    // ================================================================
    //  Public Queries
    // ================================================================

    /// <summary>Get import summary text</summary>
    public string GetImportSummary()
    {
        if (_currentDataset == null)
            return "No dataset imported";

        return $"Dataset: {_currentDataset.name}\\n" +
               $"Customers: {_currentDataset.CustomerCount}\\n" +
               $"Vehicles: {_currentDataset.vehicleCount} (cap={_currentDataset.vehicleCapacity})\\n" +
               $"Total Demand: {_currentDataset.TotalDemand}\\n" +
               $"Time Horizon: {_currentDataset.TimeHorizon:F0}\\n" +
               $"Mapped to: ({centerLatitude:F4}, {centerLongitude:F4})";
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void EmitStatus(string msg)
    {
        Debug.Log($"[SolomonImporter] {msg}");
        OnImportStatus?.Invoke(msg);
    }
}