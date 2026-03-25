// Assets/Scripts/Routing/Solvers/FullCoverageSolver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Full Coverage Solver — GUARANTEES 100% customer routing.
///
/// Strategy: dead simple, impossible to lose customers.
///
/// Step 1: Sort all customers by deadline (earliest first)
/// Step 2: Pack them greedily into routes respecting capacity only
/// Step 3: Every customer gets a route, period.
/// </summary>
public class FullCoverageSolver : IRoutingSolver
{
    public string Name => "Full Coverage (Zero Loss)";
    public string Description =>
        "Guarantees 100% customer coverage with zero dropped orders. " +
        "Packs customers greedily by deadline order. " +
        "Capacity is hard; time windows are best-effort. " +
        "Simple and reliable — use this when other solvers drop customers.";

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        int n = ctx.orders.Count;
        Debug.Log($"[FullCoverage] START: {n} customers, " +
                  $"{ctx.maxVehicles} vehicles, cap={ctx.vehicleCapacity}, " +
                  $"speed={ctx.speedMps:F1}m/s");

        // ════════════════════════════════════════
        //  Step 1: Sort customers by deadline
        // ════════════════════════════════════════
        var sorted = Enumerable.Range(0, n)
            .OrderBy(i => ctx.orders[i].dueTime)
            .ThenBy(i => ctx.orders[i].readyTime)
            .ToList();

        // ════════════════════════════════════════
        //  Step 2: Build routes greedily
        // ════════════════════════════════════════
        var routes = new List<PlannedRoute>();
        var routed = new bool[n];
        int routedCount = 0;

        while (routedCount < n)
        {
            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };

            route.stops.Add(ctx.MakeDepotStop(0f));

            int lastMatrixIdx = 0;
            float currentTime = 0f;
            int addedThisRoute = 0;

            // Try to add customers in sorted order
            foreach (int custIdx in sorted)
            {
                if (routed[custIdx]) continue;

                var order = ctx.orders[custIdx];

                // Hard capacity check
                if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                    continue;

                int custMatrixIdx = custIdx + 1;
                float travelTime = ctx.distanceMatrix[lastMatrixIdx, custMatrixIdx] / ctx.speedMps;
                float arrival = currentTime + travelTime;

                // Use ctx.MakeDeliveryStop to avoid readonly property issues
                var stop = ctx.MakeDeliveryStop(order);
                stop.plannedArrival = arrival;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = Mathf.Max(arrival, order.readyTime);
                stop.serviceEnd = stop.serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = arrival > order.dueTime;

                route.stops.Add(stop);
                route.totalDemand += order.demand;

                currentTime = stop.plannedDeparture;
                lastMatrixIdx = custMatrixIdx;

                routed[custIdx] = true;
                routedCount++;
                addedThisRoute++;
            }

            if (addedThisRoute == 0)
            {
                // Safety: force-add the first unrouted customer
                for (int i = 0; i < n; i++)
                {
                    if (!routed[i])
                    {
                        var order = ctx.orders[i];
                        int custMatrixIdx = i + 1;
                        float travelTime = ctx.distanceMatrix[0, custMatrixIdx] / ctx.speedMps;

                        var stop = ctx.MakeDeliveryStop(order);
                        stop.plannedArrival = travelTime;
                        stop.waitUntil = order.readyTime;
                        stop.serviceStart = Mathf.Max(travelTime, order.readyTime);
                        stop.serviceEnd = stop.serviceStart + order.serviceTime;
                        stop.plannedDeparture = stop.serviceEnd;
                        stop.wasLate = true;

                        route.stops.Add(stop);
                        route.totalDemand += order.demand;
                        currentTime = stop.plannedDeparture;
                        lastMatrixIdx = custMatrixIdx;

                        routed[i] = true;
                        routedCount++;
                        addedThisRoute++;
                        break;
                    }
                }
            }

            // Return to depot
            float returnTime = ctx.distanceMatrix[lastMatrixIdx, 0] / ctx.speedMps;
            route.stops.Add(ctx.MakeDepotStop(currentTime + returnTime));

            // Finalize
            route.customerCount = addedThisRoute;
            route.totalTime = currentTime + returnTime;
            route.totalDistance = ComputeDistance(route, ctx);

