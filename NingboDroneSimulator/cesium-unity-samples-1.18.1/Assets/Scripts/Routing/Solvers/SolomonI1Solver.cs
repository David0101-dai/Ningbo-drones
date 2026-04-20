// Assets/Scripts/Routing/Solvers/SolomonI1Solver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Solomon's I1 Insertion Heuristic for VRPTW.
///
/// Enhanced version:
/// - Two-phase approach: strict feasibility first, then relaxed
/// - Allows soft time window violations in phase 2
/// - Improved seed selection for scattered distributions (RC-type)
/// - Guarantees all customers are routed
/// </summary>
public class SolomonI1Solver : IRoutingSolver
{
    public string Name => "Solomon I1 Insertion";
    public string Description =>
        "Classic insertion heuristic for VRPTW. " +
        "Builds routes by iteratively inserting the best feasible customer. " +
        "Two-phase: strict time windows first, then relaxed for remaining customers.";

    // Solomon parameters (configurable by VehicleRouter)
    public float alpha1 = 0.5f;
    public float mu = 0.8f;
    public float lambda = 1.0f;

    // Relaxation settings
    private const float SOFT_TW_PENALTY = 100f;     // Penalty per unit of TW violation
    private const float MAX_TW_VIOLATION = 50f;      // Max allowed violation in phase 2

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>();
        for (int i = 0; i < ctx.orders.Count; i++)
            unrouted.Add(i);

        int vehicleNum = 0;

        // ═══════════════════════════════════
        //  Phase 1: Strict feasibility
        // ═══════════════════════════════════
        DLog.Info("Solomon", $" Phase 1 (strict): {unrouted.Count} customers, " +
                  $"{ctx.maxVehicles} vehicles");

        while (unrouted.Count > 0 && vehicleNum < ctx.maxVehicles)
        {
            vehicleNum++;
            var route = BuildOneRoute(ctx, unrouted, strict: true);

            if (route != null && route.DeliveryStopCount > 0)
            {
                routes.Add(route);
                DLog.Info("Solomon", $" Phase 1 Route {vehicleNum}: " +
                          $"{route.DeliveryStopCount} customers, " +
                          $"demand={route.totalDemand}");
            }
            else
            {
                // Can't build any more strict routes
                vehicleNum--;
                break;
            }
        }

        int phase1Routed = ctx.orders.Count - unrouted.Count;
        DLog.Info("Solomon", $" Phase 1 complete: {phase1Routed}/{ctx.orders.Count} routed, " +
                  $"{unrouted.Count} remaining");

        // ═══════════════════════════════════
        //  Phase 2: Relaxed — route ALL remaining customers
        // ═══════════════════════════════════
        if (unrouted.Count > 0)
        {
            DLog.Info("Solomon", $" Phase 2 (relaxed): routing {unrouted.Count} remaining customers");

            // First try to insert into existing routes
            int insertedIntoExisting = TryInsertIntoExistingRoutes(routes, ctx, unrouted);
            DLog.Info("Solomon", $" Inserted {insertedIntoExisting} into existing routes");

            // Create new routes for the rest
            while (unrouted.Count > 0 && vehicleNum < ctx.maxVehicles * 2)
            {
                vehicleNum++;
                var route = BuildOneRoute(ctx, unrouted, strict: false);

                if (route != null && route.DeliveryStopCount > 0)
                {
                    routes.Add(route);
                    DLog.Info("Solomon", $" Phase 2 Route {vehicleNum}: " +
                              $"{route.DeliveryStopCount} customers, " +
                              $"demand={route.totalDemand}");
                }
                else
                {
                    break;
                }
            }

            // Last resort: force remaining into closest routes
            if (unrouted.Count > 0)
            {
                int forced = ForceInsertRemaining(routes, ctx, unrouted);
                DLog.Info("Solomon", $" Force-inserted {forced} remaining customers");
            }
        }

