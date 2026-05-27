using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────

public enum TileType
{
    Straight,
    Curve,
    TJunction,
    Intersection,
    Residential     // Single-lane, narrow
}

public enum ZoneType
{
    Residential,
    Commercial,
    Industrial,
    Highway
}

public enum TrafficDeviceType
{
    None,
    StopSign,       // RM 250  — mild accident reduction, good for residential
    TrafficLight,   // RM 2500 — strong reduction, but causes jam if residential
    SpeedBump       // RM 350  — moderate reduction, best for residential
}

public enum PlacementResult
{
    Success,            // Device placed correctly
    AlreadyOccupied,    // Tile already has a device
    DeviceNotAllowed,   // Device not in allowedDevices list
    InsufficientFunds,  // Player cannot afford the device
    PoorPlacement       // Device allowed but wrong zone — placed with happiness penalty
}

// ─────────────────────────────────────────────
//  DEVICE INFO  (cost + effects)
// ─────────────────────────────────────────────

/// <summary>
/// Static data for each traffic device — cost, accident reduction,
/// happiness delta per zone, and whether it's suitable for a zone.
/// Edit these values here to tune game balance.
/// </summary>
public static class DeviceData
{
    public struct DeviceStats
    {
        public float  costRM;               // Purchase cost in Ringgit
        public int    accidentReduction;    // Flat accident rate points removed
        public float  happinessDeltaGood;   // Happiness change for good placement
        public float  happinessDeltaPoor;   // Happiness change for poor placement
        public bool   unsuitableInResidential; // Traffic jam risk in residential
    }

    private static readonly Dictionary<TrafficDeviceType, DeviceStats> _data =
        new Dictionary<TrafficDeviceType, DeviceStats>
    {
        {
            TrafficDeviceType.StopSign, new DeviceStats
            {
                costRM                  = 250f,
                accidentReduction       = 2,
                happinessDeltaGood      = 5f,
                happinessDeltaPoor      = -3f,
                unsuitableInResidential = false
            }
        },
        {
            TrafficDeviceType.SpeedBump, new DeviceStats
            {
                costRM                  = 350f,
                accidentReduction       = 3,
                happinessDeltaGood      = 7f,
                happinessDeltaPoor      = -2f,
                unsuitableInResidential = false
            }
        },
        {
            TrafficDeviceType.TrafficLight, new DeviceStats
            {
                costRM                  = 2500f,
                accidentReduction       = 5,
                happinessDeltaGood      = 10f,
                happinessDeltaPoor      = -15f,  // causes jam → big happiness hit
                unsuitableInResidential = true   // poor placement in residential
            }
        }
    };

    public static DeviceStats Get(TrafficDeviceType type)
    {
        return _data.TryGetValue(type, out DeviceStats stats)
            ? stats
            : default;
    }

    public static float GetCost(TrafficDeviceType type)   => Get(type).costRM;
    public static int   GetReduction(TrafficDeviceType type) => Get(type).accidentReduction;

    /// <summary>
    /// Returns true if the device is a poor fit for the given zone.
    /// Currently: TrafficLight in Residential = poor placement.
    /// </summary>
    public static bool IsPoorPlacement(TrafficDeviceType device, ZoneType zone)
    {
        DeviceStats stats = Get(device);
        return stats.unsuitableInResidential && zone == ZoneType.Residential;
    }
}

// ─────────────────────────────────────────────
//  ROAD TILE
// ─────────────────────────────────────────────

