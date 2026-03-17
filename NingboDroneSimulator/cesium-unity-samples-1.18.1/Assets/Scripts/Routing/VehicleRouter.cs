// Assets/Scripts/Routing/VehicleRouter.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Vehicle Routing Problem with Time Windows (VRPTW) solver.
/// Implements Solomon's I1 Insertion Heuristic.
///
/// Algorithm overview:
/// 1. Sort unrouted customers by urgency (earliest deadline first)
/// 2. For each new route, pick the "seed" customer (farthest or most urgent)
/// 3. Iteratively insert the best feasible customer into the route
/// 4. Repeat until all customers are routed or no more feasible insertions
/// </summary>
public class VehicleRouter : MonoBehaviour
{
    public static VehicleRouter Instance;

    [Header("Strategy")]
    public RoutingStrategy strategy = RoutingStrategy.Balanced;

    [Header("Solomon I1 Parameters")]
    [Tooltip("Weight for distance criterion (α1)")]
    [Range(0f, 1f)]
    public float alpha1 = 0.5f;

    [Tooltip("Weight for time criterion (α2 = 1 - α1)")]
    public float Alpha2 => 1f - alpha1;

    [Tooltip("Weight: distance saving vs time saving (μ)")]
    [Range(0f, 1f)]
    public float mu = 0.8f;

    [Tooltip("Weight: route time push-forward vs distance (λ)")]
    [Range(0f, 3f)]
    public float lambda = 1.0f;

    [Header("Speed Settings")]
    [Tooltip("Default drone speed for time estimation (m/s)")]
    public float defaultSpeedMps = 15f;

    [Tooltip("Efficiency mode: higher speed, more energy")]
    public float efficiencySpeedMps = 25f;

    [Tooltip("Economy mode: lower speed, less energy")]
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
        Efficiency,   // Minimize total time (faster speeds, more vehicles OK)
        Economy,      // Minimize vehicles & energy (slower, pack more per route)
        Balanced      // Middle ground
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Main Entry Point
    // ================================================================

    /// <summary>
    /// Plan routes for all pending orders using the configured strategy.
    /// Returns list of planned routes ready for dispatch.
    /// </summary>
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

        // Apply strategy parameters
        ApplyStrategy();

        float speedForPlanning = GetPlanningSpeed();

        EmitStatus($"[Router] Planning {orders.Count} orders with {fleet.Count} drones " +
                   $"(capacity={vehicleCapacity}, strategy={strategy}, speed={speedForPlanning:F1}m/s)");

