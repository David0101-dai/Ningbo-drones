using System.Collections.Generic;
using UnityEngine;

public class DroneUIManager : MonoBehaviour
{
    [Header("UI 容器 Canvas（Screen Space - Overlay）")]
    public Canvas uiCanvas;

    [Header("标记预制体（DroneMarkerUI）")]
    public DroneMarkerUI markerPrefab;

    [Header("是否自动查找场景中所有 DroneInfo")]
    public bool autoFindDrones = true;
    

    private readonly List<DroneMarkerUI> _markers = new List<DroneMarkerUI>();

    void Start()
    {
        if (!uiCanvas)
        {
            uiCanvas = FindObjectOfType<Canvas>();
            if (!uiCanvas)
            {
                Debug.LogError("[DroneUIManager] 找不到 Canvas，请在 Inspector 上指定 uiCanvas。");
                enabled = false;
                return;
            }
        }

        if (!markerPrefab)
        {
            Debug.LogError("[DroneUIManager] 未指定 markerPrefab。");
            enabled = false;
            return;
        }

        if (autoFindDrones)
        {
            CreateMarkersForAllDrones();
        }
    }

    public void CreateMarkersForAllDrones()
    {
        DroneInfo[] drones = FindObjectsOfType<DroneInfo>();
        foreach (var d in drones)
        {
            CreateMarkerForDrone(d);
        }
    }

    public void CreateMarkerForDrone(DroneInfo info)
    {
        if (info == null) return;

        DroneMarkerUI marker = Instantiate(markerPrefab, uiCanvas.transform);
        marker.name = $"UI_{info.GetName()}";
        marker.Init(info, uiCanvas);
        _markers.Add(marker);
    }
}
