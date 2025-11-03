using UnityEngine;
using CesiumForUnity;

public class ThirdPersonGeoFollow : MonoBehaviour
{
    public CesiumGlobeAnchor targetAnchor; // 拖拽Drone的GlobeAnchor

    private CesiumGlobeAnchor myAnchor;

    void Start()
    {
        myAnchor = GetComponent<CesiumGlobeAnchor>();
        if (myAnchor == null) { Debug.LogError("缺少CesiumGlobeAnchor!"); return; }
    }

    void LateUpdate()
    {
        if (targetAnchor == null) return;

        // 跟随目标地理，偏移5m后方，2m高
        myAnchor.latitude = targetAnchor.latitude;
        myAnchor.longitude = targetAnchor.longitude - 0.00005; // 后移≈5m
        myAnchor.height = targetAnchor.height + 2;

        transform.LookAt(targetAnchor.transform.position);
    }
}