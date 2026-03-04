// Assets/Scripts/OrderManager.cs
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class OrderManager : MonoBehaviour
{
    public static OrderManager Instance;

    [Header("References")]
    public DroneCommandCenter commandCenter;
    public RuntimeWaypointsBuilder routeBuilder;
    public LocationManager locationManager;

    [Header("Settings")]
    public float assignCheckInterval = 1.0f;
    public float maxAssignDistanceMeters = 5000f;

    [Header("Debug")]
    public bool logAssignments = true;

    // ====== Order Storage ======
    private readonly List<DeliveryOrder> _allOrders = new();
    private readonly Dictionary<string, DeliveryOrder> _activeByDrone = new();
    private int _orderCounter = 0;
    private float _lastAssignCheck = 0f;

    // ====== Events ======
    public System.Action<DeliveryOrder> OnOrderCreated;
    public System.Action<DeliveryOrder> OnOrderAssigned;
    public System.Action<DeliveryOrder> OnOrderPickedUp;
    public System.Action<DeliveryOrder> OnOrderCompleted;
    public System.Action<string> OnStatusChanged;

    // ====== Properties ======
    public int PendingCount => _allOrders.Count(o => o.status == DeliveryOrder.OrderStatus.Pending);
    public int ActiveCount => _activeByDrone.Count;
    public int CompletedCount => _allOrders.Count(o => o.status == DeliveryOrder.OrderStatus.Completed);
    public int TotalCount => _allOrders.Count;
    public List<DeliveryOrder> AllOrders => _allOrders;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!routeBuilder) routeBuilder = FindObjectOfType<RuntimeWaypointsBuilder>();
        if (!locationManager) locationManager = FindObjectOfType<LocationManager>();
    }

    void Start()
    {
        SubscribeToDroneEvents();
    }

    void Update()
    {
        if (Time.time - _lastAssignCheck >= assignCheckInterval)
        {
            _lastAssignCheck = Time.time;
            TryAssignPendingOrders();
        }
    }

    // ================================================================
    //  Order Creation: From Named Locations
    // ================================================================

    /// <summary>
    /// Create order by pickup and delivery location names
    /// </summary>
    public DeliveryOrder CreateOrderByNames(string pickupName, string deliveryName, string description = "")
    {
        if (locationManager == null)
        {
            Debug.LogError("[OrderManager] LocationManager not found");
            return null;
        }

        var pickup = locationManager.GetPointByName(pickupName);
        if (pickup == null)
        {
            Debug.LogWarning($"[OrderManager] Pickup point '{pickupName}' not found");
            EmitStatus($"Error: Pickup '{pickupName}' not found. Available: {string.Join(", ", locationManager.GetPointNames(LocationPoint.PointType.PickupPoint))}");
            return null;
        }

        var delivery = locationManager.GetPointByName(deliveryName);
        if (delivery == null)
        {
            Debug.LogWarning($"[OrderManager] Delivery point '{deliveryName}' not found");
            EmitStatus($"Error: Delivery '{deliveryName}' not found. Available: {string.Join(", ", locationManager.GetPointNames(LocationPoint.PointType.DeliveryPoint))}");
            return null;
        }

        if (string.IsNullOrEmpty(description))
            description = $"{pickupName} -> {deliveryName}";

        return CreateOrder(pickup.GetLLH(), delivery.GetLLH(), description, pickupName, deliveryName);
    }

    /// <summary>
    /// Create order from raw LLH coordinates
    /// </summary>
    public DeliveryOrder CreateOrder(double3 pickupLLH, double3 deliveryLLH,
        string description = "", string pickupName = "", string deliveryName = "")
    {
        _orderCounter++;
        string id = $"ORD-{_orderCounter:D3}";

        var order = new DeliveryOrder(id, pickupLLH, deliveryLLH, description);
        order.pickupPointName = pickupName;
        order.deliveryPointName = deliveryName;
        _allOrders.Add(order);

        if (logAssignments)
            Debug.Log($"[OrderManager] Created {id}: {description}");

        EmitStatus($"Order {id} created: {description} ({PendingCount} pending)");
        OnOrderCreated?.Invoke(order);

        TryAssignPendingOrders();
        return order;
    }

    /// <summary>
    /// Create a random test order from available locations
    /// </summary>
    public DeliveryOrder CreateTestOrder()
    {
        if (locationManager == null)
        {
            Debug.LogWarning("[OrderManager] LocationManager not found, cannot create test order");
            return null;
        }

        var pickups = locationManager.GetPickupPoints();
        var deliveries = locationManager.GetDeliveryPoints();

        if (pickups.Count == 0 || deliveries.Count == 0)
        {
            EmitStatus("Error: Need at least 1 pickup and 1 delivery point");
            return null;
        }

        var pickup = pickups[UnityEngine.Random.Range(0, pickups.Count)];
        var delivery = deliveries[UnityEngine.Random.Range(0, deliveries.Count)];

        return CreateOrderByNames(pickup.GetDisplayName(), delivery.GetDisplayName());
    }

    // ================================================================
    //  Batch Order Import from JSON
    // ================================================================

    [System.Serializable]
    private class OrderBatch
    {
        public List<OrderEntry> orders = new();
    }

    [System.Serializable]
    private class OrderEntry
    {
        public string pickup;
        public string delivery;
        public string description;
    }

    /// <summary>
    /// Import orders from a JSON file
    /// </summary>
    public int ImportOrdersFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            EmitStatus($"Error: File not found - {filePath}");
            return 0;
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var batch = JsonUtility.FromJson<OrderBatch>(json);

            if (batch == null || batch.orders == null || batch.orders.Count == 0)
            {
                EmitStatus("Error: No orders found in file");
                return 0;
            }

            int created = 0;
            foreach (var entry in batch.orders)
            {
                var order = CreateOrderByNames(entry.pickup, entry.delivery, entry.description);
                if (order != null) created++;
            }

            EmitStatus($"Imported {created}/{batch.orders.Count} orders from file");
            return created;
        }
        catch (System.Exception e)
        {
            EmitStatus($"Error importing orders: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Save a sample order file for reference
    /// </summary>
    public string SaveSampleOrderFile()
    {
        var pickups = locationManager != null
            ? locationManager.GetPointNames(LocationPoint.PointType.PickupPoint)
            : new List<string> { "Restaurant A", "Restaurant B" };
        var deliveries = locationManager != null
            ? locationManager.GetPointNames(LocationPoint.PointType.DeliveryPoint)
            : new List<string> { "Customer A", "Customer B" };

        var sample = new OrderBatch();
        sample.orders.Add(new OrderEntry
        {
            pickup = pickups.Count > 0 ? pickups[0] : "Restaurant A",
            delivery = deliveries.Count > 0 ? deliveries[0] : "Customer A",
            description = "Lunch delivery"
        });
        sample.orders.Add(new OrderEntry
        {
            pickup = pickups.Count > 1 ? pickups[1] : pickups[0],
            delivery = deliveries.Count > 1 ? deliveries[1] : deliveries[0],
            description = "Dinner delivery"
        });

        string json = JsonUtility.ToJson(sample, true);
        string path = Path.Combine(Application.persistentDataPath, "sample_orders.json");
        File.WriteAllText(path, json);

        Debug.Log($"[OrderManager] Sample order file saved: {path}");
        EmitStatus($"Sample order file saved to: {path}");
        return path;
    }

    // ================================================================
    //  Assignment Logic
    // ================================================================

    private void TryAssignPendingOrders()
    {
        if (commandCenter == null || routeBuilder == null) return;

        var pendingOrders = _allOrders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (pendingOrders.Count == 0) return;

        foreach (var order in pendingOrders)
        {
            string bestDrone = FindBestDroneForOrder(order);
            if (bestDrone == null) continue;
            AssignOrderToDrone(order, bestDrone);
        }
    }

    private string FindBestDroneForOrder(DeliveryOrder order)
    {
        var snapshots = commandCenter.GetFleetSnapshot();
        string bestDrone = null;
        double bestDistance = double.MaxValue;

        foreach (var snap in snapshots)
        {
            if (!snap.isIdle) continue;
            if (_activeByDrone.ContainsKey(snap.name)) continue;

            double dist = GeoDistance(snap.positionLLH, order.pickupLLH);
            if (dist < bestDistance && dist < maxAssignDistanceMeters)
            {
                bestDistance = dist;
                bestDrone = snap.name;
            }
        }

        return bestDrone;
    }

    private void AssignOrderToDrone(DeliveryOrder order, string droneName)
    {
        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var anchor = nav.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 startLLH = anchor.longitudeLatitudeHeight;

        if (!BuildAndWriteRoute(droneName, startLLH, order.pickupLLH, out _))
            return;

        order.status = DeliveryOrder.OrderStatus.PickingUp;
        order.assignedDrone = droneName;
        _activeByDrone[droneName] = order;

        if (commandCenter.TryGetInfo(droneName, out var info))
            info.AssignRoute($"Pickup {order.orderId}");

        if (logAssignments)
            Debug.Log($"[OrderManager] Assigned {order.orderId} to {droneName} (picking up from {order.pickupPointName})");

        EmitStatus($"{order.orderId} -> {droneName}: picking up from {order.pickupPointName}");
        OnOrderAssigned?.Invoke(order);
    }

    // ================================================================
    //  Route Completion Handler
    // ================================================================

    private void SubscribeToDroneEvents()
    {
#if UNITY_2023_1_OR_NEWER
        var infos = FindObjectsByType<DroneInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var infos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in infos)
        {
            info.OnRouteCompleted -= OnDroneRouteCompleted;
            info.OnRouteCompleted += OnDroneRouteCompleted;
        }
    }

    /// <summary>
    /// Call this when a new drone is created at runtime to subscribe to its events
    /// </summary>
    public void SubscribeDrone(DroneInfo info)
    {
        info.OnRouteCompleted -= OnDroneRouteCompleted;
        info.OnRouteCompleted += OnDroneRouteCompleted;
    }

    private void OnDroneRouteCompleted(DroneInfo droneInfo)
    {
        string droneName = droneInfo.GetName();
        if (!_activeByDrone.TryGetValue(droneName, out var order)) return;

        if (order.status == DeliveryOrder.OrderStatus.PickingUp)
        {
            StartDeliveryLeg(order, droneName);
        }
        else if (order.status == DeliveryOrder.OrderStatus.Delivering)
        {
            CompleteOrder(order, droneName);
        }
    }

    private void StartDeliveryLeg(DeliveryOrder order, string droneName)
    {
        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var anchor = nav.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 startLLH = anchor.longitudeLatitudeHeight;

        if (!BuildAndWriteRoute(droneName, startLLH, order.deliveryLLH, out _))
        {
            order.status = DeliveryOrder.OrderStatus.Failed;
            _activeByDrone.Remove(droneName);
            return;
        }

        order.status = DeliveryOrder.OrderStatus.Delivering;

        if (commandCenter.TryGetInfo(droneName, out var info))
            info.AssignRoute($"Deliver {order.orderId}");

        if (logAssignments)
            Debug.Log($"[OrderManager] {droneName} picked up {order.orderId}, delivering to {order.deliveryPointName}");

        EmitStatus($"{droneName}: delivering {order.orderId} to {order.deliveryPointName}");
        OnOrderPickedUp?.Invoke(order);
    }

    private void CompleteOrder(DeliveryOrder order, string droneName)
    {
        order.status = DeliveryOrder.OrderStatus.Completed;
        order.completedTime = Time.time;
        _activeByDrone.Remove(droneName);

        float duration = order.completedTime - order.createdTime;

        if (logAssignments)
            Debug.Log($"[OrderManager] {order.orderId} completed by {droneName} ({duration:F1}s)");

        EmitStatus($"{order.orderId} delivered by {droneName}! ({duration:F0}s)");
        OnOrderCompleted?.Invoke(order);

        // Return to nearest spawn point
        ReturnToSpawn(droneName);
    }

    private void ReturnToSpawn(string droneName)
    {
        if (locationManager == null) return;
        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var anchor = nav.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 currentLLH = anchor.longitudeLatitudeHeight;

        var spawns = locationManager.GetSpawnPoints();
        if (spawns.Count == 0)
        {
            if (commandCenter.TryGetInfo(droneName, out var info2))
                info2.ClearMission();
            return;
        }

        LocationPoint nearest = null;
        double nearestDist = double.MaxValue;

        foreach (var sp in spawns)
        {
            double dist = GeoDistance(currentLLH, sp.GetLLH());
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = sp;
            }
        }

        if (nearest == null || nearestDist < 20.0)
        {
            if (logAssignments && nearest != null)
                Debug.Log($"[OrderManager] {droneName} already near {nearest.GetDisplayName()}");
            if (commandCenter.TryGetInfo(droneName, out var info3))
                info3.ClearMission();
            return;
        }

        if (!BuildAndWriteRoute(droneName, currentLLH, nearest.GetLLH(), out _))
        {
            if (commandCenter.TryGetInfo(droneName, out var info4))
                info4.ClearMission();
            return;
        }

        if (commandCenter.TryGetInfo(droneName, out var droneInfo))
            droneInfo.AssignRoute($"Return to {nearest.GetDisplayName()}");

        if (logAssignments)
            Debug.Log($"[OrderManager] {droneName} returning to {nearest.GetDisplayName()} ({nearestDist:F0}m away)");
    }

    // ================================================================
    //  Clear
    // ================================================================

    public void ClearAllOrders()
    {
        // Stop all drones that have active orders
        foreach (var kvp in _activeByDrone)
        {
            if (commandCenter.TryGetNav(kvp.Key, out var nav))
            {
                nav.SetStop(DroneGeoNavigator.StopReason.External, true);
                nav.SetStop(DroneGeoNavigator.StopReason.External, false);
            }
            if (commandCenter.TryGetInfo(kvp.Key, out var info))
                info.ClearMission();
        }

        _allOrders.Clear();
        _activeByDrone.Clear();
        _orderCounter = 0;
        EmitStatus("All orders cleared, active drones stopped");
    }

    // ================================================================
    //  Status & Query
    // ================================================================

    public string GetStatusText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Orders: {PendingCount} pending, {ActiveCount} active, {CompletedCount} completed, {TotalCount} total");

        foreach (var order in _allOrders)
        {
            string drone = string.IsNullOrEmpty(order.assignedDrone) ? "unassigned" : order.assignedDrone;
            sb.AppendLine($"  {order.orderId}: {order.status} | {order.description} | Drone: {drone}");
        }

        return sb.ToString();
    }

    public DeliveryOrder GetOrderById(string orderId)
    {
        return _allOrders.FirstOrDefault(o => o.orderId == orderId);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private double GeoDistance(double3 a, double3 b)
    {
        double dLon = (b.x - a.x) * 111320.0 * System.Math.Cos(a.y * System.Math.PI / 180.0);
        double dLat = (b.y - a.y) * 110540.0;
        double dAlt = b.z - a.z;
        return System.Math.Sqrt(dLon * dLon + dLat * dLat + dAlt * dAlt);
    }

    private void EmitStatus(string msg)
    {
        Debug.Log($"[OrderManager] {msg}");
        OnStatusChanged?.Invoke(msg);
    }

        /// <summary>
    /// Get or create a dedicated runtime waypoints container for a specific drone
    /// </summary>
    private Transform GetOrCreateDroneWaypointsContainer(string droneName)
    {
        // Look under the main waypoints parent for a container named "Runtime_{droneName}"
        Transform waypointsRoot = routeBuilder.runtimeWaypointsParent?.parent;
        if (waypointsRoot == null) waypointsRoot = routeBuilder.runtimeWaypointsParent;

        string containerName = $"Runtime_{droneName}";

        // Check if it already exists
        Transform existing = waypointsRoot.Find(containerName);
        if (existing != null) return existing;

        // Create new container
        GameObject go = new GameObject(containerName);
        go.transform.SetParent(waypointsRoot, false);

        Debug.Log($"[OrderManager] Created waypoint container: {containerName}");
        return go.transform;
    }

    private bool BuildAndWriteRoute(string droneName, double3 startLLH, double3 endLLH, out Transform container)
    {
        container = null; // No longer needed for navigation, kept for compatibility

        if (!routeBuilder.BuildRoute(startLLH, endLLH, out var route))
        {
            if (logAssignments)
                Debug.LogWarning($"[OrderManager] Failed to plan route for {droneName}");
            return false;
        }

        // Ensure the first point is exactly the drone's current position
        if (route.Count > 0)
        {
            route[0] = startLLH;
        }
        else
        {
            route.Insert(0, startLLH);
        }

        // Directly inject the path into the navigator (no CesiumGlobeAnchor creation needed)
        if (!commandCenter.TryGetNav(droneName, out var nav))
            return false;

        bool ok = nav.InjectPath(route, startNow: true);

        if (logAssignments && ok)
            Debug.Log($"[OrderManager] Injected {route.Count} waypoints for {droneName}");

        return ok;
    }
}