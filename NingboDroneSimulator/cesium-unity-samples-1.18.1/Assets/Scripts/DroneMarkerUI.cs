using UnityEngine;
using UnityEngine.UI;
using TMPro;   // ★ 使用 TextMeshPro

public class DroneMarkerUI : MonoBehaviour
{
    [Header("UI 组件")]
    public Image background;
    public TextMeshProUGUI nameText;   // ★ 改成 TMP
    public TextMeshProUGUI speedText;  // ★ 改成 TMP

    [Header("跟踪目标")]
    public Transform target;       // 要跟随的世界坐标（无人机/锚点）
    public DroneInfo droneInfo;    // 对应无人机信息

    [Header("速度显示")]
    public bool useNavigatorCruiseSpeed = false;  // true=显示导航器巡航速度; false=实时速度
    public bool showSpeedInKmh = true;

    // 内部状态
    private Vector3 _lastPos;
    private float _smoothedSpeed;   // 平滑后的速度
    private RectTransform _rect;
    private Canvas _canvas;
    private Camera _cam;
    private CanvasGroup _canvasGroup;

    public void Init(DroneInfo info, Canvas canvas)
    {
        droneInfo = info;
        target = info.GetAnchor();
        _canvas = canvas;
        _cam = Camera.main;

        if (!_rect) _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // 名字和颜色初始化
        if (nameText) nameText.text = droneInfo.GetName();
        if (background) background.color = droneInfo.uiColor;

        // 初始速度
        if (target != null)
            _lastPos = target.position;
        _smoothedSpeed = 0f;
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        if (_rect == null) _rect = GetComponent<RectTransform>();

        // 1. 世界坐标 → 屏幕坐标
        Vector3 screenPos = _cam.WorldToScreenPoint(target.position);

        // 在相机背面时隐藏
        if (screenPos.z < 0f)
        {
            if (_canvasGroup) _canvasGroup.alpha = 0f;
            return;
        }

        if (_canvasGroup) _canvasGroup.alpha = 1f;

        // Canvas 是 Screen Space - Overlay，直接设 UI 的屏幕坐标即可
        _rect.position = screenPos;

        // 2. 计算速度
        double rawSpeed = 0.0;
        if (useNavigatorCruiseSpeed && droneInfo && droneInfo.navigator)
        {
            rawSpeed = droneInfo.GetCruiseSpeed(); // 导航器上的巡航速度（m/s）
        }
        else
        {
            // 实时速度（上一帧与这一帧的位置差）
            Vector3 curPos = target.position;
            float dist = Vector3.Distance(curPos, _lastPos);
            float spd = Time.deltaTime > 0f ? dist / Time.deltaTime : 0f;
            _lastPos = curPos;
            rawSpeed = spd;
        }

        // 简单平滑一下，避免数字剧烈抖动
        float targetSpeed = (float)rawSpeed;
        _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, targetSpeed, Time.deltaTime * 5f);

        // 3. 更新速度文本
        if (speedText)
        {
            if (showSpeedInKmh)
            {
                float kmh = _smoothedSpeed * 3.6f;
                speedText.text = $"{kmh:0.0} km/h";
            }
            else
            {
                speedText.text = $"{_smoothedSpeed:0.0} m/s";
            }
        }
    }
}
