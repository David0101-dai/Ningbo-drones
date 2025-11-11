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

    // —— 内部状态 ——
    // 路径经纬高列表（double3表示经度、纬度、高度）
    private readonly List<double3> _pathLLH = new List<double3>();
    // 当前段索引
    private int _segmentIndex;
    // 当前段上的进度（0到1）
    private double _tOnSegment;
    // 无人机 Rigidbody 的引用
    private Rigidbody _rb; 

    // 前进方向的指数平滑（只平滑向量，不平滑 rotation，避免与 Anchor 抢旋转）
    // 平滑后的前进向量
    private Vector3 _smoothFwd;
    // 是否已初始化前进方向
    private bool _hasFwd = false;

    // Reset方法：在Inspector重置时调用
    void Reset()
    {   
        // 获取CesiumGlobeAnchor组件
        anchor = GetComponent<CesiumGlobeAnchor>();
    }

    void Awake()
    {
        // 如果anchor为空，尝试获取组件
        if (!anchor) anchor = GetComponent<CesiumGlobeAnchor>();
        // 如果georeference为空，查找场景中的CesiumGeoreference
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
        // 设置锚点在移动时调整方向以匹配地球
        anchor.adjustOrientationForGlobeWhenMoving = true;
        // 建议在 Inspector 上把 CesiumGlobeAnchor 的 Detect Transform Changes 关闭，避免互相回写引起抖动。
        _rb = GetComponent<Rigidbody>(); // 新增：获取 Rigidbody（假设它在同一 GameObject 上）
        if (!_rb)
        {
            Debug.LogError("[DroneGeoNavigator] 无人机缺少 Rigidbody。");
            enabled = false;
        }
    }
    
    // Awake方法：在脚本实例化时调用
    void Start()
    {
        // 检查georeference是否存在
        if (!georeference)
        {
            Debug.LogError("[DroneGeoNavigator] CesiumGeoreference 缺失。");
            // 禁用脚本
            enabled = false; return;
        }
        // 检查waypointsParent是否存在
        if (!waypointsParent)
        {
            Debug.LogError("[DroneGeoNavigator] 请指定 Waypoints Parent。");
            // 禁用脚本
            enabled = false; return;
        }
        // 获取所有子物体上的CesiumGlobeAnchor组件，排除自身
        var anchors = waypointsParent
            .GetComponentsInChildren<CesiumGlobeAnchor>(includeInactive: false)
            .Where(a => a.gameObject != this.gameObject);
        // 如果需要，按名称排序
        if (sortWaypointsByName)
            anchors = anchors.OrderBy(a => a.name, System.StringComparer.Ordinal);
        // 提取经纬高列表
        var llh = anchors.Select(a => a.longitudeLatitudeHeight).ToList();
        // 检查航点数量是否至少2个
        if (llh.Count < 2)
        {
            Debug.LogError("[DroneGeoNavigator] 航点不足（至少 2 个）。");
            enabled = false; return;
        }
        // 加密路径，使其更平滑
        var densified = DensifyLlhLinear(llh, densifyStepMeters);
        // 清空并添加加密后的路径
        _pathLLH.Clear();
        _pathLLH.AddRange(densified);
        // 初始化段索引和进度
        _segmentIndex = 0;
        _tOnSegment = 0.0;
        // 重置前进方向标志
        _hasFwd = false;
        // 设置初始位置为第一个航点
        anchor.longitudeLatitudeHeight = _pathLLH[0];
    }

    void LateUpdate()
    {
        // 如果路径不足2点或已到达末尾，返回
        if (_pathLLH.Count < 2 || _segmentIndex >= _pathLLH.Count - 1)
            return;

        // —— 当前段端点（经纬高）与 Unity 坐标 —— //
        // 当前段起点经纬高
        double3 A = _pathLLH[_segmentIndex];
        // 当前段终点经纬高
        double3 B = _pathLLH[_segmentIndex + 1];
        // 转换为Unity坐标
        Vector3 aU_curr = LLHToUnity(A);
        Vector3 bU_curr = LLHToUnity(B);
        // 计算当前段长度
        double segLenCurr = (bU_curr - aU_curr).magnitude;
        // 如果段长过小，跳到下一段
        if (segLenCurr < 1e-3)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
            return;
        }

        // —— 以 m/s 推进 —— //
        // 计算deltaTime对应的距离
        double dtMeters = cruiseSpeed * Time.deltaTime;
        // 转换为段进度
        double dt = dtMeters / segLenCurr;
        // 更新进度，限制在0-1
        _tOnSegment = math.clamp(_tOnSegment + dt, 0.0, 1.0);

        // —— 插值经纬高 —— //
        // 线性插值计算当前位置经纬高
        double3 C = new double3(
            math.lerp(A.x, B.x, _tOnSegment),
            math.lerp(A.y, B.y, _tOnSegment),
            math.lerp(A.z, B.z, _tOnSegment)
        );
        // 设置锚点位置
        anchor.longitudeLatitudeHeight = C;

        // —— 生成“期望前进方向”：lookahead + 拐角混合（全部用不同变量名，避免重名） —— //
        // 当前位置Unity坐标
        Vector3 cU_curr = LLHToUnity(C);

        // 1) 向前看一定距离，计算 desired 方向
        // 计算向前看的点数
        int aheadCount = Mathf.Max(1, Mathf.CeilToInt(
            (float)(lookaheadMeters / Mathf.Max(0.1f, (float)densifyStepMeters))
        ));
        // 向前索引，限制不超过路径末尾
        int aheadIndex = Mathf.Min(_segmentIndex + aheadCount, _pathLLH.Count - 1);
        // 向前点Unity坐标
        Vector3 aheadU = LLHToUnity(_pathLLH[aheadIndex]);
        // 期望方向向量
        Vector3 desiredDir = aheadU - cU_curr;

        // 2) 临近拐角时，在当前段方向与下一段方向之间过渡（避免方向突跳）
        // 剩余距离
        double distRemain = segLenCurr * (1.0 - _tOnSegment);
        if (distRemain < cornerBlendMeters && _segmentIndex + 2 < _pathLLH.Count)
        {
            Vector3 aU_corner = aU_curr;                       // 当前段起点（已算）
            Vector3 bU_corner = bU_curr;                       // 当前段终点（已算）
            Vector3 cU_nextCorner = LLHToUnity(_pathLLH[_segmentIndex + 2]); // 下一段终点

            // 当前段方向（归一化）
            Vector3 dirCurr = (bU_corner - aU_corner).normalized;
            // 下一段方向（归一化）
            Vector3 dirNext = (cU_nextCorner - bU_corner).normalized;
            // 平滑过渡参数u（0到1）
            float u = Mathf.SmoothStep(0f, 1f, (float)((cornerBlendMeters - distRemain) / cornerBlendMeters));
            // 球面插值混合方向
            Vector3 blended = Vector3.Slerp(dirCurr, dirNext, Mathf.Clamp01(u));

            // 与 lookahead 方向再融合（权重 0.5 可按需调整）
            desiredDir = Vector3.Slerp(desiredDir.normalized, blended, 0.5f);
        }

        // —— 抗抖：只平滑方向向量，最后一次性设置旋转 —— //
        // 如果启用自动面向且方向有效
        if (autoFaceForward && desiredDir.sqrMagnitude > 1e-6f)
        {
            // 归一化期望方向
            desiredDir.Normalize();
            // 如果未初始化前进方向，直接设置
            if (!_hasFwd)
            {
                _smoothFwd = desiredDir;
                _hasFwd = true;
            }

            // 自适应平滑强度：拐弯越急，平滑越强（更稳）
            // 计算当前平滑向量与期望向量的角度差
            float angleErr = Vector3.Angle(_smoothFwd, desiredDir);
            // 归一化到0-1
            float t = Mathf.Clamp01(angleErr / Mathf.Max(1e-3f, cornerAngleForMaxSmooth));
            // 线性插值最小和最大平滑强度
            float adaptiveSmooth = Mathf.Lerp(headingSmoothMin, headingSmoothMax, t);
            // 如果你更喜欢固定值，也可以直接用 headingSmooth 替代 adaptiveSmooth。
            // 计算指数平滑系数
            float k = 1f - Mathf.Exp(-adaptiveSmooth * Time.deltaTime); // 指数平滑系数
            // 球面插值平滑前进向量
            _smoothFwd = Vector3.Slerp(_smoothFwd, desiredDir, k);
            // 设置变换旋转，面向平滑方向，保持向上向量
            transform.rotation = Quaternion.LookRotation(_smoothFwd, transform.up);
        }

        // —— 段完成 —— //
        // 如果进度接近1，进入下一段
        if (_tOnSegment >= 1.0 - 1e-6)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
        }
    }

    // —— 工具函数 —— //
    // 将经纬高转换为Unity坐标
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