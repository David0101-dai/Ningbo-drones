using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;

[RequireComponent(typeof(DroneGeoNavigator))]
[RequireComponent(typeof(CesiumObstacleDetector))]
public class SimpleDroneAvoidance : MonoBehaviour
{
    [Header("Avoidance Settings")]
    public float checkInterval = 0.5f;
    public float avoidanceHeight = 30f;
    public float emergencyStopDistance = 10f;
    
    private DroneGeoNavigator navigator;
    private CesiumObstacleDetector detector;
    private CesiumGeoreference georeference;
    private float lastCheckTime;
    
    void Start()
    {
        navigator = GetComponent<DroneGeoNavigator>();
        detector = GetComponent<CesiumObstacleDetector>();
        georeference = navigator.georeference;
    }
    
    void Update()
    {
        if (Time.time - lastCheckTime < checkInterval) return;
        lastCheckTime = Time.time;
        
        // 简单的高度避障
        Vector3 forward = transform.forward;
        if (detector.CheckObstacleAhead(transform.position, forward, out Vector3 obstacle))
        {
            float distance = Vector3.Distance(transform.position, obstacle);
            
            if (distance < emergencyStopDistance)
            {
                // 紧急爬升
                ModifyNextWaypoint(avoidanceHeight);
                Debug.Log($"[简单避障] 紧急爬升 {avoidanceHeight}米");
            }
        }
    }
    
    void ModifyNextWaypoint(float heightIncrease)
    {
        // 获取路径
        var pathField = navigator.GetType().GetField("_pathLLH", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var segmentField = navigator.GetType().GetField("_segmentIndex", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (pathField != null && segmentField != null)
        {
            var path = (List<double3>)pathField.GetValue(navigator);
            int currentSegment = (int)segmentField.GetValue(navigator);
            
            if (currentSegment + 1 < path.Count)
            {
                // 提升下一个航点的高度
                double3 nextPoint = path[currentSegment + 1];
                nextPoint.z += heightIncrease;
                path[currentSegment + 1] = nextPoint;
            }
        }
    }
}