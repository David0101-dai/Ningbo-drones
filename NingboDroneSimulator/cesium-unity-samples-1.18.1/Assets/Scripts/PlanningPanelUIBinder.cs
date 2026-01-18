using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlanningPanelUIBinder : MonoBehaviour
{
    [Header("Controllers (can be auto-found)")]
    public PlanningModeController planning;
    public MapPickController picker;
    public DroneFleetController fleet;
    public SwitchView switchView;

    [Header("Route Apply Controller")]
    public ApplyRuntimeRouteController applyRouteController;

    [Header("UI (auto-find by name if left empty)")]
    public Button enterPlanningButton;
    public Button exitPlanningButton;
    public Button pickStartButton;
    public Button pickEndButton;
    public Button clearPickButton;

    public Button pauseAllButton;   // optional
    public Button resumeAllButton;  // optional

    public Button applyRouteButton; // ✅ 新增：生成 Waypoints_Runtime 并应用到当前无人机

    public TMP_Dropdown droneDropdown; // recommended TMP dropdown
    public TMP_Text statusText;        // optional

    [Header("Auto-find child names (rename your UI to match)")]
    public string enterPlanningName = "EnterPlanningButton";
    public string exitPlanningName  = "ExitPlanningButton";
    public string pickStartName     = "PickStartButton";
    public string pickEndName       = "PickEndButton";
    public string clearPickName     = "ClearPickButton";
    public string pauseAllName      = "PauseAllButton";
    public string resumeAllName     = "ResumeAllButton";
    public string applyRouteName    = "ApplyRouteButton";   // ✅ 新增
    public string droneDropdownName = "DroneDropdown";
    public string statusTextName    = "StatusText";

    void Awake()
    {
        // ---- Auto find controllers ----
        if (!planning) planning = FindObjectOfType<PlanningModeController>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!applyRouteController) applyRouteController = FindObjectOfType<ApplyRuntimeRouteController>();

        // ---- Auto find UI ----
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

        BindButtons();
        BindDropdown();
        RefreshDropdownOptions();
        RefreshStatus();
    }

    void OnEnable()
    {
        RefreshDropdownOptions();
        RefreshStatus();
    }

    void Update()
    {
        if (Time.frameCount % 15 == 0)
            RefreshStatus();
    }

    // ---------------- Binding ----------------

    void BindButtons()
    {
        // ✅ 防重复：先清掉旧监听
        if (enterPlanningButton)
        {
            enterPlanningButton.onClick.RemoveAllListeners();
            enterPlanningButton.onClick.AddListener(() => planning?.EnterPlanningMode());
        }

        if (exitPlanningButton)
        {
            exitPlanningButton.onClick.RemoveAllListeners();
            exitPlanningButton.onClick.AddListener(() => planning?.ExitPlanningMode());
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
            clearPickButton.onClick.AddListener(() => picker?.Clear());
        }

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

        // ✅ 新增：ApplyRoute
        if (applyRouteButton)
        {
            applyRouteButton.onClick.RemoveAllListeners();
            applyRouteButton.onClick.AddListener(() =>
            {
                if (!applyRouteController)
                {
                    Debug.LogWarning("[UIBinder] ApplyRuntimeRouteController not found.");
                    return;
                }
                applyRouteController.ApplyToCurrentDrone();
                RefreshStatus();
            });
        }
    }

    void BindDropdown()
    {
        if (!droneDropdown) return;

        droneDropdown.onValueChanged.RemoveAllListeners();
        droneDropdown.onValueChanged.AddListener(OnDroneDropdownChanged);
    }

    void OnDroneDropdownChanged(int index)
    {
        if (!switchView) return;
        switchView.SelectDroneByIndex(index);
        RefreshStatus();
    }

    // ---------------- UI Helpers ----------------

    void RefreshDropdownOptions()
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

        // dropdown 设为当前目标
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

    void RefreshStatus()
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

        // ✅ 增加一点提示：End 没选就提示
        string hint = "";
        if (picker && !picker.HasEnd) hint = "\nHint: 需要先选 End";

        statusText.text = $"Current: {curName}\nPickMode: {pickMode}\n{se}{hint}";
    }

    // ---------------- Find children by name ----------------

    Button FindButton(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<Button>(true);
        foreach (var b in all)
            if (b && b.name == childName) return b;
        return null;
    }

    TMP_Dropdown FindDropdown(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (var d in all)
            if (d && d.name == childName) return d;
        return null;
    }

    TMP_Text FindTMPText(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;
        var all = GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in all)
            if (t && t.name == childName) return t;
        return null;
    }
}
