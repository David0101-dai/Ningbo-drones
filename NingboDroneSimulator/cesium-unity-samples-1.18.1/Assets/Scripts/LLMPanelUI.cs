using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LLMPanelUI : MonoBehaviour
{
    public TMP_InputField inputField;   // 你的 InputField-TextMeshPro
    public Button sendButton;
    public TMP_Text outputText;
    public LLMManagerHttp llm;

    void Awake()
    {
        if (!llm) llm = FindObjectOfType<LLMManagerHttp>();

        if (sendButton)
            sendButton.onClick.AddListener(OnSend);

        if (llm && outputText)
            llm.outputText = outputText;
    }

    void OnSend()
    {
        if (!llm || !inputField) return;
        llm.SendUserText(inputField.text);
    }
}
