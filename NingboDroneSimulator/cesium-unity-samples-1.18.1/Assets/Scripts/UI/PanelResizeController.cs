// Assets/Scripts/UI/PanelResizeController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Toggles LLMPanel between fullscreen and mini mode.
///
/// Uses localScale to shrink everything uniformly,
/// then uses anchoredPosition to slide the panel to the right side.
///
/// Attach to LLMPanel.
/// </summary>
public class PanelResizeController : MonoBehaviour
{
    public enum PanelMode { Full, Mini }

    [Header("Current Mode")]
    public PanelMode currentMode = PanelMode.Full;

    [Header("Toggle Button")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text toggleButtonText;

    [Header("=== Mini Mode Settings ===")]
    [Tooltip("Scale factor (0.4 = 40% of original size)")]
    [Range(0.2f, 0.8f)]
    public float miniScale = 0.4f;

    [Tooltip("How far right to push (pixels). Positive = right. Try 400~800.")]
    public float miniOffsetX = 600f;

    [Tooltip("Vertical offset (pixels). Positive = up, Negative = down.")]
    public float miniOffsetY = 0f;

    [Header("=== Animation ===")]
    [Tooltip("Higher = faster transition")]
    public float transitionSpeed = 8f;

    [Header("=== Elements to HIDE in Mini Mode ===")]
    public List<GameObject> hideInMiniMode = new List<GameObject>();

    [Header("=== Background ===")]
    [SerializeField] private Image backgroundImage;
    [Range(0f, 1f)]
    public float miniBackgroundAlpha = 0.93f;

    // ====== Saved full-mode state ======
    private Vector2 _fullAnchorMin;
    private Vector2 _fullAnchorMax;
    private Vector2 _fullPivot;
    private Vector2 _fullOffsetMin;
    private Vector2 _fullOffsetMax;
    private Vector2 _fullAnchoredPos;
    private Vector3 _fullScale;
    private float _fullBgAlpha;
    private bool _fullStateSaved = false;

    // ====== Animation ======
    private Vector3 _targetScale;
    private Vector2 _targetAnchoredPos;
    private float _targetBgAlpha;
    private bool _isAnimating;

    private RectTransform _rect;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        SaveFullState();

        // Find toggle button
        if (toggleButton == null)
            toggleButton = FindChildButton("ToggleSizeButton");

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(ToggleMode);
            if (toggleButtonText == null)
                toggleButtonText = toggleButton.GetComponentInChildren<TMP_Text>();
        }

        // Auto-detect hide list
        if (hideInMiniMode.Count == 0)
            AutoDetectHideElements();

