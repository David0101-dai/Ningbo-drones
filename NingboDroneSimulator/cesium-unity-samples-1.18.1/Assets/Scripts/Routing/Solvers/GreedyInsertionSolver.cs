// Assets/Scripts/Routing/Solvers/GreedyInsertionSolver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Greedy Insertion — "All Drones Launch" strategy.
///
/// Core principle: Use ALL available drones simultaneously.
/// Each drone loads a balanced share of cargo, flies to a geographic
/// sector, delivers everything, then returns.
///
/// Algorithm:
///   1. Divide customers into K sectors (K = number of drones)
///      using angular sweep from depot
///   2. Balance demand across sectors (swap customers between
///      neighboring sectors to equalize load)
///   3. Within each sector, order stops by nearest-neighbor
///   4. Apply 2-opt improvement
///   5. Guarantee 100% coverage, all drones active
///
/// Result: All drones fly simultaneously, balanced workload,
/// minimal total time (makespan), maximum parallelism.
/// </summary>
public class GreedyInsertionSolver : IRoutingSolver
{
    public string Name => "Greedy Insertion (100% Coverage)";
    public string Description =>
        "All Drones Launch: divides customers into geographic sectors, " +
        "one per drone. All drones fly simultaneously with balanced cargo. " +
        "100% coverage guaranteed.";

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        int n = ctx.orders.Count;
        int K = ctx.maxVehicles; // Use ALL drones

        Debug.Log($"[GreedyInsertion] Starting: {n} customers, " +
                  $"{K} drones, capacity={ctx.vehicleCapacity}, " +
                  $"speed={ctx.speedMps:F1} m/s");

        // ═══════════════════════════════════════════════
        //  Step 1: Compute polar angle for each customer
        // ═══════════════════════════════════════════════

        var custData = new List<CustData>(n);
        for (int i = 0; i < n; i++)
        {
            var order = ctx.orders[i];
            double dx = (order.deliveryLLH.x - ctx.depotLLH.x) * 111320.0 *
                        System.Math.Cos(ctx.depotLLH.y * System.Math.PI / 180.0);
            double dy = (order.deliveryLLH.y - ctx.depotLLH.y) * 110540.0;

            custData.Add(new CustData
            {
                index = i,
                angle = (float)System.Math.Atan2(dy, dx),
                distFromDepot = ctx.distanceMatrix[0, i + 1],
                demand = order.demand
            });
        }

        // Sort by angle
        custData.Sort((a, b) => a.angle.CompareTo(b.angle));

        // ═══════════════════════════════════════════════
        //  Step 2: Initial sector assignment
        //  Sweep through sorted customers, assign to K sectors
        //  trying to balance demand
        // ═══════════════════════════════════════════════

        var sectors = new List<List<int>>(K);
        var sectorDemand = new int[K];
        for (int i = 0; i < K; i++)
            sectors.Add(new List<int>());

        int totalDemand = ctx.orders.Sum(o => o.demand);
        int targetDemandPerSector = Mathf.CeilToInt((float)totalDemand / K);

        // Greedy sweep assignment
        int currentSector = 0;
        foreach (var cust in custData)
        {
            // If current sector is full enough and we have sectors left,
            // move to next sector
            if (currentSector < K - 1 &&
                sectorDemand[currentSector] >= targetDemandPerSector &&
                sectorDemand[currentSector] + cust.demand > targetDemandPerSector * 1.2f)
            {
                currentSector++;
            }

            // Safety: don't exceed capacity
            if (sectorDemand[currentSector] + cust.demand > ctx.vehicleCapacity &&
                currentSector < K - 1)
            {
                currentSector++;
            }

            sectors[currentSector].Add(cust.index);
            sectorDemand[currentSector] += cust.demand;
        }

        // ═══════════════════════════════════════════════
        //  Step 3: Balance sectors
        //  Move customers from overloaded to underloaded sectors
        // ═══════════════════════════════════════════════

