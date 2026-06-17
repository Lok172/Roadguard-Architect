using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR AGENT  (v6 — junction + lane-transition + crash-stop fixes)
//
//  KEY CHANGES vs v5:
//    • Issue 3 — LANE TRANSITION: when entering a segment whose
//      laneOffset differs from the previous one, the car smoothly
//      interpolates its lateral position over laneTransitionDistance
//      world-units instead of jumping.
//    • Issue 4 — JUNCTION RESERVATION: at intersections marked
//      isJunction the car checks whether it's turning. Turning
//      cars must reserve the junction (one at a time); straight-
//      through cars pass freely.
//    • Issue 5 — CRASH STOP DIRECTION: a car that stops for a
//      crash scene ahead maintains its original travel direction
//      (segmentFrom→segmentTo), never flipping 180°.
//    • Issue 6 — JUNCTION OVERLAP: before entering a segment at a
//      junction node, the car checks that no other car is already
//      near that lane-start position. If occupied, it waits.
// ─────────────────────────────────────────────────────────────────

public class CarAgent : MonoBehaviour
{
    // ── Movement ──────────────────────────────
    [Header("Movement")]
    [Tooltip("Default cruising speed (units/sec). Clamped to segment speedLimit.")]
    public float baseSpeed = 8f;

    [Tooltip("How close to the laned target before triggering arrival.")]
    public float arrivalThreshold = 0.25f;

    // ── Car Following ─────────────────────────
    [Header("Car Following")]
    [Tooltip("Distance ahead (from car front) to check for cars on the same lane.")]
    public float followCheckDistance = 8f;

    [Tooltip("Speed multiplier when following a car ahead (0=stop, 1=full speed).")]
    [Range(0f, 1f)] public float followSpeedMultiplier = 0.3f;

    [Tooltip("Layer mask that contains the Car layer.")]
    public LayerMask carLayerMask;

    // ── Traffic Light ─────────────────────────
    [Header("Traffic Light")]
    [Tooltip("Base stop time n. Both-corner edge = n sec; one-corner edge = 2n sec.")]
    public float trafficLightWaitN = 3f;

    // ── Speed Bump ────────────────────────────
    [Header("Speed Bump")]
    public float speedBumpSpeed = 2f;
    public float speedBumpTransitionTime = 0.5f;

    // ── Stop Sign ─────────────────────────────
    [Header("Stop Sign")]
    [Range(0f, 1f)] public float stopSignStopChance = 0.5f;
    public float stopSignDuration = 2f;

    // ── Blocked Segment ───────────────────────
    [Header("Blocked Segment")]
    public float rerouteCheckInterval = 2f;

    // ── Random Walk ────────────────────────────
    [Header("Random Walk")]
    [Tooltip("Pick a random direction at every junction instead of following an " +
             "A* route to a destination. Set per spawn by CarManager.")]
    public bool randomWalk = false;

    // ── Crash Ahead ───────────────────────────
    [Header("Crash Ahead")]
    [Tooltip("Gap left between this car's nose and a crash scene blocking the " +
             "lane ahead (world units). The car waits here until the crash clears.")]
    [Min(0f)] public float blockStopGap = 0.6f;

    // ── Lane Transition (Issue 3) ─────────────
    [Header("Lane Transition")]
    [Tooltip("World-units over which to smoothly interpolate the lateral lane " +
             "position when transitioning between segments with different laneOffset " +
             "values. Creates an inclined merge path instead of an abrupt jump.")]
    [Min(0.5f)] public float laneTransitionDistance = 3f;

    // ── Junction Overlap (Issue 6) ─────────────
    [Header("Junction Overlap")]
    [Tooltip("Minimum distance from another car at a junction lane-start before " +
             "this car will enter the segment. Prevents vehicles overlapping.")]
    [Min(0.5f)] public float junctionClearance = 2.5f;

    [Tooltip("How often (seconds) to re-check if the junction lane-start is clear.")]
    [Min(0.1f)] public float junctionRetryInterval = 0.5f;

    // ── Runtime (read-only Inspector) ─────────
    [Header("Runtime (read-only)")]
    [SerializeField] private float _currentSpeed;
    [SerializeField] private bool _isCrashed;
    [SerializeField] private bool _isStopped;
    [SerializeField] private bool _isQueued;
    [SerializeField] private RoadIntersection _currentNode;
    [SerializeField] private RoadIntersection _targetNode;
    [SerializeField] private RoadIntersection _destination;
    [SerializeField] private RoadSegment _currentSegment;

