// Assets/Scripts/Routing/Solvers/NearestFirstSolver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Nearest-Neighbor heuristic for VRPTW.
///
/// Phase 1 (Strict): Always visit the nearest unserved customer that
///   satisfies both capacity and time window constraints.
/// Phase 2 (Relaxed): For any remaining unrouted customers, relax
///   time window constraints and insert using cheapest insertion.
/// Phase 3 (Emergency): Create solo routes for any still-unrouted customers.
///
/// 100% customer coverage guaranteed.
/// </summary>
public class NearestFirstSolver : IRoutingSolver
{
    public string Name => "Nearest Neighbor";

    public string Description =>
        "Greedy nearest-neighbor heuristic with guaranteed 100% coverage. " +
        "Phase 1: strict TW. Phase 2: relaxed insertion. Phase 3: emergency solo routes.";

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>();

        for (int i = 0; i < ctx.orders.Count; i++)
            unrouted.Add(i);

        int n = ctx.orders.Count;

        Debug.Log($"[NearestFirst] Starting: {n} customers, " +
                  $"{ctx.maxVehicles} vehicles, cap={ctx.vehicleCapacity}, " +
                  $"speed={ctx.speedMps:F1} m/s");

        // ══════════════════════════════════════════
        //  Phase 1: Strict TW — original NN logic
        // ══════════════════════════════════════════
        int vehicleNum = 0;

        while (unrouted.Count > 0 && vehicleNum < ctx.maxVehicles)
        {
            vehicleNum++;

            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };
            route.stops.Add(ctx.MakeDepotStop(0));

            int currentMI = 0;
            float currentTime = 0f;

            bool found = true;
            while (found && unrouted.Count > 0)
            {
                found = false;
                int bestIdx = -1;
                float bestDist = float.MaxValue;

                foreach (int idx in unrouted)
                {
                    var order = ctx.orders[idx];
                    int mIdx = idx + 1;

                    // Capacity check
                    if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                        continue;

                    // Strict TW check
                    float travel = ctx.timeMatrix[currentMI, mIdx];
                    float arrival = currentTime + travel;
                    if (arrival > order.dueTime)
                        continue;

                    // Pick nearest
                    float dist = ctx.distanceMatrix[currentMI, mIdx];
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = idx;
                    }
                }

