using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using CesiumForUnity;

[RequireComponent(typeof(DroneGeoNavigator))]
public class DroneObstacleAvoidance : MonoBehaviour
{
    [Header("前向探测")]
    [Tooltip("前方最大探测距离")]
    public float detectionDistance = 25f;

    [Tooltip("用来模拟无人机体积的球半径")]
    public float detectionRadius = 1.5f;

    [Tooltip("障碍物层（建筑物等）")]
    public LayerMask obstacleLayer;

    [Tooltip("前向扇形发射的射线数量")]
    public int forwardRayCount = 5;

    [Tooltip("前向扇形总角度（度），左右各一半")]
    public float forwardRaySpread = 40f;


    [Header("候选避障点")]
    [Tooltip("左右各采样多少个角度")]
    public int lateralSamplesPerSide = 5;

    [Tooltip("相邻候选角度间隔（度）")]
    public float lateralAngleStep = 10f;

    [Tooltip("新航点距离当前无人机多远（米）")]
    public float candidateDistance = 20f;

    [Tooltip("验证安全的球半径")]
    public float safetyRadius = 2f;


    [Header("行为控制")]
    [Tooltip("通过避障点后，至少飞出多少米才重新允许下一次避障")]
    public float minDistanceAfterAvoid = 10f;

    [Tooltip("打印调试日志")]
    public bool logInfo = true;

    [Tooltip("绘制调试射线")]
    public bool drawDebug = true;


    private DroneGeoNavigator navigator;
    private CesiumGeoreference georeference;

    // 避障冷却：刚完成一次避障后，等飞离一定距离再检测
    private bool recentlyAvoided = false;
    private Vector3 avoidStartPos;

    // 记录是我们自己触发的紧急停止
    private bool stoppedByThisScript = false;


    void Awake()
    {
        navigator = GetComponent<DroneGeoNavigator>();
        if (navigator != null)
            georeference = navigator.georeference;
    }

    void Update()
    {
        if (navigator == null || georeference == null)
            return;

        // 避障之后的冷却：先飞离一定距离再重新开启检测
        if (recentlyAvoided)
        {
            float dist = Vector3.Distance(avoidStartPos, transform.position);
            if (dist >= minDistanceAfterAvoid)
            {
                recentlyAvoided = false;
                if (logInfo)
                    Debug.Log("[Avoid] 已离开避障点足够距离，重新开启检测");
            }
            else
            {
                // 冷却期间不再触发新避障，避免刚绕过去就又被拉手刹
                return;
            }
        }

        // 如果当前不是我们自己触发的停车，则可以检测障碍并尝试避障
        if (!stoppedByThisScript)
        {
            if (!ObstacleAhead())
                return;

            if (logInfo)
                Debug.Log("[Avoid] 前方检测到障碍，开始规划避障航点");

            stoppedByThisScript = true;
            navigator.SetEmergencyStop(true);

            bool planned = PlanAndInsertAvoidWaypoint(out Vector3 bestPos);

            if (planned)
            {
                avoidStartPos = transform.position;
                recentlyAvoided = true;

                if (logInfo)
                    Debug.Log($"[Avoid] 成功插入避障点，位置：{bestPos}");

                if (drawDebug)
                    Debug.DrawLine(transform.position, bestPos, Color.green, 3f);

                // 插入成功，解除刹车，让导航器按新路径继续飞
                stoppedByThisScript = false;
                navigator.SetEmergencyStop(false);
            }
            else
            {
                // 没找到合适点，至少不要一直死停在原地
                if (logInfo)
                    Debug.LogWarning("[Avoid] 未找到安全避障点，本次不插入新航点，继续沿原路径前进");

                stoppedByThisScript = false;
                navigator.SetEmergencyStop(false);
            }
        }
    }

    //================ 前方障碍检测 ================
    bool ObstacleAhead()
    {
        Vector3 origin = transform.position;
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

    //================ 避障航点规划 ================
    bool PlanAndInsertAvoidWaypoint(out Vector3 bestWaypointPos)
    {
        bestWaypointPos = Vector3.zero;

        List<double3> path = navigator.GetPath();
        int currentSeg = navigator.GetCurrentSegmentIndex();

        if (path == null || path.Count < 2 || currentSeg >= path.Count - 1)
        {
            if (logInfo)
                Debug.LogWarning("[Avoid] 路径信息无效，无法插入避障点");
            return false;
        }

        Vector3 currentPos = transform.position;
        Vector3 originalNextPos = navigator.LLHToUnity(path[currentSeg + 1]);

        Vector3 baseDir = (originalNextPos - currentPos).normalized;
        if (baseDir.sqrMagnitude < 1e-4f)
            baseDir = transform.forward;

        // 生成候选点并评估
        List<Candidate> candidates = GenerateCandidates(currentPos, baseDir, originalNextPos);

        // 选一个代价最小且 cost 非无穷大的候选
        Candidate best = default;
        float bestCost = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (float.IsInfinity(c.cost))
                continue;

            if (!found || c.cost < bestCost)
            {
                found = true;
                best = c;
                bestCost = c.cost;
            }
        }

        if (!found)
        {
            if (logInfo)
                Debug.LogWarning("[Avoid] 没有找到完全安全的候选点");
            return false;
        }

        // 将选中的候选点插入
        // 将选中的候选点插入到路径中（当前段之后）
        double3 llh = UnityToLLH(best.position);
        path.Insert(currentSeg + 1, llh);

        bestWaypointPos = best.position;
        return true;
    }

