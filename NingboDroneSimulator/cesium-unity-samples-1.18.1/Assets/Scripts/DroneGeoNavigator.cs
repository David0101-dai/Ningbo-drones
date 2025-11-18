// Assets/Scripts/DroneGeoNavigator.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using CesiumForUnity;

[RequireComponent(typeof(CesiumGlobeAnchor))]
[DefaultExecutionOrder(-100)]
public class DroneGeoNavigator : MonoBehaviour
{
    #region Inspector字段
    
    [Header("=== Cesium组件 ===")]
    public CesiumGeoreference georeference;
    public CesiumGlobeAnchor anchor;

    [Header("=== 航点设置 ===")]
    public Transform waypointsParent;
    public bool sortWaypointsByName = true;

    [Header("=== 运动参数 ===")]
    public double cruiseSpeed = 15.0;
    [Range(5f, 50f)]
    public float densifyStepMeters = 15f;
    public bool autoFaceForward = true;

    [Header("=== 方向平滑 ===")]
    [Range(1f, 20f)] public float headingSmoothMin = 4f;
    [Range(1f, 20f)] public float headingSmoothMax = 12f;
    [Range(5f, 90f)] public float cornerAngleForMaxSmooth = 45f;
    [Range(0f, 20f)] public float lookaheadMeters = 3f;
    [Range(0f, 50f)] public float cornerBlendMeters = 10f;

    [Header("=== 启动设置 ===")]
    [Range(0f, 10f)]
    public float startupDelay = 5f;

    [Header("=== 碰撞检测 ===")]
    public bool enableEmergencyStop = true;
    public LayerMask obstacleLayer;
    public float emergencyStopDistance = 3f;

    [Header("=== 调试 ===")]
    public bool showProgressLogs = true;
    public bool showPathGizmos = true;
    public bool showMovementLogs = false; // ✅ 新增：移动日志
    
    #endregion

    #region 私有字段
    
    private readonly List<double3> _pathLLH = new List<double3>();
    private int _segmentIndex;
    private double _tOnSegment;
    private Vector3 _smoothFwd;
    private bool _hasFwd = false;
    private float _startupTimer = 0f;
    private bool _isStarted = false;
    private bool _emergencyStopped = false;
    private bool _externalEmergencyStop = false;
    
    #endregion

    public void SetEmergencyStop(bool stop)
    {
        bool wasExternalStop = _externalEmergencyStop;
        _externalEmergencyStop = stop;
        
        // ✅ 添加日志确认
        if (wasExternalStop != stop)
        {
            Debug.Log($"[导航器] 外部停止状态变更: {wasExternalStop} → {stop}");
        }
    }

    void Reset()
    {
        anchor = GetComponent<CesiumGlobeAnchor>();
    }

    void Awake()
    {
        if (!anchor) anchor = GetComponent<CesiumGlobeAnchor>();
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
        anchor.adjustOrientationForGlobeWhenMoving = true;
    }

    void Start()
    {
        if (!georeference)
        {
            Debug.LogError("[导航器] 缺少CesiumGeoreference！");
            enabled = false; return;
        }
        
        if (!waypointsParent)
        {
            Debug.LogError("[导航器] 未指定航点父物体！");
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
            Debug.LogError("[导航器] 航点不足（至少2个）！");
            enabled = false; return;
        }

        Debug.Log($"[导航器] 已加载{llh.Count}个航点");

        var densified = DensifyLlhLinear(llh, (double)densifyStepMeters);
        _pathLLH.Clear();
        _pathLLH.AddRange(densified);

        _segmentIndex = 0;
        _tOnSegment = 0.0;
        _hasFwd = false;

        anchor.longitudeLatitudeHeight = _pathLLH[0];
        Debug.Log($"[导航器] 初始化完成，起点: {_pathLLH[0]}");
    }

