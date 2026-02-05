using UnityEngine;
using TMPro; // 如果用 TMP_Text

public class UIPanelManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject defaultPanel;
    [SerializeField] private GameObject planningPanel;
    [SerializeField] private TMP_Text outputText;

    [Header("Controllers")]
    [SerializeField] private MapPickController picker;
    [SerializeField] private SwitchView switchView;
    [SerializeField] private PlanningModeController planningController; // 用于 Enter/ExitPlanning

    void Awake()
    {
        // 自动查找缺失引用（鲁棒性提升）
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!planningController) planningController = FindObjectOfType<PlanningModeController>();

        // 默认模式
        if (defaultPanel) defaultPanel.SetActive(true);
        if (planningPanel) planningPanel.SetActive(false);
    }

    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    public void ResetToDefaultMode()
    {
        defaultPanel.SetActive(true);
        planningPanel.SetActive(false);
        UpdateOutputText("默认模式：输入 LLM 指令。");
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