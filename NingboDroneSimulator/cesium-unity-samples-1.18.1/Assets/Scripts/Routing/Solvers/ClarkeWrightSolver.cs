// Assets/Scripts/Routing/Solvers/ClarkeWrightSolver.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Clarke-Wright Savings Algorithm for VRPTW.
/// Starts with one route per customer, then iteratively merges
/// routes that yield the greatest distance savings.
///
/// GUARANTEED 100% customer coverage:
/// - When route count exceeds maxVehicles, excess routes are
///   merged into existing routes using cheapest insertion
///   rather than being discarded.
/// </summary>
public class ClarkeWrightSolver : IRoutingSolver
{
    public string Name => "Clarke-Wright Savings";
    public string Description =>
        "Savings-based merging heuristic.\\n" +
        "Starts with one route per customer, then merges route pairs\\n" +
        "that save the most distance while respecting capacity and time windows.\\n" +
        "100% customer coverage guaranteed.";

    private struct Saving : IComparable<Saving>
    {
        public int i, j;
        public float value;
        public int CompareTo(Saving other) => other.value.CompareTo(value);
    }

    private class CWRoute
    {
        public List<int> customers = new();
        public int totalDemand;
        public bool merged;
    }

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        int n = ctx.orders.Count;
        if (n == 0) return new List<PlannedRoute>();

        DLog.Info("CW", $" Starting: {n} customers, " +
                  $"{ctx.maxVehicles} vehicles, cap={ctx.vehicleCapacity}, " +
                  $"speed={ctx.speedMps:F1} m/s");