    void LateUpdate()
    {
        // ===== 启动延迟 =====
        if (!_isStarted)
        {
            _startupTimer += Time.deltaTime;
            
            if (_startupTimer < startupDelay)
            {
                if (Time.frameCount % 30 == 0)
                {
                    Debug.Log($"[导航器] 等待场景加载... {startupDelay - _startupTimer:F1}秒");
                }
                return;
            }
            
            _isStarted = true;
            Debug.Log("[导航器] ✈️ 开始飞行！");
        }

        // ===== ✅ 修复：外部停止检查（添加详细日志） =====
        if (_externalEmergencyStop)
        {
            if (showMovementLogs)
                Debug.Log($"[导航器] 外部停止中... (_externalEmergencyStop={_externalEmergencyStop})");
            return;
        }

        // ===== 路径完成检查 =====
        if (_pathLLH.Count < 2 || _segmentIndex >= _pathLLH.Count - 1)
        {
            if (showProgressLogs && _segmentIndex >= _pathLLH.Count - 1)
                Debug.Log("[导航器] 🎯 已到达终点！");
            return;
        }

        // ===== ✅ 修复：紧急停止检测（不覆盖外部解除） =====
        if (enableEmergencyStop)
        {
            Vector3 checkOrigin = transform.position;
            Vector3 checkDirection = transform.forward;
            
            bool hasImmediateThreat = false;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 dir = Quaternion.Euler(0, i * 10f, 0) * checkDirection;
                if (Physics.Raycast(checkOrigin, dir, emergencyStopDistance, obstacleLayer))
                {
                    hasImmediateThreat = true;
                    break;
                }
            }
            
            if (hasImmediateThreat)
            {
                // ✅ 只在首次检测到时触发
                if (!_emergencyStopped)
                {
                    _emergencyStopped = true;
                    _externalEmergencyStop = true;
                    Debug.LogWarning("[导航器] ⚠️ 检测到障碍，触发紧急停止");
                }
                // ✅ 已经停止的情况下，不重复设置
            }
            else
            {
                // 前方安全，清除内部标志（但不清除外部标志）
                _emergencyStopped = false;
            }
        }

        // ===== 获取当前段 =====
        double3 pointA = _pathLLH[_segmentIndex];
        double3 pointB = _pathLLH[_segmentIndex + 1];

        Vector3 unityA = LLHToUnity(pointA);
        Vector3 unityB = LLHToUnity(pointB);

        double segmentLength = (unityB - unityA).magnitude;
        
