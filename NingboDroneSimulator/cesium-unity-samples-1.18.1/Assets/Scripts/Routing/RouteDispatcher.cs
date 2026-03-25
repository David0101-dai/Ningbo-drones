// Assets/Scripts/Routing/RouteDispatcher.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Dispatches planned routes to actual drones.
/// Manages multi-stop execution: fly to stop → load/unload → fly to next stop.
/// </summary>
public class RouteDispatcher : MonoBehaviour
{
    public static RouteDispatcher Instance;

    [Header("References")]
    public DroneCommandCenter commandCenter;
    public OrderManager orderManager;
    public RuntimeWaypointsBuilder routeBuilder;

    [Header("Settings")]
    [Tooltip("Time to simulate loading/unloading at each stop (real seconds)")]
    public float stopDwellTimeSeconds = 2.0f;

    [Header("Adaptive Speed")]
    [Tooltip("Enable drones to speed up when running late")]
    public bool enableAdaptiveSpeed = true;

    [Tooltip("Speed buffer factor: start speeding up when ETA > deadline * this")]
    [Range(0.5f, 0.95f)]
    public float lateWarningFactor = 0.85f;

    private readonly Dictionary<string, int> _routeIndices = new();

    // ====== Active Dispatches ======
    private readonly Dictionary<string, ActiveDispatch> _activeDispatches = new();

    // ====== Events ======
    public System.Action<string, RouteStop, bool> OnStopCompleted; // droneName, stop, wasLate
    public System.Action<string, PlannedRoute> OnRouteCompleted;    // droneName, route
    public System.Action<string> OnStatus;

