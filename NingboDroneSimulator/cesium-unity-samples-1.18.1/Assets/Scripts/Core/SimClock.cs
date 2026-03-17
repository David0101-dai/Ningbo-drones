// Assets/Scripts/Core/SimClock.cs
using UnityEngine;

/// <summary>
/// Simulation clock that maps real time to simulation time.
/// Solomon datasets use abstract time units; this clock lets us
/// control speed, pause, and convert between real/sim time.
/// </summary>
public class SimClock : MonoBehaviour
{
    public static SimClock Instance;

    [Header("Time Scale")]
    [Tooltip("How many simulation time units pass per real second")]
    public float simSpeedMultiplier = 1f;

    [Header("State")]
    [SerializeField] private float _simTime = 0f;
    [SerializeField] private bool _isPaused = false;
    [SerializeField] private bool _isRunning = false;

    [Header("Display")]
    [SerializeField] private string _displayTime = "00:00:00";

    // ====== Events ======
    public System.Action<float> OnSimTimeChanged;
    public System.Action<bool> OnPauseChanged;

    // ====== Properties ======

    /// <summary>Current simulation time (abstract units, matches Solomon data)</summary>
    public float SimTime => _simTime;

    /// <summary>Is the simulation clock running?</summary>
    public bool IsRunning => _isRunning && !_isPaused;

    /// <summary>Is paused?</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Formatted time string HH:MM:SS</summary>
    public string DisplayTime => _displayTime;

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Update()
    {
        if (!_isRunning || _isPaused) return;

        _simTime += Time.deltaTime * simSpeedMultiplier;
        UpdateDisplayTime();
        OnSimTimeChanged?.Invoke(_simTime);
    }

    // ================================================================
    //  Control API
    // ================================================================

    /// <summary>Start the simulation clock from time 0 (or specified start)</summary>
    public void StartSimulation(float startTime = 0f)
    {
        _simTime = startTime;
        _isRunning = true;
        _isPaused = false;
        UpdateDisplayTime();
        Debug.Log($"[SimClock] Started at simTime={startTime:F1}, speed={simSpeedMultiplier}x");
    }

    /// <summary>Stop the clock entirely</summary>
    public void StopSimulation()
    {
        _isRunning = false;
        Debug.Log($"[SimClock] Stopped at simTime={_simTime:F1}");
    }

    /// <summary>Pause/resume</summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        OnPauseChanged?.Invoke(paused);
    }

    /// <summary>Toggle pause</summary>
    public void TogglePause()
    {
        SetPaused(!_isPaused);
    }

    /// <summary>Set simulation speed multiplier</summary>
    public void SetSpeed(float multiplier)
    {
        simSpeedMultiplier = Mathf.Max(0.1f, multiplier);
        Debug.Log($"[SimClock] Speed set to {simSpeedMultiplier}x");
    }

    /// <summary>Jump to specific simulation time</summary>
    public void SetSimTime(float time)
    {
        _simTime = Mathf.Max(0f, time);
        UpdateDisplayTime();
    }

    // ================================================================
    //  Conversion Helpers
    // ================================================================

    /// <summary>
    /// Convert simulation time units to a display string.
    /// Assumes 1 unit ~= 1 minute for display purposes.
    /// </summary>
    private void UpdateDisplayTime()
    {
        int totalMinutes = Mathf.FloorToInt(_simTime);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        int seconds = Mathf.FloorToInt((_simTime - totalMinutes) * 60f);
        _displayTime = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// Get simulation time as formatted string with speed indicator
    /// </summary>
    public string GetStatusText()
    {
        string pauseStr = _isPaused ? " [PAUSED]" : "";
        return $"SimTime: {_displayTime} ({simSpeedMultiplier}x){pauseStr}";
    }
}