// Assets/Scripts/Routing/Solvers/GreedyInsertionSolver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Greedy Insertion Solver — guarantees 100% customer coverage.
///
/// Design principle: EVERY customer MUST be routed. No exceptions.
///
/// Algorithm:
///   Phase 1 — Greedy time-window-aware insertion
///     - Sort customers by deadline (earliest first)
///     - For each customer, find the cheapest feasible insertion
///       across all existing routes
///     - If no feasible insertion exists, open a new route
///     - "Feasible" = capacity OK + arrival ≤ dueTime
///
///   Phase 2 — Force insert (if Phase 1 leaves anyone behind)
///     - For remaining customers, insert into the position that
///       minimizes additional distance, ignoring time windows
///     - If no route has capacity, create overflow routes
///
///   Phase 3 — Local improvement (2-opt within each route)
///     - Try swapping consecutive customers to reduce distance
///
/// Guarantees:
///   ✅ All customers routed (hard guarantee)
///   ✅ Capacity constraint respected (hard guarantee)
///   ⚠️ Time windows: best-effort (soft — some may be late)
/// </summary>
public class GreedyInsertionSolver : IRoutingSolver
{
    public string Name => "Greedy Insertion (100% Coverage)";
    public string Description =>
        "Guarantees all customers are routed. " +
        "Phase 1: time-window-aware greedy insertion. " +
        "Phase 2: force-insert any remaining. " +
        "Phase 3: 2-opt local improvement. " +
        "Capacity is always respected; time windows are best-effort.";

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        Debug.Log($"[GreedyInsertion] Starting: {ctx.orders.Count} customers, " +
                  $"{ctx.maxVehicles} vehicles, capacity={ctx.vehicleCapacity}, " +
                  $"speed={ctx.speedMps:F1} m/s");

        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>();
        for (int i = 0; i < ctx.orders.Count; i++)
            unrouted.Add(i);

        // ═══════════════════════════════════════
        //  Phase 1: Greedy insertion with TW check
        // ═══════════════════════════════════════

        // Sort by deadline (tightest windows first)
        var sortedByDeadline = unrouted
            .OrderBy(i => ctx.orders[i].dueTime)
            .ThenBy(i => ctx.orders[i].readyTime)
            .ToList();

        foreach (int custIdx in sortedByDeadline)
        {
            if (!unrouted.Contains(custIdx)) continue;

            var order = ctx.orders[custIdx];

            // Try to insert into an existing route
            bool inserted = TryBestFeasibleInsertion(routes, custIdx, ctx, strict: true);

            if (!inserted)
            {
                // Open a new route with this customer as seed
                var route = CreateNewRoute(ctx);
                InsertAtPosition(route, 1, custIdx, ctx);
                FinalizeRoute(route, ctx);
                routes.Add(route);
            }

            unrouted.Remove(custIdx);
        }

        int phase1Count = ctx.orders.Count - unrouted.Count;
        int phase1OnTime = CountOnTimeInPlan(routes, ctx);
        Debug.Log($"[GreedyInsertion] Phase 1 done: {phase1Count}/{ctx.orders.Count} routed, " +
                  $"{routes.Count} routes, {phase1OnTime} planned on-time");

        // ═══════════════════════════════════════
        //  Phase 2: Force-insert remaining (relaxed TW)
        // ═══════════════════════════════════════
        if (unrouted.Count > 0)
        {
            Debug.Log($"[GreedyInsertion] Phase 2: force-inserting {unrouted.Count} remaining");

            // Sort remaining by distance from depot (farthest first — harder to fit later)
            var sortedByDist = unrouted
                .OrderByDescending(i => ctx.distanceMatrix[0, i + 1])
                .ToList();

            foreach (int custIdx in sortedByDist)
            {
                if (!unrouted.Contains(custIdx)) continue;

                // Try relaxed insertion (ignore TW, respect capacity only)
                bool inserted = TryBestFeasibleInsertion(routes, custIdx, ctx, strict: false);

                if (!inserted)
                {
                    // Create overflow route
                    var route = CreateNewRoute(ctx);
                    InsertAtPosition(route, 1, custIdx, ctx);
                    FinalizeRoute(route, ctx);
                    routes.Add(route);
                    Debug.Log($"[GreedyInsertion] Overflow route created for C{ctx.orders[custIdx].customerNumber:D3}");
                }

                unrouted.Remove(custIdx);
            }
        }

