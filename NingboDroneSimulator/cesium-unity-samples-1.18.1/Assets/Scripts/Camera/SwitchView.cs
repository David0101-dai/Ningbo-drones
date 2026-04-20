// Assets/Scripts/SwitchView.cs
using UnityEngine;
using Cinemachine;

public class SwitchView : MonoBehaviour
{
    [Header("Drag your vcams here")]
    public CinemachineVirtualCamera sideView;
    public CinemachineVirtualCamera rearChase;
    public CinemachineVirtualCamera topDown;

    [Header("Hotkeys - View Mode")]
    public KeyCode sideKey = KeyCode.Alpha1;
    public KeyCode rearKey = KeyCode.Alpha2;
    public KeyCode topKey  = KeyCode.Alpha3;

    [Header("Hotkeys - Drone Target")]
    public KeyCode nextDroneKey = KeyCode.D;
    public KeyCode prevDroneKey = KeyCode.A;

    [Header("Hotkeys - Info Panel")]
    public KeyCode toggleAllPanelsKey = KeyCode.I;  // 新增：批量显示/隐藏所有InfoPanel

    [Header("Priorities")]
    public int activePriority = 20;
    public int inactivePriority = 10;

    [Header("Drone Targets")]
    public Transform[] droneTargets;

    [SerializeField] private int _currentDroneIndex = 0;

    private enum View { Side, Rear, TopDown }
    private View _currentView = View.Side;
    private bool _allPanelsVisible = false;

    // ======== Public APIs ========

    public int CurrentDroneIndex => _currentDroneIndex;
    public int DroneCount => (droneTargets != null) ? droneTargets.Length : 0;

    public Transform CurrentDroneTarget
    {
        get
        {
            if (droneTargets == null || droneTargets.Length == 0) return null;
            _currentDroneIndex = Mathf.Clamp(_currentDroneIndex, 0, droneTargets.Length - 1);
            return droneTargets[_currentDroneIndex];
        }
    }

    public void SetTopDown() { ApplyView(View.TopDown); }
    public void SetSide()    { ApplyView(View.Side); }
    public void SetRear()    { ApplyView(View.Rear); }

    public void SelectDroneByIndex(int index)
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex = Mathf.Clamp(index, 0, droneTargets.Length - 1);

