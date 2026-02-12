using UnityEngine;
using Cinemachine;

public class TopDownCameraController : MonoBehaviour
{
    [Header("移动设置")]
    public float moveSpeed = -10f;         // 正常移动速度
    public float fastMultiplier = 2f;     // Shift 加速倍数

    [Header("缩放设置")]
    public float zoomSpeed = 20f;         // 鼠标轮缩放速度
    public float minFOV = 20f;            // 最小 FOV
    public float maxFOV = 90f;            // 最大 FOV

    private CinemachineVirtualCamera vcam;
    private CinemachineOrbitalTransposer orbital;  // 如果是 Orbital 类型
    private float currentFOV;

    void Awake()
    {
        vcam = GetComponent<CinemachineVirtualCamera>();
        orbital = vcam.GetCinemachineComponent<CinemachineOrbitalTransposer>();  // 如果你的 Top vcam 是 Orbital
        currentFOV = vcam.m_Lens.FieldOfView;
    }

    void Update()
    {
        // 只在 Top 视角激活时工作（通过 SwitchView 的优先级判断）
        if (vcam.Priority <= 10) return;  // inactivePriority = 10，假设

        // WASD 移动
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);
        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
        transform.position += move.normalized * speed * Time.deltaTime;

        // 鼠标轮缩放 FOV
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0)
        {
            currentFOV -= scroll * zoomSpeed;
            currentFOV = Mathf.Clamp(currentFOV, minFOV, maxFOV);
            vcam.m_Lens.FieldOfView = currentFOV;
        }
    }
}