        // Verify 100% coverage
        Debug.Assert(unrouted.Count == 0,
            $"[GreedyInsertion] BUG: {unrouted.Count} customers still unrouted!");

        // ═══════════════════════════════════════
        //  Phase 3: Local improvement (2-opt)
        // ═══════════════════════════════════════
        int improvements = 0;
        foreach (var route in routes)
        {
            improvements += TwoOptImprove(route, ctx);
        }

        // ═══════════════════════════════════════
        //  Final statistics
        // ═══════════════════════════════════════
        int totalCustomers = routes.Sum(r => r.DeliveryStopCount);
        int totalOnTime = CountOnTimeInPlan(routes, ctx);
        float totalDist = routes.Sum(r => r.totalDistance);
        float makespan = routes.Count > 0 ? routes.Max(r => r.totalTime) : 0;

        Debug.Log($"[GreedyInsertion] COMPLETE:" +
                  $"\\n  Customers: {totalCustomers}/{ctx.orders.Count} (100%)" +
                  $"\\n  Routes: {routes.Count}" +
                  $"\\n  Planned on-time: {totalOnTime}/{totalCustomers} " +
                  $"({(totalCustomers > 0 ? (float)totalOnTime / totalCustomers * 100f : 0):F1}%)" +
                  $"\\n  Total distance: {totalDist:F0}m" +
                  $"\\n  Makespan: {makespan:F1} time units" +
                  $"\\n  2-opt improvements: {improvements}");

