using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;

[RequireComponent(typeof(DroneGeoNavigator))]
public class DroneSmartAvoidance : MonoBehaviour
{
    [Header("Detection")]
    public float scanDistance = 30f;
    public LayerMask obstacleLayer;
    
    [Header("Waypoint Generation")]
    [Tooltip("候选方向数量")]
    public int candidateDirections = 36;
    
    [Tooltip("候选点距离（不要太远）")]
    public float candidateDistance = 15f; // ✅ 改小到15米
    
    [Tooltip("安全半径")]
    public float safetyCheckRadius = 2f;
    
    [Header("Performance")]
    public float checkInterval = 0.5f;
    
    [Header("Debug")]
    public bool showLogs = true;
    public bool showDebugRays = true;
    public bool showDetailedValidation = false;
    
    private DroneGeoNavigator navigator;
    private CesiumGeoreference georeference;
    private float checkTimer = 0f;
    private bool isProcessing = false;
    private int insertedWaypointIndex = -1;
    
    void Start()
    {
        navigator = GetComponent<DroneGeoNavigator>();
        georeference = navigator.georeference;
        
        if (showLogs)
            Debug.Log("[避障] 系统初始化完成");
    }
    
    void Update()
    {
        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval) return;
        checkTimer = 0f;
        
        var emergencyStopField = typeof(DroneGeoNavigator).GetField("_externalEmergencyStop",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (emergencyStopField == null)
        {
            Debug.LogError("[避障] 无法访问_externalEmergencyStop字段");
            return;
        }
        
        bool isStopped = (bool)emergencyStopField.GetValue(navigator);
        
        // ✅ 添加详细状态日志
        if (showDetailedValidation)
        {
            Debug.Log($"[避障] 状态检查: isStopped={isStopped}, isProcessing={isProcessing}");
        }
        
        if (isStopped && !isProcessing)
        {
            if (showLogs)
                Debug.Log("[避障] 🔍 开始搜索安全航点...");
            
            isProcessing = true;
            FindAndInsertSafeWaypoint();
        }
        
        if (isProcessing && insertedWaypointIndex >= 0)
        {
            int currentSeg = navigator.GetCurrentSegmentIndex();
            
            if (showDetailedValidation)
            {
                Debug.Log($"[避障] 检查完成: 当前段={currentSeg}, 插入段={insertedWaypointIndex}");
            }
            
            if (currentSeg > insertedWaypointIndex)
            {
                isProcessing = false;
                insertedWaypointIndex = -1;
                
                if (showLogs)
                    Debug.Log("[避障] ✅ 已通过避障点");
            }
        }
    }
    
