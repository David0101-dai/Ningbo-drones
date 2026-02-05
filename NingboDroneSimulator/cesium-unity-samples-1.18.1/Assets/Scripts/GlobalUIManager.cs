using UnityEngine;

public class GlobalUIManager : MonoBehaviour
{
    [SerializeField] private GameObject llmPanel; // Drag LLMPanel here
    [SerializeField] private UIPanelManager panelManager; // Drag LLMPanel's UIPanelManager component

    void Awake()
    {
        if (!llmPanel) llmPanel = FindObjectOfType<Canvas>().gameObject; // Auto-find if not dragged
        if (!panelManager) panelManager = llmPanel.GetComponent<UIPanelManager>();

        llmPanel.SetActive(false); // Ensure hidden at start
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            bool isActive = llmPanel.activeSelf;
            llmPanel.SetActive(!isActive);
            if (!isActive && panelManager) panelManager.ResetToDefaultMode(); // Call your reset
        }

        if (Input.GetKeyDown(KeyCode.Escape) && llmPanel.activeSelf)
        {
            llmPanel.SetActive(false);
        }
    }
}