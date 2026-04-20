// Assets/Scripts/Drone/DroneInfo.cs
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
    public DroneSpec spec;            // 新增：无人机规格

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
        Idle,
        Flying,
        Paused,
        Charging,     // 新增：充电中
        Completed
    }

    // ================================================================
    //  Events
    // ================================================================
    public System.Action<DroneInfo> OnRouteCompleted;
    public System.Action<DroneInfo, MissionState, MissionState> OnMissionStateChanged;
    public System.Action<DroneInfo> OnLowBattery;    // 新增

    // ================================================================
    //  Private
    // ================================================================
    private DroneInfoPanel _panel;
    private bool _routeCompletedFired = false;
    private Camera _cachedCamera;
    private bool _lowBatteryFired = false;

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Start()
    {
        _cachedCamera = Camera.main;

        if (navigator != null)
            _routeCompletedFired = false;

        // Auto-find DroneSpec if not assigned
        if (spec == null)
            spec = GetComponent<DroneSpec>();

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
                DLog.Error("General",$"[{gameObject.name}] DroneInfoPanel script not found on prefab");
            }
        }

        if (navigator != null && !navigator.HasNoPath() && navigator.GetProgress() < 99.9f && navigator.GetTotalSegments() > 0)
            SetMissionState(MissionState.Flying);
        else
            SetMissionState(MissionState.Idle);
    }

    void Update()
    {
        // ====== Mission state tracking ======
        if (navigator != null)
        {
            UpdateMissionState();
            CheckRouteCompletion();
        }

        // ====== Battery consumption ======
        if (spec != null && navigator != null && missionState == MissionState.Flying)
        {
            spec.ConsumeBattery(navigator.cruiseSpeed, Time.deltaTime);

            // Low battery check
            if (spec.IsLowBattery && !_lowBatteryFired)
            {
                _lowBatteryFired = true;
                OnLowBattery?.Invoke(this);
                DLog.Warn("General",$"[{GetName()}] Low battery: {spec.BatteryPercent:F0}%");
            }
        }

        // ====== Info panel update ======
        if (_panel != null)
        {
            if (navigator != null)
            {
                _panel.UpdateSpeed((float)navigator.cruiseSpeed);
                _panel.UpdateStatus(missionState.ToString());

                // Position update
                var anchor = navigator.GetComponent<CesiumGlobeAnchor>();
                if (anchor != null)
                {
                    var llh = anchor.longitudeLatitudeHeight;
                    _panel.UpdatePosition(llh.x, llh.y, llh.z);
                }
            }

            // Battery & cargo update
            if (spec != null)
            {
                _panel.UpdateBattery(spec.BatteryPercent, spec.batteryState.ToString());
                _panel.UpdateCargo(spec.currentLoad, spec.maxCapacity);
            }
        }

        // ====== Click to toggle info panel ======
        if (UIInputBlocker.IsBlocking) return;
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

    private void UpdateMissionState()
    {
        MissionState newState = missionState;

        if (missionState == MissionState.Charging)
        {
            // Stay in charging until explicitly changed
            return;
        }

        if (navigator.HasNoPath())
        {
            newState = MissionState.Idle;
        }
        else if (navigator.IsPaused())
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

    public void SetMissionState(MissionState newState)
    {
        if (newState == missionState) return;

        MissionState oldState = missionState;
        missionState = newState;

        OnMissionStateChanged?.Invoke(this, oldState, newState);

        if (newState == MissionState.Flying)
        {
            _routeCompletedFired = false;
            _lowBatteryFired = false;
        }
    }

    private void CheckRouteCompletion()
    {
        if (_routeCompletedFired) return;

        if (navigator.GetProgress() >= 99.9f)
        {
            _routeCompletedFired = true;
            DLog.Info("DroneInfo", $"{GetName()} Route completed: {currentRouteName}");
            OnRouteCompleted?.Invoke(this);
        }
    }

    public void AssignRoute(string routeName)
    {
        currentRouteName = routeName;
        _routeCompletedFired = false;
        SetMissionState(MissionState.Flying);
    }

    public void ClearMission()
    {
        currentRouteName = "";
        SetMissionState(MissionState.Idle);
    }

    // ================================================================
    //  Snapshot
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
        // New fields
        public float batteryPercent;
        public int currentLoad;
        public int maxCapacity;
        public string batteryState;
    }

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
            snap.isIdle = (missionState == MissionState.Idle);

            var anchor = navigator.GetComponent<CesiumGlobeAnchor>();
            if (anchor != null)
                snap.positionLLH = anchor.longitudeLatitudeHeight;
        }

        if (spec != null)
        {
            snap.batteryPercent = spec.BatteryPercent;
            snap.currentLoad = spec.currentLoad;
            snap.maxCapacity = spec.maxCapacity;
            snap.batteryState = spec.batteryState.ToString();
        }

        return snap;
    }

    // ================================================================
    //  Info Panel Access (for external scripts)
    // ================================================================

    /// <summary>Show or hide the info panel</summary>
    public void ShowInfoPanel(bool show)
    {
        if (_panel != null)
            _panel.ShowPanel(show);
    }

    /// <summary>Is the info panel currently visible?</summary>
    public bool IsInfoPanelVisible => _panel != null && _panel.isVisible;

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

    public bool IsCharging()
    {
        return missionState == MissionState.Charging;
    }
}