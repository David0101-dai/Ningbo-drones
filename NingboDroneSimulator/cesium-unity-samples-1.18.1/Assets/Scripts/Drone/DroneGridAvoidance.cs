// Assets/Scripts/Drone/DroneGridAvoidance.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;

[RequireComponent(typeof(DroneGeoNavigator))]
public class DroneGridAvoidance : MonoBehaviour
{
    [Header("Forward Detection")]
    public float detectionDistance = 30f;
    public float detectionRadius = 1.0f;
    public LayerMask obstacleLayer;
    public int forwardRayCount = 5;
    public float forwardRaySpread = 20f;

    [Header("Speed-Adaptive Detection")]
    [Tooltip("Detection distance scales with speed: base + speed * factor")]
    public float speedDetectionFactor = 1.5f;

    [Header("Local Grid Pathfinding")]
    public float gridHalfSizeMeters = 100f;
    public float cellSizeMeters = 4f;
    public float cellCheckRadius = 2.2f;

    [Header("Height Escalation")]
    [Tooltip("If A* fails at current height, try this many height steps above")]
    public int maxHeightEscalations = 3;
    public float heightEscalationStep = 8f;

    [Header("Behavior")]
    public float minDistanceAfterAvoid = 15f;
    public float minTimeAfterAvoid = 2.0f;
    public bool logInfo = true;
    public bool drawDebug = true;

    [Header("Debug Visualization")]
    public bool debugDrawGridSlice = false;
    public bool debugLogSearchDetails = false;

    private DroneGeoNavigator navigator;
    private CesiumGeoreference georeference;

    private bool recentlyAvoided = false;
    private Vector3 avoidStartPos;
    private float avoidStartTime;
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
        if (navigator == null || georeference == null) return;

        // Skip if in replay mode
        if (navigator.IsStopped() &&
            (navigator.IsPaused() || navigator.HasNoPath()))
            return;

        // Cooldown: must fly away AND wait minimum time
        if (recentlyAvoided)
        {
            float dist = Vector3.Distance(avoidStartPos, transform.position);
            float elapsed = Time.time - avoidStartTime;

            if (dist >= minDistanceAfterAvoid && elapsed >= minTimeAfterAvoid)
            {
                recentlyAvoided = false;
                if (logInfo)
                    Debug.Log($"{_logPrefix} Cooldown ended (dist={dist:F1}m, time={elapsed:F1}s)");
            }
            else
            {
                return;
            }
        }

        if (planningInProgress) return;

        if (!ObstacleAhead()) return;

        planningInProgress = true;
        navigator.SetStop(DroneGeoNavigator.StopReason.Avoidance, true);

        PlanAndApplyDetour();

