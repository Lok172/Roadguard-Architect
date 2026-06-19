using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD SEGMENT  (v3 — generateCrash as eligibility pool)
//
//  NEW in v3:
//    • generateCrash   (Req 4 revised): tick in Inspector to add
//      this segment to the crash-eligible pool. Crashes only spawn
//      on pool members during risk evaluation. The flag is NOT
//      auto-cleared after a crash — it stays ticked until the user
//      manually un-ticks it.
//
//  Retained from v2:    • isTurning        (Req 2): per-lane flag set by a turning car
//      so that straight-going cars yield.
//    • Linked tiles     (Req 5): ordered list of RoadTiles that sit
//      on this segment. Populated at runtime by CarManager via
//      CollectNearbyTiles(). Used by CarAgent to detect devices
//      ahead and calculate a stop position one tile before the
//      device tile, per travel direction.
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
//    A→B travellers are offset +laneOffset (right of AB direction,
//    drawn GREEN). B→A travellers are offset -laneOffset (right of
//    BA direction, drawn ORANGE).
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

    [Tooltip("How much each recent accident on this segment adds to risk. " +
             "risk += recentAccidentCount * accidentRiskFactor")]
    [Min(0f)] public float accidentRiskFactor = 0.05f;

    [Tooltip("Seconds before an accident's risk contribution decays away.")]
    [Min(1f)] public float accidentMemoryDuration = 60f;

    // ── Lane Offset ───────────────────────────
    [Header("Lane Offset")]
    [Tooltip("Lateral offset from the segment centre-line (world units). " +
             "Cars travelling A→B are displaced to their right (+offset); " +
             "cars travelling B→A are displaced to their right as well (-offset in AB space). " +
             "Set to 0 to disable lane splitting.")]
    [Min(0f)] public float laneOffset = 0.5f;

    // ── Segment Length Override ─────────────────
    [Header("Segment Length Override")]
    [Tooltip("When > 0, overrides the auto-calculated distance between " +
             "intersections A and B. Affects lane-transition smoothness " +
             "(longer = smoother turns), A* path cost, and risk calculations. " +
             "Leave at 0 to use the real geometric distance.")]
    [Min(0f)] public float overrideLength = 0f;

    // ── Crash Generation (Req 4 — eligibility pool) ──
    [Header("Crash Generation")]
    [Tooltip("Tick this to add the segment to the crash-eligible pool. " +
             "Crashes are generated probabilistically (risk-based) only on " +
             "segments in this pool. The flag stays on — it is NOT auto-cleared " +
             "after a crash spawns. Un-tick to remove from the pool.")]
    public bool generateCrash = false;

    // ── Turn Status (Req 2) ───────────────────
    //  Per-lane flag set by a car that is currently turning into this
    //  segment.  Straight-going cars check this before entering and
    //  yield for WaitDuration if true.
    [Header("Turn Status (runtime)")]
    [SerializeField] private bool _isTurningAB = false;
    [SerializeField] private bool _isTurningBA = false;

    /// <summary>Is a car currently turning into the lane toward <paramref name="to"/>?</summary>
    public bool GetIsTurning(RoadIntersection to)
    {
        if (to == intersectionB) return _isTurningAB;
        if (to == intersectionA) return _isTurningBA;
        return false;
    }

    /// <summary>Set/clear the turning flag for the lane toward <paramref name="to"/>.</summary>
    public void SetIsTurning(RoadIntersection to, bool value)
    {
        if (to == intersectionB) _isTurningAB = value;
        else if (to == intersectionA) _isTurningBA = value;
    }

    // ── Linked Tiles (Req 5) ──────────────────
    //  RoadTiles that sit on this segment, ordered by their t-value
    //  (normalised position along A→B).  Populated at runtime by
    //  CarManager.LinkTilesToSegments() calling CollectNearbyTiles().
    [Header("Linked Tiles (auto-populated at runtime)")]
    [SerializeField] private List<RoadTile> _linkedTiles = new List<RoadTile>();
    public IReadOnlyList<RoadTile> LinkedTiles => _linkedTiles;

    // ── Block / Accident State (DIRECTIONAL) ──
    //  A segment carries two opposing lanes:
    //    • A→B (green)  = travel toward intersectionB.
    //    • B→A (orange) = travel toward intersectionA.
    //  Each lane is blocked INDEPENDENTLY so a crash in one lane never
    //  stops the opposite-direction traffic (see Req 2 / Req 4).
    //  Reference counts allow several sources (a crash wreck + queued
    //  cars propagating the jam backward) to block the same lane without
    //  one clearing another's block prematurely.
    [Header("Block State (runtime)")]
    [SerializeField] private int _blockAB;   // refcount: lane toward B (A→B / green) blocked
    [SerializeField] private int _blockBA;   // refcount: lane toward A (B→A / orange) blocked

    // World position of whatever blocks each lane (e.g. a crash scene),
    // so cars already on the segment know where to stop.
    private Vector3 _blockPosAB; private bool _hasPosAB;
    private Vector3 _blockPosBA; private bool _hasPosBA;

    /// <summary>True if EITHER lane is blocked. Use IsBlockedToward for per-lane checks.</summary>
    public bool IsBlocked => _blockAB > 0 || _blockBA > 0;

    // ── Runtime tracking ──────────────────────
    private readonly List<CarAgent> _carsOnSegment = new List<CarAgent>();
    public int CarCount => _carsOnSegment.Count;

    // Device risk reduction is re-computed whenever a device is placed/removed.
    private float _deviceRiskReduction;
    public float DeviceRiskReduction => _deviceRiskReduction;

    // Accident history — timestamps of recent accidents on this segment.
    private readonly List<float> _accidentTimestamps = new List<float>();

    /// <summary>
    /// Number of accidents still within the memory window.
    /// </summary>
    public int RecentAccidentCount
    {
        get
        {
            PruneOldAccidents();
            return _accidentTimestamps.Count;
        }
    }

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
        if (overrideLength > 0f)
        {
            _length = overrideLength;
        }
        else if (intersectionA != null && intersectionB != null)
        {
            _length = Vector3.Distance(intersectionA.transform.position,
                                       intersectionB.transform.position);
        }
        else
        {
            _length = 1f;
        }
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
        // NOTE: sign flipped (was Cross(up, forward)) so A→B traffic now lands on
        // the correct physical side — this was inverted relative to the B→A lane.
        Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
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

    /// <summary>
    /// Finds two cars on this segment travelling in the same direction
    /// (same segmentFrom → segmentTo). Returns true if a pair was found.
    /// carB is ahead, carA is behind.
    /// </summary>
    public bool FindSameDirectionPair(out CarAgent carA, out CarAgent carB)
    {
        carA = null; carB = null;
        if (_carsOnSegment.Count < 2) return false;

        // Group by direction (compare target nodes).
        for (int i = 0; i < _carsOnSegment.Count; i++)
        {
            var ci = _carsOnSegment[i];
            if (ci == null || ci.SegmentTo == null) continue;

            for (int j = i + 1; j < _carsOnSegment.Count; j++)
            {
                var cj = _carsOnSegment[j];
                if (cj == null || cj.SegmentTo == null) continue;
                if (ci.SegmentTo != cj.SegmentTo) continue;

                // Same direction — figure out who is in front.
                // Project both onto the A→B line; higher t = closer to target.
                float ti = GetTAtPosition(ci.transform.position);
                float tj = GetTAtPosition(cj.transform.position);

                // If travelling B→A, higher t means further from target (rear).
                bool towardsB = (ci.SegmentTo == intersectionB);

                if (towardsB)
                {
                    // Higher t = closer to B = front car.
                    carB = (ti >= tj) ? ci : cj;
                    carA = (ti >= tj) ? cj : ci;
                }
                else
                {
                    // Lower t = closer to A = front car.
                    carB = (ti <= tj) ? ci : cj;
                    carA = (ti <= tj) ? cj : ci;
                }
                return true;
            }
        }
        return false;
    }

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

        PruneOldAccidents();

        float risk = baseRisk
                   + speedFactor * 0.1f
                   + _carsOnSegment.Count * densityRiskFactor
                   + complexity * intersectionComplexityFactor
                   + _accidentTimestamps.Count * accidentRiskFactor
                   - _deviceRiskReduction;

        return Mathf.Clamp01(risk);
    }

    // ─────────────────────────────────────────
    //  ACCIDENT HISTORY
    // ─────────────────────────────────────────

    /// <summary>
    /// Records that an accident just happened on this segment.
    /// Its risk contribution decays after accidentMemoryDuration seconds.
    /// </summary>
    public void RecordAccident()
    {
        _accidentTimestamps.Add(Time.time);
    }

    private void PruneOldAccidents()
    {
        float cutoff = Time.time - accidentMemoryDuration;
        _accidentTimestamps.RemoveAll(t => t < cutoff);
    }

    // ─────────────────────────────────────────
    //  DEVICE REDUCTION  (call after any placement change)
    // ─────────────────────────────────────────

    /// <summary>
    /// Re-sums device risk reduction from all child RoadTiles AND linked tiles.
    /// Call this whenever a device is placed or removed on any child tile.
    /// </summary>
    public void RefreshDeviceReduction()
    {
        _deviceRiskReduction = 0f;

        // Original: child tiles
        foreach (var tile in GetComponentsInChildren<RoadTile>())
        {
            foreach (var slot in tile.Slots)
            {
                var stats = DeviceData.Get(slot.deviceType);
                _deviceRiskReduction += stats.accidentReduction * 0.01f;
            }
        }

        // NEW: also count linked tiles (Req 5) that aren't already children
        foreach (var tile in _linkedTiles)
        {
            if (tile == null) continue;
            // Skip if tile is already a child (avoid double-counting)
            if (tile.transform.IsChildOf(transform)) continue;
            foreach (var slot in tile.Slots)
            {
                var stats = DeviceData.Get(slot.deviceType);
                _deviceRiskReduction += stats.accidentReduction * 0.01f;
            }
        }
    }

    // ─────────────────────────────────────────
    //  LINKED TILES  (Req 5)
    //
    //  RoadTiles are spatially linked to their nearest segment at
    //  runtime.  Tiles are ordered by their normalised t-value
    //  along A→B so that CarAgent can walk the list in travel
    //  direction and find devices ahead.
    // ─────────────────────────────────────────

    /// <summary>
    /// Register a tile with this segment.  Tiles are kept sorted by
    /// their A→B t-value.
    /// </summary>
    public void RegisterLinkedTile(RoadTile tile)
    {
        if (tile == null || _linkedTiles.Contains(tile)) return;
        _linkedTiles.Add(tile);
        SortTilesByT();
    }

    public void UnregisterLinkedTile(RoadTile tile)
    {
        _linkedTiles.Remove(tile);
    }

    private void SortTilesByT()
    {
        if (intersectionA == null || intersectionB == null) return;
        _linkedTiles.Sort((a, b) =>
            GetTAtPosition(a.transform.position)
                .CompareTo(GetTAtPosition(b.transform.position)));
    }

    /// <summary>
    /// Scans ALL RoadTiles in the scene and links those whose centre
    /// projects onto this segment within <paramref name="maxLateralDist"/>
    /// world units.  Called once at startup by CarManager.
    /// </summary>
    public void CollectNearbyTiles(float maxLateralDist = 3f)
    {
        _linkedTiles.Clear();
        if (intersectionA == null || intersectionB == null) return;

        foreach (var tile in Object.FindObjectsOfType<RoadTile>())
        {
            if (tile == null) continue;
            float t = GetTAtPosition(tile.transform.position);
            // Must be within the segment span (exclude endpoints)
            if (t < 0.005f || t > 0.995f) continue;

            Vector3 projected = GetPositionAt(t);
            Vector3 diff = tile.transform.position - projected;
            diff.y = 0f; // ignore height difference
            if (diff.magnitude <= maxLateralDist)
                _linkedTiles.Add(tile);
        }
        SortTilesByT();

        if (_linkedTiles.Count > 0)
            Debug.Log($"[RoadSegment] {segmentID}: linked {_linkedTiles.Count} tile(s).");
    }

    /// <summary>
    /// For a car travelling from→to, finds the first tile with a traffic
    /// device AHEAD in the travel direction and returns the stop position
    /// (the centre of the tile immediately BEFORE the device tile, offset
    /// into the correct lane).
    ///
    /// Returns true if a device was found, with out-params set:
    ///   stopPos     — world position where the car should stop
    ///   deviceType  — what kind of device was found
    ///   deviceTile  — the tile holding the device (for debug / wait logic)
    /// </summary>
    public bool TryGetDeviceStopInfo(
        RoadIntersection from,
        RoadIntersection to,
        out Vector3 stopPos,
        out TrafficDeviceType deviceType,
        out RoadTile deviceTile)
    {
        stopPos = Vector3.zero;
        deviceType = TrafficDeviceType.None;
        deviceTile = null;

        if (_linkedTiles.Count == 0 || from == null || to == null) return false;

        bool travelAtoB = (from == intersectionA && to == intersectionB);
        bool travelBtoA = (from == intersectionB && to == intersectionA);
        if (!travelAtoB && !travelBtoA) return false;

        // Build a travel-order list (A→B is already sorted ascending t)
        List<RoadTile> ordered;
        if (travelAtoB)
        {
            ordered = new List<RoadTile>(_linkedTiles);
        }
        else
        {
            ordered = new List<RoadTile>(_linkedTiles);
            ordered.Reverse();
        }

        // Walk tiles in travel order and find the first with a placed device
        for (int i = 0; i < ordered.Count; i++)
        {
            RoadTile tile = ordered[i];
            if (tile == null || tile.PlacedCount == 0) continue;

            // Determine which device type is on this tile
            TrafficDeviceType foundType = TrafficDeviceType.None;
            foreach (var slot in tile.Slots)
            {
                if (slot.deviceType != TrafficDeviceType.None)
                {
                    foundType = slot.deviceType;
                    break;
                }
            }
            if (foundType == TrafficDeviceType.None) continue;

            deviceType = foundType;
            deviceTile = tile;

            // Stop position = centre of the tile BEFORE this one in travel order
            Vector3 laneOffset = GetLaneOffsetVector(from, to);
            if (i > 0)
            {
                RoadTile stopTile = ordered[i - 1];
                stopPos = stopTile.transform.position + laneOffset;
            }
            else
            {
                // Device is on the very first tile — stop at entry point
                stopPos = from.transform.position + laneOffset;
            }

            return true;
        }

        return false;
    }

    // ─────────────────────────────────────────
    //  BLOCKING  (DIRECTIONAL / per-lane)
    // ─────────────────────────────────────────

    /// <summary>
    /// Is the lane heading toward <paramref name="to"/> blocked?
    /// (to == intersectionB → A→B/green lane; to == intersectionA → B→A/orange lane.)
    /// </summary>
    public bool IsBlockedToward(RoadIntersection to)
    {
        if (to == intersectionB) return _blockAB > 0;
        if (to == intersectionA) return _blockBA > 0;
        return false;
    }

    /// <summary>True if the lane toward <paramref name="to"/> has a known block position.</summary>
    public bool HasBlockPositionToward(RoadIntersection to)
    {
        if (to == intersectionB) return _blockAB > 0 && _hasPosAB;
        if (to == intersectionA) return _blockBA > 0 && _hasPosBA;
        return false;
    }

    /// <summary>World position of the block on the lane toward <paramref name="to"/>.</summary>
    public Vector3 BlockPositionToward(RoadIntersection to)
    {
        if (to == intersectionB) return _blockPosAB;
        if (to == intersectionA) return _blockPosBA;
        return transform.position;
    }

    /// <summary>
    /// Block / unblock the single lane that travels toward <paramref name="to"/>.
    /// Reference-counted: each true must be balanced by a matching false.
    /// </summary>
    public void SetBlockedToward(RoadIntersection to, bool blocked,
                                 Vector3 worldPosition, bool hasPosition = true)
    {
        bool ab;
        if (to == intersectionB) ab = true;
        else if (to == intersectionA) ab = false;
        else { Debug.LogWarning($"[RoadSegment] {segmentID}: SetBlockedToward node not an endpoint."); return; }

        if (ab)
        {
            if (blocked) { _blockAB++; _blockPosAB = worldPosition; _hasPosAB = hasPosition; }
            else { _blockAB = Mathf.Max(0, _blockAB - 1); if (_blockAB == 0) _hasPosAB = false; }
        }
        else
        {
            if (blocked) { _blockBA++; _blockPosBA = worldPosition; _hasPosBA = hasPosition; }
            else { _blockBA = Mathf.Max(0, _blockBA - 1); if (_blockBA == 0) _hasPosBA = false; }
        }

        Debug.Log($"[RoadSegment] {segmentID}: lane→{(to != null ? to.intersectionID : "?")} " +
                  $"blocked={blocked} (AB={_blockAB}, BA={_blockBA})");
    }

    /// <summary>Convenience overload without a stop position.</summary>
    public void SetBlockedToward(RoadIntersection to, bool blocked)
        => SetBlockedToward(to, blocked, transform.position, hasPosition: false);

    // ── Legacy whole-segment blocking (both lanes) ────────────────────
    //  Kept for the legacy solo-crash path and watchdog cleanup. Blocks
    //  or unblocks BOTH lanes at once; routes through the refcounted
    //  directional API so accounting stays consistent.
    public void SetBlocked(bool blocked)
    {
        SetBlockedToward(intersectionB, blocked, transform.position, hasPosition: false);
        SetBlockedToward(intersectionA, blocked, transform.position, hasPosition: false);
    }

    public void SetBlocked(bool blocked, Vector3 worldPosition)
    {
        SetBlockedToward(intersectionB, blocked, worldPosition, hasPosition: true);
        SetBlockedToward(intersectionA, blocked, worldPosition, hasPosition: true);
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

        Gizmos.color = IsBlocked
            ? new Color(1f, 0.1f, 0.1f, 0.9f)
            : new Color(0.3f, 0.8f, 1f, 0.7f);

        Gizmos.DrawLine(intersectionA.transform.position, intersectionB.transform.position);

        // Draw lane offset lines when offset is non-zero
        // NOTE: sign flipped (was Cross(up, ab)) to match GetLaneOffsetVector's
        // corrected A→B / B→A convention. Green = A→B lane, Orange = B→A lane.
        if (laneOffset > 0f)
        {
            Vector3 ab = intersectionB.transform.position - intersectionA.transform.position;
            Vector3 right = Vector3.Cross(ab.normalized, Vector3.up).normalized * laneOffset;

            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);   // Green = A→B lane
            Gizmos.DrawLine(intersectionA.transform.position + right,
                            intersectionB.transform.position + right);

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);   // Orange = B→A lane
            Gizmos.DrawLine(intersectionA.transform.position - right,
                            intersectionB.transform.position - right);
        }

        Vector3 mid = (intersectionA.transform.position + intersectionB.transform.position) * 0.5f;

        string extra = "";
        if (generateCrash) extra += " [CRASH POOL]";
        if (_isTurningAB) extra += " [TURN→B]";
        if (_isTurningBA) extra += " [TURN→A]";
        if (_linkedTiles.Count > 0) extra += $" Tiles:{_linkedTiles.Count}";
        if (overrideLength > 0f) extra += $" OvrLen:{overrideLength:F1}";

        UnityEditor.Handles.Label(
            mid + Vector3.up * 0.5f,
            $"{segmentID}\nCars:{_carsOnSegment.Count}  Risk:{CalculateRisk(speedLimit):F2}" +
            (IsBlocked ? " [BLOCKED]" : "") + extra
        );
    }
#endif
}