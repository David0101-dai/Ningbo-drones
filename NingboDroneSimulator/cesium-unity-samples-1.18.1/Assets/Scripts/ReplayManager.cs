// Assets/Scripts/ReplayManager.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using CesiumForUnity;

public class ReplayManager : MonoBehaviour
{
    public static ReplayManager Instance;

    [Header("Replay Control")]
    public float replaySpeed = 1.0f;

    [Header("Debug")]
    public bool logReplayProgress = false;

    // ====== State ======
    public bool IsReplaying { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsLoaded { get; private set; }
    public float Progress { get; private set; }
    public float CurrentTime { get; private set; }
    public float TotalDuration { get; private set; }
    public string LoadedFileName { get; private set; }

    // ====== Events ======
    public System.Action<string> OnStatusChanged;
    public System.Action OnReplayFinished;

    // ====== Private ======
    private FlightLogData _loadedLog;
    private float _replayStartRealTime;
    private float _pausedAtTime;

    // Drone cache
    private Dictionary<string, DroneGeoNavigator> _droneCache = new();

    // Pre-split frames per drone for fast lookup
    private Dictionary<string, List<DroneFrame>> _framesPerDrone = new();

    // Saved states for restore
    private Dictionary<string, double3> _savedPositions = new();
    private Dictionary<string, bool> _savedPauseStates = new();

    // Selected drones (null = all)
    private HashSet<string> _selectedDrones = null;

    // Drone names in loaded log
    public List<string> DroneNamesInLog { get; private set; } = new();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ========================================================
    //  Load
    // ========================================================

    public void LoadReplayFile(string filePath)
    {
        StartCoroutine(LoadCoroutine(filePath));
    }

    private IEnumerator LoadCoroutine(string filePath)
    {
        IsLoaded = false;
        EmitStatus("Loading log file...");

        if (!File.Exists(filePath))
        {
            EmitStatus("Error: File not found - " + Path.GetFileName(filePath));
            yield break;
        }

        yield return null;

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (System.Exception e)
        {
            EmitStatus("Error: Read failed - " + e.Message);
            yield break;
        }

        yield return null;

        FlightLogData log;
        try
        {
            log = JsonUtility.FromJson<FlightLogData>(json);
        }
        catch (System.Exception e)
        {
            EmitStatus("Error: JSON parse failed - " + e.Message);
            yield break;
        }

        if (log == null || log.frames == null || log.frames.Count == 0)
        {
            EmitStatus("Error: Log file is empty or invalid");
            yield break;
        }

        _loadedLog = log;
        LoadedFileName = Path.GetFileName(filePath);

        // Sort by time
        _loadedLog.frames.Sort((a, b) => a.time.CompareTo(b.time));

        // Total duration
        TotalDuration = _loadedLog.frames[_loadedLog.frames.Count - 1].time;

        // Extract drone names
        DroneNamesInLog = _loadedLog.frames
            .Select(f => f.droneName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        // ====== Pre-split frames per drone (KEY FIX) ======
        _framesPerDrone.Clear();
        foreach (var frame in _loadedLog.frames)
        {
            if (string.IsNullOrEmpty(frame.droneName)) continue;

            if (!_framesPerDrone.ContainsKey(frame.droneName))
                _framesPerDrone[frame.droneName] = new List<DroneFrame>();

            _framesPerDrone[frame.droneName].Add(frame);
        }

        // Default: all selected
        _selectedDrones = null;
        IsLoaded = true;

        // Log debug info
        string droneInfo = "";
        foreach (var kvp in _framesPerDrone)
        {
            var droneFrames = kvp.Value;
            float minT = droneFrames[0].time;
            float maxT = droneFrames[droneFrames.Count - 1].time;
            droneInfo += $"\\n  {kvp.Key}: {droneFrames.Count} frames ({minT:F1}s - {maxT:F1}s)";
        }

        Debug.Log($"[Replay] Loaded: {LoadedFileName}" +
                  $"\\n  Total frames: {_loadedLog.frames.Count}" +
                  $"\\n  Duration: {TotalDuration:F1}s" +
                  $"\\n  Drones: {droneInfo}");

        EmitStatus($"Loaded: {LoadedFileName}\\n" +
                   $"Frames: {_loadedLog.frames.Count}, Duration: {TotalDuration:F1}s\\n" +
                   $"Drones: {string.Join(", ", DroneNamesInLog)}");
    }

    // ========================================================
    //  Drone selection
    // ========================================================

    public void SetSelectedDrones(HashSet<string> selected)
    {
        _selectedDrones = selected;
    }

    public void SelectAllDrones()
    {
        _selectedDrones = null;
        EmitStatus("Selected: All drones");
    }

    public void SelectSingleDrone(string droneName)
    {
        _selectedDrones = new HashSet<string> { droneName };
        EmitStatus("Selected: " + droneName);
    }

    private bool IsDroneSelected(string droneName)
    {
        if (_selectedDrones == null) return true;
        return _selectedDrones.Contains(droneName);
    }

    // ========================================================
    //  Replay control
    // ========================================================

    public void StartReplay()
    {
        if (!IsLoaded || _loadedLog == null)
        {
            EmitStatus("Error: Please load a log file first");
            return;
        }

        CacheDrones();

        if (_droneCache.Count == 0)
        {
            EmitStatus("Error: No drones found in scene");
            return;
        }

        SaveDroneStates();

        // Pause all drones' normal flight
        foreach (var kvp in _droneCache)
        {
            kvp.Value.SetStop(DroneGeoNavigator.StopReason.External, true);
        }

        _replayStartRealTime = Time.time;
        _pausedAtTime = 0f;
        CurrentTime = 0f;
        Progress = 0f;
        IsReplaying = true;
        IsPaused = false;

        int selectedCount = _selectedDrones?.Count ?? DroneNamesInLog.Count;
        EmitStatus($"Replay started ({selectedCount}/{DroneNamesInLog.Count} drones, speed: {replaySpeed}x)");
    }

    public void TogglePause()
    {
        if (!IsReplaying) return;

        if (IsPaused)
        {
            _replayStartRealTime = Time.time - _pausedAtTime / Mathf.Max(0.01f, replaySpeed);
            IsPaused = false;
            EmitStatus("Replay resumed");
        }
        else
        {
            _pausedAtTime = CurrentTime;
            IsPaused = true;
            EmitStatus($"Replay paused ({Progress * 100f:F0}%)");
        }
    }

    public void StopReplay()
    {
        if (!IsReplaying) return;

        IsReplaying = false;
        IsPaused = false;

        foreach (var kvp in _droneCache)
        {
            if (kvp.Value != null)
                kvp.Value.ExitReplayMode();
        }

        RestoreDroneStates();

        EmitStatus("Replay stopped, drone states restored");
        OnReplayFinished?.Invoke();
    }

    public void SetReplaySpeed(float speed)
    {
        if (IsReplaying && !IsPaused)
        {
            _replayStartRealTime = Time.time - CurrentTime / Mathf.Max(0.01f, speed);
        }
        replaySpeed = Mathf.Max(0.1f, speed);
    }

    // ========================================================
    //  Update: core replay loop
    // ========================================================

    void Update()
    {
        if (!IsReplaying || IsPaused || _loadedLog == null) return;

        CurrentTime = (Time.time - _replayStartRealTime) * replaySpeed;
        Progress = Mathf.Clamp01(CurrentTime / Mathf.Max(0.001f, TotalDuration));

        if (CurrentTime >= TotalDuration)
        {
            Progress = 1f;
            EmitStatus("Replay complete!");
            StopReplay();
            return;
        }

        // ====== Core: apply frames for each selected drone ======
        ApplyFramesAtCurrentTime();

        if (logReplayProgress && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Replay] {Progress * 100f:F0}% | {CurrentTime:F1}s / {TotalDuration:F1}s");
        }
    }

    private void ApplyFramesAtCurrentTime()
    {
        foreach (string droneName in DroneNamesInLog)
        {
            if (!IsDroneSelected(droneName)) continue;
            if (!_droneCache.TryGetValue(droneName, out var nav)) continue;
            if (nav == null) continue;

            if (!_framesPerDrone.TryGetValue(droneName, out var droneFrames)) continue;
            if (droneFrames.Count == 0) continue;

            // Get interpolated position between two nearest frames
            double3 interpolatedLLH = InterpolatePosition(droneFrames, CurrentTime);
            nav.SetReplayPosition(interpolatedLLH);
        }
    }

    /// <summary>
    /// Linearly interpolate drone position between two recorded frames
    /// </summary>
    private double3 InterpolatePosition(List<DroneFrame> frames, float targetTime)
    {
        // Edge cases: before first frame or after last frame
        if (targetTime <= frames[0].time)
            return frames[0].llh;

        if (targetTime >= frames[frames.Count - 1].time)
            return frames[frames.Count - 1].llh;

        // Binary search: find the largest index where time <= targetTime
        int lo = 0, hi = frames.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (frames[mid].time <= targetTime)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        // hi = index of frame just before or at targetTime
        // lo = index of frame just after targetTime

        int indexA = Mathf.Clamp(hi, 0, frames.Count - 2);
        int indexB = indexA + 1;

        DroneFrame frameA = frames[indexA];
        DroneFrame frameB = frames[indexB];

        // Calculate interpolation factor (0.0 ~ 1.0)
        float timeDiff = frameB.time - frameA.time;
        if (timeDiff < 0.001f)
            return frameA.llh;

        float t = (targetTime - frameA.time) / timeDiff;
        t = Mathf.Clamp01(t);

        // Linearly interpolate longitude, latitude, height
        double3 result = new double3(
            frameA.llh.x + (frameB.llh.x - frameA.llh.x) * t,
            frameA.llh.y + (frameB.llh.y - frameA.llh.y) * t,
            frameA.llh.z + (frameB.llh.z - frameA.llh.z) * t
        );

        return result;
    }

    // ========================================================
    //  Helpers
    // ========================================================

    private void CacheDrones()
    {
        _droneCache.Clear();
        foreach (var info in FindObjectsOfType<DroneInfo>(true))
        {
            var nav = info.GetComponent<DroneGeoNavigator>();
            if (nav != null)
            {
                _droneCache[info.GetName()] = nav;
                // Also register by GameObject name for matching flexibility
                if (info.GetName() != info.gameObject.name)
                    _droneCache[info.gameObject.name] = nav;
            }
        }
        Debug.Log($"[Replay] Cached {_droneCache.Count} drone entries");
    }

    private void SaveDroneStates()
    {
        _savedPositions.Clear();
        _savedPauseStates.Clear();

        foreach (var kvp in _droneCache)
        {
            var nav = kvp.Value;
            if (nav == null) continue;

            string key = nav.gameObject.name; // Use consistent key
            if (_savedPositions.ContainsKey(key)) continue;

            var anchor = nav.GetComponent<CesiumGlobeAnchor>();
            if (anchor != null)
            {
                _savedPositions[key] = anchor.longitudeLatitudeHeight;
                _savedPauseStates[key] = nav.IsPaused();
            }
        }
    }

    private void RestoreDroneStates()
    {
        foreach (var kvp in _droneCache)
        {
            var nav = kvp.Value;
            if (nav == null) continue;

            string key = nav.gameObject.name;

            if (_savedPositions.TryGetValue(key, out var pos))
            {
                var anchor = nav.GetComponent<CesiumGlobeAnchor>();
                if (anchor != null)
                    anchor.longitudeLatitudeHeight = pos;
            }

            if (_savedPauseStates.TryGetValue(key, out bool wasPaused))
                nav.SetStop(DroneGeoNavigator.StopReason.External, wasPaused);
            else
                nav.SetStop(DroneGeoNavigator.StopReason.External, false);
        }
    }

    private void EmitStatus(string msg)
    {
        Debug.Log($"[Replay] {msg}");
        OnStatusChanged?.Invoke(msg);
    }
}