    void FindAndInsertSafeWaypoint()
    {
        var pathField = typeof(DroneGeoNavigator).GetField("_pathLLH",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var segmentField = typeof(DroneGeoNavigator).GetField("_segmentIndex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (pathField == null || segmentField == null)
        {
            Debug.LogError("[避障] 无法访问导航器内部数据");
            return;
        }
        
        var path = (List<double3>)pathField.GetValue(navigator);
        int currentSeg = (int)segmentField.GetValue(navigator);
        
        if (currentSeg >= path.Count - 1)
        {
            Debug.LogWarning("[避障] 已到达最后航点");
            return;
        }
        
        Vector3 currentPos = transform.position;
        Vector3 targetPos = navigator.LLHToUnity(path[currentSeg + 1]);
        Vector3 toTarget = (targetPos - currentPos).normalized;
        
        if (showLogs)
        {
            Debug.Log($"[避障] 当前位置: {currentPos}");
            Debug.Log($"[避障] 原目标位置: {targetPos}");
            Debug.Log($"[避障] 目标方向: {toTarget}");
        }
        
        List<WaypointCandidate> candidates = GenerateCandidates(currentPos, toTarget);
        List<WaypointCandidate> validCandidates = new List<WaypointCandidate>();
        
        foreach (var candidate in candidates)
        {
            bool isValid = ValidateCandidate(currentPos, candidate);
            
            if (isValid)
            {
                validCandidates.Add(candidate);
                
                if (showDebugRays)
                {
                    Debug.DrawLine(currentPos, candidate.position, Color.green, 5f);
                }
            }
            else if (showDebugRays)
            {
                Debug.DrawLine(currentPos, candidate.position, Color.red, 5f);
            }
        }
        
        if (showLogs)
            Debug.Log($"[避障] 验证完成: {validCandidates.Count}/{candidates.Count} 个点安全");
        
        validCandidates.Sort((a, b) => b.score.CompareTo(a.score));
        
        if (validCandidates.Count > 0)
        {
            var best = validCandidates[0];
            
            double3 waypointLLH = UnityToLLH(best.position);
            path.Insert(currentSeg + 1, waypointLLH);
            insertedWaypointIndex = currentSeg + 1;
            
            if (showLogs)
            {
                Debug.Log($"[避障] ✅ 成功插入安全航点！");
                Debug.Log($"    新航点位置: {best.position}");
                Debug.Log($"    从当前方向转向: {best.angleFromCurrent:F1}°");
                Debug.Log($"    与目标偏离: {best.angleDeviation:F1}°");
                Debug.Log($"    得分: {best.score:F2}");
            }
            
            // ✅ 关键：设置停止标志为false并清除内部标志
            var emergencyField = typeof(DroneGeoNavigator).GetField("_emergencyStopped",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (emergencyField != null)
            {
                emergencyField.SetValue(navigator, false);
                Debug.Log("[避障] 清除内部紧急停止标志");
            }
            
            navigator.SetEmergencyStop(false);
            Debug.Log("[避障] 🚀 解除外部停止，无人机应该开始移动");
        }
        else
        {
            Debug.LogError("[避障] ❌ 所有候选点都不安全，强制选择");
            ForceSelectBestCandidate(path, currentSeg, currentPos, toTarget);
        }
    }
    
    List<WaypointCandidate> GenerateCandidates(Vector3 origin, Vector3 targetDirection)
    {
        List<WaypointCandidate> candidates = new List<WaypointCandidate>();
        
        Vector3 baseDirection = transform.forward;
        
        for (int i = 0; i < candidateDirections; i++)
        {
            float angle = (360f / candidateDirections) * i;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * baseDirection;
            Vector3 position = origin + direction * candidateDistance;
            
            float angleDeviation = Vector3.Angle(targetDirection, direction);
            float angleFromCurrent = angle;
            if (angleFromCurrent > 180f) angleFromCurrent = 360f - angleFromCurrent;
            
            // ✅ 修复评分：优先小角度转向
            // 得分 = 100 / (1 + 当前转向角*1.0 + 目标偏离角*0.2)
            float score = 100f / (1f + angleFromCurrent * 1.0f + angleDeviation * 0.2f);
            
            candidates.Add(new WaypointCandidate
            {
                position = position,
                direction = direction,
                angleDeviation = angleDeviation,
                angleFromCurrent = angleFromCurrent,
                score = score
            });
        }
        
        return candidates;
    }
    
    bool ValidateCandidate(Vector3 startPos, WaypointCandidate candidate)
    {
        Vector3 endPos = candidate.position;
        Vector3 direction = (endPos - startPos).normalized;
        float distance = Vector3.Distance(startPos, endPos);
        
        // ✅ 简化验证：单次SphereCast
        if (Physics.SphereCast(startPos, safetyCheckRadius, direction, out RaycastHit hit, distance, obstacleLayer))
        {
            if (showDetailedValidation)
                Debug.Log($"[验证] 角度{candidate.angleFromCurrent:F0}° - 碰撞于{hit.distance:F1}m处");
            return false;
        }
        
        // 终点检查
        if (Physics.CheckSphere(endPos, safetyCheckRadius, obstacleLayer))
        {
            if (showDetailedValidation)
                Debug.Log($"[验证] 角度{candidate.angleFromCurrent:F0}° - 终点有障碍物");
            return false;
        }
        
        if (showDetailedValidation)
            Debug.Log($"[验证] 角度{candidate.angleFromCurrent:F0}° - ✅ 安全");
        
        return true;
    }
    
    void ForceSelectBestCandidate(List<double3> path, int currentSeg, Vector3 currentPos, Vector3 toTarget)
    {
        var candidates = GenerateCandidates(currentPos, toTarget);
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        
        if (candidates.Count > 0)
        {
            var forced = candidates[0];
            double3 waypointLLH = UnityToLLH(forced.position);
            path.Insert(currentSeg + 1, waypointLLH);
            insertedWaypointIndex = currentSeg + 1;
            
            var emergencyField = typeof(DroneGeoNavigator).GetField("_emergencyStopped",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (emergencyField != null)
            {
                emergencyField.SetValue(navigator, false);
            }
            
            navigator.SetEmergencyStop(false);
            
            Debug.LogWarning($"[避障] ⚠️ 强制选择方向 (转向{forced.angleFromCurrent:F0}°)");
        }
    }
    
    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 unityDouble = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(unityDouble);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugRays || !Application.isPlaying) return;
        
        Gizmos.color = new Color(1, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, scanDistance);
    }
    
    private class WaypointCandidate
    {
        public Vector3 position;
        public Vector3 direction;
        public float angleDeviation;
        public float angleFromCurrent;
        public float score;
    }
}