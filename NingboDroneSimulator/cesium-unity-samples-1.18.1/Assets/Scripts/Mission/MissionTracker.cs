// Assets/Scripts/Routing/MissionTracker.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Tracks global mission statistics across all drones and routes.
/// Collects data for CSV export and real-time display.
/// </summary>
public class MissionTracker : MonoBehaviour
{
    public static MissionTracker Instance;

    // ====== Per-Stop Records ======
    [System.Serializable]
    public class StopRecord
    {
        public string droneName;
        public int routeIndex;
        public int stopIndex;
        public int customerNumber;
        public int demand;
        public float plannedArrival;
        public float actualArrival;
        public float readyTime;
        public float dueTime;
        public float serviceTime;
        public bool wasLate;
        public bool wasEarly;
        public float waitTime;
        public float lateness;
        public float droneSpeedAtArrival;
        public float droneBatteryAtArrival;
        public int droneCargoAtArrival;
        public double longitude;
        public double latitude;
        public string timestamp;
    }

    // ====== Per-Route Records ======
    [System.Serializable]
    public class RouteRecord
    {
        public string droneName;
        public int routeIndex;
        public int customerCount;
        public int totalDemand;
        public int vehicleCapacity;
        public float startTime;
        public float endTime;
        public float totalTime;
        public float totalDistanceMeters;
        public int onTimeCount;
        public int lateCount;
        public float totalLateness;
        public float batteryUsedWh;
        public float batteryStartPercent;
        public float batteryEndPercent;
        public string strategy;
        public bool completed;
    }

    // ====== NEW: Per-Leg Records (each flight segment between two stops) ======
    [System.Serializable]
    public class LegRecord
    {
        // Identity
        public string droneName;
        public int routeIndex;
        public int legIndex;           // 0 = depot→first stop, 1 = stop1→stop2, ...

        // Origin
        public string originName;      // e.g. "Depot_RC50", "C003"
        public double originLongitude;  // degrees
        public double originLatitude;   // degrees
        public double originAltitude;   // meters WGS84

        // Destination
        public string destName;        // e.g. "C001", "Depot_RC50"
        public double destLongitude;
        public double destLatitude;
        public double destAltitude;

        // Distance
        public float straightLineDistM;    // Haversine direct distance (meters)
        public float actualFlownDistM;     // Sum of waypoint-to-waypoint distances (meters)
        public int waypointCount;          // Number of waypoints in this leg's path

        // Time
        public float departureSimTime;     // SimClock time when leg started
        public float arrivalSimTime;       // SimClock time when leg ended
        public float legDurationSimTime;   // arrival - departure
        public float avgSpeedMps;          // actualFlownDist / legDuration

        // Drone state at departure
        public float departBatteryPct;
        public int departCargoLoad;
        public float cruiseSpeedMps;       // Planned cruise speed for this leg

        // Drone state at arrival
        public float arriveBatteryPct;
        public int arriveCargoLoad;

        // Time window (of destination, if customer)
        public float destReadyTime;
        public float destDueTime;
        public bool arrivedOnTime;

        // Units reference
        public string distanceUnit;        // always "meters"
        public string timeUnit;            // always "sim_time_units"
        public string speedUnit;           // always "m/s"
        public string altitudeUnit;        // always "meters_WGS84"
        public string coordSystem;         // always "WGS84_degrees"
    }

    // ====== Storage ======
    private readonly List<StopRecord> _stopRecords = new List<StopRecord>();
    private readonly List<RouteRecord> _routeRecords = new List<RouteRecord>();
    private readonly List<LegRecord> _legRecords = new List<LegRecord>();
    private int _routeCounter = 0;
    private float _missionStartTime;
    private string _datasetName = "";
    private string _strategyName = "";

    // ====== Properties ======
    public List<StopRecord> StopRecords => _stopRecords;
    public List<RouteRecord> RouteRecords => _routeRecords;
    public List<LegRecord> LegRecords => _legRecords;
    public int TotalStopsCompleted => _stopRecords.Count;
    public int TotalOnTime => _stopRecords.Count(r => !r.wasLate);
    public int TotalLate => _stopRecords.Count(r => r.wasLate);
    public float OnTimePercent => _stopRecords.Count > 0
        ? (float)TotalOnTime / _stopRecords.Count * 100f : 0f;

    // ====== Events ======
    public System.Action<StopRecord> OnStopRecorded;
    public System.Action<RouteRecord> OnRouteRecorded;
    public System.Action<LegRecord> OnLegRecorded;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Mission Control
    // ================================================================