    private class ActiveDispatch
    {
        public PlannedRoute route;
        public int currentStopIndex;       // Which stop we're heading to
        public bool isWaitingAtStop;       // Dwelling at current stop
        public float dwellTimer;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!orderManager) orderManager = FindObjectOfType<OrderManager>();
        if (!routeBuilder) routeBuilder = FindObjectOfType<RuntimeWaypointsBuilder>();
    }

    void Start()
    {
        // Subscribe to drone route completion events
        SubscribeAllDrones();
    }

    void Update()
    {
        // Update dwell timers and adaptive speed
        var keys = new List<string>(_activeDispatches.Keys);
        foreach (var droneName in keys)
        {
            if (!_activeDispatches.TryGetValue(droneName, out var dispatch)) continue;

            if (dispatch.isWaitingAtStop)
            {
                dispatch.dwellTimer -= Time.deltaTime;
                if (dispatch.dwellTimer <= 0)
                {
                    dispatch.isWaitingAtStop = false;
                    AdvanceToNextStop(droneName, dispatch);
                }
            }
            else if (enableAdaptiveSpeed)
            {
                CheckAdaptiveSpeed(droneName, dispatch);
            }
        }
    }

    // ================================================================
    //  Dispatch All Routes
    // ================================================================

    /// <summary>
    /// Dispatch all planned routes. Each drone starts flying its route.
    /// </summary>
    public int DispatchAll(List<PlannedRoute> routes)
    {
        if (routes == null || routes.Count == 0)
        {
            EmitStatus("[Dispatcher] No routes to dispatch");
            return 0;
        }

        int dispatched = 0;

        foreach (var route in routes)
        {
            if (DispatchRoute(route))
                dispatched++;
        }

        EmitStatus($"[Dispatcher] Dispatched {dispatched}/{routes.Count} routes");
        return dispatched;
    }

    /// <summary>
    /// Dispatch a single route to its assigned drone.
    /// </summary>
    public bool DispatchRoute(PlannedRoute route)
    {
        if (route == null || route.stops.Count < 2) return false;

        string droneName = route.droneName;

        if (!commandCenter.TryGetNav(droneName, out var nav))
        {
            Debug.LogWarning($"[Dispatcher] Drone '{droneName}' not found");
            return false;
        }

        // Subscribe this drone's completion event
        if (commandCenter.TryGetInfo(droneName, out var info))
        {
            info.OnRouteCompleted -= OnDroneSegmentCompleted;
            info.OnRouteCompleted += OnDroneSegmentCompleted;
        }

        // Mark all orders as Scheduled
        foreach (var stop in route.stops)
        {
            if (stop.order != null)
                stop.order.status = DeliveryOrder.OrderStatus.Scheduled;
        }

        // Load all cargo at depot before departure
        var spec = nav.GetComponent<DroneSpec>();
        if (spec != null)
        {
            spec.currentLoad = 0;
            int totalLoad = route.totalDemand;
            spec.LoadCargo(totalLoad);
            Debug.Log($"[Dispatcher] {droneName} loaded {totalLoad}/{spec.maxCapacity} cargo");
        }

        // Set planning speed
        float planSpeed = VehicleRouter.Instance != null
            ? VehicleRouter.Instance.GetPlanningSpeed()
            : 15f;
        nav.SetCruiseSpeed(planSpeed);

        // Store dispatch state
        _activeDispatches[droneName] = new ActiveDispatch
        {
            route = route,
            currentStopIndex = 1, // Start heading to first delivery (index 0 = depot start)
            isWaitingAtStop = false
        };

        route.isDispatched = true;
        route.currentStopIndex = 1;

        // Fly to first stop
        FlyToStop(droneName, route, 1);

        EmitStatus($"[Dispatcher] {droneName} dispatched: {route.customerCount} stops, " +
                   $"cargo={route.totalDemand}");

        // Register with MissionTracker
        if (MissionTracker.Instance != null)
        {
            int routeIdx = MissionTracker.Instance.BeginRoute(droneName, route);
            // Store route index for later
            _routeIndices[droneName] = routeIdx;
        }

        return true;
    }

    /// <summary>
    /// Clear all active dispatches. Call before importing new data.
    /// </summary>
    public void ClearAll()
    {
        foreach (var kvp in _activeDispatches)
        {
            if (commandCenter != null && commandCenter.TryGetNav(kvp.Key, out var nav))
            {
                nav.SetStop(DroneGeoNavigator.StopReason.External, true);
                nav.SetStop(DroneGeoNavigator.StopReason.External, false);
            }
        }
        _activeDispatches.Clear();
        _routeIndices.Clear();
        Debug.Log("[Dispatcher] All dispatches cleared");
    }

    // ================================================================
    //  Stop-by-Stop Execution
    // ================================================================

    private void FlyToStop(string droneName, PlannedRoute route, int stopIndex)
    {
        if (stopIndex >= route.stops.Count) return;

        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var anchor = nav.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 currentLLH = anchor.longitudeLatitudeHeight;
        double3 targetLLH = route.stops[stopIndex].locationLLH;

        // Try RuntimeWaypointsBuilder first (with collision avoidance)
        bool routeBuilt = false;
        if (routeBuilder != null)
        {
            // Temporarily enforce low altitude limits
            float origCruiseOffset = routeBuilder.cruiseHeightOffset;
            float origHeightStep = routeBuilder.heightStep;
            int origMaxRetries = routeBuilder.maxHeightRetries;

            // Clamp: max cruise = endpoint height + 15m, max retry = 3 steps of 5m
            routeBuilder.cruiseHeightOffset = 10f;
            routeBuilder.heightStep = 5f;
            routeBuilder.maxHeightRetries = 3;

            if (routeBuilder.BuildRoute(currentLLH, targetLLH, out var llhPath))
            {
                if (llhPath.Count > 0)
                    llhPath[0] = currentLLH;

                // Cap all waypoint heights to prevent excessive altitude
                double maxAllowedHeight = System.Math.Max(currentLLH.z, targetLLH.z) + 20.0;
                for (int i = 0; i < llhPath.Count; i++)
                {
                    if (llhPath[i].z > maxAllowedHeight)
                        llhPath[i] = new double3(llhPath[i].x, llhPath[i].y, maxAllowedHeight);
                }

                nav.InjectPath(llhPath, startNow: true);
                routeBuilt = true;
            }

            // Restore original values
            routeBuilder.cruiseHeightOffset = origCruiseOffset;
            routeBuilder.heightStep = origHeightStep;
            routeBuilder.maxHeightRetries = origMaxRetries;
        }

        // Fallback: simple low-altitude path if route builder fails
        if (!routeBuilt)
        {
            var path = BuildLowAltitudePath(currentLLH, targetLLH);
            nav.InjectPath(path, startNow: true);
        }

        // Update DroneInfo
        if (commandCenter.TryGetInfo(droneName, out var info))
        {
            var stop = route.stops[stopIndex];
            string routeLabel = stop.type == RouteStop.StopType.Depot
                ? "Return to Depot"
                : $"→ C{stop.order?.customerNumber:D3} ({stopIndex}/{route.stops.Count - 2})";
            info.AssignRoute(routeLabel);
        }

        // Update order status
        var currentStop = route.stops[stopIndex];
        if (currentStop.order != null)
        {
            currentStop.order.status = DeliveryOrder.OrderStatus.Delivering;
            currentStop.order.assignedDrone = droneName;
        }

        Debug.Log($"[Dispatcher] {droneName} flying to stop {stopIndex}: {route.stops[stopIndex]} " +
                $"(RRT={routeBuilt})");
    }

    /// <summary>
    /// Fallback: simple low-altitude path when RRT fails
    /// </summary>
    private List<double3> BuildLowAltitudePath(double3 startLLH, double3 endLLH)
    {
        var path = new List<double3>();

        double maxEndpointH = System.Math.Max(startLLH.z, endLLH.z);
        double cruiseH = maxEndpointH + 10.0;

        double dLon = (endLLH.x - startLLH.x) * 111320.0 * System.Math.Cos(startLLH.y * System.Math.PI / 180.0);
        double dLat = (endLLH.y - startLLH.y) * 110540.0;
        double horizontalDist = System.Math.Sqrt(dLon * dLon + dLat * dLat);

        if (horizontalDist < 200.0)
        {
            double safeH = maxEndpointH + 5.0;
            path.Add(startLLH);
            path.Add(new double3(startLLH.x, startLLH.y, safeH));
            path.Add(new double3(endLLH.x, endLLH.y, safeH));
            path.Add(endLLH);
        }
        else
        {
            double3 climbPoint = new double3(
                math.lerp(startLLH.x, endLLH.x, 0.15),
                math.lerp(startLLH.y, endLLH.y, 0.15),
                cruiseH
            );
            double3 descendPoint = new double3(
                math.lerp(startLLH.x, endLLH.x, 0.85),
                math.lerp(startLLH.y, endLLH.y, 0.85),
                cruiseH
            );
            path.Add(startLLH);
            path.Add(climbPoint);
            path.Add(descendPoint);
            path.Add(endLLH);
        }

        return path;
    }

    private void OnDroneSegmentCompleted(DroneInfo droneInfo)
    {
        string droneName = droneInfo.GetName();

        if (!_activeDispatches.TryGetValue(droneName, out var dispatch))
            return;

        var route = dispatch.route;
        int stopIdx = dispatch.currentStopIndex;

        if (stopIdx >= route.stops.Count) return;

        var stop = route.stops[stopIdx];

        // Record actual arrival
        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;
        stop.actualArrival = simTime;
        stop.wasLate = stop.order != null && simTime > stop.order.dueTime;
        stop.isCompleted = true;
        // 在 OnDroneSegmentCompleted 方法中，stop.isCompleted = true; 之后追加：

        // Record to MissionTracker
        if (stop.type == RouteStop.StopType.Delivery && MissionTracker.Instance != null)
        {
            var nav = droneInfo.navigator;
            var spec = nav != null ? nav.GetComponent<DroneSpec>() : null;
            int routeIdx = _routeIndices.ContainsKey(droneName) ? _routeIndices[droneName] : 0;

            MissionTracker.Instance.RecordStopCompletion(
                droneName, routeIdx, stopIdx, stop, spec, nav);
        }

        // Unload cargo at delivery stop
        if (stop.type == RouteStop.StopType.Delivery && stop.order != null)
        {
            var nav = droneInfo.navigator;
            var spec = nav != null ? nav.GetComponent<DroneSpec>() : null;
            if (spec != null)
            {
                int unloaded = Mathf.Min(stop.order.demand, spec.currentLoad);
                spec.currentLoad -= unloaded;
            }

            // Mark order completed
            stop.order.status = DeliveryOrder.OrderStatus.Completed;
            stop.order.completedTime = simTime;

            string lateStr = stop.wasLate ? " [LATE!]" : " [ON TIME]";
            EmitStatus($"{droneName} delivered to C{stop.order.customerNumber:D3}{lateStr} " +
                       $"(cargo remaining: {spec?.currentLoad ?? 0})");

            OnStopCompleted?.Invoke(droneName, stop, stop.wasLate);
        }

        // Start dwell timer (simulates unloading)
        dispatch.isWaitingAtStop = true;
        dispatch.dwellTimer = stop.type == RouteStop.StopType.Depot ? 0.5f : stopDwellTimeSeconds;
    }

    private void AdvanceToNextStop(string droneName, ActiveDispatch dispatch)
    {
        dispatch.currentStopIndex++;
        dispatch.route.currentStopIndex = dispatch.currentStopIndex;

        if (dispatch.currentStopIndex >= dispatch.route.stops.Count)
        {
            // Route complete!
            dispatch.route.isCompleted = true;
            // 在 AdvanceToNextStop 方法中，dispatch.route.isCompleted = true; 之后追加：

            if (MissionTracker.Instance != null && _routeIndices.ContainsKey(droneName))
            {
                MissionTracker.Instance.EndRoute(droneName, _routeIndices[droneName]);
                _routeIndices.Remove(droneName);
            }
            _activeDispatches.Remove(droneName);

            if (commandCenter.TryGetInfo(droneName, out var info))
                info.ClearMission();

            EmitStatus($"[Dispatcher] {droneName} completed all {dispatch.route.customerCount} deliveries!");
            OnRouteCompleted?.Invoke(droneName, dispatch.route);
            return;
        }

        // Fly to next stop
        FlyToStop(droneName, dispatch.route, dispatch.currentStopIndex);
    }

    // ================================================================
    //  Adaptive Speed Control
    // ================================================================

    private void CheckAdaptiveSpeed(string droneName, ActiveDispatch dispatch)
    {
        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var route = dispatch.route;
        int stopIdx = dispatch.currentStopIndex;
        if (stopIdx >= route.stops.Count) return;

        var targetStop = route.stops[stopIdx];
        if (targetStop.order == null) return;

        // Get current position
        var anchor = nav.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 currentLLH = anchor.longitudeLatitudeHeight;
        double3 targetLLH = targetStop.locationLLH;

        // Calculate remaining distance
        double remainingDist = GeoDistanceMeters(currentLLH, targetLLH);

        // Calculate time budget
        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;
        float deadline = targetStop.order.dueTime;
        float timeRemaining = deadline - simTime;

        if (timeRemaining <= 0)
        {
            // Already late, go max speed
            var spec = nav.GetComponent<DroneSpec>();
            if (spec != null)
                nav.SetCruiseSpeed(spec.maxSpeed);
            return;
        }

        // ETA at current speed
        float currentSpeed = (float)nav.cruiseSpeed;
        float eta = (float)(remainingDist / currentSpeed);

        // If ETA > timeRemaining * warningFactor, speed up
        if (eta > timeRemaining * lateWarningFactor)
        {
            // Calculate needed speed
            float neededSpeed = (float)(remainingDist / (timeRemaining * 0.9f)); // 10% safety margin

            var spec = nav.GetComponent<DroneSpec>();
            float maxSpeed = spec != null ? (float)spec.maxSpeed : 30f;
            float newSpeed = Mathf.Min(neededSpeed, maxSpeed);

            if (newSpeed > currentSpeed * 1.1f) // Only adjust if significant
            {
                nav.SetCruiseSpeed(newSpeed);
                Debug.Log($"[Dispatcher] {droneName} speeding up: {currentSpeed:F1} → {newSpeed:F1} m/s " +
                          $"(deadline in {timeRemaining:F0}s, ETA was {eta:F0}s)");
            }
        }
    }

    // ================================================================
    //  Query
    // ================================================================

    public int ActiveDispatchCount => _activeDispatches.Count;

    public string GetDispatchStatus()
    {
        if (_activeDispatches.Count == 0)
            return "No active dispatches";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Active dispatches: {_activeDispatches.Count}");

        foreach (var kvp in _activeDispatches)
        {
            var d = kvp.Value;
            int completed = d.route.stops.Count(s => s.isCompleted);
            int total = d.route.DeliveryStopCount;
            int late = d.route.stops.Count(s => s.wasLate);
            sb.AppendLine($"  {kvp.Key}: {completed}/{total} delivered, {late} late");
        }

        return sb.ToString();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void SubscribeAllDrones()
    {
#if UNITY_2023_1_OR_NEWER
        var infos = FindObjectsByType<DroneInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var infos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in infos)
        {
            info.OnRouteCompleted -= OnDroneSegmentCompleted;
            info.OnRouteCompleted += OnDroneSegmentCompleted;
        }
    }

    /// <summary>Call after new drones are spawned</summary>
    public void SubscribeDrone(DroneInfo info)
    {
        info.OnRouteCompleted -= OnDroneSegmentCompleted;
        info.OnRouteCompleted += OnDroneSegmentCompleted;
    }

    private double GeoDistanceMeters(double3 a, double3 b)
    {
        double dLon = (b.x - a.x) * 111320.0 * System.Math.Cos(a.y * System.Math.PI / 180.0);
        double dLat = (b.y - a.y) * 110540.0;
        return System.Math.Sqrt(dLon * dLon + dLat * dLat);
    }

    private void EmitStatus(string msg)
    {
        Debug.Log(msg);
        OnStatus?.Invoke(msg);
    }
}