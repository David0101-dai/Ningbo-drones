using UnityEngine;
using CesiumForUnity;

public class DroneGeoMovement : MonoBehaviour
{
    public double[] latitudes = { 29.86854988, 29.86854988, 29.86954988, 29.86954988 }; // 矩形Lat (调整为您的site1-4值)
    public double[] longitudes = { 121.54424917, 121.54524917, 121.54524917, 121.54424917 }; // 矩形Lon
    public double height = 67.2001; // 固定高度

    public float speed = 0.0001f; // 度/秒，≈10m/s，根据地图调整

    private CesiumGlobeAnchor anchor;
    private int currentWaypoint = 0;
    private double currentLat;
    private double currentLon;
    private double targetLat;
    private double targetLon;
    private float t = 0f; // 插值参数

    void Start()
    {
        anchor = GetComponent<CesiumGlobeAnchor>();
        if (anchor == null) { Debug.LogError("缺少CesiumGlobeAnchor!"); return; }

        // 初始位置
        currentLat = latitudes[0];
        currentLon = longitudes[0];
        SetAnchorPosition(currentLat, currentLon, height);
        UpdateTarget();
    }

    void LateUpdate()
    {
        if (t < 1f)
        {
            t += speed * Time.deltaTime;
            currentLat = Mathf.Lerp((float)currentLat, (float)targetLat, t);
            currentLon = Mathf.Lerp((float)currentLon, (float)targetLon, t);
            SetAnchorPosition(currentLat, currentLon, height);
        }
        else
        {
            currentWaypoint = (currentWaypoint + 1) % 4; // 循环
            UpdateTarget();
        }
    }

    private void UpdateTarget()
    {
        targetLat = latitudes[currentWaypoint];
        targetLon = longitudes[currentWaypoint];
        t = 0f;
    }

    private void SetAnchorPosition(double lat, double lon, double h)
    {
        anchor.latitude = lat;
        anchor.longitude = lon;
        anchor.height = h;
    }
}