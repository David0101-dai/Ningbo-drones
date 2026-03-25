// Assets/Scripts/Routing/Solvers/SolomonI1Solver.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Solomon's I1 Insertion Heuristic for VRPTW.
///
/// Extracted from the original VehicleRouter.cs and wrapped
/// to implement IRoutingSolver.
/// </summary>
public class SolomonI1Solver : IRoutingSolver
{
    public string Name => "Solomon I1 Insertion";
    public string Description =>
        "Classic insertion heuristic for VRPTW. " +
        "Builds routes by iteratively inserting the best feasible customer. " +
        "Good balance of solution quality and speed.";

    // Solomon parameters (configurable)
    public float alpha1 = 0.5f;
    public float mu = 0.8f;
    public float lambda = 1.0f;

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>();
        for (int i = 0; i < ctx.orders.Count; i++)
            unrouted.Add(i);

        int vehicleNum = 0;

        while (unrouted.Count > 0 && vehicleNum < ctx.maxVehicles)
        {
            vehicleNum++;

            // Step 1: Select seed (earliest deadline, tie-break: farthest)
            int seedIdx = SelectSeed(ctx, unrouted);
            if (seedIdx < 0) break;

            // Step 2: Init route
            var route = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = 0
            };

            route.stops.Add(ctx.MakeDepotStop(0));
            InsertCustomer(route, 1, ctx.orders[seedIdx], seedIdx, ctx);
            unrouted.Remove(seedIdx);
            route.stops.Add(ctx.MakeDepotStop());

            // Step 3: Iterative best insertion
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
                    if (route.totalDemand + order.demand > ctx.vehicleCapacity)
                        continue;

                    for (int pos = 1; pos < route.stops.Count; pos++)
                    {
                        if (!IsFeasible(route, pos, order, uIdx, ctx))
                            continue;

                        float c1 = ComputeC1(route, pos, uIdx, ctx);
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

            if (route.customerCount > 0)
                routes.Add(route);
        }

        if (unrouted.Count > 0)
            Debug.LogWarning($"[SolomonI1] {unrouted.Count} customers unrouted");

        return routes;
    }

    // ================================================================
    //  Internal Methods
    // ================================================================

    private int SelectSeed(RoutingContext ctx, HashSet<int> unrouted)
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

    private bool IsFeasible(PlannedRoute route, int pos, DeliveryOrder order,
                            int orderIdx, RoutingContext ctx)
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
                if (arrival > temp[i].order.dueTime) return false;
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

    private float ComputeC1(PlannedRoute route, int pos, int orderIdx, RoutingContext ctx)
    {
        int prevIdx = MatrixIdx(route.stops[pos - 1], ctx);
        int nextIdx = pos < route.stops.Count ? MatrixIdx(route.stops[pos], ctx) : 0;
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

    private void InsertCustomer(PlannedRoute route, int pos, DeliveryOrder order,
                                int orderIdx, RoutingContext ctx)
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

            if (route.stops[i].type == RouteStop.StopType.Delivery && route.stops[i].order != null)
            {
                route.stops[i].waitUntil = route.stops[i].order.readyTime;
                route.stops[i].serviceStart = Mathf.Max(arrival, route.stops[i].order.readyTime);
                route.stops[i].serviceEnd = route.stops[i].serviceStart + route.stops[i].order.serviceTime;
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
            total += ctx.distanceMatrix[MatrixIdx(route.stops[i], ctx), MatrixIdx(route.stops[i + 1], ctx)];
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