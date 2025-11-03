using UnityEngine;
using UnityEngine.InputSystem;

public class CameraSwitcher : MonoBehaviour
{
    public Camera firstPersonCam;
    public Camera thirdPersonCam;
    public ThirdPersonCameraControl thirdPersonControl; // Inspector拖拽第三视角的ThirdPersonCameraControl组件
    private bool isFirstPerson = true;

    void Start()
    {
        thirdPersonCam.enabled = false;
        if (thirdPersonControl != null) thirdPersonControl.enabled = false; // 默认禁用第三视角控制
    }

    void Update()
    {
        if (Keyboard.current.rKey.wasPressedThisFrame) // 按R切换（新Input System）
        {
            isFirstPerson = !isFirstPerson;
            firstPersonCam.enabled = isFirstPerson;
            thirdPersonCam.enabled = !isFirstPerson;
            if (thirdPersonControl != null) thirdPersonControl.enabled = !isFirstPerson; // 同步启用/禁用控制
        }
    }
}