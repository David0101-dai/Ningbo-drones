using UnityEngine;

public class PlanningModeController : MonoBehaviour
{
    public DroneFleetController fleet;
    public SwitchView switchView;
    public MapPickController picker;

    [Header("Exit behavior")]
    public bool resumeAllOnExit = false;

    void Awake()
    {
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!picker) picker = FindObjectOfType<MapPickController>();
    }

    public void EnterPlanningMode()
    {
        fleet?.PauseAll(true);
        switchView?.SetTopDown();     // 需要你在 SwitchView 加 public wrapper
        if (picker) picker.EnablePicking(true);

        Debug.Log("[PlanningMode] Enter");
    }

    public void ExitPlanningMode()
    {
        if (picker) picker.EnablePicking(false);

        // 退出时回到侧视/追踪（你也可以换 Rear）
        switchView?.SetSide();

        if (resumeAllOnExit)
            fleet?.PauseAll(false);

        Debug.Log("[PlanningMode] Exit");
    }
}