        // Start in full mode
        ApplyInstant(currentMode);
        UpdateButtonLabel();
    }

    /// <summary>
    /// Save the current RectTransform as the "full mode" reference.
    /// Called once in Awake.
    /// </summary>
    private void SaveFullState()
    {
        _fullAnchorMin = _rect.anchorMin;
        _fullAnchorMax = _rect.anchorMax;
        _fullPivot = _rect.pivot;
        _fullOffsetMin = _rect.offsetMin;
        _fullOffsetMax = _rect.offsetMax;
        _fullAnchoredPos = _rect.anchoredPosition;
        _fullScale = _rect.localScale;

        if (backgroundImage != null)
            _fullBgAlpha = backgroundImage.color.a;
        else
            _fullBgAlpha = 1f;

        _fullStateSaved = true;

        DLog.Info("UI", $" Full state saved: " +
                  $"anchorMin={_fullAnchorMin}, anchorMax={_fullAnchorMax}, " +
                  $"pivot={_fullPivot}, pos={_fullAnchoredPos}, scale={_fullScale}");
    }

    void Update()
    {
        if (!_isAnimating) return;

        float dt = Time.unscaledDeltaTime * transitionSpeed;

        // Animate scale
        _rect.localScale = Vector3.Lerp(_rect.localScale, _targetScale, dt);

        // Animate position
        _rect.anchoredPosition = Vector2.Lerp(_rect.anchoredPosition, _targetAnchoredPos, dt);

        // Animate background alpha
        if (backgroundImage != null)
        {
            Color c = backgroundImage.color;
            c.a = Mathf.Lerp(c.a, _targetBgAlpha, dt);
            backgroundImage.color = c;
        }

        // Check convergence
        bool scaleOk = Vector3.Distance(_rect.localScale, _targetScale) < 0.003f;
        bool posOk = Vector2.Distance(_rect.anchoredPosition, _targetAnchoredPos) < 0.5f;

        if (scaleOk && posOk)
        {
            _rect.localScale = _targetScale;
            _rect.anchoredPosition = _targetAnchoredPos;
            if (backgroundImage != null)
            {
                Color c = backgroundImage.color;
                c.a = _targetBgAlpha;
                backgroundImage.color = c;
            }
            _isAnimating = false;
        }
    }

    // ================================================================
    //  Public API
    // ================================================================

    public void ToggleMode()
    {
        SetMode(currentMode == PanelMode.Full ? PanelMode.Mini : PanelMode.Full);
    }

    public void SetMode(PanelMode mode)
    {
        currentMode = mode;

        if (mode == PanelMode.Full)
        {
            // ★ Restore EVERYTHING about the full-mode layout
            _rect.anchorMin = _fullAnchorMin;
            _rect.anchorMax = _fullAnchorMax;
            _rect.pivot = _fullPivot;
            _rect.offsetMin = _fullOffsetMin;
            _rect.offsetMax = _fullOffsetMax;

            _targetScale = _fullScale;
            _targetAnchoredPos = _fullAnchoredPos;
            _targetBgAlpha = _fullBgAlpha;
        }
        else // Mini
        {
            // Keep same anchors/offsets as full mode (stretch 0,0 → 1,1)
            // Just scale down and slide right
            _targetScale = new Vector3(miniScale, miniScale, 1f);
            _targetAnchoredPos = new Vector2(miniOffsetX, miniOffsetY);
            _targetBgAlpha = miniBackgroundAlpha;
        }

        ApplyVisibility(mode);
        UpdateButtonLabel();
        _isAnimating = true;
    }

    public void OnPanelOpened()
    {
        ApplyInstant(currentMode);
        ApplyVisibility(currentMode);
        UpdateButtonLabel();
    }

    // ================================================================
    //  Instant apply
    // ================================================================

    private void ApplyInstant(PanelMode mode)
    {
        if (mode == PanelMode.Full)
        {
            _rect.anchorMin = _fullAnchorMin;
            _rect.anchorMax = _fullAnchorMax;
            _rect.pivot = _fullPivot;
            _rect.offsetMin = _fullOffsetMin;
            _rect.offsetMax = _fullOffsetMax;
            _rect.anchoredPosition = _fullAnchoredPos;
            _rect.localScale = _fullScale;

            if (backgroundImage != null)
            {
                Color c = backgroundImage.color;
                c.a = _fullBgAlpha;
                backgroundImage.color = c;
            }
        }
        else // Mini
        {
            _rect.localScale = new Vector3(miniScale, miniScale, 1f);
            _rect.anchoredPosition = new Vector2(miniOffsetX, miniOffsetY);

            if (backgroundImage != null)
            {
                Color c = backgroundImage.color;
                c.a = miniBackgroundAlpha;
                backgroundImage.color = c;
            }
        }

        _isAnimating = false;
    }

    // ================================================================
    //  Visibility
    // ================================================================

    private void ApplyVisibility(PanelMode mode)
    {
        bool hide = (mode == PanelMode.Mini);
        foreach (var obj in hideInMiniMode)
        {
            if (obj != null)
                obj.SetActive(!hide);
        }
    }

    private void UpdateButtonLabel()
    {
        if (toggleButtonText == null) return;
        toggleButtonText.text = currentMode == PanelMode.Full ? "◳" : "◲";
    }

    // ================================================================
    //  Auto-detect
    // ================================================================

    private void AutoDetectHideElements()
    {
        string[] hideNames =
        {
            // "PauseAllButton",
            // "ResumeAllButton",
            // "SaveLogButton",
            // "EnterReplayButton",
            // "EnterPlanningButton",
            // "RoutingTabButton"
        };

        var allButtons = GetComponentsInChildren<Button>(true);
        foreach (var btn in allButtons)
        {
            foreach (var name in hideNames)
            {
                if (btn.gameObject.name == name)
                {
                    hideInMiniMode.Add(btn.gameObject);
                    break;
                }
            }
        }
    }

    private Button FindChildButton(string buttonName)
    {
        var all = GetComponentsInChildren<Button>(true);
        foreach (var b in all)
            if (b.gameObject.name == buttonName)
                return b;
        return null;
    }
}