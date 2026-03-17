// Assets/Scripts/Data/SolomonDataset.cs
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>
/// Parsed Solomon VRPTW benchmark dataset.
/// Can be loaded from raw text format or our JSON format.
/// </summary>
[System.Serializable]
public class SolomonDataset
{
    public string name;                 // e.g. "c1_2_1"
    public int vehicleCount;            // Max vehicles available
    public int vehicleCapacity;         // Capacity per vehicle

    public SolomonCustomer depot;       // Customer 0 = depot
    public List<SolomonCustomer> customers = new List<SolomonCustomer>();

    // Mapping info (set during import)
    public CoordinateMapping mapping;

    [System.Serializable]
    public class CoordinateMapping
    {
        public double centerLongitude;  // Target city center
        public double centerLatitude;
        public double scaleMetersPerUnit; // How many meters per Solomon coordinate unit
        public double flightHeightMeters; // Default flight altitude
    }

    /// <summary>
    /// Get total number of customers (excluding depot)
    /// </summary>
    public int CustomerCount => customers.Count;

    /// <summary>
    /// Get total demand across all customers
    /// </summary>
    public int TotalDemand
    {
        get
        {
            int sum = 0;
            foreach (var c in customers) sum += c.demand;
            return sum;
        }
    }

    /// <summary>
    /// Get the time horizon (max due date)
    /// </summary>
    public float TimeHorizon
    {
        get
        {
            float max = 0;
            foreach (var c in customers)
                if (c.dueDate > max) max = c.dueDate;
            if (depot != null && depot.dueDate > max) max = depot.dueDate;
            return max;
        }
    }

    public override string ToString()
    {
        return $"Solomon '{name}': {vehicleCount} vehicles (cap={vehicleCapacity}), " +
               $"{CustomerCount} customers, totalDemand={TotalDemand}, horizon={TimeHorizon:F0}";
    }
}

[System.Serializable]
public class SolomonCustomer
{
    public int id;
    public float x;
    public float y;
    public int demand;
    public float readyTime;
    public float dueDate;
    public float serviceTime;

    // Computed during mapping
    public double longitude;
    public double latitude;
    public double height;

    public double3 GetLLH() => new double3(longitude, latitude, height);
}