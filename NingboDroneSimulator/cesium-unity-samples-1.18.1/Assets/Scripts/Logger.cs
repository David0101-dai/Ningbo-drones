// Assets/Scripts/Logger.cs
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

public class Logger : MonoBehaviour
{
    public static Logger Instance;

    [Header("Recording Settings")]
    [Tooltip("Sample interval in seconds. 0.5 = 2 frames per second per drone")]
    public float recordInterval = 0.5f;

    [Header("Debug")]
    public bool logOnSave = true;

    // ====== Internal State ======
    private FlightLogData _currentLog;
    private float _sessionStartTime;
    private bool _isRecording = false;
    private bool _saved = false;
    private bool _sessionInitialized = false;

    // Per-drone last record time
    private readonly Dictionary<string, float> _lastRecordTime = new();

    // ====== Public Properties ======
    public bool IsRecording => _isRecording;
    public int FrameCount => _currentLog?.frames?.Count ?? 0;

    // ====== Path Helper ======
    /// <summary>
    /// Returns a folder inside the project directory (Editor) or next to the .exe (Build).
    /// Keeps C: drive clean.
    /// </summary>
    private static string GetLogDirectory()
    {
        #if UNITY_EDITOR
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, "Logs", "FlightLogs");
        #else
            string exeDir = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(exeDir, "Logs", "FlightLogs");
        #endif
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (!_sessionInitialized)
            StartNewSession();
    }

    /// <summary>
    /// Start a new recording session
    /// </summary>
    public void StartNewSession()
    {
        if (_sessionInitialized && _isRecording)
        {
            Debug.LogWarning("[Logger] Session already active, ignoring StartNewSession call");
            return;
        }

        _currentLog = new FlightLogData
        {
            sessionId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            startTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            frames = new List<DroneFrame>()
        };
        _sessionStartTime = Time.time;
        _isRecording = true;
        _saved = false;
        _sessionInitialized = true;
        _lastRecordTime.Clear();

        Debug.Log($"[Logger] New session started: {_currentLog.sessionId}, startTime={_sessionStartTime:F2}");
    }

    /// <summary>
    /// Stop recording (does not save - call SaveLog to write file)
    /// </summary>
    public void StopRecording()
    {
        _isRecording = false;
        Debug.Log($"[Logger] Recording stopped, {FrameCount} frames recorded");
    }

    /// <summary>
    /// Record a frame (with sample interval control)
    /// </summary>
    public void RecordFrame(string droneName, double3 llh, float speed,
                            string stopReason, string command = "", bool isColliding = false)
    {
        if (!_isRecording || _currentLog == null) return;

        if (llh.x == 0 && llh.y == 0 && llh.z == 0) return;

        float now = Time.time;
        if (_lastRecordTime.TryGetValue(droneName, out float lastTime))
        {
            if (now - lastTime < recordInterval) return;
        }
        _lastRecordTime[droneName] = now;

        float frameTime = now - _sessionStartTime;

        var frame = new DroneFrame
        {
            time = frameTime,
            droneName = droneName,
            llh = llh,
            speed = speed,
            stopReason = stopReason,
            currentCommand = command,
            isColliding = isColliding
        };

        _currentLog.frames.Add(frame);
    }

    /// <summary>
    /// Save log to file. Returns file path on success, null on failure.
    /// </summary>
    public string SaveLog()
    {
        if (_saved)
        {
            Debug.Log("[Logger] Already saved this session");
            return null;
        }

        if (_currentLog == null || _currentLog.frames.Count == 0)
        {
            Debug.LogWarning("[Logger] No data recorded");
            return null;
        }

        _currentLog.frames.Sort((a, b) => a.time.CompareTo(b.time));

        string json = JsonUtility.ToJson(_currentLog, true);
        string fileName = $"FlightLog_{_currentLog.sessionId}.json";

        // ★ Changed: save to project directory instead of C: drive
        string dir = GetLogDirectory();
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);

        File.WriteAllText(path, json);
        _saved = true;
        _isRecording = false;

        if (logOnSave)
        {
            float minTime = _currentLog.frames[0].time;
            float maxTime = _currentLog.frames[_currentLog.frames.Count - 1].time;
            var droneNames = new HashSet<string>();
            foreach (var f in _currentLog.frames)
                droneNames.Add(f.droneName);

            Debug.Log($"[Logger] Saved: {path}\\n" +
                      $"  Frames: {_currentLog.frames.Count}\\n" +
                      $"  Time range: {minTime:F2}s - {maxTime:F2}s\\n" +
                      $"  Drones: {string.Join(", ", droneNames)}");
        }

        return path;
    }

    /// <summary>
    /// Get list of all saved log files (fileName, fullPath)
    /// </summary>
    public static List<(string fileName, string fullPath)> GetSavedLogFiles()
    {
        var result = new List<(string, string)>();

        // ★ Changed: look in project directory instead of C: drive
        string dir = GetLogDirectory();

        if (!Directory.Exists(dir)) return result;

        var files = Directory.GetFiles(dir, "FlightLog_*.json")
                             .OrderByDescending(f => f)
                             .ToArray();

        foreach (var f in files)
        {
            result.Add((Path.GetFileName(f), f));
        }

        return result;
    }

    void OnApplicationQuit()
    {
        if (_isRecording && !_saved) SaveLog();
    }

    void OnDestroy()
    {
        if (_isRecording && !_saved) SaveLog();
    }
}