// Assets/Scripts/Drone/DroneSpec.cs
using UnityEngine;

/// <summary>
/// Physical specifications for a drone type.
/// Attach to the drone prefab or drone GameObject.
/// </summary>
public class DroneSpec : MonoBehaviour
{
    [Header("=== Capacity ===")]
    [Tooltip("Maximum cargo capacity per trip")]
    public int maxCapacity = 200;

    [Tooltip("Current cargo load")]
    public int currentLoad = 0;

    [Header("=== Speed ===")]
    [Tooltip("Minimum cruise speed (m/s)")]
    public double minSpeed = 5.0;

    [Tooltip("Maximum cruise speed (m/s)")]
    public double maxSpeed = 30.0;

    [Tooltip("Default cruise speed (m/s)")]
    public double defaultSpeed = 15.0;

    [Header("=== Battery ===")]
    [Tooltip("Maximum battery capacity (Wh)")]
    public float maxBatteryWh = 500f;

    [Tooltip("Current battery level (Wh)")]
    public float currentBatteryWh = 500f;

    [Tooltip("Base power consumption (W) at idle/hover")]
    public float basePowerW = 50f;

    [Tooltip("Additional power per m/s of speed (W per m/s)")]
    public float speedPowerFactor = 8f;

    [Tooltip("Additional power per unit of cargo (W per unit)")]
    public float cargoPowerFactor = 0.5f;

    [Tooltip("Battery level below which drone must return to charge (%)")]
    [Range(5f, 30f)]
    public float lowBatteryThresholdPercent = 15f;

    [Tooltip("Charging rate (Wh per second)")]
    public float chargeRateWhPerSec = 50f;

    [Header("=== State ===")]
    public BatteryState batteryState = BatteryState.Normal;

    public enum BatteryState
    {
        Normal,     // Battery OK
        Low,        // Below threshold, should return
        Critical,   // Almost empty, must land
        Charging    // At charging station
    }

    // ================================================================
    //  Properties
    // ================================================================

    /// <summary>Battery percentage 0-100</summary>
    public float BatteryPercent => maxBatteryWh > 0 ? (currentBatteryWh / maxBatteryWh) * 100f : 0f;

    /// <summary>Remaining capacity for cargo</summary>
    public int RemainingCapacity => maxCapacity - currentLoad;

    /// <summary>Is there room for more cargo?</summary>
    public bool HasCapacity(int amount) => currentLoad + amount <= maxCapacity;

    /// <summary>Is battery below low threshold?</summary>
    public bool IsLowBattery => BatteryPercent <= lowBatteryThresholdPercent;

    /// <summary>Is battery critically low (below 5%)?</summary>
    public bool IsCriticalBattery => BatteryPercent <= 5f;

    // ================================================================
    //  Power Consumption Model
    // ================================================================

    /// <summary>
    /// Calculate current power consumption in Watts
    /// based on speed and cargo load.
    /// </summary>
    public float GetCurrentPowerW(double speedMps)
    {
        float power = basePowerW;
        power += speedPowerFactor * (float)speedMps;
        power += cargoPowerFactor * currentLoad;
        return power;
    }

    /// <summary>
    /// Estimate range remaining in meters at current speed and load
    /// </summary>
    public float EstimateRangeMeters(double speedMps)
    {
        float powerW = GetCurrentPowerW(speedMps);
        if (powerW <= 0) return float.MaxValue;

        float hoursRemaining = currentBatteryWh / powerW;
        return (float)(hoursRemaining * 3600.0 * speedMps);
    }

    /// <summary>
    /// Estimate flight time remaining in seconds at current speed and load
    /// </summary>
    public float EstimateFlightTimeSeconds(double speedMps)
    {
        float powerW = GetCurrentPowerW(speedMps);
        if (powerW <= 0) return float.MaxValue;

        return (currentBatteryWh / powerW) * 3600f;
    }

    // ================================================================
    //  Battery Operations
    // ================================================================

    /// <summary>
    /// Consume battery for one frame. Call from Update/LateUpdate.
    /// Returns false if battery is depleted.
    /// </summary>
    public bool ConsumeBattery(double speedMps, float deltaTime)
    {
        if (batteryState == BatteryState.Charging) return true;

        float powerW = GetCurrentPowerW(speedMps);
        float whConsumed = powerW * (deltaTime / 3600f);
        currentBatteryWh = Mathf.Max(0f, currentBatteryWh - whConsumed);

        // Update state
        if (IsCriticalBattery)
            batteryState = BatteryState.Critical;
        else if (IsLowBattery)
            batteryState = BatteryState.Low;
        else
            batteryState = BatteryState.Normal;

        return currentBatteryWh > 0;
    }

    /// <summary>
    /// Charge battery for one frame. Returns true when fully charged.
    /// </summary>
    public bool ChargeBattery(float deltaTime)
    {
        batteryState = BatteryState.Charging;
        currentBatteryWh = Mathf.Min(maxBatteryWh, currentBatteryWh + chargeRateWhPerSec * deltaTime);

        if (currentBatteryWh >= maxBatteryWh)
        {
            batteryState = BatteryState.Normal;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Load cargo onto drone. Returns actual amount loaded.
    /// </summary>
    public int LoadCargo(int amount)
    {
        int canLoad = Mathf.Min(amount, RemainingCapacity);
        currentLoad += canLoad;
        return canLoad;
    }

    /// <summary>
    /// Unload all cargo.
    /// </summary>
    public void UnloadCargo()
    {
        currentLoad = 0;
    }

    /// <summary>
    /// Reset to full battery and empty cargo
    /// </summary>
    public void ResetFull()
    {
        currentBatteryWh = maxBatteryWh;
        currentLoad = 0;
        batteryState = BatteryState.Normal;
    }
}