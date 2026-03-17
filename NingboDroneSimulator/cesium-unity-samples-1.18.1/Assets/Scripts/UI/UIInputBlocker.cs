// Assets/Scripts/UI/UIInputBlocker.cs
using UnityEngine;

/// <summary>
/// Centralized check: is any UI panel open that should block game input?
/// Other scripts query UIInputBlocker.IsBlocking to skip hotkeys/clicks.
/// </summary>
public class UIInputBlocker : MonoBehaviour
{
    public static UIInputBlocker Instance;

    [Header("Panels that block game input when active")]
    public GameObject[] blockingPanels;

    /// <summary>
    /// True when any blocking panel is active → game input should be suppressed
    /// </summary>
    public static bool IsBlocking
    {
        get
        {
            if (Instance == null) return false;
            if (Instance.blockingPanels == null) return false;

            foreach (var panel in Instance.blockingPanels)
            {
                if (panel != null && panel.activeInHierarchy)
                    return true;
            }
            return false;
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
}