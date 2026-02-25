using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UIPanelManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject defaultPanel;
    [SerializeField] private GameObject planningPanel;
    [SerializeField] private TMP_Text outputText;

    [Header("Default Mode UI")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button pauseAllButton;
    [SerializeField] private Button resumeAllButton;
    [SerializeField] private Button enterPlanningButton;
    [SerializeField] private Button saveLogButton;
    [SerializeField] private Button loadReplayButton;
    [SerializeField] private Button startReplayButton;   

    [Header("Planning Mode UI")]
    [SerializeField] private Button exitPlanningButton;
    [SerializeField] private Button pickStartButton;
    [SerializeField] private Button pickEndButton;
    [SerializeField] private Button clearPickButton; // 如果有
    [SerializeField] private Button applyRouteButton;
    [SerializeField] private TMP_Dropdown droneDropdown;
    [SerializeField] private TMP_Text statusText; // optional, 用于 RefreshStatus

    [Header("Optional Toggle (from Stage2PauseUI)")]
    [SerializeField] private Toggle pauseToggleOptional; // 可选，如果有 Toggle

    [Header("Controllers")]
    [SerializeField] private MapPickController picker;
    [SerializeField] private SwitchView switchView;
    [SerializeField] private PlanningModeController planningController;
    [SerializeField] private DroneFleetController fleet;
    [SerializeField] private ApplyRuntimeRouteController applyRouteController;
    [SerializeField] private LLMManagerHttp llm;

    [Header("Auto-find child names (from PlanningPanelUIBinder)")]
    public string enterPlanningName = "EnterPlanningButton";
    public string exitPlanningName  = "ExitPlanningButton";
    public string pickStartName     = "PickStartButton";
    public string pickEndName       = "PickEndButton";
    public string clearPickName     = "ClearPickButton";
    public string pauseAllName      = "PauseAllButton";
    public string resumeAllName     = "ResumeAllButton";
    public string applyRouteName    = "ApplyRouteButton";
    public string droneDropdownName = "DroneDropdown";
    public string statusTextName    = "StatusText";
    public string saveLogName       = "SaveLogButton";
    public string loadReplayName    = "LoadReplayButton";
    public string startReplayName     = "StartReplayButton";
    


    void Awake()
    {
        // 自动查找控制器（合并所有脚本的查找逻辑）
        if (!planningController) planningController = FindObjectOfType<PlanningModeController>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!applyRouteController) applyRouteController = FindObjectOfType<ApplyRuntimeRouteController>();
        if (!llm) llm = FindObjectOfType<LLMManagerHttp>();

        // 自动查找 UI 元素（从 PlanningPanelUIBinder 转移）
        if (!enterPlanningButton) enterPlanningButton = FindButton(enterPlanningName);
        if (!exitPlanningButton)  exitPlanningButton  = FindButton(exitPlanningName);
        if (!pickStartButton)     pickStartButton     = FindButton(pickStartName);
        if (!pickEndButton)       pickEndButton       = FindButton(pickEndName);
        if (!clearPickButton)     clearPickButton     = FindButton(clearPickName);
        if (!pauseAllButton)      pauseAllButton      = FindButton(pauseAllName);
        if (!resumeAllButton)     resumeAllButton     = FindButton(resumeAllName);
        if (!applyRouteButton)    applyRouteButton    = FindButton(applyRouteName);
        if (!droneDropdown)       droneDropdown       = FindDropdown(droneDropdownName);
        if (!statusText)          statusText          = FindTMPText(statusTextName);
        if (!saveLogButton)       saveLogButton       = FindButton(saveLogName);
        if (!loadReplayButton)    loadReplayButton    = FindButton(loadReplayName);
        if (!startReplayButton)   startReplayButton   = FindButton(startReplayName);

        // 默认模式设置
        if (defaultPanel) defaultPanel.SetActive(true);
        if (planningPanel) planningPanel.SetActive(false);

        // 绑定所有事件（合并所有脚本的绑定逻辑）
        BindEvents();

        // 设置 LLM 输出（从 LLMPanelUI 转移）
        if (llm && outputText) llm.outputText = outputText;

        // 刷新 Dropdown 和 Status（从 PlanningPanelUIBinder 转移）
        RefreshDropdownOptions();
        RefreshStatus();
    }

    void OnEnable()
    {
        // 从 PlanningPanelUIBinder 转移
        RefreshDropdownOptions();
        RefreshStatus();
    }

    void Update()
    {
        // 从 PlanningPanelUIBinder 转移：每 15 帧刷新 Status
        if (Time.frameCount % 15 == 0)
            RefreshStatus();
    }

    // ---------------- 合并的事件绑定函数 ----------------
    private void BindEvents()
    {
        // 从 LLMPanelUI 转移：SendButton
        if (sendButton)
        {
            sendButton.onClick.RemoveAllListeners();
            sendButton.onClick.AddListener(OnSend);
        }

        // 从 Stage2PauseUI 转移：Pause/Resume 和 Toggle
        if (pauseAllButton)
        {
            pauseAllButton.onClick.RemoveAllListeners();
            pauseAllButton.onClick.AddListener(() => fleet?.PauseAll(true));
        }
        if (resumeAllButton)
        {
            resumeAllButton.onClick.RemoveAllListeners();
            resumeAllButton.onClick.AddListener(() => fleet?.PauseAll(false));
        }
        if (pauseToggleOptional)
        {
            pauseToggleOptional.onValueChanged.RemoveAllListeners();
            pauseToggleOptional.onValueChanged.AddListener(v => fleet?.PauseAll(v));
        }

        // 从 PlanningPanelUIBinder 转移：所有按钮和 Dropdown
        if (enterPlanningButton)
        {
            enterPlanningButton.onClick.RemoveAllListeners();
            enterPlanningButton.onClick.AddListener(EnterPlanning);
        }
        if (exitPlanningButton)
        {
            exitPlanningButton.onClick.RemoveAllListeners();
            exitPlanningButton.onClick.AddListener(ExitPlanning);
        }
        if (pickStartButton)
        {
            pickStartButton.onClick.RemoveAllListeners();
            pickStartButton.onClick.AddListener(() => picker?.SetPickStart());
        }
        if (pickEndButton)
        {
            pickEndButton.onClick.RemoveAllListeners();
            pickEndButton.onClick.AddListener(() => picker?.SetPickEnd());
        }
        if (clearPickButton)
        {
            clearPickButton.onClick.RemoveAllListeners();
            clearPickButton.onClick.AddListener(() => picker?.Clear()); // 假设 picker 有 Clear 方法
        }
        if (applyRouteButton)
        {
            applyRouteButton.onClick.RemoveAllListeners();
            applyRouteButton.onClick.AddListener(() => applyRouteController?.ApplyToCurrentDrone());
        }
        if (droneDropdown)
        {
            droneDropdown.onValueChanged.RemoveAllListeners();
            droneDropdown.onValueChanged.AddListener(OnDroneDropdownChanged);
        }
        if (saveLogButton)  
        {
            saveLogButton.onClick.RemoveAllListeners();
            saveLogButton.onClick.AddListener(() => Logger.Instance.SaveLog());
        }
        if (loadReplayButton)   
        {
            loadReplayButton.onClick.RemoveAllListeners();
            loadReplayButton.onClick.AddListener(OnLoadReplay);
        }
        if (startReplayButton)
        {
            startReplayButton.onClick.RemoveAllListeners();
            startReplayButton.onClick.AddListener(() => ReplayManager.Instance.StartReplay());
        }
    }

    private void OnLoadReplay()
    {
        // 使用异步加载，避免卡顿
        string path = Application.persistentDataPath + "/FlightLog_20260215_204055.json"; // 请改成你实际的文件名
        
        if (ReplayManager.Instance != null)
        {
            ReplayManager.Instance.LoadReplayAsync(path);   // 注意改成 LoadReplayAsync
            Debug.Log("[UI] 开始异步加载回放文件...");
        }
        else
        {
            Debug.LogError("[UI] ReplayManager 未找到！");
        }
    }

    // 从 LLMPanelUI 转移的 OnSend
    private void OnSend()
    {
        if (!llm || !inputField) return;
        llm.SendUserText(inputField.text);
        Debug.Log($"Input text: {inputField.text}");
    }

    // 从 PlanningPanelUIBinder 转移的 OnDroneDropdownChanged
    private void OnDroneDropdownChanged(int index)
    {
        if (!switchView) return;
        switchView.SelectDroneByIndex(index);
        RefreshStatus();
    }

    // 从 PlanningPanelUIBinder 转移的 RefreshDropdownOptions
    private void RefreshDropdownOptions()
    {
        if (!droneDropdown || !switchView) return;

        int n = switchView.DroneCount;
        if (n <= 0) return;

        var options = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            Transform t = switchView.droneTargets[i];
            if (!t)
            {
                options.Add($"Drone_{i}");
                continue;
            }

            var info = t.GetComponentInParent<DroneInfo>();
            options.Add(info ? info.gameObject.name : t.name);
        }

        droneDropdown.ClearOptions();
        droneDropdown.AddOptions(options);

        // 设置为当前目标
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

    // 从 PlanningPanelUIBinder 转移的 RefreshStatus
    private void RefreshStatus()
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

        // 增加提示：End 没选就提示
        string hint = "";
        if (picker && !picker.HasEnd) hint = "\nHint: 需要先选 End";

        statusText.text = $"Current: {curName}\nPickMode: {pickMode}\n{se}{hint}";
    }

    // 从 PlanningPanelUIBinder 转移的 Find 辅助方法
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

    // 原有方法保持不变：ClosePanel, ResetToDefaultMode, EnterPlanning, ExitPlanning, UpdateStatus 等
    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    public void ResetToDefaultMode()
    {
        defaultPanel.SetActive(true);
        planningPanel.SetActive(false);
        UpdateOutputText("默认模式：输入 LLM 指令。");
        if (inputField) {
            inputField.ActivateInputField();
            inputField.Select();
        }
    }

    public void EnterPlanning()
    {
        defaultPanel.SetActive(false);
        planningPanel.SetActive(true);
        if (planningController) planningController.EnterPlanningMode();
        UpdateOutputText("规划模式：请选择起点 / 终点。");
    }

    public void ExitPlanning()
    {
        planningPanel.SetActive(false);
        defaultPanel.SetActive(true);
        if (planningController) planningController.ExitPlanningMode();
        UpdateOutputText("默认模式：输入 LLM 指令。");
    }

    // 在 PickStart/End OnClick 或 DroneDropdown OnValueChanged 中调用
    public void UpdateStatus()
    {
        if (!outputText || !picker || !switchView) return;

        Transform currentTarget = switchView.CurrentDroneTarget;
        string droneName = currentTarget ? currentTarget.GetComponentInParent<DroneInfo>()?.GetName() ?? "未知" : "无";

        string modeStr = picker.mode.ToString();
        Unity.Mathematics.double3 llh = (picker.mode == MapPickController.PickMode.PickStart && picker.HasStart) ? picker.StartLLH :
                                        (picker.mode == MapPickController.PickMode.PickEnd && picker.HasEnd) ? picker.EndLLH : new Unity.Mathematics.double3(0,0,0);

        UpdateOutputText($"当前无人机：{droneName}\n拾取模式：{modeStr}\n位置：{llh.x:F4}, {llh.y:F4}, {llh.z:F1}m");
    }

    private void UpdateOutputText(string msg)
    {
        if (outputText) outputText.text = msg;
    }

    
}