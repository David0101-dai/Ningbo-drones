using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;

[RequireComponent(typeof(DroneGeoNavigator))]
public class DroneGridAvoidance : MonoBehaviour
{
    [Header("前向检测")]
    public float detectionDistance = 25f;
    public float detectionRadius = 1.5f;
    public LayerMask obstacleLayer;
    public int forwardRayCount = 5;
    public float forwardRaySpread = 40f;

    [Header("局部网格寻路")]
    public float gridHalfSizeMeters = 80f;
    public float cellSizeMeters = 4f;
    public float cellCheckRadius = 2f;

    [Header("行为")]
    public float minDistanceAfterAvoid = 10f;
    public bool logInfo = true;
    public bool drawDebug = true;

    [Header("调试可视化")]
    public bool debugDrawGridSlice = false;

    [Tooltip("输出更详细的 A* 调试信息")]
    public bool debugLogSearchDetails = true;

    private DroneGeoNavigator navigator;
    private CesiumGeoreference georeference;

    private bool recentlyAvoided = false;
    private Vector3 avoidStartPos;
    private bool planningInProgress = false;
    private string _logPrefix;

    void Awake()
    {
        navigator = GetComponent<DroneGeoNavigator>();
        if (navigator != null)
            georeference = navigator.georeference;
        _logPrefix = $"[GridAvoid {gameObject.name}]";
    }

    void Update()
    {
        if (navigator == null || georeference == null)
            return;

        // 避障后的冷却：飞离一定距离才允许再次避障
        if (recentlyAvoided)
        {
            float dist = Vector3.Distance(avoidStartPos, transform.position);
            if (dist >= minDistanceAfterAvoid)
            {
                recentlyAvoided = false;
                if (logInfo)
                    Debug.Log($"{_logPrefix} 已离开上次避障区域，允许下一次避障");
            }
            else
            {
                return;
            }
        }

        if (planningInProgress)
            return;

        if (!ObstacleAhead())
            return;

        planningInProgress = true;
        navigator.SetStop(DroneGeoNavigator.StopReason.Avoidance, true);

        PlanAndApplyDetour();

        navigator.SetStop(DroneGeoNavigator.StopReason.Avoidance, false);
        planningInProgress = false;
    }

    bool ObstacleAhead()
    {
        Vector3 origin = transform.position;

        // 1. 检查自身是否已经在障碍体内部
        if (Physics.CheckSphere(origin, detectionRadius * 0.8f, obstacleLayer))
        {
            if (drawDebug)
                Debug.DrawRay(origin, transform.up * 3f, Color.magenta, 0.2f);
            return true;
        }

        // 2. 前向扇形检测
        int rays = Mathf.Max(1, forwardRayCount);
        float spread = Mathf.Max(0f, forwardRaySpread);

        for (int i = 0; i < rays; i++)
        {
            float t = (rays == 1) ? 0.5f : (float)i / (rays - 1);
            float angle = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;

            if (Physics.SphereCast(origin, detectionRadius, dir, out RaycastHit hit,
                                   detectionDistance, obstacleLayer))
            {
                if (drawDebug)
                    Debug.DrawLine(origin, hit.point, Color.red, 0.2f);
                return true;
            }
            else if (drawDebug)
            {
                Debug.DrawLine(origin, origin + dir * detectionDistance, Color.cyan, 0.2f);
            }
        }
        return false;
    }

    void PlanAndApplyDetour()
    {
        if (logInfo)
            Debug.Log($"{_logPrefix} 检测到障碍，开始计算绕行路径");

        List<double3> path = navigator.GetPath();
        int currentSeg = navigator.GetCurrentSegmentIndex();

        if (path == null || path.Count < 2 || currentSeg >= path.Count - 1)
        {
            if (logInfo)
                Debug.LogWarning($"{_logPrefix} 路径无效，无法规划绕行");
            return;
        }

        Vector3 currentPos = transform.position;

        // 找一个未来的“并入点”索引
        int joinIndex = FindJoinIndex(path, currentSeg, currentPos);
        if (joinIndex <= currentSeg + 1)
        {
            if (logInfo)
                Debug.LogWarning($"{_logPrefix} 没有找到合适的并入点，本次不避障");
            return;
        }

        Vector3 joinWorldPos = LLHToUnity(path[joinIndex]);
        if (logInfo)
            Debug.Log($"{_logPrefix} 选择并入目标 index={joinIndex}, pos={joinWorldPos}");

        // 在 currentPos 和 joinWorldPos 之间用 A* 算一条折线
        List<Vector3> detourWorld = ComputeGridPath(currentPos, joinWorldPos, currentPos.y);
        if (detourWorld == null || detourWorld.Count == 0)
        {
            if (logInfo)
                Debug.LogWarning($"{_logPrefix} A* 未找到可行绕行路径");
            return;
        }

        // 折线转 LLH 插入
        List<double3> detourLLH = new List<double3>(detourWorld.Count);
        foreach (var p in detourWorld)
            detourLLH.Add(UnityToLLH(p));

        int removeCount = Mathf.Max(0, joinIndex - (currentSeg + 1));
        if (removeCount > 0)
            path.RemoveRange(currentSeg + 1, removeCount);

        path.InsertRange(currentSeg + 1, detourLLH);

        avoidStartPos = currentPos;
        recentlyAvoided = true;

        if (logInfo)
            Debug.Log($"{_logPrefix} 插入绕行点 {detourLLH.Count} 个，移除原路径点 {removeCount} 个");
    }

