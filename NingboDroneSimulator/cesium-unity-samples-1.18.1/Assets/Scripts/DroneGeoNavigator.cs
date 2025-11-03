using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;
using CesiumForUnity;

[RequireComponent(typeof(CesiumGlobeAnchor))]
public class DroneGeoNavigator : MonoBehaviour
{
    public CesiumGeoreference georeference;
    public CesiumGlobeAnchor anchor;

    [Header("Waypoints")]
    public Transform waypointsParent;          // 拖拽 Waypoints 空物体
    public bool sortWaypointsByName = true;    // 名称排序：WP1, WP2, ...

    [Header("Motion")]
    [Tooltip("巡航速度 m/s")]
    public double cruiseSpeed = 15.0;
    [Tooltip("航迹加密步长 m")]
    public double densifyStepMeters = 15.0;
    public bool autoFaceForward = true;

    private List<double3> _pathLLH = new List<double3>();
    private int _segmentIndex;
    private double _tOnSegment;

    void Reset()
    {
        anchor = GetComponent<CesiumGlobeAnchor>();
    }

    void Awake()
    {
        if (!anchor) anchor = GetComponent<CesiumGlobeAnchor>();
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
        // 保持机体在球面上直立
        anchor.adjustOrientationForGlobeWhenMoving = true; // 重要！
    }

    void Start()
    {
        var anchors = waypointsParent.GetComponentsInChildren<CesiumGlobeAnchor>();
        IEnumerable<CesiumGlobeAnchor> ordered = anchors.Where(a => a.gameObject != this.gameObject);
        if (sortWaypointsByName)
            ordered = ordered.OrderBy(a => a.name, System.StringComparer.Ordinal);

        var llh = new List<double3>();
        foreach (var a in ordered)
            llh.Add(a.longitudeLatitudeHeight); // 注意：顺序是 Lon/Lat/Height（度/度/米）

        _pathLLH = DensifyLlhLinear(llh, densifyStepMeters);
        _segmentIndex = 0;
        _tOnSegment = 0.0;

        if (_pathLLH.Count > 0)
            anchor.longitudeLatitudeHeight = _pathLLH[0];
    }

    void Update()
    {
        if (_pathLLH == null || _pathLLH.Count < 2 || _segmentIndex >= _pathLLH.Count - 1)
            return;

        double3 A = _pathLLH[_segmentIndex];
        double3 B = _pathLLH[_segmentIndex + 1];

        Vector3 aU = LLHToUnity(A);
        Vector3 bU = LLHToUnity(B);
        double segLen = (bU - aU).magnitude;
        if (segLen < 1e-3)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
            return;
        }

        // 以 m/s 推进
        double dtMeters = cruiseSpeed * Time.deltaTime;
        double dt = dtMeters / segLen;
        _tOnSegment = math.clamp(_tOnSegment + dt, 0.0, 1.0);

        double3 C = new double3(
            math.lerp(A.x, B.x, _tOnSegment),
            math.lerp(A.y, B.y, _tOnSegment),
            math.lerp(A.z, B.z, _tOnSegment)
        );

        anchor.longitudeLatitudeHeight = C;

        if (autoFaceForward)
        {
            Vector3 cU = LLHToUnity(C);
            Vector3 forward = (bU - cU);
            if (forward.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(forward.normalized, transform.up);
        }

        if (_tOnSegment >= 1.0 - 1e-6)
        {
            _segmentIndex++;
            _tOnSegment = 0.0;
        }
    }

    Vector3 LLHToUnity(double3 llh)
    {
        // 经/纬为度；高度为椭球高（米）
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
            Vector3 a = LLHToUnity(llh[i]);
            Vector3 b = LLHToUnity(llh[i + 1]);
            double dist = (b - a).magnitude;
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
        var anchors = waypointsParent.GetComponentsInChildren<CesiumGlobeAnchor>()
            .OrderBy(a => a.name, System.StringComparer.Ordinal).ToList();
        for (int i = 0; i < anchors.Count - 1; i++)
        {
            Vector3 p0 = LLHToUnity(anchors[i].longitudeLatitudeHeight);
            Vector3 p1 = LLHToUnity(anchors[i + 1].longitudeLatitudeHeight);
            Gizmos.DrawLine(p0, p1);
        }
    }
#endif
}