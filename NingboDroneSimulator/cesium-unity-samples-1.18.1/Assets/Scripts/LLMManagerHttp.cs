using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LLMManagerHttp : MonoBehaviour
{
    void OnDisable() { StopAllCoroutines(); }
    void OnDestroy() { StopAllCoroutines(); }

    [Header("Core")]
    public DroneCommandCenter commandCenter;
    public SwitchView switchView;

    [Header("Fleet (for pause_all/resume_all)")]
    public DroneFleetController fleet;

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
        // pause/resume/set_speed/select_route/pause_all/resume_all
        public string type;

        // "current" or explicit name or null
        public string drone;

        public string route;
        public double speed;
    }

    void Awake()
    {
        if (!commandCenter) commandCenter = FindObjectOfType<DroneCommandCenter>();
        if (!switchView) switchView = FindObjectOfType<SwitchView>();
        if (!fleet) fleet = FindObjectOfType<DroneFleetController>();
    }

    public void SendUserText(string text)
    {
        Debug.Log($"[LLM Debug] SendUserText called with text: '{text}'"); // Debug: 输入调用
        StartCoroutine(CallGateway(text));
    }

    IEnumerator CallGateway(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText) || !commandCenter || !switchView)
        {
            Debug.LogWarning("[LLM Debug] CallGateway aborted: invalid input or missing components");
            yield break;
        }

        string currentDroneName = GetCurrentDroneName();
        string[] routes = new[] { "Waypoints_A", "Waypoints_B", "Waypoints_C", "Waypoints_Runtime" };

        var reqObj = new CommandRequest
        {
            text = userText,
            current_drone = currentDroneName,
            routes = routes
        };

        string json = JsonUtility.ToJson(reqObj);
        byte[] body = Encoding.UTF8.GetBytes(json);

        Debug.Log($"[LLM Debug] Sending request to {gatewayUrl} with JSON: {json}"); // Debug: 请求内容

        using (var req = new UnityWebRequest(gatewayUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 10;

            yield return req.SendWebRequest();

            Debug.Log($"[LLM Debug] Response received with code: {req.responseCode}"); // Debug: 响应码

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogOut($"[LLM] Request failed: {req.error}");
                Debug.LogError($"[LLM Debug] Full error details: {req.error} - Response: {req.downloadHandler.text}"); // Debug: 错误详情
                yield break;
            }

            string respJson = req.downloadHandler.text;
            Debug.Log($"[LLM Debug] Raw response JSON: {respJson}"); // Debug: 原始响应

            LlmResponse resp;
            try
            {
                resp = JsonUtility.FromJson<LlmResponse>(respJson);
                Debug.Log($"[LLM Debug] JSON parsed successfully: say='{resp.say}', commands count={resp.commands?.Length ?? 0}"); // Debug: 解析结果
            }
            catch (Exception e)
            {
                LogOut($"[LLM] JSON parse error: {e.Message}\nraw={respJson}");
                Debug.LogError($"[LLM Debug] Parse exception: {e.StackTrace}"); // Debug: 异常栈
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
        // 依赖你 SwitchView 暴露的当前目标
        var t = switchView.CurrentDroneTarget;
        if (!t) return "";

        var info = t.GetComponentInParent<DroneInfo>();
        return info ? info.gameObject.name : "";
    }

    void Execute(LlmResponse resp)
    {
        if (resp.commands == null || resp.commands.Length == 0)
        {
            Debug.LogWarning("[LLM Debug] No commands in response");
            return;
        }

        string current = GetCurrentDroneName();
        Debug.Log($"[LLM Debug] Executing for current drone: {current}"); // Debug: 当前无人机

        foreach (var c in resp.commands)
        {
            if (c == null || string.IsNullOrEmpty(c.type))
                continue;

            string type = c.type.Trim().ToLowerInvariant();

            Debug.Log($"[LLM Debug] Processing command: type={type}, drone={c.drone ?? "null"}, route={c.route ?? "null"}, speed={c.speed}"); // Debug: 每个命令详情

            // ===== 全体命令（不需要 drone 字段）=====
            if (type == "pause_all")
            {
                if (fleet) fleet.PauseAll(true);
                else LogOut("[LLM] pause_all failed: DroneFleetController not found.");
                continue;
            }

            if (type == "resume_all")
            {
                if (fleet) fleet.PauseAll(false);
                else LogOut("[LLM] resume_all failed: DroneFleetController not found.");
                continue;
            }

            // ===== 单机命令：默认控制 current =====
            string drone = (string.IsNullOrEmpty(c.drone) || c.drone == "current") ? current : c.drone;

            if (string.IsNullOrEmpty(drone))
            {
                LogOut($"[LLM] Skip '{type}': current drone is empty.");
                continue;
            }

            switch (type)
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

                default:
                    LogOut($"[LLM] Unknown command type: {type}");
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