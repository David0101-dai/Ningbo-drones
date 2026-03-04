// Assets/Scripts/SceneStateProvider.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class SceneStateProvider : MonoBehaviour
{
    [Header("References")]
    public DroneCommandCenter commandCenter;

    [Header("Environment Info")]
    public string cityName = "Ningbo";

    // ================================================================
    //  Structured State (JSON-serializable)
    // ================================================================

    [System.Serializable]
    public class SceneState
    {
        public string city;
        public float simulationTimeSec;
        public int droneCount;
        public List<DroneState> drones = new List<DroneState>();
        public List<string> availableRoutes = new List<string>();
        public FleetSummary summary = new FleetSummary();
    }

    [System.Serializable]
    public class DroneState
    {
        public string name;
        public string missionState;
        public string currentRoute;
        public float speedMps;
        public float progressPercent;
        public bool isPaused;
        public bool isIdle;
        public double longitude;
        public double latitude;
        public double heightMeters;
    }

    [System.Serializable]
    public class FleetSummary
    {
        public int total;
        public int flying;
        public int idle;
        public int paused;
    }

    // ================================================================
    //  Awake
    // ================================================================

    void Awake()
    {
        if (!commandCenter)
            commandCenter = FindObjectOfType<DroneCommandCenter>();
    }

    // ================================================================
    //  Public API: Get State
    // ================================================================

    /// <summary>
    /// Build complete scene state object
    /// </summary>
    public SceneState BuildState()
    {
        var state = new SceneState
        {
            city = cityName,
            simulationTimeSec = Time.time,
            droneCount = commandCenter != null ? commandCenter.DroneCount : 0,
        };

        // Available routes
        if (commandCenter != null)
        {
            state.availableRoutes = commandCenter.GetAvailableRoutes();
        }

        // Drone states
        if (commandCenter != null)
        {
            var allSnapshots = commandCenter.GetFleetSnapshot();
            var seen = new HashSet<string>();
            var snapshots = new List<DroneInfo.Snapshot>();
            foreach (var s in allSnapshots)
            {
                if (seen.Add(s.name))
                    snapshots.Add(s);
            }

            int flying = 0, idle = 0, paused = 0;

            foreach (var snap in snapshots)
            {
                var ds = new DroneState
                {
                    name = snap.name,
                    missionState = snap.missionState,
                    currentRoute = snap.currentRoute,
                    speedMps = snap.speedMps,
                    progressPercent = snap.progressPercent,
                    isPaused = snap.isPaused,
                    isIdle = snap.isIdle,
                    longitude = snap.positionLLH.x,
                    latitude = snap.positionLLH.y,
                    heightMeters = snap.positionLLH.z,
                };
                state.drones.Add(ds);

                // Count summary
                if (snap.isPaused) paused++;
                else if (snap.isIdle) idle++;
                else flying++;
            }

            state.summary = new FleetSummary
            {
                total = snapshots.Count,
                flying = flying,
                idle = idle,
                paused = paused
            };
        }

        return state;
    }

    /// <summary>
    /// Get scene state as JSON string (for LLM context)
    /// </summary>
    public string GetStateJson()
    {
        var state = BuildState();
        return JsonUtility.ToJson(state, true);
    }

    /// <summary>
    /// Get a short text summary (for debug / OutputText)
    /// </summary>
    public string GetStateSummary()
    {
        var state = BuildState();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"City: {state.city} | Time: {state.simulationTimeSec:F0}s");
        sb.AppendLine($"Fleet: {state.summary.total} drones " +
                      $"(Flying:{state.summary.flying} Idle:{state.summary.idle} Paused:{state.summary.paused})");
        sb.AppendLine($"Routes: {string.Join(", ", state.availableRoutes)}");
        sb.AppendLine("---");

        foreach (var d in state.drones)
        {
            sb.AppendLine($"  {d.name}: {d.missionState} | {d.speedMps:F1}m/s | " +
                          $"{d.progressPercent:F0}% | Route: {d.currentRoute}");
        }

        return sb.ToString();
    }
}