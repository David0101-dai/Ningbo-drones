using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LLMManagerHttp : MonoBehaviour
{
    public DroneCommandCenter commandCenter;
    public SwitchView switchView;

    [Header("Gateway URL")]
    public string gatewayUrl = "http://127.0.0.1:8000/command";

    [Header("Debug UI output (optional)")]
    public TMPro.TMP_Text outputText;

    [Serializable]
    class CommandRequest
    {
        public string text;
        public string current_drone;
        public string[] routes;
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
        public string type;   // pause/resume/set_speed/select_route
        public string drone;  // "current" or explicit name or null
        public string route;
        public double speed;
    }

    void Awake()
    {
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
    }

    public void SendUserText(string text)
    {
        StartCoroutine(CallGateway(text));
    }

    IEnumerator CallGateway(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText) || !commandCenter || !switchView)
            yield break;

        // 当前相机跟踪的无人机（通过 CamTarget 找 DroneInfo -> navigator）
        string currentDroneName = GetCurrentDroneName();

        // 可用路线（你也可以从场景 WaypointsRoot 动态扫描）
        string[] routes = new[] { "Waypoints_A", "Waypoints_B", "Waypoints_C", "Waypoints_Runtime" };

        var reqObj = new CommandRequest
        {
            text = userText,
            current_drone = currentDroneName,
            routes = routes
        };

        string json = JsonUtility.ToJson(reqObj);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(gatewayUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogOut($"[LLM] Request failed: {req.error}");
                yield break;
            }

            string respJson = req.downloadHandler.text;
            LlmResponse resp;
            try
            {
                resp = JsonUtility.FromJson<LlmResponse>(respJson);
            }
            catch (Exception e)
            {
                LogOut($"[LLM] JSON parse error: {e.Message}\nraw={respJson}");
                yield break;
            }

            if (resp != null)
            {
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
        if (resp.commands == null) return;

        string current = GetCurrentDroneName();

        foreach (var c in resp.commands)
        {
            if (c == null || string.IsNullOrEmpty(c.type)) continue;

            // 默认控制 current
            string drone = string.IsNullOrEmpty(c.drone) || c.drone == "current" ? current : c.drone;

            switch (c.type)
            {
                case "pause":
                    commandCenter.Pause(drone, true);
                    break;
                case "resume":
                    commandCenter.Pause(drone, false);
                    break;
                case "set_speed":
                    commandCenter.SetSpeed(drone, c.speed);
                    break;
                case "select_route":
                    commandCenter.SelectRoute(drone, c.route);
                    break;
            }
        }
    }

    void LogOut(string msg)
    {
        Debug.Log(msg);
        if (outputText) outputText.text = msg;
    }
}
