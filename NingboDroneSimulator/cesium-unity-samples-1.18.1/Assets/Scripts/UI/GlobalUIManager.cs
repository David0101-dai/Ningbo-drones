// Assets/Scripts/GlobalUIManager.cs
using UnityEngine;

public class GlobalUIManager : MonoBehaviour
{
    [SerializeField] private GameObject llmPanel;
    [SerializeField] private UIPanelManager panelManager;
    [SerializeField] private PanelResizeController resizeController;

    void Awake()
    {
        if (!llmPanel) llmPanel = FindObjectOfType<Canvas>()?.gameObject;
        if (!panelManager) panelManager = llmPanel?.GetComponent<UIPanelManager>();
        if (!resizeController) resizeController = llmPanel?.GetComponent<PanelResizeController>();

        if (llmPanel) llmPanel.SetActive(false);
    }

    void Update()
    {
        // Tab: toggle panel open/close
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            bool isActive = llmPanel.activeSelf;
            llmPanel.SetActive(!isActive);

            if (!isActive)
            {
                // Opening panel
                if (panelManager) panelManager.ResetToDefaultMode();
                if (resizeController) resizeController.OnPanelOpened();
            }
        }

        // Escape: close panel
        if (Input.GetKeyDown(KeyCode.Escape) && llmPanel.activeSelf)
        {
            llmPanel.SetActive(false);
        }

        // M key: quick toggle full/mini mode (while panel is open)
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