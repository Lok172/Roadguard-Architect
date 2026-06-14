using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD SEGMENT
//
//  Represents an undirected edge between two RoadIntersections.
//  Attach to a GameObject placed between two intersections.
//
//  Stores per-segment tunable risk values and tracks:
//    • Cars currently travelling on it (density).
//    • Whether the segment is blocked (accident).
//    • Device risk reduction (auto-summed from child RoadTiles).
//
//  LANE OFFSET
//    laneOffset defines how far left/right (in world units) each
//    car is displaced from the segment centre-line depending on
//    direction of travel.  Positive = right-hand traffic.
//    A→B travellers are offset +laneOffset (right of AB direction).
//    B→A travellers are offset -laneOffset (left of AB direction,
//    which is the right side when facing from B to A).
// ─────────────────────────────────────────────────────────────────

public class RoadSegment : MonoBehaviour
{
    // ── Identity ──────────────────────────────
    [Header("Identity")]
    public string segmentID = "Segment_00";

    // ── Graph connections ─────────────────────
    [Header("Graph Connections")]
    [Tooltip("One endpoint of this road segment.")]
    public RoadIntersection intersectionA;

    [Tooltip("Other endpoint of this road segment.")]
    public RoadIntersection intersectionB;

    // ── Road Properties ───────────────────────
    [Header("Road Properties")]
    [Tooltip("Speed limit on this segment (world units per second).")]
    [Min(1f)] public float speedLimit = 10f;

    [Tooltip("Base accident risk score for this segment [0..1]. " +
             "Higher = more dangerous before devices.")]
    [Range(0f, 1f)] public float baseRisk = 0.1f;

    [Tooltip("How much each extra car on this segment adds to risk. " +
             "risk += carCount * densityRiskFactor")]
    [Min(0f)] public float densityRiskFactor = 0.02f;

    [Tooltip("How much the intersection at each end adds to risk " +
             "(more connections = more complexity).")]
    [Min(0f)] public float intersectionComplexityFactor = 0.01f;

    // ── Lane Offset ───────────────────────────
    [Header("Lane Offset")]
    [Tooltip("Lateral offset from the segment centre-line (world units). " +
             "Cars travelling A→B are displaced to their right (+offset); " +
             "cars travelling B→A are displaced to their right as well (-offset in AB space). " +
             "Set to 0 to disable lane splitting.")]
    [Min(0f)] public float laneOffset = 0.5f;

    // ── Block / Accident State ────────────────
    [Header("Block State (runtime)")]
    [SerializeField] private bool _isBlocked;
    public bool IsBlocked => _isBlocked;

    // ── Runtime tracking ──────────────────────
    private readonly List<CarAgent> _carsOnSegment = new List<CarAgent>();
    public int CarCount => _carsOnSegment.Count;

    // Device risk reduction is re-computed whenever a device is placed/removed.
    private float _deviceRiskReduction;
    public float DeviceRiskReduction => _deviceRiskReduction;

