// Assets/Scripts/Routing/Solvers/IRoutingSolver.cs
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>
/// Universal interface for Vehicle Routing Problem solvers.
///
/// Any student implementing a new algorithm should:
/// 1. Create a new class that implements IRoutingSolver
/// 2. Register it with SolverRegistry
/// 3. It will automatically appear in the Strategy dropdown
///
/// See NearestFirstSolver.cs for a minimal example.
/// </summary>
public interface IRoutingSolver
{
    /// <summary>
    /// Unique display name shown in the Strategy dropdown.
    /// Example: "Solomon I1 Insertion", "Genetic Algorithm", "Ant Colony"
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description of the algorithm for UI tooltips.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Solve the vehicle routing problem.
    ///
    /// Input:  RoutingContext containing all problem data
    /// Output: List of PlannedRoute, one per vehicle used
    ///
    /// IMPORTANT RULES:
    /// - Each PlannedRoute.stops must START and END with a Depot stop
    /// - Each delivery stop must reference a DeliveryOrder from context.orders
    /// - Do not exceed context.vehicleCapacity per route
    /// - Do not create more routes than context.maxVehicles
    /// - Respect time windows: arrival <= order.dueTime
    /// - Set plannedArrival, serviceStart, serviceEnd, plannedDeparture for each stop
    /// </summary>
    List<PlannedRoute> Solve(RoutingContext context);
}

/// <summary>
/// All data needed to solve a routing problem.
/// Passed to IRoutingSolver.Solve().
///
/// Students should NOT modify this class.
/// </summary>
[System.Serializable]
public class RoutingContext
{
    // ====== Problem Data ======

    /// <summary>Depot location (longitude, latitude, height)</summary>
    public double3 depotLLH;

    /// <summary>All pending delivery orders to route</summary>
    public List<DeliveryOrder> orders;

    /// <summary>Maximum cargo capacity per vehicle</summary>
    public int vehicleCapacity;

    /// <summary>Maximum number of vehicles (drones) available</summary>
    public int maxVehicles;

    /// <summary>Drone speed for time estimation (meters/second)</summary>
    public float speedMps;

    // ====== Pre-computed Matrices (optional to use) ======

    /// <summary>
    /// Distance matrix in meters. Index 0 = depot, index i+1 = orders[i].
    /// distanceMatrix[i,j] = Euclidean distance from point i to point j.
    /// </summary>
    public float[,] distanceMatrix;

    /// <summary>
    /// Travel time matrix in seconds. timeMatrix[i,j] = distanceMatrix[i,j] / speedMps.
    /// </summary>
    public float[,] timeMatrix;

    // ====== Helper Methods ======

    /// <summary>
    /// Get the matrix index for an order. Depot = 0, orders[i] = i+1.
    /// </summary>
    public int OrderToMatrixIndex(int orderListIndex) => orderListIndex + 1;

    /// <summary>
    /// Get the matrix index for the depot.
    /// </summary>
    public int DepotIndex => 0;

    /// <summary>
    /// Calculate geo distance between two LLH points (meters).
    /// Students can use this instead of implementing their own.
    /// </summary>
    public static double GeoDistance(double3 a, double3 b)
    {
        double dLon = (b.x - a.x) * 111320.0 * System.Math.Cos(a.y * System.Math.PI / 180.0);
        double dLat = (b.y - a.y) * 110540.0;
        return System.Math.Sqrt(dLon * dLon + dLat * dLat);
    }

    /// <summary>
    /// Build a depot RouteStop. Use this to create the first and last stop of each route.
    /// </summary>
    public RouteStop MakeDepotStop(float time = 0f)
    {
        return new RouteStop
        {
            type = RouteStop.StopType.Depot,
            locationLLH = depotLLH,
            locationName = "Depot",
            plannedArrival = time,
            plannedDeparture = time
        };
    }

    /// <summary>
    /// Build a delivery RouteStop for an order.
    /// Students must still set timing fields (plannedArrival, serviceStart, etc.)
    /// </summary>
    public RouteStop MakeDeliveryStop(DeliveryOrder order)
    {
        return new RouteStop
        {
            type = RouteStop.StopType.Delivery,
            order = order,
            locationLLH = order.deliveryLLH,
            locationName = $"C{order.customerNumber:D3}"
        };
    }
}