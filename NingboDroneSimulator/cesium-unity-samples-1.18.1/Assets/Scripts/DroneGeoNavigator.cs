// Assets/Scripts/DroneGeoNavigator.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using CesiumForUnity;

/// <summary>
/// 让无人机按经纬高航点飞行（Cesium）
/// 抗抖策略：只对“前进方向向量”做指数平滑，最后一次性设置旋转，避免与 Anchor 的自动竖直打架。
/// 更新时序：LateUpdate + 提前执行（保证“先动目标→再跟拍”）
/// </summary>
[RequireComponent(typeof(CesiumGlobeAnchor))]
[DefaultExecutionOrder(-100)] // 关键：在 CinemachineBrain 之前执行
public class DroneGeoNavigator : MonoBehaviour
{
    [Header("Cesium")]
    public CesiumGeoreference georeference;
    public CesiumGlobeAnchor anchor;

    [Header("Waypoints")]
    [Tooltip("存放 WP1/WP2/... 的空物体")]
    public Transform waypointsParent;
    [Tooltip("按名称 WP1, WP2, ... 排序")]
    public bool sortWaypointsByName = true;

    [Header("Motion")]
    [Tooltip("巡航速度 (m/s)")]
    public double cruiseSpeed = 15.0;
    [Tooltip("航迹加密步长 (m)；越小越平滑")]
    public double densifyStepMeters = 15.0;
    [Tooltip("是否自动朝向前进方向")]
    public bool autoFaceForward = true;

    [Header("Stabilization 基础")]
    [Tooltip("（传统模式）固定前进方向平滑强度；越大越跟手，越小越稳")]
    [Range(1f, 20f)] public float headingSmooth = 6f;

    [Header("Stabilization 自适应（拐弯更稳）")]
    [Tooltip("平滑强度下限（直线路段）")]
    [Range(1f, 20f)] public float headingSmoothMin = 4f;
    [Tooltip("平滑强度上限（急拐弯）")]
    [Range(1f, 20f)] public float headingSmoothMax = 12f;
    [Tooltip("达到该夹角（度）时使用最大平滑")]
    [Range(5f, 90f)] public float cornerAngleForMaxSmooth = 45f;
    [Tooltip("向前看的距离(m)，降低对瞬时噪声的敏感")]
    [Range(0f, 20f)] public float lookaheadMeters = 3f;
    [Tooltip("临近拐点时的方向过渡窗口(m)")]
    [Range(0f, 50f)] public float cornerBlendMeters = 10f;

    // 新增：碰撞检测层（Buildings）
    public LayerMask buildingLayer;

    // 新增：射线检测最大距离（可 Inspector 调整，默认为 5m 提前检测）
    public float raycastMaxDistance = 5f;

    // —— 内部状态 ——
    private readonly List<double3> _pathLLH = new List<double3>();
    private int _segmentIndex;
    private double _tOnSegment;

    // 前进方向的指数平滑（只平滑向量，不平滑 rotation，避免与 Anchor 抢旋转）
    private Vector3 _smoothFwd;
    private bool _hasFwd = false;

    // 新增：碰撞暂停标志
    private bool _collisionPaused = false;

    void Reset()
    {
        anchor = GetComponent<CesiumGlobeAnchor>();
    }

    void Awake()
    {
        if (!anchor) anchor = GetComponent<CesiumGlobeAnchor>();
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
        anchor.adjustOrientationForGlobeWhenMoving = true;

        // 建议在 Inspector 上把 CesiumGlobeAnchor 的 Detect Transform Changes 关闭，避免互相回写引起抖动。
    }

