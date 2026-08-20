using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────────────────────────

public enum TileType { Straight, Curve, TJunction, Intersection, Residential }
public enum ZoneType { Residential, TJunction, Crossroad }
public enum TrafficDeviceType { None, StopSign, TrafficLight, SpeedBump }
public enum PlacementResult { Success, AlreadyOccupied, DeviceNotAllowed, InsufficientFunds, PoorPlacement }

// Segment classification — drives the placement-correctness table.
public enum TileSegmentType { Middle, End, Intersection }

// Which tile-local axis counts as "forward".
public enum ForwardAxis { LocalPosZ, LocalNegZ, LocalPosX, LocalNegX }

// Compass corner labels in tile-local axes.
public enum TileCorner { None, NorthWest, NorthEast, SouthEast, SouthWest, Center }

// ─────────────────────────────────────────────────────────────────
//  DEVICE DATA
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
        { TrafficDeviceType.StopSign,     new DeviceStats { costRM = 250f,  accidentReduction = 2, happinessDeltaGood = 5f,  happinessDeltaPoor = -3f,  unsuitableInResidential = false } },
        { TrafficDeviceType.SpeedBump,    new DeviceStats { costRM = 350f,  accidentReduction = 3, happinessDeltaGood = 7f,  happinessDeltaPoor = -2f,  unsuitableInResidential = false } },
        { TrafficDeviceType.TrafficLight, new DeviceStats { costRM = 2500f, accidentReduction = 5, happinessDeltaGood = 10f, happinessDeltaPoor = -15f, unsuitableInResidential = true  } }
    };

    public static DeviceStats Get(TrafficDeviceType type)
        => _data.TryGetValue(type, out DeviceStats s) ? s : default;

    public static float GetCost(TrafficDeviceType type) => Get(type).costRM;
}

// ─────────────────────────────────────────────────────────────────
//  PLACED SLOT
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class PlacedSlot
{
    public TileCorner corner;
    public TrafficDeviceType deviceType;
    public GameObject deviceObject;
}

// ─────────────────────────────────────────────────────────────────
//  ROAD TILE
//
//  CHANGES (this version):
//    • Req 3 — TrafficLight limit on End tiles raised from 1 → 2.
//    • Req 4 — StopSign limit on End tiles raised from 2 → 4.
//    • maxDevices default raised to 5 to accommodate 4 corner devices
//      + 1 center device without hitting the hard cap.
//    • Stop Sign on End tiles now requires far-end corners (same rule
//      as TrafficLight), matching real-world stop-sign placement at
//      the approach end of a segment.
// ─────────────────────────────────────────────────────────────────

[RequireComponent(typeof(BoxCollider))]
public class RoadTile : MonoBehaviour
{
    // ── Identity ──────────────────────────────
    [Header("Tile Identity")]
    public TileType tileType = TileType.Straight;
    public ZoneType zoneType = ZoneType.Residential;
    public string tileID = "";

    // ── Segment Classification ────────────────
    [Header("Segment Classification")]
    [Tooltip("Determines what device counts / corners are 'correct' on this tile.")]
    public TileSegmentType segmentType = TileSegmentType.Middle;

    [Tooltip("Tile-local axis that counts as 'forward'.")]
    public ForwardAxis forwardAxis = ForwardAxis.LocalPosZ;

    // ── Multi-Device ──────────────────────────
    [Header("Multi-Device")]
    [Tooltip("Hard cap on devices for this tile (corners + center).\n" +
             "Raised to 5 to allow 4 stop-sign corners + 1 center device.")]
    [Range(1, 5)] public int maxDevices = 5;

    // ── Geometry ──────────────────────────────
    [Header("Corner Layout")]
    [Tooltip("How far corner snap-points are inset from the tile edge along the X axis (width).")]
    [Min(0f)] public float cornerInsetX = 0.5f;

    [Tooltip("How far corner snap-points are inset from the tile edge along the Z axis (depth).")]
    [Min(0f)] public float cornerInsetZ = 0.5f;

    [Tooltip("Y offset for the CENTER (middle) device slot.")]
    public float centerDeviceYOffset = 3f;

