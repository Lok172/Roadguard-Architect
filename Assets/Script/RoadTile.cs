using System.Collections.Generic;
using UnityEngine;

//Check comment later
public enum TileType
{
    Straight,
    Curve,
    TJunction,
    Intersection,
    Residential 
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
    StopSign,
    TrafficLight,
    SpeedBump
}

public enum PlacementResult
{
    Success,
    AlreadyOccupied,
    DeviceNotAllowed,
    InvalidTile
}

[RequireComponent(typeof(BoxCollider))]
public class RoadTile : MonoBehaviour
{
    // ── Identity ──────────────────────────────
    [Header("Tile Identity")]
    public TileType tileType = TileType.Straight;
    public ZoneType zoneType = ZoneType.Residential;

    [Tooltip("Human-readable ID, e.g. 'ResRoad_03'")]
    public string tileID = "";

    // ── Accident Rate ─────────────────────────
    [Header("Accident Rate")]
    [Range(0f, 1f)]
    [Tooltip("Base accident probability for this tile (0 = safe, 1 = very dangerous)")]
    public float baseAccidentRate = 0.3f;

    [Tooltip("Current effective accident rate after device modifiers")]
    [HideInInspector] public float currentAccidentRate;

    // ── Device Placement ──────────────────────
    [Header("Device Placement")]
    [Tooltip("Which devices are valid on this tile")]
    public List<TrafficDeviceType> allowedDevices = new List<TrafficDeviceType>();

    [HideInInspector] public bool isOccupied = false;
    [HideInInspector] public TrafficDeviceType placedDeviceType = TrafficDeviceType.None;
    [HideInInspector] public GameObject placedDeviceObject = null;

    // ── Snap Point ────────────────────────────
    [Header("Snap Point")]
    [Tooltip("Where the device will be placed. Defaults to tile centre if null.")]
    public Transform deviceSnapPoint;

    // ── Events ────────────────────────────────
    public System.Action<RoadTile> OnDevicePlaced;
    public System.Action<RoadTile> OnDeviceRemoved;
    public System.Action<RoadTile, float> OnAccidentRateChanged;


    private void Awake()
    {
        currentAccidentRate = baseAccidentRate;

        GetComponent<BoxCollider>().isTrigger = true;
    }


    public PlacementResult CanPlace(TrafficDeviceType device)
    {
        if (isOccupied)
            return PlacementResult.AlreadyOccupied;

        if (!allowedDevices.Contains(device))
            return PlacementResult.DeviceNotAllowed;

        return PlacementResult.Success;
    }

    /// <summary>
    /// Places a device on this tile.
    /// Call this after instantiating the device prefab.
    /// </summary>
    /// <param name="device">The type of device being placed.</param>
    /// <param name="deviceObject">The instantiated device GameObject.</param>
    /// <returns>PlacementResult indicating success or failure reason.</returns>
    public PlacementResult PlaceDevice(TrafficDeviceType device, GameObject deviceObject)
    {
        PlacementResult result = CanPlace(device);
        if (result != PlacementResult.Success)
            return result;

        isOccupied = true;
        placedDeviceType = device;
        placedDeviceObject = deviceObject;

        // Snap the device to the snap point (or tile centre)
        Vector3 snapPos = deviceSnapPoint != null
            ? deviceSnapPoint.position
            : transform.position;

        deviceObject.transform.position = snapPos;
        deviceObject.transform.rotation = transform.rotation;
        deviceObject.transform.SetParent(transform);

        RecalculateAccidentRate();
        OnDevicePlaced?.Invoke(this);

        return PlacementResult.Success;
    }

    /// <summary>
    /// Removes the currently placed device and restores base accident rate.
    /// </summary>
    public void RemoveDevice()
    {
        if (!isOccupied) return;

        if (placedDeviceObject != null)
            Destroy(placedDeviceObject);

        isOccupied = false;
        placedDeviceType = TrafficDeviceType.None;
        placedDeviceObject = null;

        RecalculateAccidentRate();
        OnDeviceRemoved?.Invoke(this);
    }

    /// <summary>
    /// Recalculates currentAccidentRate based on the placed device.
    /// Extend the switch below to tune each device's effect.
    /// </summary>
    public void RecalculateAccidentRate()
    {
        float modifier = GetDeviceModifier(placedDeviceType);
        float previous = currentAccidentRate;
        currentAccidentRate = Mathf.Clamp01(baseAccidentRate * modifier);

        if (!Mathf.Approximately(previous, currentAccidentRate))
            OnAccidentRateChanged?.Invoke(this, currentAccidentRate);

        Debug.Log($"[RoadTile] {tileID} accident rate: {currentAccidentRate:P0}");
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns a multiplier for baseAccidentRate based on device type.
    /// Values below 1.0 reduce the rate; above 1.0 increase it.
    /// </summary>
    private float GetDeviceModifier(TrafficDeviceType device)
    {
        switch (device)
        {
            case TrafficDeviceType.TrafficLight: return 0.40f;
            case TrafficDeviceType.StopSign: return 0.55f;
            case TrafficDeviceType.YieldSign: return 0.65f;
            case TrafficDeviceType.SpeedBump: return 0.60f;
            case TrafficDeviceType.Crosswalk: return 0.70f;
            case TrafficDeviceType.SpeedLimitSign: return 0.80f;
            case TrafficDeviceType.RoundaboutMarker: return 0.50f;
            default: return 1.00f; // No device
        }
    }

    // ─────────────────────────────────────────
    //  GIZMOS (visible in Scene view)
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Colour-code by zone type
        Color gizmoColor = zoneType switch
        {
            ZoneType.Residential => new Color(0.2f, 0.8f, 0.3f, 0.25f),
            ZoneType.Commercial => new Color(0.2f, 0.4f, 0.9f, 0.25f),
            ZoneType.Industrial => new Color(0.9f, 0.6f, 0.1f, 0.25f),
            ZoneType.Highway => new Color(0.9f, 0.2f, 0.2f, 0.25f),
            _ => new Color(0.5f, 0.5f, 0.5f, 0.25f)
        };

        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(col.center, col.size);

        gizmoColor.a = 0.8f;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(col.center, col.size);

        // Draw snap point
        if (deviceSnapPoint != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(deviceSnapPoint.position, 0.15f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Show tile info label in scene view
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.5f,
            $"{tileID}\n{zoneType} | {tileType}\nRate: {baseAccidentRate:P0}"
        );
    }
#endif
}