        if (segmentLength < 1e-3)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
            return;
        }

        // ===== 计算移动 =====
        double distanceThisFrame = cruiseSpeed * Time.deltaTime;
        double progressIncrement = distanceThisFrame / segmentLength;
        _tOnSegment = math.clamp(_tOnSegment + progressIncrement, 0.0, 1.0);

        // ✅ 添加移动日志
        if (showMovementLogs && Time.frameCount % 10 == 0)
        {
            Debug.Log($"[导航器] 移动中: 段{_segmentIndex}, 进度{_tOnSegment:F2}, 速度{distanceThisFrame:F2}m/frame");
        }

        double3 currentLLH = new double3(
            math.lerp(pointA.x, pointB.x, _tOnSegment),
            math.lerp(pointA.y, pointB.y, _tOnSegment),
            math.lerp(pointA.z, pointB.z, _tOnSegment)
        );

        anchor.longitudeLatitudeHeight = currentLLH;

        // ===== 计算方向 =====
        if (autoFaceForward)
        {
            Vector3 currentUnity = LLHToUnity(currentLLH);
            
            int lookaheadCount = Mathf.Max(1, Mathf.CeilToInt(lookaheadMeters / Mathf.Max(0.1f, densifyStepMeters)));
            int lookaheadIndex = Mathf.Min(_segmentIndex + lookaheadCount, _pathLLH.Count - 1);
            Vector3 lookaheadUnity = LLHToUnity(_pathLLH[lookaheadIndex]);
            Vector3 desiredDirection = (lookaheadUnity - currentUnity).normalized;

            double remainingDistance = segmentLength * (1.0 - _tOnSegment);
            if (remainingDistance < cornerBlendMeters && _segmentIndex + 2 < _pathLLH.Count)
            {
                Vector3 currentSegmentDir = (unityB - unityA).normalized;
                Vector3 nextSegmentEnd = LLHToUnity(_pathLLH[_segmentIndex + 2]);
                Vector3 nextSegmentDir = (nextSegmentEnd - unityB).normalized;

                float blendFactor = Mathf.SmoothStep(0f, 1f, 
                    (float)((cornerBlendMeters - remainingDistance) / cornerBlendMeters));
                
                Vector3 blendedDirection = Vector3.Slerp(currentSegmentDir, nextSegmentDir, blendFactor);
                desiredDirection = Vector3.Slerp(desiredDirection, blendedDirection, 0.5f);
            }

            if (desiredDirection.sqrMagnitude > 1e-6f)
            {
                if (!_hasFwd)
                {
                    _smoothFwd = desiredDirection;
                    _hasFwd = true;
                }
                else
                {
                    float angleError = Vector3.Angle(_smoothFwd, desiredDirection);
                    float adaptiveFactor = Mathf.Clamp01(angleError / Mathf.Max(1e-3f, cornerAngleForMaxSmooth));
                    float smoothStrength = Mathf.Lerp(headingSmoothMin, headingSmoothMax, adaptiveFactor);
                    
                    float smoothCoeff = 1f - Mathf.Exp(-smoothStrength * Time.deltaTime);
                    _smoothFwd = Vector3.Slerp(_smoothFwd, desiredDirection, smoothCoeff);
                }

                transform.rotation = Quaternion.LookRotation(_smoothFwd, transform.up);
            }
        }

        // ===== 段完成 =====
        if (_tOnSegment >= 1.0 - 1e-6)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
            
            if (showProgressLogs)
            {
                float progress = (float)_segmentIndex / (_pathLLH.Count - 1) * 100f;
                Debug.Log($"[导航器] 进度: {progress:F1}% (段 {_segmentIndex}/{_pathLLH.Count - 1})");
            }
        }
    }

    public Vector3 LLHToUnity(double3 llh)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
    }

    List<double3> DensifyLlhLinear(List<double3> waypoints, double stepMeters)
    {
        var densifiedPath = new List<double3>();
        if (waypoints.Count == 0) return densifiedPath;
        densifiedPath.Add(waypoints[0]);

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 unityStart = LLHToUnity(waypoints[i]);
            Vector3 unityEnd = LLHToUnity(waypoints[i + 1]);
            
            double distance = (unityEnd - unityStart).magnitude;
            int subdivisions = Mathf.Max(1, Mathf.FloorToInt((float)(distance / stepMeters)));

            for (int s = 1; s <= subdivisions; s++)
            {
                double t = (double)s / subdivisions;
                double3 interpolated = new double3(
                    math.lerp(waypoints[i].x, waypoints[i + 1].x, t),
                    math.lerp(waypoints[i].y, waypoints[i + 1].y, t),
                    math.lerp(waypoints[i].z, waypoints[i + 1].z, t)
                );
                densifiedPath.Add(interpolated);
            }
        }
        
        return densifiedPath;
    }

    public int GetCurrentSegmentIndex() => _segmentIndex;
    public int GetTotalSegments() => _pathLLH.Count - 1;
    public float GetProgress() => _pathLLH.Count < 2 ? 0f : (float)_segmentIndex / (_pathLLH.Count - 1) * 100f;
    public List<double3> GetPath() => _pathLLH;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showPathGizmos || !georeference || !waypointsParent) return;

        var anchors = waypointsParent.GetComponentsInChildren<CesiumGlobeAnchor>(false);
        if (sortWaypointsByName)
            System.Array.Sort(anchors, (a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        Gizmos.color = Color.cyan;
        for (int i = 0; i < anchors.Length - 1; i++)
        {
            Vector3 start = LLHToUnity(anchors[i].longitudeLatitudeHeight);
            Vector3 end = LLHToUnity(anchors[i + 1].longitudeLatitudeHeight);
            Gizmos.DrawLine(start, end);
        }

        Gizmos.color = Color.yellow;
        foreach (var anc in anchors)
        {
            Vector3 pos = LLHToUnity(anc.longitudeLatitudeHeight);
            Gizmos.DrawWireSphere(pos, 2f);
        }

        if (Application.isPlaying && _isStarted && _pathLLH.Count > 0)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 3f);
            
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, transform.forward * 10f);
            
            if (_segmentIndex < _pathLLH.Count - 1)
            {
                Vector3 targetPos = LLHToUnity(_pathLLH[_segmentIndex + 1]);
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, targetPos);
                Gizmos.DrawWireSphere(targetPos, 2f);
            }
        }
    }
#endif
}