    int FindJoinIndex(List<double3> path, int currentSeg, Vector3 currentPos)
    {
        int lastIndex = path.Count - 1;
        int maxLookAhead = Mathf.Min(lastIndex, currentSeg + 30);

        for (int i = currentSeg + 2; i <= maxLookAhead; i++)
        {
            Vector3 candidatePos = LLHToUnity(path[i]);
            if (HasLineOfSight(currentPos, candidatePos))
                return i;
        }
        /////////////////////////////////////////////////
        return lastIndex;
    }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 1f)
            return true;

        dir /= dist;
        if (Physics.SphereCast(from, detectionRadius, dir, out RaycastHit hit,
                               dist, obstacleLayer))
            return false;

        return true;
    }

    class Node
    {
        public int ix;
        public int iz;
        public float g;
        public float h;
        public Node parent;
        public bool walkable;
        public float f => g + h;
    }

List<Vector3> ComputeGridPath(Vector3 startWorld, Vector3 endWorld, float yHeight)
    {
        float size = gridHalfSizeMeters;
        float cell = Mathf.Max(0.5f, cellSizeMeters);

        Vector3 mid = (startWorld + endWorld) * 0.5f;
        float originX = mid.x - size;
        float originZ = mid.z - size;
        int gridSize = Mathf.Max(4, Mathf.CeilToInt((size * 2f) / cell));

        Node[,] grid = new Node[gridSize, gridSize];

        int walkableCount = 0;

        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iz = 0; iz < gridSize; iz++)
            {
                float cx = originX + (ix + 0.5f) * cell;
                float cz = originZ + (iz + 0.5f) * cell;
                Vector3 cpos = new Vector3(cx, yHeight, cz);

                bool blocked = Physics.CheckSphere(cpos, cellCheckRadius, obstacleLayer);

                Node n = new Node();
                n.ix = ix;
                n.iz = iz;
                n.walkable = !blocked;
                n.g = float.PositiveInfinity;
                n.h = 0f;
                n.parent = null;

                if (n.walkable)
                    walkableCount++;

                grid[ix, iz] = n;
            }
        }

        // 将世界坐标转换为网格索引
        int startIx = Mathf.Clamp(Mathf.FloorToInt((startWorld.x - originX) / cell), 0, gridSize - 1);
        int startIz = Mathf.Clamp(Mathf.FloorToInt((startWorld.z - originZ) / cell), 0, gridSize - 1);
        int endIx   = Mathf.Clamp(Mathf.FloorToInt((endWorld.x   - originX) / cell), 0, gridSize - 1);
        int endIz   = Mathf.Clamp(Mathf.FloorToInt((endWorld.z   - originZ) / cell), 0, gridSize - 1);

        Node startNode = FindNearestWalkable(grid, startIx, startIz);
        Node endNode   = FindNearestWalkable(grid, endIx,   endIz);

        if (startNode == null || endNode == null)
        {
            if (logInfo)
                Debug.LogWarning($"{_logPrefix} 起点或终点附近没有可行走格子 " +
                                 $"(walkable={walkableCount}/{gridSize * gridSize})");
            return null;
        }

        // A* 搜索
        List<Node> openList = new List<Node>();
        HashSet<Node> closedSet = new HashSet<Node>();

        startNode.g = 0f;
        startNode.h = Heuristic(startNode, endNode, cell);
        openList.Add(startNode);

        int[] dirX = { 1, -1, 0, 0, 1, 1, -1, -1 }; // 8 邻域
        int[] dirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        float[] dirCost = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        int maxIterations = gridSize * gridSize * 4;
        int iter = 0;
        int visitedCount = 0;

        while (openList.Count > 0 && iter < maxIterations)
        {
            iter++;

            // 取 f 最小的节点
            Node current = openList[0];
            int currentIndex = 0;
            for (int i = 1; i < openList.Count; i++)
            {
                if (openList[i].f < current.f)
                {
                    current = openList[i];
                    currentIndex = i;
                }
            }

            openList.RemoveAt(currentIndex);
            closedSet.Add(current);
            visitedCount++;

            if (current == endNode)
            {
                List<Vector3> pathWorld = ReconstructPath(current, originX, originZ, cell, yHeight);

                if (!ValidateDetour(startWorld, endWorld, pathWorld))
                {
                    if (logInfo)
                        Debug.LogWarning($"{_logPrefix} 生成的绕行路径仍然与障碍相交，放弃本次规划");
                    return null;
                }

                if (debugLogSearchDetails && logInfo)
                {
                    Debug.Log($"{_logPrefix} A* 成功: visited={visitedCount}, " +
                              $"walkable={walkableCount}/{gridSize * gridSize}, " +
                              $"gridSize={gridSize}, " +
                              $"start=({startNode.ix},{startNode.iz}), end=({endNode.ix},{endNode.iz}), " +
                              $"iter={iter}");
                }

                return pathWorld;
            }

            for (int d = 0; d < dirX.Length; d++)
            {
                int nx = current.ix + dirX[d];
                int nz = current.iz + dirZ[d];

                if (nx < 0 || nx >= gridSize || nz < 0 || nz >= gridSize)
                    continue;

                Node neighbor = grid[nx, nz];
                if (!neighbor.walkable || closedSet.Contains(neighbor))
                    continue;

                float stepCost = dirCost[d] * cell;
                float tentativeG = current.g + stepCost;

                if (tentativeG < neighbor.g)
                {
                    neighbor.parent = current;
                    neighbor.g = tentativeG;
                    neighbor.h = Heuristic(neighbor, endNode, cell);

                    if (!openList.Contains(neighbor))
                        openList.Add(neighbor);
                }
            }
        }

        bool hitIterationLimit = (iter >= maxIterations && openList.Count > 0);

        if (logInfo)
        {
            string reason = hitIterationLimit ? "超出迭代上限" : "openList 为空，无路可达";
            Debug.LogWarning(
                $"{_logPrefix} A* 搜索失败: {reason}, " +
                $"visited={visitedCount}, walkable={walkableCount}/{gridSize * gridSize}, gridSize={gridSize}, " +
                $"start=({startNode.ix},{startNode.iz}), end=({endNode.ix},{endNode.iz}), iter={iter}");
        }

        return null;
    }
    float Heuristic(Node a, Node b, float cell)
    {
        // 曼哈顿 + 对角启发
        int dx = Mathf.Abs(a.ix - b.ix);
        int dz = Mathf.Abs(a.iz - b.iz);
        int diag = Mathf.Min(dx, dz);
        int straight = dx + dz - 2 * diag;
        return (diag * 1.414f + straight) * cell;
    }

    Node FindNearestWalkable(Node[,] grid, int ix, int iz)
    {
        int sizeX = grid.GetLength(0);
        int sizeZ = grid.GetLength(1);

        if (ix < 0 || ix >= sizeX || iz < 0 || iz >= sizeZ)
            return null;

        if (grid[ix, iz].walkable)
            return grid[ix, iz];

        int maxRadius = Mathf.Max(sizeX, sizeZ);
        for (int r = 1; r < maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    int nx = ix + dx;
                    int nz = iz + dz;
                    if (nx < 0 || nx >= sizeX || nz < 0 || nz >= sizeZ)
                        continue;

                    Node n = grid[nx, nz];
                    if (n.walkable)
                        return n;
                }
            }
        }

        return null;
    }

    List<Vector3> ReconstructPath(Node endNode, float originX, float originZ, float cell, float yHeight)
    {
        List<Vector3> result = new List<Vector3>();
        Node current = endNode;
        while (current != null)
        {
            float cx = originX + (current.ix + 0.5f) * cell;
            float cz = originZ + (current.iz + 0.5f) * cell;
            result.Insert(0, new Vector3(cx, yHeight, cz));
            current = current.parent;
        }

        // 简单去掉几乎共线的中间点
        result = SimplifyPath(result);

        return result;
    }

    List<Vector3> SimplifyPath(List<Vector3> points)
    {
        if (points == null || points.Count <= 2)
            return points;

        List<Vector3> simplified = new List<Vector3>();
        simplified.Add(points[0]);

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 prev = simplified[simplified.Count - 1];
            Vector3 curr = points[i];
            Vector3 next = points[i + 1];

            Vector3 v1 = (curr - prev).normalized;
            Vector3 v2 = (next - curr).normalized;

            // 如果方向变化很小，就跳过这个点
            float dot = Vector3.Dot(v1, v2);
            if (dot < 0.99f) // 不完全共线才保留
                simplified.Add(curr);
        }

        simplified.Add(points[points.Count - 1]);
        return simplified;
    }

    // 对整个绕行路径做最终碰撞校验：起点->首点，点间各段，末点->并入点
    bool ValidateDetour(Vector3 start, Vector3 end, List<Vector3> points)
    {
        if (points == null || points.Count == 0)
            return false;

        // 起点 -> 第一段
        if (SegmentHitsObstacle(start, points[0]))
            return false;

        // 中间各段
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (SegmentHitsObstacle(points[i], points[i + 1]))
                return false;
        }

        // 最后一段 -> 并入点
        if (SegmentHitsObstacle(points[points.Count - 1], end))
            return false;

        return true;
    }

    bool SegmentHitsObstacle(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.5f)
            return false;

        dir /= dist;

        // 用检测半径略大一点的 SphereCast 检查整段
        float radius = Mathf.Max(detectionRadius, cellCheckRadius);
        return Physics.SphereCast(from, radius, dir, out RaycastHit hit, dist, obstacleLayer);
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

    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 只在勾选了调试、并且游戏在运行时画网格
        if (!debugDrawGridSlice || !Application.isPlaying)
            return;

        if (navigator == null || georeference == null)
            return;

        // 起点：当前无人机位置
        Vector3 startWorld = transform.position;

        // 终点：复用一部分路径逻辑，取一个“未来的并入点”
        List<double3> path = navigator.GetPath();
        int currentSeg = navigator.GetCurrentSegmentIndex();

        if (path == null || path.Count < 2 || currentSeg >= path.Count - 1)
            return;

        // 尝试用和运行时相同的策略选一个 joinIndex
        int joinIndex = FindJoinIndex(path, currentSeg, startWorld);
        joinIndex = Mathf.Clamp(joinIndex, currentSeg + 1, path.Count - 1);

        Vector3 endWorld = LLHToUnity(path[joinIndex]);
        float yHeight = startWorld.y;  // 使用当前无人机高度这一个水平切片

        // === 下面开始重用 ComputeGridPath 的网格构造逻辑 ===

        float size = gridHalfSizeMeters;
        float cell = Mathf.Max(0.5f, cellSizeMeters);

        // 网格中心：起点和并入点的中点
        Vector3 mid = (startWorld + endWorld) * 0.5f;
        float originX = mid.x - size;
        float originZ = mid.z - size;
        int gridSize = Mathf.Max(4, Mathf.CeilToInt((size * 2f) / cell));

        // 画网格包围框（在当前高度画一个很薄的“框”）
        Gizmos.color = Color.yellow;
        Vector3 center = new Vector3(originX + size, yHeight, originZ + size);
        Vector3 cubeSize = new Vector3(size * 2f, 0.1f, size * 2f);
        Gizmos.DrawWireCube(center, cubeSize);

        // 画起点 / 终点
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(startWorld, cell * 0.7f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(endWorld, cell * 0.7f);

        Gizmos.DrawLine(startWorld, endWorld);

        // walkable / blocked 颜色（带一点透明度避免太刺眼）
        Color walkableColor = new Color(0f, 1f, 0f, 0.25f);
        Color blockedColor  = new Color(1f, 0f, 0f, 0.35f);

        Vector3 cellSizeVec = new Vector3(cell, 0.05f, cell);

        // 遍历整张网格，逐格调用 Physics.CheckSphere 决定是否 blocked，并画立方体
        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iz = 0; iz < gridSize; iz++)
            {
                float cx = originX + (ix + 0.5f) * cell;
                float cz = originZ + (iz + 0.5f) * cell;
                Vector3 cpos = new Vector3(cx, yHeight, cz);

                bool blocked = Physics.CheckSphere(cpos, cellCheckRadius, obstacleLayer);

                Gizmos.color = blocked ? blockedColor : walkableColor;
                Gizmos.DrawCube(cpos, cellSizeVec);
            }
        }
    }
#endif
}