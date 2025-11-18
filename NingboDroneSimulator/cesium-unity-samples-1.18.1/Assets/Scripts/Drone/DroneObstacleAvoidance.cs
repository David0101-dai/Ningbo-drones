using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;
using System.Linq;

[RequireComponent(typeof(DroneGeoNavigator))]
public class DroneObstacleAvoidance : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("提前检测距离(米)")]
    public float detectionDistance = 50f;
    
    [Tooltip("检测间隔(米)")]
    public float detectionInterval = 2f;
    
    [Tooltip("障碍物检测层")]
    public LayerMask obstacleLayer;
    
    [Tooltip("检测射线半径")]
    public float detectionRadius = 3f;
    
    [Header("Avoidance Settings")]
    [Tooltip("避障高度增量(米)")]
    public float avoidanceHeightOffset = 20f;
    
    [Tooltip("横向避让距离(米)")]
    public float lateralAvoidanceDistance = 15f;
    
    [Tooltip("最大合理障碍物尺寸(米)")]
    public float maxReasonableObstacleSize = 500f;
    
    [Tooltip("路径点最大偏移距离(米)")]
    public float maxDeviationDistance = 100f;
    
    [Header("Path Validation")]
    [Tooltip("验证射线数量")]
    public int validationRayCount = 5;
    
    [Tooltip("安全裕度(米)")]
    public float safetyMargin = 5f;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool visualizeDetection = true;
    
    private DroneGeoNavigator navigator;
    private CesiumGeoreference georeference;
    private HashSet<Vector3> processedObstacles = new HashSet<Vector3>();
    private List<Vector3> currentAvoidancePath = new List<Vector3>();
    
    void Start()
    {
        navigator = GetComponent<DroneGeoNavigator>();
        georeference = navigator.georeference;
        
        if (!georeference)
        {
            Debug.LogError("[避障] 缺少CesiumGeoreference");
            enabled = false;
        }
    }
    
    void Update()
    {
        ScanForwardPath();
    }
    
    void ScanForwardPath()
    {
        // 获取导航器的内部数据
        var pathField = navigator.GetType().GetField("_pathLLH", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var segmentField = navigator.GetType().GetField("_segmentIndex", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (pathField == null || segmentField == null) return;
        
        var path = (List<double3>)pathField.GetValue(navigator);
        int currentSegment = (int)segmentField.GetValue(navigator);
        
        if (path == null || currentSegment >= path.Count - 1) return;
        
        // 获取当前位置和前方路径点
        Vector3 currentPos = transform.position;
        double3 nextLLH = path[currentSegment + 1];
        Vector3 nextPos = LLHToUnity(nextLLH);
        
        // 基础方向检测
        Vector3 moveDirection = (nextPos - currentPos).normalized;
        float checkDistance = Mathf.Min(Vector3.Distance(currentPos, nextPos), detectionDistance);
        
        // 多射线检测
        List<RaycastHit> hits = PerformMultiRaycast(currentPos, moveDirection, checkDistance);
        
        if (hits.Count > 0)
        {
            // 分析最近的有效障碍物
            RaycastHit? validHit = FindValidObstacle(hits, currentPos);
            
            if (validHit.HasValue)
            {
                Vector3 obstaclePoint = validHit.Value.point;
                
                // 检查是否已处理过这个障碍物
                bool alreadyProcessed = false;
                foreach (var processed in processedObstacles)
                {
                    if (Vector3.Distance(processed, obstaclePoint) < 10f)
                    {
                        alreadyProcessed = true;
                        break;
                    }
                }
                
                if (!alreadyProcessed)
                {
                    if (showDebugInfo)
                        Debug.Log($"[避障] 检测到障碍物，距离: {validHit.Value.distance:F1}米");
                    
                    // 生成避障路径
                    List<double3> avoidancePath = GenerateLocalAvoidancePath(
                        currentLLH: GetComponent<CesiumGlobeAnchor>().longitudeLatitudeHeight,
                        targetLLH: nextLLH,
                        obstaclePoint: obstaclePoint,
                        hitInfo: validHit.Value
                    );
                    
                    if (avoidancePath.Count > 0)
                    {
                        InsertAvoidanceWaypoints(path, currentSegment, avoidancePath);
                        processedObstacles.Add(obstaclePoint);
                        
                        // 更新可视化
                        currentAvoidancePath.Clear();
                        foreach (var llh in avoidancePath)
                        {
                            currentAvoidancePath.Add(LLHToUnity(llh));
                        }
                    }
                }
            }
        }
    }
    
    List<RaycastHit> PerformMultiRaycast(Vector3 origin, Vector3 direction, float distance)
    {
        List<RaycastHit> allHits = new List<RaycastHit>();
        
        // 中心射线
        RaycastHit[] centerHits = Physics.SphereCastAll(origin, detectionRadius, 
            direction, distance, obstacleLayer);
        allHits.AddRange(centerHits);
        
        // 扇形射线检测
        float[] angles = { -15f, -7.5f, 7.5f, 15f };
        foreach (float angle in angles)
        {
            Vector3 rotatedDir = Quaternion.Euler(0, angle, 0) * direction;
            RaycastHit[] hits = Physics.RaycastAll(origin, rotatedDir, distance, obstacleLayer);
            allHits.AddRange(hits);
        }
        
        return allHits;
    }
    
    RaycastHit? FindValidObstacle(List<RaycastHit> hits, Vector3 currentPos)
    {
        RaycastHit? closest = null;
        float minDistance = float.MaxValue;
        
        foreach (var hit in hits)
        {
            // 忽略地面
            if (hit.normal.y > 0.8f) continue;
            
            // 检查碰撞体大小是否合理
            Bounds bounds = hit.collider.bounds;
            float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            
            if (maxDimension > maxReasonableObstacleSize)
            {
                if (showDebugInfo)
                    Debug.LogWarning($"[避障] 忽略超大碰撞体: {maxDimension:F0}米");
                continue;
            }
            
            // 选择最近的有效障碍物
            if (hit.distance < minDistance)
            {
                minDistance = hit.distance;
                closest = hit;
            }
        }
        
        return closest;
    }
    
    List<double3> GenerateLocalAvoidancePath(double3 currentLLH, double3 targetLLH, 
        Vector3 obstaclePoint, RaycastHit hitInfo)
    {
        List<double3> avoidancePath = new List<double3>();
        
        Vector3 currentPos = LLHToUnity(currentLLH);
        Vector3 targetPos = LLHToUnity(targetLLH);
        
        // 计算避障方向
        Vector3 toTarget = (targetPos - currentPos).normalized;
        Vector3 toObstacle = (obstaclePoint - currentPos).normalized;
        Vector3 avoidanceNormal = Vector3.Cross(Vector3.up, toTarget).normalized;
        
        // 判断绕行方向（选择开阔的一侧）
        bool goLeft = IsPathClear(currentPos, obstaclePoint + avoidanceNormal * lateralAvoidanceDistance);
        Vector3 lateralDir = goLeft ? avoidanceNormal : -avoidanceNormal;
        
        // 生成关键路径点
        // 1. 转向点（开始避让）
        Vector3 turnPoint = currentPos + toObstacle * (hitInfo.distance - safetyMargin);
        
        // 2. 侧向绕行点
        Vector3 bypassPoint = obstaclePoint + lateralDir * lateralAvoidanceDistance;
        bypassPoint.y = Mathf.Max(currentPos.y, obstaclePoint.y) + avoidanceHeightOffset;
        
        // 3. 回归点（准备回到原路径）
        Vector3 returnPoint = bypassPoint + toTarget * (lateralAvoidanceDistance * 0.5f);
        returnPoint.y = currentPos.y + avoidanceHeightOffset * 0.5f;
        
        // 验证路径点不要偏离太远
        if (Vector3.Distance(turnPoint, currentPos) < maxDeviationDistance)
            avoidancePath.Add(UnityToLLH(turnPoint));
            
        if (Vector3.Distance(bypassPoint, currentPos) < maxDeviationDistance)
            avoidancePath.Add(UnityToLLH(bypassPoint));
            
        if (Vector3.Distance(returnPoint, currentPos) < maxDeviationDistance)
            avoidancePath.Add(UnityToLLH(returnPoint));
        
        // 验证生成的路径
        if (!ValidateAvoidancePath(avoidancePath, currentPos))
        {
            if (showDebugInfo)
                Debug.LogWarning("[避障] 生成的路径无效，尝试简单爬升");
            
            // 备选方案：简单垂直爬升
            avoidancePath.Clear();
            Vector3 climbPoint = currentPos + toTarget * 10f;
            climbPoint.y += avoidanceHeightOffset * 2;
            avoidancePath.Add(UnityToLLH(climbPoint));
        }
        
        return avoidancePath;
    }
    
    bool IsPathClear(Vector3 from, Vector3 to)
    {
        return !Physics.Linecast(from, to, obstacleLayer);
    }
    
    bool ValidateAvoidancePath(List<double3> path, Vector3 startPos)
    {
        if (path.Count == 0) return false;
        
        Vector3 lastPos = startPos;
        foreach (var llh in path)
        {
            Vector3 pos = LLHToUnity(llh);
            
            // 检查路径段是否碰撞
            if (!IsPathClear(lastPos, pos))
                return false;
                
            // 检查是否偏离过远
            if (Vector3.Distance(pos, startPos) > maxDeviationDistance)
                return false;
                
            lastPos = pos;
        }
        
        return true;
    }
    
    void InsertAvoidanceWaypoints(List<double3> mainPath, int currentIndex, List<double3> avoidancePath)
    {
        // 在当前位置后插入避障路径
        for (int i = 0; i < avoidancePath.Count; i++)
        {
            mainPath.Insert(currentIndex + 1 + i, avoidancePath[i]);
        }
        
        if (showDebugInfo)
            Debug.Log($"[避障] 插入{avoidancePath.Count}个避障点");
    }
    
    Vector3 LLHToUnity(double3 llh)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
    }
    
    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 unityDouble = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(unityDouble);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }
    
    void OnDrawGizmos()
    {
        if (!visualizeDetection || !Application.isPlaying) return;
        
        // 绘制检测范围
        Gizmos.color = new Color(0, 1, 1, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionDistance);
        
        // 绘制当前避障路径
        if (currentAvoidancePath.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < currentAvoidancePath.Count; i++)
            {
                Gizmos.DrawSphere(currentAvoidancePath[i], 2f);
                if (i > 0)
                {
                    Gizmos.DrawLine(currentAvoidancePath[i-1], currentAvoidancePath[i]);
                }
            }
        }
        
        // 绘制检测射线
        if (navigator != null)
        {
            Gizmos.color = Color.red;
            Vector3 forward = transform.forward;
            Gizmos.DrawRay(transform.position, forward * detectionDistance);
        }
    }
}