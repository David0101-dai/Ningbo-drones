using UnityEngine;
using UnityEngine.UI;

public class Stage2PauseUI : MonoBehaviour
{
    public DroneFleetController fleet;
    public Button pauseAllButton;
    public Button resumeAllButton;
    public Toggle pauseToggleOptional; // 可选：你想做一个 Toggle 也行

    void Awake()
    {
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();

        if (pauseAllButton)
            pauseAllButton.onClick.AddListener(() => fleet?.PauseAll(true));

        if (resumeAllButton)
            resumeAllButton.onClick.AddListener(() => fleet?.PauseAll(false));

        if (pauseToggleOptional)
            pauseToggleOptional.onValueChanged.AddListener(v => fleet?.PauseAll(v));
    }
}
