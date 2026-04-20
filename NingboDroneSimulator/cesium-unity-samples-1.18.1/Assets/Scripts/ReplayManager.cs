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

    // All drones (real + ghost) keyed by log name
    private readonly Dictionary<string, DroneGeoNavigator> _droneCache = new();

    // Ghost drone tracking
    private readonly HashSet<DroneGeoNavigator> _ghostNavSet = new();
    private readonly List<GameObject> _ghostDrones = new();

    // Frame data
    private readonly Dictionary<string, List<DroneFrame>> _framesPerDrone = new();

    // Saved states (real drones only)
    private readonly Dictionary<string, double3> _savedPositions = new();
    private readonly Dictionary<string, bool> _savedPauseStates = new();

    private HashSet<string> _selectedDrones = null;
    
    // ★ FIX: Color palette for ghost drones — distinct colors matching typical mission drone colors
    private static readonly Color[] GhostDroneColors = new Color[]
    {
        new Color(1.0f, 0.35f, 0.35f, 0.9f),  // V01 — Red
        new Color(0.35f, 0.85f, 1.0f, 0.9f),  // V02 — Cyan
        new Color(0.35f, 1.0f, 0.35f, 0.9f),  // V03 — Green
        new Color(1.0f, 0.85f, 0.0f,  0.9f),  // V04 — Yellow
        new Color(1.0f, 0.45f, 0.0f,  0.9f),  // V05 — Orange
        new Color(0.75f, 0.2f, 1.0f,  0.9f),  // V06 — Purple
        new Color(0.2f,  0.55f, 1.0f, 0.9f),  // V07 — Blue
        new Color(1.0f,  0.55f, 0.85f, 0.9f), // V08 — Pink
        new Color(0.3f,  1.0f,  0.75f, 0.9f), // V09 — Teal
        new Color(0.85f, 1.0f,  0.25f, 0.9f), // V10 — Lime
        new Color(1.0f,  0.9f,  0.5f,  0.9f), // V11 — Peach
        new Color(0.5f,  0.35f, 0.2f,  0.9f), // V12 — Brown
    };
    public List<string> DroneNamesInLog { get; private set; } = new();
    

    // Diagnostic counter
    private int _diagFrameCount = 0;
    private const int DiagLogFrames = 3;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ================================================================
    //  Load
    // ================================================================

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
        try { json = File.ReadAllText(filePath); }
        catch (System.Exception e)
        {
            EmitStatus("Error: Read failed - " + e.Message);
            yield break;
        }

        yield return null;

        FlightLogData log;
        try { log = JsonUtility.FromJson<FlightLogData>(json); }
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
        _loadedLog.frames.Sort((a, b) => a.time.CompareTo(b.time));
        TotalDuration = _loadedLog.frames[_loadedLog.frames.Count - 1].time;

        DroneNamesInLog = _loadedLog.frames
            .Select(f => f.droneName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct().OrderBy(n => n).ToList();

        _framesPerDrone.Clear();
        foreach (var frame in _loadedLog.frames)
        {
            if (string.IsNullOrEmpty(frame.droneName)) continue;
            if (!_framesPerDrone.ContainsKey(frame.droneName))
                _framesPerDrone[frame.droneName] = new List<DroneFrame>();
            _framesPerDrone[frame.droneName].Add(frame);
        }

        _selectedDrones = null;
        IsLoaded = true;

        // ★ FIX: Use real newlines, not \\n
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[Replay] Loaded: {LoadedFileName}");
        sb.AppendLine($"  Frames: {_loadedLog.frames.Count}, Duration: {TotalDuration:F1}s");
        foreach (var kvp in _framesPerDrone)
        {
            var df = kvp.Value;
            int zeros = df.Count(f => f.llh.x == 0 && f.llh.y == 0 && f.llh.z == 0);
            sb.Append($"  {kvp.Key}: {df.Count} frames " +
                      $"({df[0].time:F1}s - {df[df.Count - 1].time:F1}s)");
            if (zeros > 0) sb.Append($" [WARNING: {zeros} zero-LLH frames]");
            sb.AppendLine();
        }
        DLog.Info("Replay", sb.ToString());

        // 替换 LoadCoroutine 末尾的 EmitStatus 调用
        EmitStatus($"Loaded: {LoadedFileName} | {_loadedLog.frames.Count} frames | " +
                $"{TotalDuration:F1}s | {DroneNamesInLog.Count} drones");
    }

    // ================================================================
    //  Drone Selection
    // ================================================================

    public void SetSelectedDrones(HashSet<string> selected) => _selectedDrones = selected;

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

    private bool IsDroneSelected(string name) =>
        _selectedDrones == null || _selectedDrones.Contains(name);

    // ================================================================
    //  Replay Control
    // ================================================================

    public void StartReplay()
    {
        if (!IsLoaded || _loadedLog == null)
        {
            EmitStatus("Error: Please load a log file first");
            return;
        }

        // Clean up any leftover ghosts
        DestroyGhostDrones();

        // Cache real scene drones
        CacheDrones();

        // Create ghost drones for missing drones
        int ghostsCreated = 0;
        foreach (string droneName in DroneNamesInLog)
        {
            if (!_droneCache.ContainsKey(droneName))
            {
                if (CreateGhostDrone(droneName))
                    ghostsCreated++;
            }
        }

        // Validation
        int matched = 0, unmatched = 0;
        foreach (string logName in DroneNamesInLog)
        {
            if (_droneCache.ContainsKey(logName))
            {
                bool isGhost = _ghostNavSet.Contains(_droneCache[logName]);
                DLog.Info("Replay", $" ✓ '{logName}' ready " +
                          $"({(isGhost ? "ghost" : "real drone")})");
                matched++;
            }
            else
            {
                DLog.Warn("General",$"[Replay] ✗ '{logName}' NOT found");
                unmatched++;
            }
        }

        if (matched == 0)
        {
            EmitStatus("Error: No drones available for replay");
            return;
        }

        SaveDroneStates();

        // ★ Pause only REAL drones' normal flight
        foreach (var kvp in _droneCache)
        {
            if (!_ghostNavSet.Contains(kvp.Value))
                kvp.Value.SetStop(DroneGeoNavigator.StopReason.External, true);
        }
        // ★ FIX: Inject recorded trajectory as visualization path for ALL replay drones.
        // For real drones: their _pathLLH contains stale last-leg data from the mission.
        // For ghosts: CreateGhostDrone already called InjectPath, but doing it again is harmless.
        // InjectPath(vizPath, startNow:false) populates _pathLLH for OnRenderObject rendering.
        // Since isInReplayMode=true, LateUpdate won't advance _segmentIndex — full path is drawn.
        foreach (string droneName in DroneNamesInLog)
        {
            if (!IsDroneSelected(droneName)) continue;
            if (!_droneCache.TryGetValue(droneName, out var nav) || nav == null) continue;
            if (!_framesPerDrone.TryGetValue(droneName, out var frames) || frames.Count == 0) continue;

            // Enter replay mode BEFORE InjectPath so LateUpdate doesn't try to fly the path
            nav.SetReplayPosition(frames[0].llh);

            var vizPath = new List<double3>(frames.Count);
            foreach (var frame in frames)
                vizPath.Add(frame.llh);
            nav.InjectPath(vizPath, startNow: false);
        }

        // ★ FIX: Auto-advance start time to just before first activity
        float firstActiveTime = float.MaxValue;
        foreach (string droneName in DroneNamesInLog)
        {
            if (!IsDroneSelected(droneName)) continue;
            if (_framesPerDrone.TryGetValue(droneName, out var frames) && frames.Count > 0)
                firstActiveTime = Mathf.Min(firstActiveTime, frames[0].time);
        }
        if (firstActiveTime == float.MaxValue || firstActiveTime < 2f)
            firstActiveTime = 0f;

        float startOffset = Mathf.Max(0f, firstActiveTime - 2f);

        // ★ FIX: Time.unscaledTime — immune to Time.timeScale
        _replayStartRealTime = Time.unscaledTime - startOffset / Mathf.Max(0.01f, replaySpeed);
        _pausedAtTime = startOffset;
        CurrentTime = startOffset;
        Progress = 0f;
        IsReplaying = true;
        IsPaused = false;
        _diagFrameCount = 0;

        DLog.Info("Replay", $" Starting: timeScale={Time.timeScale}, " +
                  $"replaySpeed={replaySpeed}x, duration={TotalDuration:F1}s, " +
                  $"startOffset={startOffset:F1}s, " +
                  $"matched={matched}, unmatched={unmatched}, ghosts={ghostsCreated}");

        int selectedCount = _selectedDrones?.Count ?? DroneNamesInLog.Count;
        EmitStatus($"Replay started ({selectedCount}/{DroneNamesInLog.Count} drones, " +
                   $"speed: {replaySpeed}x, {ghostsCreated} ghost drones)");
    }

    public void TogglePause()
    {
        if (!IsReplaying) return;

        if (IsPaused)
        {
            _replayStartRealTime = Time.unscaledTime -
                                   _pausedAtTime / Mathf.Max(0.01f, replaySpeed);
            IsPaused = false;
            EmitStatus("Replay resumed");
        }
        else
        {
            _pausedAtTime = CurrentTime;
            IsPaused = true;
            EmitStatus($"Replay paused at {Progress * 100f:F0}%");
        }
    }

    public void StopReplay()
    {
        if (!IsReplaying) return;

        IsReplaying = false;
        IsPaused = false;

        // Exit replay mode for all drones
        foreach (var kvp in _droneCache)
        {
            if (kvp.Value != null)
                kvp.Value.ExitReplayMode();
        }

        RestoreDroneStates();
        DestroyGhostDrones();

        EmitStatus("Replay stopped, drone states restored");
        OnReplayFinished?.Invoke();
    }

    public void SetReplaySpeed(float speed)
    {
        if (IsReplaying && !IsPaused)
        {
            _replayStartRealTime = Time.unscaledTime -
                                   CurrentTime / Mathf.Max(0.01f, speed);
        }
        replaySpeed = Mathf.Max(0.1f, speed);
    }

    // ================================================================
    //  Update: Core Replay Loop
    // ================================================================

    void Update()
    {
        if (!IsReplaying || IsPaused || _loadedLog == null) return;

        // ★ FIX: Time.unscaledTime — immune to Time.timeScale
        CurrentTime = (Time.unscaledTime - _replayStartRealTime) * replaySpeed;
        Progress = Mathf.Clamp01(CurrentTime / Mathf.Max(0.001f, TotalDuration));

        if (CurrentTime >= TotalDuration)
        {
            Progress = 1f;
            EmitStatus("Replay complete!");
            StopReplay();
            return;
        }

        ApplyFramesAtCurrentTime();

        if (logReplayProgress && Time.frameCount % 60 == 0)
            DLog.Info("Replay", $" {Progress * 100f:F0}% | " +
                      $"{CurrentTime:F1}s / {TotalDuration:F1}s");
    }

    private void ApplyFramesAtCurrentTime()
    {
        foreach (string droneName in DroneNamesInLog)
        {
            if (!IsDroneSelected(droneName)) continue;
            if (!_droneCache.TryGetValue(droneName, out var nav) || nav == null) continue;
            if (!_framesPerDrone.TryGetValue(droneName, out var frames) ||
                frames.Count == 0) continue;

            double3 pos = InterpolatePosition(frames, CurrentTime);

            // ★ All drones (real and ghost) use SetReplayPosition
            // DroneGeoNavigator.LateUpdate() handles the actual position update
            nav.SetReplayPosition(pos);

            if (_diagFrameCount < DiagLogFrames)
            {
                bool isGhost = _ghostNavSet.Contains(nav);
                // Debug.Log($"[Replay DIAG] {droneName}" +
                   //       $"{(isGhost ? " (ghost)" : "")}: " +
                     //     $"t={CurrentTime:F2}s, " +
                       //   $"pos=({pos.x:F5}, {pos.y:F5}, {pos.z:F1})");
            }
        }
        _diagFrameCount++;
    }

    private double3 InterpolatePosition(List<DroneFrame> frames, float targetTime)
    {
        if (targetTime <= frames[0].time) return frames[0].llh;
        if (targetTime >= frames[frames.Count - 1].time)
            return frames[frames.Count - 1].llh;

        int lo = 0, hi = frames.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (frames[mid].time <= targetTime) lo = mid + 1;
            else hi = mid - 1;
        }

        int indexA = Mathf.Clamp(hi, 0, frames.Count - 2);
        int indexB = indexA + 1;
        DroneFrame fA = frames[indexA], fB = frames[indexB];

        float dt = fB.time - fA.time;
        if (dt < 0.001f) return fA.llh;

        float t = Mathf.Clamp01((targetTime - fA.time) / dt);
        return new double3(
            fA.llh.x + (fB.llh.x - fA.llh.x) * t,
            fA.llh.y + (fB.llh.y - fA.llh.y) * t,
            fA.llh.z + (fB.llh.z - fA.llh.z) * t
        );
    }

    // ================================================================
    //  ★ Ghost Drone Management (KEY FIX)
    // ================================================================

    /// <summary>
    /// Create a ghost drone that behaves exactly like a real drone
    /// but operates purely in replay mode.
    /// Key: uses nav.SetReplayPosition() — same path as real drones.
    /// </summary>
    private bool CreateGhostDrone(string droneName)
    {
        if (!_framesPerDrone.TryGetValue(droneName, out var frames) ||
            frames.Count == 0)
            return false;

        CesiumGeoreference geoRef = FindObjectOfType<CesiumGeoreference>();
        DroneFactory factory = FindObjectOfType<DroneFactory>();

        if (factory == null || factory.dronePrefab == null || geoRef == null)
        {
            DLog.Warn("General",$"[Replay] Cannot create ghost '{droneName}': " +
                             "missing DroneFactory/prefab/georeference");
            return false;
        }

        // Instantiate the full drone prefab (same as DroneFactory does)
        GameObject ghost = Instantiate(factory.dronePrefab, geoRef.transform);
        ghost.name = $"[Ghost] {droneName}";

        // ★ Only disable simulation scripts
        // (DroneGridAvoidance can interfere with replay paths)
        var avoidance = ghost.GetComponent<DroneGridAvoidance>();
        if (avoidance != null) avoidance.enabled = false;

        // ── Configure CesiumGlobeAnchor ──
        var anchor = ghost.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) anchor = ghost.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = frames[0].llh;

        // ── Configure DroneGeoNavigator ──
        var nav = ghost.GetComponent<DroneGeoNavigator>();
        if (nav == null)
        {
            DLog.Warn("General",$"[Replay] Ghost '{droneName}': no DroneGeoNavigator found");
            Destroy(ghost);
            return false;
        }

        nav.georeference = geoRef;
        nav.anchor = anchor;
        nav.waypointsParent = null;
        nav.startupDelay = 0f;
        nav.showPathGizmos = true;
        // ★ FIX: Assign distinct color from palette based on ghost creation order
        int colorIdx = _ghostDrones.Count % GhostDroneColors.Length;
        Color ghostColor = GhostDroneColors[colorIdx];
        nav.pathGizmoColor = ghostColor;
        nav.autoFaceForward = false;
        nav.ForceStartNow();

        // ★ FIX: Enter replay mode FIRST
        nav.SetReplayPosition(frames[0].llh);

        // ★ FIX: Use InjectPath instead of SetVisualizationPath.
        // InjectPath is the proven code path: it densifies, initializes
        // _segmentIndex=0, and populates _pathLLH for OnRenderObject.
        // startNow:false prevents it from overriding ForceStartNow.
        // Since isInReplayMode=true, LateUpdate won't advance _segmentIndex,
        // so the FULL trajectory is always drawn by OnRenderObject.
        var vizPath = new List<double3>(frames.Count);
        foreach (var frame in frames)
            vizPath.Add(frame.llh);
        nav.InjectPath(vizPath, startNow: false);

        DLog.Info("Replay", $" Ghost '{droneName}' path injected: " +
                $"{nav.GetPath().Count} points (from {frames.Count} log frames)");

        // ── Configure DroneInfo ──
        var info = ghost.GetComponent<DroneInfo>();
        if (info != null)
        {
            info.displayName = droneName;
            info.uiColor = ghostColor;   
            info.navigator = nav;
            // DroneSpec stays, but battery drain is cosmetic for replay
            var spec = ghost.GetComponent<DroneSpec>();
            if (spec != null)
            {
                spec.ResetFull();
                info.spec = spec;
            }
        }

        // ── Register camera target with SwitchView ──
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            Transform camTarget = ghost.transform.Find("CamTarget");
            if (camTarget == null)
            {
                var ct = new GameObject("CamTarget");
                ct.transform.SetParent(ghost.transform, false);
                ct.transform.localPosition = new Vector3(0, 2, 0);
                camTarget = ct.transform;
            }
            var targetList = new List<Transform>(
                switchView.droneTargets ?? new Transform[0]);
            if (!targetList.Contains(camTarget))
            {
                targetList.Add(camTarget);
                switchView.droneTargets = targetList.ToArray();
            }
        }

        // ── Register in caches ──
        _droneCache[droneName] = nav;
        _ghostNavSet.Add(nav);
        _ghostDrones.Add(ghost);

        DLog.Info("Replay", $" ✓ Ghost '{droneName}': {frames.Count} frames, " +
                  $"t=[{frames[0].time:F1}s~{frames[frames.Count - 1].time:F1}s], " +
                  $"start=({frames[0].llh.x:F4}, {frames[0].llh.y:F4}, " +
                  $"{frames[0].llh.z:F1})");
        return true;
    }

    private void DestroyGhostDrones()
    {
        if (_ghostDrones.Count == 0) return;

        // Remove ghost camera targets from SwitchView
        var switchView = FindObjectOfType<SwitchView>();
        if (switchView != null)
        {
            foreach (var g in _ghostDrones)
            {
                if (g == null) continue;
                Transform camTarget = g.transform.Find("CamTarget");
                if (camTarget != null)
                {
                    var list = new List<Transform>(
                        switchView.droneTargets ?? new Transform[0]);
                    list.Remove(camTarget);
                    switchView.droneTargets = list.ToArray();
                }
            }
            switchView.CleanNullTargets();
            switchView.EnsureCameraHasTarget();
        }

        // Remove ghost entries from _droneCache
        var keysToRemove = new List<string>();
        foreach (var kvp in _droneCache)
        {
            if (kvp.Value != null && _ghostNavSet.Contains(kvp.Value))
                keysToRemove.Add(kvp.Key);
        }
        foreach (var k in keysToRemove) _droneCache.Remove(k);

        // Destroy GameObjects
        int count = 0;
        foreach (var g in _ghostDrones)
        {
            if (g != null) { Destroy(g); count++; }
        }
        _ghostDrones.Clear();
        _ghostNavSet.Clear();

        if (count > 0)
            DLog.Info("Replay", $" Destroyed {count} ghost drones");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void CacheDrones()
    {
        _droneCache.Clear();

#if UNITY_2023_1_OR_NEWER
        var allInfos = FindObjectsByType<DroneInfo>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var allInfos = FindObjectsOfType<DroneInfo>(true);
#endif
        foreach (var info in allInfos)
        {
            var nav = info.GetComponent<DroneGeoNavigator>();
            if (nav == null) continue;

            // Skip leftover ghosts from previous replay (shouldn't exist, but safe)
            if (info.gameObject.name.StartsWith("[Ghost]")) continue;

            string infoName = info.GetName();
            string goName = info.gameObject.name;

            if (!_droneCache.ContainsKey(infoName))
                _droneCache[infoName] = nav;
            if (goName != infoName && !_droneCache.ContainsKey(goName))
                _droneCache[goName] = nav;
        }

        DLog.Info("Replay", $" Cached {_droneCache.Count} real drone entries: " +
                  $"[{string.Join(", ", _droneCache.Keys)}]");
    }

    private void SaveDroneStates()
    {
        _savedPositions.Clear();
        _savedPauseStates.Clear();

        foreach (var kvp in _droneCache)
        {
            var nav = kvp.Value;
            if (nav == null) continue;

            // ★ Skip ghost drones
            if (_ghostNavSet.Contains(nav)) continue;

            string key = nav.gameObject.name;
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

            // ★ Skip ghost drones (they'll be destroyed)
            if (_ghostNavSet.Contains(nav)) continue;

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
        // Console: multi-line messages show nicely when expanded
        DLog.Info("Replay", $" {msg}");

        // ★ FIX: UI receives clean single-line version (no literal \\n in panel)
        string uiMsg = msg.Replace("\\n", " | ").Replace("  ", " ").Trim();
        OnStatusChanged?.Invoke(uiMsg);
    }
}