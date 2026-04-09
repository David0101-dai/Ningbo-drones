// Assets/Scripts/UI/UIPanelManager.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UIPanelManager : MonoBehaviour
{
    // ================================================================
    //  Panels
    // ================================================================
    [Header("=== Panels ===")]
    [SerializeField] private GameObject defaultPanel;
    [SerializeField] private GameObject planningPanel;
    [SerializeField] private GameObject replayPanel;
    [SerializeField] private TMP_Text outputText;

    // ================================================================
    //  Default Mode UI
    // ================================================================
    [Header("=== Default Mode UI ===")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button pauseAllButton;
    [SerializeField] private Button resumeAllButton;
    [SerializeField] private Button enterPlanningButton;
    [SerializeField] private Button enterReplayButton;
    [SerializeField] private Button saveLogButton;

    // ================================================================
    //  Planning Mode UI
    // ================================================================
    [Header("=== Planning Mode UI ===")]
    [SerializeField] private Button exitPlanningButton;
    [SerializeField] private Button pickStartButton;
    [SerializeField] private Button pickEndButton;
    [SerializeField] private Button clearPickButton;
    [SerializeField] private Button applyRouteButton;
    [SerializeField] private TMP_Dropdown droneDropdown;
    [SerializeField] private TMP_Text statusText;

    // ================================================================
    //  Replay Mode UI
    // ================================================================
    [Header("=== Replay Mode UI ===")]
    [SerializeField] private Button exitReplayButton;
    [SerializeField] private TMP_Dropdown logFileDropdown;
    [SerializeField] private TMP_Dropdown replayDroneDropdown;
    [SerializeField] private Button startReplayButton;
    [SerializeField] private Button pauseReplayButton;
    [SerializeField] private Button stopReplayButton;
    [SerializeField] private TMP_Dropdown speedDropdown;
    [SerializeField] private Button saveLogInReplayButton;

    [Header("=== Mission Mode ===")]
    [SerializeField] private GameObject missionPanel;
    [SerializeField] private Button enterMissionButton;
    [SerializeField] private Button exitMissionButton;

    [Header("=== Optional Toggle ===")]
    [SerializeField] private Toggle pauseToggleOptional;

    // ================================================================
    //  Controllers
    // ================================================================
    [Header("=== Controllers ===")]
    [SerializeField] private MapPickController picker;
    [SerializeField] private SwitchView switchView;
    [SerializeField] private PlanningModeController planningController;
    [SerializeField] private DroneCommandCenter commandCenter;
    [SerializeField] private ApplyRuntimeRouteController applyRouteController;
    [SerializeField] private LLMManagerHttp llm;

    // ================================================================
    //  Auto-find child names
    // ================================================================
    [Header("=== Auto-find Names ===")]
    public string enterPlanningName   = "EnterPlanningButton";
    public string enterReplayName     = "EnterReplayButton";
    public string exitPlanningName    = "ExitPlanningButton";
    public string exitReplayName      = "ExitReplayButton";
    public string pickStartName       = "PickStartButton";
    public string pickEndName         = "PickEndButton";
    public string clearPickName       = "ClearPickButton";
    public string pauseAllName        = "PauseAllButton";
    public string resumeAllName       = "ResumeAllButton";
    public string applyRouteName      = "ApplyRouteButton";
    public string droneDropdownName   = "DroneDropdown";
    public string statusTextName      = "StatusText";
    public string saveLogName         = "SaveLogButton";
    public string logFileDropdownName     = "LogFileDropdown";
    public string replayDroneDropdownName = "ReplayDroneDropdown";
    public string startReplayName         = "StartReplayButton";
    public string pauseReplayName         = "PauseReplayButton";
    public string stopReplayName          = "StopReplayButton";
    public string speedDropdownName       = "SpeedDropdown";
    public string saveLogInReplayName     = "SaveLogButton2";

    // ================================================================
    //  Replay internal state
    // ================================================================
    private List<(string fileName, string fullPath)> _logFiles = new();
    private readonly float[] _speedOptions = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };
    private int _selectedSpeedIndex = 2;

    // ================================================================
    //  Awake
    // ================================================================
    void Awake()
    {
        if (!planningController) planningController = FindObjectOfType<PlanningModeController>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!applyRouteController) applyRouteController = FindObjectOfType<ApplyRuntimeRouteController>();
        if (!llm) llm = FindObjectOfType<LLMManagerHttp>();

        if (!enterPlanningButton) enterPlanningButton = FindButton(enterPlanningName);
        if (!enterReplayButton)   enterReplayButton   = FindButton(enterReplayName);
        if (!pauseAllButton)      pauseAllButton      = FindButton(pauseAllName);
        if (!resumeAllButton)     resumeAllButton     = FindButton(resumeAllName);
        if (!saveLogButton)       saveLogButton       = FindButton(saveLogName);

        if (!exitPlanningButton)  exitPlanningButton  = FindButton(exitPlanningName);
        if (!pickStartButton)     pickStartButton     = FindButton(pickStartName);
        if (!pickEndButton)       pickEndButton       = FindButton(pickEndName);
        if (!clearPickButton)     clearPickButton     = FindButton(clearPickName);
        if (!applyRouteButton)    applyRouteButton    = FindButton(applyRouteName);
        if (!droneDropdown)       droneDropdown       = FindDropdown(droneDropdownName);
        if (!statusText)          statusText          = FindTMPText(statusTextName);

        if (!exitReplayButton)      exitReplayButton      = FindButton(exitReplayName);
        if (!logFileDropdown)       logFileDropdown       = FindDropdown(logFileDropdownName);
        if (!replayDroneDropdown)   replayDroneDropdown   = FindDropdown(replayDroneDropdownName);
        if (!startReplayButton)     startReplayButton     = FindButton(startReplayName);
        if (!pauseReplayButton)     pauseReplayButton     = FindButton(pauseReplayName);
        if (!stopReplayButton)      stopReplayButton      = FindButton(stopReplayName);
        if (!speedDropdown)         speedDropdown         = FindDropdown(speedDropdownName);
        if (!saveLogInReplayButton) saveLogInReplayButton = FindButton(saveLogInReplayName);

        if (!enterMissionButton) enterMissionButton = FindButton("EnterMissionButton");
        if (!exitMissionButton) exitMissionButton = FindButton("ExitMissionButton");

        if (defaultPanel)  defaultPanel.SetActive(true);
        if (planningPanel) planningPanel.SetActive(false);
        if (replayPanel)   replayPanel.SetActive(false);
        if (missionPanel)  missionPanel.SetActive(false);

        BindDefaultEvents();
        BindPlanningEvents();
        BindReplayEvents();

        if (llm && outputText) llm.outputText = outputText;

        if (ReplayManager.Instance != null)
        {
            ReplayManager.Instance.OnStatusChanged += OnReplayStatusChanged;
            ReplayManager.Instance.OnReplayFinished += OnReplayFinished;
        }

        RefreshPlanningDropdown();
        RefreshPlanningStatus();
    }

    void OnDestroy()
    {
        if (ReplayManager.Instance != null)
        {
            ReplayManager.Instance.OnStatusChanged -= OnReplayStatusChanged;
            ReplayManager.Instance.OnReplayFinished -= OnReplayFinished;
        }
    }

    void OnEnable()
    {
        RefreshPlanningDropdown();
        RefreshPlanningStatus();
    }

    void Update()
    {
        if (Time.frameCount % 15 == 0)
            RefreshPlanningStatus();
    }

    // ================================================================
    //  DEFAULT MODE
    // ================================================================
    private void BindDefaultEvents()
    {
        BindButton(sendButton, OnSend);
        BindButton(pauseAllButton, () => commandCenter?.PauseAll(true));
        BindButton(resumeAllButton, () => commandCenter?.PauseAll(false));
        BindButton(enterPlanningButton, EnterPlanning);
        BindButton(enterReplayButton, EnterReplay);
        BindButton(saveLogButton, OnSaveLog);
        BindButton(enterMissionButton, EnterMission);

        if (pauseToggleOptional)
        {
            pauseToggleOptional.onValueChanged.RemoveAllListeners();
            pauseToggleOptional.onValueChanged.AddListener(v => commandCenter?.PauseAll(v));
        }
    }

    // ================================================================
    //  PLANNING MODE
    // ================================================================
    private void BindPlanningEvents()
    {
        BindButton(exitPlanningButton, ExitPlanning);
        BindButton(pickStartButton, () => picker?.SetPickStart());
        BindButton(pickEndButton, () => picker?.SetPickEnd());
        BindButton(clearPickButton, () => picker?.Clear());
        BindButton(applyRouteButton, () => applyRouteController?.ApplyToCurrentDrone());

        if (droneDropdown)
        {
            droneDropdown.onValueChanged.RemoveAllListeners();
            droneDropdown.onValueChanged.AddListener(OnPlanningDroneChanged);
        }
    }

    // ================================================================
    //  REPLAY MODE
    // ================================================================
    private void BindReplayEvents()
    {
        BindButton(exitReplayButton, ExitReplay);
        BindButton(startReplayButton, OnStartReplay);
        BindButton(pauseReplayButton, OnPauseReplay);
        BindButton(stopReplayButton, OnStopReplay);
        BindButton(saveLogInReplayButton, OnSaveLog);

        if (logFileDropdown)
        {
            logFileDropdown.onValueChanged.RemoveAllListeners();
            logFileDropdown.onValueChanged.AddListener(OnLogFileSelected);
        }

        if (replayDroneDropdown)
        {
            replayDroneDropdown.onValueChanged.RemoveAllListeners();
            replayDroneDropdown.onValueChanged.AddListener(OnReplayDroneSelected);
        }

        if (speedDropdown)
        {
            speedDropdown.onValueChanged.RemoveAllListeners();
            speedDropdown.onValueChanged.AddListener(OnSpeedChanged);
        }

        InitSpeedDropdown();
    }

    // ================================================================
    //  Mission Mode
    // ================================================================

    public void EnterMission()
    {
        ShowOnlyPanel(missionPanel);
        UpdateOutputText("Mission Mode: Manage fleet, points, and orders.");
    }

    public void ExitMission()
    {
        ShowOnlyPanel(defaultPanel);
        UpdateOutputText("Default Mode: Enter LLM commands.");
    }

    // ================================================================
    //  Panel Switching
    // ================================================================

    private void ShowOnlyPanel(GameObject panel)
    {
        if (defaultPanel)  defaultPanel.SetActive(panel == defaultPanel);
        if (planningPanel) planningPanel.SetActive(panel == planningPanel);
        if (replayPanel)   replayPanel.SetActive(panel == replayPanel);
        if (missionPanel)  missionPanel.SetActive(panel == missionPanel);
    }

    public void ResetToDefaultMode()
    {
        if (ReplayManager.Instance != null && ReplayManager.Instance.IsReplaying)
        {
            ShowOnlyPanel(replayPanel);
            UpdateReplayButtonStates();
            return;
        }

        ShowOnlyPanel(defaultPanel);
        UpdateOutputText("Default Mode: Enter LLM commands.");
        if (inputField)
        {
            inputField.ActivateInputField();
            inputField.Select();
        }
    }

    public void EnterPlanning()
    {
        ShowOnlyPanel(planningPanel);
        if (planningController) planningController.EnterPlanningMode();
        UpdateOutputText("Planning Mode: Pick start/end points.");
    }

    public void ExitPlanning()
    {
        ShowOnlyPanel(defaultPanel);
        if (planningController) planningController.ExitPlanningMode();
        UpdateOutputText("Default Mode: Enter LLM commands.");
    }

    public void EnterReplay()
    {
        ShowOnlyPanel(replayPanel);
        RefreshLogFileDropdown();
        UpdateReplayButtonStates();
        UpdateOutputText("Replay Mode: Select log file and drones, then click Start.");
    }

    public void ExitReplay()
    {
        if (ReplayManager.Instance != null && ReplayManager.Instance.IsReplaying)
            ReplayManager.Instance.StopReplay();

        ShowOnlyPanel(defaultPanel);
        UpdateOutputText("Default Mode: Enter LLM commands.");
    }

    // ================================================================
    //  Replay: Log File Dropdown
    // ================================================================

    private void RefreshLogFileDropdown()
    {
        if (!logFileDropdown) return;

        _logFiles = Logger.GetSavedLogFiles();

        logFileDropdown.ClearOptions();

        if (_logFiles.Count == 0)
        {
            logFileDropdown.AddOptions(new List<string> { "(no log files)" });
            UpdateOutputText("Replay Mode: No log files found.\\nFly and save a log first.");
            return;
        }

        var options = new List<string>();
        foreach (var (fileName, _) in _logFiles)
        {
            string display = FormatLogFileName(fileName);
            options.Add(display);
        }

        logFileDropdown.AddOptions(options);
        logFileDropdown.SetValueWithoutNotify(0);

        OnLogFileSelected(0);
    }

    private string FormatLogFileName(string fileName)
    {
        string name = fileName.Replace("FlightLog_", "").Replace(".json", "");
        if (name.Length >= 15)
        {
            try
            {
                string date = $"{name.Substring(0, 4)}-{name.Substring(4, 2)}-{name.Substring(6, 2)}";
                string time = $"{name.Substring(9, 2)}:{name.Substring(11, 2)}:{name.Substring(13, 2)}";
                return $"{date} {time}";
            }
            catch { }
        }
        return fileName;
    }

    public void OnLogFileSelected(int index)
    {
        if (index < 0 || index >= _logFiles.Count) return;

        string fullPath = _logFiles[index].fullPath;
        string fileName = _logFiles[index].fileName;

        if (ReplayManager.Instance == null)
        {
            UpdateOutputText("ReplayManager not found!");
            return;
        }

        UpdateOutputText($"Loading: {FormatLogFileName(fileName)}...");

        ReplayManager.Instance.LoadReplayFile(fullPath);

        StartCoroutine(RefreshReplayDroneDropdownDelayed());
    }

    private System.Collections.IEnumerator RefreshReplayDroneDropdownDelayed()
    {
        float timeout = 3f;
        while (!ReplayManager.Instance.IsLoaded && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        RefreshReplayDroneDropdown();
        UpdateReplayButtonStates();
    }

    // ================================================================
    //  Replay: Drone Selection
    // ================================================================

    private void RefreshReplayDroneDropdown()
    {
        if (!replayDroneDropdown) return;

        replayDroneDropdown.ClearOptions();

        var replay = ReplayManager.Instance;
        if (replay == null || !replay.IsLoaded || replay.DroneNamesInLog.Count == 0)
        {
            replayDroneDropdown.AddOptions(new List<string> { "(no data)" });
            return;
        }

        var options = new List<string>();
        options.Add("ALL (all drones)");
        foreach (var name in replay.DroneNamesInLog)
            options.Add(name);

        replayDroneDropdown.AddOptions(options);
        replayDroneDropdown.SetValueWithoutNotify(0);
    }

    public void OnReplayDroneSelected(int index)
    {
        var replay = ReplayManager.Instance;
        if (replay == null || !replay.IsLoaded) return;

        if (index == 0)
        {
            replay.SelectAllDrones();
            UpdateOutputText($"Selected: All drones ({replay.DroneNamesInLog.Count})");
        }
        else
        {
            int droneIndex = index - 1;
            if (droneIndex < replay.DroneNamesInLog.Count)
            {
                string droneName = replay.DroneNamesInLog[droneIndex];
                replay.SelectSingleDrone(droneName);
                UpdateOutputText($"Selected: {droneName}");
            }
        }
    }

    // ================================================================
    //  Replay: Speed Control
    // ================================================================

    private void InitSpeedDropdown()
    {
        if (!speedDropdown) return;

        speedDropdown.ClearOptions();
        var options = new List<string>();
        for (int i = 0; i < _speedOptions.Length; i++)
            options.Add($"{_speedOptions[i]}x");
        speedDropdown.AddOptions(options);

        _selectedSpeedIndex = 2;
        speedDropdown.SetValueWithoutNotify(_selectedSpeedIndex);
    }

    private void OnSpeedChanged(int index)
    {
        if (index < 0 || index >= _speedOptions.Length) return;

        _selectedSpeedIndex = index;
        float speed = _speedOptions[index];

        if (ReplayManager.Instance != null)
            ReplayManager.Instance.SetReplaySpeed(speed);
    }

    // ================================================================
    //  Replay: Control Buttons
    // ================================================================

    public void OnStartReplay()
    {
        var replay = ReplayManager.Instance;
        if (replay == null)
        {
            UpdateOutputText("ReplayManager not found!");
            return;
        }

        if (!replay.IsLoaded)
        {
            UpdateOutputText("Please select a log file first.");
            return;
        }

        if (replay.IsReplaying)
            replay.StopReplay();

        float speed = _speedOptions[_selectedSpeedIndex];
        replay.SetReplaySpeed(speed);

        if (replayDroneDropdown)
            OnReplayDroneSelected(replayDroneDropdown.value);

        replay.StartReplay();
        UpdateReplayButtonStates();
    }

    private void OnPauseReplay()
    {
        var replay = ReplayManager.Instance;
        if (replay == null || !replay.IsReplaying) return;

        replay.TogglePause();
        UpdateReplayButtonStates();
    }

    public void OnStopReplay()
    {
        var replay = ReplayManager.Instance;
        if (replay == null) return;

        replay.StopReplay();
        UpdateReplayButtonStates();
    }

    // ================================================================
    //  Replay: UI State
    // ================================================================

    private void UpdateReplayButtonStates()
    {
        var replay = ReplayManager.Instance;
        bool isLoaded = replay != null && replay.IsLoaded;
        bool isReplaying = replay != null && replay.IsReplaying;
        bool isPaused = replay != null && replay.IsPaused;

        if (startReplayButton)
            startReplayButton.interactable = isLoaded;

        if (pauseReplayButton)
        {
            pauseReplayButton.interactable = isReplaying;
            var btnText = pauseReplayButton.GetComponentInChildren<TMP_Text>();
            if (btnText)
                btnText.text = isPaused ? "Resume" : "Pause";
        }

        if (stopReplayButton)
            stopReplayButton.interactable = isReplaying;

        if (logFileDropdown)
            logFileDropdown.interactable = !isReplaying;
        if (replayDroneDropdown)
            replayDroneDropdown.interactable = !isReplaying;

        if (exitReplayButton)
            exitReplayButton.interactable = !isReplaying;
    }

    private void OnReplayStatusChanged(string msg)
    {
        UpdateOutputText(msg);
        UpdateReplayButtonStates();
    }

    private void OnReplayFinished()
    {
        UpdateReplayButtonStates();
        UpdateOutputText("Replay complete! Drone states restored.\\nSelect another file or exit replay mode.");
    }

    // ================================================================
    //  DEFAULT MODE handlers
    // ================================================================

    private void OnSend()
    {
        if (!llm || !inputField) return;
        string text = inputField.text;
        if (string.IsNullOrWhiteSpace(text)) return;
        llm.SendUserText(text);
        Debug.Log($"[UI] Input: {text}");

        inputField.text = "";
        inputField.ActivateInputField();
    }

    private void OnSaveLog()
    {
        if (Logger.Instance == null)
        {
            UpdateOutputText("Error: Logger not found");
            return;
        }

        string path = Logger.Instance.SaveLogAndContinue();

        if (!string.IsNullOrEmpty(path))
        {
            UpdateOutputText("Log saved: " + System.IO.Path.GetFileName(path) +
    System.Environment.NewLine + "New recording started.");

            // Refresh log file list if replay panel is open
            if (replayPanel && replayPanel.activeSelf)
                RefreshLogFileDropdown();
        }
        else
        {
            UpdateOutputText("No data to save (may already be saved).");
        }
    }

    // ================================================================
    //  PLANNING MODE handlers
    // ================================================================

    private void OnPlanningDroneChanged(int index)
    {
        if (!switchView) return;
        switchView.SelectDroneByIndex(index);
        RefreshPlanningStatus();
    }

    private void RefreshPlanningDropdown()
    {
        if (!droneDropdown || !switchView) return;

        int n = switchView.DroneCount;
        if (n <= 0) return;

        var options = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            Transform t = switchView.droneTargets[i];
            if (!t) { options.Add($"Drone_{i}"); continue; }

            var info = t.GetComponentInParent<DroneInfo>();
            options.Add(info ? info.gameObject.name : t.name);
        }

        droneDropdown.ClearOptions();
        droneDropdown.AddOptions(options);

        var cur = switchView.CurrentDroneTarget;
        int curIndex = 0;
        if (cur != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (switchView.droneTargets[i] == cur) { curIndex = i; break; }
            }
        }
        droneDropdown.SetValueWithoutNotify(curIndex);
    }

    private void RefreshPlanningStatus()
    {
        if (!statusText) return;

        string curName = "(none)";
        if (switchView && switchView.CurrentDroneTarget)
        {
            var info = switchView.CurrentDroneTarget.GetComponentInParent<DroneInfo>();
            curName = info ? info.gameObject.name : switchView.CurrentDroneTarget.name;
        }

        string pickMode = picker ? picker.mode.ToString() : "(picker missing)";
        string se = picker ? $"Start={(picker.HasStart ? "\\u2714" : "\\u2014")}  End={(picker.HasEnd ? "\\u2714" : "\\u2014")}" : "";

        string hint = "";
        if (picker && !picker.HasEnd) hint = "\\nHint: Pick End point first";

        statusText.text = $"Current: {curName}\\nPickMode: {pickMode}\\n{se}{hint}";
    }

    // ================================================================
    //  Status Update
    // ================================================================

    public void UpdateStatus()
    {
        if (!outputText || !picker || !switchView) return;

        Transform currentTarget = switchView.CurrentDroneTarget;
        string droneName = currentTarget
            ? currentTarget.GetComponentInParent<DroneInfo>()?.GetName() ?? "Unknown"
            : "None";

        string modeStr = picker.mode.ToString();
        Unity.Mathematics.double3 llh =
            (picker.mode == MapPickController.PickMode.PickStart && picker.HasStart) ? picker.StartLLH :
            (picker.mode == MapPickController.PickMode.PickEnd && picker.HasEnd) ? picker.EndLLH :
            new Unity.Mathematics.double3(0, 0, 0);

        UpdateOutputText($"Drone: {droneName}\\nPick mode: {modeStr}\\nPos: {llh.x:F4}, {llh.y:F4}, {llh.z:F1}m");
    }

    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    public void UpdateOutputText(string msg)
    {
        if (outputText) outputText.text = msg;
    }

    // ================================================================
    //  Utilities
    // ================================================================

    private void BindButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (!btn) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    private Button FindButton(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<Button>(true);
        foreach (var b in all)
            if (b && b.name == childName) return b;
        return null;
    }

    private TMP_Dropdown FindDropdown(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (var d in all)
            if (d && d.name == childName) return d;
        return null;
    }

    private TMP_Text FindTMPText(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in all)
            if (t && t.name == childName) return t;
        return null;
    }
}