        BalanceSectors(sectors, sectorDemand, ctx, targetDemandPerSector);

        // Remove empty sectors and redistribute
        RedistributeEmptySectors(sectors, sectorDemand, ctx);

        // Log sector assignment
        Debug.Log($"[GreedyInsertion] Sector assignment ({sectors.Count(s => s.Count > 0)} active sectors):");
        for (int s = 0; s < K; s++)
        {
            if (sectors[s].Count > 0)
                Debug.Log($"  Sector {s}: {sectors[s].Count} customers, " +
                          $"demand={sectorDemand[s]}/{ctx.vehicleCapacity}");
        }

        // ═══════════════════════════════════════════════
        //  Step 4: Build routes from sectors
        // ═══════════════════════════════════════════════

        var routes = new List<PlannedRoute>();

        for (int s = 0; s < K; s++)
        {
            if (sectors[s].Count == 0) continue;

            // Order customers within sector by nearest-neighbor
            var ordered = NearestNeighborOrder(sectors[s], ctx);

            // Build route
            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = sectorDemand[s]
            };

            route.stops.Add(ctx.MakeDepotStop(0));
            foreach (int custIdx in ordered)
                route.stops.Add(ctx.MakeDeliveryStop(ctx.orders[custIdx]));
            route.stops.Add(ctx.MakeDepotStop());

            UpdateAllTiming(route, ctx);
            route.customerCount = route.DeliveryStopCount;
            route.totalDistance = ComputeRouteDistance(route, ctx);
            route.totalTime = route.stops[route.stops.Count - 1].plannedArrival;