        // Filter to pending orders only
        var pending = orders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            EmitStatus("[Router] No pending orders");
            return new List<PlannedRoute>();
        }

        // Build distance matrix
        var allPoints = new List<double3> { depotLLH };
        var orderMap = new Dictionary<int, DeliveryOrder>(); // index → order

        for (int i = 0; i < pending.Count; i++)
        {
            allPoints.Add(pending[i].deliveryLLH);
            orderMap[i + 1] = pending[i]; // index 0 = depot
        }

        float[,] distMatrix = BuildDistanceMatrix(allPoints);
        float[,] timeMatrix = BuildTimeMatrix(distMatrix, speedForPlanning);

        // Run Solomon I1
        var routes = SolomonI1(pending, depotLLH, vehicleCapacity,
                               distMatrix, timeMatrix, fleet.Count, speedForPlanning);

        // Assign drone names
        AssignDroneNames(routes, fleet);

        // Store results
        _lastSolution = routes;
        _lastRouteCount = routes.Count;
        _lastUnrouted = pending.Count - routes.Sum(r => r.DeliveryStopCount);

        string summary = $"[Router] Solution: {routes.Count} routes, " +
                         $"{routes.Sum(r => r.DeliveryStopCount)}/{pending.Count} customers routed, " +
                         $"{_lastUnrouted} unrouted";
        EmitStatus(summary);
        Debug.Log(summary);

        foreach (var route in routes)
            Debug.Log($"[Router] {route}");

        OnRoutesPlanned?.Invoke(routes);
        return routes;
    }

    // ================================================================
    //  Solomon I1 Insertion Heuristic
    // ================================================================

    private List<PlannedRoute> SolomonI1(
        List<DeliveryOrder> orders,
        double3 depotLLH,
        int capacity,
        float[,] distMatrix,
        float[,] timeMatrix,
        int maxVehicles,
        float speedMps)
    {
        var routes = new List<PlannedRoute>();
        var unrouted = new HashSet<int>(); // indices into orders list

        for (int i = 0; i < orders.Count; i++)
            unrouted.Add(i);

        int vehicleNum = 0;

        while (unrouted.Count > 0 && vehicleNum < maxVehicles)
        {
            vehicleNum++;

            // ====== Step 1: Select seed customer ======
            int seedIdx = SelectSeed(orders, unrouted, depotLLH, distMatrix);
            if (seedIdx < 0) break;

            // ====== Step 2: Initialize route with seed ======
            var route = new PlannedRoute
            {
                vehicleCapacity = capacity,
                totalDemand = 0
            };

            // Add depot start
            route.stops.Add(new RouteStop
            {
                type = RouteStop.StopType.Depot,
                locationLLH = depotLLH,
                locationName = "Depot",
                plannedArrival = 0,
                plannedDeparture = 0
            });

            // Insert seed
            InsertCustomerAt(route, 1, orders[seedIdx], seedIdx, depotLLH, distMatrix, timeMatrix, speedMps);
            unrouted.Remove(seedIdx);

            // Add depot end (will be updated as we insert more)
            route.stops.Add(new RouteStop
            {
                type = RouteStop.StopType.Depot,
                locationLLH = depotLLH,
                locationName = "Depot"
            });

            // ====== Step 3: Iteratively insert best feasible customer ======
            bool improved = true;
            while (improved && unrouted.Count > 0)
            {
                improved = false;
                int bestCustomer = -1;
                int bestPosition = -1;
                float bestCost2 = float.MinValue; // c2 criterion (higher = better)

                foreach (int uIdx in unrouted)
                {
                    var order = orders[uIdx];

                    // Skip if demand exceeds remaining capacity
                    if (route.totalDemand + order.demand > capacity)
                        continue;

                    // Try every insertion position (between existing stops)
                    for (int pos = 1; pos < route.stops.Count; pos++)
                    {
                        // Check feasibility
                        if (!IsInsertionFeasible(route, pos, order, uIdx, distMatrix, timeMatrix, speedMps))
                            continue;

                        // Calculate c1 (insertion cost) and c2 (selection criterion)
                        float c1 = ComputeC1(route, pos, uIdx, distMatrix, timeMatrix, speedMps);
                        float c2 = ComputeC2(route, uIdx, c1, distMatrix, timeMatrix);

                        if (c2 > bestCost2)
                        {
                            bestCost2 = c2;
                            bestCustomer = uIdx;
                            bestPosition = pos;
                        }
                    }
                }

                if (bestCustomer >= 0)
                {
                    // Remove the trailing depot, insert customer, re-add depot
                    route.stops.RemoveAt(route.stops.Count - 1);
                    InsertCustomerAt(route, bestPosition, orders[bestCustomer],
                                     bestCustomer, depotLLH, distMatrix, timeMatrix, speedMps);
                    route.stops.Add(new RouteStop
                    {
                        type = RouteStop.StopType.Depot,
                        locationLLH = depotLLH,
                        locationName = "Depot"
                    });

                    unrouted.Remove(bestCustomer);
                    improved = true;
                }
            }

            // Update route-level timing for final depot return
            UpdateRouteTiming(route, distMatrix, timeMatrix, speedMps);

            // Calculate totals
            route.customerCount = route.DeliveryStopCount;
            route.totalDistance = ComputeRouteDistance(route, distMatrix);

            if (route.customerCount > 0)
                routes.Add(route);
        }

        // Handle unrouted customers (log warning)
        if (unrouted.Count > 0)
        {
            Debug.LogWarning($"[VehicleRouter] {unrouted.Count} customers could not be routed! " +
                             $"(capacity or time window constraints)");
        }

        return routes;
    }

    // ================================================================
    //  Seed Selection
    // ================================================================

    /// <summary>
    /// Select seed customer for a new route.
    /// Strategy: pick the unrouted customer with the earliest deadline (most urgent).
    /// Tie-break: farthest from depot (to use vehicle capacity fully).
    /// </summary>
    private int SelectSeed(List<DeliveryOrder> orders, HashSet<int> unrouted,
                           double3 depotLLH, float[,] distMatrix)
    {
        int best = -1;
        float bestDue = float.MaxValue;
        float bestDist = -1;

        foreach (int idx in unrouted)
        {
            var order = orders[idx];
            float due = order.dueTime;
            float dist = distMatrix[0, idx + 1]; // depot to customer

            if (due < bestDue || (Mathf.Approximately(due, bestDue) && dist > bestDist))
            {
                bestDue = due;
                bestDist = dist;
                best = idx;
            }
        }

        return best;
    }

    // ================================================================
    //  Insertion Feasibility Check
    // ================================================================

    private bool IsInsertionFeasible(PlannedRoute route, int pos,
                                      DeliveryOrder newOrder, int orderIdx,
                                      float[,] distMatrix, float[,] timeMatrix, float speedMps)
    {
        // Capacity check
        if (route.totalDemand + newOrder.demand > route.vehicleCapacity)
            return false;

        // Time window check: simulate inserting at pos and check all subsequent stops
        var tempStops = new List<RouteStop>(route.stops);

        var newStop = new RouteStop
        {
            type = RouteStop.StopType.Delivery,
            order = newOrder,
            locationLLH = newOrder.deliveryLLH,
            locationName = $"C{newOrder.customerNumber:D3}"
        };
        tempStops.Insert(pos, newStop);

        // Recalculate arrival times from pos-1 onward
        for (int i = pos; i < tempStops.Count; i++)
        {
            int prevMatrixIdx = GetMatrixIndex(tempStops[i - 1]);
            int currMatrixIdx = GetMatrixIndex(tempStops[i]);

            float travelTime = timeMatrix[prevMatrixIdx, currMatrixIdx];
            float arrival = tempStops[i - 1].plannedDeparture + travelTime;

            if (tempStops[i].type == RouteStop.StopType.Delivery && tempStops[i].order != null)
            {
                // Check: can we arrive before dueDate?
                if (arrival > tempStops[i].order.dueTime)
                    return false;

                float serviceStart = Mathf.Max(arrival, tempStops[i].order.readyTime);
                tempStops[i].plannedArrival = arrival;
                tempStops[i].serviceStart = serviceStart;
                tempStops[i].serviceEnd = serviceStart + tempStops[i].order.serviceTime;
                tempStops[i].plannedDeparture = tempStops[i].serviceEnd;
            }
            else
            {
                // Depot stop
                tempStops[i].plannedArrival = arrival;
                tempStops[i].plannedDeparture = arrival;
            }
        }

        return true;
    }

    // ================================================================
    //  Solomon I1 Cost Criteria
    // ================================================================

    /// <summary>
    /// c1(i,u,j) = α1 * c11 + α2 * c12
    /// where:
    ///   c11 = d(i,u) + d(u,j) - μ * d(i,j)    [distance criterion]
    ///   c12 = bNew_j - b_j                      [time push-forward]
    ///   b = begin of service time
    /// </summary>
    private float ComputeC1(PlannedRoute route, int pos,
                             int orderIdx, float[,] distMatrix, float[,] timeMatrix, float speedMps)
    {
        int prevIdx = GetMatrixIndex(route.stops[pos - 1]);
        int nextIdx = pos < route.stops.Count ? GetMatrixIndex(route.stops[pos]) : 0;
        int newIdx = orderIdx + 1; // +1 because depot is index 0

        // c11: distance detour
        float diu = distMatrix[prevIdx, newIdx];
        float duj = distMatrix[newIdx, nextIdx];
        float dij = distMatrix[prevIdx, nextIdx];
        float c11 = diu + duj - mu * dij;

        // c12: time push-forward at next stop
        float travelToNew = timeMatrix[prevIdx, newIdx];
        float arrivalAtNew = route.stops[pos - 1].plannedDeparture + travelToNew;
        var order = OrderManager.Instance.AllOrders.FirstOrDefault(o => o.customerNumber == GetCustomerNumber(orderIdx));
        if (order == null) order = GetOrderByIndex(orderIdx);

        float serviceStartNew = Mathf.Max(arrivalAtNew, order != null ? order.readyTime : 0);
        float departNew = serviceStartNew + (order != null ? order.serviceTime : 0);

        float travelToNext = timeMatrix[newIdx, nextIdx];
        float newArrivalAtNext = departNew + travelToNext;

        float oldArrivalAtNext = route.stops[pos < route.stops.Count ? pos : route.stops.Count - 1].plannedArrival;
        float c12 = newArrivalAtNext - oldArrivalAtNext;

        return alpha1 * c11 + Alpha2 * c12;
    }

    /// <summary>
    /// c2(i,u) = λ * d(0,u) - c1(i,u,j)
    /// Higher c2 = better candidate for insertion.
    /// This favors inserting customers that are far from depot
    /// but cheap to insert into the current route.
    /// </summary>
    private float ComputeC2(PlannedRoute route, int orderIdx,
                             float c1, float[,] distMatrix, float[,] timeMatrix)
    {
        int newIdx = orderIdx + 1;
        float distFromDepot = distMatrix[0, newIdx];
        return lambda * distFromDepot - c1;
    }

    // ================================================================
    //  Route Construction Helpers
    // ================================================================

    private void InsertCustomerAt(PlannedRoute route, int pos,
                                   DeliveryOrder order, int orderIdx,
                                   double3 depotLLH, float[,] distMatrix,
                                   float[,] timeMatrix, float speedMps)
    {
        var stop = new RouteStop
        {
            type = RouteStop.StopType.Delivery,
            order = order,
            locationLLH = order.deliveryLLH,
            locationName = $"C{order.customerNumber:D3}"
        };

        route.stops.Insert(pos, stop);
        route.totalDemand += order.demand;

        // Recalculate timing from the beginning
        UpdateRouteTiming(route, distMatrix, timeMatrix, speedMps);
    }

    private void UpdateRouteTiming(PlannedRoute route, float[,] distMatrix,
                                    float[,] timeMatrix, float speedMps)
    {
        if (route.stops.Count < 2) return;

        route.stops[0].plannedArrival = 0;
        route.stops[0].plannedDeparture = 0;
        route.totalTime = 0;

        for (int i = 1; i < route.stops.Count; i++)
        {
            int prevIdx = GetMatrixIndex(route.stops[i - 1]);
            int currIdx = GetMatrixIndex(route.stops[i]);

            float travelTime = timeMatrix[prevIdx, currIdx];
            float arrival = route.stops[i - 1].plannedDeparture + travelTime;

            route.stops[i].plannedArrival = arrival;

            if (route.stops[i].type == RouteStop.StopType.Delivery && route.stops[i].order != null)
            {
                float readyTime = route.stops[i].order.readyTime;
                float serviceTime = route.stops[i].order.serviceTime;

                route.stops[i].waitUntil = readyTime;
                route.stops[i].serviceStart = Mathf.Max(arrival, readyTime);
                route.stops[i].serviceEnd = route.stops[i].serviceStart + serviceTime;
                route.stops[i].plannedDeparture = route.stops[i].serviceEnd;

                // Check if late
                route.stops[i].wasLate = arrival > route.stops[i].order.dueTime;
            }
            else
            {
                route.stops[i].plannedDeparture = arrival;
            }
        }

        route.totalTime = route.stops[route.stops.Count - 1].plannedArrival;
    }

    private float ComputeRouteDistance(PlannedRoute route, float[,] distMatrix)
    {
        float total = 0;
        for (int i = 0; i < route.stops.Count - 1; i++)
        {
            int a = GetMatrixIndex(route.stops[i]);
            int b = GetMatrixIndex(route.stops[i + 1]);
            total += distMatrix[a, b];
        }
        return total;
    }

    // ================================================================
    //  Drone Name Assignment
    // ================================================================

    private void AssignDroneNames(List<PlannedRoute> routes, List<DroneInfo.Snapshot> fleet)
    {
        // Sort fleet: idle first, then by name
        var available = fleet
            .Where(f => f.isIdle)
            .OrderBy(f => f.name)
            .ToList();

        // If not enough idle drones, use all drones
        if (available.Count < routes.Count)
        {
            available = fleet.OrderBy(f => f.name).ToList();
        }

        for (int i = 0; i < routes.Count; i++)
        {
            if (i < available.Count)
                routes[i].droneName = available[i].name;
            else
                routes[i].droneName = $"V{(i + 1):D2}"; // Placeholder
        }
    }

    // ================================================================
    //  Distance & Time Matrices
    // ================================================================

    private float[,] BuildDistanceMatrix(List<double3> points)
    {
        int n = points.Count;
        var matrix = new float[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                float dist = (float)GeoDistanceMeters(points[i], points[j]);
                matrix[i, j] = dist;
                matrix[j, i] = dist;
            }
        }

        return matrix;
    }

    private float[,] BuildTimeMatrix(float[,] distMatrix, float speedMps)
    {
        int n = distMatrix.GetLength(0);
        var matrix = new float[n, n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                matrix[i, j] = distMatrix[i, j] / speedMps;
            }
        }

        return matrix;
    }

    // ================================================================
    //  Strategy Application
    // ================================================================

    private void ApplyStrategy()
    {
        switch (strategy)
        {
            case RoutingStrategy.Efficiency:
                // Favor time minimization: more vehicles OK, faster
                alpha1 = 0.3f;  // Less weight on distance
                mu = 0.6f;
                lambda = 1.5f;  // Strongly prefer far-from-depot customers
                break;

            case RoutingStrategy.Economy:
                // Favor fewer vehicles: pack more per route, slower
                alpha1 = 0.7f;  // More weight on distance
                mu = 0.9f;      // Strong distance saving preference
                lambda = 0.5f;  // Less urgency to grab far customers
                break;

            case RoutingStrategy.Balanced:
            default:
                alpha1 = 0.5f;
                mu = 0.8f;
                lambda = 1.0f;
                break;
        }
    }

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
    //  Matrix Index Helpers
    // ================================================================

    /// <summary>
    /// Get the distance matrix index for a route stop.
    /// Depot = 0, customers = their order index + 1
    /// </summary>
    private int GetMatrixIndex(RouteStop stop)
    {
        if (stop.type == RouteStop.StopType.Depot) return 0;
        if (stop.order == null) return 0;

        // Find the order's index in AllOrders pending list
        var allOrders = OrderManager.Instance?.AllOrders;
        if (allOrders == null) return 0;

        var pendingOrders = allOrders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        int idx = pendingOrders.IndexOf(stop.order);
        return idx >= 0 ? idx + 1 : 0;
    }

    private int GetCustomerNumber(int orderIndex)
    {
        var allOrders = OrderManager.Instance?.AllOrders;
        if (allOrders == null) return -1;
        var pending = allOrders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (orderIndex >= 0 && orderIndex < pending.Count)
            return pending[orderIndex].customerNumber;
        return -1;
    }

    private DeliveryOrder GetOrderByIndex(int orderIndex)
    {
        var allOrders = OrderManager.Instance?.AllOrders;
        if (allOrders == null) return null;
        var pending = allOrders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (orderIndex >= 0 && orderIndex < pending.Count)
            return pending[orderIndex];
        return null;
    }

    // ================================================================
    //  Geo Helpers
    // ================================================================

    private double GeoDistanceMeters(double3 a, double3 b)
    {
        double dLon = (b.x - a.x) * 111320.0 * System.Math.Cos(a.y * System.Math.PI / 180.0);
        double dLat = (b.y - a.y) * 110540.0;
        return System.Math.Sqrt(dLon * dLon + dLat * dLat);
    }

    // ================================================================
    //  Public Queries
    // ================================================================

    public string GetSolutionSummary()
    {
        if (_lastSolution == null || _lastSolution.Count == 0)
            return "No routes planned yet. Click 'Solve Routes' first.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Routing Solution ({strategy}) ===");
        sb.AppendLine($"Routes: {_lastSolution.Count}, Unrouted: {_lastUnrouted}");
        sb.AppendLine();

        foreach (var route in _lastSolution)
        {
            sb.AppendLine(route.ToShortString());
        }

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