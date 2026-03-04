// Assets/Scripts/Mission/DeliveryOrder.cs
using Unity.Mathematics;

[System.Serializable]
public class DeliveryOrder
{
    public string orderId;
    public string description;
    public double3 pickupLLH;
    public double3 deliveryLLH;
    public string pickupPointName;
    public string deliveryPointName;
    public OrderStatus status;
    public string assignedDrone;
    public float createdTime;
    public float completedTime;

    public enum OrderStatus
    {
        Pending,
        PickingUp,
        Delivering,
        Completed,
        Failed
    }

    public DeliveryOrder(string id, double3 pickup, double3 delivery, string desc = "")
    {
        orderId = id;
        description = desc;
        pickupLLH = pickup;
        deliveryLLH = delivery;
        pickupPointName = "";
        deliveryPointName = "";
        status = OrderStatus.Pending;
        assignedDrone = "";
        createdTime = UnityEngine.Time.time;
        completedTime = 0f;
    }
}