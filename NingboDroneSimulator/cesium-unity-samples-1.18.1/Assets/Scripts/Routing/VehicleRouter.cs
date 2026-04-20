// Assets/Scripts/Routing/VehicleRouter.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Vehicle Routing Problem manager.
/// Now delegates solving to IRoutingSolver implementations via SolverRegistry.
///
/// Responsibilities:
/// - Build RoutingContext from raw data
/// - Call the active solver
/// - Assign drone names to routes
/// - Provide solution summaries
/// </summary>
public class VehicleRouter : MonoBehaviour
{
    public static VehicleRouter Instance;

    [Header("Strategy (Legacy — now uses SolverRegistry)")]
    public RoutingStrategy strategy = RoutingStrategy.Balanced;

    [Header("Speed Settings")]
    public float defaultSpeedMps = 15f;
    public float efficiencySpeedMps = 25f;
    public float economySpeedMps = 10f;

    [Header("State")]
    [SerializeField] private int _lastRouteCount;
    [SerializeField] private int _lastUnrouted;

    // ====== Results ======
    private List<PlannedRoute> _lastSolution;
    public List<PlannedRoute> LastSolution => _lastSolution;

    // ====== Events ======
    public System.Action<List<PlannedRoute>> OnRoutesPlanned;
    public System.Action<string> OnStatus;

    public enum RoutingStrategy
    {
        Efficiency,
        Economy,
        Balanced
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Main Entry Point
    // ================================================================

    public List<PlannedRoute> PlanRoutes(
        List<DeliveryOrder> orders,
        List<DroneInfo.Snapshot> fleet,
        double3 depotLLH,
        int vehicleCapacity)
    {
        if (orders == null || orders.Count == 0)
        {
            EmitStatus("[Router] No orders to route");
            return new List<PlannedRoute>();
        }

        IRoutingSolver solver = GetActiveSolver();
        if (solver == null)
        {
            EmitStatus("[Router] No solver available!");
            return new List<PlannedRoute>();
        }

        float speedForPlanning = GetPlanningSpeed();

        var pending = orders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            EmitStatus("[Router] No pending orders");
            return new List<PlannedRoute>();
        }

        EmitStatus($"[Router] Solving with '{solver.Name}': {pending.Count} orders, " +
                $"{fleet.Count} drones (cap={vehicleCapacity}, speed={speedForPlanning:F1}m/s)");

        RoutingContext ctx = BuildContext(pending, fleet, depotLLH, vehicleCapacity, speedForPlanning);

        ApplySolomonParams(solver);

        // ★ Call the solver ★
        var routes = solver.Solve(ctx);

        // ════════════════════════════════════════════════
        //  SAFETY NET: Ensure 100% customer coverage
        //  If the solver left some customers unrouted,
        //  force them into existing or new routes.
        // ════════════════════════════════════════════════
        routes = EnsureFullCoverage(routes, ctx, pending);

        // Assign drone names
        AssignDroneNames(routes, fleet);

        // Store results
        _lastSolution = routes;
        _lastRouteCount = routes.Count;
        _lastUnrouted = 0; // Guaranteed by EnsureFullCoverage

        int totalRouted = routes.Sum(r => r.DeliveryStopCount);

        string summary = $"[Router] {solver.Name}: {routes.Count} routes, " +
                        $"{totalRouted}/{pending.Count} routed";
        EmitStatus(summary);
        Debug.Log(summary);

        foreach (var route in routes)
            DLog.Info("Router", $" {route}");

        OnRoutesPlanned?.Invoke(routes);
        return routes;
    }

    // ================================================================
    //  Safety Net: Force 100% Coverage
    // ================================================================

