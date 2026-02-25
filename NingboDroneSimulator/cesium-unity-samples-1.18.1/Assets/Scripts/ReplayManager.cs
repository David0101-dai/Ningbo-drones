using UnityEngine;
using System.Collections;
using System.IO;
using Unity.Mathematics;

public class ReplayManager : MonoBehaviour
{
    public static ReplayManager Instance;

    private FlightLogData loadedLog;
    private bool isReplaying = false;
    private float replayStartTime;
    private int currentFrameIndex = 0;

    [Header("回放控制")]
    public float replaySpeed = 1.0f;

    private bool isLoading = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        Debug.Log("[Replay] ReplayManager 已启动");
    }

    // ==================== 异步加载（解决卡顿） ====================
    public void LoadReplayAsync(string filePath)
    {
        if (isLoading) return;
        StartCoroutine(LoadReplayCoroutine(filePath));
    }

    private IEnumerator LoadReplayCoroutine(string filePath)
    {
        isLoading = true;
        LogOut("[Replay] 开始加载日志文件...");

        if (!File.Exists(filePath))
        {
            LogOut("[Replay] 文件不存在！");
            isLoading = false;
            yield break;
        }
        
        LogOut($"[Replay] 正在加载... 已处理 {loadedLog.frames.Count} 帧");

        // 异步读取文件（避免卡顿）
        string json = "";
        yield return new WaitForSeconds(0.1f); // 让UI先显示

        json = File.ReadAllText(filePath);

        // 解析 JSON
        loadedLog = JsonUtility.FromJson<FlightLogData>(json);

        if (loadedLog == null || loadedLog.frames.Count == 0)
        {
            LogOut("[Replay] 文件解析失败或无数据！");
            isLoading = false;
            yield break;
        }

        LogOut($"[Replay] 加载完成！共 {loadedLog.frames.Count} 帧数据");
        isLoading = false;

        // 加载完成后自动启用开始回放按钮（后面我们会加UI控制）
    }

    private void LogOut(string msg)
    {
        Debug.Log(msg);
        // 如果你想在 OutputText 显示进度，可以在这里调用
        // UIPanelManager.Instance?.UpdateOutputText(msg); 
    }

    // 开始回放
    public void StartReplay()
    {
        if (loadedLog == null)
        {
            Debug.LogWarning("[Replay] 请先加载日志文件");
            return;
        }

        replayStartTime = Time.time;
        currentFrameIndex = 0;
        isReplaying = true;

        Debug.Log("[Replay] 开始回放...");
    }

    public void StopReplay()
    {
        isReplaying = false;
        Debug.Log("[Replay] 回放停止");
    }

    void Update()
    {
        if (!isReplaying || loadedLog == null) return;

        float currentTime = (Time.time - replayStartTime) * replaySpeed;

        while (currentFrameIndex < loadedLog.frames.Count - 1 && 
               loadedLog.frames[currentFrameIndex + 1].time < currentTime)
        {
            currentFrameIndex++;
        }

        if (currentFrameIndex >= loadedLog.frames.Count - 1)
        {
            StopReplay();
            return;
        }

        var frame = loadedLog.frames[currentFrameIndex];

        // 查找并更新无人机
        DroneInfo[] drones = FindObjectsOfType<DroneInfo>(true);
        foreach (var d in drones)
        {
            if (d.GetName() == frame.droneName || d.gameObject.name.Contains(frame.droneName))
            {
                var nav = d.GetComponent<DroneGeoNavigator>();
                if (nav != null)
                {
                    nav.SetReplayPosition(frame.llh);
                }
                break;
            }
        }
    }

    
}