// Assets/Scripts/TopDownPanZoom.cs
using UnityEngine;

[AddComponentMenu("Camera/TopDown Pan & Zoom (no-follow)")]
public class TopDownPanZoom : MonoBehaviour
{
    [Header("Keys")]
    public KeyCode forwardKey = KeyCode.W;
    public KeyCode backKey    = KeyCode.S;
    public KeyCode leftKey    = KeyCode.A;
    public KeyCode rightKey   = KeyCode.D;
    public KeyCode yawLeftKey  = KeyCode.Q;  // 可选：左转
    public KeyCode yawRightKey = KeyCode.E;  // 可选：右转
    public bool holdShiftForFast = true;     // Shift 加速
    public float fastMultiplier   = 3f;
    public bool holdAltForSlow    = true;    // Alt 减速
    public float slowMultiplier   = 0.5f;

    [Header("Plane Move")]
    public float moveSpeed = 300f;          // 基础平移速度(米/秒)
    public float speedScaleByHeight = 0.4f; // 高度越高越快(0=关闭)
    public float moveDamping = 10f;         // 有输入时的平移平滑(0=无)
    public bool instantStopWhenNoInput = true;  // 松手瞬停(无残余惯性)

    [Header("Height (mouse wheel)")]
    public float heightStepPerNotch = 80f;  // 每格滚轮升降多少米
    public float heightMin = 50f;
    public float heightMax = 20000f;
    public float heightDamping = 12f;       // 仅在滚动“进行中”使用
    public bool heightSnapOnRelease = true; // 滚轮停=立停
    public float heightScrollActiveTimeout = 0.12f; // 判定“仍在滚”的宽限(秒)

    [Header("Yaw (around Up)")]
    public float yawDegPerSec = 90f;        // Q/E 旋转速度
    public float yawDamping = 12f;          // 旋转平滑
    public bool yawInstantStopWhenNoInput = true;

    [Header("Up Vector")]
    public bool useWorldUp = true;          // 通常 true 足够
    public Transform upReference;           // 若想用地理Up，可拖一个带 GlobeAnchor 的变换

    // 内部状态
    float _targetHeight;
    float _targetYaw;       // 目标偏航角(度)
    float _currentYaw;      // 当前偏航角(用于平滑)
    Vector3 _vel;           // 平移阻尼速度
    float _yawVel;          // 角速度
    float _heightVel;       // 高度速度
    float _scrollActiveT;   // “滚轮仍在活跃”的倒计时

    void Start()
    {
        Vector3 up = GetUp();
        _targetHeight = Vector3.Dot(transform.position, up);

        // 以当前朝向初始化偏航
        Vector3 rightOnPlane = Vector3.ProjectOnPlane(transform.right, up).normalized;
        _currentYaw = _targetYaw = SignedAngleOnPlane(rightOnPlane, Vector3.right, up);
    }

    void Update()
    {
        Vector3 up = GetUp();

        // —— 偏航基底（按屏幕朝向定义 forward/right）——
        Quaternion yawRot = Quaternion.AngleAxis(_currentYaw, up);
        Vector3 right = (yawRot * Vector3.right).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        // —— WASD 平移（平面内）——
        float v = (Input.GetKey(forwardKey) ? 1f : 0f) - (Input.GetKey(backKey) ? 1f : 0f);
        float h = (Input.GetKey(rightKey)   ? 1f : 0f) - (Input.GetKey(leftKey) ? 1f : 0f);
        Vector3 movePlanar = (forward * v + right * h);
        if (movePlanar.sqrMagnitude > 1e-6f) movePlanar.Normalize();

        // 速度倍率：Shift/Alt + 高度自适应
        float mul = 1f;
        if (holdShiftForFast && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) mul *= fastMultiplier;
        if (holdAltForSlow  && (Input.GetKey(KeyCode.LeftAlt)   || Input.GetKey(KeyCode.RightAlt)))   mul *= slowMultiplier;

        float heightNow = Vector3.Dot(transform.position, up);
        float heightMul = 1f + Mathf.Max(0f, heightNow) * speedScaleByHeight / 1000f;
        float speed = moveSpeed * mul * heightMul;

        Vector3 desiredPlanarDelta = movePlanar * speed * Time.unscaledDeltaTime;

        // —— 鼠标滚轮：改高度（沿 Up）——
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.001f)
        {
            _targetHeight = Mathf.Clamp(_targetHeight + (-wheel) * heightStepPerNotch, heightMin, heightMax);
            _scrollActiveT = heightScrollActiveTimeout; // 进入“滚动活跃期”
        }
        else
        {
            _scrollActiveT -= Time.unscaledDeltaTime;
        }