    public void StartMission(string datasetName, string strategy)
    {
        _stopRecords.Clear();
        _routeRecords.Clear();
        _legRecords.Clear();
        _routeCounter = 0;
        _missionStartTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;
        _datasetName = datasetName;
        _strategyName = strategy;

        Debug.Log($"[MissionTracker] Mission started: {datasetName}, strategy={strategy}");
    }

    // ================================================================
    //  Record Leg (NEW)
    // ================================================================

    /// <summary>
    /// Begin recording a leg. Returns a LegRecord that the caller
    /// should hold onto and pass to EndLeg() when the drone arrives.
    /// </summary>
    public LegRecord BeginLeg(
        string droneName, int routeIndex, int legIndex,
        string originName, double3 originLLH,
        string destName, double3 destLLH,
        List<double3> flightPath,
        DroneSpec spec, DroneGeoNavigator nav)
    {
        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;

        var leg = new LegRecord
        {
            droneName = droneName,
            routeIndex = routeIndex,
            legIndex = legIndex,

            originName = originName,
            originLongitude = originLLH.x,
            originLatitude = originLLH.y,
            originAltitude = originLLH.z,

            destName = destName,
            destLongitude = destLLH.x,
            destLatitude = destLLH.y,
            destAltitude = destLLH.z,

            straightLineDistM = (float)HaversineDistance(originLLH, destLLH),
            actualFlownDistM = ComputePathDistance(flightPath),
            waypointCount = flightPath != null ? flightPath.Count : 0,

            departureSimTime = simTime,
            departBatteryPct = spec != null ? spec.BatteryPercent : 100f,
            departCargoLoad = spec != null ? spec.currentLoad : 0,
            cruiseSpeedMps = nav != null ? (float)nav.cruiseSpeed : 0f,

            // Units metadata
            distanceUnit = "meters",
            timeUnit = "sim_time_units",
            speedUnit = "m/s",
            altitudeUnit = "meters_WGS84",
            coordSystem = "WGS84_degrees"
        };

        Debug.Log($"[MissionTracker] Leg started: {droneName} leg#{legIndex} " +
                  $"{originName}→{destName}, " +
                  $"straight={leg.straightLineDistM:F0}m, " +
                  $"path={leg.actualFlownDistM:F0}m ({leg.waypointCount} wps)");

        return leg;
    }

    /// <summary>
    /// End a leg recording. Fills in arrival data and stores the record.
    /// </summary>
    public void EndLeg(LegRecord leg, DroneSpec spec, DeliveryOrder destOrder)
    {
        if (leg == null) return;

        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;

        leg.arrivalSimTime = simTime;
        leg.legDurationSimTime = simTime - leg.departureSimTime;
        leg.avgSpeedMps = leg.legDurationSimTime > 0
            ? leg.actualFlownDistM / leg.legDurationSimTime
            : 0f;

        leg.arriveBatteryPct = spec != null ? spec.BatteryPercent : 0f;
        leg.arriveCargoLoad = spec != null ? spec.currentLoad : 0;

        // Time window info
        if (destOrder != null)
        {
            leg.destReadyTime = destOrder.readyTime;
            leg.destDueTime = destOrder.dueTime;
            leg.arrivedOnTime = simTime <= destOrder.dueTime;
        }
        else
        {
            // Depot return — always "on time"
            leg.destReadyTime = 0;
            leg.destDueTime = float.MaxValue;
            leg.arrivedOnTime = true;
        }

        _legRecords.Add(leg);
        OnLegRecorded?.Invoke(leg);

        string timeStatus = leg.arrivedOnTime ? "ON TIME" : $"LATE by {simTime - leg.destDueTime:F1}";
        Debug.Log($"[MissionTracker] Leg ended: {leg.droneName} leg#{leg.legIndex} " +
                  $"{leg.originName}→{leg.destName}: " +
                  $"dist={leg.actualFlownDistM:F0}m, time={leg.legDurationSimTime:F1}, " +
                  $"avgSpeed={leg.avgSpeedMps:F1}m/s, {timeStatus}");
    }

    // ================================================================
    //  Record Stop Completion (existing, unchanged)
    // ================================================================

