using UnityEngine;
using System.Collections.Generic;
using CesiumForUnity;

public class CesiumObstacleDetector : MonoBehaviour
{
    [Header("Detection Settings")]
    public float detectionRange = 50f;
    public float detectionHeight = 10f;
    public LayerMask obstacleLayer;
    
    [Header("Cesium Specific")]
    [Tooltip("忽略名称包含这些关键词的对象")]
    public string[] ignoreObjectNames = { "Tileset", "Tile", "CesiumTile" };
    
    [Tooltip("使用高度检测替代碰撞体大小")]
    public bool useHeightBasedDetection = true;
    
    [Tooltip("地面高度偏移")]
    public float groundOffset = 2f;
    
    private CesiumGeoreference georeference;
    
    void Start()
    {
        georeference = FindObjectOfType<CesiumGeoreference>();
    }
    
    public bool CheckObstacleAhead(Vector3 currentPos, Vector3 direction, out Vector3 obstaclePoint)
    {
        obstaclePoint = Vector3.zero;
        
        // 使用多层高度检测
        for (float h = groundOffset; h < detectionHeight; h += 2f)
        {
            Vector3 rayStart = currentPos + Vector3.up * h;
            
            if (Physics.Raycast(rayStart, direction, out RaycastHit hit, detectionRange, obstacleLayer))
            {
                // 检查是否是有效障碍物
                if (IsValidObstacle(hit))
                {
                    obstaclePoint = hit.point;
                    return true;
                }
            }
        }
        
        return false;
    }
    
    bool IsValidObstacle(RaycastHit hit)
    {
        // 检查对象名称
        string objName = hit.collider.gameObject.name.ToLower();
        foreach (string ignore in ignoreObjectNames)
        {
            if (objName.Contains(ignore.ToLower()))
            {
                return false;
            }
        }
        
        // 检查是否是建筑物（通过标签或层级判断）
        if (hit.collider.CompareTag("Building") || hit.collider.gameObject.layer == LayerMask.NameToLayer("Buildings"))
        {
            return true;
        }
        
        // 使用高度差判断是否是障碍物
        if (useHeightBasedDetection)
        {
            float heightDiff = hit.point.y - transform.position.y;
            return heightDiff > groundOffset; // 高于地面一定高度才算障碍
        }
        
        return true;
    }
    
    public List<Vector3> ScanAroundPoint(Vector3 center, float radius, int samples = 8)
    {
        List<Vector3> obstacles = new List<Vector3>();
        
        for (int i = 0; i < samples; i++)
        {
            float angle = (360f / samples) * i;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            
            if (CheckObstacleAhead(center, direction, out Vector3 obstacle))
            {
                obstacles.Add(obstacle);
            }
        }
        
        return obstacles;
    }
}