        if (unrouted.Count > 0)
            DLog.Error("General",$"[SolomonI1] STILL {unrouted.Count} customers unrouted after all phases!");
        else
            DLog.Info("Solomon", $" All {ctx.orders.Count} customers routed in {routes.Count} routes");

        return routes;
    }

    // ================================================================
    //  Build One Route
    // ================================================================

    private PlannedRoute BuildOneRoute(RoutingContext ctx, HashSet<int> unrouted, bool strict)
    {
        if (unrouted.Count == 0) return null;

        // Select seed
        int seedIdx = strict ? SelectSeedByDeadline(ctx, unrouted)
                             : SelectSeedByDistance(ctx, unrouted);
        if (seedIdx < 0) return null;

        var route = new PlannedRoute
        {
            vehicleCapacity = ctx.vehicleCapacity,
            totalDemand = 0
        };

        route.stops.Add(ctx.MakeDepotStop(0));
        InsertCustomer(route, 1, ctx.orders[seedIdx], seedIdx, ctx);
        unrouted.Remove(seedIdx);
        route.stops.Add(ctx.MakeDepotStop());

        // Iterative best insertion
        bool improved = true;
        while (improved && unrouted.Count > 0)
        {
            improved = false;
            int bestCustomer = -1;
            int bestPosition = -1;
            float bestC2 = float.MinValue;

            foreach (int uIdx in unrouted)
            {
                var order = ctx.orders[uIdx];

                // Capacity check (always hard)
                if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                    continue;

                for (int pos = 1; pos < route.stops.Count; pos++)
                {
                    bool feasible = strict
                        ? IsFeasibleStrict(route, pos, order, uIdx, ctx)
                        : IsFeasibleRelaxed(route, pos, order, uIdx, ctx);

                    if (!feasible) continue;

                    float c1 = ComputeC1(route, pos, uIdx, ctx);

                    // In relaxed mode, add penalty for TW violation
                    if (!strict)
                    {
                        float violation = EstimateViolation(route, pos, order, uIdx, ctx);
                        c1 += violation * SOFT_TW_PENALTY;
                    }

                    float c2 = lambda * ctx.distanceMatrix[0, uIdx + 1] - c1;

                    if (c2 > bestC2)
                    {
                        bestC2 = c2;
                        bestCustomer = uIdx;
                        bestPosition = pos;
                    }
                }
            }

            if (bestCustomer >= 0)
            {
                route.stops.RemoveAt(route.stops.Count - 1);
                InsertCustomer(route, bestPosition, ctx.orders[bestCustomer], bestCustomer, ctx);
                route.stops.Add(ctx.MakeDepotStop());
                unrouted.Remove(bestCustomer);
                improved = true;
            }
        }

        UpdateAllTiming(route, ctx);
        route.customerCount = route.DeliveryStopCount;
        route.totalDistance = ComputeTotalDistance(route, ctx);
        route.totalTime = route.stops.Count > 0
            ? route.stops[route.stops.Count - 1].plannedArrival : 0;

        return route;
    }

    // ================================================================
    //  Phase 2: Insert into Existing Routes
    // ================================================================

    private int TryInsertIntoExistingRoutes(List<PlannedRoute> routes,
                                            RoutingContext ctx,
                                            HashSet<int> unrouted)
    {
        int inserted = 0;
        var toRemove = new List<int>();

        foreach (int uIdx in unrouted)
        {
            var order = ctx.orders[uIdx];
            float bestCost = float.MaxValue;
            PlannedRoute bestRoute = null;
            int bestPos = -1;

            foreach (var route in routes)
            {
                if (route.totalDemand + order.demand > route.vehicleCapacity)
                    continue;

                for (int pos = 1; pos < route.stops.Count; pos++)
                {
                    if (!IsFeasibleRelaxed(route, pos, order, uIdx, ctx))
                        continue;

                    float cost = ComputeInsertionCost(route, pos, uIdx, ctx);
                    float violation = EstimateViolation(route, pos, order, uIdx, ctx);
                    cost += violation * SOFT_TW_PENALTY;

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
                bestRoute.stops.RemoveAt(bestRoute.stops.Count - 1);
                InsertCustomer(bestRoute, bestPos, order, uIdx, ctx);
                bestRoute.stops.Add(ctx.MakeDepotStop());
                UpdateAllTiming(bestRoute, ctx);
                bestRoute.customerCount = bestRoute.DeliveryStopCount;
                bestRoute.totalDistance = ComputeTotalDistance(bestRoute, ctx);

                toRemove.Add(uIdx);
                inserted++;
            }
        }

        foreach (var idx in toRemove)
            unrouted.Remove(idx);

        return inserted;
    }

    // ================================================================
    //  Last Resort: Force Insert
    // ================================================================

    private int ForceInsertRemaining(List<PlannedRoute> routes,
                                     RoutingContext ctx,
                                     HashSet<int> unrouted)
    {
        int inserted = 0;
        var toRemove = new List<int>();

        foreach (int uIdx in unrouted)
        {
            var order = ctx.orders[uIdx];

            // Find route with most remaining capacity, or create new
            PlannedRoute bestRoute = null;
            int bestPos = -1;
            float bestDist = float.MaxValue;

            foreach (var route in routes)
            {
                if (route.totalDemand + order.demand > route.vehicleCapacity)
                    continue;

                // Find best position by distance only (ignore time windows)
                for (int pos = 1; pos < route.stops.Count; pos++)
                {
                    float dist = ComputeInsertionCost(route, pos, uIdx, ctx);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestRoute = route;
                        bestPos = pos;
                    }
                }
            }

            if (bestRoute == null)
            {
                // Create a new route just for this customer
                bestRoute = new PlannedRoute
                {
                    vehicleCapacity = ctx.vehicleCapacity,
                    totalDemand = 0
                };
                bestRoute.stops.Add(ctx.MakeDepotStop(0));
                bestRoute.stops.Add(ctx.MakeDepotStop());
                bestPos = 1;
                routes.Add(bestRoute);
            }

            bestRoute.stops.RemoveAt(bestRoute.stops.Count - 1);
            InsertCustomer(bestRoute, bestPos, order, uIdx, ctx);
            bestRoute.stops.Add(ctx.MakeDepotStop());
            UpdateAllTiming(bestRoute, ctx);
            bestRoute.customerCount = bestRoute.DeliveryStopCount;
            bestRoute.totalDistance = ComputeTotalDistance(bestRoute, ctx);

            toRemove.Add(uIdx);
            inserted++;
        }

        foreach (var idx in toRemove)
            unrouted.Remove(idx);

        return inserted;
    }

    // ================================================================
    //  Seed Selection
    // ================================================================

    /// <summary>Original: earliest deadline, tie-break by farthest distance</summary>
    private int SelectSeedByDeadline(RoutingContext ctx, HashSet<int> unrouted)
    {
        int best = -1;
        float bestDue = float.MaxValue;
        float bestDist = -1;

        foreach (int idx in unrouted)
        {
            float due = ctx.orders[idx].dueTime;
            float dist = ctx.distanceMatrix[0, idx + 1];

            if (due < bestDue || (Mathf.Approximately(due, bestDue) && dist > bestDist))
            {
                bestDue = due;
                bestDist = dist;
                best = idx;
            }
        }
        return best;
    }

    /// <summary>Phase 2: farthest unrouted customer from depot</summary>
    private int SelectSeedByDistance(RoutingContext ctx, HashSet<int> unrouted)
    {
        int best = -1;
        float bestDist = -1;

        foreach (int idx in unrouted)
        {
            float dist = ctx.distanceMatrix[0, idx + 1];
            if (dist > bestDist)
            {
                bestDist = dist;
                best = idx;
            }
        }
        return best;
    }

    // ================================================================
    //  Feasibility Checks
    // ================================================================

    /// <summary>Strict: no time window violation allowed</summary>
    private bool IsFeasibleStrict(PlannedRoute route, int pos,
                                  DeliveryOrder order, int orderIdx,
                                  RoutingContext ctx)
    {
        if (route.totalDemand + order.demand > route.vehicleCapacity)
            return false;

        var temp = new List<RouteStop>(route.stops);
        temp.Insert(pos, ctx.MakeDeliveryStop(order));

        for (int i = pos; i < temp.Count; i++)
        {
            int prevIdx = MatrixIdx(temp[i - 1], ctx);
            int currIdx = MatrixIdx(temp[i], ctx);
            float travel = ctx.timeMatrix[prevIdx, currIdx];
            float arrival = temp[i - 1].plannedDeparture + travel;

            if (temp[i].type == RouteStop.StopType.Delivery && temp[i].order != null)
            {
                if (arrival > temp[i].order.dueTime)
                    return false; // Hard reject

                float svcStart = Mathf.Max(arrival, temp[i].order.readyTime);
                temp[i].plannedArrival = arrival;
                temp[i].serviceStart = svcStart;
                temp[i].serviceEnd = svcStart + temp[i].order.serviceTime;
                temp[i].plannedDeparture = temp[i].serviceEnd;
            }
            else
            {
                temp[i].plannedArrival = arrival;
                temp[i].plannedDeparture = arrival;
            }
        }
        return true;
    }

    /// <summary>Relaxed: allow time window violation up to MAX_TW_VIOLATION</summary>
    private bool IsFeasibleRelaxed(PlannedRoute route, int pos,
                                   DeliveryOrder order, int orderIdx,
                                   RoutingContext ctx)
    {
        // Capacity is always a hard constraint
        if (route.totalDemand + order.demand > route.vehicleCapacity)
            return false;

        var temp = new List<RouteStop>(route.stops);
        temp.Insert(pos, ctx.MakeDeliveryStop(order));

        for (int i = pos; i < temp.Count; i++)
        {
            int prevIdx = MatrixIdx(temp[i - 1], ctx);
            int currIdx = MatrixIdx(temp[i], ctx);
            float travel = ctx.timeMatrix[prevIdx, currIdx];
            float arrival = temp[i - 1].plannedDeparture + travel;

            if (temp[i].type == RouteStop.StopType.Delivery && temp[i].order != null)
            {
                // Allow violation up to MAX_TW_VIOLATION
                float violation = arrival - temp[i].order.dueTime;
                if (violation > MAX_TW_VIOLATION)
                    return false;

                float svcStart = Mathf.Max(arrival, temp[i].order.readyTime);
                temp[i].plannedArrival = arrival;
                temp[i].serviceStart = svcStart;
                temp[i].serviceEnd = svcStart + temp[i].order.serviceTime;
                temp[i].plannedDeparture = temp[i].serviceEnd;
            }
            else
            {
                temp[i].plannedArrival = arrival;
                temp[i].plannedDeparture = arrival;
            }
        }
        return true;
    }

    /// <summary>Estimate how much TW violation inserting this customer would cause</summary>
    private float EstimateViolation(PlannedRoute route, int pos,
                                    DeliveryOrder order, int orderIdx,
                                    RoutingContext ctx)
    {
        int prevIdx = MatrixIdx(route.stops[pos - 1], ctx);
        int newIdx = orderIdx + 1;
        float travel = ctx.timeMatrix[prevIdx, newIdx];
        float arrival = route.stops[pos - 1].plannedDeparture + travel;
        float violation = Mathf.Max(0, arrival - order.dueTime);
        return violation;
    }

    // ================================================================
    //  Cost Computation
    // ================================================================

    private float ComputeC1(PlannedRoute route, int pos, int orderIdx,
                            RoutingContext ctx)
    {
        int prevIdx = MatrixIdx(route.stops[pos - 1], ctx);
        int nextIdx = pos < route.stops.Count
            ? MatrixIdx(route.stops[pos], ctx) : 0;
        int newIdx = orderIdx + 1;

        float diu = ctx.distanceMatrix[prevIdx, newIdx];
        float duj = ctx.distanceMatrix[newIdx, nextIdx];
        float dij = ctx.distanceMatrix[prevIdx, nextIdx];
        float c11 = diu + duj - mu * dij;

        float travelToNew = ctx.timeMatrix[prevIdx, newIdx];
        float arrivalNew = route.stops[pos - 1].plannedDeparture + travelToNew;
        var order = ctx.orders[orderIdx];
        float svcStart = Mathf.Max(arrivalNew, order.readyTime);
        float depart = svcStart + order.serviceTime;
        float travelNext = ctx.timeMatrix[newIdx, nextIdx];
        float newArrNext = depart + travelNext;
        float oldArrNext = route.stops[Mathf.Min(pos, route.stops.Count - 1)].plannedArrival;
        float c12 = newArrNext - oldArrNext;

        return alpha1 * c11 + (1f - alpha1) * c12;
    }

    private float ComputeInsertionCost(PlannedRoute route, int pos,
                                       int orderIdx, RoutingContext ctx)
    {
        int prevIdx = MatrixIdx(route.stops[pos - 1], ctx);
        int nextIdx = pos < route.stops.Count
            ? MatrixIdx(route.stops[pos], ctx) : 0;
        int newIdx = orderIdx + 1;

        return ctx.distanceMatrix[prevIdx, newIdx] +
               ctx.distanceMatrix[newIdx, nextIdx] -
               ctx.distanceMatrix[prevIdx, nextIdx];
    }

    // ================================================================
    //  Route Management
    // ================================================================

    private void InsertCustomer(PlannedRoute route, int pos,
                                DeliveryOrder order, int orderIdx,
                                RoutingContext ctx)
    {
        route.stops.Insert(pos, ctx.MakeDeliveryStop(order));
        route.totalDemand += order.demand;
        UpdateAllTiming(route, ctx);
    }

    private void UpdateAllTiming(PlannedRoute route, RoutingContext ctx)
    {
        if (route.stops.Count < 2) return;
        route.stops[0].plannedArrival = 0;
        route.stops[0].plannedDeparture = 0;

        for (int i = 1; i < route.stops.Count; i++)
        {
            int prev = MatrixIdx(route.stops[i - 1], ctx);
            int curr = MatrixIdx(route.stops[i], ctx);
            float travel = ctx.timeMatrix[prev, curr];
            float arrival = route.stops[i - 1].plannedDeparture + travel;

            route.stops[i].plannedArrival = arrival;

            if (route.stops[i].type == RouteStop.StopType.Delivery &&
                route.stops[i].order != null)
            {
                route.stops[i].waitUntil = route.stops[i].order.readyTime;
                route.stops[i].serviceStart =
                    Mathf.Max(arrival, route.stops[i].order.readyTime);
                route.stops[i].serviceEnd =
                    route.stops[i].serviceStart + route.stops[i].order.serviceTime;
                route.stops[i].plannedDeparture = route.stops[i].serviceEnd;
                route.stops[i].wasLate = arrival > route.stops[i].order.dueTime;
            }
            else
            {
                route.stops[i].plannedDeparture = arrival;
            }
        }
        route.totalTime = route.stops[route.stops.Count - 1].plannedArrival;
    }

    private float ComputeTotalDistance(PlannedRoute route, RoutingContext ctx)
    {
        float total = 0;
        for (int i = 0; i < route.stops.Count - 1; i++)
        {
            total += ctx.distanceMatrix[
                MatrixIdx(route.stops[i], ctx),
                MatrixIdx(route.stops[i + 1], ctx)];
        }
        return total;
    }

    private int MatrixIdx(RouteStop stop, RoutingContext ctx)
    {
        if (stop.type == RouteStop.StopType.Depot) return 0;
        if (stop.order == null) return 0;
        int idx = ctx.orders.IndexOf(stop.order);
        return idx >= 0 ? idx + 1 : 0;
    }
}