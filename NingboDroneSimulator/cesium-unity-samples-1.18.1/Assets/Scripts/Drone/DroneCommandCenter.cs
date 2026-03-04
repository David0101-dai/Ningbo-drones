// Assets/Scripts/DroneCommandCenter.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class DroneCommandCenter : MonoBehaviour
{
    [Header("Waypoints Root (drag Waypoints node here)")]
    public Transform waypointsRoot;

    [Header("Auto Refresh")]
    public bool autoRefreshOnEnable = true;

    // ====== Registry ======
    private readonly Dictionary<string, DroneInfo> _infoByName = new();
    private readonly Dictionary<string, DroneGeoNavigator> _navByName = new();

    // ====== Public Properties ======
    public int DroneCount => _infoByName.Count;
    public IReadOnlyCollection<string> DroneNames => _infoByName.Keys;

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Awake()
    {
        Refresh();
    }

    void OnEnable()
    {
        if (autoRefreshOnEnable) Refresh();
    }

    // ================================================================
    //  Registry: Find & Register Drones
    // ================================================================

    /// <summary>
    /// Scan scene and register all drones (via DroneInfo)
    /// </summary>
    [ContextMenu("Refresh Registry")]
    public void Refresh()
    {
        _infoByName.Clear();
        _navByName.Clear();

#if UNITY_2023_1_OR_NEWER
        var infos = FindObjectsByType<DroneInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var infos = FindObjectsOfType<DroneInfo>(true);
#endif

        foreach (var info in infos)
        {
            if (info == null) continue;

            string objName = info.gameObject.name;
            string displayName = info.GetName();

            // Register by gameObject.name
            _infoByName[objName] = info;
            if (info.navigator != null)
                _navByName[objName] = info.navigator;

            // Also register by displayName (if different)
            if (!string.IsNullOrEmpty(displayName) && displayName != objName)
            {
                _infoByName[displayName] = info;
                if (info.navigator != null)
                    _navByName[displayName] = info.navigator;
            }
        }


        // Count unique physical drones
        var uniqueDrones = new HashSet<DroneInfo>(_infoByName.Values);
        Debug.Log($"[CommandCenter] Registry refreshed: {uniqueDrones.Count} drones ({_infoByName.Count} name entries)");


    }

    // ================================================================
    //  Query: Single Drone
    // ================================================================

    /// <summary>
    /// Get navigator by drone name
    /// </summary>
    public bool TryGetNav(string droneName, out DroneGeoNavigator nav)
        => _navByName.TryGetValue(droneName, out nav);

    /// <summary>
    /// Get DroneInfo by drone name
    /// </summary>
    public bool TryGetInfo(string droneName, out DroneInfo info)
        => _infoByName.TryGetValue(droneName, out info);

    /// <summary>
    /// Get navigator by camera target (used by SwitchView integration)
    /// </summary>
    public bool TryGetNavByCamTarget(Transform camTarget, out DroneGeoNavigator nav)
    {
        nav = null;
        if (!camTarget) return false;

        var info = camTarget.GetComponentInParent<DroneInfo>();
        if (!info || !info.navigator) return false;

        nav = info.navigator;
        return true;
    }

    /// <summary>
    /// Get list of all drone names
    /// </summary>
    public List<string> GetAllDroneNames()
    {
        return new List<string>(_infoByName.Keys);
    }

    /// <summary>
    /// Get list of available route names
    /// </summary>
    public List<string> GetAvailableRoutes()
    {
        var routes = new List<string>();
        if (!waypointsRoot) return routes;

        foreach (Transform child in waypointsRoot)
            routes.Add(child.name);

        return routes;
    }

    // ================================================================
    //  Query: Fleet Status (for SceneStateProvider / LLM)
    // ================================================================


    /// <summary>
    /// Get status snapshot for a single drone (uses DroneInfo.Snapshot)
    /// </summary>
    public DroneInfo.Snapshot GetDroneSnapshot(string droneName)
    {
        if (_infoByName.TryGetValue(droneName, out var info) && info != null)
            return info.GetSnapshot();

        return new DroneInfo.Snapshot { name = droneName };
    }

    /// <summary>
    /// Get status snapshots for all drones
    /// </summary>
    public List<DroneInfo.Snapshot> GetFleetSnapshot()
    {
        var seen = new HashSet<DroneInfo>();
        var snapshots = new List<DroneInfo.Snapshot>();

        foreach (var kvp in _infoByName)
        {
            if (kvp.Value != null && seen.Add(kvp.Value))
                snapshots.Add(kvp.Value.GetSnapshot());
        }

        return snapshots;
    }

    /// <summary>
    /// Get fleet status as formatted string
    /// </summary>
    public string GetFleetStatusText()
    {
        if (_infoByName.Count == 0) return "No drones registered";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Fleet: {_infoByName.Count} drones");

        foreach (var kvp in _infoByName)
        {
            var s = kvp.Value.GetSnapshot();
            sb.AppendLine($"  {s.name}: {s.missionState} | {s.speedMps:F1}m/s | {s.progressPercent:F0}% | Route: {s.currentRoute}");
        }

        return sb.ToString();
    }

    // ================================================================
    //  Control: Single Drone
    // ================================================================

    /// <summary>
    /// Set cruise speed for a single drone
    /// </summary>
    public bool SetSpeed(string droneName, double speed)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        nav.SetCruiseSpeed(speed);
        Debug.Log($"[CommandCenter] {droneName} speed set to {speed:F1}m/s");
        return true;
    }

    /// <summary>
    /// Pause or resume a single drone
    /// </summary>
    public bool Pause(string droneName, bool pause)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        nav.PauseFlight(pause);
        Debug.Log($"[CommandCenter] {droneName} {(pause ? "paused" : "resumed")}");
        return true;
    }

    /// <summary>
    /// Switch a drone to a named route (e.g. "Waypoints_B")
    /// </summary>
    public bool SelectRoute(string droneName, string routeName, bool warpToStart = false)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        if (!waypointsRoot) return false;

        Transform route = waypointsRoot.Find(routeName);
        if (!route)
        {
            Debug.LogWarning($"[CommandCenter] Route '{routeName}' not found under {waypointsRoot.name}");
            return false;
        }

        bool ok = nav.LoadRoute(route, warpToStart: warpToStart, startNow: true);

        // Notify DroneInfo about the new route
        if (ok && _infoByName.TryGetValue(droneName, out var info))
        {
            info.AssignRoute(routeName);
        }

        Debug.Log($"[CommandCenter] {droneName} route -> {routeName} (ok={ok})");
        return ok;
    }

    /// <summary>
    /// Reload current route for a drone
    /// </summary>
    public bool ReloadRoute(string droneName)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        return nav.ReloadFromWaypointsParent(warpToStart: false);
    }

    // ================================================================
    //  Control: Fleet (absorbed from DroneFleetController)
    // ================================================================

    /// <summary>
    /// Pause or resume all drones
    /// </summary>
    public void PauseAll(bool pause)
    {
        if (_navByName.Count == 0) Refresh();

        foreach (var kvp in _navByName)
        {
            if (kvp.Value != null)
                kvp.Value.SetStop(DroneGeoNavigator.StopReason.External, pause);
        }

        Debug.Log($"[CommandCenter] PauseAll={pause} ({_navByName.Count} drones)");
    }

    /// <summary>
    /// Resume all drones
    /// </summary>
    public void ResumeAll()
    {
        PauseAll(false);
    }

    /// <summary>
    /// Check if any drone is externally paused
    /// </summary>
    public bool AnyExternallyPaused()
    {
        foreach (var kvp in _navByName)
        {
            if (kvp.Value != null && kvp.Value.IsPaused())
                return true;
        }
        return false;
    }

    /// <summary>
    /// Toggle pause state for all drones
    /// </summary>
    public void TogglePauseAll()
    {
        bool anyPaused = AnyExternallyPaused();
        PauseAll(!anyPaused);
    }

    /// <summary>
    /// Set speed for all drones
    /// </summary>
    public void SetSpeedAll(double speed)
    {
        foreach (var kvp in _navByName)
        {
            if (kvp.Value != null)
                kvp.Value.SetCruiseSpeed(speed);
        }

        Debug.Log($"[CommandCenter] SetSpeedAll={speed:F1}m/s");
    }

    /// <summary>
    /// Get list of idle drones (reached end of route and not stopped)
    /// </summary>
    public List<string> GetIdleDrones()
    {
        var idle = new List<string>();
        foreach (var kvp in _navByName)
        {
            if (kvp.Value == null) continue;
            if (kvp.Value.GetProgress() >= 99.9f && !kvp.Value.IsStopped())
                idle.Add(kvp.Key);
        }
        return idle;
    }
        // ================================================================
    //  Command Protocol: Unified command execution
    // ================================================================

    [System.Serializable]
    public class DroneCommand
    {
        public string type;
        public string drone;
        public string route;
        public double speed;
        public double longitude;
        public double latitude;
        public double height;
    }

    /// <summary>
    /// Execute a single command and return result message
    /// </summary>
    public string ExecuteCommand(DroneCommand cmd)
    {
        if (cmd == null || string.IsNullOrEmpty(cmd.type))
            return "Error: empty command";

        string type = cmd.type.Trim().ToLowerInvariant();

        // ===== Fleet commands (no drone needed) =====
        switch (type)
        {
            case "pause_all":
                PauseAll(true);
                return "All drones paused";

            case "resume_all":
                PauseAll(false);
                return "All drones resumed";

            case "query_status":
                return GetFleetStatusText();

            case "query_routes":
                var routes = GetAvailableRoutes();
                return "Available routes: " + string.Join(", ", routes);

            case "create_test_order":
                if (OrderManager.Instance == null)
                    return "Error: OrderManager not found";
                var testOrder = OrderManager.Instance.CreateTestOrder();
                return testOrder != null
                    ? $"Test order {testOrder.orderId} created: {testOrder.description}"
                    : "Error: Could not create test order (need pickup and delivery points)";

            case "create_order":
                return ExecuteCreateNamedOrder(cmd);

            case "order_status":
                if (OrderManager.Instance == null)
                    return "Error: OrderManager not found";
                return OrderManager.Instance.GetStatusText();

            case "location_status":
                if (LocationManager.Instance == null)
                    return "Error: LocationManager not found";
                return LocationManager.Instance.GetStatusText();

            case "import_orders":
                return ExecuteImportOrders(cmd);

            case "save_sample_orders":
                if (OrderManager.Instance == null)
                    return "Error: OrderManager not found";
                string samplePath = OrderManager.Instance.SaveSampleOrderFile();
                return $"Sample order file saved to: {samplePath}";

            case "clear_orders":
                if (OrderManager.Instance == null)
                    return "Error: OrderManager not found";
                OrderManager.Instance.ClearAllOrders();
                return "All orders cleared";
        }

        // ===== Single drone commands (need drone name) =====
        string droneName = cmd.drone;
        if (string.IsNullOrEmpty(droneName))
            return $"Error: command '{type}' requires a drone name";

        // Check drone exists
        if (!TryGetNav(droneName, out var nav))
        {
            var available = string.Join(", ", _navByName.Keys);
            return $"Error: drone '{droneName}' not found. Available: {available}";
        }

        switch (type)
        {
            case "pause":
                nav.PauseFlight(true);
                return $"{droneName} paused";

            case "resume":
                nav.PauseFlight(false);
                return $"{droneName} resumed";

            case "set_speed":
                if (cmd.speed <= 0)
                    return $"Error: speed must be > 0, got {cmd.speed}";
                nav.SetCruiseSpeed(cmd.speed);
                double kmh = cmd.speed * 3.6;
                return $"{droneName} speed set to {kmh:F1} km/h";


            case "select_route":
                if (string.IsNullOrEmpty(cmd.route))
                    return "Error: select_route requires a route name";
                bool routeOk = SelectRoute(droneName, cmd.route);
                return routeOk
                    ? $"{droneName} route changed to {cmd.route}"
                    : $"Error: failed to set route '{cmd.route}' for {droneName}";

            case "query_drone":
                var snap = GetDroneSnapshot(droneName);
                return $"{snap.name}: {snap.missionState} | " +
                       $"Speed:{snap.speedMps:F1}m/s | Progress:{snap.progressPercent:F0}% | " +
                       $"Route:{snap.currentRoute} | " +
                       $"Pos:({snap.positionLLH.x:F4}, {snap.positionLLH.y:F4}, {snap.positionLLH.z:F1}m)";

            case "go_to":
                return ExecuteGoTo(droneName, nav, cmd);

            default:
                return $"Error: unknown command type '{type}'";
        }
    }

    /// <summary>
    /// Execute a list of commands and return combined results
    /// </summary>
    public string ExecuteCommands(DroneCommand[] commands, string currentDroneName)
    {
        if (commands == null || commands.Length == 0)
            return "No commands to execute";

        var results = new System.Text.StringBuilder();

        foreach (var cmd in commands)
        {
            if (cmd == null) continue;

            // Resolve "current" drone name
            if (string.IsNullOrEmpty(cmd.drone) || cmd.drone == "current")
                cmd.drone = currentDroneName;

            string result = ExecuteCommand(cmd);
            results.AppendLine(result);
        }

        return results.ToString().TrimEnd();
    }

    // ================================================================
    //  Go To: fly drone to a specific coordinate
    // ================================================================

    [Header("Runtime Route Builder (for go_to command)")]
    public RuntimeWaypointsBuilder routeBuilder;
    public Transform runtimeWaypointsParent;

    private RuntimeWaypointsBuilder _cachedBuilder;
    private Transform _cachedRuntimeParent;

    private string ExecuteGoTo(string droneName, DroneGeoNavigator nav, DroneCommand cmd)
    {
        // Lazy find builder
        if (_cachedBuilder == null)
        {
            _cachedBuilder = routeBuilder != null
                ? routeBuilder
                : FindObjectOfType<RuntimeWaypointsBuilder>();
        }

        if (_cachedBuilder == null)
            return "Error: RuntimeWaypointsBuilder not found in scene";

        if (_cachedRuntimeParent == null)
        {
            _cachedRuntimeParent = runtimeWaypointsParent != null
                ? runtimeWaypointsParent
                : _cachedBuilder.runtimeWaypointsParent;
        }

        if (_cachedRuntimeParent == null)
            return "Error: runtimeWaypointsParent not configured";

        // Get current position as start
        var anchor = nav.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null)
            return "Error: drone has no CesiumGlobeAnchor";

        double3 startLLH = anchor.longitudeLatitudeHeight;
        double3 endLLH = new double3(cmd.longitude, cmd.latitude, cmd.height);

        // Validate coordinates
        if (cmd.longitude == 0 && cmd.latitude == 0)
            return "Error: go_to requires valid longitude and latitude";

        // Build route
        if (!_cachedBuilder.BuildRoute(startLLH, endLLH, out var llhPoints))
        {
            string reason = _cachedBuilder.LastFailReason;
            return $"Error: route planning failed for {droneName} - {reason}";
        }

        // Write waypoints
        if (!_cachedBuilder.WriteToRuntimeParent(llhPoints))
            return $"Error: failed to write waypoints for {droneName}";

        // Apply route
        bool ok = nav.LoadRoute(_cachedRuntimeParent, warpToStart: false, startNow: true);

        if (ok && _infoByName.TryGetValue(droneName, out var info))
        {
            info.AssignRoute($"GoTo({cmd.longitude:F4},{cmd.latitude:F4})");
        }

        return ok
            ? $"{droneName} flying to ({cmd.longitude:F4}, {cmd.latitude:F4}, {cmd.height:F0}m) " +
              $"via {llhPoints.Count} waypoints" +
              (_cachedBuilder.LastUsedFallback ? " [FALLBACK ROUTE]" : "")
            : $"Error: failed to apply route for {droneName}";
    }

        private string ExecuteCreateNamedOrder(DroneCommand cmd)
    {
        if (OrderManager.Instance == null)
            return "Error: OrderManager not found";

        // cmd.route = "pickup_name,delivery_name"
        if (string.IsNullOrEmpty(cmd.route))
            return "Error: create_order needs pickup and delivery names (format: pickup_name,delivery_name)";

        var parts = cmd.route.Split(',');
        if (parts.Length < 2)
            return "Error: create_order format: pickup_name,delivery_name";

        string pickupName = parts[0].Trim();
        string deliveryName = parts[1].Trim();
        string description = parts.Length >= 3 ? parts[2].Trim() : "";

        var order = OrderManager.Instance.CreateOrderByNames(pickupName, deliveryName, description);
        return order != null
            ? $"Order {order.orderId} created: {order.description}"
            : "Error: Could not create order (check location names)";
    }

    private string ExecuteImportOrders(DroneCommand cmd)
    {
        if (OrderManager.Instance == null)
            return "Error: OrderManager not found";

        string filePath = cmd.route;
        if (string.IsNullOrEmpty(filePath))
        {
            // Try sample_orders.json first, then orders.json
            string samplePath = System.IO.Path.Combine(Application.persistentDataPath, "sample_orders.json");
            string ordersPath = System.IO.Path.Combine(Application.persistentDataPath, "orders.json");

            if (System.IO.File.Exists(samplePath))
                filePath = samplePath;
            else if (System.IO.File.Exists(ordersPath))
                filePath = ordersPath;
            else
                return $"Error: No order files found in {Application.persistentDataPath}";
        }

        int count = OrderManager.Instance.ImportOrdersFromFile(filePath);
        return $"Imported {count} orders from {System.IO.Path.GetFileName(filePath)}";
    }

}