                if (bestIdx >= 0)
                {
                    var order = ctx.orders[bestIdx];
                    int mIdx = bestIdx + 1;

                    var stop = ctx.MakeDeliveryStop(order);
                    float travel = ctx.timeMatrix[currentMI, mIdx];
                    float arrival = currentTime + travel;
                    float svcStart = Mathf.Max(arrival, order.readyTime);

                    stop.plannedArrival = arrival;
                    stop.waitUntil = order.readyTime;
                    stop.serviceStart = svcStart;
                    stop.serviceEnd = svcStart + order.serviceTime;
                    stop.plannedDeparture = stop.serviceEnd;
                    stop.wasLate = arrival > order.dueTime;

                    route.stops.Add(stop);
                    route.totalDemand += order.demand;

                    currentMI = mIdx;
                    currentTime = stop.plannedDeparture;

                    unrouted.Remove(bestIdx);
                    found = true;
                }
            }

            // Close route
            float returnTravel = ctx.timeMatrix[currentMI, 0];
            route.stops.Add(ctx.MakeDepotStop(currentTime + returnTravel));

            route.customerCount = route.DeliveryStopCount;
            route.totalTime = currentTime + returnTravel;
            route.totalDistance = ComputeRouteDistance(route, ctx);

            if (route.customerCount > 0)
                routes.Add(route);
        }

        int phase1Routed = n - unrouted.Count;
        Debug.Log($"[NearestFirst] Phase 1 complete: {phase1Routed}/{n} routed, " +
                  $"{unrouted.Count} remaining, {routes.Count} routes");

        // ══════════════════════════════════════════
        //  Phase 2: Relaxed — insert remaining into
        //  existing routes (ignore TW, respect capacity)
        // ══════════════════════════════════════════
        if (unrouted.Count > 0)
        {
            int inserted = InsertIntoExistingRoutes(routes, ctx, unrouted);
            Debug.Log($"[NearestFirst] Phase 2: inserted {inserted} into existing routes, " +
                      $"{unrouted.Count} remaining");
        }

        // ══════════════════════════════════════════
        //  Phase 3: Create new routes for remaining
        //  (no vehicle limit — guarantee coverage)
        // ══════════════════════════════════════════
        if (unrouted.Count > 0)
        {
            int newRoutes = CreateRoutesForRemaining(routes, ctx, unrouted);
            Debug.Log($"[NearestFirst] Phase 3: created {newRoutes} new routes, " +
                      $"{unrouted.Count} remaining");
        }

        // ══════════════════════════════════════════
        //  Phase 4: Emergency solo routes
        //  (absolute last resort — should never reach here)
        // ══════════════════════════════════════════
        if (unrouted.Count > 0)
        {
            Debug.LogWarning($"[NearestFirst] Phase 4 EMERGENCY: " +
                             $"{unrouted.Count} still unrouted, creating solo routes");

            var toRemove = new List<int>(unrouted);
            foreach (int idx in toRemove)
            {
                var order = ctx.orders[idx];
                var solo = new PlannedRoute
                {
                    vehicleCapacity = ctx.vehicleCapacity,
                    totalDemand = order.demand
                };

                solo.stops.Add(ctx.MakeDepotStop(0));

                int mIdx = idx + 1;
                float travel = ctx.timeMatrix[0, mIdx];
                var stop = ctx.MakeDeliveryStop(order);
                stop.plannedArrival = travel;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = Mathf.Max(travel, order.readyTime);
                stop.serviceEnd = stop.serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = travel > order.dueTime;
                solo.stops.Add(stop);

                float returnT = ctx.timeMatrix[mIdx, 0];
                solo.stops.Add(ctx.MakeDepotStop(stop.plannedDeparture + returnT));

                solo.customerCount = 1;
                solo.totalDemand = order.demand;
                solo.totalDistance = ctx.distanceMatrix[0, mIdx] +
                                    ctx.distanceMatrix[mIdx, 0];
                solo.totalTime = stop.plannedDeparture + returnT;

                routes.Add(solo);
                unrouted.Remove(idx);
            }
        }

        // ══════════════════════════════════════════
        //  Final verification + statistics
        // ══════════════════════════════════════════
        int totalRouted = routes.Sum(r => r.DeliveryStopCount);
        int onTime = CountOnTime(routes);
        float totalDist = routes.Sum(r => r.totalDistance);
        float makespan = routes.Count > 0 ? routes.Max(r => r.totalTime) : 0;

        Debug.Log($"[NearestFirst] COMPLETE:" +
                  $"\\n  Customers: {totalRouted}/{n} " +
                  $"({(totalRouted >= n ? "100%" : "MISSING!")})" +
                  $"\\n  Routes: {routes.Count}" +
                  $"\\n  On-time: {onTime}/{totalRouted} " +
                  $"({(totalRouted > 0 ? (float)onTime / totalRouted * 100f : 0):F1}%)" +
                  $"\\n  Distance: {totalDist:F0}m ({totalDist / 1000f:F1}km)" +
                  $"\\n  Makespan: {makespan:F1}");

        if (totalRouted < n)
            Debug.LogError($"[NearestFirst] FATAL: {n - totalRouted} customers lost!");

        return routes;
    }

    // ══════════════════════════════════════════════
    //  Phase 2: Insert into existing routes
    //  Cheapest insertion, capacity only (TW relaxed)
    // ══════════════════════════════════════════════

    private int InsertIntoExistingRoutes(List<PlannedRoute> routes,
                                         RoutingContext ctx,
                                         HashSet<int> unrouted)
    {
        int inserted = 0;
        var toRemove = new List<int>();

        foreach (int idx in unrouted)
        {
            var order = ctx.orders[idx];
            int newMI = idx + 1;

            float bestCost = float.MaxValue;
            PlannedRoute bestRoute = null;
            int bestPos = -1;

            foreach (var route in routes)
            {
                // Capacity check (always hard)
                if (route.totalDemand + order.demand > route.vehicleCapacity)
                    continue;

                // Try every insertion position (between existing stops)
                for (int pos = 1; pos < route.stops.Count; pos++)
                {
                    int prevMI = GetMatrixIndex(route.stops[pos - 1], ctx);
                    int nextMI = GetMatrixIndex(route.stops[pos], ctx);

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
                // Insert the stop
                var stop = ctx.MakeDeliveryStop(order);
                bestRoute.stops.Insert(bestPos, stop);
                bestRoute.totalDemand += order.demand;
                bestRoute.customerCount++;

                // Recalculate timing for entire route
                RecalcTiming(bestRoute, ctx);
                bestRoute.totalDistance = ComputeRouteDistance(bestRoute, ctx);
                bestRoute.totalTime =
                    bestRoute.stops[bestRoute.stops.Count - 1].plannedArrival;

                toRemove.Add(idx);
                inserted++;
            }
        }

        foreach (int idx in toRemove)
            unrouted.Remove(idx);

        return inserted;
    }

    // ══════════════════════════════════════════════
    //  Phase 3: Create new NN routes for remaining
    //  (relaxed TW — accept late deliveries)
    // ══════════════════════════════════════════════

    private int CreateRoutesForRemaining(List<PlannedRoute> routes,
                                          RoutingContext ctx,
                                          HashSet<int> unrouted)
    {
        int newRouteCount = 0;

        while (unrouted.Count > 0)
        {
            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };
            route.stops.Add(ctx.MakeDepotStop(0));

            int currentMI = 0;
            float currentTime = 0f;

            // NN greedy — capacity only, NO TW check
            bool found = true;
            while (found && unrouted.Count > 0)
            {
                found = false;
                int bestIdx = -1;
                float bestDist = float.MaxValue;

                foreach (int idx in unrouted)
                {
                    var order = ctx.orders[idx];

                    // Only check capacity
                    if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                        continue;

                    float dist = ctx.distanceMatrix[currentMI, idx + 1];
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = idx;
                    }
                }

                if (bestIdx >= 0)
                {
                    var order = ctx.orders[bestIdx];
                    int mIdx = bestIdx + 1;

                    var stop = ctx.MakeDeliveryStop(order);
                    float travel = ctx.timeMatrix[currentMI, mIdx];
                    float arrival = currentTime + travel;
                    float svcStart = Mathf.Max(arrival, order.readyTime);

                    stop.plannedArrival = arrival;
                    stop.waitUntil = order.readyTime;
                    stop.serviceStart = svcStart;
                    stop.serviceEnd = svcStart + order.serviceTime;
                    stop.plannedDeparture = stop.serviceEnd;
                    stop.wasLate = arrival > order.dueTime;

                    route.stops.Add(stop);
                    route.totalDemand += order.demand;

                    currentMI = mIdx;
                    currentTime = stop.plannedDeparture;

                    unrouted.Remove(bestIdx);
                    found = true;
                }
            }

            // Close route
            float returnTravel = ctx.timeMatrix[currentMI, 0];
            route.stops.Add(ctx.MakeDepotStop(currentTime + returnTravel));

            route.customerCount = route.DeliveryStopCount;
            route.totalTime = currentTime + returnTravel;
            route.totalDistance = ComputeRouteDistance(route, ctx);

            if (route.customerCount > 0)
            {
                routes.Add(route);
                newRouteCount++;
            }
            else
            {
                // Safety: if no customer could be added (all exceed capacity),
                // break to avoid infinite loop
                break;
            }
        }

        return newRouteCount;
    }

    // ══════════════════════════════════════════════
    //  Timing Recalculation
    // ══════════════════════════════════════════════

    private void RecalcTiming(PlannedRoute route, RoutingContext ctx)
    {
        if (route.stops.Count < 2) return;

        route.stops[0].plannedArrival = 0;
        route.stops[0].plannedDeparture = 0;

        for (int i = 1; i < route.stops.Count; i++)
        {
            int prevMI = GetMatrixIndex(route.stops[i - 1], ctx);
            int currMI = GetMatrixIndex(route.stops[i], ctx);
            float travel = ctx.timeMatrix[prevMI, currMI];
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
    }

    // ══════════════════════════════════════════════
    //  Statistics
    // ══════════════════════════════════════════════

    private int CountOnTime(List<PlannedRoute> routes)
    {
        int count = 0;
        foreach (var r in routes)
            foreach (var s in r.stops)
                if (s.type == RouteStop.StopType.Delivery &&
                    s.order != null && !s.wasLate)
                    count++;
        return count;
    }

    // ══════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════

    private float ComputeRouteDistance(PlannedRoute route, RoutingContext ctx)
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
}