        // ══════════════════════════════════════════
        //  Step 1: Compute savings
        // ══════════════════════════════════════════
        var savings = new List<Saving>();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                int mi = i + 1;
                int mj = j + 1;
                float sij = ctx.distanceMatrix[0, mi] + ctx.distanceMatrix[0, mj]
                          - ctx.distanceMatrix[mi, mj];
                if (sij > 0)
                {
                    savings.Add(new Saving { i = i, j = j, value = sij });
                    savings.Add(new Saving { i = j, j = i, value = sij });
                }
            }
        }
        savings.Sort();

        // ══════════════════════════════════════════
        //  Step 2: One route per customer
        // ══════════════════════════════════════════
        var routes = new List<CWRoute>();
        var custToRoute = new int[n];

        for (int i = 0; i < n; i++)
        {
            var r = new CWRoute
            {
                customers = new List<int> { i },
                totalDemand = ctx.orders[i].demand
            };
            custToRoute[i] = routes.Count;
            routes.Add(r);
        }

    // ══════════════════════════════════════════
    //  Step 3: Merge using savings — FIXED
    //  Ensure merged route's customer list is
    //  properly maintained and original is cleared
    // ══════════════════════════════════════════
    foreach (var s in savings)
    {
        int ri = custToRoute[s.i];
        int rj = custToRoute[s.j];
        if (ri == rj) continue;

        var routeI = routes[ri];
        var routeJ = routes[rj];
        if (routeI.merged || routeJ.merged) continue;

        if (routeI.totalDemand + routeJ.totalDemand > ctx.vehicleCapacity)
            continue;

        List<int> mergedCustomers = null;

        if (routeI.customers.Last() == s.i && routeJ.customers.First() == s.j)
        {
            mergedCustomers = new List<int>(routeI.customers);
            mergedCustomers.AddRange(routeJ.customers);
        }
        else if (routeJ.customers.Last() == s.j && routeI.customers.First() == s.i)
        {
            mergedCustomers = new List<int>(routeJ.customers);
            mergedCustomers.AddRange(routeI.customers);
        }

        if (mergedCustomers == null) continue;
        if (!IsTimeFeasible(mergedCustomers, ctx)) continue;

        // ★ FIX: Update custToRoute for ALL customers in merged list
        routeI.customers = mergedCustomers;
        routeI.totalDemand += routeJ.totalDemand;

        // ★ FIX: Clear routeJ's customer list to prevent ghost references
        routeJ.customers.Clear();
        routeJ.totalDemand = 0;
        routeJ.merged = true;

        // Update mapping for all customers
        foreach (int c in mergedCustomers)
            custToRoute[c] = ri;
    }

        // ══════════════════════════════════════════
        //  Step 4: Collect active routes
        //  FIX: Do NOT discard excess routes.
        //  Instead, absorb them into existing routes.
        // ══════════════════════════════════════════
        var active = routes.Where(r => !r.merged && r.customers.Count > 0)
                           .OrderByDescending(r => r.totalDemand)
                           .ToList();

        DLog.Info("CW", $" After merging: {active.Count} routes " +
                  $"(limit={ctx.maxVehicles})");

        if (active.Count > ctx.maxVehicles)
        {
            active = AbsorbExcessRoutes(active, ctx);
        }

        // ══════════════════════════════════════════
        //  Step 5: Convert to PlannedRoute
        // ══════════════════════════════════════════
        var result = new List<PlannedRoute>();

        foreach (var cwRoute in active)
        {
            var pr = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = cwRoute.totalDemand
            };

            pr.stops.Add(ctx.MakeDepotStop(0));

            float currentTime = 0f;
            int prevMatrixIdx = 0;
            float totalDist = 0f;

            foreach (int custIdx in cwRoute.customers)
            {
                var order = ctx.orders[custIdx];
                int currMatrixIdx = custIdx + 1;

                float travel = ctx.timeMatrix[prevMatrixIdx, currMatrixIdx];
                currentTime += travel;
                totalDist += ctx.distanceMatrix[prevMatrixIdx, currMatrixIdx];

                float serviceStart = Mathf.Max(currentTime, order.readyTime);
                bool late = currentTime > order.dueTime;

                var stop = ctx.MakeDeliveryStop(order);
                stop.plannedArrival = currentTime;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = serviceStart;
                stop.serviceEnd = serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = late;

                pr.stops.Add(stop);

                currentTime = stop.plannedDeparture;
                prevMatrixIdx = currMatrixIdx;
            }

            float returnTravel = ctx.timeMatrix[prevMatrixIdx, 0];
            totalDist += ctx.distanceMatrix[prevMatrixIdx, 0];
            currentTime += returnTravel;

            pr.stops.Add(ctx.MakeDepotStop(currentTime));

            pr.totalDistance = totalDist;
            pr.totalTime = currentTime;
            pr.customerCount = cwRoute.customers.Count;

            result.Add(pr);
        }

        // ══════════════════════════════════════════
        //  Step 6: Final verification — 100% coverage
        // ══════════════════════════════════════════
        result = VerifyAndFixCoverage(result, ctx, n);

        // ══════════════════════════════════════════
        //  Statistics
        // ══════════════════════════════════════════
        int routed = result.Sum(r => r.customerCount);
        int unrouted = n - routed;
        int lateCount = result.Sum(r => r.stops.Count(s => s.wasLate));
        float totalDistAll = result.Sum(r => r.totalDistance);
        float makespan = result.Count > 0 ? result.Max(r => r.totalTime) : 0;
        int onTime = result.Sum(r => r.stops.Count(s =>
            s.type == RouteStop.StopType.Delivery && !s.wasLate));

        DLog.Info("CW", $" COMPLETE:" +
                  $"\\n  Routes: {result.Count}/{ctx.maxVehicles}" +
                  $"\\n  Customers: {routed}/{n} ({(unrouted > 0 ? "MISSING!" : "100%")})" +
                  $"\\n  On-time: {onTime}/{routed} " +
                  $"({(routed > 0 ? (float)onTime / routed * 100f : 0):F1}%)" +
                  $"\\n  Late: {lateCount}" +
                  $"\\n  Distance: {totalDistAll:F0}m ({totalDistAll / 1000f:F1}km)" +
                  $"\\n  Makespan: {makespan:F1}");

        if (unrouted > 0)
            DLog.Error("General",$"[Clarke-Wright] {unrouted} customers STILL unrouted — this should never happen!");

        return result;
    }

    // ══════════════════════════════════════════════
    //  Absorb Excess Routes (replaces old truncation)
    //
    //  When we have more routes than vehicles, take
    //  the smallest routes and insert their customers
    //  into the largest routes using cheapest insertion.
    //  If insertion is impossible, keep as overflow.
    // ══════════════════════════════════════════════
    private List<CWRoute> AbsorbExcessRoutes(List<CWRoute> active, RoutingContext ctx)
    {
        // ── Count all unique customers across all active routes ──
        var allCustomersSet = new HashSet<int>();
        foreach (var route in active)
            foreach (int c in route.customers)
                allCustomersSet.Add(c);
        int totalBefore = allCustomersSet.Count;

        DLog.Info("CW", $" AbsorbExcess: {active.Count} routes, " +
                $"{totalBefore} unique customers, limit={ctx.maxVehicles}");

        if (active.Count <= ctx.maxVehicles)
            return active;

        var keep = active.Take(ctx.maxVehicles).ToList();
        var excess = active.Skip(ctx.maxVehicles).ToList();

        // ── Collect customers already in keep routes ──
        var inKeep = new HashSet<int>();
        foreach (var route in keep)
            foreach (int c in route.customers)
                inKeep.Add(c);

        // ── Collect customers ONLY in excess routes (not already in keep) ──
        var toAbsorb = new List<int>();
        foreach (var route in excess)
            foreach (int c in route.customers)
                if (!inKeep.Contains(c))
                    toAbsorb.Add(c);

        // Remove any duplicates within toAbsorb itself
        toAbsorb = toAbsorb.Distinct().ToList();

        DLog.Info("CW", $" Customers in keep: {inKeep.Count}, " +
                $"to absorb: {toAbsorb.Count}, " +
                $"expected total: {inKeep.Count + toAbsorb.Count}");

        // ── Verify no customers are lost at this point ──
        if (inKeep.Count + toAbsorb.Count < totalBefore)
        {
            // Find truly missing customers
            var accounted = new HashSet<int>(inKeep);
            foreach (int c in toAbsorb) accounted.Add(c);

            foreach (int c in allCustomersSet)
            {
                if (!accounted.Contains(c))
                {
                    toAbsorb.Add(c);
                    DLog.Warn("General",$"[Clarke-Wright] Rescued lost customer " +
                                    $"C{ctx.orders[c].customerNumber:D3}");
                }
            }
        }

        int absorbed = 0;
        var phase2 = new List<int>();

        // ── Phase 1: Cheapest feasible insertion ──
        foreach (int custIdx in toAbsorb)
        {
            int demand = ctx.orders[custIdx].demand;
            float bestCost = float.MaxValue;
            CWRoute bestTarget = null;
            int bestPos = -1;

            foreach (var target in keep)
            {
                if (target.totalDemand + demand > ctx.vehicleCapacity)
                    continue;

                for (int pos = 0; pos <= target.customers.Count; pos++)
                {
                    int prevMI = pos > 0 ? target.customers[pos - 1] + 1 : 0;
                    int nextMI = pos < target.customers.Count
                        ? target.customers[pos] + 1 : 0;
                    int newMI = custIdx + 1;

                    float cost = ctx.distanceMatrix[prevMI, newMI] +
                                ctx.distanceMatrix[newMI, nextMI] -
                                ctx.distanceMatrix[prevMI, nextMI];

                    if (cost >= bestCost) continue;

                    var trial = new List<int>(target.customers);
                    trial.Insert(pos, custIdx);
                    if (IsTimeFeasible(trial, ctx))
                    {
                        bestCost = cost;
                        bestTarget = target;
                        bestPos = pos;
                    }
                }
            }

            if (bestTarget != null)
            {
                bestTarget.customers.Insert(bestPos, custIdx);
                bestTarget.totalDemand += demand;
                absorbed++;
            }
            else
            {
                phase2.Add(custIdx);
            }
        }

        // ── Phase 2: Relaxed insertion (ignore TW, respect capacity) ──
        var phase3 = new List<int>();
        foreach (int custIdx in phase2)
        {
            int demand = ctx.orders[custIdx].demand;
            float bestCost = float.MaxValue;
            CWRoute bestTarget = null;
            int bestPos = -1;

            foreach (var target in keep)
            {
                if (target.totalDemand + demand > ctx.vehicleCapacity)
                    continue;

                for (int pos = 0; pos <= target.customers.Count; pos++)
                {
                    int prevMI = pos > 0 ? target.customers[pos - 1] + 1 : 0;
                    int nextMI = pos < target.customers.Count
                        ? target.customers[pos] + 1 : 0;
                    int newMI = custIdx + 1;

                    float cost = ctx.distanceMatrix[prevMI, newMI] +
                                ctx.distanceMatrix[newMI, nextMI] -
                                ctx.distanceMatrix[prevMI, nextMI];

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestTarget = target;
                        bestPos = pos;
                    }
                }
            }

            if (bestTarget != null)
            {
                bestTarget.customers.Insert(bestPos, custIdx);
                bestTarget.totalDemand += demand;
                absorbed++;
            }
            else
            {
                phase3.Add(custIdx);
            }
        }

        // ── Phase 3: Solo overflow routes ──
        foreach (int custIdx in phase3)
        {
            keep.Add(new CWRoute
            {
                customers = new List<int> { custIdx },
                totalDemand = ctx.orders[custIdx].demand
            });
            DLog.Warn("General",$"[Clarke-Wright] Overflow: C{ctx.orders[custIdx].customerNumber:D3}");
        }

        // ── Final verification ──
        var finalSet = new HashSet<int>();
        foreach (var route in keep)
            foreach (int c in route.customers)
                finalSet.Add(c);

        if (finalSet.Count < totalBefore)
        {
            foreach (int c in allCustomersSet)
            {
                if (!finalSet.Contains(c))
                {
                    keep.Add(new CWRoute
                    {
                        customers = new List<int> { c },
                        totalDemand = ctx.orders[c].demand
                    });
                    DLog.Error("General",$"[Clarke-Wright] EMERGENCY rescue: " +
                                $"C{ctx.orders[c].customerNumber:D3}");
                }
            }
        }

        int finalCount = 0;
        foreach (var r in keep) finalCount += r.customers.Count;

        DLog.Info("CW", $" Absorption done: {absorbed} absorbed, " +
                $"{phase3.Count} overflow, {keep.Count} routes, " +
                $"{finalCount} total customers (expected {totalBefore})");

        return keep;
    }

    // ══════════════════════════════════════════════
    //  Final Verification: ensure every customer
    //  appears exactly once across all routes.
    //  If any are missing, force-insert them.
    // ══════════════════════════════════════════════

    private List<PlannedRoute> VerifyAndFixCoverage(
        List<PlannedRoute> routes, RoutingContext ctx, int totalCustomers)
    {
        // Collect all routed customer indices
        var routed = new HashSet<int>();
        foreach (var route in routes)
        {
            foreach (var stop in route.stops)
            {
                if (stop.type == RouteStop.StopType.Delivery && stop.order != null)
                {
                    int idx = ctx.orders.IndexOf(stop.order);
                    if (idx >= 0) routed.Add(idx);
                }
            }
        }

        if (routed.Count >= totalCustomers)
            return routes; // All covered

        // Find missing customers
        var missing = new List<int>();
        for (int i = 0; i < totalCustomers; i++)
        {
            if (!routed.Contains(i))
                missing.Add(i);
        }

        DLog.Warn("General",$"[Clarke-Wright] VerifyAndFix: {missing.Count} " +
                         $"customers missing after conversion — force inserting");

        foreach (int custIdx in missing)
        {
            var order = ctx.orders[custIdx];

            // Try cheapest insertion into existing routes
            PlannedRoute bestRoute = null;
            int bestPos = -1;
            float bestCost = float.MaxValue;

            foreach (var route in routes)
            {
                if (route.totalDemand + order.demand > route.vehicleCapacity)
                    continue;

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
                var stop = ctx.MakeDeliveryStop(order);

                // Calculate timing for this stop
                int prevMI = GetMatrixIndex(bestRoute.stops[bestPos - 1], ctx);
                float travel = ctx.timeMatrix[prevMI, custIdx + 1];
                float arrival = bestRoute.stops[bestPos - 1].plannedDeparture + travel;

                stop.plannedArrival = arrival;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = Mathf.Max(arrival, order.readyTime);
                stop.serviceEnd = stop.serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = arrival > order.dueTime;

                bestRoute.stops.Insert(bestPos, stop);
                bestRoute.totalDemand += order.demand;
                bestRoute.customerCount++;

                // Recalculate timing for subsequent stops
                RecalcTimingFrom(bestRoute, bestPos + 1, ctx);
                bestRoute.totalDistance = ComputeRouteDistance(bestRoute, ctx);
                bestRoute.totalTime =
                    bestRoute.stops[bestRoute.stops.Count - 1].plannedArrival;
            }
            else
            {
                // Create emergency solo route
                var solo = new PlannedRoute
                {
                    vehicleCapacity = ctx.vehicleCapacity,
                    totalDemand = order.demand
                };

                solo.stops.Add(ctx.MakeDepotStop(0));

                float travel = ctx.timeMatrix[0, custIdx + 1];
                var stop = ctx.MakeDeliveryStop(order);
                stop.plannedArrival = travel;
                stop.waitUntil = order.readyTime;
                stop.serviceStart = Mathf.Max(travel, order.readyTime);
                stop.serviceEnd = stop.serviceStart + order.serviceTime;
                stop.plannedDeparture = stop.serviceEnd;
                stop.wasLate = travel > order.dueTime;
                solo.stops.Add(stop);

                float returnTravel = ctx.timeMatrix[custIdx + 1, 0];
                solo.stops.Add(ctx.MakeDepotStop(stop.plannedDeparture + returnTravel));

                solo.totalDistance = ctx.distanceMatrix[0, custIdx + 1] +
                                    ctx.distanceMatrix[custIdx + 1, 0];
                solo.totalTime = stop.plannedDeparture + returnTravel;
                solo.customerCount = 1;

                routes.Add(solo);

                DLog.Warn("General",$"[Clarke-Wright] Emergency solo route for " +
                                 $"C{order.customerNumber:D3}");
            }
        }

        return routes;
    }

    // ══════════════════════════════════════════════
    //  Timing Helpers
    // ══════════════════════════════════════════════

    /// <summary>
    /// Recalculate timing for all stops from startIdx onward.
    /// Used after inserting a stop mid-route.
    /// </summary>
    private void RecalcTimingFrom(PlannedRoute route, int startIdx, RoutingContext ctx)
    {
        for (int i = startIdx; i < route.stops.Count; i++)
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

    private bool IsTimeFeasible(List<int> customers, RoutingContext ctx)
    {
        float time = 0f;
        int prev = 0;

        foreach (int c in customers)
        {
            var order = ctx.orders[c];
            int mi = c + 1;
            time += ctx.timeMatrix[prev, mi];

            if (time > order.dueTime)
                return false;

            float svcStart = Mathf.Max(time, order.readyTime);
            time = svcStart + order.serviceTime;
            prev = mi;
        }

        return true;
    }

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