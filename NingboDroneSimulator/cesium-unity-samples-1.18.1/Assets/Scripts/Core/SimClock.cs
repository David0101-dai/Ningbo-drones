// Assets/Scripts/Core/SimClock.cs
using UnityEngine;

/// <summary>
/// Simulation clock that maps real time to simulation time.
///
/// KEY DESIGN: Uses Time.deltaTime (same as drone movement) to ensure
/// perfect synchronization. Time.timeScale controls both drone speed
/// AND clock speed simultaneously. No separate multiplier needed.
///
/// This avoids the maximumDeltaTime clamping desync that occurs when
/// SimClock uses unscaledDeltaTime * multiplier while drones use deltaTime.
/// </summary>
public class SimClock : MonoBehaviour
{
    public static SimClock Instance;

    [Header("State")]
    [SerializeField] private float _simTime = 0f;
    [SerializeField] private bool _isPaused = false;
    [SerializeField] private bool _isRunning = false;

    [Header("Display")]
    [SerializeField] private string _displayTime = "00:00:00";

    // Kept for API compatibility — but now only used for display/query.
    // Actual speed is controlled by Time.timeScale.
    [Header("Speed (read-only, set by SimSpeedController)")]
    [SerializeField] private float _displaySpeedMultiplier = 1f;

    // ====== Events ======
    public System.Action<float> OnSimTimeChanged;
    public System.Action<bool> OnPauseChanged;

    // ====== Properties ======
    public float SimTime => _simTime;
    public bool IsRunning => _isRunning && !_isPaused;
    public bool IsPaused => _isPaused;
    public string DisplayTime => _displayTime;
    public float simSpeedMultiplier => _displaySpeedMultiplier;

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

        // KEY FIX: Use Time.deltaTime — the SAME time source as
        // DroneGeoNavigator's movement calculation.
        //
        // When Time.timeScale = N:
        //   - Drone flies: cruiseSpeed * Time.deltaTime (accelerated by N)
        //   - SimClock:    _simTime += Time.deltaTime   (accelerated by N)
        //   - Both are subject to the SAME maximumDeltaTime clamping
        //   - Therefore they stay perfectly in sync at ANY timeScale
        //
        // Previous bug: unscaledDeltaTime * multiplier was NOT clamped
        // by maximumDeltaTime, but drone's deltaTime WAS clamped,
        // causing SimClock to run faster than drones at high timeScale.
        _simTime += Time.deltaTime;

        UpdateDisplayTime();
        OnSimTimeChanged?.Invoke(_simTime);
    }

    // ================================================================
    //  Control API
    // ================================================================

    public void StartSimulation(float startTime = 0f)
    {
        _simTime = startTime;
        _isRunning = true;
        _isPaused = false;
        UpdateDisplayTime();
        Debug.Log($"[SimClock] Started at simTime={startTime:F1}");
    }

    public void StopSimulation()
    {
        _isRunning = false;
        Debug.Log($"[SimClock] Stopped at simTime={_simTime:F1}");
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        OnPauseChanged?.Invoke(paused);
    }

    public void TogglePause()
    {
        SetPaused(!_isPaused);
    }

    /// <summary>
    /// Called by SimSpeedController for display purposes.
    /// Actual speed is controlled by Time.timeScale, not this value.
    /// </summary>
    public void SetSpeed(float multiplier)
    {
        _displaySpeedMultiplier = Mathf.Max(0.1f, multiplier);
        // Note: We do NOT use this for time calculation anymore.
        // Time.timeScale (set by SimSpeedController) handles everything.
        Debug.Log($"[SimClock] Display speed set to {_displaySpeedMultiplier}x");
    }

    public void SetSimTime(float time)
    {
        _simTime = Mathf.Max(0f, time);
        UpdateDisplayTime();
    }

    // ================================================================
    //  Display
    // ================================================================

    private void UpdateDisplayTime()
    {
        int totalMinutes = Mathf.FloorToInt(_simTime);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        int seconds = Mathf.FloorToInt((_simTime - totalMinutes) * 60f);
        _displayTime = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    public string GetStatusText()
    {
        string pauseStr = _isPaused ? " [PAUSED]" : "";
        return $"SimTime: {_displayTime} ({_displaySpeedMultiplier}x){pauseStr}";
    }
}