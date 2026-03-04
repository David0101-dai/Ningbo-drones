// Assets/Scripts/Mission/LocationPoint.cs
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class LocationPoint : MonoBehaviour
{
    public enum PointType
    {
        SpawnPoint,     // Drone launch/home base
        PickupPoint,    // Restaurant/pickup location
        DeliveryPoint   // Customer/delivery location
    }

    [Header("Location Settings")]
    public PointType pointType = PointType.PickupPoint;
    public string pointName = "";

    [Header("Visual")]
    public Color gizmoColor = Color.green;
    public float gizmoRadius = 3f;

    /// <summary>
    /// Get this point's LLH coordinates
    /// </summary>
    public double3 GetLLH()
    {
        var anchor = GetComponent<CesiumGlobeAnchor>();
        if (anchor != null)
            return anchor.longitudeLatitudeHeight;

        // Fallback: convert from Unity world position
        var geo = FindObjectOfType<CesiumGeoreference>();
        if (geo != null)
        {
            double3 unity = new double3(transform.position.x, transform.position.y, transform.position.z);
            double3 ecef = geo.TransformUnityPositionToEarthCenteredEarthFixed(unity);
            return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
        }

        return double3.zero;
    }

    public string GetDisplayName()
    {
        if (!string.IsNullOrEmpty(pointName)) return pointName;
        return gameObject.name;
    }

    void OnDrawGizmos()
    {
        switch (pointType)
        {
            case PointType.SpawnPoint:    gizmoColor = Color.blue; break;
            case PointType.PickupPoint:   gizmoColor = Color.yellow; break;
            case PointType.DeliveryPoint: gizmoColor = Color.red; break;
        }

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
        Gizmos.DrawSphere(transform.position, gizmoRadius * 0.3f);
    }
}