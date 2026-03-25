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
/// </summary>
public class ClarkeWrightSolver : IRoutingSolver
{
    public string Name => "Clarke-Wright Savings";
    public string Description =>
        "Savings-based merging heuristic.\\n" +
        "Starts with one route per customer, then merges route pairs\\n" +
        "that save the most distance while respecting capacity and time windows.";

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

        // ── Step 1: Compute savings ──
        var savings = new List<Saving>();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                int mi = i + 1; // matrix index
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

        // ── Step 2: One route per customer ──
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

        // ── Step 3: Merge using savings ──
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

            // Only merge if s.i is at end of routeI and s.j is at start of routeJ
            // or s.j is at end of routeJ and s.i is at start of routeI
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

            // Check time window feasibility
            if (!IsTimeFeasible(mergedCustomers, ctx)) continue;

            // Perform merge
            routeI.customers = mergedCustomers;
            routeI.totalDemand += routeJ.totalDemand;
            routeJ.merged = true;

            foreach (int c in routeJ.customers)
                custToRoute[c] = ri;
        }

        // ── Step 4: Collect active routes, limit to maxVehicles ──
        var active = routes.Where(r => !r.merged && r.customers.Count > 0).ToList();

        if (active.Count > ctx.maxVehicles)
        {
            active = active
                .OrderByDescending(r => r.totalDemand)
                .Take(ctx.maxVehicles)
                .ToList();
        }

        // ── Step 5: Convert to PlannedRoute ──
        var result = new List<PlannedRoute>();

        foreach (var cwRoute in active)
        {
            var pr = new PlannedRoute
            {
                vehicleCapacity = ctx.vehicleCapacity,
                totalDemand = cwRoute.totalDemand
            };

            // Depot start
            pr.stops.Add(ctx.MakeDepotStop(0));

            // Customer stops
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

                // Wait if early
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

            // Depot return
            float returnTravel = ctx.timeMatrix[prevMatrixIdx, 0];
            totalDist += ctx.distanceMatrix[prevMatrixIdx, 0];
            currentTime += returnTravel;

            var depotEnd = ctx.MakeDepotStop(currentTime);
            pr.stops.Add(depotEnd);

            pr.totalDistance = totalDist;
            pr.totalTime = currentTime;
            pr.customerCount = cwRoute.customers.Count;

            result.Add(pr);
        }

        // ── Log ──
        int routed = result.Sum(r => r.customerCount);
        int unrouted = n - routed;
        int lateCount = result.Sum(r => r.stops.Count(s => s.wasLate));

        Debug.Log($"[Clarke-Wright] {result.Count} routes, {routed}/{n} routed, " +
                  $"{unrouted} unrouted, {lateCount} late");

        if (unrouted > 0)
            Debug.LogWarning($"[Clarke-Wright] {unrouted} customers unrouted");

        return result;
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
}