            routes.Add(route);

            Debug.Log($"[FullCoverage] Route {routes.Count}: " +
                      $"{addedThisRoute} customers, demand={route.totalDemand}, " +
                      $"time={route.totalTime:F1}, dist={route.totalDistance:F0}m");
        }

        // ════════════════════════════════════════
        //  Step 3: Verify — MUST be 100%
        // ════════════════════════════════════════
        int totalRouted = routes.Sum(r => r.customerCount);
        int lateCount = routes.Sum(r =>
            r.stops.Count(s => s.type == RouteStop.StopType.Delivery && s.wasLate));
        int onTimeCount = totalRouted - lateCount;

        if (totalRouted != n)
        {
            Debug.LogError($"[FullCoverage] BUG! Routed {totalRouted}/{n}.");

            for (int i = 0; i < n; i++)
            {
                if (!routed[i])
                {
                    Debug.LogError($"[FullCoverage] EMERGENCY: C{ctx.orders[i].customerNumber:D3} missed!");

                    var emergRoute = new PlannedRoute
                    {
                        vehicleCapacity = ctx.vehicleCapacity,
                        totalDemand = ctx.orders[i].demand
                    };
                    emergRoute.stops.Add(ctx.MakeDepotStop(0));

                    float travel = ctx.distanceMatrix[0, i + 1] / ctx.speedMps;
                    var stop = ctx.MakeDeliveryStop(ctx.orders[i]);
                    stop.plannedArrival = travel;
                    stop.waitUntil = ctx.orders[i].readyTime;
                    stop.serviceStart = Mathf.Max(travel, ctx.orders[i].readyTime);
                    stop.serviceEnd = stop.serviceStart + ctx.orders[i].serviceTime;
                    stop.plannedDeparture = stop.serviceEnd;
                    stop.wasLate = true;
                    emergRoute.stops.Add(stop);

                    float ret = ctx.distanceMatrix[i + 1, 0] / ctx.speedMps;
                    emergRoute.stops.Add(ctx.MakeDepotStop(stop.plannedDeparture + ret));
                    emergRoute.customerCount = 1;
                    emergRoute.totalTime = stop.plannedDeparture + ret;
                    emergRoute.totalDistance = ctx.distanceMatrix[0, i + 1] +
                                              ctx.distanceMatrix[i + 1, 0];
                    routes.Add(emergRoute);
                }
            }
        }

        Debug.Log($"[FullCoverage] COMPLETE:" +
                  $"\\n  Customers: {totalRouted}/{n}" +
                  $"\\n  Routes: {routes.Count}" +
                  $"\\n  On-time (planned): {onTimeCount}/{totalRouted} " +
                  $"({(totalRouted > 0 ? (float)onTimeCount / totalRouted * 100f : 0):F1}%)" +
                  $"\\n  Late (planned): {lateCount}" +
                  $"\\n  Total distance: {routes.Sum(r => r.totalDistance):F0}m");

        for (int r = 0; r < routes.Count; r++)
        {
            var stops = routes[r].stops
                .Where(s => s.type == RouteStop.StopType.Delivery)
                .Select(s => $"C{s.order?.customerNumber:D3}")
                .ToList();
            Debug.Log($"[FullCoverage] Route {r + 1} stops: " +
                      $"Depot → {string.Join(" → ", stops)} → Depot");
        }

        return routes;
    }

    private float ComputeDistance(PlannedRoute route, RoutingContext ctx)
    {
        float total = 0;
        for (int i = 0; i < route.stops.Count - 1; i++)
        {
            int a = StopToMatrixIdx(route.stops[i], ctx);
            int b = StopToMatrixIdx(route.stops[i + 1], ctx);
            total += ctx.distanceMatrix[a, b];
        }
        return total;
    }

    private int StopToMatrixIdx(RouteStop stop, RoutingContext ctx)
    {
        if (stop.type == RouteStop.StopType.Depot) return 0;
        if (stop.order == null) return 0;
        int idx = ctx.orders.IndexOf(stop.order);
        return idx >= 0 ? idx + 1 : 0;
    }
}