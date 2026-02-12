using UnityEngine;
using TMPro;

public class DroneInfoPanel : MonoBehaviour
{
    [Header("UI 组件")]
    public TMP_Text nameText;
    public TMP_Text speedText;

    [Header("跟随设置")]
    public Transform targetDrone;
    public Vector3 offset = new Vector3(1.5f, 2.8f, 0.5f);  // X 向右偏移，避免遮挡；Z 轻微前移

    [Header("防抖动参数")]
    public float smoothSpeed = 8f;          // 位置平滑速度
    public float rotationSmooth = 10f;      // 旋转平滑速度

    private Canvas canvas;
    public bool isVisible = false;

    void Awake()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("必须挂在 Canvas 上！");
            return;
        }
        canvas.enabled = false;
    }

    void LateUpdate()
    {
        if (targetDrone == null || !isVisible) return;

        // 平滑跟随位置
        Vector3 targetPos = targetDrone.position + offset;
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothSpeed);
        // 距离自适应缩放（近大远小）
        float distance = Vector3.Distance(transform.position, Camera.main.transform.position);
        float targetScale = Mathf.Lerp(0.008f, 0.55f, Mathf.InverseLerp(5f, 50f, distance)); // 近5米时0.025，远50米时0.008
        transform.localScale = Vector3.Lerp(transform.localScale, new Vector3(targetScale, targetScale, targetScale), Time.deltaTime * 5f);

        // 判断当前相机是否为 TopDown（通过角度或标签判断）
        bool isTopView = Vector3.Dot(Camera.main.transform.forward, Vector3.down) > 0.9f;  // 向下看 > 0.9 认为俯视

        if (isTopView)
        {
            // 俯视时强制水平（X-Z 平面）
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);  // 让面板平躺朝上
        }
        else
        {
            // 正常视角：面向相机（水平旋转）
            Vector3 direction = transform.position - Camera.main.transform.position;
            direction.y = 0;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmooth);
        }
    }

    public void TogglePanel()
    {
        isVisible = !isVisible;
        canvas.enabled = isVisible;
    }

    public void UpdateSpeed(float speedMps)
    {
        if (speedText)
        {
            float kmh = speedMps * 3.6f;
            speedText.text = $"{kmh:F1} km/h";
        }
    }

    public void SetName(string droneName)
    {
        if (nameText) nameText.text = droneName;
    }
}