    // ── Internal ──────────────────────────────
    private List<RoadIntersection> _path = new List<RoadIntersection>();
    private int _pathIndex = 1;
    private List<RoadIntersection> _allNodes;
    private List<RoadIntersection> _endPoints;
    private readonly HashSet<RoadTile> _processingTiles = new HashSet<RoadTile>();
    private bool _active;
    private bool _inArrival;
    private Rigidbody _rb;
    private RoadIntersection _segmentFrom;
    private RoadIntersection _segmentTo;
    private bool _hasMovedOnce;   // random-walk: true once the car has left its spawn node

    // ── Crash-driving state ───────────────────
    private bool _isCrashRear;
    private bool _isCrashFront;
    private CarAgent _crashPartner;
    private Vector3 _snapPoint;
    private RoadSegment _crashSegment;
    private float _crashRearSpeed;

    // ── Lane transition state (Issue 3) ───────
    private bool _inLaneTransition;
    private Vector3 _transitionWaypoint;
    private Vector3 _prevLaneOffsetVec;   // lane offset of the PREVIOUS segment
    private Vector3 _newLaneOffsetVec;    // lane offset of the CURRENT (new) segment

    // ── Junction reservation state (Issue 4) ──
    private RoadIntersection _reservedJunction;   // junction we currently hold
    private RoadSegment _prevSegmentForJunction;  // incoming segment at junction (for turn detection)

    // ─────────────────────────────────────────
    //  INITIALISE  (normal traffic)
    // ─────────────────────────────────────────

    public void Initialise(RoadIntersection startNode,
                           List<RoadIntersection> allNodes,
                           List<RoadIntersection> endPoints)
    {
        _allNodes = allNodes;
        _endPoints = endPoints;
        transform.localScale = Vector3.one;
        _currentNode = startNode;
        _currentSegment = null;
        _segmentFrom = null;
        _segmentTo = null;
        _isCrashed = false;
        _isStopped = false;
        _isQueued = false;
        _inArrival = false;
        _isCrashRear = false;
        _isCrashFront = false;
        _crashPartner = null;
        _active = true;
        _currentSpeed = baseSpeed;
        _path.Clear();
        _pathIndex = 1;
        _processingTiles.Clear();
        _hasMovedOnce = false;
        _inLaneTransition = false;
        ReleaseAnyJunction();
        _prevSegmentForJunction = null;
        if (randomWalk) StepRandomWalk(null);
        else PickNewDestinationAndRoute();
    }

    // ─────────────────────────────────────────
    //  INITIALISE  (crash-front car)
    // ─────────────────────────────────────────

    public void InitialiseAsCrashFront(RoadSegment seg,
                                        RoadIntersection from,
                                        RoadIntersection to,
                                        float speed)
    {
        ResetState();
        _isCrashFront = true;
        _active = true;
        _currentSegment = seg;
        _segmentFrom = from;
        _segmentTo = to;
        _targetNode = to;
        _crashSegment = seg;
        _currentSpeed = speed;
        seg.RegisterCar(this);

        Vector3 dir = (to.transform.position - transform.position).normalized;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    // ─────────────────────────────────────────
    //  INITIALISE  (crash-rear car)
    // ─────────────────────────────────────────

    public void InitialiseAsCrashRear(CarAgent frontCar,
                                       RoadSegment seg,
                                       RoadIntersection from,
                                       RoadIntersection to,
                                       float speed)
    {
        ResetState();
        _isCrashRear = true;
        _crashPartner = frontCar;
        _active = true;
        _currentSegment = seg;
        _segmentFrom = from;
        _segmentTo = to;
        _crashSegment = seg;
        _crashRearSpeed = speed;
        _currentSpeed = speed;
        seg.RegisterCar(this);

        Vector3 dir = (frontCar.transform.position - transform.position).normalized;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    private void ResetState()
    {
        transform.localScale = Vector3.one;
        _isCrashed = false;
        _isStopped = false;
        _isQueued = false;
        _inArrival = false;
        _isCrashRear = false;
        _isCrashFront = false;
        _crashPartner = null;
        _inLaneTransition = false;
        _path.Clear();
        _pathIndex = 1;
        _processingTiles.Clear();
        ReleaseAnyJunction();
        _prevSegmentForJunction = null;
    }

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; }
    }