    public void RecordStopCompletion(
        string droneName, int routeIndex, int stopIndex,
        RouteStop stop, DroneSpec spec, DroneGeoNavigator nav)
    {
        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;

        var record = new StopRecord
        {
            droneName = droneName,
            routeIndex = routeIndex,
            stopIndex = stopIndex,
            customerNumber = stop.order?.customerNumber ?? -1,
            demand = stop.demand,
            plannedArrival = stop.plannedArrival,
            actualArrival = simTime,
            readyTime = stop.order?.readyTime ?? 0,
            dueTime = stop.order?.dueTime ?? float.MaxValue,
            serviceTime = stop.order?.serviceTime ?? 0,
            wasLate = simTime > (stop.order?.dueTime ?? float.MaxValue),
            wasEarly = simTime < (stop.order?.readyTime ?? 0),
            lateness = Mathf.Max(0, simTime - (stop.order?.dueTime ?? float.MaxValue)),
            waitTime = Mathf.Max(0, (stop.order?.readyTime ?? 0) - simTime),
            droneSpeedAtArrival = nav != null ? (float)nav.cruiseSpeed : 0,
            droneBatteryAtArrival = spec != null ? spec.BatteryPercent : 0,
            droneCargoAtArrival = spec != null ? spec.currentLoad : 0,
            longitude = stop.locationLLH.x,
            latitude = stop.locationLLH.y,
            timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _stopRecords.Add(record);
        OnStopRecorded?.Invoke(record);

        string status = record.wasLate ? $"LATE by {record.lateness:F0}" : "ON TIME";
        if (record.wasEarly) status += $" (waited {record.waitTime:F0})";

        Debug.Log($"[MissionTracker] {droneName} → C{record.customerNumber:D3}: {status} " +
                  $"(planned={record.plannedArrival:F0}, actual={record.actualArrival:F0}, " +
                  $"battery={record.droneBatteryAtArrival:F0}%)");
    }

    // ================================================================
    //  Record Route Completion (existing, unchanged)
    // ================================================================

    public int BeginRoute(string droneName, PlannedRoute route)
    {
        _routeCounter++;

        var spec = GetDroneSpec(droneName);

        var record = new RouteRecord
        {
            droneName = droneName,
            routeIndex = _routeCounter,
            customerCount = route.customerCount,
            totalDemand = route.totalDemand,
            vehicleCapacity = route.vehicleCapacity,
            startTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time,
            totalDistanceMeters = route.totalDistance,
            batteryStartPercent = spec != null ? spec.BatteryPercent : 100f,
            strategy = _strategyName,
            completed = false
        };

        _routeRecords.Add(record);
        return _routeCounter;
    }

    public void EndRoute(string droneName, int routeIndex)
    {
        var record = _routeRecords.FirstOrDefault(r =>
            r.droneName == droneName && r.routeIndex == routeIndex);

        if (record == null) return;

        float simTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;

        record.endTime = simTime;
        record.totalTime = simTime - record.startTime;
        record.completed = true;

        // Recalculate total distance from actual leg records
        var routeLegs = _legRecords.Where(l =>
            l.droneName == droneName && l.routeIndex == routeIndex).ToList();
        if (routeLegs.Count > 0)
            record.totalDistanceMeters = routeLegs.Sum(l => l.actualFlownDistM);

        var routeStops = _stopRecords.Where(s =>
            s.droneName == droneName && s.routeIndex == routeIndex).ToList();

        record.onTimeCount = routeStops.Count(s => !s.wasLate);
        record.lateCount = routeStops.Count(s => s.wasLate);
        record.totalLateness = routeStops.Sum(s => s.lateness);

        var spec = GetDroneSpec(droneName);
        record.batteryEndPercent = spec != null ? spec.BatteryPercent : 0;
        record.batteryUsedWh = record.batteryStartPercent - record.batteryEndPercent;

        OnRouteRecorded?.Invoke(record);

        Debug.Log($"[MissionTracker] Route {routeIndex} completed by {droneName}: " +
                  $"{record.onTimeCount}/{record.customerCount} on time, " +
                  $"actual distance={record.totalDistanceMeters:F0}m, " +
                  $"battery {record.batteryStartPercent:F0}%→{record.batteryEndPercent:F0}%");
    }

    // ================================================================
    //  Summary (enhanced)
    // ================================================================

    public string GetMissionSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Mission Summary ===");
        sb.AppendLine($"Dataset: {_datasetName}");
        sb.AppendLine($"Strategy: {_strategyName}");
        sb.AppendLine();

        int totalCustomers = _stopRecords.Count;
        sb.AppendLine($"Deliveries: {totalCustomers}");
        sb.AppendLine($"On time: {TotalOnTime} ({OnTimePercent:F1}%)");
        sb.AppendLine($"Late: {TotalLate}");

        if (TotalLate > 0)
        {
            float avgLateness = _stopRecords.Where(s => s.wasLate).Average(s => s.lateness);
            float maxLateness = _stopRecords.Where(s => s.wasLate).Max(s => s.lateness);
            sb.AppendLine($"Avg lateness: {avgLateness:F1} sim_time_units");
            sb.AppendLine($"Max lateness: {maxLateness:F1} sim_time_units");
        }

        sb.AppendLine();
        sb.AppendLine($"=== Leg Statistics ===");
        sb.AppendLine($"Total legs: {_legRecords.Count}");
        if (_legRecords.Count > 0)
        {
            float totalStraight = _legRecords.Sum(l => l.straightLineDistM);
            float totalActual = _legRecords.Sum(l => l.actualFlownDistM);
            float detourRatio = totalStraight > 0 ? totalActual / totalStraight : 1f;
            sb.AppendLine($"Total straight-line distance: {totalStraight:F0}m ({totalStraight / 1000f:F1}km)");
            sb.AppendLine($"Total actual flown distance: {totalActual:F0}m ({totalActual / 1000f:F1}km)");
            sb.AppendLine($"Detour ratio: {detourRatio:F2}x (1.0 = no detour)");
            sb.AppendLine($"Avg speed: {_legRecords.Average(l => l.avgSpeedMps):F1} m/s");
        }

        sb.AppendLine();
        sb.AppendLine($"=== Route Statistics ===");
        sb.AppendLine($"Routes completed: {_routeRecords.Count(r => r.completed)}/{_routeRecords.Count}");

        if (_routeRecords.Count > 0)
        {
            float totalDist = _routeRecords.Sum(r => r.totalDistanceMeters);
            sb.AppendLine($"Total distance (actual): {totalDist:F0}m ({totalDist / 1000f:F1}km)");

            var completed = _routeRecords.Where(r => r.completed).ToList();
            if (completed.Count > 0)
            {
                float makespan = completed.Max(r => r.endTime) - _missionStartTime;
                float avgBatteryUsed = completed.Average(r => r.batteryUsedWh);
                sb.AppendLine($"Makespan: {makespan:F0} sim_time_units");
                sb.AppendLine($"Avg battery used per route: {avgBatteryUsed:F1}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"=== Units Reference ===");
        sb.AppendLine($"Distance: meters (m)");
        sb.AppendLine($"Time: sim_time_units (Solomon abstract units)");
        sb.AppendLine($"Speed: meters/second (m/s)");
        sb.AppendLine($"Coordinates: WGS84 degrees (longitude, latitude)");
        sb.AppendLine($"Altitude: meters above WGS84 ellipsoid");

        return sb.ToString();
    }

    // ================================================================
    //  Geo Helpers
    // ================================================================

    /// <summary>
    /// Compute total path distance by summing consecutive waypoint distances.
    /// This gives the ACTUAL flown distance including detours around obstacles.
    /// </summary>
    public static float ComputePathDistance(List<double3> path)
    {
        if (path == null || path.Count < 2) return 0f;

        double total = 0;
        for (int i = 1; i < path.Count; i++)
        {
            total += HaversineDistance(path[i - 1], path[i]);
        }
        return (float)total;
    }

    /// <summary>
    /// Haversine distance between two LLH points (ignores altitude difference).
    /// Returns meters.
    /// </summary>
    public static double HaversineDistance(double3 a, double3 b)
    {
        const double R = 6371000.0; // Earth radius in meters

        double lat1 = a.y * System.Math.PI / 180.0;
        double lat2 = b.y * System.Math.PI / 180.0;
        double dLat = (b.y - a.y) * System.Math.PI / 180.0;
        double dLon = (b.x - a.x) * System.Math.PI / 180.0;

        double h = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                   System.Math.Cos(lat1) * System.Math.Cos(lat2) *
                   System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);

        double c = 2 * System.Math.Atan2(System.Math.Sqrt(h), System.Math.Sqrt(1 - h));

        double horizontalDist = R * c;

        // Include altitude difference for 3D distance
        double dAlt = b.z - a.z;
        return System.Math.Sqrt(horizontalDist * horizontalDist + dAlt * dAlt);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private DroneSpec GetDroneSpec(string droneName)
    {
        var cc = FindObjectOfType<DroneCommandCenter>();
        if (cc == null) return null;
        if (!cc.TryGetNav(droneName, out var nav)) return null;
        return nav.GetComponent<DroneSpec>();
    }
}