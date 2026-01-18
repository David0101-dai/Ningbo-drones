using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class RuntimeWaypointsBuilder : MonoBehaviour
{
    [Header("Cesium")]
    public CesiumGeoreference georeference;

    [Header("Where to write generated points")]
    public Transform runtimeWaypointsParent; // Waypoints_Runtime

    [Header("Collision")]
    public LayerMask obstacleLayer = ~0; // ✅ 记得把 UAV / Marker / Waypoints 的 layer 排除
    public float safetyRadius = 2.0f;

    [Header("Altitude (LLH height based)")]
    public float pickLiftMin = 25f;          // 兜底：起终点至少离地(相对原点)这么高
    public float cruiseHeightOffset = 60f;   // 巡航高度相对 max(start,end) 的加高
    public float heightStep = 20f;
    public int maxHeightRetries = 10;

    [Header("Escape from near-building (key fix)")]
    public float escapeRadiusMin = 10f;
    public float escapeRadiusMax = 120f;
    public float escapeRadiusStep = 10f;
    public int escapeAngles = 16;

    [Header("Mid-path (RRT)")]
    public bool enableRrtMidPath = true;
    public int rrtMaxIters = 1200;
    public float rrtStep = 30f;
    [Range(0f, 1f)] public float rrtGoalBias = 0.2f;
    public float rrtBoundsMargin = 200f; // 采样边界扩展
    public int rrtPathMaxPoints = 60;

    [Header("Fallback")]
    public bool allowFallbackWhenBlocked = true;

    public bool LastUsedFallback { get; private set; }
    public string LastFailReason { get; private set; }

    void Awake()
    {
        if (!georeference) georeference = FindObjectOfType<CesiumGeoreference>();
    }

    // ===== Public: build route LLH list =====
    public bool BuildRoute(double3 startLLH, double3 endLLH, out List<double3> outLLH)
    {
        outLLH = null;
        LastUsedFallback = false;
        LastFailReason = "";

        if (!georeference || !runtimeWaypointsParent)
        {
            LastFailReason = "Missing georeference/runtimeWaypointsParent";
            return false;
        }

        // 统一一个“基准巡航高度”
        double baseH = System.Math.Max(startLLH.z, endLLH.z) + cruiseHeightOffset;

        // 起点/终点：先确保“不是贴地”，再确保“不在碰撞体内部”
        startLLH = LiftUntilFree(EnsureMinLift(startLLH), baseH);
        endLLH   = LiftUntilFree(EnsureMinLift(endLLH), baseH);

        // 尝试多个高度，选成本最小的可行路线
        List<double3> best = null;
        double bestCost = double.PositiveInfinity;

        for (int hi = 0; hi <= maxHeightRetries; hi++)
        {
            double cruiseH = baseH + hi * heightStep;

            // 1) 先构造 start leg：start -> (escape?) -> startCruise
            if (!BuildLegToCruise(startLLH, cruiseH, out var legStart))
                continue;

            // 2) end leg：end -> (escape?) -> endCruise（注意方向：我们最后要拼接成 start ... end）
            if (!BuildLegToCruise(endLLH, cruiseH, out var legEndReverse))
                continue;

            // legEndReverse 是 end->...->endCruise，我们要反过来拼接（endCruise->...->end）
            legEndReverse.Reverse();

            double3 startCruiseLLH = legStart[legStart.Count - 1];
            double3 endCruiseLLH   = legEndReverse[0];

            Vector3 startCruiseW = LLHToUnity(startCruiseLLH);
            Vector3 endCruiseW   = LLHToUnity(endCruiseLLH);

            // 3) 中段：优先直线；被挡则用 RRT 生成多点侧向路径
            List<double3> mid = null;

            if (IsSegmentClear(startCruiseW, endCruiseW))
            {
                mid = new List<double3> { startCruiseLLH, endCruiseLLH };
            }
            else if (enableRrtMidPath)
            {
                if (TryBuildRrtMidPath(startCruiseLLH, endCruiseLLH, cruiseH, out var midRrt))
                    mid = midRrt;
            }

            if (mid == null)
                continue;

            // 4) 拼接完整路径：legStart + mid(去掉重复起点) + legEndReverse(去掉重复起点)
            var full = new List<double3>();
            full.AddRange(legStart);

            // mid 的第一个点==startCruise，避免重复
            for (int i = 1; i < mid.Count; i++) full.Add(mid[i]);

            // legEndReverse 的第一个点==endCruise，避免重复
            for (int i = 1; i < legEndReverse.Count; i++) full.Add(legEndReverse[i]);

            // 5) 成本：用 Unity world 距离近似（城市尺度够用）
            double cost = ComputeCostWorld(full);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = full;
            }
        }

        if (best != null)
        {
            outLLH = best;
            return true;
        }

        if (allowFallbackWhenBlocked)
        {
            // 兜底：尽量写一个“能飞”的路线（让你先链路跑通）
            double cruiseH = baseH + maxHeightRetries * heightStep;

            var upS = new double3(startLLH.x, startLLH.y, cruiseH);
            var upE = new double3(endLLH.x, endLLH.y, cruiseH);

            outLLH = new List<double3> { startLLH, upS, upE, endLLH };
            LastUsedFallback = true;
            LastFailReason = "No collision-free route; fallback used.";
            Debug.LogWarning($"[RuntimeWaypointsBuilder] {LastFailReason}");
            return true;
        }

        LastFailReason = "No route found (blocked).";
        return false;
    }

    public bool WriteToRuntimeParent(List<double3> llhPoints)
    {
        if (!runtimeWaypointsParent || llhPoints == null || llhPoints.Count < 2) return false;

        for (int i = runtimeWaypointsParent.childCount - 1; i >= 0; i--)
            Destroy(runtimeWaypointsParent.GetChild(i).gameObject);

        for (int i = 0; i < llhPoints.Count; i++)
        {
            var go = new GameObject($"WP{i + 1}");
            go.transform.SetParent(runtimeWaypointsParent, false);

            var anc = go.AddComponent<CesiumGlobeAnchor>();
            anc.longitudeLatitudeHeight = llhPoints[i];
        }

        Debug.Log($"[RuntimeWaypointsBuilder] Wrote {llhPoints.Count} points to {runtimeWaypointsParent.name} (fallback={LastUsedFallback})");
        return true;
    }

    // ===== Build leg: startLLH -> (escape?) -> cruiseLLH =====
    bool BuildLegToCruise(double3 startLLH, double cruiseH, out List<double3> leg)
    {
        leg = null;

        var cruiseLLH = new double3(startLLH.x, startLLH.y, cruiseH);

        Vector3 startW = LLHToUnity(startLLH);
        Vector3 cruiseW = LLHToUnity(cruiseLLH);

        // 直接可通
        if (IsSegmentClear(startW, cruiseW))
        {
            leg = new List<double3> { startLLH, cruiseLLH };
            return true;
        }

        // 尝试 escape：在 start 周围找一个点，满足 start->escape 和 escape->cruise 都不撞
        if (TryFindEscapePoint(startLLH, cruiseLLH, out var escapeLLH))
        {
            leg = new List<double3> { startLLH, escapeLLH, cruiseLLH };
            return true;
        }

        return false;
    }

    bool TryFindEscapePoint(double3 startLLH, double3 cruiseLLH, out double3 escapeLLH)
    {
        escapeLLH = default;

        Vector3 startW = LLHToUnity(startLLH);
        Vector3 cruiseW = LLHToUnity(cruiseLLH);

        // 用 world 平面做近似（城市尺度够用）
        Vector3 dir = cruiseW - startW; dir.y = 0;
        if (dir.sqrMagnitude < 1e-3f) dir = Vector3.forward;
        dir.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;

        double bestCost = double.PositiveInfinity;
        bool found = false;

        for (float r = escapeRadiusMin; r <= escapeRadiusMax; r += escapeRadiusStep)
        {
            for (int a = 0; a < escapeAngles; a++)
            {
                float ang = (a / (float)escapeAngles) * Mathf.PI * 2f;
                Vector3 offset = right * Mathf.Cos(ang) * r + dir * Mathf.Sin(ang) * r;

                Vector3 candW0 = startW + offset;

                // 转回 LLH，再强制高度为 startLLH.z（不贴地也不乱飞）
                var candLLH = UnityToLLH(candW0);
                candLLH = new double3(candLLH.x, candLLH.y, startLLH.z);
                Vector3 candW = LLHToUnity(candLLH);

                if (Physics.CheckSphere(candW, safetyRadius * 0.8f, obstacleLayer))
                    continue;

                if (!IsSegmentClear(startW, candW))
                    continue;

                if (!IsSegmentClear(candW, cruiseW))
                    continue;

                double cost = (candW - startW).magnitude + (cruiseW - candW).magnitude;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    escapeLLH = candLLH;
                    found = true;
                }
            }
        }

        return found;
    }

    // ===== RRT for mid path (at fixed cruiseH) =====
    class RrtNode
    {
        public Vector3 p;
        public int parent;
    }

    bool TryBuildRrtMidPath(double3 startCruiseLLH, double3 endCruiseLLH, double cruiseH, out List<double3> midLLH)
    {
        midLLH = null;

        Vector3 startW = LLHToUnity(startCruiseLLH);
        Vector3 goalW  = LLHToUnity(endCruiseLLH);

        // 采样边界（AABB + margin）
        float minX = Mathf.Min(startW.x, goalW.x) - rrtBoundsMargin;
        float maxX = Mathf.Max(startW.x, goalW.x) + rrtBoundsMargin;
        float minZ = Mathf.Min(startW.z, goalW.z) - rrtBoundsMargin;
        float maxZ = Mathf.Max(startW.z, goalW.z) + rrtBoundsMargin;

        var nodes = new List<RrtNode>(rrtMaxIters + 2) { new RrtNode { p = startW, parent = -1 } };

        int goalIndex = -1;

        for (int iter = 0; iter < rrtMaxIters; iter++)
        {
            Vector3 sample;

            if (UnityEngine.Random.value < rrtGoalBias)
            {
                sample = goalW;
            }
            else
            {
                sample = new Vector3(
                    UnityEngine.Random.Range(minX, maxX),
                    startW.y,
                    UnityEngine.Random.Range(minZ, maxZ)
                );
            }

            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = (nodes[i].p - sample).sqrMagnitude;
                if (d < best) { best = d; nearest = i; }
            }

            Vector3 from = nodes[nearest].p;
            Vector3 dir = (sample - from); dir.y = 0;
            float dist = dir.magnitude;
            if (dist < 1e-3f) continue;
            dir /= dist;

            Vector3 to = from + dir * Mathf.Min(rrtStep, dist);

            if (Physics.CheckSphere(to, safetyRadius * 0.8f, obstacleLayer))
                continue;

            if (!IsSegmentClear(from, to))
                continue;

            nodes.Add(new RrtNode { p = to, parent = nearest });
            int newIndex = nodes.Count - 1;

            // 试图连到 goal
            if ((to - goalW).magnitude <= rrtStep && IsSegmentClear(to, goalW))
            {
                nodes.Add(new RrtNode { p = goalW, parent = newIndex });
                goalIndex = nodes.Count - 1;
                break;
            }
        }

        if (goalIndex < 0) return false;

        // 回溯
        var pathW = new List<Vector3>();
        int cur = goalIndex;
        while (cur >= 0)
        {
            pathW.Add(nodes[cur].p);
            cur = nodes[cur].parent;
        }
        pathW.Reverse();

        // 简化：能直连就删中间点
        pathW = SimplifyPath(pathW);

        // 限制点数（防止太密）
        if (pathW.Count > rrtPathMaxPoints)
        {
            var down = new List<Vector3>();
            float step = (pathW.Count - 1) / (float)(rrtPathMaxPoints - 1);
            for (int i = 0; i < rrtPathMaxPoints; i++)
            {
                int idx = Mathf.RoundToInt(i * step);
                idx = Mathf.Clamp(idx, 0, pathW.Count - 1);
                down.Add(pathW[idx]);
            }
            pathW = down;
        }

        // world -> LLH，并强制高度 cruiseH（Cesium-aware）
        var llh = new List<double3>(pathW.Count);
        for (int i = 0; i < pathW.Count; i++)
        {
            var pLLH = UnityToLLH(pathW[i]);
            llh.Add(new double3(pLLH.x, pLLH.y, cruiseH));
        }

        midLLH = llh;
        return true;
    }

    List<Vector3> SimplifyPath(List<Vector3> pts)
    {
        if (pts == null || pts.Count <= 2) return pts;

        var outPts = new List<Vector3>();
        int i = 0;
        outPts.Add(pts[0]);

        while (i < pts.Count - 1)
        {
            int far = pts.Count - 1;
            for (int j = pts.Count - 1; j > i; j--)
            {
                if (IsSegmentClear(pts[i], pts[j]))
                {
                    far = j;
                    break;
                }
            }
            outPts.Add(pts[far]);
            i = far;
        }

        return outPts;
    }

    // ===== Helpers =====
    double3 EnsureMinLift(double3 llh)
    {
        // 你的 MapPickController 已经 +25m，这里只是再兜底一次
        if (llh.z < pickLiftMin) return new double3(llh.x, llh.y, pickLiftMin);
        return llh;
    }

    double3 LiftUntilFree(double3 llh, double minHeight)
    {
        if (llh.z < minHeight) llh = new double3(llh.x, llh.y, minHeight);

        for (int i = 0; i < 12; i++)
        {
            Vector3 w = LLHToUnity(llh);
            if (!Physics.CheckSphere(w, safetyRadius * 0.8f, obstacleLayer))
                return llh;

            llh = new double3(llh.x, llh.y, llh.z + heightStep);
        }
        return llh;
    }

    bool IsSegmentClear(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.5f) return true;
        dir /= dist;

        if (Physics.CheckSphere(from, safetyRadius * 0.8f, obstacleLayer))
            return false;

        return !Physics.SphereCast(from, safetyRadius, dir, out RaycastHit hit, dist, obstacleLayer);
    }

    double ComputeCostWorld(List<double3> llhPath)
    {
        double sum = 0;
        for (int i = 0; i < llhPath.Count - 1; i++)
        {
            Vector3 a = LLHToUnity(llhPath[i]);
            Vector3 b = LLHToUnity(llhPath[i + 1]);
            sum += (b - a).magnitude;
        }
        return sum;
    }

    Vector3 LLHToUnity(double3 llh)
    {
        double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(llh);
        double3 unity = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        return new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
    }

    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 u = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(u);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }
}
