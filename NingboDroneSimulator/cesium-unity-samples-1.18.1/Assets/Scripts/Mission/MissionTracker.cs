// Assets/Scripts/Routing/MissionTracker.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        public bool wasEarly;        // Arrived before readyTime
        public float waitTime;       // Time spent waiting for readyTime
        public float lateness;       // How many time units late (0 if on time)
        public float droneSpeedAtArrival;
        public float droneBatteryAtArrival;
        public int droneCargoAtArrival;
        public double longitude;
        public double latitude;
        public string timestamp;     // Real wall-clock time
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

    // ====== Storage ======
    private readonly List<StopRecord> _stopRecords = new List<StopRecord>();
    private readonly List<RouteRecord> _routeRecords = new List<RouteRecord>();
    private int _routeCounter = 0;
    private float _missionStartTime;
    private string _datasetName = "";
    private string _strategyName = "";

    // ====== Properties ======
    public List<StopRecord> StopRecords => _stopRecords;
    public List<RouteRecord> RouteRecords => _routeRecords;
    public int TotalStopsCompleted => _stopRecords.Count;
    public int TotalOnTime => _stopRecords.Count(r => !r.wasLate);
    public int TotalLate => _stopRecords.Count(r => r.wasLate);
    public float OnTimePercent => _stopRecords.Count > 0
        ? (float)TotalOnTime / _stopRecords.Count * 100f : 0f;

    // ====== Events ======
    public System.Action<StopRecord> OnStopRecorded;
    public System.Action<RouteRecord> OnRouteRecorded;

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
        _routeCounter = 0;
        _missionStartTime = SimClock.Instance != null ? SimClock.Instance.SimTime : Time.time;
        _datasetName = datasetName;
        _strategyName = strategy;

        Debug.Log($"[MissionTracker] Mission started: {datasetName}, strategy={strategy}");
    }

    // ================================================================
    //  Record Stop Completion
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
    //  Record Route Completion
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

        // Count on-time/late for this route
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
                  $"battery {record.batteryStartPercent:F0}%→{record.batteryEndPercent:F0}%");
    }

    // ================================================================
    //  Summary
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
            sb.AppendLine($"Avg lateness: {avgLateness:F1} time units");
            sb.AppendLine($"Max lateness: {maxLateness:F1} time units");
        }

        sb.AppendLine();
        sb.AppendLine($"Routes completed: {_routeRecords.Count(r => r.completed)}/{_routeRecords.Count}");

        if (_routeRecords.Count > 0)
        {
            float totalDist = _routeRecords.Sum(r => r.totalDistanceMeters);
            sb.AppendLine($"Total distance: {totalDist:F0}m ({totalDist / 1000f:F1}km)");

            var completed = _routeRecords.Where(r => r.completed).ToList();
            if (completed.Count > 0)
            {
                float makespan = completed.Max(r => r.endTime) - _missionStartTime;
                float avgBatteryUsed = completed.Average(r => r.batteryUsedWh);
                sb.AppendLine($"Makespan: {makespan:F0} time units");
                sb.AppendLine($"Avg battery used: {avgBatteryUsed:F1}%");
            }
        }

        return sb.ToString();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private DroneSpec GetDroneSpec(string droneName)
    {
        var cc = DroneCommandCenter.FindObjectOfType<DroneCommandCenter>();
        if (cc == null) return null;
        if (!cc.TryGetNav(droneName, out var nav)) return null;
        return nav.GetComponent<DroneSpec>();
    }
}