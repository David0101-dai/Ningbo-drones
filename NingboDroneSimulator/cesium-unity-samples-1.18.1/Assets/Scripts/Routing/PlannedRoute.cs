// Assets/Scripts/Routing/PlannedRoute.cs
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>
/// A single stop on a drone's planned route.
/// </summary>
[System.Serializable]
public class RouteStop
{
    public enum StopType { Depot, Delivery }

    public StopType type;
    public DeliveryOrder order;          // null for depot stops
    public double3 locationLLH;
    public string locationName;

    // ====== Time Planning ======
    public float plannedArrival;         // Planned arrival time (sim units)
    public float waitUntil;              // Must wait until readyTime if early
    public float serviceStart;           // max(arrival, readyTime)
    public float serviceEnd;             // serviceStart + serviceTime
    public float plannedDeparture;       // = serviceEnd

    // ====== Runtime Tracking ======
    public float actualArrival;          // Actual arrival (filled during execution)
    public bool isCompleted;
    public bool wasLate;                 // Arrived after dueDate?

    public int demand => order != null ? order.demand : 0;

    public override string ToString()
    {
        if (type == StopType.Depot)
            return $"[Depot] {locationName}";
        return $"[C{order?.customerNumber:D3}] D={demand} TW=[{order?.readyTime:F0}-{order?.dueTime:F0}] " +
               $"Arrive={plannedArrival:F0} Late={wasLate}";
    }
}

/// <summary>
/// A complete planned route for one drone: Depot → C1 → C2 → ... → Depot
/// </summary>
[System.Serializable]
public class PlannedRoute
{
    public string droneName;
    public List<RouteStop> stops = new List<RouteStop>();

    // ====== Aggregates ======
    public int totalDemand;
    public int vehicleCapacity;
    public float totalDistance;           // Estimated meters
    public float totalTime;              // Estimated sim time units
    public int customerCount;

    // ====== Runtime ======
    public int currentStopIndex;         // Which stop the drone is heading to
    public bool isDispatched;
    public bool isCompleted;

    /// <summary>
    /// Number of delivery stops (excluding depot stops)
    /// </summary>
    public int DeliveryStopCount
    {
        get
        {
            int count = 0;
            foreach (var s in stops)
                if (s.type == RouteStop.StopType.Delivery) count++;
            return count;
        }
    }

    /// <summary>
    /// Get all delivery orders on this route
    /// </summary>
    public List<DeliveryOrder> GetOrders()
    {
        var orders = new List<DeliveryOrder>();
        foreach (var s in stops)
            if (s.order != null) orders.Add(s.order);
        return orders;
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{droneName}: {customerCount} customers, D={totalDemand}/{vehicleCapacity}, ");
        sb.Append($"T={totalTime:F0}, Dist={totalDistance:F0}m");
        sb.AppendLine();
        foreach (var stop in stops)
            sb.AppendLine($"  {stop}");
        return sb.ToString();
    }

    /// <summary>
    /// Short summary for UI display
    /// </summary>
    public string ToShortString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{droneName}: ");
        foreach (var stop in stops)
        {
            if (stop.type == RouteStop.StopType.Depot)
                sb.Append("D→");
            else
                sb.Append($"C{stop.order?.customerNumber:D3}→");
        }
        sb.Append($" [{totalDemand}/{vehicleCapacity}]");
        return sb.ToString();
    }
}