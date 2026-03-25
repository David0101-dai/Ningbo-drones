// Assets/Scripts/Routing/Solvers/NearestFirstSolver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ★★★ EXAMPLE SOLVER — Template for future students ★★★
///
/// This is the simplest possible routing algorithm:
/// Nearest-Neighbor heuristic. Always go to the closest unvisited customer.
///
/// HOW TO CREATE YOUR OWN SOLVER:
/// ================================================
/// 1. Copy this file and rename it (e.g., GeneticAlgorithmSolver.cs)
/// 2. Rename the class
/// 3. Change Name and Description
/// 4. Implement your algorithm in Solve()
/// 5. Register it in SolverRegistry.InitBuiltInSolvers():
///        Register(new GeneticAlgorithmSolver());
/// 6. Done! It will appear in the Strategy dropdown.
/// ================================================
///
/// RULES YOUR SOLVER MUST FOLLOW:
/// - Each route starts and ends with a Depot stop (use ctx.MakeDepotStop())
/// - Each delivery stop references a DeliveryOrder (use ctx.MakeDeliveryStop())
/// - Do NOT exceed ctx.vehicleCapacity per route
/// - Do NOT create more routes than ctx.maxVehicles
/// - Set timing fields: plannedArrival, serviceStart, serviceEnd, plannedDeparture
/// - Respect time windows: arrival should be <= order.dueTime
///
/// AVAILABLE DATA (from RoutingContext ctx):
/// - ctx.depotLLH              → Depot GPS coordinates
/// - ctx.orders                → List of all orders to deliver
/// - ctx.vehicleCapacity       → Max cargo per drone
/// - ctx.maxVehicles           → Max number of drones
/// - ctx.speedMps              → Drone speed in m/s
/// - ctx.distanceMatrix[i][j]  → Distance in meters (0=depot, i+1=orders[i])
/// - ctx.timeMatrix[i][j]      → Travel time in seconds
/// - ctx.MakeDepotStop()       → Helper to create depot RouteStop
/// - ctx.MakeDeliveryStop(order) → Helper to create delivery RouteStop
/// - RoutingContext.GeoDistance(a, b) → Calculate distance between two points
/// </summary>
public class NearestFirstSolver : IRoutingSolver
{
    // ================================================================
    //  Step 1: Set your solver's name and description
    // ================================================================

    public string Name => "Nearest Neighbor";

    public string Description =>
        "Simple greedy heuristic: always visit the nearest unserved customer next. " +
        "Fast but produces suboptimal routes. Good baseline for comparison.";

    // ================================================================
    //  Step 2: Implement the Solve method
    // ================================================================

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>();

        // Initialize: all orders need routing
        for (int i = 0; i < ctx.orders.Count; i++)
            unrouted.Add(i);

        int vehicleNum = 0;

        // Keep creating routes until all orders are routed or we run out of vehicles
        while (unrouted.Count > 0 && vehicleNum < ctx.maxVehicles)
        {
            vehicleNum++;

            // Create a new route starting at depot
            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };
            route.stops.Add(ctx.MakeDepotStop(0));

            int currentMatrixIdx = 0; // Start at depot
            float currentTime = 0f;

            // Greedily add nearest feasible customer
            bool found = true;
            while (found && unrouted.Count > 0)
            {
                found = false;
                int bestIdx = -1;
                float bestDist = float.MaxValue;

                foreach (int idx in unrouted)
                {
                    var order = ctx.orders[idx];
                    int mIdx = ctx.OrderToMatrixIndex(idx);

                    // Check capacity
                    if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                        continue;

                    // Check time window feasibility
                    float travelTime = ctx.timeMatrix[currentMatrixIdx, mIdx];
                    float arrival = currentTime + travelTime;
                    if (arrival > order.dueTime)
                        continue;

                    // Pick nearest
                    float dist = ctx.distanceMatrix[currentMatrixIdx, mIdx];
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIdx = idx;
                    }
                }

                if (bestIdx >= 0)
                {
                    var order = ctx.orders[bestIdx];
                    int mIdx = ctx.OrderToMatrixIndex(bestIdx);

                    // Create delivery stop with timing
                    var stop = ctx.MakeDeliveryStop(order);
                    float travel = ctx.timeMatrix[currentMatrixIdx, mIdx];
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

                    currentMatrixIdx = mIdx;
                    currentTime = stop.plannedDeparture;

                    unrouted.Remove(bestIdx);
                    found = true;
                }
            }

            // Close route: return to depot
            float returnTravel = ctx.timeMatrix[currentMatrixIdx, ctx.DepotIndex];
            route.stops.Add(ctx.MakeDepotStop(currentTime + returnTravel));

            // Set route totals
            route.customerCount = route.DeliveryStopCount;
            route.totalTime = currentTime + returnTravel;
            route.totalDistance = ComputeRouteDistance(route, ctx);

            if (route.customerCount > 0)
                routes.Add(route);
        }

        if (unrouted.Count > 0)
            Debug.LogWarning($"[NearestFirst] {unrouted.Count} customers unrouted");

        return routes;
    }

    // ================================================================
    //  Helper (students can add their own helper methods)
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