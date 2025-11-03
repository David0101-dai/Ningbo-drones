using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCameraControl : MonoBehaviour
{
    public Transform drone; // 拖拽无人机
    public float distance = 5f; // 距离
    public float height = 2f; // 高度
    public float rotationSpeed = 100f; // 旋转速度

    void LateUpdate()
    {
        if (!enabled) return; // 只在启用时运行（由CameraSwitcher控制）

        // 跟随无人机（用世界坐标）
        Vector3 targetPos = drone.position - drone.forward * distance + Vector3.up * height;
        transform.position = targetPos;
        transform.LookAt(drone.position + Vector3.up * height);

        // // A/D围绕旋转（新Input System）
        // float horizontal = 0f;
        // if (Keyboard.current.aKey.isPressed) horizontal -= 1f;
        // if (Keyboard.current.dKey.isPressed) horizontal += 1f;
        // transform.RotateAround(drone.position, Vector3.up, horizontal * rotationSpeed * Time.deltaTime);
    }
}