/// <summary>
/// Attach to each invisible tile collider GameObject.
/// Tracks tile metadata, device placement, and contributes to
/// the global AccidentRate and Happiness via events.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RoadTile : MonoBehaviour
{
    // ── Identity ──────────────────────────────
    [Header("Tile Identity")]
    public TileType tileType = TileType.Straight;
    public ZoneType zoneType = ZoneType.Residential;

    [Tooltip("Human-readable ID, e.g. 'ResRoad_03'")]
    public string tileID = "";

    // ── Accident Contribution ─────────────────
    [Header("Accident Rate Contribution")]
    [Tooltip("How many accident-rate points this tile contributes at baseline. " +
             "The GameManager sums all tiles to get the city-wide rate.")]
    [Min(0)]
    public int baseAccidentContribution = 1;

    /// <summary>Current contribution after device modifier is applied.</summary>
    [HideInInspector] public int currentAccidentContribution;

    // ── Device Placement ──────────────────────
    [Header("Device Placement")]
    [Tooltip("Devices valid on this tile. Leave empty to allow all.")]
    public List<TrafficDeviceType> allowedDevices = new List<TrafficDeviceType>();

    [HideInInspector] public bool isOccupied             = false;
    [HideInInspector] public TrafficDeviceType placedDeviceType = TrafficDeviceType.None;
    [HideInInspector] public GameObject placedDeviceObject = null;
    [HideInInspector] public bool isPoorPlacement         = false;

    // ── Snap Point ────────────────────────────
    [Header("Snap Point")]
    [Tooltip("Child Transform where the device prefab will be positioned. " +
             "Defaults to tile centre if null.")]
    public Transform deviceSnapPoint;

    // ── Events ────────────────────────────────
    /// <summary>Fired after a device is placed. bool = isPoorPlacement.</summary>
    public System.Action<RoadTile, bool> OnDevicePlaced;
    public System.Action<RoadTile>       OnDeviceRemoved;

    /// <summary>
    /// Fired when this tile's accident contribution changes.
    /// GameManager listens to all tiles and recalculates city total.
    /// </summary>
    public System.Action<RoadTile, int, int> OnContributionChanged; // tile, old, new

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        currentAccidentContribution = baseAccidentContribution;
        GetComponent<BoxCollider>().isTrigger = true;
    }

    // ─────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────

    /// <summary>
    /// Checks whether the device can be placed. Does NOT spend money.
    /// Call this to validate before showing a placement preview.
    /// </summary>
    public PlacementResult CanPlace(TrafficDeviceType device, float playerCapital)
    {
        if (isOccupied)
            return PlacementResult.AlreadyOccupied;

        // If allowedDevices is non-empty, enforce the whitelist
        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return PlacementResult.DeviceNotAllowed;

        if (playerCapital < DeviceData.GetCost(device))
            return PlacementResult.InsufficientFunds;

        // PoorPlacement is still allowed but triggers happiness penalty
        if (DeviceData.IsPoorPlacement(device, zoneType))
            return PlacementResult.PoorPlacement;

        return PlacementResult.Success;
    }

    /// <summary>
    /// Places the device on this tile. Deducts cost and applies effects
    /// via the returned happiness delta — caller (GameManager) applies it.
    /// </summary>
    /// <param name="device">Device type to place.</param>
    /// <param name="deviceObject">Already-instantiated device prefab.</param>
    /// <param name="playerCapital">Player's current RM balance.</param>
    /// <param name="happinessDelta">OUT: happiness change to apply.</param>
    /// <param name="costSpent">OUT: RM deducted.</param>
    /// <returns>PlacementResult — check for Success or PoorPlacement.</returns>
    public PlacementResult PlaceDevice(
        TrafficDeviceType device,
        GameObject        deviceObject,
        float             playerCapital,
        out float         happinessDelta,
        out float         costSpent)
    {
        happinessDelta = 0f;
        costSpent      = 0f;

        PlacementResult result = CanPlace(device, playerCapital);

        // Only reject on hard failures
        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
        {
            return result;
        }

        // ── Commit placement ──────────────────
        isPoorPlacement    = (result == PlacementResult.PoorPlacement);
        isOccupied         = true;
        placedDeviceType   = device;
        placedDeviceObject = deviceObject;

        // Snap device into position
        Vector3 snapPos = deviceSnapPoint != null
            ? deviceSnapPoint.position
            : transform.position;

        deviceObject.transform.position = snapPos;
        deviceObject.transform.rotation = transform.rotation;
        deviceObject.transform.SetParent(transform);

        // ── Costs & happiness ─────────────────
        DeviceData.DeviceStats stats = DeviceData.Get(device);
        costSpent      = stats.costRM;
        happinessDelta = isPoorPlacement
            ? stats.happinessDeltaPoor
            : stats.happinessDeltaGood;

        // ── Accident contribution ─────────────
        RecalculateContribution();

        OnDevicePlaced?.Invoke(this, isPoorPlacement);

        Debug.Log($"[RoadTile] {tileID}: placed {device} " +
                  $"(poor={isPoorPlacement}) cost=RM{costSpent} " +
                  $"happiness={happinessDelta:+0;-0} " +
                  $"accidentContrib={currentAccidentContribution}");

        return result;
    }

    /// <summary>
    /// Removes the device, restores accident contribution.
    /// Does NOT refund money (design decision — change if needed).
    /// </summary>
    public void RemoveDevice()
    {
        if (!isOccupied) return;

        if (placedDeviceObject != null)
            Destroy(placedDeviceObject);

        isOccupied         = false;
        placedDeviceType   = TrafficDeviceType.None;
        placedDeviceObject = null;
        isPoorPlacement    = false;

        RecalculateContribution();
        OnDeviceRemoved?.Invoke(this);
    }

    /// <summary>
    /// Recalculates this tile's accident contribution.
    /// Subtracts the device's flat reduction, clamped to 0.
    /// GameManager should sum all tiles after calling this.
    /// </summary>
    public void RecalculateContribution()
    {
        int prev      = currentAccidentContribution;
        int reduction = DeviceData.GetReduction(placedDeviceType);

        // Poor placement = half effectiveness
        if (isPoorPlacement)
            reduction = Mathf.FloorToInt(reduction * 0.5f);

        currentAccidentContribution =
            Mathf.Max(0, baseAccidentContribution - reduction);

        if (prev != currentAccidentContribution)
            OnContributionChanged?.Invoke(this, prev, currentAccidentContribution);
    }

    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Color gizmoColor = zoneType switch
        {
            ZoneType.Residential => new Color(0.2f, 0.8f, 0.3f, 0.25f),  // green
            ZoneType.Commercial  => new Color(0.2f, 0.4f, 0.9f, 0.25f),  // blue
            ZoneType.Industrial  => new Color(0.9f, 0.6f, 0.1f, 0.25f),  // orange
            ZoneType.Highway     => new Color(0.9f, 0.2f, 0.2f, 0.25f),  // red
            _                    => new Color(0.5f, 0.5f, 0.5f, 0.25f)   // grey
        };

        // Draw filled ghost
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color  = gizmoColor;
        Gizmos.DrawCube(col.center, col.size);

        // Draw wire outline (more opaque)
        gizmoColor.a  = 0.8f;
        Gizmos.color  = gizmoColor;
        Gizmos.DrawWireCube(col.center, col.size);

        // Poor placement indicator — bright red outline
        if (isPoorPlacement)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(col.center, col.size + Vector3.one * 0.05f);
        }

        // Snap point sphere
        if (deviceSnapPoint != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color  = Color.yellow;
            Gizmos.DrawSphere(deviceSnapPoint.position, 0.15f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        string deviceInfo = isOccupied
            ? $"{placedDeviceType}{(isPoorPlacement ? " ⚠ POOR" : " ✓")}"
            : "No device";

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.6f,
            $"{tileID}\n{zoneType} | {tileType}\n" +
            $"Contribution: {currentAccidentContribution}\n" +
            $"Device: {deviceInfo}"
        );
    }
#endif
}
