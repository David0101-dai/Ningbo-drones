// Assets/Scripts/Routing/MissionReportExporter.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports mission data to CSV files for analysis.
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

    /// <summary>
    /// Export both stop-level and route-level CSV reports.
    /// Returns the folder path where files were saved.
    /// </summary>
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
        string summaryFile = Path.Combine(folder, $"summary_{timestamp}.txt");

        int stopsCount = ExportStopsCSV(stopsFile);
        int routesCount = ExportRoutesCSV(routesFile);
        ExportSummary(summaryFile);

        string msg = $"[Exporter] Exported to {folder}:\\n" +
                     $"  stops_{timestamp}.csv ({stopsCount} records)\\n" +
                     $"  routes_{timestamp}.csv ({routesCount} records)\\n" +
                     $"  summary_{timestamp}.txt";

        Debug.Log(msg);
        return folder;
    }

    // ================================================================
    //  Stops CSV
    // ================================================================

    public int ExportStopsCSV(string filePath)
    {
        var records = MissionTracker.Instance.StopRecords;
        if (records.Count == 0) return 0;

        var sb = new StringBuilder();

        // Header
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
    //  Routes CSV
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

    // Assets/Scripts/Routing/MissionReportExporter.cs

    private string GetExportFolder()
    {
        // Save to project folder (next to Assets/) instead of C:\Users\...\AppData
        // In editor: D:\desk\FYP\Ningbo-drones\NingboDroneSimulator\...\Reports\
        // In build: next to the .exe file
        
        #if UNITY_EDITOR
            // Editor: save next to Assets folder
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            return System.IO.Path.Combine(projectRoot, exportFolder);
        #else
            // Build: save next to the .exe
            string exeDir = System.IO.Path.GetDirectoryName(Application.dataPath);
            return System.IO.Path.Combine(exeDir, exportFolder);
        #endif
    }

}