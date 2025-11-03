using UnityEngine;
using CesiumForUnity;

public class FixGeoreferenceOrigin : MonoBehaviour
{
    public double fixedLatitude = 29.87031;    // 固定Latitude值（从截图复制）
    public double fixedLongitude = 121.5459;   // 固定Longitude值
    public double fixedHeight = 462.4608;      // 固定Height值
    public double fixedEcefX = -2896245;       // 固定ECEF X值
    public double fixedEcefY = 4717754;        // 固定ECEF Y值
    public double fixedEcefZ = 3158146;        // 固定ECEF Z值

    private CesiumGeoreference geoRef;

    void Start()
    {
        geoRef = GetComponent<CesiumGeoreference>();
        if (geoRef == null)
        {
            Debug.LogError("缺少CesiumGeoreference组件！");
            return;
        }

        // 初始化固定值
        LockOriginValues();
    }

    void LateUpdate() // 在LateUpdate中每帧强制锁定，防止Cesium动态变化
    {
        LockOriginValues();
    }

    private void LockOriginValues()
    {
        // 锁定Cartographic Origin
        geoRef.latitude = fixedLatitude;
        geoRef.longitude = fixedLongitude;
        geoRef.height = fixedHeight;

        // 锁定ECEF Origin
        geoRef.ecefX = fixedEcefX;
        geoRef.ecefY = fixedEcefY;
        geoRef.ecefZ = fixedEcefZ;

        // 调试日志，检查是否锁定
        Debug.Log("Origin固定: Lat=" + geoRef.latitude + ", Lon=" + geoRef.longitude + ", Height=" + geoRef.height);
    }
}