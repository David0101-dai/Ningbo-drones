// Assets/Scripts/GlobalUIManager.cs
using UnityEngine;

public class GlobalUIManager : MonoBehaviour
{
    [SerializeField] private GameObject llmPanel;
    [SerializeField] private UIPanelManager panelManager;
    [SerializeField] private PanelResizeController resizeController;

    void Awake()
    {
        // ★ FIX: 精确查找 LLMPanel，而不是随机找第一个 Canvas
        if (!llmPanel)
        {
            var upm = FindObjectOfType<UIPanelManager>(true); // true = include inactive
            if (upm != null)
                llmPanel = upm.gameObject;
        }

        if (!panelManager && llmPanel)
            panelManager = llmPanel.GetComponent<UIPanelManager>();
        if (!resizeController && llmPanel)
            resizeController = llmPanel.GetComponent<PanelResizeController>();

        if (llmPanel)
            llmPanel.SetActive(false);
    }

    void Update()
    {
        if (llmPanel == null) return;

        // Tab: toggle panel open/close
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            bool isActive = llmPanel.activeSelf;
            llmPanel.SetActive(!isActive);

            if (!isActive)
            {
                if (panelManager) panelManager.ResetToDefaultMode();
                if (resizeController) resizeController.OnPanelOpened();
            }
        }

        // Escape: close panel
        if (Input.GetKeyDown(KeyCode.Escape) && llmPanel.activeSelf)
        {
            llmPanel.SetActive(false);
        }

        // M key: toggle full/mini mode (while panel is open)
        if (Input.GetKeyDown(KeyCode.M) && llmPanel.activeSelf)
        {
            if (resizeController) resizeController.ToggleMode();
        }

        // P key: toggle path lines
        if (Input.GetKeyDown(KeyCode.P))
        {
            DroneGeoNavigator.ToggleAllPathLines();
        }
    }
}