    void Start()
    {
        if (!georeference)
        {
            Debug.LogError("[DroneGeoNavigator] CesiumGeoreference 缺失。");
            enabled = false; return;
        }
        if (!waypointsParent)
        {
            Debug.LogError("[DroneGeoNavigator] 请指定 Waypoints Parent。");
            enabled = false; return;
        }

        var anchors = waypointsParent
            .GetComponentsInChildren<CesiumGlobeAnchor>(includeInactive: false)
            .Where(a => a.gameObject != this.gameObject);

        if (sortWaypointsByName)
            anchors = anchors.OrderBy(a => a.name, System.StringComparer.Ordinal);

        var llh = anchors.Select(a => a.longitudeLatitudeHeight).ToList();
        if (llh.Count < 2)
        {
            Debug.LogError("[DroneGeoNavigator] 航点不足（至少 2 个）。");
            enabled = false; return;
        }

        var densified = DensifyLlhLinear(llh, densifyStepMeters);
        _pathLLH.Clear();
        _pathLLH.AddRange(densified);

        _segmentIndex = 0;
        _tOnSegment = 0.0;
        _hasFwd = false;

        anchor.longitudeLatitudeHeight = _pathLLH[0];
    }

    void LateUpdate()
    {
        if (_pathLLH.Count < 2 || _segmentIndex >= _pathLLH.Count - 1)
            return;

        // 新增：如果碰撞暂停，停止更新并清零微动
        if (_collisionPaused)
        {
            anchor.longitudeLatitudeHeight = anchor.longitudeLatitudeHeight; // 强制冻结 geo 位置
            transform.position = transform.position; // 冻结 Unity 位置
            return;
        }

        // —— 当前段端点（经纬高）与 Unity 坐标 —— //
        double3 A = _pathLLH[_segmentIndex];
        double3 B = _pathLLH[_segmentIndex + 1];

        Vector3 aU_curr = LLHToUnity(A);
        Vector3 bU_curr = LLHToUnity(B);

        double segLenCurr = (bU_curr - aU_curr).magnitude;
        if (segLenCurr < 1e-3)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
            return;
        }

        // —— 以 m/s 推进 —— //
        double dtMeters = cruiseSpeed * Time.deltaTime;
        double dt = dtMeters / segLenCurr;
        _tOnSegment = math.clamp(_tOnSegment + dt, 0.0, 1.0);

        // —— 插值经纬高 —— //
        double3 C = new double3(
            math.lerp(A.x, B.x, _tOnSegment),
            math.lerp(A.y, B.y, _tOnSegment),
            math.lerp(A.z, B.z, _tOnSegment)
        );

        // 新增：射线检测前方碰撞
        Vector3 currentPos = transform.position;
        Vector3 nextPos = LLHToUnity(C);
        Vector3 rayDirection = (nextPos - currentPos).normalized;
        float detectionDistance = Mathf.Max((float)dtMeters, raycastMaxDistance); // 使用最大值，确保提前检测
        Debug.Log("射线起点: " + currentPos + ", 终点: " + nextPos + ", 方向: " + rayDirection + ", 距离: " + detectionDistance);

        if (Physics.SphereCast(currentPos, 1f, rayDirection, out RaycastHit hit, detectionDistance, buildingLayer)) // 使用 SphereCast 覆盖宽度
        {
            Debug.Log("射线检测到碰撞: " + hit.collider.gameObject.name + ", 碰撞点: " + hit.point + ", 距离: " + hit.distance);
            _tOnSegment = 1.0;
            _collisionPaused = true;
            return; // 停止更新
        }
        else
        {
            Debug.Log("射线未检测到碰撞，当前层: " + buildingLayer.value + ", 射线长度: " + detectionDistance);
        }

        anchor.longitudeLatitudeHeight = C;

        Debug.Log("位置同步: " + anchor.longitudeLatitudeHeight + ", 当前位置: " + transform.position);

        // —— 生成“期望前进方向”：lookahead + 拐角混合（全部用不同变量名，避免重名） —— //
        Vector3 cU_curr = LLHToUnity(C);

        // 1) 向前看一定距离，计算 desired 方向
        int aheadCount = Mathf.Max(1, Mathf.CeilToInt(
            (float)(lookaheadMeters / Mathf.Max(0.1f, (float)densifyStepMeters))
        ));
        int aheadIndex = Mathf.Min(_segmentIndex + aheadCount, _pathLLH.Count - 1);
        Vector3 aheadU = LLHToUnity(_pathLLH[aheadIndex]);
        Vector3 desiredDir = aheadU - cU_curr;

