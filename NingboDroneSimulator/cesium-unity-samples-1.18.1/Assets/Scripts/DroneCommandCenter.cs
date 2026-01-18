using System.Collections.Generic;
using UnityEngine;

public class DroneCommandCenter : MonoBehaviour
{
    [Header("拖拽场景里的 Waypoints 根节点到这里")]
    public Transform waypointsRoot;

    private readonly Dictionary<string, DroneGeoNavigator> _navByDroneName = new();

    void Awake()
    {
        RefreshRegistry();
    }

    public void RefreshRegistry()
    {
        _navByDroneName.Clear();

        // 你每架无人机上都有 DroneInfo（你说的：DroneInfo 在 UAV_MCAircraft_A 上）
        // DroneInfo 里会拿到 navigator/avoidance 等信息
        foreach (var info in FindObjectsOfType<DroneInfo>())
        {
            if (info != null && info.navigator != null)
                _navByDroneName[info.gameObject.name] = info.navigator;
        }

        Debug.Log($"[CommandCenter] Registry drones={_navByDroneName.Count}");
    }

    public bool TryGetNav(string droneName, out DroneGeoNavigator nav)
        => _navByDroneName.TryGetValue(droneName, out nav);

    public bool SetSpeed(string droneName, double speed)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        nav.SetCruiseSpeed(speed);
        return true;
    }

    public bool Pause(string droneName, bool pause)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        nav.PauseFlight(pause);
        return true;
    }

    public bool SelectRoute(string droneName, string routeName, bool warpToStart = false)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        if (!waypointsRoot) return false;

        Transform route = waypointsRoot.Find(routeName);
        if (!route) return false;

        return nav.LoadRoute(route, warpToStart: warpToStart, startNow: true);
    }

    public bool ReloadRoute(string droneName)
    {
        if (!TryGetNav(droneName, out var nav)) return false;
        return nav.ReloadFromWaypointsParent(warpToStart: false);
    }

    public bool TryGetNavByCamTarget(Transform camTarget, out DroneGeoNavigator nav)
    {
        nav = null;
        if (!camTarget) return false;

        var info = camTarget.GetComponentInParent<DroneInfo>();
        if (!info || !info.navigator) return false;   // DroneInfo 里就是用 navigator 供 UI 读速度:contentReference[oaicite:4]{index=4}

        nav = info.navigator;
        return true;
    }

}
