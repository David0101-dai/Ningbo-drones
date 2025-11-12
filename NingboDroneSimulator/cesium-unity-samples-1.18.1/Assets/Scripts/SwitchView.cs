// Assets/Scripts/SwitchView.cs
using UnityEngine;
using Cinemachine;

public class SwitchView : MonoBehaviour
{
    [Header("Drag your vcams here")]
    public CinemachineVirtualCamera sideView;      // 数字 1
    public CinemachineVirtualCamera rearChase;     // 数字 2
    public CinemachineVirtualCamera topDown;       // 数字 3（新增）

    [Header("Hotkeys")]
    public KeyCode sideKey = KeyCode.Alpha1;
    public KeyCode rearKey = KeyCode.Alpha2;
    public KeyCode topKey  = KeyCode.Alpha3;

    [Header("Priorities")]
    public int activePriority = 20;
    public int inactivePriority = 10;

    void OnEnable()
    {
        // 默认仍启用侧视
        Apply(View.Side);
    }

    void Update()
    {
        if (Input.GetKeyDown(sideKey)) Apply(View.Side);
        if (Input.GetKeyDown(rearKey)) Apply(View.Rear);
        if (Input.GetKeyDown(topKey))  Apply(View.TopDown);
    }

    enum View { Side, Rear, TopDown }

    void Apply(View v)
    {
        SetPrio(sideView,  v == View.Side);
        SetPrio(rearChase, v == View.Rear);
        SetPrio(topDown,   v == View.TopDown);
    }

    void SetPrio(CinemachineVirtualCamera vcam, bool active)
    {
        if (!vcam) return;
        vcam.Priority = active ? activePriority : inactivePriority;
    }
}