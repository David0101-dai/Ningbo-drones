// Assets/Scripts/UI/SimSpeedController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Simulation speed control UI.
/// Sets Time.timeScale for drone movement speed AND
/// SimClock.simSpeedMultiplier for simulation time tracking.
/// Both use the SAME multiplier value to stay in sync.
/// </summary>
public class SimSpeedController : MonoBehaviour
{
    [Header("UI References (assign in Inspector)")]
    public TMP_Dropdown speedDropdown;
    public TMP_Text speedLabel;
    public Button pauseButton;

    [Header("Speed Options")]
    public float[] speedOptions = { 1f, 1.5f, 2f, 3f, 5f, 10f, 20f, 50f };

    private int _currentIndex = 0;
    private bool _isPaused = false;

    void Start()
    {
        SetupDropdown();
        SetupPauseButton();
        UpdateLabel();
    }

    void Update()
    {
        UpdateLabel();
    }

    // ════════════════════════════════════
    //  Setup
    // ════════════════════════════════════

    private void SetupDropdown()
    {
        if (speedDropdown == null) return;

        speedDropdown.ClearOptions();
        var options = new List<string>();
        for (int i = 0; i < speedOptions.Length; i++)
        {
            string label = speedOptions[i] <= 1f
                ? $"{speedOptions[i]}x (Normal)"
                : $"{speedOptions[i]}x";
            options.Add(label);
        }
        speedDropdown.AddOptions(options);
        speedDropdown.value = 0;
        speedDropdown.RefreshShownValue();

        speedDropdown.onValueChanged.RemoveAllListeners();
        speedDropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    private void SetupPauseButton()
    {
        if (pauseButton == null) return;
        pauseButton.onClick.RemoveAllListeners();
        pauseButton.onClick.AddListener(TogglePause);
        UpdatePauseButtonText();
    }

    // ════════════════════════════════════
    //  Speed Control
    // ════════════════════════════════════

    private void OnDropdownChanged(int index)
    {
        if (index < 0 || index >= speedOptions.Length) return;
        _currentIndex = index;
        ApplySpeed(speedOptions[index]);
    }

    private void ApplySpeed(float multiplier)
    {
        if (_isPaused) return;

        // Time.timeScale controls Unity physics/movement (drones fly faster)
        Time.timeScale = multiplier;

        // SimClock uses unscaledDeltaTime * multiplier (no double counting)
        if (SimClock.Instance != null)
            SimClock.Instance.SetSpeed(multiplier);

        UpdateLabel();
        Debug.Log($"[SimSpeed] Set to {multiplier}x");
    }

    // ════════════════════════════════════
    //  Pause
    // ════════════════════════════════════

    public void TogglePause()
    {
        _isPaused = !_isPaused;

        if (_isPaused)
        {
            Time.timeScale = 0f;
            if (SimClock.Instance != null)
                SimClock.Instance.SetPaused(true);
        }
        else
        {
            float speed = speedOptions[_currentIndex];
            Time.timeScale = speed;
            if (SimClock.Instance != null)
            {
                SimClock.Instance.SetPaused(false);
                SimClock.Instance.SetSpeed(speed);
            }
        }

        UpdateLabel();
        UpdatePauseButtonText();
        Debug.Log($"[SimSpeed] {(_isPaused ? "PAUSED" : $"RESUMED at {speedOptions[_currentIndex]}x")}");
    }

    // ════════════════════════════════════
    //  Display
    // ════════════════════════════════════

    private void UpdateLabel()
    {
        if (speedLabel == null) return;

        string simTime = SimClock.Instance != null
            ? SimClock.Instance.DisplayTime
            : "--:--:--";

        if (_isPaused)
            speedLabel.text = $"<color=#FF6666>PAUSED</color>  SimTime: {simTime}";
        else
            speedLabel.text = $"{speedOptions[_currentIndex]}x  SimTime: {simTime}";
    }

    private void UpdatePauseButtonText()
    {
        if (pauseButton == null) return;
        var txt = pauseButton.GetComponentInChildren<TMP_Text>();
        if (txt != null)
            txt.text = _isPaused ? "Resume" : "Pause";
    }

    // ════════════════════════════════════
    //  Public API
    // ════════════════════════════════════

    public void ResetSpeed()
    {
        _currentIndex = 0;
        _isPaused = false;

        if (speedDropdown != null)
        {
            speedDropdown.value = 0;
            speedDropdown.RefreshShownValue();
        }

        Time.timeScale = 1f;
        if (SimClock.Instance != null)
        {
            SimClock.Instance.SetSpeed(1f);
            SimClock.Instance.SetPaused(false);
        }

        UpdateLabel();
        UpdatePauseButtonText();
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    void OnApplicationQuit()
    {
        Time.timeScale = 1f;
    }
}