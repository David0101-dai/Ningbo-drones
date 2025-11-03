using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics; // 为double3

public class DroneRectangularPath : MonoBehaviour
{
    public GameObject waypoint1; // Inspector拖拽第一个坐标点GameObject
    public GameObject waypoint2; // Inspector拖拽第二个坐标点GameObject
    public GameObject waypoint3; // Inspector拖拽第三个坐标点GameObject
    public GameObject waypoint4; // Inspector拖拽第四个坐标点GameObject
    public float speed = 10f;

    private Vector3[] waypoints = new Vector3[4];
    private int currentWaypoint = 0;
    private CesiumGeoreference geoRef;

    void Start()
    {
        geoRef = GetComponentInParent<CesiumGeoreference>();
        if (geoRef == null) { Debug.LogError("缺少CesiumGeoreference!"); return; }
        if (waypoint1 == null || waypoint2 == null || waypoint3 == null || waypoint4 == null) { Debug.LogError("请拖拽所有四个坐标点到waypoint字段!"); return; }

        InitializeWaypoints();
    }

    void InitializeWaypoints()
    {
        GameObject[] waypointObjects = { waypoint1, waypoint2, waypoint3, waypoint4 };

        for (int i = 0; i < 4; i++)
        {
            CesiumGlobeAnchor anchor = waypointObjects[i].GetComponent<CesiumGlobeAnchor>();
            if (anchor == null) { Debug.LogError("坐标点缺少CesiumGlobeAnchor组件!"); return; }

            double longitude = anchor.longitude;
            double latitude = anchor.latitude;
            double height = anchor.height;
            double3 llh = new double3(longitude, latitude, height);
            double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
            double3 unityWp = geoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            waypoints[i] = new Vector3((float)unityWp.x, (float)unityWp.y, (float)unityWp.z);
        }

        // 设置初始无人机位置到第一个waypoint
        transform.position = waypoints[0];

        // 强制重设，防止Cesium重置到0
        if (transform.position == Vector3.zero)
        {
            transform.position = waypoints[0];
            Debug.Log("Position重置到0，已修复");
        }
    }

    void LateUpdate() // 用LateUpdate避免Cesium同步覆盖
    {
        // 锁定scale防止微增（如果有外部影响）
        if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;

        if (waypoints.Length == 0 || waypoints[0] == Vector3.zero) return; // 等待初始化

        Vector3 target = waypoints[currentWaypoint];
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        // 面向前进方向
        transform.LookAt(target);

        if (Vector3.Distance(transform.position, target) < 0.1f)
        {
            currentWaypoint = (currentWaypoint + 1) % waypoints.Length; // 循环
        }

        // 如果吸附到原点，重设
        if (transform.position == Vector3.zero || Vector3.Distance(transform.position, geoRef.transform.position) < 1f)
        {
            transform.position = waypoints[0]; // 或你的初始waypoint
            Debug.Log("重置吸附");
        }
    }
}