        // 2) 临近拐角时，在当前段方向与下一段方向之间过渡（避免方向突跳）
        double distRemain = segLenCurr * (1.0 - _tOnSegment);
        if (distRemain < cornerBlendMeters && _segmentIndex + 2 < _pathLLH.Count)
        {
            Vector3 aU_corner = aU_curr;                       // 当前段起点（已算）
            Vector3 bU_corner = bU_curr;                       // 当前段终点（已算）
            Vector3 cU_nextCorner = LLHToUnity(_pathLLH[_segmentIndex + 2]); // 下一段终点

            Vector3 dirCurr = (bU_corner - aU_corner).normalized;
            Vector3 dirNext = (cU_nextCorner - bU_corner).normalized;

            float u = Mathf.SmoothStep(0f, 1f, (float)((cornerBlendMeters - distRemain) / cornerBlendMeters));
            Vector3 blended = Vector3.Slerp(dirCurr, dirNext, Mathf.Clamp01(u));

            // 与 lookahead 方向再融合（权重 0.5 可按需调整）
            desiredDir = Vector3.Slerp(desiredDir.normalized, blended, 0.5f);
        }

        // —— 抗抖：只平滑方向向量，最后一次性设置旋转 —— //
        if (autoFaceForward && desiredDir.sqrMagnitude > 1e-6f)
        {
            desiredDir.Normalize();

            if (!_hasFwd)
            {
                _smoothFwd = desiredDir;
                _hasFwd = true;
            }

            // 自适应平滑强度：拐弯越急，平滑越强（更稳）
            float angleErr = Vector3.Angle(_smoothFwd, desiredDir);
            float t = Mathf.Clamp01(angleErr / Mathf.Max(1e-3f, cornerAngleForMaxSmooth));
            float adaptiveSmooth = Mathf.Lerp(headingSmoothMin, headingSmoothMax, t);
            // 如果你更喜欢固定值，也可以直接用 headingSmooth 替代 adaptiveSmooth。

            float k = 1f - Mathf.Exp(-adaptiveSmooth * Time.deltaTime); // 指数平滑系数
            _smoothFwd = Vector3.Slerp(_smoothFwd, desiredDir, k);

            transform.rotation = Quaternion.LookRotation(_smoothFwd, transform.up);
        }

        // —— 段完成 —— //
        if (_tOnSegment >= 1.0 - 1e-6)
        {
            Debug.Log("段完成，进度: " + _tOnSegment + ", 暂停状态: " + _collisionPaused);
            _segmentIndex++;
            _tOnSegment = 0.0;
            _collisionPaused = false; // 重置暂停
        }
    }

    // —— 工具函数 —— //
    Vector3 LLHToUnity(double3 llh)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
    }

    List<double3> DensifyLlhLinear(List<double3> llh, double stepMeters)
    {
        var list = new List<double3>();
        if (llh.Count == 0) return list;
        list.Add(llh[0]);

        for (int i = 0; i < llh.Count - 1; i++)
        {
            Vector3 u0 = LLHToUnity(llh[i]);
            Vector3 u1 = LLHToUnity(llh[i + 1]);
            double dist = (u1 - u0).magnitude;
            int steps = Mathf.Max(1, Mathf.FloorToInt((float)(dist / stepMeters)));

            for (int s = 1; s <= steps; s++)
            {
                double t = (double)s / steps;
                list.Add(new double3(
                    math.lerp(llh[i].x, llh[i + 1].x, t),
                    math.lerp(llh[i].y, llh[i + 1].y, t),
                    math.lerp(llh[i].z, llh[i + 1].z, t)
                ));
            }
        }
        return list;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!georeference || !waypointsParent) return;
        var anchors = waypointsParent.GetComponentsInChildren<CesiumGlobeAnchor>(false);
        if (sortWaypointsByName)
            System.Array.Sort(anchors, (a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        Gizmos.color = Color.cyan;
        for (int i = 0; i < anchors.Length - 1; i++)
        {
            Vector3 p0 = LLHToUnity(anchors[i].longitudeLatitudeHeight);
            Vector3 p1 = LLHToUnity(anchors[i + 1].longitudeLatitudeHeight);
            Gizmos.DrawLine(p0, p1);
        }
    }
#endif
}