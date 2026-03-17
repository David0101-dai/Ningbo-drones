// Assets/Scripts/Mission/DeliveryOrder.cs
using Unity.Mathematics;

[System.Serializable]
public class DeliveryOrder
{
    // ====== Identity ======
    public string orderId;
    public string description;
    public int customerNumber;          // Solomon CUST NO

    // ====== Locations ======
    public double3 pickupLLH;
    public double3 deliveryLLH;
    public string pickupPointName;
    public string deliveryPointName;

    // ====== Demand / Capacity ======
    public int demand;                  // Solomon DEMAND (货物量)
    public int remainingDemand;         // 剩余未分配的量

    // ====== Time Windows ======
    public float readyTime;             // 最早服务时间 (模拟时间)
    public float dueTime;               // 最晚到达时间 (模拟时间)
    public float serviceTime;           // 在该点的停留/卸货时间

    // ====== Status ======
    public OrderStatus status;
    public string assignedDrone;
    public float createdTime;           // Unity Time.time when created
    public float assignedTime;
    public float pickedUpTime;
    public float completedTime;

    // ====== Split Order Tracking ======
    public string parentOrderId;        // 如果是拆分订单，记录父订单ID
    public int splitIndex;              // 拆分序号 (0=原始, 1,2...=子订单)
    public int totalSplits;             // 总共拆了几份

    public enum OrderStatus
    {
        Pending,        // 等待分配
        Scheduled,      // 已排入计划但未开始（时间窗口未到）
        PickingUp,      // 无人机正在前往取货
        Delivering,     // 无人机正在送货
        Completed,      // 已完成
        Failed          // 失败
    }

    // ====== Constructor: Simple (backward compatible) ======
    public DeliveryOrder(string id, double3 pickup, double3 delivery, string desc = "")
    {
        orderId = id;
        description = desc;
        pickupLLH = pickup;
        deliveryLLH = delivery;
        pickupPointName = "";
        deliveryPointName = "";
        demand = 1;
        remainingDemand = 1;
        readyTime = 0f;
        dueTime = float.MaxValue;
        serviceTime = 0f;
        status = OrderStatus.Pending;
        assignedDrone = "";
        createdTime = UnityEngine.Time.time;
        assignedTime = 0f;
        pickedUpTime = 0f;
        completedTime = 0f;
        parentOrderId = "";
        splitIndex = 0;
        totalSplits = 1;
        customerNumber = -1;
    }

    // ====== Constructor: Full (Solomon data) ======
    public DeliveryOrder(string id, double3 pickup, double3 delivery,
        int demandAmount, float ready, float due, float service, string desc = "")
    {
        orderId = id;
        description = desc;
        pickupLLH = pickup;
        deliveryLLH = delivery;
        pickupPointName = "";
        deliveryPointName = "";
        demand = demandAmount;
        remainingDemand = demandAmount;
        readyTime = ready;
        dueTime = due;
        serviceTime = service;
        status = OrderStatus.Pending;
        assignedDrone = "";
        createdTime = UnityEngine.Time.time;
        assignedTime = 0f;
        pickedUpTime = 0f;
        completedTime = 0f;
        parentOrderId = "";
        splitIndex = 0;
        totalSplits = 1;
        customerNumber = -1;
    }

    // ====== Helpers ======

    /// <summary>
    /// Duration from creation to completion (0 if not completed)
    /// </summary>
    public float GetDuration()
    {
        if (completedTime <= 0f) return 0f;
        return completedTime - createdTime;
    }

    /// <summary>
    /// Is this a split sub-order?
    /// </summary>
    public bool IsSplit => !string.IsNullOrEmpty(parentOrderId);

    /// <summary>
    /// Is time window still valid at given simulation time?
    /// </summary>
    public bool IsWithinTimeWindow(float simTime)
    {
        return simTime >= readyTime && simTime <= dueTime;
    }

    /// <summary>
    /// Is the order ready to be serviced at given simulation time?
    /// </summary>
    public bool IsReady(float simTime)
    {
        return simTime >= readyTime;
    }

    /// <summary>
    /// Has the time window expired?
    /// </summary>
    public bool IsExpired(float simTime)
    {
        return simTime > dueTime;
    }

    public override string ToString()
    {
        return $"{orderId}: {description} [D={demand}, {readyTime:F0}-{dueTime:F0}] {status}";
    }
}