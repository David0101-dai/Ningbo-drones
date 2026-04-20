using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 自动发现并注册所有 IRoutingSolver 实现。
/// 未来同学只需在项目中放入一个实现了 IRoutingSolver 的 .cs 文件，
/// 无需修改本文件或任何其他文件，算法自动出现在下拉菜单中。
/// </summary>
public class SolverRegistry : MonoBehaviour
{
    public static SolverRegistry Instance { get; private set; }

    private readonly List<IRoutingSolver> _solvers = new();
    private int _activeIndex = 0;

    // ═══════════════════════════════════════
    //  公共属性
    // ═══════════════════════════════════════
    public int Count => _solvers.Count;
    public int ActiveIndex => _activeIndex;
    public IRoutingSolver ActiveSolver => _solvers.Count > 0 ? _solvers[_activeIndex] : null;
    public List<string> SolverNames => _solvers.Select(s => s.Name).ToList();

    // ═══════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        AutoDiscoverSolvers();
    }

    // ═══════════════════════════════════════
    //  核心：反射自动发现所有 Solver
    // ═══════════════════════════════════════
    private void AutoDiscoverSolvers()
    {
        _solvers.Clear();

        var solverType = typeof(IRoutingSolver);

        // 扫描当前程序集中所有实现了 IRoutingSolver 的非抽象类
        var found = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => solverType.IsAssignableFrom(t)
                     && !t.IsInterface
                     && !t.IsAbstract)
            .OrderBy(t => GetSolverOrder(t))   // 按优先级排序
            .ThenBy(t => t.Name)               // 同优先级按名称排序
            .ToList();

        foreach (var type in found)
        {
            try
            {
                var instance = (IRoutingSolver)Activator.CreateInstance(type);
                _solvers.Add(instance);
                DLog.Info("Registry", $" Discovered: {instance.Name} ({type.Name})");
            }
            catch (Exception e)
            {
                DLog.Warn("General",$"[SolverRegistry] Failed to instantiate {type.Name}: {e.Message}");
            }
        }

        if (_solvers.Count == 0)
        {
            DLog.Error("General","[SolverRegistry] No IRoutingSolver implementations found!");
        }
        else
        {
            DLog.Info("Registry", $" {_solvers.Count} solvers ready. Default: {_solvers[0].Name}");
        }
    }

    // ═══════════════════════════════════════
    //  排序：内置算法靠前，用户算法按名称排列
    // ═══════════════════════════════════════
    private int GetSolverOrder(Type t)
    {
        // 内置 Solver 固定排在前面
        if (t == typeof(SolomonI1Solver))    return 0;
        if (t == typeof(NearestFirstSolver)) return 1;
        // 所有用户自定义 Solver 排在后面
        return 100;
    }

    // ═══════════════════════════════════════
    //  公共方法
    // ═══════════════════════════════════════
    public void SetActiveSolver(int index)
    {
        if (index >= 0 && index < _solvers.Count)
        {
            _activeIndex = index;
            DLog.Info("Registry", $" Active solver: {_solvers[_activeIndex].Name}");
        }
    }

    public IRoutingSolver GetSolver(int index)
    {
        return (index >= 0 && index < _solvers.Count) ? _solvers[index] : null;
    }

    public IRoutingSolver GetSolverByName(string name)
    {
        return _solvers.FirstOrDefault(s => s.Name == name);
    }
}