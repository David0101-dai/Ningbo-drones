// Assets/Scripts/DroneInfo.cs
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class DroneInfo : MonoBehaviour
{
    // ================================================================
    //  Display Settings
    // ================================================================
    [Header("=== Display ===")]
    public string displayName;
    public Color uiColor = Color.cyan;
    public Transform uiAnchor;

    // ================================================================
    //  Core References
    // ================================================================
    [Header("=== Core ===")]
    public DroneGeoNavigator navigator;

    [Header("=== Info Panel ===")]
    public GameObject infoPanelPrefab;

    // ================================================================
    //  Mission State
    // ================================================================
    [Header("=== Mission ===")]
    public MissionState missionState = MissionState.Idle;
    public string currentRouteName = "";

    public enum MissionState
    {
        Idle,       // No active route / reached destination
        Flying,     // Currently following a route
        Paused,     // Externally paused
        Completed   // Just completed a route (will reset to Idle)
    }

    // ================================================================
    //  Events
    // ================================================================

    /// <summary>
    /// Fired when drone reaches end of its route.
    /// Parameter: this DroneInfo instance.
    /// </summary>
    public System.Action<DroneInfo> OnRouteCompleted;

    /// <summary>
    /// Fired when mission state changes.
    /// Parameters: (DroneInfo, oldState, newState)
    /// </summary>
    public System.Action<DroneInfo, MissionState, MissionState> OnMissionStateChanged;

    // ================================================================
    //  Private
    // ================================================================
    private DroneInfoPanel _panel;
    private bool _routeCompletedFired = false;
    private Camera _cachedCamera;

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Start()
    {
        _cachedCamera = Camera.main;

        // Setup navigator event
        if (navigator != null)
        {
            // We'll check route completion in Update
            _routeCompletedFired = false;
        }

        // Create info panel
        if (infoPanelPrefab != null)
        {
            GameObject panelObj = Instantiate(infoPanelPrefab, transform);
            _panel = panelObj.GetComponent<DroneInfoPanel>();

            if (_panel != null)
            {
                _panel.targetDrone = transform;
                _panel.SetName(GetName());
            }
            else
            {
                Debug.LogError($"[{gameObject.name}] DroneInfoPanel script not found on prefab");
            }
        }

        SetMissionState(MissionState.Flying);
    }

    void Update()
    {
        // ====== Mission state tracking ======
        if (navigator != null)
        {
            UpdateMissionState();
            CheckRouteCompletion();
        }

        // ====== Info panel update ======
        if (_panel != null && navigator != null)
        {
            _panel.UpdateSpeed((float)navigator.cruiseSpeed);
            _panel.UpdateStatus(missionState.ToString());
        }

        // ====== Click to toggle info panel ======
        if (_panel != null && Input.GetMouseButtonDown(0))
        {
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            if (_cachedCamera == null) return;

            Ray ray = _cachedCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                if (hit.transform.GetComponentInParent<DroneInfo>() == this)
                {
                    _panel.TogglePanel();
                }
            }
        }

        
    }

    // ================================================================
    //  Mission State Management
    // ================================================================

    /// <summary>
    /// Automatically update mission state based on navigator status
    /// </summary>
    private void UpdateMissionState()
    {
        MissionState newState = missionState;

        if (navigator.IsPaused())
        {
            newState = MissionState.Paused;
        }
        else if (navigator.GetProgress() >= 99.9f)
        {
            newState = MissionState.Idle;
        }
        else if (!navigator.IsStopped())
        {
            newState = MissionState.Flying;
        }

        if (newState != missionState)
        {
            SetMissionState(newState);
        }
    }

    /// <summary>
    /// Set mission state and fire event
    /// </summary>
    public void SetMissionState(MissionState newState)
    {
        if (newState == missionState) return;

        MissionState oldState = missionState;
        missionState = newState;

        OnMissionStateChanged?.Invoke(this, oldState, newState);

        if (newState == MissionState.Flying)
            _routeCompletedFired = false;
    }

    /// <summary>
    /// Check if drone has reached the end of its route
    /// </summary>
    private void CheckRouteCompletion()
    {
        if (_routeCompletedFired) return;

        if (navigator.GetProgress() >= 99.9f)
        {
            _routeCompletedFired = true;
            Debug.Log($"[{GetName()}] Route completed: {currentRouteName}");
            OnRouteCompleted?.Invoke(this);
        }
    }

    /// <summary>
    /// Call this when assigning a new route to the drone
    /// </summary>
    public void AssignRoute(string routeName)
    {
        currentRouteName = routeName;
        _routeCompletedFired = false;
        SetMissionState(MissionState.Flying);
    }

    /// <summary>
    /// Mark drone as idle (no active mission)
    /// </summary>
    public void ClearMission()
    {
        currentRouteName = "";
        SetMissionState(MissionState.Idle);
    }

    // ================================================================
    //  Snapshot (for CommandCenter / SceneStateProvider / LLM)
    // ================================================================

    [System.Serializable]
    public struct Snapshot
    {
        public string name;
        public double3 positionLLH;
        public float speedMps;
        public float progressPercent;
        public string missionState;
        public string currentRoute;
        public bool isPaused;
        public bool isStopped;
        public bool isIdle;
    }

    /// <summary>
    /// Get a complete snapshot of this drone's state
    /// </summary>
    public Snapshot GetSnapshot()
    {
        var snap = new Snapshot
        {
            name = GetName(),
            missionState = missionState.ToString(),
            currentRoute = currentRouteName,
        };

        if (navigator != null)
        {
            snap.speedMps = (float)navigator.cruiseSpeed;
            snap.progressPercent = navigator.GetProgress();
            snap.isPaused = navigator.IsPaused();
            snap.isStopped = navigator.IsStopped();
            snap.isIdle = navigator.GetProgress() >= 99.9f;

            var anchor = navigator.GetComponent<CesiumGlobeAnchor>();
            if (anchor != null)
                snap.positionLLH = anchor.longitudeLatitudeHeight;
        }

        return snap;
    }

    // ================================================================
    //  Public Getters
    // ================================================================

    public string GetName()
    {
        return string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
    }

    public Transform GetAnchor()
    {
        return uiAnchor ? uiAnchor : transform;
    }

    public double GetCruiseSpeed()
    {
        return navigator ? navigator.cruiseSpeed : 0.0;
    }

    public bool IsIdle()
    {
        return missionState == MissionState.Idle;
    }

    public bool IsFlying()
    {
        return missionState == MissionState.Flying;
    }
}