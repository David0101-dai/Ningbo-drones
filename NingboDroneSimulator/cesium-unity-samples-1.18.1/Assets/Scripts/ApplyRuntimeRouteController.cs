using UnityEngine;

public class ApplyRuntimeRouteController : MonoBehaviour
{
    public MapPickController picker;
    public RuntimeWaypointsBuilder builder;
    public SwitchView switchView;

    [Header("Execute behavior")]
    public bool warpDroneToStart = true;
    public bool forceStartNow = true;

    [Header("Runtime parent (Waypoints_Runtime)")]
    public Transform runtimeWaypointsParent;

    void Awake()
    {
        if (!picker) picker = FindObjectOfType<MapPickController>();
        if (!builder) builder = FindObjectOfType<RuntimeWaypointsBuilder>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
    }

    public void ApplyToCurrentDrone()
    {
        if (!picker || !builder || !switchView)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] missing refs");
            return;
        }

        if (!picker.HasEnd)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] End not picked");
            return;
        }

        // 起点：如果没选 start，就用当前无人机位置当 start
        var curTarget = switchView.CurrentDroneTarget;
        if (!curTarget)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] no current drone target");
            return;
        }

        var info = curTarget.GetComponentInParent<DroneInfo>();
        if (!info)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] DroneInfo not found");
            return;
        }

        var nav = info.GetComponent<DroneGeoNavigator>();
        if (!nav)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] DroneGeoNavigator not found");
            return;
        }

        var startLLH = picker.HasStart ? picker.StartLLH : nav.anchor.longitudeLatitudeHeight;
        var endLLH = picker.EndLLH;

        if (!builder.BuildRoute(startLLH, endLLH, out var llhPoints))
        {
            DLog.Warn("General","[ApplyRuntimeRoute] BuildRoute failed (blocked)");
            return;
        }

        if (!runtimeWaypointsParent) runtimeWaypointsParent = builder.runtimeWaypointsParent;
        if (!runtimeWaypointsParent)
        {
            DLog.Warn("General","[ApplyRuntimeRoute] runtimeWaypointsParent missing");
            return;
        }

        if (!builder.WriteToRuntimeParent(llhPoints))
        {
            DLog.Warn("General","[ApplyRuntimeRoute] WriteToRuntimeParent failed");
            return;
        }

        // 应用到无人机：切换航点父物体为 Waypoints_Runtime
        bool ok = nav.LoadRoute(runtimeWaypointsParent, warpDroneToStart, forceStartNow);
        DLog.Info("ApplyRuntimeRoute", $"Apply route to {info.name} ok={ok}");
    }
}