            routes.Add(route);
        }

        // ═══════════════════════════════════════════════
        //  Step 5: Verify 100% coverage
        // ═══════════════════════════════════════════════

        var routedSet = new HashSet<int>();
        foreach (var route in routes)
            foreach (var stop in route.stops)
                if (stop.type == RouteStop.StopType.Delivery && stop.order != null)
                {
                    int idx = ctx.orders.IndexOf(stop.order);
                    if (idx >= 0) routedSet.Add(idx);
                }

        for (int i = 0; i < n; i++)
        {
            if (routedSet.Contains(i)) continue;

            Debug.LogWarning($"[GreedyInsertion] Straggler C{ctx.orders[i].customerNumber:D3}");

            // Find route with most remaining capacity
            PlannedRoute bestRoute = null;
            int bestRemaining = 0;
            foreach (var route in routes)
            {
                int remaining = route.vehicleCapacity - route.totalDemand;
                if (remaining >= ctx.orders[i].demand && remaining > bestRemaining)
                {
                    bestRemaining = remaining;
                    bestRoute = route;
                }
            }

            if (bestRoute != null)
            {
                // Insert before trailing depot
                bestRoute.stops.Insert(bestRoute.stops.Count - 1,
                    ctx.MakeDeliveryStop(ctx.orders[i]));
                bestRoute.totalDemand += ctx.orders[i].demand;
                bestRoute.customerCount++;
                UpdateAllTiming(bestRoute, ctx);
                bestRoute.totalDistance = ComputeRouteDistance(bestRoute, ctx);
                bestRoute.totalTime =
                    bestRoute.stops[bestRoute.stops.Count - 1].plannedArrival;
            }
            else
            {
                // Create overflow route
                var solo = new PlannedRoute
                {
                    vehicleCapacity = ctx.vehicleCapacity,
                    totalDemand = ctx.orders[i].demand
                };
                solo.stops.Add(ctx.MakeDepotStop(0));
                solo.stops.Add(ctx.MakeDeliveryStop(ctx.orders[i]));
                solo.stops.Add(ctx.MakeDepotStop());
                UpdateAllTiming(solo, ctx);
                solo.customerCount = 1;
                solo.totalDistance = ComputeRouteDistance(solo, ctx);
                solo.totalTime = solo.stops[solo.stops.Count - 1].plannedArrival;
                routes.Add(solo);
            }
        }

        // ═══════════════════════════════════════════════
        //  Step 6: 2-opt improvement
        // ═══════════════════════════════════════════════

        int improvements = 0;
        foreach (var route in routes)
        {
            if (route.DeliveryStopCount >= 3)
                improvements += TwoOptImprove(route, ctx);
        }

        // ═══════════════════════════════════════════════
        //  Statistics
        // ═══════════════════════════════════════════════

        int totalCustomers = routes.Sum(r => r.DeliveryStopCount);
        int totalOnTime = CountPlannedOnTime(routes);
        float totalDist = routes.Sum(r => r.totalDistance);
        float totalDem = routes.Sum(r => r.totalDemand);
        float avgUtil = routes.Count > 0
            ? totalDem / (routes.Count * ctx.vehicleCapacity) * 100f : 0;
        float makespan = routes.Count > 0 ? routes.Max(r => r.totalTime) : 0;

        var sizeDist = routes.GroupBy(r => r.DeliveryStopCount)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}-stop:{g.Count()}")
            .ToList();

        Debug.Log($"[GreedyInsertion] COMPLETE:" +
                  $"\\n  Customers: {totalCustomers}/{n} (100%)" +
                  $"\\n  Routes: {routes.Count} (target was {K})" +
                  $"\\n  Avg capacity utilization: {avgUtil:F1}%" +
                  $"\\n  Planned on-time: {totalOnTime}/{totalCustomers} " +
                  $"({(totalCustomers > 0 ? (float)totalOnTime / totalCustomers * 100f : 0):F1}%)" +
                  $"\\n  Total distance: {totalDist:F0}m ({totalDist / 1000f:F1}km)" +
                  $"\\n  Makespan: {makespan:F1} time units" +
                  $"\\n  Route sizes: {string.Join(", ", sizeDist)}" +
                  $"\\n  2-opt improvements: {improvements}");

        return routes;
    }

    // ================================================================
    //  Data Structures
    // ================================================================

    private struct CustData
    {
        public int index;
        public float angle;
        public float distFromDepot;
        public int demand;
    }

    // ================================================================
    //  Sector Balancing
    // ================================================================

    /// <summary>
    /// Balance demand across sectors by moving customers from
    /// overloaded sectors to neighboring underloaded ones.
    /// </summary>
    private void BalanceSectors(List<List<int>> sectors, int[] sectorDemand,
                                 RoutingContext ctx, int targetDemand)
    {
        int K = sectors.Count;

        for (int pass = 0; pass < 3; pass++)
        {
            for (int s = 0; s < K - 1; s++)
            {
                // Try moving from sector s to s+1 or vice versa
                if (sectorDemand[s] > targetDemand * 1.3f &&
                    sectorDemand[s + 1] < targetDemand * 0.7f)
                {
                    // Move last customer from s to s+1
                    if (sectors[s].Count > 1)
                    {
                        int custIdx = sectors[s][sectors[s].Count - 1];
                        int demand = ctx.orders[custIdx].demand;
                        sectors[s].RemoveAt(sectors[s].Count - 1);
                        sectors[s + 1].Insert(0, custIdx);
                        sectorDemand[s] -= demand;
                        sectorDemand[s + 1] += demand;
                    }
                }
                else if (sectorDemand[s + 1] > targetDemand * 1.3f &&
                         sectorDemand[s] < targetDemand * 0.7f)
                {
                    // Move first customer from s+1 to s
                    if (sectors[s + 1].Count > 1)
                    {
                        int custIdx = sectors[s + 1][0];
                        int demand = ctx.orders[custIdx].demand;
                        sectors[s + 1].RemoveAt(0);
                        sectors[s].Add(custIdx);
                        sectorDemand[s] += demand;
                        sectorDemand[s + 1] -= demand;
                    }
                }
            }
        }
    }

    /// <summary>
    /// If any sector is empty, steal customers from the largest sector.
    /// </summary>
    private void RedistributeEmptySectors(List<List<int>> sectors,
                                           int[] sectorDemand, RoutingContext ctx)
    {
        int K = sectors.Count;

        for (int s = 0; s < K; s++)
        {
            if (sectors[s].Count > 0) continue;

            // Find largest sector
            int largestIdx = 0;
            for (int i = 1; i < K; i++)
                if (sectors[i].Count > sectors[largestIdx].Count)
                    largestIdx = i;

            if (sectors[largestIdx].Count <= 1) continue;

            // Split: move half the customers to the empty sector
            int moveCount = sectors[largestIdx].Count / 2;
            for (int i = 0; i < moveCount; i++)
            {
                int lastIdx = sectors[largestIdx].Count - 1;
                int custIdx = sectors[largestIdx][lastIdx];
                int demand = ctx.orders[custIdx].demand;

                // Check capacity
                if (sectorDemand[s] + demand > ctx.vehicleCapacity)
                    continue;

                sectors[largestIdx].RemoveAt(lastIdx);
                sectors[s].Add(custIdx);
                sectorDemand[largestIdx] -= demand;
                sectorDemand[s] += demand;
            }
        }
    }

    // ================================================================
    //  Nearest Neighbor Ordering
    // ================================================================

    private List<int> NearestNeighborOrder(List<int> customers, RoutingContext ctx)
    {
        if (customers.Count <= 1) return new List<int>(customers);

        var ordered = new List<int>();
        var remaining = new HashSet<int>(customers);
        int currentMI = 0; // Start at depot

        while (remaining.Count > 0)
        {
            int nearest = -1;
            float nearestDist = float.MaxValue;

            foreach (int c in remaining)
            {
                float d = ctx.distanceMatrix[currentMI, c + 1];
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = c;
                }
            }

            if (nearest < 0) break;

            ordered.Add(nearest);
            remaining.Remove(nearest);
            currentMI = nearest + 1;
        }

        return ordered;
    }

    // ================================================================
    //  Timing
    // ================================================================

    private void UpdateAllTiming(PlannedRoute route, RoutingContext ctx)
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
    //  2-opt Improvement
    // ================================================================

    private int TwoOptImprove(PlannedRoute route, RoutingContext ctx)
    {
        int improvements = 0;
        bool improved = true;
        int maxIter = 50;

        while (improved && maxIter-- > 0)
        {
            improved = false;

            for (int i = 1; i < route.stops.Count - 2; i++)
            {
                if (route.stops[i].type != RouteStop.StopType.Delivery)
                    continue;

                for (int j = i + 1; j < route.stops.Count - 1; j++)
                {
                    if (route.stops[j].type != RouteStop.StopType.Delivery)
                        continue;

                    float currentDist = ComputeRouteDistance(route, ctx);

                    // Try swap
                    var temp = route.stops[i];
                    route.stops[i] = route.stops[j];
                    route.stops[j] = temp;
                    UpdateAllTiming(route, ctx);

                    float newDist = ComputeRouteDistance(route, ctx);

                    if (newDist < currentDist - 0.5f)
                    {
                        route.totalDistance = newDist;
                        route.totalTime =
                            route.stops[route.stops.Count - 1].plannedArrival;
                        improvements++;
                        improved = true;
                    }
                    else
                    {
                        // Swap back
                        temp = route.stops[i];
                        route.stops[i] = route.stops[j];
                        route.stops[j] = temp;
                        UpdateAllTiming(route, ctx);
                    }
                }
            }
        }

        return improvements;
    }

    // ================================================================
    //  Statistics
    // ================================================================

    private int CountPlannedOnTime(List<PlannedRoute> routes)
    {
        int count = 0;
        foreach (var route in routes)
            foreach (var stop in route.stops)
                if (stop.type == RouteStop.StopType.Delivery &&
                    stop.order != null && !stop.wasLate)
                    count++;
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