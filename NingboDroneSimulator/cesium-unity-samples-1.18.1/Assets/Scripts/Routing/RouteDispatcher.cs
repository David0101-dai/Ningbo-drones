// Assets/Scripts/Routing/RouteDispatcher.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Dispatches planned routes to actual drones.
/// Supports MULTI-TRIP: when a drone finishes its route and returns to depot,
/// it automatically picks up the next unassigned route.
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

    // ====== Multi-Trip Queue ======
    private readonly Queue<PlannedRoute> _pendingRoutes = new();
    private int _totalRoutesPlanned = 0;
    private int _totalRoutesCompleted = 0;

    // ====== Events ======
    public System.Action<string, RouteStop, bool> OnStopCompleted;
    public System.Action<string, PlannedRoute> OnRouteCompleted;
    public System.Action<string> OnStatus;

    private class ActiveDispatch
    {
        public PlannedRoute route;
        public int currentStopIndex;
        public bool isWaitingAtStop;
        public float dwellTimer;
        public MissionTracker.LegRecord currentLeg;
        public List<double3> currentLegPath;
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
        SubscribeAllDrones();
    }

    void Update()
    {
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
    //  Dispatch All Routes (with Multi-Trip Queue)
    // ================================================================

    public int DispatchAll(List<PlannedRoute> routes)
    {
        if (routes == null || routes.Count == 0)
        {
            EmitStatus("[Dispatcher] No routes to dispatch");
            return 0;
        }

        _pendingRoutes.Clear();
        _totalRoutesPlanned = routes.Count;
        _totalRoutesCompleted = 0;

        // Collect REAL available drone names (only drones that actually exist)
        var realDrones = new List<string>();
        var seen = new HashSet<DroneInfo>();
        foreach (var kvp in commandCenter.GetFleetSnapshot())
        {
            realDrones.Add(kvp.name);
        }

        if (realDrones.Count == 0)
        {
            EmitStatus("[Dispatcher] No drones available!");
            return 0;
        }

        // Assign real drone names to routes and dispatch
        int dispatched = 0;
        int droneIdx = 0;

        foreach (var route in routes)
        {
            if (droneIdx < realDrones.Count)
            {
                // Assign a real drone name to this route
                route.droneName = realDrones[droneIdx];

                if (DispatchRoute(route))
                {
                    dispatched++;
                    droneIdx++;
                }
                else
                {
                    // Dispatch failed — queue it
                    _pendingRoutes.Enqueue(route);
                }
            }
            else
            {
                // No more free drones — queue for multi-trip
                _pendingRoutes.Enqueue(route);
            }
        }

        int queued = _pendingRoutes.Count;
        EmitStatus($"[Dispatcher] Dispatched {dispatched}/{routes.Count} routes" +
                (queued > 0 ? $", {queued} queued for multi-trip" : ""));

        return dispatched;
    }

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

        // Load all cargo at depot
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
            currentStopIndex = 1,
            isWaitingAtStop = false,
            currentLeg = null,
            currentLegPath = null
        };

        route.isDispatched = true;
        route.currentStopIndex = 1;

        // Register with MissionTracker
        if (MissionTracker.Instance != null)
        {
            int routeIdx = MissionTracker.Instance.BeginRoute(droneName, route);
            _routeIndices[droneName] = routeIdx;
        }

        // Fly to first stop
        FlyToStop(droneName, route, 1);

        EmitStatus($"[Dispatcher] {droneName} dispatched: {route.customerCount} stops, " +
                   $"cargo={route.totalDemand}");

        return true;
    }

    /// <summary>
    /// Clear all active dispatches.
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
            if (commandCenter != null && commandCenter.TryGetInfo(kvp.Key, out var info))
                info.ClearMission();
        }

        _activeDispatches.Clear();
        _routeIndices.Clear();
        _pendingRoutes.Clear();
        _totalRoutesPlanned = 0;
        _totalRoutesCompleted = 0;

