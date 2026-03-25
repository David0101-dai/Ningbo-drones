// Assets/Scripts/Routing/MissionReportExporter.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports mission data to CSV files for analysis.
/// Now exports 4 files: stops, routes, legs, and summary.
/// </summary>
public class MissionReportExporter : MonoBehaviour
{
    public static MissionReportExporter Instance;

    [Header("Export Settings")]
    public string exportFolder = "Reports";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Export All Reports
    // ================================================================

    public string ExportAll()
    {
        if (MissionTracker.Instance == null)
        {
            Debug.LogWarning("[Exporter] MissionTracker not found");
            return null;
        }

        string folder = GetExportFolder();
        Directory.CreateDirectory(folder);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string stopsFile = Path.Combine(folder, $"stops_{timestamp}.csv");
        string routesFile = Path.Combine(folder, $"routes_{timestamp}.csv");
        string legsFile = Path.Combine(folder, $"legs_{timestamp}.csv");
        string summaryFile = Path.Combine(folder, $"summary_{timestamp}.txt");

        int stopsCount = ExportStopsCSV(stopsFile);
        int routesCount = ExportRoutesCSV(routesFile);
        int legsCount = ExportLegsCSV(legsFile);
        ExportSummary(summaryFile);

        string msg = $"[Exporter] Exported to {folder}:\\n" +
                     $"  stops_{timestamp}.csv ({stopsCount} records)\\n" +
                     $"  routes_{timestamp}.csv ({routesCount} records)\\n" +
                     $"  legs_{timestamp}.csv ({legsCount} records)\\n" +
                     $"  summary_{timestamp}.txt";

        Debug.Log(msg);
        return folder;
    }

    // ================================================================
    //  Stops CSV (existing, unchanged)
    // ================================================================

    public int ExportStopsCSV(string filePath)
    {
        var records = MissionTracker.Instance.StopRecords;
        if (records.Count == 0) return 0;

        var sb = new StringBuilder();

        sb.AppendLine("Drone,RouteIndex,StopIndex,CustomerNo,Demand," +
                      "PlannedArrival,ActualArrival,ReadyTime,DueTime,ServiceTime," +
                      "OnTime,Late,Lateness,WaitTime," +
                      "SpeedMps,BatteryPct,CargoLoad," +
                      "Longitude,Latitude,Timestamp");

        foreach (var r in records)
        {
            sb.AppendLine($"{r.droneName},{r.routeIndex},{r.stopIndex}," +
                          $"{r.customerNumber},{r.demand}," +
                          $"{r.plannedArrival:F1},{r.actualArrival:F1}," +
                          $"{r.readyTime:F1},{r.dueTime:F1},{r.serviceTime:F1}," +
                          $"{(!r.wasLate ? "YES" : "NO")},{(r.wasLate ? "YES" : "NO")}," +
                          $"{r.lateness:F1},{r.waitTime:F1}," +
                          $"{r.droneSpeedAtArrival:F1},{r.droneBatteryAtArrival:F1}," +
                          $"{r.droneCargoAtArrival}," +
                          $"{r.longitude:F6},{r.latitude:F6},{r.timestamp}");
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return records.Count;
    }

    // ================================================================
    //  Routes CSV (existing, unchanged)
    // ================================================================

    public int ExportRoutesCSV(string filePath)
    {
        var records = MissionTracker.Instance.RouteRecords;
        if (records.Count == 0) return 0;

        var sb = new StringBuilder();

        sb.AppendLine("Drone,RouteIndex,CustomerCount,TotalDemand,VehicleCapacity," +
                      "StartTime,EndTime,TotalTime,DistanceMeters," +
                      "OnTimeCount,LateCount,TotalLateness," +
                      "BatteryStartPct,BatteryEndPct,BatteryUsedPct," +
                      "Strategy,Completed");

        foreach (var r in records)
        {
            sb.AppendLine($"{r.droneName},{r.routeIndex},{r.customerCount}," +
                          $"{r.totalDemand},{r.vehicleCapacity}," +
                          $"{r.startTime:F1},{r.endTime:F1},{r.totalTime:F1}," +
                          $"{r.totalDistanceMeters:F0}," +
                          $"{r.onTimeCount},{r.lateCount},{r.totalLateness:F1}," +
                          $"{r.batteryStartPercent:F1},{r.batteryEndPercent:F1}," +
                          $"{r.batteryUsedWh:F1}," +
                          $"{r.strategy},{r.completed}");
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return records.Count;
    }

    // ================================================================
    //  Legs CSV (NEW — detailed per-segment flight data)
    // ================================================================

    public int ExportLegsCSV(string filePath)
    {
        var records = MissionTracker.Instance.LegRecords;
        if (records.Count == 0) return 0;

        var sb = new StringBuilder();

        // Header with full detail
        sb.AppendLine(
            "Drone,RouteIndex,LegIndex," +
            "OriginName,OriginLongitude_deg,OriginLatitude_deg,OriginAltitude_mWGS84," +
            "DestName,DestLongitude_deg,DestLatitude_deg,DestAltitude_mWGS84," +
            "StraightLineDist_m,ActualFlownDist_m,DetourRatio,WaypointCount," +
            "DepartureSimTime,ArrivalSimTime,LegDuration_simUnits," +
            "CruiseSpeed_mps,AvgSpeed_mps," +
            "DepartBattery_pct,ArriveBattery_pct," +
            "DepartCargo,ArriveCargo," +
            "DestReadyTime,DestDueTime,ArrivedOnTime," +
            "DistanceUnit,TimeUnit,SpeedUnit,AltitudeUnit,CoordSystem"
        );

        foreach (var l in records)
        {
            float detourRatio = l.straightLineDistM > 0
                ? l.actualFlownDistM / l.straightLineDistM
                : 1f;

            sb.AppendLine(
                $"{l.droneName},{l.routeIndex},{l.legIndex}," +

                $"{l.originName},{l.originLongitude:F6},{l.originLatitude:F6}," +
                $"{l.originAltitude:F1}," +

                $"{l.destName},{l.destLongitude:F6},{l.destLatitude:F6}," +
                $"{l.destAltitude:F1}," +

                $"{l.straightLineDistM:F1},{l.actualFlownDistM:F1}," +
                $"{detourRatio:F3},{l.waypointCount}," +

                $"{l.departureSimTime:F2},{l.arrivalSimTime:F2}," +
                $"{l.legDurationSimTime:F2}," +

                $"{l.cruiseSpeedMps:F1},{l.avgSpeedMps:F1}," +

                $"{l.departBatteryPct:F1},{l.arriveBatteryPct:F1}," +
                $"{l.departCargoLoad},{l.arriveCargoLoad}," +

                $"{l.destReadyTime:F1},{l.destDueTime:F1}," +
                $"{(l.arrivedOnTime ? "YES" : "NO")}," +

                $"{l.distanceUnit},{l.timeUnit},{l.speedUnit}," +
                $"{l.altitudeUnit},{l.coordSystem}"
            );
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[Exporter] Legs CSV: {records.Count} records written");
        return records.Count;
    }

    // ================================================================
    //  Summary Text
    // ================================================================

    public void ExportSummary(string filePath)
    {
        string summary = MissionTracker.Instance.GetMissionSummary();
        File.WriteAllText(filePath, summary, Encoding.UTF8);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private string GetExportFolder()
    {
#if UNITY_EDITOR
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot, exportFolder);
#else
        string exeDir = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(exeDir, exportFolder);
#endif
    }
}