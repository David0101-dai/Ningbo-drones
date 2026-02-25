using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Unity.Mathematics;

public class Logger : MonoBehaviour
{
    public static Logger Instance;

    private FlightLogData currentLog;
    private float startTime;
    private bool isRecording = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        StartNewSession();
    }

    public void StartNewSession()
    {
        currentLog = new FlightLogData
        {
            sessionId = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            startTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        startTime = Time.time;
        isRecording = true;

        Debug.Log($"[Logger] 新会话开始: {currentLog.sessionId}");
    }

    public void RecordFrame(string droneName, double3 llh, float speed, string stopReason, string command = "", bool isColliding = false)
    {
        if (!isRecording || currentLog == null) return;

        var frame = new DroneFrame
        {
            time = Time.time - startTime,
            droneName = droneName,
            llh = llh,
            speed = speed,
            stopReason = stopReason,
            currentCommand = command,
            isColliding = isColliding
        };

        currentLog.frames.Add(frame);
    }

    public void SaveLog()
    {
        if (currentLog == null || currentLog.frames.Count == 0)
        {
            Debug.LogWarning("[Logger] 没有记录到任何数据");
            return;
        }

        string json = JsonUtility.ToJson(currentLog, true);
        string path = Application.persistentDataPath + $"/FlightLog_{currentLog.sessionId}.json";

        File.WriteAllText(path, json);
        Debug.Log($"[Logger] 日志保存成功！路径：{path} （共 {currentLog.frames.Count} 帧数据）");

        isRecording = false;
    }

    void OnApplicationQuit() => SaveLog();
    void OnDestroy() => SaveLog();
}