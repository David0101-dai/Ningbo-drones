using UnityEngine;

/// <summary>
/// 挂在每架无人机根物体上，用来提供 UI 需要的元信息。
/// </summary>
public class DroneInfo : MonoBehaviour
{
    [Header("显示名字（不填就用 GameObject.name）")]
    public string displayName;

    [Header("UI 颜色")]
    public Color uiColor = Color.cyan;

    [Header("用于 UI 锚点的 Transform（不填就用自身）")]
    public Transform uiAnchor;

    // 可选：如果你希望显示“理论巡航速度”，可以直接引用导航器
    [Header("可选：导航器，用于读巡航速度")]
    public DroneGeoNavigator navigator;

    void Reset()
    {
        uiAnchor = transform;
        navigator = GetComponent<DroneGeoNavigator>();
    }

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
}
