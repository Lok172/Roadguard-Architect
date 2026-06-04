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
    InsufficientFunds,
    PoorPlacement
}

// ─────────────────────────────────────────────────────────────────
//  TILE CORNERS
//  Local-space corners in the tile's own axes.
//  +Z = road forward, +X = road right (driver's POV on +Z lane).
//
//  Layout (looking down):
//
//        +Z (road forward)
//          ▲
//   NW ────┼──── NE
//    │     │      │
//   ─┼─────●─────┼─→ +X
//    │     │      │
//   SW ────┼──── SE
//          ▼
//
//  Center is reserved for SpeedBumps (sits flat in the middle of
//  the road, perpendicular to traffic). The 4 corners are for
//  StopSigns and TrafficLights.
// ─────────────────────────────────────────────────────────────────

public enum TileCorner
{
    None,
    NorthWest,  // -X, +Z   (correct in left-driving for +Z approach)
    NorthEast,  // +X, +Z   (correct in right-driving for +Z approach)
    SouthEast,  // +X, -Z   (correct in left-driving for -Z approach)
    SouthWest,  // -X, -Z   (correct in right-driving for -Z approach)
    Center      // Reserved for SpeedBumps
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
//  PLACED SLOT  — one device occupying one corner (or the center)
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class PlacedSlot
{
    public TileCorner corner;
    public TrafficDeviceType deviceType;
    public GameObject deviceObject;
    public bool wasWrongCorner;
    public bool wasPoorZone;
    public bool wasOverLimit;
    public float happinessApplied;   // signed value actually applied (for accounting / undo)
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

    // ── Multi-Device Config ───────────────────
    [Header("Multi-Device Config")]
    [Tooltip("Tick if this tile is an intersection. Intersections are exempt from the >3 devices penalty.")]
    public bool isIntersection = false;

    [Tooltip("Number of devices that can be placed before the over-limit happiness penalty kicks in. " +
             "Ignored when isIntersection is true.")]
    [Range(1, 4)]
    public int normalDeviceLimit = 3;

    [Tooltip("Absolute maximum devices that can ever fit on this tile (corners + center cap).")]
    [Range(1, 4)]
    public int maxDevices = 4;

    [Tooltip("Random happiness penalty (negative) applied per device beyond normalDeviceLimit. " +
             "X = min magnitude, Y = max magnitude. Default 10–15.")]
    public Vector2 overLimitPenaltyRange = new Vector2(10f, 15f);

    // ── Corner Layout ─────────────────────────
    [Header("Corner Layout")]
    [Tooltip("How far inward from each edge to inset the corner snap points (metres). " +
             "Larger values push corner devices closer to the tile center.")]
    [Min(0f)]
    public float cornerInset = 0.5f;

    [Tooltip("Vertical offset from the tile pivot for placed devices.")]
    public float deviceYOffset = 0f;

    [Tooltip("If true (Malaysia / UK / Japan / SG), correct corners are NW and SE. " +
             "If false (US / most of EU), correct corners are NE and SW.")]
    public bool drivesOnLeft = true;

    // ── Allowed Devices ───────────────────────
    [Header("Device Placement")]
    public List<TrafficDeviceType> allowedDevices = new List<TrafficDeviceType>();

    // ── Placement State ───────────────────────
    [SerializeField] private List<PlacedSlot> _slots = new List<PlacedSlot>();
    public IReadOnlyList<PlacedSlot> Slots => _slots;
    public int PlacedCount => _slots.Count;
    public bool isOccupied => _slots.Count >= maxDevices;

    // Convenience for callers that previously checked single-device fields.
    public TrafficDeviceType placedDeviceType =>
        _slots.Count > 0 ? _slots[0].deviceType : TrafficDeviceType.None;
    public GameObject placedDeviceObject =>
        _slots.Count > 0 ? _slots[0].deviceObject : null;
    public bool isPoorPlacement
    {
        get
        {
            foreach (var s in _slots)
                if (s.wasWrongCorner || s.wasPoorZone || s.wasOverLimit) return true;
            return false;
        }
    }

    // ── Snap Point (legacy, unused by new flow) ──
    [Header("Snap Point (legacy)")]
    public Transform deviceSnapPoint;

    // ── Grow Effect ───────────────────────────
    [Header("Grow Effect")]
    public bool playGrowOnStart = true;

    [Min(0.05f)]
    public float growDuration = 0.4f;

    [Range(0f, 0.5f)]
    public float growOvershoot = 0.12f;

    [Range(0.1f, 0.5f)]
    public float overshootFraction = 0.25f;

    // ── Events ────────────────────────────────
    public System.Action<RoadTile, bool> OnDevicePlaced;
    public System.Action<RoadTile> OnDeviceRemoved;
    public System.Action<RoadTile, int, int> OnContributionChanged;

    // ── Private ───────────────────────────────
    private Vector3 _originalScale;
    private bool _growDone = false;
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
    //  CORNER GEOMETRY
    // ─────────────────────────────────────────

    /// <summary>Position of a corner / center in tile-local space.</summary>
    public Vector3 GetCornerLocalPosition(TileCorner corner)
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 size = col != null ? col.size : Vector3.one;
        Vector3 center = col != null ? col.center : Vector3.zero;

        float hx = Mathf.Max(0f, size.x * 0.5f - cornerInset);
        float hz = Mathf.Max(0f, size.z * 0.5f - cornerInset);
        float y = center.y + deviceYOffset;

        return corner switch
        {
            TileCorner.NorthWest => center + new Vector3(-hx, deviceYOffset, +hz),
            TileCorner.NorthEast => center + new Vector3(+hx, deviceYOffset, +hz),
            TileCorner.SouthEast => center + new Vector3(+hx, deviceYOffset, -hz),
            TileCorner.SouthWest => center + new Vector3(-hx, deviceYOffset, -hz),
            TileCorner.Center => new Vector3(center.x, deviceYOffset, center.z),
            _ => new Vector3(center.x, deviceYOffset, center.z),
        };
    }