    private void Update()
    {
        if (CarManager.IsDragging) return;
        if (!_active || _isCrashed || _isStopped || _isQueued) return;

        if (_isCrashRear)
        {
            UpdateCrashRear();
            return;
        }

        if (_isCrashFront)
        {
            UpdateCrashFront();
            return;
        }

        if (_inArrival) return;
        if (_targetNode == null) return;
        MoveTowardTarget();
    }

    // ─────────────────────────────────────────
    //  CRASH DRIVING — REAR CAR
    // ─────────────────────────────────────────

    private void UpdateCrashRear()
    {
        if (_crashPartner == null) return;

        _snapPoint = GetRearBumperPosition(_crashPartner);

        Vector3 dir = _snapPoint - GetFrontBumperPosition(this);
        float dist = dir.magnitude;

        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float step = _crashRearSpeed * Time.deltaTime;

        if (dist <= arrivalThreshold || step >= dist)
        {
            transform.position += dir;
            OnCrashImpact();
        }
        else
        {
            transform.position += dir.normalized * step;
        }
    }

    // ─────────────────────────────────────────
    //  CRASH DRIVING — FRONT CAR
    // ─────────────────────────────────────────

    private void UpdateCrashFront()
    {
        if (_targetNode == null) return;

        Vector3 laneTarget = LaneTargetFor(_targetNode, _segmentFrom, _segmentTo, _currentSegment);
        Vector3 dir = laneTarget - transform.position;
        float dist = dir.magnitude;

        if (dist > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float step = _currentSpeed * Time.deltaTime;

        if (dist <= arrivalThreshold || step >= dist)
        {
            transform.position = laneTarget;
            _isStopped = true;
        }
        else
        {
            transform.position += dir.normalized * step;
        }
    }

    // ─────────────────────────────────────────
    //  CRASH IMPACT
    // ─────────────────────────────────────────

    private void OnCrashImpact()
    {
        _isCrashRear = false;
        _isCrashed = true;
        _isStopped = true;
        StopAllCoroutines();

        if (_crashPartner != null)
        {
            _crashPartner._isCrashFront = false;
            _crashPartner._isCrashed = true;
            _crashPartner._isStopped = true;
            _crashPartner.StopAllCoroutines();
        }

        if (_crashSegment != null)
        {
            Vector3 wreckPos = _crashPartner != null
                ? (transform.position + _crashPartner.transform.position) * 0.5f
                : transform.position;
            _crashSegment.SetBlocked(true, wreckPos);
        }

        CarManager.Instance?.OnCrashImpact(this, _crashPartner, _crashSegment);
    }

    // ─────────────────────────────────────────
    //  MOVEMENT  (normal traffic)
    // ─────────────────────────────────────────

    private void MoveTowardTarget()
    {
        float speed = _currentSpeed;

        // ── Issue 3: Lane transition — drive toward waypoint first ────
        if (_inLaneTransition)
        {
            Vector3 wdir = _transitionWaypoint - transform.position;
            float wdist = wdir.magnitude;

            if (wdist <= arrivalThreshold)
            {
                // Transition complete — snap and continue normally.
                transform.position = _transitionWaypoint;
                _inLaneTransition = false;
            }
            else
            {
                // ── Stop for a crash scene even during transition ─────
                if (TryGetBlockStopPoint(_transitionWaypoint, out Vector3 blockStop))
                {
                    DriveTowardBlockStop(blockStop, speed);
                    return;
                }

                // Car-following raycast.
                speed = ApplyCarFollowing(speed);

                if (wdist > 0.01f)
                    transform.rotation = Quaternion.LookRotation(wdir.normalized, Vector3.up);

                float step = speed * Time.deltaTime;
                transform.position += wdir.normalized * Mathf.Min(step, wdist);
                return;
            }
        }

        Vector3 laneTarget = LaneTargetFor(_targetNode, _segmentFrom, _segmentTo, _currentSegment);

        // ── Issue 5: Stop for a crash scene — preserve forward direction ──
        if (TryGetBlockStopPoint(laneTarget, out Vector3 bStop))
        {
            DriveTowardBlockStop(bStop, speed);
            return;
        }

        // Car-following raycast.
        speed = ApplyCarFollowing(speed);

        Vector3 dir = laneTarget - transform.position;
        float dist = dir.magnitude;

        if (dist > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float moveStep = speed * Time.deltaTime;

        if (dist <= arrivalThreshold || moveStep >= dist)
        {
            transform.position = laneTarget;
            if (!_inArrival)
            {
                _inArrival = true;
                OnReachedNode(_targetNode);
            }
        }
        else
        {
            transform.position += dir.normalized * moveStep;
        }
    }

    // ─────────────────────────────────────────
    //  Issue 5 — CRASH STOP HELPER
    //
    //  Drives toward the block stop point while PRESERVING
    //  the car's travel direction (segmentFrom → segmentTo).
    //  The car never flips 180° when the crash is at its position.
    // ─────────────────────────────────────────

    private void DriveTowardBlockStop(Vector3 blockStop, float speed)
    {
        Vector3 bdir = blockStop - transform.position;
        float bdist = bdir.magnitude;

        // ── DIRECTION FIX: face along the segment, not toward the block point. ──
        // This prevents the car from flipping when the crash is very close or
        // exactly at its position. The travel direction (from → to) is the
        // ground truth for which way the car should face.
        if (_segmentFrom != null && _segmentTo != null)
        {
            Vector3 travelDir = (_segmentTo.transform.position -
                                 _segmentFrom.transform.position).normalized;
            if (travelDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);
        }

        if (bdist > arrivalThreshold)
            transform.position += bdir.normalized * Mathf.Min(speed * Time.deltaTime, bdist);
        // else: hold here — do NOT advance
    }

    /// <summary>
    /// Applies car-following raycast and returns the adjusted speed.
    /// </summary>
    private float ApplyCarFollowing(float speed)
    {
        Bounds b = GetComponentInChildren<Renderer>()?.bounds
                            ?? new Bounds(transform.position, Vector3.one);
        Vector3 rayOrigin = transform.position + transform.forward * (b.extents.z + 0.1f);
        if (Physics.Raycast(rayOrigin, transform.forward,
                            out RaycastHit hit, followCheckDistance, carLayerMask))
        {
            if (hit.collider.GetComponent<CarAgent>() != null)
                speed *= followSpeedMultiplier;
        }
        return speed;
    }

    private static Vector3 LaneTargetFor(RoadIntersection node,
                                          RoadIntersection from,
                                          RoadIntersection to,
                                          RoadSegment seg)
    {
        if (seg != null && from != null && to != null)
            return node.transform.position + seg.GetLaneOffsetVector(from, to);
        return node.transform.position;
    }

    /// <summary>
    /// If the current segment is blocked by a crash scene that lies AHEAD of
    /// us (between this car and its target), returns the point to stop at —
    /// just short of the wreck. Returns false when the segment isn't blocked,
    /// the wreck position is unknown, or the block is behind us.
    /// </summary>
    private bool TryGetBlockStopPoint(Vector3 laneTarget, out Vector3 stopPoint)
    {
        stopPoint = default;
        if (_currentSegment == null || !_currentSegment.IsBlocked) return false;
        if (!_currentSegment.HasBlockPosition) return false;

        // ── Issue 5 FIX: Use the segment direction for the "forward" test,
        //    NOT the direction to the lane target. This guarantees a stable
        //    forward reference that never flips, even if the lane target is
        //    behind the block. ──
        Vector3 fwd;
        if (_segmentFrom != null && _segmentTo != null)
            fwd = (_segmentTo.transform.position - _segmentFrom.transform.position).normalized;
        else
        {
            Vector3 toTarget = laneTarget - transform.position;
            fwd = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
        }

        Vector3 toBlock = _currentSegment.BlockPosition - transform.position;
        float along = Vector3.Dot(toBlock, fwd);
        if (along <= 0f) return false;   // wreck is behind us — keep going

        float clearance = HalfLengthAlongForward(this) + blockStopGap;
        stopPoint = transform.position + fwd * Mathf.Max(0f, along - clearance);
        return true;
    }

    // ─────────────────────────────────────────
    //  NODE ARRIVAL
    // ─────────────────────────────────────────

    private void OnReachedNode(RoadIntersection node)
    {
        RoadIntersection arrivedFrom = _segmentFrom;

        // ── Issue 3: Capture outgoing lane offset BEFORE clearing segment state,
        //    so EnterSegment can compare it with the new segment's offset. ──
        _prevLaneOffsetVec = Vector3.zero;
        _prevSegmentForJunction = _currentSegment;   // Issue 4: remember incoming segment
        if (_currentSegment != null && _segmentFrom != null && _segmentTo != null)
            _prevLaneOffsetVec = _currentSegment.GetLaneOffsetVector(_segmentFrom, _segmentTo);

        if (_currentSegment != null)
        {
            _currentSegment.UnregisterCar(this);
            _currentSegment = null;
            _segmentFrom = null;
            _segmentTo = null;
        }

        _currentNode = node;
        _inArrival = false;

        // Release any junction we held on the previous node.
        ReleaseAnyJunction();

        if (randomWalk) { StepRandomWalk(arrivedFrom); return; }

        if (node == _destination || _pathIndex >= _path.Count)
        {
            PickNewDestinationAndRoute();
            return;
        }

        AdvanceAlongPath();
    }

    // ─────────────────────────────────────────
    //  PATH ADVANCE
    // ─────────────────────────────────────────

    private void AdvanceAlongPath()
    {
        if (_pathIndex >= _path.Count)
        {
            PickNewDestinationAndRoute();
            return;
        }

        RoadIntersection nextNode = _path[_pathIndex];
        RoadSegment seg = _currentNode.SegmentTo(nextNode);

        if (seg == null)
        {
            PickNewDestinationAndRoute();
            return;
        }

        if (seg.IsBlocked)
        {
            StartCoroutine(WaitForSegmentOrReroute(seg));
            return;
        }

        // ── Issue 4: Junction reservation ────────────────────────────
        if (_currentNode.isJunction)
        {
            bool isTurn = _currentNode.IsTurn(_prevSegmentForJunction, seg);
            if (isTurn)
            {
                if (!_currentNode.TryReserveJunction(this))
                {
                    // Junction is in use by another turning car — wait.
                    StartCoroutine(WaitForJunctionReservation(_currentNode, seg, nextNode));
                    return;
                }
                _reservedJunction = _currentNode;
            }
        }

        // ── Issue 6: Junction overlap avoidance ──────────────────────
        if (_currentNode.isJunction)
        {
            if (IsLaneStartOccupied(_currentNode, seg, nextNode))
            {
                StartCoroutine(WaitForLaneStartClear(seg, nextNode));
                return;
            }
        }

        EnterSegment(seg, _currentNode, nextNode);
        _pathIndex++;
    }

    private void EnterSegment(RoadSegment seg, RoadIntersection from, RoadIntersection next)
    {
        _currentSegment = seg;
        _segmentFrom = from;
        _segmentTo = next;
        _targetNode = next;
        _currentSpeed = Mathf.Min(baseSpeed, seg.speedLimit);
        seg.RegisterCar(this);

        // ── Issue 3: Detect lane offset change and set up transition ──
        _newLaneOffsetVec = seg.GetLaneOffsetVector(from, next);

        if ((_prevLaneOffsetVec - _newLaneOffsetVec).sqrMagnitude > 0.01f)
        {
            // Offsets differ — create a smooth transition waypoint.
            // The waypoint sits laneTransitionDistance into the new segment,
            // at the correct new-lane offset. The car drives diagonally from
            // its current position (still at the old offset) to this waypoint,
            // producing the smooth inclined path.
            float transitionDist = Mathf.Min(laneTransitionDistance, seg.Length * 0.4f);
            float transitionT = transitionDist / Mathf.Max(0.01f, seg.Length);

            // Direction-aware t: if travelling A→B, t goes 0→1; B→A, 1→0.
            bool towardsB = (next == seg.intersectionB);
            float sampleT = towardsB ? transitionT : (1f - transitionT);

            _transitionWaypoint = seg.GetPositionAt(sampleT) + _newLaneOffsetVec;
            _inLaneTransition = true;
        }
        else
        {
            _inLaneTransition = false;
        }
    }

    // ─────────────────────────────────────────
    //  Issue 4 — JUNCTION RESERVATION WAIT
    // ─────────────────────────────────────────

    private IEnumerator WaitForJunctionReservation(RoadIntersection junction,
                                                    RoadSegment seg,
                                                    RoadIntersection nextNode)
    {
        _isStopped = true;

        while (_active && !_isCrashed)
        {
            yield return new WaitForSeconds(junctionRetryInterval);
            if (junction.TryReserveJunction(this))
            {
                _reservedJunction = junction;
                break;
            }
        }

        _isStopped = false;

        if (!_active || _isCrashed) yield break;

        // Re-check that the segment is still available.
        if (seg.IsBlocked)
        {
            ReleaseAnyJunction();
            StartCoroutine(WaitForSegmentOrReroute(seg));
            yield break;
        }

        // Issue 6: Also check overlap after reservation acquired.
        if (junction.isJunction && IsLaneStartOccupied(junction, seg, nextNode))
        {
            StartCoroutine(WaitForLaneStartClear(seg, nextNode));
            yield break;
        }

        EnterSegment(seg, _currentNode, nextNode);
        _pathIndex++;
    }

    /// <summary>Releases the junction this car currently holds (if any).</summary>
    private void ReleaseAnyJunction()
    {
        if (_reservedJunction != null)
        {
            _reservedJunction.ReleaseJunction(this);
            _reservedJunction = null;
        }
    }

    // ─────────────────────────────────────────
    //  Issue 6 — JUNCTION OVERLAP AVOIDANCE
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns true if another car is currently near the lane-start position
    /// on <paramref name="seg"/> when entering from <paramref name="node"/>.
    /// </summary>
    private bool IsLaneStartOccupied(RoadIntersection node, RoadSegment seg,
                                      RoadIntersection nextNode)
    {
        Vector3 laneStart = node.transform.position + seg.GetLaneOffsetVector(node, nextNode);
        float sqrThreshold = junctionClearance * junctionClearance;

        foreach (var car in seg.CarsOnSegment)
        {
            if (car == null || car == this) continue;
            if ((car.transform.position - laneStart).sqrMagnitude < sqrThreshold)
                return true;
        }

        // Also check cars on other segments connected to this node.
        foreach (var otherSeg in node.ConnectedSegments)
        {
            if (otherSeg == null || otherSeg == seg) continue;
            foreach (var car in otherSeg.CarsOnSegment)
            {
                if (car == null || car == this) continue;
                if ((car.transform.position - laneStart).sqrMagnitude < sqrThreshold)
                    return true;
            }
        }
        return false;
    }

    private IEnumerator WaitForLaneStartClear(RoadSegment seg, RoadIntersection nextNode)
    {
        _isStopped = true;

        while (_active && !_isCrashed)
        {
            yield return new WaitForSeconds(junctionRetryInterval);
            if (!IsLaneStartOccupied(_currentNode, seg, nextNode))
                break;
        }

        _isStopped = false;

        if (!_active || _isCrashed) yield break;

        if (seg.IsBlocked)
        {
            ReleaseAnyJunction();
            StartCoroutine(WaitForSegmentOrReroute(seg));
            yield break;
        }

        EnterSegment(seg, _currentNode, nextNode);
        _pathIndex++;
    }

    // ─────────────────────────────────────────
    //  ROUTING
    // ─────────────────────────────────────────

    private void PickNewDestinationAndRoute()
    {
        var candidates = new List<RoadIntersection>();
        var pool = (_endPoints != null && _endPoints.Count > 0) ? _endPoints : _allNodes;
        foreach (var n in pool)
            if (n != null && n != _currentNode) candidates.Add(n);

        if (candidates.Count == 0) { Despawn(); return; }

        _destination = candidates[Random.Range(0, candidates.Count)];
        _path = RoadGraph.FindPath(_currentNode, _destination);
        _pathIndex = 1;

        if (_path.Count < 2)
        {
            Debug.LogWarning($"[CarAgent] No path from {_currentNode?.intersectionID} " +
                             $"to {_destination?.intersectionID}. Despawning.");
            Despawn();
            return;
        }

        AdvanceAlongPath();
    }

    // ─────────────────────────────────────────
    //  RANDOM WALK  (random turns at every junction)
    // ─────────────────────────────────────────

    private void StepRandomWalk(RoadIntersection arrivedFrom)
    {
        bool isEndpoint = _endPoints != null && _endPoints.Count > 0
                          && _endPoints.Contains(_currentNode);
        if (isEndpoint && _hasMovedOnce) { Despawn(); return; }

        var segs = new List<RoadSegment>();
        var tos = new List<RoadIntersection>();
        foreach (var seg in _currentNode.ConnectedSegments)
        {
            if (seg == null || seg.IsBlocked) continue;
            var other = seg.Other(_currentNode);
            if (other == null || other == arrivedFrom) continue;
            segs.Add(seg); tos.Add(other);
        }

        if (segs.Count == 0 && arrivedFrom != null)
        {
            var back = _currentNode.SegmentTo(arrivedFrom);
            if (back != null && !back.IsBlocked) { segs.Add(back); tos.Add(arrivedFrom); }
        }

        if (segs.Count == 0) { StartCoroutine(RetryRandomWalk()); return; }

        int pick = Random.Range(0, segs.Count);

        // ── Issue 4: Junction check for random walk too ──────────────
        if (_currentNode.isJunction)
        {
            bool isTurn = _currentNode.IsTurn(_prevSegmentForJunction, segs[pick]);
            if (isTurn && !_currentNode.TryReserveJunction(this))
            {
                StartCoroutine(WaitForJunctionThenRandomWalk(_currentNode, arrivedFrom));
                return;
            }
            if (isTurn) _reservedJunction = _currentNode;

            // Issue 6: overlap check.
            if (IsLaneStartOccupied(_currentNode, segs[pick], tos[pick]))
            {
                StartCoroutine(WaitForLaneStartClearRandomWalk(segs[pick], tos[pick], arrivedFrom));
                return;
            }
        }

        _hasMovedOnce = true;
        EnterSegment(segs[pick], _currentNode, tos[pick]);
    }

    private IEnumerator WaitForJunctionThenRandomWalk(RoadIntersection junction,
                                                       RoadIntersection arrivedFrom)
    {
        _isStopped = true;
        while (_active && !_isCrashed)
        {
            yield return new WaitForSeconds(junctionRetryInterval);
            if (junction.TryReserveJunction(this))
            {
                _reservedJunction = junction;
                break;
            }
        }
        _isStopped = false;
        if (_active && !_isCrashed) StepRandomWalk(arrivedFrom);
    }

    private IEnumerator WaitForLaneStartClearRandomWalk(RoadSegment seg,
                                                         RoadIntersection nextNode,
                                                         RoadIntersection arrivedFrom)
    {
        _isStopped = true;
        while (_active && !_isCrashed)
        {
            yield return new WaitForSeconds(junctionRetryInterval);
            if (!IsLaneStartOccupied(_currentNode, seg, nextNode)) break;
        }
        _isStopped = false;
        if (_active && !_isCrashed)
        {
            _hasMovedOnce = true;
            EnterSegment(seg, _currentNode, nextNode);
        }
    }

    private IEnumerator RetryRandomWalk()
    {
        _isQueued = true;
        yield return new WaitForSeconds(rerouteCheckInterval);
        _isQueued = false;
        if (_active && !_isCrashed) StepRandomWalk(null);
    }

    // ─────────────────────────────────────────
    //  BLOCKED SEGMENT QUEUE
    // ─────────────────────────────────────────

    private IEnumerator WaitForSegmentOrReroute(RoadSegment seg)
    {
        _isQueued = true;

        while (seg.IsBlocked && _active && !_isCrashed)
        {
            yield return new WaitForSeconds(rerouteCheckInterval);
            if (!seg.IsBlocked) break;

            var alt = RoadGraph.FindPath(_currentNode, _destination);
            if (alt.Count >= 2)
            {
                _path = alt;
                _pathIndex = 1;
                _isQueued = false;
                AdvanceAlongPath();
                yield break;
            }
        }

        _isQueued = false;
        if (_active && !_isCrashed) AdvanceAlongPath();
    }

    // ─────────────────────────────────────────
    //  TILE TRIGGER
    // ─────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_isCrashed || _isCrashRear || _isCrashFront) return;
        RoadTile tile = other.GetComponent<RoadTile>();
        if (tile == null || _processingTiles.Contains(tile)) return;
        _processingTiles.Add(tile);
        StartCoroutine(HandleTileEntry(tile));
    }

    private void OnTriggerExit(Collider other)
    {
        RoadTile tile = other.GetComponent<RoadTile>();
        if (tile != null) _processingTiles.Remove(tile);
    }

    private IEnumerator HandleTileEntry(RoadTile tile)
    {
        float lightWait = GetTrafficLightWait(tile);
        if (lightWait > 0f)
        {
            _isStopped = true;
            yield return new WaitForSeconds(lightWait);
            _isStopped = false;
        }

        if (tile.HasDeviceAtCorner(TileCorner.Center, TrafficDeviceType.SpeedBump))
            yield return StartCoroutine(SpeedBumpRoutine());

        if (tile.HasAnyDeviceOfType(TrafficDeviceType.StopSign))
        {
            if (Random.value <= stopSignStopChance)
            {
                _isStopped = true;
                yield return new WaitForSeconds(stopSignDuration);
                _isStopped = false;
            }
        }
    }

    private float GetTrafficLightWait(RoadTile tile)
    {
        var edges = new (TileCorner a, TileCorner b)[]
        {
            (TileCorner.NorthWest, TileCorner.NorthEast),
            (TileCorner.SouthWest, TileCorner.SouthEast),
            (TileCorner.NorthWest, TileCorner.SouthWest),
            (TileCorner.NorthEast, TileCorner.SouthEast),
        };

        int best = 0;
        foreach (var edge in edges)
        {
            int c = (tile.HasDeviceAtCorner(edge.a, TrafficDeviceType.TrafficLight) ? 1 : 0)
                  + (tile.HasDeviceAtCorner(edge.b, TrafficDeviceType.TrafficLight) ? 1 : 0);
            if (c > best) best = c;
        }

        if (best == 0) return 0f;
        return best == 2 ? trafficLightWaitN : trafficLightWaitN * 2f;
    }

    private IEnumerator SpeedBumpRoutine()
    {
        float elapsed = 0f, start = _currentSpeed;
        while (elapsed < speedBumpTransitionTime)
        {
            elapsed += Time.deltaTime;
            _currentSpeed = Mathf.Lerp(start, speedBumpSpeed, elapsed / speedBumpTransitionTime);
            yield return null;
        }
        _currentSpeed = speedBumpSpeed;
        yield return new WaitForFixedUpdate();
        elapsed = 0f;
        while (elapsed < speedBumpTransitionTime)
        {
            elapsed += Time.deltaTime;
            _currentSpeed = Mathf.Lerp(speedBumpSpeed, baseSpeed, elapsed / speedBumpTransitionTime);
            yield return null;
        }
        _currentSpeed = baseSpeed;
    }

    // ─────────────────────────────────────────
    //  CRASH  (legacy solo-crash path)
    // ─────────────────────────────────────────

    public void SetCrashed(GameObject crashVFXPrefab, float recoveryDuration,
                           bool managedByCrashScene = false)
    {
        if (_isCrashed) return;
        _isCrashed = true;
        _isStopped = true;
        StopAllCoroutines();

        if (_currentSegment != null) _currentSegment.SetBlocked(true);

        if (!managedByCrashScene)
        {
            if (crashVFXPrefab != null)
            {
                var vfx = Instantiate(crashVFXPrefab, transform.position, Quaternion.identity);
                Destroy(vfx, recoveryDuration + 2f);
            }

            var anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.SetTrigger("Crash");

            StartCoroutine(RecoverAfter(recoveryDuration));
        }
        else
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.SetTrigger("Crash");
        }
    }

