// Assets/Scripts/SwitchView.cs
using UnityEngine;
using Cinemachine;

public class SwitchView : MonoBehaviour
{
    [Header("Drag your two vcams here")]
    public CinemachineVirtualCamera sideView;   // 侧视 vcam
    public CinemachineVirtualCamera rearChase;  // 后追 vcam

    [Header("Hotkeys")]
    public KeyCode sideKey = KeyCode.Alpha1;
    public KeyCode rearKey = KeyCode.Alpha2;

    [Header("Priorities")]
    public int activePriority = 20;
    public int inactivePriority = 10;

    void OnEnable()
    {
        // 默认启用后追
        Apply(rearActive: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(sideKey)) Apply(rearActive: false);
        if (Input.GetKeyDown(rearKey)) Apply(rearActive: true);
    }

    void Apply(bool rearActive)
    {
        if (sideView)  sideView.Priority  = rearActive ? inactivePriority : activePriority;
        if (rearChase) rearChase.Priority = rearActive ? activePriority : inactivePriority;
    }
}