        return routes;
    }

    // ================================================================
    //  Core: Try Best Feasible Insertion
    // ================================================================

    /// <summary>
    /// Try to insert customer into the best position across all existing routes.
    /// Returns true if inserted, false if no feasible position found.
    /// </summary>
    private bool TryBestFeasibleInsertion(List<PlannedRoute> routes, int custIdx,
                                           RoutingContext ctx, bool strict)
    {
        var order = ctx.orders[custIdx];

        float bestCost = float.MaxValue;
        PlannedRoute bestRoute = null;
        int bestPos = -1;

        foreach (var route in routes)
        {
            // Hard capacity check
            if (route.totalDemand + order.demand > route.vehicleCapacity)
                continue;

            // Try every insertion position (between each pair of stops)
            for (int pos = 1; pos < route.stops.Count; pos++)
            {
                // Check feasibility
                if (strict && !CheckInsertionFeasible(route, pos, custIdx, ctx))
                    continue;

                // Compute cost = extra distance + time window penalty
                float cost = ComputeInsertionCost(route, pos, custIdx, ctx);

                if (!strict)
                {
                    // Add soft penalty for TW violation (prefer less violation)
                    float violation = EstimateTWViolation(route, pos, custIdx, ctx);
                    cost += violation * 10f;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestRoute = route;
                    bestPos = pos;
                }
            }
        }

        if (bestRoute != null && bestPos >= 0)
        {
            // Remove trailing depot, insert, re-add depot
            bestRoute.stops.RemoveAt(bestRoute.stops.Count - 1);
            InsertAtPosition(bestRoute, bestPos, custIdx, ctx);
            FinalizeRoute(bestRoute, ctx);
            return true;
        }

        return false;
    }

    // ================================================================
    //  Feasibility Check (strict mode)
    // ================================================================

    /// <summary>
    /// Check if inserting customer at position doesn't violate any
    /// time window for subsequent stops.
    /// </summary>
    private bool CheckInsertionFeasible(PlannedRoute route, int pos,
                                        int custIdx, RoutingContext ctx)
    {
        var order = ctx.orders[custIdx];

        // Simulate the insertion
        var tempStops = new List<RouteStop>(route.stops);
        tempStops.Insert(pos, ctx.MakeDeliveryStop(order));

        // Check timing from insertion point onwards
        for (int i = pos; i < tempStops.Count; i++)
        {
            int prevMI = GetMatrixIndex(tempStops[i - 1], ctx);
            int currMI = GetMatrixIndex(tempStops[i], ctx);
            float travel = ctx.timeMatrix[prevMI, currMI];

            float prevDepart;
            if (i == 1)
            {
                prevDepart = 0; // Depot departure
            }
            else
            {
                var prev = tempStops[i - 1];
                if (prev.type == RouteStop.StopType.Delivery && prev.order != null)
                {
                    float prevArr = (i - 1 == pos)
                        ? EstimateArrival(tempStops, i - 1, ctx)
                        : prev.plannedArrival;
                    float prevSvcStart = Mathf.Max(prevArr, prev.order.readyTime);
                    prevDepart = prevSvcStart + prev.order.serviceTime;
                }
                else
                {
                    prevDepart = prev.plannedDeparture;
                }
            }

            float arrival = prevDepart + travel;

            // Check time window
            if (tempStops[i].type == RouteStop.StopType.Delivery &&
                tempStops[i].order != null)
            {
                if (arrival > tempStops[i].order.dueTime)
                    return false; // Would make this stop late
            }
        }

        return true;
    }

    private float EstimateArrival(List<RouteStop> stops, int index, RoutingContext ctx)
    {
        float time = 0;
        for (int i = 1; i <= index; i++)
        {
            int prev = GetMatrixIndex(stops[i - 1], ctx);
            int curr = GetMatrixIndex(stops[i], ctx);
            time += ctx.timeMatrix[prev, curr];

            if (stops[i].type == RouteStop.StopType.Delivery && stops[i].order != null)
            {
                time = Mathf.Max(time, stops[i].order.readyTime);
                time += stops[i].order.serviceTime;
            }
        }
        // Return arrival (before service) at the index
        // We need just the arrival, so subtract back the service
        if (stops[index].type == RouteStop.StopType.Delivery && stops[index].order != null)
            time -= stops[index].order.serviceTime;

        return time;
    }

    // ================================================================
    //  Cost Computation
    // ================================================================

    /// <summary>Extra distance caused by inserting customer at position</summary>
    private float ComputeInsertionCost(PlannedRoute route, int pos,
                                        int custIdx, RoutingContext ctx)
    {
        int prevMI = GetMatrixIndex(route.stops[pos - 1], ctx);
        int nextMI = GetMatrixIndex(route.stops[Mathf.Min(pos, route.stops.Count - 1)], ctx);
        int newMI = custIdx + 1;

        float distBefore = ctx.distanceMatrix[prevMI, nextMI];
        float distAfter = ctx.distanceMatrix[prevMI, newMI] +
                          ctx.distanceMatrix[newMI, nextMI];

        return distAfter - distBefore;
    }

    /// <summary>Estimate TW violation if customer inserted at position</summary>
    private float EstimateTWViolation(PlannedRoute route, int pos,
                                      int custIdx, RoutingContext ctx)
    {
        var order = ctx.orders[custIdx];
        int prevMI = GetMatrixIndex(route.stops[pos - 1], ctx);
        int newMI = custIdx + 1;

        float prevDepart = route.stops[pos - 1].plannedDeparture;
        float travel = ctx.timeMatrix[prevMI, newMI];
        float arrival = prevDepart + travel;

        return Mathf.Max(0, arrival - order.dueTime);
    }

    // ================================================================
    //  Route Creation & Management
    // ================================================================

    private PlannedRoute CreateNewRoute(RoutingContext ctx)
    {
        var route = new PlannedRoute
        {
            vehicleCapacity = ctx.vehicleCapacity,
            totalDemand = 0
        };
        route.stops.Add(ctx.MakeDepotStop(0));
        route.stops.Add(ctx.MakeDepotStop());
        return route;
    }

    private void InsertAtPosition(PlannedRoute route, int pos,
                                   int custIdx, RoutingContext ctx)
    {
        var order = ctx.orders[custIdx];
        route.stops.Insert(pos, ctx.MakeDeliveryStop(order));
        route.totalDemand += order.demand;
    }

    /// <summary>Re-add trailing depot and update all timing</summary>
    private void FinalizeRoute(PlannedRoute route, RoutingContext ctx)
    {
        // Ensure route ends with depot
        if (route.stops.Count == 0 ||
            route.stops[route.stops.Count - 1].type != RouteStop.StopType.Depot)
        {
            route.stops.Add(ctx.MakeDepotStop());
        }

        UpdateTiming(route, ctx);
        route.customerCount = route.DeliveryStopCount;
        route.totalDistance = ComputeRouteDistance(route, ctx);
        route.totalTime = route.stops.Count > 0
            ? route.stops[route.stops.Count - 1].plannedArrival : 0;
    }

    private void UpdateTiming(PlannedRoute route, RoutingContext ctx)
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
    }

    // ================================================================
    //  Phase 3: 2-opt Local Improvement
    // ================================================================

    /// <summary>
    /// Try swapping pairs of delivery stops within a route to reduce distance.
    /// Returns number of improvements made.
    /// </summary>
    private int TwoOptImprove(PlannedRoute route, RoutingContext ctx)
    {
        int improvements = 0;
        bool improved = true;

        while (improved)
        {
            improved = false;

            // Only swap delivery stops (indices 1 to Count-2, skipping depots)
            for (int i = 1; i < route.stops.Count - 2; i++)
            {
                if (route.stops[i].type != RouteStop.StopType.Delivery) continue;

                for (int j = i + 1; j < route.stops.Count - 1; j++)
                {
                    if (route.stops[j].type != RouteStop.StopType.Delivery) continue;

                    float currentDist = SegmentDistance(route, i, j, ctx);

                    // Try swap
                    var temp = route.stops[i];
                    route.stops[i] = route.stops[j];
                    route.stops[j] = temp;

                    UpdateTiming(route, ctx);
                    float newDist = SegmentDistance(route, i, j, ctx);

                    // Check if swap is beneficial and doesn't cause
                    // significantly more TW violations
                    int newViolations = CountViolationsInRange(route, i, j);

                    // Swap back
                    temp = route.stops[i];
                    route.stops[i] = route.stops[j];
                    route.stops[j] = temp;
                    UpdateTiming(route, ctx);

                    int oldViolations = CountViolationsInRange(route, i, j);

                    // Accept if: shorter distance AND no more violations
                    // OR: same/fewer violations AND shorter distance
                    if (newDist < currentDist - 1f && newViolations <= oldViolations)
                    {
                        // Apply the swap for real
                        temp = route.stops[i];
                        route.stops[i] = route.stops[j];
                        route.stops[j] = temp;
                        UpdateTiming(route, ctx);

                        improvements++;
                        improved = true;
                    }
                }
            }
        }

        if (improvements > 0)
        {
            route.totalDistance = ComputeRouteDistance(route, ctx);
            route.totalTime = route.stops.Count > 0
                ? route.stops[route.stops.Count - 1].plannedArrival : 0;
        }

        return improvements;
    }

    private float SegmentDistance(PlannedRoute route, int from, int to,
                                  RoutingContext ctx)
    {
        float dist = 0;
        for (int i = from - 1; i <= to; i++)
        {
            if (i + 1 < route.stops.Count)
            {
                int a = GetMatrixIndex(route.stops[i], ctx);
                int b = GetMatrixIndex(route.stops[i + 1], ctx);
                dist += ctx.distanceMatrix[a, b];
            }
        }
        return dist;
    }

    private int CountViolationsInRange(PlannedRoute route, int from, int to)
    {
        int count = 0;
        for (int i = from; i <= to && i < route.stops.Count; i++)
        {
            if (route.stops[i].wasLate) count++;
        }
        return count;
    }

    // ================================================================
    //  Statistics
    // ================================================================

    private int CountOnTimeInPlan(List<PlannedRoute> routes, RoutingContext ctx)
    {
        int count = 0;
        foreach (var route in routes)
        {
            foreach (var stop in route.stops)
            {
                if (stop.type == RouteStop.StopType.Delivery &&
                    stop.order != null && !stop.wasLate)
                    count++;
            }
        }
        return count;
    }

    // ================================================================
    //  Helpers
    // ================================================================

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