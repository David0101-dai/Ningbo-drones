// Assets/Scripts/CameraClipByView.cs
using UnityEngine;
using Cinemachine;

[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(CinemachineBrain))]
public class CameraClipByView : MonoBehaviour
{
    public CinemachineVirtualCamera topDown; // 拖 "CM vcam - TopDown"
    public float defaultNear = 0.1f;
    public float defaultFar  = 2_000_000f;   // 平时
    public float topDownFar  = 4_000_000f;   // 俯视时更远

    CinemachineBrain _brain; Camera _cam;

    void Awake()
    {
        _brain = GetComponent<CinemachineBrain>();
        _cam   = GetComponent<Camera>();
    }

    void OnEnable()
    {
        _brain.m_CameraActivatedEvent.RemoveListener(OnActivated);
        _brain.m_CameraActivatedEvent.AddListener(OnActivated);
    }

    void OnActivated(ICinemachineCamera fromCam, ICinemachineCamera toCam)
    {
        _cam.nearClipPlane = defaultNear;
        _cam.farClipPlane  = (toCam == topDown) ? topDownFar : defaultFar;
    }
}