    /// <summary>
    /// After any solver runs, check if all customers are routed.
    /// If not, force-insert the missing ones into existing routes
    /// or create new overflow routes.
    /// This guarantees no customer is ever dropped.
    /// </summary>
    private List<PlannedRoute> EnsureFullCoverage(
        List<PlannedRoute> routes, RoutingContext ctx, List<DeliveryOrder> allOrders)
    {
        // Find which orders are already routed
        var routedOrders = new HashSet<DeliveryOrder>();
        foreach (var route in routes)
        {
            foreach (var stop in route.stops)
            {
                if (stop.type == RouteStop.StopType.Delivery && stop.order != null)
                    routedOrders.Add(stop.order);
            }
        }

        // Find unrouted orders
        var unrouted = new List<int>();
        for (int i = 0; i < ctx.orders.Count; i++)
        {
            if (!routedOrders.Contains(ctx.orders[i]))
                unrouted.Add(i);
        }

        if (unrouted.Count == 0)
        {
            DLog.Info("Router", $" All {ctx.orders.Count} customers routed by solver ✓");
            return routes;
        }

        DLog.Warn("General",$"[Router SafetyNet] Solver left {unrouted.Count}/{ctx.orders.Count} " +
                        $"customers unrouted! Force-inserting...");

        // Try to insert into existing routes (cheapest position, ignore TW)
        var stillUnrouted = new List<int>();

        foreach (int custIdx in unrouted)
        {
            var order = ctx.orders[custIdx];
            bool inserted = false;

            // Find route with enough capacity and cheapest insertion
            float bestCost = float.MaxValue;
            PlannedRoute bestRoute = null;
            int bestPos = -1;

            foreach (var route in routes)
            {
                if (route.totalDemand + order.demand > route.vehicleCapacity)
                    continue;

                // Try each position (skip depot at start and end)
                for (int pos = 1; pos < route.stops.Count; pos++)
                {
                    int prevMI = GetMatrixIndex(route.stops[pos - 1], ctx);
                    int nextMI = GetMatrixIndex(
                        route.stops[Mathf.Min(pos, route.stops.Count - 1)], ctx);
                    int newMI = custIdx + 1;

                    float cost = ctx.distanceMatrix[prevMI, newMI] +
                                ctx.distanceMatrix[newMI, nextMI] -
                                ctx.distanceMatrix[prevMI, nextMI];

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestRoute = route;
                        bestPos = pos;
                    }
                }
            }

            if (bestRoute != null)
            {
                // Remove trailing depot, insert, re-add depot
                bestRoute.stops.RemoveAt(bestRoute.stops.Count - 1);

                var stop = ctx.MakeDeliveryStop(order);
                bestRoute.stops.Insert(bestPos, stop);
                bestRoute.totalDemand += order.demand;

                bestRoute.stops.Add(ctx.MakeDepotStop());

                // Recalculate timing
                RecalcTiming(bestRoute, ctx);
                bestRoute.customerCount = bestRoute.DeliveryStopCount;
                bestRoute.totalDistance = CalcRouteDistance(bestRoute, ctx);

                inserted = true;

                DLog.Info("Router", $" Inserted C{order.customerNumber:D3} " +
                        $"into existing route (pos={bestPos}, demand={order.demand})");
            }

            if (!inserted)
                stillUnrouted.Add(custIdx);
        }

        // Create overflow routes for any remaining
        while (stillUnrouted.Count > 0)
        {
            var overflowRoute = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };
            overflowRoute.stops.Add(ctx.MakeDepotStop(0));

            float currentTime = 0;
            int lastMI = 0;
            var toRemove = new List<int>();

            foreach (int custIdx in stillUnrouted)
            {
                var order = ctx.orders[custIdx];
                if (overflowRoute.totalDemand + order.demand > ctx.vehicleCapacity)
                    continue;

                int custMI = custIdx + 1;
                float travel = ctx.distanceMatrix[lastMI, custMI] / ctx.speedMps;
                currentTime += travel;

                var stop = ctx.MakeDeliveryStop(order);
                stop.plannedArrival = currentTime;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = Mathf.Max(currentTime, order.readyTime);
                stop.serviceEnd = stop.serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = currentTime > order.dueTime;

                overflowRoute.stops.Add(stop);
                overflowRoute.totalDemand += order.demand;

                currentTime = stop.plannedDeparture;
                lastMI = custMI;
                toRemove.Add(custIdx);
            }

            foreach (var idx in toRemove)
                stillUnrouted.Remove(idx);

            // Return to depot
            float returnTravel = ctx.distanceMatrix[lastMI, 0] / ctx.speedMps;
            overflowRoute.stops.Add(ctx.MakeDepotStop(currentTime + returnTravel));
            overflowRoute.customerCount = overflowRoute.DeliveryStopCount;
            overflowRoute.totalTime = currentTime + returnTravel;
            overflowRoute.totalDistance = CalcRouteDistance(overflowRoute, ctx);

            routes.Add(overflowRoute);