    private IEnumerator RecoverAfter(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!CarManager.IsDragging) elapsed += Time.deltaTime;
            yield return null;
        }
        if (_currentSegment != null) _currentSegment.SetBlocked(false);
        Despawn();
    }

    // ─────────────────────────────────────────
    //  DESPAWN
    // ─────────────────────────────────────────

    private void Despawn()
    {
        _active = false;
        _isCrashed = false;
        _isStopped = false;
        _isQueued = false;
        _inArrival = false;
        _isCrashRear = false;
        _isCrashFront = false;
        _crashPartner = null;
        _inLaneTransition = false;
        _processingTiles.Clear();
        ReleaseAnyJunction();
        _prevSegmentForJunction = null;

        if (_currentSegment != null)
        {
            _currentSegment.UnregisterCar(this);
            _currentSegment = null;
        }

        _segmentFrom = null;
        _segmentTo = null;
        _targetNode = null;

        CarManager.Instance?.ReturnCarToPool(this);
    }

    // ─────────────────────────────────────────
    //  BUMPER POSITION HELPERS
    // ─────────────────────────────────────────

    private static Vector3 GetFrontBumperPosition(CarAgent car)
        => car.transform.position + car.transform.forward * HalfLengthAlongForward(car);

    private static Vector3 GetRearBumperPosition(CarAgent car)
        => car.transform.position - car.transform.forward * HalfLengthAlongForward(car);

    private static float HalfLengthAlongForward(CarAgent car)
    {
        Bounds b = GetWorldBounds(car.gameObject);
        Vector3 f = car.transform.forward;
        return 0.5f * (Mathf.Abs(f.x) * b.size.x + Mathf.Abs(f.z) * b.size.z);
    }

    public float NoseToTailLength() => 2f * HalfLengthAlongForward(this);

    private static Bounds GetWorldBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // ─────────────────────────────────────────
    //  PUBLIC ACCESSORS
    // ─────────────────────────────────────────

    public RoadSegment CurrentSegment => _currentSegment;
    public RoadIntersection CurrentNode => _currentNode;
    public RoadIntersection SegmentFrom => _segmentFrom;
    public RoadIntersection SegmentTo => _segmentTo;
    public bool IsCrashCar => _isCrashRear || _isCrashFront;

    public void ForceDespawn() => Despawn();
}