    /// <summary>
    /// Local rotation for a device at a corner.
    /// Corners at the +Z end face -Z (toward oncoming +Z-bound drivers).
    /// Corners at the -Z end face +Z. Center keeps default orientation
    /// (speed bump sits flat across the road).
    /// </summary>
    public Quaternion GetCornerLocalRotation(TileCorner corner)
    {
        return corner switch
        {
            TileCorner.NorthWest => Quaternion.Euler(0f, 180f, 0f),
            TileCorner.NorthEast => Quaternion.Euler(0f, 180f, 0f),
            TileCorner.SouthEast => Quaternion.identity,
            TileCorner.SouthWest => Quaternion.identity,
            TileCorner.Center => Quaternion.identity,
            _ => Quaternion.identity,
        };
    }

    /// <summary>
    /// Returns the corner nearest to a given world point.
    /// SpeedBumps always return Center.
    /// </summary>
    public TileCorner GetNearestCorner(Vector3 worldPoint, TrafficDeviceType device)
    {
        if (device == TrafficDeviceType.SpeedBump)
            return TileCorner.Center;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 c = col != null ? col.center : Vector3.zero;

        bool east = (local.x - c.x) >= 0f;
        bool north = (local.z - c.z) >= 0f;

        if (north && !east) return TileCorner.NorthWest;
        if (north && east) return TileCorner.NorthEast;
        if (!north && east) return TileCorner.SouthEast;
        return TileCorner.SouthWest;
    }

    /// <summary>
    /// Whether the given corner is the "correct" side of the road for this device.
    /// Bidirectional roads have two correct corners (on opposite ends of the same diagonal).
    /// </summary>
    public bool IsCorrectCorner(TileCorner corner, TrafficDeviceType device)
    {
        // Speed bumps always go in the center — never "wrong" geometrically.
        if (device == TrafficDeviceType.SpeedBump)
            return corner == TileCorner.Center;

        if (drivesOnLeft)
            return corner == TileCorner.NorthWest || corner == TileCorner.SouthEast;
        else
            return corner == TileCorner.NorthEast || corner == TileCorner.SouthWest;
    }

    public bool IsCornerOccupied(TileCorner corner)
    {
        foreach (var s in _slots)
            if (s.corner == corner) return true;
        return false;
    }

    /// <summary>True if placing a new device right now would exceed the
    /// non-intersection soft cap and incur the random 10–15 penalty.</summary>
    public bool WouldBeOverLimit()
    {
        return !isIntersection && _slots.Count >= normalDeviceLimit;
    }

    // ─────────────────────────────────────────
    //  OVERLAY HELPERS
    // ─────────────────────────────────────────

