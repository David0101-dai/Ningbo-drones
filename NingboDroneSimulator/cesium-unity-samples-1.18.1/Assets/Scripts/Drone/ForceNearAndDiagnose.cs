using UnityEngine;
using Cinemachine;

[DefaultExecutionOrder(10000)]
public class ForceNearAndDiagnose : MonoBehaviour
{
    public CinemachineVirtualCamera vcam;
    [Tooltip("希望的 Transposer 距离(m)")]
    public float targetDistance = 0.9f;

    [Tooltip("用于世界距离统计的测距点(建议拖无人机 Root)")]
    public Transform distanceProbe;

    [Tooltip("是否强制 Body 使用 Transposer")]
    public bool forceTransposer = true;

    [Tooltip("自动禁用可能改距离的扩展")]
    public bool autoDisableExtensions = true;

    [Tooltip("每秒打印一次调试信息(0=不打印)")]
    public float debugPrintInterval = 0.5f;

    CinemachineBrain _brain;
    float _tPrint;

    void OnEnable()
    {
        if (!vcam) { Debug.LogError("[ForceNear] 请拖入 vcam"); enabled = false; return; }
        if (!_brain && Camera.main) _brain = Camera.main.GetComponent<CinemachineBrain>();

        if (autoDisableExtensions)
        {
            var col   = vcam.GetComponent<CinemachineCollider>();    if (col) col.enabled = false;
            var conf3 = vcam.GetComponent<CinemachineConfiner>();    if (conf3) conf3.enabled = false;
            var conf2 = vcam.GetComponent<CinemachineConfiner2D>();  if (conf2) conf2.enabled = false;
            var off   = vcam.GetComponent<CinemachineCameraOffset>();if (off) off.enabled = false;
            var fz    = vcam.GetComponent<CinemachineFollowZoom>();  if (fz) fz.enabled = false;
        }

        EnsureTransposer();
        ForceDistanceImmediate();
        _tPrint = 0f;
    }

    void LateUpdate()
    {
        if (!_brain && Camera.main) _brain = Camera.main.GetComponent<CinemachineBrain>();
        EnsureTransposer();
        ForceDistanceImmediate();

        if (debugPrintInterval > 0f)
        {
            _tPrint += Time.unscaledDeltaTime;
            if (_tPrint >= debugPrintInterval)
            {
                _tPrint = 0f;
                PrintDiagnostics();
            }
        }
    }

    void EnsureTransposer()
    {
        var trans = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (!trans && forceTransposer)
        {
            trans = vcam.AddCinemachineComponent<CinemachineTransposer>();
            var third   = vcam.GetCinemachineComponent<Cinemachine3rdPersonFollow>(); if (third) Destroy(third);
            var framing = vcam.GetCinemachineComponent<CinemachineFramingTransposer>(); if (framing) Destroy(framing);

            trans.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetNoRoll;
            if (vcam.Follow)
                trans.m_FollowOffset = new Vector3(0f, 0.25f, -targetDistance);
        }
    }

    void ForceDistanceImmediate()
    {
        var trans = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (!trans) return;

        Vector3 off = trans.m_FollowOffset;
        float mag = off.magnitude;
        if (mag < 1e-4f) { off = new Vector3(0f, 0.25f, -1f); mag = 1f; }
        float newMag = Mathf.Max(0.01f, targetDistance);
        trans.m_FollowOffset = off.normalized * newMag;
    }

    void PrintDiagnostics()
    {
        var trans = vcam.GetCinemachineComponent<CinemachineTransposer>();
        float offsetMag = trans ? trans.m_FollowOffset.magnitude : -1f;

        Vector3 camPos = Camera.main ? Camera.main.transform.position : vcam.State.FinalPosition;
        Transform follow = vcam.Follow;

        float worldDistToFollow = (follow) ? Vector3.Distance(camPos, follow.position) : -1f;
        float worldDistToProbe  = (distanceProbe) ? Vector3.Distance(camPos, distanceProbe.position) : -1f;

        float fov = vcam.m_Lens.FieldOfView;
        bool blending = _brain && _brain.ActiveBlend != null;

        //Debug.Log($"[ForceNear] offsetMag={offsetMag:F3} | world→Follow={worldDistToFollow:F3} | " +
        //          $"world→Probe={(worldDistToProbe<0?float.NaN:worldDistToProbe):F3} | FOV={fov:F1} | Blending={blending}");
    }
}