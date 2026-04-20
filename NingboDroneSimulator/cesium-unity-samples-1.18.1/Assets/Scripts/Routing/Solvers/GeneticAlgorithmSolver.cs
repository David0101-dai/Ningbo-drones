using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class GeneticAlgorithmSolver : IRoutingSolver
{
    // ══════════════════════════════════════════════
    // 这两个属性会显示在 UI 下拉菜单里
    // ══════════════════════════════════════════════
    public string Name => "Genetic Algorithm";
    public string Description => "GA-based VRPTW solver with crossover and mutation operators.";

    // ══════════════════════════════════════════════
    // 这是你唯一需要写的方法
    // ══════════════════════════════════════════════
    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        // ─── 你能用到的所有输入数据 ───
        //
        // ctx.Orders          订单列表，每个订单有：
        //   .orderId           订单ID (string)
        //   .customerName      客户名 如 "C001"
        //   .destination       目的地 WGS84坐标 (lon, lat, alt)
        //   .demand            需求量 (int)
        //   .twStart           时间窗开始 (float, 秒)
        //   .twEnd             时间窗结束 (float, 秒)
        //
        // ctx.Drones           无人机列表，每个有：
        //   .droneId           无人机ID 如 "V01"
        //   .capacity          载重上限
        //
        // ctx.DepotPosition    仓库坐标 double3(lon, lat, alt)
        // ctx.VehicleCount     无人机总数 (int)
        // ctx.Capacity         单机载重上限 (int)
        // ctx.SpeedMps         规划速度 m/s (float)
        //
        // ─── 辅助函数 ───
        // ctx.Distance(i, j)   第i个点到第j个点的距离(米)
        //                      index 0 = 仓库, 1~N = 客户
        // ctx.TravelTime(i, j) 飞行时间(秒) = Distance / SpeedMps

        var routes = new List<PlannedRoute>();

        // ╔═══════════════════════════════════════╗
        // ║  在这里写你的遗传算法                    ║
        // ║  最终把结果填入 routes 列表              ║
        // ╚═══════════════════════════════════════╝

        // ─── 示例：构建一条路线 ───
        //
        // var stops = new List<RouteStop>();
        //
        // stops.Add(new RouteStop     // 第一站
        // {
        //     customerName = "C001",
        //     position = order.destination,
        //     demand = order.demand,
        //     twStart = order.twStart,
        //     twEnd = order.twEnd,
        //     plannedArrival = 计算出的到达时间,
        //     isLate = 到达时间 > order.twEnd
        // });
        //
        // routes.Add(new PlannedRoute
        // {
        //     droneId = "V01",
        //     stops = stops,
        //     totalDemand = stops.Sum(s => s.demand),
        //     totalDistance = 计算出的总距离,
        //     totalTime = 计算出的总时间
        // });

        // ─── 你的算法逻辑写在这里 ───
        // TODO: 实现遗传算法

        DLog.Info("GA", $"Solved: {routes.Count} routes");
        return routes;
    }
}