        navigator.SetStop(DroneGeoNavigator.StopReason.Avoidance, false);
        planningInProgress = false;
    }

    bool ObstacleAhead()
    {
        Vector3 origin = transform.position;
        Vector3 fwd = transform.forward;

        // Skip if forward direction is mostly vertical (climbing/descending)
        if (Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.7f)
            return false;

        // 1. Check if inside an obstacle
        if (Physics.CheckSphere(origin, detectionRadius * 0.6f, obstacleLayer))
        {
            if (drawDebug)
                Debug.DrawRay(origin, transform.up * 3f, Color.magenta, 0.2f);
            return true;
        }

        // 2. Speed-adaptive detection distance
        float speed = (float)navigator.cruiseSpeed;
        float adaptiveDist = detectionDistance + speed * speedDetectionFactor;

        // 3. Forward fan detection (horizontal plane only)
        int rays = Mathf.Max(1, forwardRayCount);
        float spread = Mathf.Max(0f, forwardRaySpread);

        // Flatten forward direction to horizontal
        Vector3 flatFwd = fwd;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 0.01f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();

        for (int i = 0; i < rays; i++)
        {
            float t = (rays == 1) ? 0.5f : (float)i / (rays - 1);
            float angle = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * flatFwd;

            if (Physics.SphereCast(origin, detectionRadius, dir, out RaycastHit hit,
                                   adaptiveDist, obstacleLayer))
            {
                if (drawDebug)
                    Debug.DrawLine(origin, hit.point, Color.red, 0.2f);
                return true;
            }
            else if (drawDebug)
            {
                Debug.DrawLine(origin, origin + dir * adaptiveDist, Color.cyan, 0.2f);
            }
        }

        return false;
    }

    void PlanAndApplyDetour()
    {
        if (logInfo)
            Debug.Log($"{_logPrefix} Obstacle detected, planning detour...");

        List<double3> path = navigator.GetPath();
        int currentSeg = navigator.GetCurrentSegmentIndex();

        if (path == null || path.Count < 2 || currentSeg >= path.Count - 1)
        {
            if (logInfo)
                DLog.Warn("General",$"{_logPrefix} Invalid path, cannot plan detour");
            return;
        }

        Vector3 currentPos = transform.position;

        // Find a future rejoin point
        int joinIndex = FindJoinIndex(path, currentSeg, currentPos);
        if (joinIndex <= currentSeg + 1)
        {
            if (logInfo)
                DLog.Warn("General",$"{_logPrefix} No suitable rejoin point found");
            return;
        }

        Vector3 joinWorldPos = LLHToUnity(path[joinIndex]);
        if (logInfo)
            Debug.Log($"{_logPrefix} Rejoin target: index={joinIndex}, " +
                      $"dist={Vector3.Distance(currentPos, joinWorldPos):F0}m");

        // Try A* at current height, then escalate upward
        List<Vector3> detourWorld = null;
        float baseY = currentPos.y;

        for (int h = 0; h <= maxHeightEscalations; h++)
        {
            float tryY = baseY + h * heightEscalationStep;
            detourWorld = ComputeGridPath(currentPos, joinWorldPos, tryY);

            if (detourWorld != null && detourWorld.Count > 0)
            {
                if (h > 0 && logInfo)
                    Debug.Log($"{_logPrefix} A* succeeded at height +{h * heightEscalationStep:F0}m");
                break;
            }
        }

        if (detourWorld == null || detourWorld.Count == 0)
        {
            if (logInfo)
                DLog.Warn("General",$"{_logPrefix} A* failed at all heights, skipping avoidance");
            return;
        }

        // Convert detour to LLH
        List<double3> detourLLH = new List<double3>(detourWorld.Count);
        foreach (var p in detourWorld)
            detourLLH.Add(UnityToLLH(p));

        // Modify the live path
        int removeCount = Mathf.Max(0, joinIndex - (currentSeg + 1));
        if (removeCount > 0)
            path.RemoveRange(currentSeg + 1, removeCount);

        path.InsertRange(currentSeg + 1, detourLLH);

        // Set cooldown
        avoidStartPos = currentPos;
        avoidStartTime = Time.time;
        recentlyAvoided = true;

        if (logInfo)
            Debug.Log($"{_logPrefix} Detour applied: +{detourLLH.Count} points, " +
                      $"-{removeCount} original points");
    }

    int FindJoinIndex(List<double3> path, int currentSeg, Vector3 currentPos)
    {
        int lastIndex = path.Count - 1;
        // Look further ahead in densified path (up to 60 points ≈ 900m at 15m spacing)
        int maxLookAhead = Mathf.Min(lastIndex, currentSeg + 60);

        // Prefer the FURTHEST point with line-of-sight (gives smoother rejoin)
        int bestJoin = -1;
        for (int i = currentSeg + 3; i <= maxLookAhead; i++)
        {
            Vector3 candidatePos = LLHToUnity(path[i]);
            float dist = Vector3.Distance(currentPos, candidatePos);

            // Must be at least 20m away to be a useful rejoin
            if (dist < 20f) continue;

            if (HasLineOfSight(currentPos, candidatePos))
                bestJoin = i;
        }

        if (bestJoin > 0) return bestJoin;

        // Fallback: use the furthest reachable point
        return Mathf.Min(currentSeg + 40, lastIndex);
    }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 1f) return true;

        dir /= dist;
        return !Physics.SphereCast(from, detectionRadius * 0.8f, dir,
                                   out RaycastHit hit, dist, obstacleLayer);
    }

    // ================================================================
    //  A* Grid Pathfinding
    // ================================================================

    class Node
    {
        public int ix, iz;
        public float g, h;
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

        // Cap grid size to prevent excessive memory/time
        gridSize = Mathf.Min(gridSize, 80);

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

                var n = new Node
                {
                    ix = ix, iz = iz,
                    walkable = !blocked,
                    g = float.PositiveInfinity,
                    h = 0f, parent = null
                };

                if (n.walkable) walkableCount++;
                grid[ix, iz] = n;
            }
        }

        int startIx = Mathf.Clamp(Mathf.FloorToInt((startWorld.x - originX) / cell), 0, gridSize - 1);
        int startIz = Mathf.Clamp(Mathf.FloorToInt((startWorld.z - originZ) / cell), 0, gridSize - 1);
        int endIx   = Mathf.Clamp(Mathf.FloorToInt((endWorld.x   - originX) / cell), 0, gridSize - 1);
        int endIz   = Mathf.Clamp(Mathf.FloorToInt((endWorld.z   - originZ) / cell), 0, gridSize - 1);

        Node startNode = FindNearestWalkable(grid, startIx, startIz, gridSize);
        Node endNode   = FindNearestWalkable(grid, endIx,   endIz,   gridSize);

        if (startNode == null || endNode == null)
        {
            if (debugLogSearchDetails && logInfo)
                DLog.Warn("General",$"{_logPrefix} A* no walkable start/end " +
                                 $"(walkable={walkableCount}/{gridSize * gridSize}, y={yHeight:F1})");
            return null;
        }

        // A* with priority via sorted insertion
        var openList = new List<Node>();
        var closedSet = new HashSet<Node>();

        startNode.g = 0f;
        startNode.h = Heuristic(startNode, endNode, cell);
        openList.Add(startNode);

        int[] dirX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dirZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        float[] dirCost = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

        int maxIter = gridSize * gridSize * 2;
        int iter = 0;

        while (openList.Count > 0 && iter < maxIter)
        {
            iter++;

            // Find node with lowest f
            Node current = openList[0];
            int currentIdx = 0;
            for (int i = 1; i < openList.Count; i++)
            {
                if (openList[i].f < current.f)
                {
                    current = openList[i];
                    currentIdx = i;
                }
            }

            openList.RemoveAt(currentIdx);
            closedSet.Add(current);

            if (current == endNode)
            {
                var pathWorld = ReconstructPath(current, originX, originZ, cell, yHeight);

                if (!ValidateDetour(startWorld, endWorld, pathWorld))
                {
                    if (debugLogSearchDetails && logInfo)
                        DLog.Warn("General",$"{_logPrefix} Detour validation failed at y={yHeight:F1}");
                    return null;
                }

                if (debugLogSearchDetails && logInfo)
                    Debug.Log($"{_logPrefix} A* OK: iter={iter}, " +
                              $"walkable={walkableCount}/{gridSize * gridSize}, y={yHeight:F1}");

                return pathWorld;
            }

            for (int d = 0; d < dirX.Length; d++)
            {
                int nx = current.ix + dirX[d];
                int nz = current.iz + dirZ[d];

                if (nx < 0 || nx >= gridSize || nz < 0 || nz >= gridSize) continue;

                Node neighbor = grid[nx, nz];
                if (!neighbor.walkable || closedSet.Contains(neighbor)) continue;

                float tentG = current.g + dirCost[d] * cell;

                if (tentG < neighbor.g)
                {
                    neighbor.parent = current;
                    neighbor.g = tentG;
                    neighbor.h = Heuristic(neighbor, endNode, cell);

                    if (!openList.Contains(neighbor))
                        openList.Add(neighbor);
                }
            }
        }

        if (debugLogSearchDetails && logInfo)
            DLog.Warn("General",$"{_logPrefix} A* failed at y={yHeight:F1}: " +
                             $"iter={iter}, walkable={walkableCount}/{gridSize * gridSize}");

        return null;
    }

    float Heuristic(Node a, Node b, float cell)
    {
        int dx = Mathf.Abs(a.ix - b.ix);
        int dz = Mathf.Abs(a.iz - b.iz);
        int diag = Mathf.Min(dx, dz);
        int straight = dx + dz - 2 * diag;
        return (diag * 1.414f + straight) * cell;
    }

    Node FindNearestWalkable(Node[,] grid, int ix, int iz, int gridSize)
    {
        if (ix >= 0 && ix < gridSize && iz >= 0 && iz < gridSize && grid[ix, iz].walkable)
            return grid[ix, iz];

        int maxR = Mathf.Max(gridSize / 2, 10);
        for (int r = 1; r < maxR; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // Only ring
                    int nx = ix + dx, nz = iz + dz;
                    if (nx < 0 || nx >= gridSize || nz < 0 || nz >= gridSize) continue;
                    if (grid[nx, nz].walkable) return grid[nx, nz];
                }
            }
        }
        return null;
    }

    List<Vector3> ReconstructPath(Node endNode, float originX, float originZ,
                                   float cell, float yHeight)
    {
        var result = new List<Vector3>();
        Node cur = endNode;
        while (cur != null)
        {
            float cx = originX + (cur.ix + 0.5f) * cell;
            float cz = originZ + (cur.iz + 0.5f) * cell;
            result.Insert(0, new Vector3(cx, yHeight, cz));
            cur = cur.parent;
        }
        return SimplifyPath(result);
    }

    List<Vector3> SimplifyPath(List<Vector3> points)
    {
        if (points == null || points.Count <= 2) return points;

        var simplified = new List<Vector3> { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 prev = simplified[simplified.Count - 1];
            Vector3 curr = points[i];
            Vector3 next = points[i + 1];

            float dot = Vector3.Dot((curr - prev).normalized, (next - curr).normalized);
            if (dot < 0.98f) // Keep points where direction changes
                simplified.Add(curr);
        }

        simplified.Add(points[points.Count - 1]);
        return simplified;
    }

    bool ValidateDetour(Vector3 start, Vector3 end, List<Vector3> points)
    {
        if (points == null || points.Count == 0) return false;

        if (SegmentBlocked(start, points[0])) return false;

        for (int i = 0; i < points.Count - 1; i++)
            if (SegmentBlocked(points[i], points[i + 1])) return false;

        if (SegmentBlocked(points[points.Count - 1], end)) return false;

        return true;
    }

    bool SegmentBlocked(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.5f) return false;
        dir /= dist;

        float radius = Mathf.Max(detectionRadius, cellCheckRadius) * 0.9f;
        return Physics.SphereCast(from, radius, dir, out _, dist, obstacleLayer);
    }

    // ================================================================
    //  Coordinate Conversion
    // ================================================================

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

    // ================================================================
    //  Editor Gizmos
    // ================================================================

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!debugDrawGridSlice || !Application.isPlaying) return;
        if (navigator == null || georeference == null) return;

        Vector3 startWorld = transform.position;
        List<double3> path = navigator.GetPath();
        int currentSeg = navigator.GetCurrentSegmentIndex();

        if (path == null || path.Count < 2 || currentSeg >= path.Count - 1) return;

        int joinIndex = FindJoinIndex(path, currentSeg, startWorld);
        joinIndex = Mathf.Clamp(joinIndex, currentSeg + 1, path.Count - 1);

        Vector3 endWorld = LLHToUnity(path[joinIndex]);
        float yHeight = startWorld.y;

        float size = gridHalfSizeMeters;
        float cell = Mathf.Max(0.5f, cellSizeMeters);
        Vector3 mid = (startWorld + endWorld) * 0.5f;
        float originX = mid.x - size;
        float originZ = mid.z - size;
        int gridSize = Mathf.Min(Mathf.Max(4, Mathf.CeilToInt((size * 2f) / cell)), 80);

        Gizmos.color = Color.yellow;
        Vector3 center = new Vector3(originX + size, yHeight, originZ + size);
        Gizmos.DrawWireCube(center, new Vector3(size * 2f, 0.1f, size * 2f));

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(startWorld, cell * 0.7f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(endWorld, cell * 0.7f);
        Gizmos.DrawLine(startWorld, endWorld);

        Color walkColor = new Color(0f, 1f, 0f, 0.25f);
        Color blockColor = new Color(1f, 0f, 0f, 0.35f);
        Vector3 cellVec = new Vector3(cell, 0.05f, cell);

        for (int ix = 0; ix < gridSize; ix++)
        {
            for (int iz = 0; iz < gridSize; iz++)
            {
                float cx = originX + (ix + 0.5f) * cell;
                float cz = originZ + (iz + 0.5f) * cell;
                Vector3 cpos = new Vector3(cx, yHeight, cz);
                bool blocked = Physics.CheckSphere(cpos, cellCheckRadius, obstacleLayer);
                Gizmos.color = blocked ? blockColor : walkColor;
                Gizmos.DrawCube(cpos, cellVec);
            }
        }
    }
#endif
}