    [Tooltip("Y offset for the four CORNER device slots.")]
    public float cornerDeviceYOffset = 3f;

    // ── Allowed Devices ───────────────────────
    [Header("Allowed Devices")]
    public List<TrafficDeviceType> allowedDevices = new List<TrafficDeviceType>();

    // ── Placement State ───────────────────────
    [SerializeField] private List<PlacedSlot> _slots = new List<PlacedSlot>();
    public IReadOnlyList<PlacedSlot> Slots => _slots;
    public int PlacedCount => _slots.Count;

    /// <summary>
    /// True only when BOTH all corner slots (up to maxDevices) AND the center
    /// slot are occupied — no slot of any kind remains on this tile.
    /// </summary>
    public bool isOccupied
    {
        get
        {
            bool centerTaken = IsCornerOccupied(TileCorner.Center);
            int cornerCount = 0;
            foreach (var s in _slots)
                if (s.corner != TileCorner.Center) cornerCount++;
            return cornerCount >= maxDevices && centerTaken;
        }
    }

    // Backwards-compat helpers
    public TrafficDeviceType placedDeviceType => _slots.Count > 0 ? _slots[0].deviceType : TrafficDeviceType.None;
    public GameObject placedDeviceObject => _slots.Count > 0 ? _slots[0].deviceObject : null;

    // ── Section ──────────────────────────────
    private RoadSection _section;
    public RoadSection Section => _section;

    // ── Events ────────────────────────────────
    public System.Action<RoadTile, bool> OnDevicePlaced;
    public System.Action<RoadTile> OnDeviceRemoved;
    public System.Action<RoadTile, int, int> OnContributionChanged;

    private TileOverlay _overlay;
    public TileOverlay Overlay => _overlay;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;

        _overlay = GetComponent<TileOverlay>();
        if (_overlay == null) _overlay = gameObject.AddComponent<TileOverlay>();