    public OverlayState GetOverlayState(TrafficDeviceType device)
    {
        if (device == TrafficDeviceType.None)
            return OverlayState.Default;

        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return OverlayState.Hidden;

        // Hard cap reached → red
        if (_slots.Count >= maxDevices)
            return OverlayState.Occupied;

        // Speed bumps: blocked once the center slot is full
        if (device == TrafficDeviceType.SpeedBump && IsCornerOccupied(TileCorner.Center))
            return OverlayState.Occupied;

        // Stop / Light: blocked when all 4 corners are taken
        if (device != TrafficDeviceType.SpeedBump)
        {
            int cornerCount = 0;
            foreach (var s in _slots)
                if (s.corner != TileCorner.Center) cornerCount++;
            if (cornerCount >= 4)
                return OverlayState.Occupied;
        }

        // Tile-wide warnings (orange)
        if (DeviceData.IsPoorPlacement(device, zoneType)) return OverlayState.PoorPlacement;
        if (WouldBeOverLimit()) return OverlayState.PoorPlacement;

        return OverlayState.Valid;
    }

    public void RefreshOverlay(TrafficDeviceType activeDevice = TrafficDeviceType.None)
    {
        if (_overlay == null) return;
        _overlay.SetState(GetOverlayState(activeDevice));
    }

    // ─────────────────────────────────────────
    //  GROW EFFECT  (unchanged)
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
    //  PLACEMENT API
    // ─────────────────────────────────────────

    public PlacementResult CanPlace(TrafficDeviceType device, TileCorner corner, float playerCapital)
    {
        // Total cap
        if (_slots.Count >= maxDevices)
            return PlacementResult.AlreadyOccupied;

        // Speed bump constraint: must be Center, only one allowed
        if (device == TrafficDeviceType.SpeedBump)
        {
            if (corner != TileCorner.Center)
                return PlacementResult.DeviceNotAllowed;
            if (IsCornerOccupied(TileCorner.Center))
                return PlacementResult.AlreadyOccupied;
        }
        else
        {
            // Corner-only devices
            if (corner == TileCorner.Center || corner == TileCorner.None)
                return PlacementResult.DeviceNotAllowed;
            if (IsCornerOccupied(corner))
                return PlacementResult.AlreadyOccupied;
        }

        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return PlacementResult.DeviceNotAllowed;

        if (playerCapital < DeviceData.GetCost(device))
            return PlacementResult.InsufficientFunds;

        // Anything beyond this is a soft warning — placement still succeeds.
        if (DeviceData.IsPoorPlacement(device, zoneType)) return PlacementResult.PoorPlacement;
        if (!IsCorrectCorner(corner, device)) return PlacementResult.PoorPlacement;
        if (WouldBeOverLimit()) return PlacementResult.PoorPlacement;

        return PlacementResult.Success;
    }

    /// <summary>
    /// Place a device at a specific corner (or Center for SpeedBumps).
    /// Penalties stack: wrong corner + poor zone + over-limit can all apply.
    /// </summary>
    public PlacementResult PlaceDevice(
        TrafficDeviceType device,
        TileCorner corner,
        GameObject deviceObject,
        float playerCapital,
        out float happinessDelta,
        out float costSpent)
    {
        happinessDelta = 0f;
        costSpent = 0f;

        PlacementResult result = CanPlace(device, corner, playerCapital);

        // Hard failures: caller is expected to destroy deviceObject
        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
            return result;

        // Categorise placement quality
        bool wrongCorner = !IsCorrectCorner(corner, device);
        bool poorZone = DeviceData.IsPoorPlacement(device, zoneType);
        bool overLimit = WouldBeOverLimit();

        // Base happiness from device stats
        DeviceData.DeviceStats stats = DeviceData.Get(device);
        float happiness = (wrongCorner || poorZone)
            ? stats.happinessDeltaPoor
            : stats.happinessDeltaGood;

        // Stack the random over-limit penalty on top
        if (overLimit)
        {
            float extra = Random.Range(overLimitPenaltyRange.x, overLimitPenaltyRange.y);
            happiness -= extra;
            Debug.Log($"[RoadTile] {tileID}: over-limit device #{_slots.Count + 1} " +
                      $"on non-intersection tile → extra happiness penalty −{extra:F1}");
        }

        // Commit slot
        PlacedSlot slot = new PlacedSlot
        {
            corner = corner,
            deviceType = device,
            deviceObject = deviceObject,
            wasWrongCorner = wrongCorner,
            wasPoorZone = poorZone,
            wasOverLimit = overLimit,
            happinessApplied = happiness
        };
        _slots.Add(slot);

        // Position the device at the corner with the right local rotation
        Vector3 localPos = GetCornerLocalPosition(corner);
        Quaternion localRot = GetCornerLocalRotation(corner);

        deviceObject.transform.SetParent(transform, worldPositionStays: false);
        deviceObject.transform.localPosition = localPos;
        deviceObject.transform.localRotation = localRot;
        // Make sure no leftover scale from the ghost prefab survives
        deviceObject.transform.localScale = Vector3.one;

        happinessDelta = happiness;
        costSpent = stats.costRM;

        RecalculateContribution();

        PlacementResult finalResult = (wrongCorner || poorZone || overLimit)
            ? PlacementResult.PoorPlacement
            : PlacementResult.Success;

        OnDevicePlaced?.Invoke(this, finalResult == PlacementResult.PoorPlacement);
        RefreshOverlay(TrafficDeviceType.None);

        Debug.Log($"[RoadTile] {tileID}: placed {device} @ {corner} " +
                  $"(wrongCorner={wrongCorner}, poorZone={poorZone}, overLimit={overLimit}) " +
                  $"cost=RM{costSpent} happiness={happinessDelta:+0.0;-0.0} " +
                  $"totalOnTile={_slots.Count}/{maxDevices}");

        return finalResult;
    }