        // —— Q/E 偏航 —— 
        float yawInput = 0f;
        if (Input.GetKey(yawLeftKey))  yawInput -= 1f;
        if (Input.GetKey(yawRightKey)) yawInput += 1f;
        _targetYaw += yawInput * yawDegPerSec * Time.unscaledDeltaTime;

        // —— 目标位置计算：平面位移 + 目标高度 —— 
        Vector3 pos = transform.position;
        float currentHeight = Vector3.Dot(pos, up);
        Vector3 planarPos = pos - up * currentHeight;
        Vector3 targetPlanarPos = planarPos + desiredPlanarDelta;

        // 高度：滚动期间用平滑；滚动停止后“立停”
        float newHeight;
        if (heightSnapOnRelease && _scrollActiveT <= 0f)
        {
            newHeight = _targetHeight;
            _heightVel = 0f; // 清除残余速度
        }
        else
        {
            newHeight = Smooth(currentHeight, _targetHeight, heightDamping, ref _heightVel);
        }

        Vector3 targetPos = targetPlanarPos + up * newHeight;

        // 平移：有输入才做平滑；没输入则立停（可选）
        Vector3 newPos;
        if (instantStopWhenNoInput && movePlanar.sqrMagnitude < 1e-6f)
        {
            _vel = Vector3.zero;              // 立停
            newPos = new Vector3(planarPos.x, 0f, planarPos.z) + up * newHeight; // 仅高度变化
        }
        else
        {
            newPos = Vector3.SmoothDamp(pos, targetPos, ref _vel, DampingToTime(moveDamping), Mathf.Infinity, Time.unscaledDeltaTime);
        }

        // 偏航：有输入才平滑；无输入可选立停
        float newYaw;
        if (yawInstantStopWhenNoInput && Mathf.Approximately(yawInput, 0f))
        {
            _yawVel = 0f;
            newYaw = _targetYaw; // 直接到目标
        }
        else
        {
            newYaw = Mathf.SmoothDampAngle(_currentYaw, _targetYaw, ref _yawVel, DampingToTime(yawDamping), Mathf.Infinity, Time.unscaledDeltaTime);
        }
        _currentYaw = newYaw;

        // —— 始终朝“正下方”（Top-Down）——
        transform.position = newPos;
        Quaternion yawOnly = Quaternion.AngleAxis(_currentYaw, up);
        transform.rotation = Quaternion.LookRotation(-up, Vector3.Cross(-up, yawOnly * Vector3.right));
    }

    Vector3 GetUp()
    {
        if (!useWorldUp && upReference) return upReference.up.normalized;
        return Vector3.up;
    }

    static float DampingToTime(float damping) => damping <= 0f ? 0f : 1f / Mathf.Max(0.0001f, damping);
    static float Smooth(float cur, float target, float damping, ref float vel)
    {
        if (damping <= 0f) return target;
        return Mathf.SmoothDamp(cur, target, ref vel, DampingToTime(damping), Mathf.Infinity, Time.unscaledDeltaTime);
    }
    static float SignedAngleOnPlane(Vector3 from, Vector3 to, Vector3 planeNormal)
    {
        Vector3 f = Vector3.ProjectOnPlane(from, planeNormal).normalized;
        Vector3 t = Vector3.ProjectOnPlane(to,   planeNormal).normalized;
        return Vector3.SignedAngle(f, t, planeNormal);
    }
}