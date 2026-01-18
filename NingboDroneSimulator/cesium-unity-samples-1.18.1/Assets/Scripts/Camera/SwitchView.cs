// Assets/Scripts/SwitchView.cs
using UnityEngine;
using Cinemachine;

public class SwitchView : MonoBehaviour
{
    [Header("Drag your vcams here")]
    public CinemachineVirtualCamera sideView;      // 数字 1
    public CinemachineVirtualCamera rearChase;     // 数字 2
    public CinemachineVirtualCamera topDown;       // 数字 3（俯瞰城市）

    [Header("Hotkeys - 视角模式切换")]
    public KeyCode sideKey = KeyCode.Alpha1;
    public KeyCode rearKey = KeyCode.Alpha2;
    public KeyCode topKey  = KeyCode.Alpha3;

    [Header("Hotkeys - 无人机目标切换")]
    public KeyCode nextDroneKey = KeyCode.D;   // 下一个无人机
    public KeyCode prevDroneKey = KeyCode.A;   // 上一个无人机

    [Header("Priorities")]
    public int activePriority = 20;
    public int inactivePriority = 10;

    [Header("Drone Targets (按顺序拖每架无人机的 CamTarget)")]
    public Transform[] droneTargets;           // UAV_A 的 CamTarget, UAV_B 的 CamTarget, ...

    // 当前选中的无人机索引（0 = 数组里的第一个）
    [SerializeField] private int _currentDroneIndex = 0;

    // 当前视角模式
    private enum View { Side, Rear, TopDown }
    private View _currentView = View.Side;

    // ======== Public APIs (for UI/Other Scripts) ========

    public int CurrentDroneIndex => _currentDroneIndex;

    public int DroneCount => (droneTargets != null) ? droneTargets.Length : 0;

    public Transform CurrentDroneTarget
    {
        get
        {
            if (droneTargets == null || droneTargets.Length == 0) return null;
            _currentDroneIndex = Mathf.Clamp(_currentDroneIndex, 0, droneTargets.Length - 1);
            return droneTargets[_currentDroneIndex];
        }
    }

    // 给 PlanningModeController / UI 用
    public void SetTopDown() { ApplyView(View.TopDown); }
    public void SetSide()    { ApplyView(View.Side); }
    public void SetRear()    { ApplyView(View.Rear); }

    // 给 UI Dropdown 用：直接切到指定 index 的无人机，并让 Side/Rear 跟随
    public void SelectDroneByIndex(int index)
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex = Mathf.Clamp(index, 0, droneTargets.Length - 1);

        // 保持当前视角模式不变，但 Side/Rear 必须跟随新目标
        if (_currentView == View.Side || _currentView == View.Rear)
        {
            ApplyDroneTarget(_currentDroneIndex);
        }
    }

    // ======== Unity lifecycle ========

    void OnEnable()
    {
        // 默认启用侧视
        ApplyView(View.Side);
        ApplyDroneTarget(_currentDroneIndex);
    }

    void Update()
    {
        // --- 数字键：切换视角模式 ---
        if (Input.GetKeyDown(sideKey)) ApplyView(View.Side);
        if (Input.GetKeyDown(rearKey)) ApplyView(View.Rear);
        if (Input.GetKeyDown(topKey))  ApplyView(View.TopDown);

        // --- A / D：切换当前被观察的无人机 ---
        // 只在非 TopDown 模式下生效（Top 是纯俯视，不去改它）
        if (_currentView != View.TopDown && droneTargets != null && droneTargets.Length > 0)
        {
            if (Input.GetKeyDown(nextDroneKey))
                SelectNextDrone();
            else if (Input.GetKeyDown(prevDroneKey))
                SelectPrevDrone();
        }
    }

    // ======== 内部实现 ========

    void ApplyView(View v)
    {
        _currentView = v;

        SetPrio(sideView,  v == View.Side);
        SetPrio(rearChase, v == View.Rear);
        SetPrio(topDown,   v == View.TopDown);

        // 切换到 Side / Rear 时，保证跟随当前选中的无人机
        if (v == View.Side || v == View.Rear)
        {
            ApplyDroneTarget(_currentDroneIndex);
        }
    }

    void SetPrio(CinemachineVirtualCamera vcam, bool active)
    {
        if (!vcam) return;
        vcam.Priority = active ? activePriority : inactivePriority;
    }

    void SelectNextDrone()
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex++;
        if (_currentDroneIndex >= droneTargets.Length)
            _currentDroneIndex = 0;

        ApplyDroneTarget(_currentDroneIndex);
    }

    void SelectPrevDrone()
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex--;
        if (_currentDroneIndex < 0)
            _currentDroneIndex = droneTargets.Length - 1;

        ApplyDroneTarget(_currentDroneIndex);
    }

    void ApplyDroneTarget(int index)
    {
        if (droneTargets == null || droneTargets.Length == 0) return;
        if (index < 0 || index >= droneTargets.Length) return;

        Transform target = droneTargets[index];
        if (!target) return;

        // Side / Rear 这两台 vcam 的 Follow / LookAt 都改成当前无人机的 CamTarget
        if (sideView)
        {
            sideView.Follow = target;
            sideView.LookAt = target;
        }

        if (rearChase)
        {
            rearChase.Follow = target;
            rearChase.LookAt = target;
        }

        // TopDown 不动，让它保持原来的俯视逻辑
    }
}
