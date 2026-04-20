// Assets/Scripts/Core/DLog.cs
using UnityEngine;
using System.Diagnostics;

public static class DLog
{
    public enum Level { Verbose, Info, Warning, Error }

    public static Level MinLevel = Level.Info;

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Verbose(string tag, string msg)
    {
        if (MinLevel <= Level.Verbose)
            UnityEngine.Debug.Log("[" + tag + "] " + msg);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Info(string tag, string msg)
    {
        if (MinLevel <= Level.Info)
            UnityEngine.Debug.Log("[" + tag + "] " + msg);
    }

    public static void Warn(string tag, string msg)
    {
        UnityEngine.Debug.LogWarning("[" + tag + "] " + msg);
    }

    public static void Error(string tag, string msg)
    {
        UnityEngine.Debug.LogError("[" + tag + "] " + msg);
    }

    public static void Milestone(string tag, string msg)
    {
        UnityEngine.Debug.Log("<b>[" + tag + "]</b> " + msg);
    }
}