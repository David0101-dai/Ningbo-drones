// Assets/Scripts/Mission/LocationManager.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class LocationManager : MonoBehaviour
{
    public static LocationManager Instance;

    [Header("References")]
    public CesiumGeoreference georeference;
    public Transform locationParent;  // Parent object for all location points

    [Header("Prefab (optional - for runtime creation)")]
    public GameObject locationPointPrefab;

    // ====== Cache ======
    private readonly List<LocationPoint> _allPoints = new();

    // ====== Events ======
    public System.Action<LocationPoint> OnPointAdded;
    public System.Action OnPointsChanged;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();

        RefreshPoints();
    }

    /// <summary>
    /// Scan scene for all LocationPoint components
    /// </summary>
    [ContextMenu("Refresh Points")]
    public void RefreshPoints()
    {
        _allPoints.Clear();

#if UNITY_2023_1_OR_NEWER
        var points = FindObjectsByType<LocationPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var points = FindObjectsOfType<LocationPoint>(true);
#endif

        _allPoints.AddRange(points);
        Debug.Log($"[LocationManager] Found {_allPoints.Count} location points " +
                  $"(Spawn:{GetSpawnPoints().Count} Pickup:{GetPickupPoints().Count} Delivery:{GetDeliveryPoints().Count})");
    }

    // ================================================================
    //  Query
    // ================================================================

    public List<LocationPoint> GetAllPoints()
        => new List<LocationPoint>(_allPoints);

    public List<LocationPoint> GetSpawnPoints()
        => _allPoints.Where(p => p.pointType == LocationPoint.PointType.SpawnPoint).ToList();

    public List<LocationPoint> GetPickupPoints()
        => _allPoints.Where(p => p.pointType == LocationPoint.PointType.PickupPoint).ToList();

    public List<LocationPoint> GetDeliveryPoints()
        => _allPoints.Where(p => p.pointType == LocationPoint.PointType.DeliveryPoint).ToList();

    public LocationPoint GetPointByName(string name)
    {
        return _allPoints.FirstOrDefault(p =>
            p.GetDisplayName() == name || p.gameObject.name == name);
    }

    public List<string> GetPointNames(LocationPoint.PointType type)
    {
        return _allPoints
            .Where(p => p.pointType == type)
            .Select(p => p.GetDisplayName())
            .ToList();
    }

    // ================================================================
    //  Runtime Creation
    // ================================================================

    /// <summary>
    /// Create a new location point at the given LLH coordinates
    /// </summary>
    public LocationPoint CreatePoint(string pointName, LocationPoint.PointType type, double3 llh)
    {
        if (georeference == null)
        {
            Debug.LogError("[LocationManager] CesiumGeoreference not found");
            return null;
        }

        // Create GameObject
        GameObject go;
        if (locationPointPrefab != null)
        {
            go = Instantiate(locationPointPrefab);
        }
        else
        {
            go = new GameObject();
        }

        go.name = pointName;

        // Parent it
        if (locationParent != null)
            go.transform.SetParent(locationParent, false);

        // Add CesiumGlobeAnchor to position it
        var anchor = go.GetComponent<CesiumGlobeAnchor>();
        if (anchor == null)
            anchor = go.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = llh;

        // Add LocationPoint component
        var lp = go.GetComponent<LocationPoint>();
        if (lp == null)
            lp = go.AddComponent<LocationPoint>();
        lp.pointType = type;
        lp.pointName = pointName;

        // Register
        _allPoints.Add(lp);

        Debug.Log($"[LocationManager] Created {type} '{pointName}' at ({llh.x:F4}, {llh.y:F4}, {llh.z:F0}m)");

        OnPointAdded?.Invoke(lp);
        OnPointsChanged?.Invoke();

        return lp;
    }

    /// <summary>
    /// Create a point from map pick coordinates (used by UI)
    /// </summary>
    public LocationPoint CreatePointFromMapPick(string pointName, LocationPoint.PointType type, double3 pickedLLH)
    {
        // Ensure minimum height
        if (pickedLLH.z < 25.0)
            pickedLLH = new double3(pickedLLH.x, pickedLLH.y, 25.0);

        return CreatePoint(pointName, type, pickedLLH);
    }

    // ================================================================
    //  Status
    // ================================================================

    public string GetStatusText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Location Points: {_allPoints.Count} total");

        var spawns = GetSpawnPoints();
        if (spawns.Count > 0)
        {
            sb.AppendLine($"  Spawn Points ({spawns.Count}):");
            foreach (var p in spawns)
                sb.AppendLine($"    - {p.GetDisplayName()}");
        }

        var pickups = GetPickupPoints();
        if (pickups.Count > 0)
        {
            sb.AppendLine($"  Pickup Points ({pickups.Count}):");
            foreach (var p in pickups)
                sb.AppendLine($"    - {p.GetDisplayName()}");
        }

        var deliveries = GetDeliveryPoints();
        if (deliveries.Count > 0)
        {
            sb.AppendLine($"  Delivery Points ({deliveries.Count}):");
            foreach (var p in deliveries)
                sb.AppendLine($"    - {p.GetDisplayName()}");
        }

        return sb.ToString();
    }
}