    // 生成候选点列表：0° 正前方，然后左右 ±step、±2step ...
    List<Candidate> GenerateCandidates(Vector3 origin,
                                       Vector3 baseDir,
                                       Vector3 originalNextPos)
    {
        List<Candidate> list = new List<Candidate>();

        // 0°（正前方）
        AddCandidate(list, origin, baseDir, 0f, originalNextPos);

        // 左右扩散：+angle / -angle
        for (int i = 1; i <= lateralSamplesPerSide; i++)
        {
            float angle = lateralAngleStep * i;

            // 右侧
            AddCandidate(list, origin, baseDir, angle, originalNextPos);

            // 左侧
            AddCandidate(list, origin, baseDir, -angle, originalNextPos);
        }

        return list;
    }

    // 向列表里加入一个候选点，并在这里就算好它的 cost
    void AddCandidate(List<Candidate> list,
                      Vector3 origin,
                      Vector3 baseDir,
                      float yawAngle,
                      Vector3 originalNextPos)
    {
        Vector3 dir = Quaternion.Euler(0f, yawAngle, 0f) * baseDir;
        Vector3 pos = origin + dir * candidateDistance;

        float cost;
        bool ok = EvaluateCandidate(origin, pos, originalNextPos, Mathf.Abs(yawAngle), out cost);

        Candidate c = new Candidate
        {
            position = pos,
            yawFromForward = Mathf.Abs(yawAngle),
            cost = ok ? cost : float.PositiveInfinity
        };

        list.Add(c);
    }

    // 对一个候选点做安全检测，并计算“代价”（越小越好）
    bool EvaluateCandidate(Vector3 start,
                           Vector3 candPos,
                           Vector3 originalNextPos,
                           float yawFromForward,
                           out float cost)
    {
        cost = float.PositiveInfinity;

        Vector3 dir = candPos - start;
        float dist = dir.magnitude;
        if (dist < 0.5f)
            return false;

        dir /= dist;

        // 1) 当前点 -> 候选点 是否有碰撞
        if (Physics.SphereCast(start, safetyRadius, dir, out RaycastHit hit, dist, obstacleLayer))
        {
            if (drawDebug)
                Debug.DrawLine(start, hit.point, Color.magenta, 1f);
            return false;
        }

        // 2) 候选点附近是否有障碍
        if (Physics.CheckSphere(candPos, safetyRadius, obstacleLayer))
        {
            return false;
        }

        // 3) 候选点 -> 原下一个航点 是否安全（避免刚绕出来马上又撞）
        Vector3 dirBack = originalNextPos - candPos;
        float distBack = dirBack.magnitude;
        if (distBack > 1f)
        {
            dirBack /= distBack;
            if (Physics.SphereCast(candPos, safetyRadius, dirBack, out RaycastHit hit2,
                                   distBack, obstacleLayer))
            {
                if (drawDebug)
                    Debug.DrawLine(candPos, hit2.point, Color.yellow, 1f);
                return false;
            }
        }

        // 4) 计算代价：
        //    - 与“原下一个点方向”的偏离越小越好
        //    - 自身偏转角 yawFromForward 越小越好
        //    - 距离越短越好
        Vector3 dirToOriginalNext = (originalNextPos - start).normalized;
        float deviationAngle = Vector3.Angle(dir, dirToOriginalNext); // 相对原路径偏离

        float angleCost = deviationAngle * 0.7f;
        float yawCost = yawFromForward * 0.3f;
        float distanceCost = dist * 0.05f;

        cost = angleCost + yawCost + distanceCost;
        return true;
    }

    double3 UnityToLLH(Vector3 unityPos)
    {
        double3 u = new double3(unityPos.x, unityPos.y, unityPos.z);
        double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(u);
        return CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
    }

    struct Candidate
    {
        public Vector3 position;      // 世界坐标
        public float yawFromForward;  // 相对当前前进方向的偏转角（绝对值）
        public float cost;            // 代价（越小越好）
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectionDistance);
    }
}