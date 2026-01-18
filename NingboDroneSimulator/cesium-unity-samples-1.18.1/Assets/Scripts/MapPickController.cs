using UnityEngine;
using UnityEngine.EventSystems;
using Unity.Mathematics;
using CesiumForUnity;

public class MapPickController : MonoBehaviour
{
    public CesiumGeoreference georeference;
    public Camera cam;

    [Header("Pick layer")]
    public LayerMask pickLayer = ~0;
    public float maxRayDistance = 200000f;

    public enum PickMode { None, PickStart, PickEnd }
    public PickMode mode = PickMode.None;

    [Header("Optional markers (world objects)")]
    public Transform startMarker;
    public Transform endMarker;

    private bool _enabled = false;

    private bool _hasStart = false;
    private bool _hasEnd = false;
    private Vector3 _startWorld;
    private Vector3 _endWorld;
    private double3 _startLLH;
    private double3 _endLLH;

    public bool HasStart => _hasStart;
    public bool HasEnd => _hasEnd;
    public double3 StartLLH => _startLLH;
    public double3 EndLLH => _endLLH;
    public Vector3 StartWorld => _startWorld;
    public Vector3 EndWorld => _endWorld;

    void Awake()
    {
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
        if (!cam) cam = Camera.main;
    }

    public void EnablePicking(bool on)
    {
        _enabled = on;
        mode = on ? PickMode.PickEnd : PickMode.None; // 默认先选终点（更顺手）
    }

    public void SetPickStart() { mode = PickMode.PickStart; }
    public void SetPickEnd()   { mode = PickMode.PickEnd; }

    public void Clear()
    {
        _hasStart = _hasEnd = false;
        if (startMarker) startMarker.gameObject.SetActive(false);
        if (endMarker) endMarker.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_enabled || mode == PickMode.None || !cam || !georeference) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, pickLayer))
            {
                if (mode == PickMode.PickStart)
                {
                    _startWorld = hit.point;
                    _startLLH = UnityToLLH(hit.point);
                    _hasStart = true;
                    if (startMarker)
                    {
                        startMarker.position = hit.point;
                        startMarker.gameObject.SetActive(true);
                    }
                    Debug.Log($"[Pick] Start set LLH=({_startLLH.x},{_startLLH.y},{_startLLH.z})");
                }
                else if (mode == PickMode.PickEnd)
                {
                    _endWorld = hit.point;
                    _endLLH = UnityToLLH(hit.point);
                    _hasEnd = true;
                    if (endMarker)
                    {
                        endMarker.position = hit.point;
                        endMarker.gameObject.SetActive(true);
                    }
                    Debug.Log($"[Pick] End set LLH=({_endLLH.x},{_endLLH.y},{_endLLH.z})");
                }
            }
        }
    }

    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 u = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(u);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }
}
