// Assets/Scripts/LLMManagerHttp.cs
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Mathematics;

public class LLMManagerHttp : MonoBehaviour
{
    void OnDisable() { StopAllCoroutines(); }
    void OnDestroy() { StopAllCoroutines(); }

    [Header("Core")]
    public DroneCommandCenter commandCenter;
    public SwitchView switchView;

    [Header("Gateway URL")]
    public string gatewayUrl = "<http://127.0.0.1:8000/command>";

    [Header("Debug UI output (optional)")]
    public TMPro.TMP_Text outputText;

    [Header("Scene State")]
    public SceneStateProvider sceneStateProvider;

    private static readonly string NL = System.Environment.NewLine;

    [Serializable]
    class CommandRequest
    {
        public string text;
        public string current_drone;
        public string[] routes;
        public string scene_state;
    }

    [Serializable]
    public class LlmResponse
    {
        public string say;
        public LlmCommand[] commands;
    }

    [Serializable]
    public class LlmCommand
    {
        public string type;
        public string drone;
        public string route;
        public double speed;
        public double longitude;
        public double latitude;
        public double height;
    }

    void Awake()
    {
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!sceneStateProvider) sceneStateProvider = FindObjectOfType<SceneStateProvider>();
    }

    public void SendUserText(string text)
    {
        DLog.Verbose("LLM", "SendUserText called with text: '" + text + "'");
        StartCoroutine(CallGateway(text));
    }

    IEnumerator CallGateway(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText) || !commandCenter || !switchView)
        {
            DLog.Warn("LLM", "CallGateway aborted: invalid input or missing components");
            yield break;
        }

        string currentDroneName = GetCurrentDroneName();
        string[] routes = commandCenter != null
            ? commandCenter.GetAvailableRoutes().ToArray()
            : new string[0];
        string sceneState = sceneStateProvider != null
            ? sceneStateProvider.GetStateJson()
            : "";

        var reqObj = new CommandRequest
        {
            text = userText,
            current_drone = currentDroneName,
            routes = routes,
            scene_state = sceneState
        };

        string json = JsonUtility.ToJson(reqObj);
        byte[] body = Encoding.UTF8.GetBytes(json);

        DLog.Verbose("LLM", "Sending request to " + gatewayUrl);

        using (var req = new UnityWebRequest(gatewayUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;

            yield return req.SendWebRequest();

            DLog.Verbose("LLM", "Response code: " + req.responseCode);

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogOut("[LLM] Request failed: " + req.error);
                DLog.Error("LLM", "Full error: " + req.error +
                           " - Response: " + req.downloadHandler.text);
                yield break;
            }

            string respJson = req.downloadHandler.text;
            DLog.Verbose("LLM", "Raw response length: " + respJson.Length);

            LlmResponse resp;
            try
            {
                resp = JsonUtility.FromJson<LlmResponse>(respJson);
                DLog.Verbose("LLM", "Parsed: say='" + resp.say +
                             "', cmds=" + (resp.commands?.Length ?? 0));
            }
            catch (Exception e)
            {
                LogOut("[LLM] JSON parse error: " + e.Message);
                DLog.Error("LLM", "Parse exception: " + e.StackTrace);
                yield break;
            }

            if (resp != null)
            {
                if (!string.IsNullOrEmpty(resp.say))
                    LogOut(resp.say);

                Execute(resp);
            }
        }
    }

    string GetCurrentDroneName()
    {
        var t = switchView.CurrentDroneTarget;
        if (!t) return "";

        var info = t.GetComponentInParent<DroneInfo>();
        return info ? info.gameObject.name : "";
    }

    void Execute(LlmResponse resp)
    {
        if (resp.commands == null || resp.commands.Length == 0)
        {
            DLog.Verbose("LLM", "No commands in response");
            return;
        }

        string currentDrone = GetCurrentDroneName();

        // Convert LlmCommand[] to DroneCommandCenter.DroneCommand[]
        var cmds = new DroneCommandCenter.DroneCommand[resp.commands.Length];
        for (int i = 0; i < resp.commands.Length; i++)
        {
            var c = resp.commands[i];
            cmds[i] = new DroneCommandCenter.DroneCommand
            {
                type = c.type,
                drone = c.drone,
                route = c.route,
                speed = c.speed
            };
        }

        // Execute all commands through CommandCenter
        string results = commandCenter.ExecuteCommands(cmds, currentDrone);
        DLog.Info("LLM", "Execution results: " + results);

        // Show results in output (append to say text)
        if (!string.IsNullOrEmpty(results) && outputText)
        {
            string display = resp.say;
            if (!string.IsNullOrEmpty(display))
                display += NL + "---" + NL;
            display += results;
            outputText.text = display;
        }
    }

    void LogOut(string msg)
    {
        DLog.Info("LLM", msg);
        if (outputText) outputText.text = msg;
    }
}