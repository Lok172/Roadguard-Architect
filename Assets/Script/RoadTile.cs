using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────
//  DEVICE INFO  (cost + effects)
// ─────────────────────────────────────────────────────────────────

public static class DeviceData
{
    public struct DeviceStats
    {
        public float costRM;
        public int accidentReduction;
        public float happinessDeltaGood;
        public float happinessDeltaPoor;
        public bool unsuitableInResidential;
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
                happinessDeltaPoor      = -15f,
                unsuitableInResidential = true
            }
        }
    };

    public static DeviceStats Get(TrafficDeviceType type)
    {
        return _data.TryGetValue(type, out DeviceStats stats) ? stats : default;
    }

    public static float GetCost(TrafficDeviceType type) => Get(type).costRM;
    public static int GetReduction(TrafficDeviceType type) => Get(type).accidentReduction;

    public static bool IsPoorPlacement(TrafficDeviceType device, ZoneType zone)
    {
        DeviceStats stats = Get(device);
        return stats.unsuitableInResidential && zone == ZoneType.Residential;
    }
}

// ─────────────────────────────────────────────────────────────────
//  ROAD TILE
// ─────────────────────────────────────────────────────────────────

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
    [Min(0)]
    public int baseAccidentContribution = 1;

    [HideInInspector] public int currentAccidentContribution;

    // ── Device Placement ──────────────────────
    [Header("Device Placement")]
    public List<TrafficDeviceType> allowedDevices = new List<TrafficDeviceType>();

    [HideInInspector] public bool isOccupied = false;
    [HideInInspector] public TrafficDeviceType placedDeviceType = TrafficDeviceType.None;
    [HideInInspector] public GameObject placedDeviceObject = null;
    [HideInInspector] public bool isPoorPlacement = false;

    // ── Snap Point ────────────────────────────
    [Header("Snap Point")]
    public Transform deviceSnapPoint;

    // ── Grow Effect ───────────────────────────
    [Header("Grow Effect")]
    [Tooltip("Play the grow-in animation when this tile is first enabled.")]
    public bool playGrowOnStart = true;

    [Tooltip("Total duration of the grow animation in seconds.")]
    [Min(0.05f)]
    public float growDuration = 0.4f;

    [Tooltip("How far past 1 the scale overshoots before settling (0 = no bounce).")]
    [Range(0f, 0.5f)]
    public float growOvershoot = 0.12f;

    [Tooltip("Fraction of growDuration spent on the overshoot bounce (0.2 = last 20%).")]
    [Range(0.1f, 0.5f)]
    public float overshootFraction = 0.25f;

    // ── Events ────────────────────────────────
    public System.Action<RoadTile, bool> OnDevicePlaced;
    public System.Action<RoadTile> OnDeviceRemoved;
    public System.Action<RoadTile, int, int> OnContributionChanged;

    // ── Private ───────────────────────────────
    private Vector3 _originalScale;
    private bool _growDone = false;

    // ── Overlay (auto-added) ──────────────────
    // TileOverlay is added automatically in Awake if not already present.
    // PlacementManager and RoadTile both call into it.
    private TileOverlay _overlay;
    public TileOverlay Overlay => _overlay;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        currentAccidentContribution = baseAccidentContribution;
        GetComponent<BoxCollider>().isTrigger = true;
        _originalScale = transform.localScale;

        // Auto-add TileOverlay so tiles always have an overlay
        // without needing a manual Inspector step.
        _overlay = GetComponent<TileOverlay>();
        if (_overlay == null)
            _overlay = gameObject.AddComponent<TileOverlay>();
    }

    private void OnEnable()
    {
        if (playGrowOnStart && !_growDone)
            StartCoroutine(GrowIn());
    }

    // ─────────────────────────────────────────
    //  OVERLAY HELPERS
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the OverlayState this tile should display when the player
    /// is about to place <paramref name="device"/>.
    /// Call with TrafficDeviceType.None to reset to Default.
    /// </summary>
    public OverlayState GetOverlayState(TrafficDeviceType device)
    {
        // No device selected → grey default
        if (device == TrafficDeviceType.None)
            return OverlayState.Default;

        // Already has a device — can't place anything
        if (isOccupied)
            return OverlayState.Occupied;

        // Device not in the allowed list
        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return OverlayState.Hidden;

        // Allowed but wrong zone → orange warning
        if (DeviceData.IsPoorPlacement(device, zoneType))
            return OverlayState.PoorPlacement;

        // All good → green
        return OverlayState.Valid;
    }

    /// <summary>
    /// Refresh the overlay to reflect the tile's current occupied state.
    /// Called after PlaceDevice / RemoveDevice.
    /// Pass the device being dragged (or None to show neutral post-placement state).
    /// </summary>
    public void RefreshOverlay(TrafficDeviceType activeDevice = TrafficDeviceType.None)
    {
        if (_overlay == null) return;
        _overlay.SetState(GetOverlayState(activeDevice));
    }

    // ─────────────────────────────────────────
    //  GROW EFFECT
    // ─────────────────────────────────────────

    public void PlayGrow()
    {
        StopCoroutine(nameof(GrowIn));
        StartCoroutine(GrowIn());
    }

    private IEnumerator GrowIn()
    {
        _growDone = false;
        transform.localScale = Vector3.zero;

        float growUpDuration = growDuration * (1f - overshootFraction);
        float elapsed = 0f;

        while (elapsed < growUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growUpDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float scale = Mathf.Lerp(0f, 1f + growOvershoot, eased);
            transform.localScale = _originalScale * scale;
            yield return null;
        }

        float settleTime = growDuration * overshootFraction;
        elapsed = 0f;

        while (elapsed < settleTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleTime);
            float eased = t < 0.5f
                ? 2f * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
            float scale = Mathf.Lerp(1f + growOvershoot, 1f, eased);
            transform.localScale = _originalScale * scale;
            yield return null;
        }

        transform.localScale = _originalScale;
        _growDone = true;
    }

    // ─────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────

    public PlacementResult CanPlace(TrafficDeviceType device, float playerCapital)
    {
        if (isOccupied)
            return PlacementResult.AlreadyOccupied;

        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return PlacementResult.DeviceNotAllowed;

        if (playerCapital < DeviceData.GetCost(device))
            return PlacementResult.InsufficientFunds;

        if (DeviceData.IsPoorPlacement(device, zoneType))
            return PlacementResult.PoorPlacement;

        return PlacementResult.Success;
    }

    public PlacementResult PlaceDevice(
        TrafficDeviceType device,
        GameObject deviceObject,
        float playerCapital,
        out float happinessDelta,
        out float costSpent)
    {
        happinessDelta = 0f;
        costSpent = 0f;

        PlacementResult result = CanPlace(device, playerCapital);

        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
            return result;

        isPoorPlacement = (result == PlacementResult.PoorPlacement);
        isOccupied = true;
        placedDeviceType = device;
        placedDeviceObject = deviceObject;

        Vector3 snapPos = deviceSnapPoint != null
            ? deviceSnapPoint.position
            : transform.position;

        deviceObject.transform.position = snapPos;
        deviceObject.transform.rotation = transform.rotation;
        deviceObject.transform.SetParent(transform);

        DeviceData.DeviceStats stats = DeviceData.Get(device);
        costSpent = stats.costRM;
        happinessDelta = isPoorPlacement
            ? stats.happinessDeltaPoor
            : stats.happinessDeltaGood;

        RecalculateContribution();
        OnDevicePlaced?.Invoke(this, isPoorPlacement);

        // ── Overlay: tile is now occupied — show red tint, then
        //    reset to Default so it shows grey when no device is active.
        RefreshOverlay(TrafficDeviceType.None);

        Debug.Log($"[RoadTile] {tileID}: placed {device} " +
                  $"(poor={isPoorPlacement}) cost=RM{costSpent} " +
                  $"happiness={happinessDelta:+0;-0} " +
                  $"accidentContrib={currentAccidentContribution}");

        return result;
    }

    public void RemoveDevice()
    {
        if (!isOccupied) return;

        if (placedDeviceObject != null)
            Destroy(placedDeviceObject);

        isOccupied = false;
        placedDeviceType = TrafficDeviceType.None;
        placedDeviceObject = null;
        isPoorPlacement = false;

        RecalculateContribution();
        OnDeviceRemoved?.Invoke(this);

        // ── Overlay: tile is free again — return to grey default
        RefreshOverlay(TrafficDeviceType.None);
    }

    public void RecalculateContribution()
    {
        int prev = currentAccidentContribution;
        int reduction = DeviceData.GetReduction(placedDeviceType);

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
            ZoneType.Residential => new Color(0.2f, 0.8f, 0.3f, 0.25f),
            ZoneType.Commercial => new Color(0.2f, 0.4f, 0.9f, 0.25f),
            ZoneType.Industrial => new Color(0.9f, 0.6f, 0.1f, 0.25f),
            ZoneType.Highway => new Color(0.9f, 0.2f, 0.2f, 0.25f),
            _ => new Color(0.5f, 0.5f, 0.5f, 0.25f)
        };

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(col.center, col.size);

        gizmoColor.a = 0.8f;
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(col.center, col.size);

        if (isPoorPlacement)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(col.center, col.size + Vector3.one * 0.05f);
        }

        if (deviceSnapPoint != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
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