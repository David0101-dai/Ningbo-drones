using UnityEngine;
using TMPro;

public class DroneInfoPanel : MonoBehaviour
{
    [Header("UI Components")]
    public TMP_Text nameText;
    public TMP_Text speedText;
    public TMP_Text statusText;

    [Header("Follow Settings")]
    public Transform targetDrone;
    public Vector3 offset = new Vector3(1.5f, 2.8f, 0.5f);

    [Header("Smoothing")]
    public float smoothSpeed = 8f;
    public float rotationSmooth = 10f;

    private Canvas canvas;
    public bool isVisible = false;
    private Camera _cachedCamera;

    void Awake()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("DroneInfoPanel must be on a Canvas!");
            return;
        }
        canvas.enabled = false;
        _cachedCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (targetDrone == null || !isVisible) return;
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        if (_cachedCamera == null) return;

        // Smooth follow position
        Vector3 targetPos = targetDrone.position + offset;
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothSpeed);

        // Distance-adaptive scale
        float distance = Vector3.Distance(transform.position, _cachedCamera.transform.position);
        float targetScale = Mathf.Lerp(0.008f, 0.55f, Mathf.InverseLerp(5f, 50f, distance));
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            new Vector3(targetScale, targetScale, targetScale),
            Time.deltaTime * 5f
        );

        // Top-down vs normal view
        bool isTopView = Vector3.Dot(_cachedCamera.transform.forward, Vector3.down) > 0.9f;

        if (isTopView)
        {
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            Vector3 direction = transform.position - _cachedCamera.transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmooth);
            }
        }
    }

    public void TogglePanel()
    {
        isVisible = !isVisible;
        canvas.enabled = isVisible;
    }

    public void SetName(string droneName)
    {
        if (nameText) nameText.text = droneName;
    }

    public void UpdateSpeed(float speedMps)
    {
        if (speedText)
        {
            float kmh = speedMps * 3.6f;
            speedText.text = $"{kmh:F1} km/h";
        }
    }

    public void UpdateStatus(string state)
    {
        if (statusText) statusText.text = state;
    }
}