    // ── Cached length ─────────────────────────
    private float _length = -1f;
    public float Length
    {
        get
        {
            if (_length < 0f) RecalcLength();
            return _length;
        }
    }

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        RecalcLength();
        RegisterWithIntersections();
        RefreshDeviceReduction();
    }

    private void OnDestroy()
    {
        UnregisterFromIntersections();
    }

    // ─────────────────────────────────────────
    //  GRAPH HELPERS
    // ─────────────────────────────────────────

    private void RegisterWithIntersections()
    {
        intersectionA?.RegisterSegment(this);
        intersectionB?.RegisterSegment(this);
    }

    private void UnregisterFromIntersections()
    {
        intersectionA?.UnregisterSegment(this);
        intersectionB?.UnregisterSegment(this);
    }

    /// <summary>
    /// Returns the intersection at the OTHER end from <paramref name="from"/>.
    /// Works for both directions (undirected).
    /// </summary>
    public RoadIntersection Other(RoadIntersection from)
    {
        if (from == intersectionA) return intersectionB;
        if (from == intersectionB) return intersectionA;
        return null;
    }

    public bool ConnectsTo(RoadIntersection node)
        => node == intersectionA || node == intersectionB;

    private void RecalcLength()
    {
        if (intersectionA != null && intersectionB != null)
            _length = Vector3.Distance(intersectionA.transform.position,
                                       intersectionB.transform.position);
        else
            _length = 1f;
    }

    // ─────────────────────────────────────────
    //  LANE OFFSET HELPERS
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the lateral offset vector for a car travelling from
    /// <paramref name="from"/> toward <paramref name="to"/>.
    /// The car is placed on the right-hand side of its direction of travel.
    /// </summary>
    public Vector3 GetLaneOffsetVector(RoadIntersection from, RoadIntersection to)
    {
        if (laneOffset <= 0f || from == null || to == null) return Vector3.zero;

        Vector3 forward = (to.transform.position - from.transform.position).normalized;
        // Right of travel direction (keep cars on the road, not floating up).
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return right * laneOffset;
    }

    /// <summary>
    /// Returns the world position along the segment at normalised t [0..1],
    /// offset into the correct lane for a car travelling A→B or B→A.
    /// </summary>
    public Vector3 GetLanedPositionAt(float t, RoadIntersection from, RoadIntersection to)
    {
        Vector3 centre = GetPositionAt(
            from == intersectionA ? t : 1f - t);
        return centre + GetLaneOffsetVector(from, to);
    }

    // ─────────────────────────────────────────
    //  CAR TRACKING
    // ─────────────────────────────────────────

    public void RegisterCar(CarAgent car)
    {
        if (!_carsOnSegment.Contains(car)) _carsOnSegment.Add(car);
    }

    public void UnregisterCar(CarAgent car)
    {
        _carsOnSegment.Remove(car);
    }

    public IReadOnlyList<CarAgent> CarsOnSegment => _carsOnSegment;

    // ─────────────────────────────────────────
    //  RISK CALCULATION
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the current risk score [0..1] for this segment.
    /// Called each evaluation tick by CarManager.
    /// </summary>
    public float CalculateRisk(float carSpeed)
    {
        float speedFactor = carSpeed / Mathf.Max(1f, speedLimit);

        int complexity = 0;
        if (intersectionA != null) complexity += intersectionA.ConnectedSegments.Count;
        if (intersectionB != null) complexity += intersectionB.ConnectedSegments.Count;

        float risk = baseRisk
                   + speedFactor * 0.1f
                   + _carsOnSegment.Count * densityRiskFactor
                   + complexity * intersectionComplexityFactor
                   - _deviceRiskReduction;

        return Mathf.Clamp01(risk);
    }

    // ─────────────────────────────────────────
    //  DEVICE REDUCTION  (call after any placement change)
    // ─────────────────────────────────────────

    /// <summary>
    /// Re-sums device risk reduction from all child RoadTiles.
    /// Call this whenever a device is placed or removed on any child tile.
    /// </summary>
    public void RefreshDeviceReduction()
    {
        _deviceRiskReduction = 0f;
        foreach (var tile in GetComponentsInChildren<RoadTile>())
        {
            foreach (var slot in tile.Slots)
            {
                var stats = DeviceData.Get(slot.deviceType);
                // accidentReduction maps to risk reduction (normalise to [0..1] range).
                _deviceRiskReduction += stats.accidentReduction * 0.01f;
            }
        }
    }

    // ─────────────────────────────────────────
    //  BLOCKING
    // ─────────────────────────────────────────

    public void SetBlocked(bool blocked)
    {
        _isBlocked = blocked;
        Debug.Log($"[RoadSegment] {segmentID}: blocked={blocked}");
    }

    // ─────────────────────────────────────────
    //  WORLD POSITION ALONG SEGMENT
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the world position at normalised t [0..1] along A→B.
    /// </summary>
    public Vector3 GetPositionAt(float t)
    {
        if (intersectionA == null || intersectionB == null) return transform.position;
        return Vector3.Lerp(intersectionA.transform.position,
                            intersectionB.transform.position, t);
    }

    /// <summary>
    /// Returns the normalised t value for the given world position
    /// projected onto the A→B line.
    /// </summary>
    public float GetTAtPosition(Vector3 worldPos)
    {
        if (intersectionA == null || intersectionB == null) return 0f;
        Vector3 ab = intersectionB.transform.position - intersectionA.transform.position;
        Vector3 ap = worldPos - intersectionA.transform.position;
        float len = ab.sqrMagnitude;
        return len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / len);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (intersectionA == null || intersectionB == null) return;

        Gizmos.color = _isBlocked
            ? new Color(1f, 0.1f, 0.1f, 0.9f)
            : new Color(0.3f, 0.8f, 1f, 0.7f);

        Gizmos.DrawLine(intersectionA.transform.position, intersectionB.transform.position);

        // Draw lane offset lines when offset is non-zero
        if (laneOffset > 0f)
        {
            Vector3 ab = intersectionB.transform.position - intersectionA.transform.position;
            Vector3 right = Vector3.Cross(Vector3.up, ab.normalized).normalized * laneOffset;

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
            Gizmos.DrawLine(intersectionA.transform.position + right,
                            intersectionB.transform.position + right);

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
            Gizmos.DrawLine(intersectionA.transform.position - right,
                            intersectionB.transform.position - right);
        }

        Vector3 mid = (intersectionA.transform.position + intersectionB.transform.position) * 0.5f;
        UnityEditor.Handles.Label(
            mid + Vector3.up * 0.5f,
            $"{segmentID}\nCars:{_carsOnSegment.Count}  Risk:{CalculateRisk(speedLimit):F2}" +
            (_isBlocked ? " [BLOCKED]" : "")
        );
    }
#endif
}