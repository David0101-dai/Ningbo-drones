using UnityEngine;

public class DroneFleetController : MonoBehaviour
{
    [Header("自动收集所有 DroneGeoNavigator")]
    public bool autoRefreshOnEnable = true;

    private DroneGeoNavigator[] _navs;

    void OnEnable()
    {
        if (autoRefreshOnEnable) Refresh();
    }

    [ContextMenu("Refresh")]
    public void Refresh()
    {
#if UNITY_2023_1_OR_NEWER
        _navs = FindObjectsByType<DroneGeoNavigator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        _navs = FindObjectsOfType<DroneGeoNavigator>(true);
#endif
        Debug.Log($"[Fleet] Refresh: found {_navs.Length} drones");
    }

    public void PauseAll(bool pause)
    {
        if (_navs == null || _navs.Length == 0) Refresh();

        foreach (var nav in _navs)
        {
            if (!nav) continue;
            nav.SetStop(DroneGeoNavigator.StopReason.External, pause);
        }

        Debug.Log($"[Fleet] PauseAll={pause}");
    }

    public void ResumeAll()
    {
        PauseAll(false);
    }

    public bool AnyExternallyPaused()
    {
        if (_navs == null || _navs.Length == 0) Refresh();

        foreach (var nav in _navs)
        {
            if (!nav) continue;
            if (nav.IsPaused()) return true; // 你已把 IsPaused() 改为 External 检查
        }
        return false;
    }

    public void TogglePauseAll()
    {
        bool any = AnyExternallyPaused();
        PauseAll(!any);
    }
}
