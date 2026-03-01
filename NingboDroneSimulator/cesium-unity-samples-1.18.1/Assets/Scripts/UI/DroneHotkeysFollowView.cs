using System.Collections.Generic;
using UnityEngine;

public class DroneHotkeysFollowView : MonoBehaviour
{
    public SwitchView switchView;
    public DroneCommandCenter commandCenter;

    [Header("Hotkeys (不与 SwitchView 冲突)")]
    public KeyCode pauseKey = KeyCode.Space;
    public KeyCode speedUpKey = KeyCode.Equals;     // 主键盘 =
    public KeyCode speedDownKey = KeyCode.Minus;    // 主键盘 -
    public KeyCode speedUpKeypad = KeyCode.KeypadPlus;
    public KeyCode speedDownKeypad = KeyCode.KeypadMinus;

    public double speedStep = 2.0;

    // 每架机单独保存：暂停状态、速度（否则切换无人机会丢状态）
    private readonly HashSet<DroneGeoNavigator> _paused = new();
    private readonly Dictionary<DroneGeoNavigator, double> _speedCache = new();

    void Awake()
    {
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (commandCenter) commandCenter.Refresh();

    }

    void Update()
    {
            // Skip hotkeys when UI input field is focused
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null)
        {
            var inputField = UnityEngine.EventSystems.EventSystem.current
                .currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>();
            if (inputField != null) return;
        }

        if (!switchView || !commandCenter) return;

        Transform camTarget = switchView.CurrentDroneTarget;
        if (!commandCenter.TryGetNavByCamTarget(camTarget, out var nav))
            return;

        // 初始化速度缓存
        if (!_speedCache.ContainsKey(nav))
            _speedCache[nav] = nav.cruiseSpeed;

        // 空格：暂停/继续（作用到“当前相机跟踪的那架机”）
        if (Input.GetKeyDown(pauseKey))
        {
            bool nowPause = !_paused.Contains(nav);
            if (nowPause) _paused.Add(nav); else _paused.Remove(nav);

            nav.SetEmergencyStop(nowPause); // 你原脚本已有，外部停止逻辑就是它【你之前贴的 DroneGeoNavigator 里有】
            Debug.Log($"[HotkeysFollowView] {(nowPause ? "Pause" : "Resume")} {nav.gameObject.name}");
        }

        // 加速/减速：同样只影响当前机
        if (Input.GetKeyDown(speedUpKey) || Input.GetKeyDown(speedUpKeypad))
        {
            _speedCache[nav] += speedStep;
            nav.cruiseSpeed = _speedCache[nav];
            Debug.Log($"[HotkeysFollowView] Speed {nav.gameObject.name} => {_speedCache[nav]:F1} m/s");
        }

        if (Input.GetKeyDown(speedDownKey) || Input.GetKeyDown(speedDownKeypad))
        {
            _speedCache[nav] = System.Math.Max(0.1, _speedCache[nav] - speedStep);
            nav.cruiseSpeed = _speedCache[nav];
            Debug.Log($"[HotkeysFollowView] Speed {nav.gameObject.name} => {_speedCache[nav]:F1} m/s");
        }
    }
}