    /// <summary>Remove a single device from a specific corner.</summary>
    public void RemoveDeviceAt(TileCorner corner)
    {
        PlacedSlot toRemove = null;
        foreach (var s in _slots)
            if (s.corner == corner) { toRemove = s; break; }

        if (toRemove == null) return;

        if (toRemove.deviceObject != null)
            Destroy(toRemove.deviceObject);

        _slots.Remove(toRemove);
        RecalculateContribution();
        OnDeviceRemoved?.Invoke(this);
        RefreshOverlay(TrafficDeviceType.None);
    }

    /// <summary>Remove every device on the tile.</summary>
    public void RemoveAllDevices()
    {
        foreach (var s in _slots)
            if (s.deviceObject != null) Destroy(s.deviceObject);

        _slots.Clear();
        RecalculateContribution();
        OnDeviceRemoved?.Invoke(this);
        RefreshOverlay(TrafficDeviceType.None);
    }

    /// <summary>Legacy single-device remove — clears the entire tile.</summary>
    public void RemoveDevice() => RemoveAllDevices();

    /// <summary>Sum every device's accident reduction. Wrong corner / poor zone halves the reduction.</summary>
    public void RecalculateContribution()
    {
        int prev = currentAccidentContribution;

        int totalReduction = 0;
        foreach (var s in _slots)
        {
            int red = DeviceData.GetReduction(s.deviceType);
            if (s.wasWrongCorner || s.wasPoorZone)
                red = Mathf.FloorToInt(red * 0.5f);
            totalReduction += red;
        }

        currentAccidentContribution =
            Mathf.Max(0, baseAccidentContribution - totalReduction);

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

        // Corner gizmos: green = correct corners for current driving side, red = wrong
        TileCorner[] all = { TileCorner.NorthWest, TileCorner.NorthEast,
                             TileCorner.SouthEast, TileCorner.SouthWest };
        foreach (var c in all)
        {
            bool correct = IsCorrectCorner(c, TrafficDeviceType.StopSign);
            Gizmos.color = correct ? new Color(0f, 1f, 0f, 0.9f)
                                   : new Color(1f, 0f, 0f, 0.9f);
            Vector3 local = GetCornerLocalPosition(c);
            Gizmos.DrawSphere(local, 0.15f);
        }

        // Center gizmo (speed bump slot)
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);
        Gizmos.DrawSphere(GetCornerLocalPosition(TileCorner.Center), 0.12f);

        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        string deviceInfo = _slots.Count > 0
            ? $"{_slots.Count}/{maxDevices} devices"
            : "empty";

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.6f,
            $"{tileID}\n{zoneType} | {tileType}\n" +
            $"Intersection: {isIntersection}\n" +
            $"Contribution: {currentAccidentContribution}\n" +
            $"Devices: {deviceInfo}"
        );
    }
#endif
}