// Assets/Scripts/Routing/VehicleRouter.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Vehicle Routing Problem manager.
/// Now delegates solving to IRoutingSolver implementations via SolverRegistry.
///
/// Responsibilities:
/// - Build RoutingContext from raw data
/// - Call the active solver
/// - Assign drone names to routes
/// - Provide solution summaries
/// </summary>
public class VehicleRouter : MonoBehaviour
{
    public static VehicleRouter Instance;

    [Header("Strategy (Legacy — now uses SolverRegistry)")]
    public RoutingStrategy strategy = RoutingStrategy.Balanced;

    [Header("Speed Settings")]
    public float defaultSpeedMps = 15f;
    public float efficiencySpeedMps = 25f;
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
        Efficiency,
        Economy,
        Balanced
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Main Entry Point
    // ================================================================

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

        // Get active solver
        IRoutingSolver solver = GetActiveSolver();
        if (solver == null)
        {
            EmitStatus("[Router] No solver available! Check SolverRegistry.");
            return new List<PlannedRoute>();
        }

        float speedForPlanning = GetPlanningSpeed();

        var pending = orders.Where(o => o.status == DeliveryOrder.OrderStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            EmitStatus("[Router] No pending orders");
            return new List<PlannedRoute>();
        }

        EmitStatus($"[Router] Solving with '{solver.Name}': {pending.Count} orders, " +
                   $"{fleet.Count} drones (cap={vehicleCapacity}, speed={speedForPlanning:F1}m/s)");

        // Build context
        RoutingContext ctx = BuildContext(pending, fleet, depotLLH, vehicleCapacity, speedForPlanning);

        // Apply Solomon-specific parameters if the active solver is SolomonI1
        ApplySolomonParams(solver);

        // ★ Call the solver ★
        var routes = solver.Solve(ctx);

        // Assign drone names
        AssignDroneNames(routes, fleet);

        // Store results
        _lastSolution = routes;
        _lastRouteCount = routes.Count;
        _lastUnrouted = pending.Count - routes.Sum(r => r.DeliveryStopCount);

        string summary = $"[Router] {solver.Name}: {routes.Count} routes, " +
                         $"{routes.Sum(r => r.DeliveryStopCount)}/{pending.Count} routed, " +
                         $"{_lastUnrouted} unrouted";
        EmitStatus(summary);
        Debug.Log(summary);

        foreach (var route in routes)
            Debug.Log($"[Router] {route}");

        OnRoutesPlanned?.Invoke(routes);
        return routes;
    }

    // ================================================================
    //  Solver Access
    // ================================================================

    private IRoutingSolver GetActiveSolver()
    {
        if (SolverRegistry.Instance != null)
            return SolverRegistry.Instance.ActiveSolver;

        // Fallback: create inline Solomon solver
        Debug.LogWarning("[Router] SolverRegistry not found, using fallback SolomonI1");
        return new SolomonI1Solver();
    }

    private void ApplySolomonParams(IRoutingSolver solver)
    {
        if (solver is SolomonI1Solver solomon)
        {
            switch (strategy)
            {
                case RoutingStrategy.Efficiency:
                    solomon.alpha1 = 0.3f;
                    solomon.mu = 0.6f;
                    solomon.lambda = 1.5f;
                    break;
                case RoutingStrategy.Economy:
                    solomon.alpha1 = 0.7f;
                    solomon.mu = 0.9f;
                    solomon.lambda = 0.5f;
                    break;
                default: // Balanced
                    solomon.alpha1 = 0.5f;
                    solomon.mu = 0.8f;
                    solomon.lambda = 1.0f;
                    break;
            }
        }
    }

    // ================================================================
    //  Context Builder
    // ================================================================

    private RoutingContext BuildContext(
        List<DeliveryOrder> pending,
        List<DroneInfo.Snapshot> fleet,
        double3 depotLLH,
        int vehicleCapacity,
        float speedMps)
    {
        // Build point list: depot + all customers
        var allPoints = new List<double3> { depotLLH };
        foreach (var order in pending)
            allPoints.Add(order.deliveryLLH);

        // Build distance matrix
        int n = allPoints.Count;
        var distMatrix = new float[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                float dist = (float)RoutingContext.GeoDistance(allPoints[i], allPoints[j]);
                distMatrix[i, j] = dist;
                distMatrix[j, i] = dist;
            }
        }

        // Build time matrix
        var timeMatrix = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                timeMatrix[i, j] = distMatrix[i, j] / speedMps;

        return new RoutingContext
        {
            depotLLH = depotLLH,
            orders = pending,
            vehicleCapacity = vehicleCapacity,
            maxVehicles = fleet.Count,
            speedMps = speedMps,
            distanceMatrix = distMatrix,
            timeMatrix = timeMatrix
        };
    }

    // ================================================================
    //  Drone Assignment (unchanged)
    // ================================================================

    private void AssignDroneNames(List<PlannedRoute> routes, List<DroneInfo.Snapshot> fleet)
    {
        var available = fleet
            .Where(f => f.isIdle)
            .OrderBy(f => f.name)
            .ToList();

        if (available.Count < routes.Count)
            available = fleet.OrderBy(f => f.name).ToList();

        for (int i = 0; i < routes.Count; i++)
        {
            routes[i].droneName = i < available.Count
                ? available[i].name
                : $"V{(i + 1):D2}";
        }
    }

    // ================================================================
    //  Speed & Strategy
    // ================================================================

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
    //  Summary
    // ================================================================

    public string GetSolutionSummary()
    {
        if (_lastSolution == null || _lastSolution.Count == 0)
            return "No routes planned yet.";

        string solverName = SolverRegistry.Instance?.ActiveSolver?.Name ?? "Unknown";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Routing Solution ===");
        sb.AppendLine($"Solver: {solverName}");
        sb.AppendLine($"Speed mode: {strategy}");
        sb.AppendLine($"Routes: {_lastSolution.Count}, Unrouted: {_lastUnrouted}");
        sb.AppendLine();

        foreach (var route in _lastSolution)
            sb.AppendLine(route.ToShortString());

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