            DLog.Info("Router", $" Created overflow route: " +
                    $"{overflowRoute.customerCount} customers, " +
                    $"demand={overflowRoute.totalDemand}");

            // Safety: if nothing was added (shouldn't happen), break
            if (toRemove.Count == 0)
            {
                // Absolute last resort: one route per customer
                foreach (int custIdx in stillUnrouted)
                {
                    var order = ctx.orders[custIdx];
                    var solo = new PlannedRoute
                    {
                        vehicleCapacity = ctx.vehicleCapacity,
                        totalDemand = order.demand
                    };

                    solo.stops.Add(ctx.MakeDepotStop(0));

                    float t = ctx.distanceMatrix[0, custIdx + 1] / ctx.speedMps;
                    var s = ctx.MakeDeliveryStop(order);
                    s.plannedArrival = t;
                    s.serviceStart = Mathf.Max(t, order.readyTime);
                    s.serviceEnd = s.serviceStart + order.serviceTime;
                    s.plannedDeparture = s.serviceEnd;
                    s.wasLate = t > order.dueTime;
                    solo.stops.Add(s);

                    float ret = ctx.distanceMatrix[custIdx + 1, 0] / ctx.speedMps;
                    solo.stops.Add(ctx.MakeDepotStop(s.plannedDeparture + ret));
                    solo.customerCount = 1;
                    solo.totalTime = s.plannedDeparture + ret;
                    solo.totalDistance = ctx.distanceMatrix[0, custIdx + 1] +
                                        ctx.distanceMatrix[custIdx + 1, 0];

                    routes.Add(solo);
                    DLog.Warn("General",$"[Router SafetyNet] EMERGENCY solo route for " +
                                    $"C{order.customerNumber:D3}");
                }
                stillUnrouted.Clear();
            }
        }

        int finalCount = routes.Sum(r => r.DeliveryStopCount);
        DLog.Info("Router", $" Final: {finalCount}/{ctx.orders.Count} customers " +
                $"in {routes.Count} routes");

        return routes;
    }

    // ================================================================
    //  Helpers for SafetyNet
    // ================================================================

    private void RecalcTiming(PlannedRoute route, RoutingContext ctx)
    {
        if (route.stops.Count < 2) return;
        route.stops[0].plannedArrival = 0;
        route.stops[0].plannedDeparture = 0;

        for (int i = 1; i < route.stops.Count; i++)
        {
            int prev = GetMatrixIndex(route.stops[i - 1], ctx);
            int curr = GetMatrixIndex(route.stops[i], ctx);
            float travel = ctx.timeMatrix[prev, curr];
            float arrival = route.stops[i - 1].plannedDeparture + travel;

            route.stops[i].plannedArrival = arrival;

            if (route.stops[i].type == RouteStop.StopType.Delivery &&
                route.stops[i].order != null)
            {
                var order = route.stops[i].order;
                route.stops[i].waitUntil = order.readyTime;
                route.stops[i].serviceStart = Mathf.Max(arrival, order.readyTime);
                route.stops[i].serviceEnd =
                    route.stops[i].serviceStart + order.serviceTime;
                route.stops[i].plannedDeparture = route.stops[i].serviceEnd;
                route.stops[i].wasLate = arrival > order.dueTime;
            }
            else
            {
                route.stops[i].plannedDeparture = arrival;
            }
        }

        route.totalTime = route.stops[route.stops.Count - 1].plannedArrival;
    }

    private float CalcRouteDistance(PlannedRoute route, RoutingContext ctx)
    {
        float total = 0;
        for (int i = 0; i < route.stops.Count - 1; i++)
        {
            int a = GetMatrixIndex(route.stops[i], ctx);
            int b = GetMatrixIndex(route.stops[i + 1], ctx);
            total += ctx.distanceMatrix[a, b];
        }
        return total;
    }

    private int GetMatrixIndex(RouteStop stop, RoutingContext ctx)
    {
        if (stop.type == RouteStop.StopType.Depot) return 0;
        if (stop.order == null) return 0;
        int idx = ctx.orders.IndexOf(stop.order);
        return idx >= 0 ? idx + 1 : 0;
    }

    // ================================================================
    //  Solver Access
    // ================================================================

    private IRoutingSolver GetActiveSolver()
    {
        if (SolverRegistry.Instance != null)
            return SolverRegistry.Instance.ActiveSolver;

        // Fallback: create inline Solomon solver
        DLog.Warn("General","[Router] SolverRegistry not found, using fallback SolomonI1");
        return new SolomonI1Solver();
    }

    private void ApplySolomonParams(IRoutingSolver solver)
    {
        if (solver is SolomonI1Solver solomon)
        {
            switch (strategy)
            {
                case RoutingStrategy.Efficiency:
                    solomon.alpha1 = 0.3f;
                    solomon.mu = 0.6f;
                    solomon.lambda = 1.5f;
                    break;
                case RoutingStrategy.Economy:
                    solomon.alpha1 = 0.7f;
                    solomon.mu = 0.9f;
                    solomon.lambda = 0.5f;
                    break;
                default: // Balanced
                    solomon.alpha1 = 0.5f;
                    solomon.mu = 0.8f;
                    solomon.lambda = 1.0f;
                    break;
            }
        }
    }

    // ================================================================
    //  Context Builder
    // ================================================================

    private RoutingContext BuildContext(
        List<DeliveryOrder> pending,
        List<DroneInfo.Snapshot> fleet,
        double3 depotLLH,
        int vehicleCapacity,
        float speedMps)
    {
        // Build point list: depot + all customers
        var allPoints = new List<double3> { depotLLH };
        foreach (var order in pending)
            allPoints.Add(order.deliveryLLH);

        // Build distance matrix
        int n = allPoints.Count;
        var distMatrix = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                float dist = (float)RoutingContext.GeoDistance(allPoints[i], allPoints[j]);
                distMatrix[i, j] = dist;
                distMatrix[j, i] = dist;
            }
        }

        // Build time matrix
        var timeMatrix = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                timeMatrix[i, j] = distMatrix[i, j] / speedMps;

        return new RoutingContext
        {
            depotLLH = depotLLH,
            orders = pending,
            vehicleCapacity = vehicleCapacity,
            maxVehicles = fleet.Count,
            speedMps = speedMps,
            distanceMatrix = distMatrix,
            timeMatrix = timeMatrix
        };
    }

    // ================================================================
    //  Drone Assignment (unchanged)
    // ================================================================

    private void AssignDroneNames(List<PlannedRoute> routes, List<DroneInfo.Snapshot> fleet)
    {
        var available = fleet
            .Where(f => f.isIdle)
            .OrderBy(f => f.name)
            .ToList();

        if (available.Count < routes.Count)
            available = fleet.OrderBy(f => f.name).ToList();

        for (int i = 0; i < routes.Count; i++)
        {
            routes[i].droneName = i < available.Count
                ? available[i].name
                : $"V{(i + 1):D2}";
        }
    }

    // ================================================================
    //  Speed & Strategy
    // ================================================================

    public float GetPlanningSpeed()
    {
        switch (strategy)
        {
            case RoutingStrategy.Efficiency: return efficiencySpeedMps;
            case RoutingStrategy.Economy: return economySpeedMps;
            default: return defaultSpeedMps;
        }
    }

    // ================================================================
    //  Summary
    // ================================================================

    public string GetSolutionSummary()
    {
        if (_lastSolution == null || _lastSolution.Count == 0)
            return "No routes planned yet.";

        string solverName = SolverRegistry.Instance?.ActiveSolver?.Name ?? "Unknown";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Routing Solution ===");
        sb.AppendLine($"Solver: {solverName}");
        sb.AppendLine($"Speed mode: {strategy}");
        sb.AppendLine($"Routes: {_lastSolution.Count}, Unrouted: {_lastUnrouted}");
        sb.AppendLine();

        foreach (var route in _lastSolution)
            sb.AppendLine(route.ToShortString());

        int totalCustomers = _lastSolution.Sum(r => r.customerCount);
        float totalDist = _lastSolution.Sum(r => r.totalDistance);
        float totalTime = _lastSolution.Max(r => r.totalTime);

        sb.AppendLine();
        sb.AppendLine($"Total customers: {totalCustomers}");
        sb.AppendLine($"Total distance: {totalDist:F0}m ({totalDist / 1000f:F1}km)");
        sb.AppendLine($"Makespan: {totalTime:F0} time units");

        return sb.ToString();
    }

    private void EmitStatus(string msg)
    {
        Debug.Log(msg);
        OnStatus?.Invoke(msg);
    }
}