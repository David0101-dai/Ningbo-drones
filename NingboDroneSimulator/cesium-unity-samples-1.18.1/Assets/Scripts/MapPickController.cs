// Assets/Scripts/MapPickController.cs
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

    [Header("Pick Height")]
    [Tooltip("选点后在 LLH 高度(z) 基础上抬升多少米，避免贴地/贴墙")]
    public float pickHeightOffsetMeters = 25f;

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
    public void SetPickEnd() { mode = PickMode.PickEnd; }

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

        if (!Input.GetMouseButtonDown(0))
            return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, pickLayer))
            return;

        // 1) hit.point 是你点到的“表面点”（地面/屋顶/墙体）
        Vector3 baseWorld = hit.point;

        // 2) 先转换到 LLH，再把高度(z)抬升
        double3 baseLLH = UnityToLLH(baseWorld);
        double3 liftedLLH = new double3(baseLLH.x, baseLLH.y, baseLLH.z + pickHeightOffsetMeters);

        // 3) 用抬升后的 LLH 再转回 Unity 世界坐标（marker 和逻辑都用它）
        Vector3 liftedWorld = LLHToUnity(liftedLLH);

        if (mode == PickMode.PickStart)
        {
            _startWorld = liftedWorld;
            _startLLH = liftedLLH;
            _hasStart = true;

            if (startMarker)
            {
                startMarker.position = liftedWorld;
                startMarker.gameObject.SetActive(true);
            }

            Debug.Log($"[Pick] Start set (lifted {pickHeightOffsetMeters}m) LLH=({_startLLH.x},{_startLLH.y},{_startLLH.z})");
        }
        else if (mode == PickMode.PickEnd)
        {
            _endWorld = liftedWorld;
            _endLLH = liftedLLH;
            _hasEnd = true;

            if (endMarker)
            {
                endMarker.position = liftedWorld;
                endMarker.gameObject.SetActive(true);
            }

            Debug.Log($"[Pick] End set (lifted {pickHeightOffsetMeters}m) LLH=({_endLLH.x},{_endLLH.y},{_endLLH.z})");
        }
    }

    // ===== 坐标转换：Unity -> LLH =====
    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 u = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(u);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }

    // ===== 坐标转换：LLH -> Unity =====
    Vector3 LLHToUnity(double3 llh)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
    }
}
