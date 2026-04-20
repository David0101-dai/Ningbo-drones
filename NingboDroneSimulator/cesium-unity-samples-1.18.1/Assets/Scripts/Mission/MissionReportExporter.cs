// Assets/Scripts/Routing/MissionReportExporter.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class MissionReportExporter : MonoBehaviour
{
    public static MissionReportExporter Instance;

    [Header("Export Settings")]
    public string exportFolder = "Reports";

    private static readonly string NL = System.Environment.NewLine;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public string ExportAll()
    {
        if (MissionTracker.Instance == null)
        {
            DLog.Warn("General","[Exporter] MissionTracker not found");
            return null;
        }

        string folder = GetExportFolder();
        Directory.CreateDirectory(folder);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string stopsFile = Path.Combine(folder, "stops_" + timestamp + ".csv");
        string routesFile = Path.Combine(folder, "routes_" + timestamp + ".csv");
        string legsFile = Path.Combine(folder, "legs_" + timestamp + ".csv");
        string summaryFile = Path.Combine(folder, "summary_" + timestamp + ".txt");

        int stopsCount = ExportStopsCSV(stopsFile);
        int routesCount = ExportRoutesCSV(routesFile);
        int legsCount = ExportLegsCSV(legsFile);
        ExportSummary(summaryFile);

        string msg = "[Exporter] Exported to " + folder + ":" + NL +
                     "  stops_" + timestamp + ".csv (" + stopsCount + " records)" + NL +
                     "  routes_" + timestamp + ".csv (" + routesCount + " records)" + NL +
                     "  legs_" + timestamp + ".csv (" + legsCount + " records)" + NL +
                     "  summary_" + timestamp + ".txt";

        Debug.Log(msg);
        return folder;
    }

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
            sb.AppendLine(r.droneName + "," + r.routeIndex + "," + r.stopIndex + "," +
                          r.customerNumber + "," + r.demand + "," +
                          r.plannedArrival.ToString("F1") + "," + r.actualArrival.ToString("F1") + "," +
                          r.readyTime.ToString("F1") + "," + r.dueTime.ToString("F1") + "," +
                          r.serviceTime.ToString("F1") + "," +
                          (!r.wasLate ? "YES" : "NO") + "," + (r.wasLate ? "YES" : "NO") + "," +
                          r.lateness.ToString("F1") + "," + r.waitTime.ToString("F1") + "," +
                          r.droneSpeedAtArrival.ToString("F1") + "," +
                          r.droneBatteryAtArrival.ToString("F1") + "," +
                          r.droneCargoAtArrival + "," +
                          r.longitude.ToString("F6") + "," + r.latitude.ToString("F6") + "," +
                          r.timestamp);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return records.Count;
    }

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
            sb.AppendLine(r.droneName + "," + r.routeIndex + "," + r.customerCount + "," +
                          r.totalDemand + "," + r.vehicleCapacity + "," +
                          r.startTime.ToString("F1") + "," + r.endTime.ToString("F1") + "," +
                          r.totalTime.ToString("F1") + "," +
                          r.totalDistanceMeters.ToString("F0") + "," +
                          r.onTimeCount + "," + r.lateCount + "," +
                          r.totalLateness.ToString("F1") + "," +
                          r.batteryStartPercent.ToString("F1") + "," +
                          r.batteryEndPercent.ToString("F1") + "," +
                          r.batteryUsedWh.ToString("F1") + "," +
                          r.strategy + "," + r.completed);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return records.Count;
    }

    public int ExportLegsCSV(string filePath)
    {
        var records = MissionTracker.Instance.LegRecords;
        if (records.Count == 0) return 0;

        var sb = new StringBuilder();
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
                l.droneName + "," + l.routeIndex + "," + l.legIndex + "," +
                l.originName + "," + l.originLongitude.ToString("F6") + "," +
                l.originLatitude.ToString("F6") + "," + l.originAltitude.ToString("F1") + "," +
                l.destName + "," + l.destLongitude.ToString("F6") + "," +
                l.destLatitude.ToString("F6") + "," + l.destAltitude.ToString("F1") + "," +
                l.straightLineDistM.ToString("F1") + "," + l.actualFlownDistM.ToString("F1") + "," +
                detourRatio.ToString("F3") + "," + l.waypointCount + "," +
                l.departureSimTime.ToString("F2") + "," + l.arrivalSimTime.ToString("F2") + "," +
                l.legDurationSimTime.ToString("F2") + "," +
                l.cruiseSpeedMps.ToString("F1") + "," + l.avgSpeedMps.ToString("F1") + "," +
                l.departBatteryPct.ToString("F1") + "," + l.arriveBatteryPct.ToString("F1") + "," +
                l.departCargoLoad + "," + l.arriveCargoLoad + "," +
                l.destReadyTime.ToString("F1") + "," + l.destDueTime.ToString("F1") + "," +
                (l.arrivedOnTime ? "YES" : "NO") + "," +
                l.distanceUnit + "," + l.timeUnit + "," + l.speedUnit + "," +
                l.altitudeUnit + "," + l.coordSystem);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log("[Exporter] Legs CSV: " + records.Count + " records written");
        return records.Count;
    }

    public void ExportSummary(string filePath)
    {
        string summary = MissionTracker.Instance.GetMissionSummary();
        File.WriteAllText(filePath, summary, Encoding.UTF8);
    }

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