using System;
using System.Collections.Generic;
using Unity.Mathematics;   // ←←← 必须加上这一行

[Serializable]
public class FlightLogData
{
    public string sessionId;
    public string startTime;
    public List<DroneFrame> frames = new List<DroneFrame>();
}

[Serializable]
public class DroneFrame
{
    public float time;
    public string droneName;
    public double3 llh;                // 现在不会报错了
    public float speed;
    public string stopReason;
    public string currentCommand;
    public bool isColliding;
}