        if (_currentView == View.Side || _currentView == View.Rear)
        {
            ApplyDroneTarget(_currentDroneIndex);
        }
    }

    // ======== Unity lifecycle ========

    void OnEnable()
    {
        ApplyView(View.Side);
        ApplyDroneTarget(_currentDroneIndex);
    }

    void Update()
    {
        // Safety: ensure camera always tracks something
        if (droneTargets != null && droneTargets.Length > 0 &&
            (_currentDroneIndex >= droneTargets.Length || droneTargets[_currentDroneIndex] == null))
        {
            EnsureCameraHasTarget();
        }

        // ★ FIX: During replay, allow view/drone/info-panel hotkeys
        // even when the Replay UI panel is open (UIInputBlocker active).
        bool isReplaying = ReplayManager.Instance != null &&
                        ReplayManager.Instance.IsReplaying;

        if (!isReplaying)
        {
            if (UIInputBlocker.IsBlocking) return;

            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
            {
                var inputField = UnityEngine.EventSystems.EventSystem.current
                    .currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
                if (inputField != null) return;
            }
        }

        // --- View mode ---
        if (Input.GetKeyDown(sideKey)) ApplyView(View.Side);
        if (Input.GetKeyDown(rearKey)) ApplyView(View.Rear);
        if (Input.GetKeyDown(topKey))  ApplyView(View.TopDown);

        // --- Drone switching ---
        if (_currentView != View.TopDown && droneTargets != null && droneTargets.Length > 0)
        {
            if (Input.GetKeyDown(nextDroneKey))
                SelectNextDrone();
            else if (Input.GetKeyDown(prevDroneKey))
                SelectPrevDrone();
        }

        // --- Toggle all info panels ---
        if (Input.GetKeyDown(toggleAllPanelsKey))
        {
            ToggleAllInfoPanels();
        }
    }

    // ======== Info Panel Batch Control ========

    /// <summary>
    /// Toggle all drone info panels on/off
    /// </summary>
    public void ToggleAllInfoPanels()
    {
        _allPanelsVisible = !_allPanelsVisible;

#if UNITY_2023_1_OR_NEWER
        var allInfos = FindObjectsByType<DroneInfo>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var allInfos = FindObjectsOfType<DroneInfo>();
#endif

        foreach (var info in allInfos)
        {
            if (info != null)
                info.ShowInfoPanel(_allPanelsVisible);
        }

        DLog.Info("View", $" All info panels: {(_allPanelsVisible ? "SHOWN" : "HIDDEN")} ({allInfos.Length} drones)");
    }

    // ======== Internal ========

    void ApplyView(View v)
    {
        _currentView = v;

        SetPrio(sideView,  v == View.Side);
        SetPrio(rearChase, v == View.Rear);
        SetPrio(topDown,   v == View.TopDown);

        if (v == View.Side || v == View.Rear)
        {
            ApplyDroneTarget(_currentDroneIndex);
        }
    }

    void SetPrio(CinemachineVirtualCamera vcam, bool active)
    {
        if (!vcam) return;
        vcam.Priority = active ? activePriority : inactivePriority;
    }

    void SelectNextDrone()
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex++;
        if (_currentDroneIndex >= droneTargets.Length)
            _currentDroneIndex = 0;

        ApplyDroneTarget(_currentDroneIndex);
    }

    void SelectPrevDrone()
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        _currentDroneIndex--;
        if (_currentDroneIndex < 0)
            _currentDroneIndex = droneTargets.Length - 1;

        ApplyDroneTarget(_currentDroneIndex);
    }

    void ApplyDroneTarget(int index)
    {
        if (droneTargets == null || droneTargets.Length == 0) return;

        // Clean null entries first
        CleanNullTargets();

        if (droneTargets.Length == 0) return;

        index = Mathf.Clamp(index, 0, droneTargets.Length - 1);
        _currentDroneIndex = index;

        Transform target = droneTargets[index];
        if (target == null) return;

        if (sideView)
        {
            sideView.Follow = target;
            sideView.LookAt = target;
        }

        if (rearChase)
        {
            rearChase.Follow = target;
            rearChase.LookAt = target;
        }
    }

    /// <summary>
    /// Remove null entries from droneTargets array.
    /// Called automatically when drones are destroyed.
    /// </summary>
    public void CleanNullTargets()
    {
        if (droneTargets == null) return;

        var clean = new System.Collections.Generic.List<Transform>();
        foreach (var t in droneTargets)
        {
            if (t != null)
                clean.Add(t);
        }

        if (clean.Count != droneTargets.Length)
        {
            droneTargets = clean.ToArray();
            _currentDroneIndex = Mathf.Clamp(_currentDroneIndex, 0,
                Mathf.Max(0, droneTargets.Length - 1));
        }
    }

    /// <summary>
    /// Ensure cameras always have a valid target.
    /// Call after drones are spawned or removed.
    /// </summary>
    public void EnsureCameraHasTarget()
    {
        CleanNullTargets();

        if (droneTargets == null || droneTargets.Length == 0)
        {
            // No drones — try to find any DroneInfo in scene
    #if UNITY_2023_1_OR_NEWER
            var anyDrone = FindObjectsByType<DroneInfo>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    #else
            var anyDrone = FindObjectsOfType<DroneInfo>();
    #endif
            if (anyDrone.Length > 0)
            {
                var camTarget = anyDrone[0].transform.Find("CamTarget");
                if (camTarget != null)
                {
                    droneTargets = new Transform[] { camTarget };
                    _currentDroneIndex = 0;
                    DLog.Info("View", $" Auto-assigned camera to {anyDrone[0].GetName()}");
                }
            }
            else
            {
                // Absolutely no drones — detach cameras to prevent errors
                if (sideView) { sideView.Follow = null; sideView.LookAt = null; }
                if (rearChase) { rearChase.Follow = null; rearChase.LookAt = null; }
                return;
            }
        }

        if (droneTargets.Length > 0)
        {
            _currentDroneIndex = Mathf.Clamp(_currentDroneIndex, 0, droneTargets.Length - 1);
            ApplyDroneTarget(_currentDroneIndex);
        }
    }
}