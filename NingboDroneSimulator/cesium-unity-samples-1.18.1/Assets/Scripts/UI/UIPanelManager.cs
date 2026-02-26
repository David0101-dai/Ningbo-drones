// Assets/Scripts/UIPanelManager.cs
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
    [SerializeField] private GameObject replayPanel;        // ← 新增
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
    [SerializeField] private Button enterReplayButton;      // ← 新增
    [SerializeField] private Button saveLogButton;

    // ================================================================
    //  Planning Mode UI（保持不变）
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
    //  Replay Mode UI（全新）
    // ================================================================
    [Header("=== Replay Mode UI ===")]
    [SerializeField] private Button exitReplayButton;
    [SerializeField] private TMP_Dropdown logFileDropdown;
    [SerializeField] private TMP_Dropdown replayDroneDropdown;
    [SerializeField] private Button startReplayButton;
    [SerializeField] private Button pauseReplayButton;
    [SerializeField] private Button stopReplayButton;
    [SerializeField] private TMP_Dropdown speedDropdown;
    [SerializeField] private Button saveLogInReplayButton;  // 可选

    [Header("=== Optional Toggle ===")]
    [SerializeField] private Toggle pauseToggleOptional;

    // ================================================================
    //  Controllers
    // ================================================================
    [Header("=== Controllers ===")]
    [SerializeField] private MapPickController picker;
    [SerializeField] private SwitchView switchView;
    [SerializeField] private PlanningModeController planningController;
    [SerializeField] private DroneFleetController fleet;
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
    // Replay
    public string logFileDropdownName     = "LogFileDropdown";
    public string replayDroneDropdownName = "ReplayDroneDropdown";
    public string startReplayName         = "StartReplayButton";
    public string pauseReplayName         = "PauseReplayButton";
    public string stopReplayName          = "StopReplayButton";
    public string speedDropdownName       = "SpeedDropdown";
    public string saveLogInReplayName     = "SaveLogButton2";

    // ================================================================
    //  Replay 内部状态
    // ================================================================
    private List<(string fileName, string fullPath)> _logFiles = new();
    private readonly float[] _speedOptions = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };
    private int _selectedSpeedIndex = 2; // 默认 1x

    // ================================================================
    //  Awake
    // ================================================================
    void Awake()
    {
        // ---------- 自动查找 Controllers ----------
        if (!planningController) planningController = FindObjectOfType<PlanningModeController>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!applyRouteController) applyRouteController = FindObjectOfType<ApplyRuntimeRouteController>();
        if (!llm) llm = FindObjectOfType<LLMManagerHttp>();

        // ---------- 自动查找 UI 元素 ----------
        // Default
        if (!enterPlanningButton) enterPlanningButton = FindButton(enterPlanningName);
        if (!enterReplayButton)   enterReplayButton   = FindButton(enterReplayName);
        if (!pauseAllButton)      pauseAllButton      = FindButton(pauseAllName);
        if (!resumeAllButton)     resumeAllButton     = FindButton(resumeAllName);
        if (!saveLogButton)       saveLogButton       = FindButton(saveLogName);

        // Planning
        if (!exitPlanningButton)  exitPlanningButton  = FindButton(exitPlanningName);
        if (!pickStartButton)     pickStartButton     = FindButton(pickStartName);
        if (!pickEndButton)       pickEndButton       = FindButton(pickEndName);
        if (!clearPickButton)     clearPickButton     = FindButton(clearPickName);
        if (!applyRouteButton)    applyRouteButton    = FindButton(applyRouteName);
        if (!droneDropdown)       droneDropdown       = FindDropdown(droneDropdownName);
        if (!statusText)          statusText          = FindTMPText(statusTextName);

        // Replay
        if (!exitReplayButton)      exitReplayButton      = FindButton(exitReplayName);
        if (!logFileDropdown)       logFileDropdown       = FindDropdown(logFileDropdownName);
        if (!replayDroneDropdown)   replayDroneDropdown   = FindDropdown(replayDroneDropdownName);
        if (!startReplayButton)     startReplayButton     = FindButton(startReplayName);
        if (!pauseReplayButton)     pauseReplayButton     = FindButton(pauseReplayName);
        if (!stopReplayButton)      stopReplayButton      = FindButton(stopReplayName);
        if (!speedDropdown)         speedDropdown         = FindDropdown(speedDropdownName);
        if (!saveLogInReplayButton) saveLogInReplayButton = FindButton(saveLogInReplayName);

        // ---------- 面板初始状态 ----------
        if (defaultPanel)  defaultPanel.SetActive(true);
        if (planningPanel) planningPanel.SetActive(false);
        if (replayPanel)   replayPanel.SetActive(false);

        // ---------- 绑定事件 ----------
        BindDefaultEvents();
        BindPlanningEvents();
        BindReplayEvents();

        // ---------- LLM 输出绑定 ----------
        if (llm && outputText) llm.outputText = outputText;

        // ---------- ReplayManager 状态回调 ----------
        if (ReplayManager.Instance != null)
        {
            ReplayManager.Instance.OnStatusChanged += OnReplayStatusChanged;
            ReplayManager.Instance.OnReplayFinished += OnReplayFinished;
        }

        // ---------- 初始刷新 ----------
        RefreshPlanningDropdown();
        RefreshPlanningStatus();
    }

    void OnDestroy()
    {
        // 清理事件订阅
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
    //  DEFAULT MODE 事件绑定
    // ================================================================
    private void BindDefaultEvents()
    {
        BindButton(sendButton, OnSend);
        BindButton(pauseAllButton, () => fleet?.PauseAll(true));
        BindButton(resumeAllButton, () => fleet?.PauseAll(false));
        BindButton(enterPlanningButton, EnterPlanning);
        BindButton(enterReplayButton, EnterReplay);
        BindButton(saveLogButton, OnSaveLog);

        if (pauseToggleOptional)
        {
            pauseToggleOptional.onValueChanged.RemoveAllListeners();
            pauseToggleOptional.onValueChanged.AddListener(v => fleet?.PauseAll(v));
        }
    }

    // ================================================================
    //  PLANNING MODE 事件绑定（保持不变）
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
    //  REPLAY MODE 事件绑定（全新）
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

        // 初始化速度下拉列表
        InitSpeedDropdown();
    }

    // ================================================================
    //  面板切换
    // ================================================================

    private void ShowOnlyPanel(GameObject panel)
    {
        if (defaultPanel)  defaultPanel.SetActive(panel == defaultPanel);
        if (planningPanel) planningPanel.SetActive(panel == planningPanel);
        if (replayPanel)   replayPanel.SetActive(panel == replayPanel);
    }

    public void ResetToDefaultMode()
    {
        // If replaying, show replay panel instead of default
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
        UpdateOutputText("规划模式：请选择起点 / 终点。");
    }

    public void ExitPlanning()
    {
        ShowOnlyPanel(defaultPanel);
        if (planningController) planningController.ExitPlanningMode();
        UpdateOutputText("默认模式：输入 LLM 指令。");
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
        // Stop replay only when user explicitly clicks Exit Replay button
        if (ReplayManager.Instance != null && ReplayManager.Instance.IsReplaying)
            ReplayManager.Instance.StopReplay();

        ShowOnlyPanel(defaultPanel);
        UpdateOutputText("Default Mode: Enter LLM commands.");
    }

    // ================================================================
    //  回放模式：日志文件下拉列表
    // ================================================================

    private void RefreshLogFileDropdown()
    {
        if (!logFileDropdown) return;

        _logFiles = Logger.GetSavedLogFiles();

        logFileDropdown.ClearOptions();

        if (_logFiles.Count == 0)
        {
            logFileDropdown.AddOptions(new List<string> { "(无可用日志)" });
            UpdateOutputText("回放模式：未找到任何日志文件。\\n请先飞行并保存日志。");
            return;
        }

        var options = new List<string>();
        foreach (var (fileName, _) in _logFiles)
        {
            // 显示更友好的名称：FlightLog_20260225_134500.json → 2026-02-25 13:45:00
            string display = FormatLogFileName(fileName);
            options.Add(display);
        }

        logFileDropdown.AddOptions(options);
        logFileDropdown.SetValueWithoutNotify(0);

        // 自动加载第一个文件
        OnLogFileSelected(0);
    }

    private string FormatLogFileName(string fileName)
    {
        // FlightLog_20260225_134500.json → 2026-02-25 13:45:00
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

    private void OnLogFileSelected(int index)
    {
        if (index < 0 || index >= _logFiles.Count) return;

        string fullPath = _logFiles[index].fullPath;
        string fileName = _logFiles[index].fileName;

        if (ReplayManager.Instance == null)
        {
            UpdateOutputText(" ReplayManager 未找到！");
            return;
        }

        UpdateOutputText($"正在加载: {FormatLogFileName(fileName)}...");

        // 加载完成后会通过 OnStatusChanged 回调更新 UI
        ReplayManager.Instance.LoadReplayFile(fullPath);

        // 延迟刷新无人机下拉列表（等加载完成）
        StartCoroutine(RefreshReplayDroneDropdownDelayed());
    }

    private System.Collections.IEnumerator RefreshReplayDroneDropdownDelayed()
    {
        // 等待加载完成（最多等3秒）
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
    //  回放模式：无人机选择下拉列表
    // ================================================================

    private void RefreshReplayDroneDropdown()
    {
        if (!replayDroneDropdown) return;

        replayDroneDropdown.ClearOptions();

        var replay = ReplayManager.Instance;
        if (replay == null || !replay.IsLoaded || replay.DroneNamesInLog.Count == 0)
        {
            replayDroneDropdown.AddOptions(new List<string> { "(无数据)" });
            return;
        }

        var options = new List<string>();
        options.Add("ALL (全部无人机)");
        foreach (var name in replay.DroneNamesInLog)
        {
            options.Add(name);
        }

        replayDroneDropdown.AddOptions(options);
        replayDroneDropdown.SetValueWithoutNotify(0); // 默认选 ALL
    }

    private void OnReplayDroneSelected(int index)
    {
        var replay = ReplayManager.Instance;
        if (replay == null || !replay.IsLoaded) return;

        if (index == 0)
        {
            // ALL
            replay.SelectAllDrones();
            UpdateOutputText($"已选择: 全部无人机 ({replay.DroneNamesInLog.Count} 架)");
        }
        else
        {
            int droneIndex = index - 1;
            if (droneIndex < replay.DroneNamesInLog.Count)
            {
                string droneName = replay.DroneNamesInLog[droneIndex];
                replay.SelectSingleDrone(droneName);
                UpdateOutputText($"已选择: {droneName}");
            }
        }
    }

    // ================================================================
    //  回放模式：速度控制
    // ================================================================

    private void InitSpeedDropdown()
    {
        if (!speedDropdown) return;

        speedDropdown.ClearOptions();
        var options = new List<string>();
        for (int i = 0; i < _speedOptions.Length; i++)
        {
            options.Add($"{_speedOptions[i]}x");
        }
        speedDropdown.AddOptions(options);

        // 默认选 1x（索引 2）
        _selectedSpeedIndex = 2;
        speedDropdown.SetValueWithoutNotify(_selectedSpeedIndex);
    }

    private void OnSpeedChanged(int index)
    {
        if (index < 0 || index >= _speedOptions.Length) return;

        _selectedSpeedIndex = index;
        float speed = _speedOptions[index];

        var replay = ReplayManager.Instance;
        if (replay != null)
        {
            replay.SetReplaySpeed(speed);
        }
    }

    // ================================================================
    //  回放模式：控制按钮
    // ================================================================

    private void OnStartReplay()
    {
        var replay = ReplayManager.Instance;
        if (replay == null)
        {
            UpdateOutputText(" ReplayManager 未找到！");
            return;
        }

        if (!replay.IsLoaded)
        {
            UpdateOutputText(" 请先选择一个日志文件");
            return;
        }

        if (replay.IsReplaying)
        {
            // 如果已经在回放，先停止再重新开始
            replay.StopReplay();
        }

        // 应用当前选择的速度
        float speed = _speedOptions[_selectedSpeedIndex];
        replay.SetReplaySpeed(speed);

        // 应用当前选择的无人机
        if (replayDroneDropdown)
            OnReplayDroneSelected(replayDroneDropdown.value);

        // 开始回放
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

    private void OnStopReplay()
    {
        var replay = ReplayManager.Instance;
        if (replay == null) return;

        replay.StopReplay();
        UpdateReplayButtonStates();
    }

    // ================================================================
    //  回放模式：UI 状态更新
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

    /// <summary>
    /// ReplayManager 的状态变化回调
    /// </summary>
    private void OnReplayStatusChanged(string msg)
    {
        UpdateOutputText(msg);
        UpdateReplayButtonStates();
    }

    /// <summary>
    /// 回放结束回调
    /// </summary>
    private void OnReplayFinished()
    {
        UpdateReplayButtonStates();
        UpdateOutputText("Replay complete! Drone states restored.\\nSelect another file or exit replay mode.");
    }
    // ================================================================
    //  DEFAULT MODE 处理函数
    // ================================================================

    private void OnSend()
    {
        if (!llm || !inputField) return;
        string text = inputField.text;
        if (string.IsNullOrWhiteSpace(text)) return;
        llm.SendUserText(text);
        Debug.Log($"[UI] Input: {text}");
    }

    private void OnSaveLog()
    {
        if (Logger.Instance == null)
        {
            UpdateOutputText("Error: Logger not found");
            return;
        }

        string path = Logger.Instance.SaveLog();
        if (!string.IsNullOrEmpty(path))
        {
            UpdateOutputText("Log saved: " + System.IO.Path.GetFileName(path));

            if (replayPanel && replayPanel.activeSelf)
                RefreshLogFileDropdown();
        }
        else
        {
            UpdateOutputText("No data to save (may already be saved)");
        }
    }

    // ================================================================
    //  PLANNING MODE 处理函数（保持不变）
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
        string se = picker ? $"Start={(picker.HasStart ? "✔" : "—")}  End={(picker.HasEnd ? "✔" : "—")}" : "";

        string hint = "";
        if (picker && !picker.HasEnd) hint = "\\nHint: 需要先选 End";

        statusText.text = $"Current: {curName}\\nPickMode: {pickMode}\\n{se}{hint}";
    }

    // ================================================================
    //  状态更新（同时给 Planning 的 UpdateStatus 用）
    // ================================================================

    public void UpdateStatus()
    {
        if (!outputText || !picker || !switchView) return;

        Transform currentTarget = switchView.CurrentDroneTarget;
        string droneName = currentTarget
            ? currentTarget.GetComponentInParent<DroneInfo>()?.GetName() ?? "未知"
            : "无";

        string modeStr = picker.mode.ToString();
        Unity.Mathematics.double3 llh =
            (picker.mode == MapPickController.PickMode.PickStart && picker.HasStart) ? picker.StartLLH :
            (picker.mode == MapPickController.PickMode.PickEnd && picker.HasEnd) ? picker.EndLLH :
            new Unity.Mathematics.double3(0, 0, 0);

        UpdateOutputText($"当前无人机：{droneName}\\n拾取模式: {modeStr}\\n位置: {llh.x:F4}, {llh.y:F4}, {llh.z:F1}m");
    }

    public void ClosePanel()
    {
        // DO NOT stop replay when closing UI panel
        // User can close UI with Tab/X and replay continues in background
        gameObject.SetActive(false);
    }

    private void UpdateOutputText(string msg)
    {
        if (outputText) outputText.text = msg;
    }

    // ================================================================
    //  工具方法
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