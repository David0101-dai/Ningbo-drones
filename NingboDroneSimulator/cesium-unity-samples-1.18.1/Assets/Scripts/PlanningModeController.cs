using UnityEngine;

public class PlanningModeController : MonoBehaviour
{
    public DroneCommandCenter commandCenter;
    public SwitchView switchView;
    public MapPickController picker;

    [Header("Exit behavior")]
    public bool resumeAllOnExit = false;

    public bool IsInPlanningMode { get; private set; }

    void Awake()
    {
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
    }

    public void EnterPlanningMode()
    {
        if (IsInPlanningMode) return;
        IsInPlanningMode = true;

        commandCenter?.PauseAll(true);
        switchView?.SetTopDown();
        if (picker) picker.EnablePicking(true);

        Debug.Log("[PlanningMode] Enter");
    }

    public void ExitPlanningMode()
    {
        if (!IsInPlanningMode) return;
        IsInPlanningMode = false;

        if (picker) picker.EnablePicking(false);
        switchView?.SetSide();

        if (resumeAllOnExit)
            commandCenter?.PauseAll(false);

        Debug.Log("[PlanningMode] Exit");
    }
}