#if UNITY_2023_1_OR_NEWER
        var allInfos = FindObjectsByType<DroneInfo>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var allInfos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in allInfos)
        {
            if (info != null)
                info.OnRouteCompleted -= OnDroneSegmentCompleted;
        }

        Debug.Log("[Dispatcher] All dispatches cleared");
    }

    // ================================================================
    //  Multi-Trip: Assign Next Route to Idle Drone
    // ================================================================

    private void TryAssignNextRoute(string droneName)
    {
        if (_pendingRoutes.Count == 0)
        {
            Debug.Log($"[Dispatcher] {droneName} idle — no more routes in queue " +
                      $"({_totalRoutesCompleted}/{_totalRoutesPlanned} completed)");

            // Check if all routes are done
            if (_totalRoutesCompleted >= _totalRoutesPlanned &&
                _activeDispatches.Count == 0)
            {
                EmitStatus($"[Dispatcher] ALL {_totalRoutesPlanned} routes completed!");
            }
            return;
        }

        // Get next route from queue
        var nextRoute = _pendingRoutes.Dequeue();

        // Reassign drone name to this route
        nextRoute.droneName = droneName;

        Debug.Log($"[Dispatcher] {droneName} picking up next route: " +
                  $"{nextRoute.customerCount} customers, demand={nextRoute.totalDemand} " +
                  $"({_pendingRoutes.Count} remaining in queue)");

        // Reset drone speed to planning speed
        if (commandCenter.TryGetNav(droneName, out var nav))
        {
            float planSpeed = VehicleRouter.Instance != null
                ? VehicleRouter.Instance.GetPlanningSpeed()
                : 15f;
            nav.SetCruiseSpeed(planSpeed);
        }

        // Dispatch!
        DispatchRoute(nextRoute);
    }

    // ================================================================
    //  Stop-by-Stop Execution (with Leg Recording)
    // ================================================================

    private void FlyToStop(string droneName, PlannedRoute route, int stopIndex)
    {
        if (stopIndex >= route.stops.Count) return;

        if (!commandCenter.TryGetNav(droneName, out var nav)) return;

        var anchor = nav.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 currentLLH = anchor.longitudeLatitudeHeight;
        double3 targetLLH = route.stops[stopIndex].locationLLH;

        // Determine origin name
        string originName;
        if (stopIndex == 0 ||
            (stopIndex == 1 && route.stops[0].type == RouteStop.StopType.Depot))
            originName = route.stops[0].order?.deliveryPointName ?? "Depot";
        else
        {
            var prevStop = route.stops[stopIndex - 1];
            originName = prevStop.type == RouteStop.StopType.Depot
                ? "Depot"
                : $"C{prevStop.order?.customerNumber:D3}";
        }

        // Determine destination name
        var targetStop = route.stops[stopIndex];
        string destName = targetStop.type == RouteStop.StopType.Depot
            ? "Depot"
            : $"C{targetStop.order?.customerNumber:D3}";

        // Build flight path
        List<double3> flightPath = null;
        bool routeBuilt = false;

        if (routeBuilder != null)
        {
            float origCruiseOffset = routeBuilder.cruiseHeightOffset;
            float origHeightStep = routeBuilder.heightStep;
            int origMaxRetries = routeBuilder.maxHeightRetries;

            routeBuilder.cruiseHeightOffset = 10f;
            routeBuilder.heightStep = 5f;
            routeBuilder.maxHeightRetries = 3;

            if (routeBuilder.BuildRoute(currentLLH, targetLLH, out flightPath))
            {
                if (flightPath.Count > 0)
                    flightPath[0] = currentLLH;

                double maxAllowedHeight =
                    System.Math.Max(currentLLH.z, targetLLH.z) + 20.0;
                for (int i = 0; i < flightPath.Count; i++)
                {
                    if (flightPath[i].z > maxAllowedHeight)
                        flightPath[i] = new double3(
                            flightPath[i].x, flightPath[i].y, maxAllowedHeight);
                }

                nav.InjectPath(flightPath, startNow: true);
                routeBuilt = true;
            }

            routeBuilder.cruiseHeightOffset = origCruiseOffset;
            routeBuilder.heightStep = origHeightStep;
            routeBuilder.maxHeightRetries = origMaxRetries;
        }

        if (!routeBuilt)
        {
            flightPath = BuildLowAltitudePath(currentLLH, targetLLH);
            nav.InjectPath(flightPath, startNow: true);
        }

        // ====== BEGIN LEG RECORDING ======
        if (MissionTracker.Instance != null &&
            _activeDispatches.TryGetValue(droneName, out var dispatch))
        {
            var spec = nav.GetComponent<DroneSpec>();
            int routeIdx = _routeIndices.ContainsKey(droneName)
                ? _routeIndices[droneName] : 0;
            int legIdx = stopIndex - 1;

            dispatch.currentLeg = MissionTracker.Instance.BeginLeg(
                droneName, routeIdx, legIdx,
                originName, currentLLH,
                destName, targetLLH,
                flightPath,
                spec, nav
            );
            dispatch.currentLegPath = flightPath;
        }

        // Update DroneInfo
        if (commandCenter.TryGetInfo(droneName, out var info))
        {
            var stop = route.stops[stopIndex];
            string routeLabel = stop.type == RouteStop.StopType.Depot
                ? "Return to Depot"
                : $"→ C{stop.order?.customerNumber:D3} " +
                  $"({stopIndex}/{route.stops.Count - 2})";
            info.AssignRoute(routeLabel);
        }

        // Update order status
        var currentStop = route.stops[stopIndex];
        if (currentStop.order != null)
        {
            currentStop.order.status = DeliveryOrder.OrderStatus.Delivering;
            currentStop.order.assignedDrone = droneName;
        }

        Debug.Log($"[Dispatcher] {droneName} flying to stop {stopIndex}: " +
                  $"{originName}→{destName} " +
                  $"(path={flightPath?.Count ?? 0} wps, RRT={routeBuilt})");
    }

    private List<double3> BuildLowAltitudePath(double3 startLLH, double3 endLLH)
    {
        var path = new List<double3>();

        double maxEndpointH = System.Math.Max(startLLH.z, endLLH.z);
        double cruiseH = maxEndpointH + 10.0;

        double dLon = (endLLH.x - startLLH.x) * 111320.0 *
                      System.Math.Cos(startLLH.y * System.Math.PI / 180.0);
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
                cruiseH);
            double3 descendPoint = new double3(
                math.lerp(startLLH.x, endLLH.x, 0.85),
                math.lerp(startLLH.y, endLLH.y, 0.85),
                cruiseH);
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
        float simTime = SimClock.Instance != null
            ? SimClock.Instance.SimTime : Time.time;
        stop.actualArrival = simTime;
        stop.wasLate = stop.order != null && simTime > stop.order.dueTime;
        stop.isCompleted = true;

        // ====== END LEG RECORDING ======
        if (MissionTracker.Instance != null && dispatch.currentLeg != null)
        {
            var nav = droneInfo.navigator;
            var spec = nav != null ? nav.GetComponent<DroneSpec>() : null;

            MissionTracker.Instance.EndLeg(
                dispatch.currentLeg,
                spec,
                stop.order
            );
            dispatch.currentLeg = null;
            dispatch.currentLegPath = null;
        }

        // Record stop to MissionTracker
        if (stop.type == RouteStop.StopType.Delivery && MissionTracker.Instance != null)
        {
            var nav = droneInfo.navigator;
            var spec = nav != null ? nav.GetComponent<DroneSpec>() : null;
            int routeIdx = _routeIndices.ContainsKey(droneName)
                ? _routeIndices[droneName] : 0;

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

            stop.order.status = DeliveryOrder.OrderStatus.Completed;
            stop.order.completedTime = simTime;

            string lateStr = stop.wasLate ? " [LATE!]" : " [ON TIME]";
            EmitStatus($"{droneName} delivered to " +
                       $"C{stop.order.customerNumber:D3}{lateStr} " +
                       $"(cargo remaining: {spec?.currentLoad ?? 0})");

            OnStopCompleted?.Invoke(droneName, stop, stop.wasLate);
        }

        // Start dwell timer
        dispatch.isWaitingAtStop = true;
        dispatch.dwellTimer = stop.type == RouteStop.StopType.Depot
            ? 0.5f : stopDwellTimeSeconds;
    }

    private void AdvanceToNextStop(string droneName, ActiveDispatch dispatch)
    {
        dispatch.currentStopIndex++;
        dispatch.route.currentStopIndex = dispatch.currentStopIndex;

        if (dispatch.currentStopIndex >= dispatch.route.stops.Count)
        {
            // ═══════════════════════════════════════
            //  Current route complete!
            // ═══════════════════════════════════════
            dispatch.route.isCompleted = true;
            _totalRoutesCompleted++;

            if (MissionTracker.Instance != null &&
                _routeIndices.ContainsKey(droneName))
            {
                MissionTracker.Instance.EndRoute(
                    droneName, _routeIndices[droneName]);
                _routeIndices.Remove(droneName);
            }
            _activeDispatches.Remove(droneName);

            if (commandCenter.TryGetInfo(droneName, out var info))
                info.ClearMission();

            EmitStatus($"[Dispatcher] {droneName} completed all " +
                       $"{dispatch.route.customerCount} deliveries! " +
                       $"({_totalRoutesCompleted}/{_totalRoutesPlanned} routes done)");

            OnRouteCompleted?.Invoke(droneName, dispatch.route);

            // ═══════════════════════════════════════
            //  MULTI-TRIP: Try to pick up next route
            // ═══════════════════════════════════════
            TryAssignNextRoute(droneName);

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

        var anchor = nav.GetComponent<CesiumForUnity.CesiumGlobeAnchor>();
        if (anchor == null) return;

        double3 currentLLH = anchor.longitudeLatitudeHeight;
        double3 targetLLH = targetStop.locationLLH;

        double remainingDist = GeoDistanceMeters(currentLLH, targetLLH);

        float simTime = SimClock.Instance != null
            ? SimClock.Instance.SimTime : Time.time;
        float deadline = targetStop.order.dueTime;
        float timeRemaining = deadline - simTime;

        if (timeRemaining <= 0)
        {
            var spec = nav.GetComponent<DroneSpec>();
            if (spec != null)
                nav.SetCruiseSpeed(spec.maxSpeed);
            return;
        }

        float currentSpeed = (float)nav.cruiseSpeed;
        float eta = (float)(remainingDist / currentSpeed);

        if (eta > timeRemaining * lateWarningFactor)
        {
            float neededSpeed =
                (float)(remainingDist / (timeRemaining * 0.9f));
            var spec = nav.GetComponent<DroneSpec>();
            float maxSpeed = spec != null ? (float)spec.maxSpeed : 30f;
            float newSpeed = Mathf.Min(neededSpeed, maxSpeed);

            if (newSpeed > currentSpeed * 1.1f)
            {
                nav.SetCruiseSpeed(newSpeed);
                Debug.Log($"[Dispatcher] {droneName} speeding up: " +
                          $"{currentSpeed:F1} → {newSpeed:F1} m/s " +
                          $"(deadline in {timeRemaining:F0}s, " +
                          $"ETA was {eta:F0}s)");
            }
        }
    }

    // ================================================================
    //  Query
    // ================================================================

    public int ActiveDispatchCount => _activeDispatches.Count;
    public int PendingRouteCount => _pendingRoutes.Count;
    public int TotalRoutesCompleted => _totalRoutesCompleted;
    public int TotalRoutesPlanned => _totalRoutesPlanned;

    public string GetDispatchStatus()
    {
        if (_activeDispatches.Count == 0 && _pendingRoutes.Count == 0)
            return "No active dispatches";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Routes: {_totalRoutesCompleted}/{_totalRoutesPlanned} done, " +
                      $"{_activeDispatches.Count} active, " +
                      $"{_pendingRoutes.Count} queued");

        foreach (var kvp in _activeDispatches)
        {
            var d = kvp.Value;
            int completed = d.route.stops.Count(s => s.isCompleted);
            int total = d.route.DeliveryStopCount;
            int late = d.route.stops.Count(s => s.wasLate);
            sb.AppendLine(
                $"  {kvp.Key}: {completed}/{total} delivered, {late} late");
        }

        return sb.ToString();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void SubscribeAllDrones()
    {
#if UNITY_2023_1_OR_NEWER
        var infos = FindObjectsByType<DroneInfo>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var infos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in infos)
        {
            info.OnRouteCompleted -= OnDroneSegmentCompleted;
            info.OnRouteCompleted += OnDroneSegmentCompleted;
        }
    }

    public void SubscribeDrone(DroneInfo info)
    {
        info.OnRouteCompleted -= OnDroneSegmentCompleted;
        info.OnRouteCompleted += OnDroneSegmentCompleted;
    }

    private double GeoDistanceMeters(double3 a, double3 b)
    {
        double dLon = (b.x - a.x) * 111320.0 *
                      System.Math.Cos(a.y * System.Math.PI / 180.0);
        double dLat = (b.y - a.y) * 110540.0;
        return System.Math.Sqrt(dLon * dLon + dLat * dLat);
    }

    private void EmitStatus(string msg)
    {
        Debug.Log(msg);
        OnStatus?.Invoke(msg);
    }
}