        _section = GetComponentInParent<RoadSection>();
    }

    public void AssignSection(RoadSection s) => _section = s;

    // ─────────────────────────────────────────
    //  FORWARD / CORNER GEOMETRY
    // ─────────────────────────────────────────

    public Vector3 LocalForward => forwardAxis switch
    {
        ForwardAxis.LocalPosZ => Vector3.forward,
        ForwardAxis.LocalNegZ => Vector3.back,
        ForwardAxis.LocalPosX => Vector3.right,
        ForwardAxis.LocalNegX => Vector3.left,
        _ => Vector3.forward,
    };

    /// <summary>True if a corner sits at the "far end" relative to forward.</summary>
    public bool IsAtFarEnd(TileCorner c) => forwardAxis switch
    {
        ForwardAxis.LocalPosZ => c == TileCorner.NorthWest || c == TileCorner.NorthEast,
        ForwardAxis.LocalNegZ => c == TileCorner.SouthEast || c == TileCorner.SouthWest,
        ForwardAxis.LocalPosX => c == TileCorner.NorthEast || c == TileCorner.SouthEast,
        ForwardAxis.LocalNegX => c == TileCorner.NorthWest || c == TileCorner.SouthWest,
        _ => false,
    };

    public Vector3 GetCornerLocalPosition(TileCorner corner)
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 size = col != null ? col.size : Vector3.one;
        Vector3 center = col != null ? col.center : Vector3.zero;

        float hx = Mathf.Max(0f, size.x * 0.5f - cornerInsetX);
        float hz = Mathf.Max(0f, size.z * 0.5f - cornerInsetZ);
        float y = (corner == TileCorner.Center) ? centerDeviceYOffset : cornerDeviceYOffset;

        return corner switch
        {
            TileCorner.NorthWest => new Vector3(center.x - hx, y, center.z + hz),
            TileCorner.NorthEast => new Vector3(center.x + hx, y, center.z + hz),
            TileCorner.SouthEast => new Vector3(center.x + hx, y, center.z - hz),
            TileCorner.SouthWest => new Vector3(center.x - hx, y, center.z - hz),
            TileCorner.Center => new Vector3(center.x, y, center.z),
            _ => new Vector3(center.x, y, center.z),
        };
    }

    public TileCorner GetNearestCorner(Vector3 worldPoint, TrafficDeviceType device)
    {
        if (device == TrafficDeviceType.SpeedBump) return TileCorner.Center;

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

    // ─────────────────────────────────────────
    //  CORRECTNESS TABLE
    //
    //  Req 3: End + TrafficLight limit raised from 1 → 2.
    //  Req 4: End + StopSign limit raised from 2 → 4.
    // ─────────────────────────────────────────

    /// <summary>The "correct" count cap per (segment, device) pair.</summary>
    public int GetCorrectCountLimit(TrafficDeviceType d) => (segmentType, d) switch
    {
        (TileSegmentType.Middle, TrafficDeviceType.SpeedBump) => 1,
        (TileSegmentType.End, TrafficDeviceType.StopSign) => 4,   // REQ 4: was 2
        (TileSegmentType.End, TrafficDeviceType.SpeedBump) => 1,
        (TileSegmentType.End, TrafficDeviceType.TrafficLight) => 2,   // REQ 3: was 1
        (TileSegmentType.Intersection, TrafficDeviceType.TrafficLight) => 4,
        _ => 0
    };

    /// <summary>
    /// True if this slot is a CORRECT placement: right type, right corner,
    /// and within the count limit.
    ///
    /// REQ 4 change: StopSign on End tiles is correct at ANY of the four
    /// corners (not just the far-end pair), because all four corners are now
    /// valid and the limit is 4.
    ///
    /// REQ 3 change: TrafficLight on End tiles accepts the two far-end corners
    /// as before, but now up to 2 can be placed (one per far-end corner).
    /// </summary>
    public bool IsSlotCorrect(PlacedSlot slot)
    {
        int limit = GetCorrectCountLimit(slot.deviceType);
        if (limit <= 0) return false;

        // Determine this slot's order-index among all slots of the same type.
        int orderIdx = 0;
        bool found = false;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].deviceType != slot.deviceType) continue;
            if (_slots[i] == slot) { found = true; break; }
            orderIdx++;
        }
        if (!found || orderIdx >= limit) return false;

        switch (slot.deviceType)
        {
            case TrafficDeviceType.SpeedBump:
                return slot.corner == TileCorner.Center;

            case TrafficDeviceType.StopSign:
                if (segmentType != TileSegmentType.End) return false;
                // Stop Signs must be placed at far-end corners (same rule as Traffic Lights),
                // so they face oncoming traffic approaching the end of the road segment.
                if (!IsAtFarEnd(slot.corner)) return false;
                // Ensure no earlier stop sign is at the same corner.
                foreach (var earlier in _slots)
                {
                    if (earlier == slot) break;
                    if (earlier.deviceType == TrafficDeviceType.StopSign
                        && earlier.corner == slot.corner)
                        return false;
                }
                return true;

            case TrafficDeviceType.TrafficLight:
                if (segmentType == TileSegmentType.End)
                {
                    // REQ 3: up to 2, each must be at a far-end corner and unique.
                    if (!IsAtFarEnd(slot.corner)) return false;
                    // Ensure no earlier light is at the same corner.
                    foreach (var earlier in _slots)
                    {
                        if (earlier == slot) break;
                        if (earlier.deviceType == TrafficDeviceType.TrafficLight
                            && earlier.corner == slot.corner)
                            return false;
                    }
                    return true;
                }

                if (segmentType == TileSegmentType.Intersection)
                {
                    foreach (var earlier in _slots)
                    {
                        if (earlier == slot) break;
                        if (earlier.deviceType == TrafficDeviceType.TrafficLight
                            && earlier.corner == slot.corner)
                            return false;
                    }
                    return true;
                }
                return false;
        }

        return false;
    }

    public int CountCorrectSlots()
    {
        int n = 0;
        foreach (var s in _slots) if (IsSlotCorrect(s)) n++;
        return n;
    }

    public bool IsCornerOccupied(TileCorner corner)
    {
        foreach (var s in _slots) if (s.corner == corner) return true;
        return false;
    }

    // ─────────────────────────────────────────
    //  CORNER DEVICE QUERIES
    // ─────────────────────────────────────────

    public bool HasDeviceAtCorner(TileCorner corner, TrafficDeviceType type)
    {
        foreach (var s in _slots)
            if (s.corner == corner && s.deviceType == type) return true;
        return false;
    }

    public bool HasAnyDeviceOfType(TrafficDeviceType type)
    {
        foreach (var s in _slots)
            if (s.deviceType == type) return true;
        return false;
    }

    // ─────────────────────────────────────────
    //  OVERLAY
    // ─────────────────────────────────────────

    public OverlayState GetOverlayState(TrafficDeviceType device)
    {
        if (device == TrafficDeviceType.None)
            return isOccupied ? OverlayState.Occupied : OverlayState.Available;

        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return OverlayState.Hidden;

        if (_slots.Count >= maxDevices)
            return OverlayState.Occupied;

        if (device == TrafficDeviceType.SpeedBump && IsCornerOccupied(TileCorner.Center))
            return OverlayState.Occupied;

        if (device != TrafficDeviceType.SpeedBump)
        {
            int cornerCount = 0;
            foreach (var s in _slots) if (s.corner != TileCorner.Center) cornerCount++;
            if (cornerCount >= 4) return OverlayState.Occupied;
        }

        if (GetCorrectCountLimit(device) <= 0)
            return OverlayState.NotSuitable;

        return OverlayState.Suitable;
    }

    public void RefreshOverlay(TrafficDeviceType activeDevice = TrafficDeviceType.None)
    {
        if (_overlay == null) return;
        _overlay.SetState(GetOverlayState(activeDevice));
    }

    // ─────────────────────────────────────────
    //  PLACEMENT API
    // ─────────────────────────────────────────

    public PlacementResult CanPlace(TrafficDeviceType device, TileCorner corner, float playerCapital)
    {
        if (device == TrafficDeviceType.SpeedBump)
        {
            if (corner != TileCorner.Center) return PlacementResult.DeviceNotAllowed;
            if (IsCornerOccupied(TileCorner.Center)) return PlacementResult.AlreadyOccupied;
        }
        else
        {
            int cornerCount = 0;
            foreach (var s in _slots)
                if (s.corner != TileCorner.Center) cornerCount++;
            int maxCorners = maxDevices;
            if (cornerCount >= maxCorners) return PlacementResult.AlreadyOccupied;

            if (corner == TileCorner.Center || corner == TileCorner.None)
                return PlacementResult.DeviceNotAllowed;
            if (IsCornerOccupied(corner)) return PlacementResult.AlreadyOccupied;
        }

        if (allowedDevices.Count > 0 && !allowedDevices.Contains(device))
            return PlacementResult.DeviceNotAllowed;

        if (playerCapital < DeviceData.GetCost(device))
            return PlacementResult.InsufficientFunds;

        // ── REQ 3 / REQ 4 capacity guard ────────────────────────────────────
        // If the correct-count limit is already reached for this device type,
        // treat any additional placement as AlreadyOccupied (plays "Failed"
        // sound) rather than PoorPlacement, because the tile is genuinely full
        // for that device category.
        int limit = GetCorrectCountLimit(device);
        if (limit > 0)
        {
            int existingOfType = 0;
            foreach (var s in _slots)
                if (s.deviceType == device) existingOfType++;
            if (existingOfType >= limit)
                return PlacementResult.AlreadyOccupied;
        }

        if (GetCorrectCountLimit(device) <= 0)
            return PlacementResult.PoorPlacement;

        return PlacementResult.Success;
    }

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

        PlacementResult gate = CanPlace(device, corner, playerCapital);
        if (gate == PlacementResult.AlreadyOccupied ||
            gate == PlacementResult.DeviceNotAllowed ||
            gate == PlacementResult.InsufficientFunds)
            return gate;

        PlacedSlot slot = new PlacedSlot
        {
            corner = corner,
            deviceType = device,
            deviceObject = deviceObject
        };
        _slots.Add(slot);

        deviceObject.transform.SetParent(transform, worldPositionStays: false);
        deviceObject.transform.localPosition = GetCornerLocalPosition(corner);

        costSpent = DeviceData.GetCost(device);

        bool isCorrect = IsSlotCorrect(slot);
        PlacementResult finalResult = isCorrect ? PlacementResult.Success : PlacementResult.PoorPlacement;

        // Activate the traffic-device behaviour zone for this placement.
        ActivateDeviceZone(device, deviceObject);

        OnDevicePlaced?.Invoke(this, !isCorrect);
        RefreshOverlay(TrafficDeviceType.None);

        Debug.Log($"[RoadTile] {tileID}: placed {device} @ {corner} " +
                  $"({(isCorrect ? "CORRECT" : "incorrect")}) cost=RM{costSpent} " +
                  $"deltaHappiness={happinessDelta:+0.0;-0.0} totalOnTile={_slots.Count}/{maxDevices}");

        return finalResult;
    }

    public void RemoveDeviceAt(TileCorner corner)
    {
        PlacedSlot toRemove = null;
        foreach (var s in _slots) if (s.corner == corner) { toRemove = s; break; }
        if (toRemove == null) return;

        if (toRemove.deviceObject != null) Destroy(toRemove.deviceObject);
        _slots.Remove(toRemove);

        OnDeviceRemoved?.Invoke(this);
        RefreshOverlay(TrafficDeviceType.None);
    }

    public void RemoveAllDevices()
    {
        foreach (var s in _slots)
            if (s.deviceObject != null) Destroy(s.deviceObject);
        _slots.Clear();
        OnDeviceRemoved?.Invoke(this);
        RefreshOverlay(TrafficDeviceType.None);
    }

    public void RemoveDevice() => RemoveAllDevices();

    // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
    //  DEVICE ZONE ACTIVATION
    // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    /// <summary>
    /// Called after a device is successfully placed on this tile.
    /// Adds the matching zone MonoBehaviour to this tile's GameObject so it
    /// can detect cars passing through the tile's trigger collider.
    /// </summary>
    private void ActivateDeviceZone(TrafficDeviceType device, GameObject deviceObject)
    {
        switch (device)
        {
            case TrafficDeviceType.StopSign:
                if (GetComponent<StopSignZone>() == null)
                    gameObject.AddComponent<StopSignZone>();
                break;

            case TrafficDeviceType.SpeedBump:
                if (GetComponent<SpeedBumpZone>() == null)
                    gameObject.AddComponent<SpeedBumpZone>();
                break;

            case TrafficDeviceType.TrafficLight:
                if (GetComponent<TrafficLightZone>() == null)
                    gameObject.AddComponent<TrafficLightZone>();
                break;
        }
    }


    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) return;

        Color baseColor = segmentType switch
        {
            TileSegmentType.Middle => new Color(0.6f, 0.6f, 0.6f, 0.20f),
            TileSegmentType.End => new Color(0.2f, 0.6f, 1.0f, 0.25f),
            TileSegmentType.Intersection => new Color(1.0f, 0.6f, 0.2f, 0.25f),
            _ => new Color(0.5f, 0.5f, 0.5f, 0.20f)
        };

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = baseColor;
        Gizmos.DrawCube(col.center, col.size);
        baseColor.a = 0.8f;
        Gizmos.color = baseColor;
        Gizmos.DrawWireCube(col.center, col.size);

        TileCorner[] all = { TileCorner.NorthWest, TileCorner.NorthEast,
                             TileCorner.SouthEast,  TileCorner.SouthWest };
        foreach (var c in all)
        {
            // Stop Signs now require far-end corners (same as Traffic Lights),
            // so far-end corners are bright green, near-end are dimmer.
            Gizmos.color = IsAtFarEnd(c)
                ? new Color(0f, 1f, 0f, 0.9f)
                : new Color(0.6f, 0.6f, 0.6f, 0.5f);
            Gizmos.DrawSphere(GetCornerLocalPosition(c), 0.15f);
        }

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);
        Gizmos.DrawSphere(GetCornerLocalPosition(TileCorner.Center), 0.12f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(col.center, LocalForward * 0.8f);

        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.6f,
            $"{tileID}\n{segmentType} | forward={forwardAxis}\n" +
            $"Devices: {_slots.Count}/{maxDevices} ({CountCorrectSlots()} correct)"
        );
    }
#endif
}