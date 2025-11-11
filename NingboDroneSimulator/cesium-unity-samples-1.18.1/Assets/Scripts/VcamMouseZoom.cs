// Assets/Scripts/VcamMouseZoom.cs
using UnityEngine;
using Cinemachine;

/// <summary>
/// 鼠标滚轮缩放 Cinemachine 2.x 虚拟相机：
/// - 优先只影响当前 Live 的 vcam（可关闭改为全部）
/// - Transposer：沿当前 FollowOffset 方向缩放距离（保持相对方位不变）
/// - FramingTransposer：调 m_CameraDistance
/// - 可选同时/单独调 FOV
/// </summary>
[AddComponentMenu("Camera/Vcam Mouse Zoom (Cinemachine 2.x)")]
public class VcamMouseZoom : MonoBehaviour
{
    [Header("Target vcams（至少拖你会用到的几台）")]
    public CinemachineVirtualCamera[] vcams;    // 例如：RearChase、Side

    [Header("作用范围")]
    public bool onlyAffectLiveCamera = true;    // 仅作用于当前 Live vcam
    public bool adjustDistance = true;          // 调整距离（Transposer/FramingTransposer）
    public bool adjustFOV = false;              // （可选）同时/改为调整 FOV

    [Header("距离缩放（Transposer/Framing）")]
    public float distanceMin = 2.0f;            // 最小距离（跟得很近）
    public float distanceMax = 30.0f;           // 最大距离
    public float distanceStepPerNotch = 0.8f;   // 每个滚轮刻度变化多少“距离”
    public float distanceSmoothing = 10f;       // 距离平滑（0 = 立即）

    [Header("FOV 缩放（Lens.FieldOfView）")]
    public float fovMin = 28f;
    public float fovMax = 60f;
    public float fovStepPerNotch = 2.0f;        // 每个滚轮刻度变化多少度
    public float fovSmoothing = 10f;            // FOV 平滑（0 = 立即）

    [Header("按键修饰（可选）")]
    public bool useShiftForFast = true;
    public float fastMultiplier = 2.0f;
    public bool useAltForSlow = true;
    public float slowMultiplier = 0.5f;

    CinemachineBrain _brain;

    void Awake()
    {
        if (!_brain) _brain = Camera.main ? Camera.main.GetComponent<CinemachineBrain>() : null;
    }

    void Update()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Approximately(scroll, 0f)) return;

        // 修饰键速度
        float speedMul = 1f;
        if (useShiftForFast && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            speedMul *= fastMultiplier;
        if (useAltForSlow && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            speedMul *= slowMultiplier;

        // 通常“滚轮向前”希望更靠近/更大：取反
        float zoomDelta = -scroll * speedMul;

        if (onlyAffectLiveCamera && TryGetLiveVcam(out var live))
        {
            ApplyZoomToVcam(live, zoomDelta);
        }
        else
        {
            // 同时作用于列表里所有 vcam（更简单，也能保证切换视角时缩放保持一致）
            foreach (var v in vcams)
                if (v) ApplyZoomToVcam(v, zoomDelta);
        }
    }

    bool TryGetLiveVcam(out CinemachineVirtualCamera vcam)
    {
        vcam = null;
        if (!_brain) _brain = Camera.main ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (_brain == null) return false;

        // ActiveVirtualCamera 可能是任意 ICinemachineCamera，这里尝试转成 VirtualCameraBase
        var live = _brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
        if (live == null && _brain.ActiveBlend != null)
            live = _brain.ActiveBlend.CamB as CinemachineVirtualCameraBase;

        vcam = live as CinemachineVirtualCamera;   // 我们场景用的是 VirtualCamera
        return vcam != null;
    }

    void ApplyZoomToVcam(CinemachineVirtualCamera vcam, float zoomDelta)
    {
        if (adjustDistance)
            ZoomDistance(vcam, zoomDelta);

        if (adjustFOV)
            ZoomFOV(vcam, zoomDelta);
    }

    void ZoomDistance(CinemachineVirtualCamera vcam, float zoomDelta)
    {
        // 优先找 Transposer
        var transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer != null)
        {
            // 沿当前 Offset 方向缩放（保持侧/后方位关系）
            Vector3 off = transposer.m_FollowOffset;
            float mag = Mathf.Max(0.001f, off.magnitude);
            float targetMag = Mathf.Clamp(mag + zoomDelta * distanceStepPerNotch, distanceMin, distanceMax);

            // 平滑到 targetMag
            float newMag = Smooth(mag, targetMag, distanceSmoothing);
            transposer.m_FollowOffset = off.normalized * newMag;
            return;
        }

        // 其次找 Framing Transposer
        var framing = vcam.GetCinemachineComponent<CinemachineFramingTransposer>();
        if (framing != null)
        {
            float dist = framing.m_CameraDistance;
            float target = Mathf.Clamp(dist + zoomDelta * distanceStepPerNotch, distanceMin, distanceMax);
            framing.m_CameraDistance = Smooth(dist, target, distanceSmoothing);
        }
    }

    void ZoomFOV(CinemachineVirtualCamera vcam, float zoomDelta)
    {
        var lens = vcam.m_Lens;
        float fov = lens.FieldOfView;
        float target = Mathf.Clamp(fov + zoomDelta * fovStepPerNotch, fovMin, fovMax);
        lens.FieldOfView = Smooth(fov, target, fovSmoothing);
        vcam.m_Lens = lens;
    }

    float Smooth(float current, float target, float smooth)
    {
        if (smooth <= 0f) return target;
        float k = 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
        return Mathf.Lerp(current, target, k);
    }
}