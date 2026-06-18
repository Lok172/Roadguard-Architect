using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR AGENT  (v8 — predefined path + directional crash stops)
//
//  KEY CHANGES vs v7:
//    • Predefined path: the A* route is computed ONCE at spawn and
//      followed verbatim. No real-time A* during the journey.
//    • Junction: a car at an isJunction node no longer picks a random
//      direction — it continues along its predefined path like any
//      other node.
//    • Directional crash stop: a car only stops for a crash that
//      blocks ITS OWN lane (the direction it is travelling). The
//      opposite lane keeps flowing. A car stops either while on a
//      blocked segment (in front of the wreck) or at the intersection
//      when the next segment in its lane is blocked.
//    • Jam propagation: when a car stops to wait, it blocks the lane
//      of the segment it just came from, so the queue grows backward;
//      it releases that block once it moves on.
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
    public float blockedCheckInterval = 2f;

    // ── Crash Ahead ───────────────────────────
    [Header("Crash Ahead")]
    [Tooltip("Gap left between this car's nose and a crash scene blocking the " +
             "lane ahead (world units). The car waits here until the crash clears.")]
    [Min(0f)] public float blockStopGap = 0.6f;

    // ── Lane Transition ─────────────────────────
    [Header("Lane Transition")]
    [Tooltip("World-units over which to smoothly interpolate the lateral lane " +
             "position when transitioning between segments with different laneOffset " +
             "values. Creates an inclined merge path instead of an abrupt jump.")]
    [Min(0.5f)] public float laneTransitionDistance = 3f;

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

    // ── Crash-driving state ───────────────────
    private bool _isCrashRear;
    private bool _isCrashFront;
    private CarAgent _crashPartner;
    private Vector3 _snapPoint;
    private RoadSegment _crashSegment;
    private float _crashRearSpeed;

    // ── Lane transition state ─────────────────
    private bool _inLaneTransition;
    private Vector3 _transitionWaypoint;
    private Vector3 _prevLaneOffsetVec;
    private Vector3 _newLaneOffsetVec;

    // ── Jam / backward-block state (Req 3) ────
    // The segment this car most recently finished travelling (its
    // approach to the node it is currently stopped at).
    private RoadSegment _arrivalSegment;
    private RoadIntersection _arrivalFrom;
    // The lane this car has blocked while waiting (so the jam grows
    // backward). Released when the car moves on or despawns.
    private RoadSegment _jamBlockSeg;
    private RoadIntersection _jamBlockToward;

    // ─────────────────────────────────────────
    //  INITIALISE  (normal traffic)
    // ─────────────────────────────────────────

    /// <summary>
    /// Initialises the car with a pre-defined goal. The car routes
    /// via A* from <paramref name="startNode"/> to <paramref name="goal"/>
    /// and despawns on arrival.
    /// </summary>
    public void Initialise(RoadIntersection startNode,
                           List<RoadIntersection> allNodes,
                           List<RoadIntersection> endPoints,
                           RoadIntersection goal)
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
        _inLaneTransition = false;
        _jamBlockSeg = null;
        _jamBlockToward = null;
        _arrivalSegment = null;
        _arrivalFrom = null;

        _destination = goal;
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
        _jamBlockSeg = null;
        _jamBlockToward = null;
        _arrivalSegment = null;
        _arrivalFrom = null;
        _path.Clear();
        _pathIndex = 1;
        _processingTiles.Clear();
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
        ReleaseJamBlock();
        StopAllCoroutines();

        if (_crashPartner != null)
        {
            _crashPartner._isCrashFront = false;
            _crashPartner._isCrashed = true;
            _crashPartner._isStopped = true;
            _crashPartner.ReleaseJamBlock();
            _crashPartner.StopAllCoroutines();
        }

        // Segment blocking is owned entirely by CarManager.OnCrashImpact,
        // which blocks the correct lane(s) directionally (Req 2). Doing it
        // here too would double-block and leak the opposite lane.
        CarManager.Instance?.OnCrashImpact(this, _crashPartner, _crashSegment);
    }

    // ─────────────────────────────────────────
    //  MOVEMENT  (normal traffic)
    // ─────────────────────────────────────────

    private void MoveTowardTarget()
    {
        float speed = _currentSpeed;

        // ── Lane transition — drive toward waypoint first ────
        if (_inLaneTransition)
        {
            Vector3 wdir = _transitionWaypoint - transform.position;
            float wdist = wdir.magnitude;

            if (wdist <= arrivalThreshold)
            {
                transform.position = _transitionWaypoint;
                _inLaneTransition = false;
            }
            else
            {
                if (TryGetBlockStopPoint(_transitionWaypoint, out Vector3 blockStop))
                {
                    DriveTowardBlockStop(blockStop, speed);
                    return;
                }

                speed = ApplyCarFollowing(speed);

                if (wdist > 0.01f)
                    transform.rotation = Quaternion.LookRotation(wdir.normalized, Vector3.up);

                float step = speed * Time.deltaTime;
                transform.position += wdir.normalized * Mathf.Min(step, wdist);
                return;
            }
        }

        Vector3 laneTarget = LaneTargetFor(_targetNode, _segmentFrom, _segmentTo, _currentSegment);

        // ── Stop for a crash scene — preserve forward direction ──
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
    //  CRASH STOP HELPER
    //
    //  Drives toward the block stop point while PRESERVING
    //  the car's travel direction (segmentFrom → segmentTo).
    // ─────────────────────────────────────────

    private void DriveTowardBlockStop(Vector3 blockStop, float speed)
    {
        Vector3 bdir = blockStop - transform.position;
        float bdist = bdir.magnitude;

        if (_segmentFrom != null && _segmentTo != null)
        {
            Vector3 travelDir = (_segmentTo.transform.position -
                                 _segmentFrom.transform.position).normalized;
            if (travelDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);
        }

        if (bdist > arrivalThreshold)
            transform.position += bdir.normalized * Mathf.Min(speed * Time.deltaTime, bdist);
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
    /// If the current segment is blocked IN OUR LANE by a crash scene that
    /// lies AHEAD of us (between this car and its target), returns the point
    /// to stop at. A block on the opposite lane is ignored (Req 6).
    /// </summary>
    private bool TryGetBlockStopPoint(Vector3 laneTarget, out Vector3 stopPoint)
    {
        stopPoint = default;
        if (_currentSegment == null || _segmentTo == null) return false;
        if (!_currentSegment.IsBlockedToward(_segmentTo)) return false;
        if (!_currentSegment.HasBlockPositionToward(_segmentTo)) return false;

        Vector3 fwd;
        if (_segmentFrom != null && _segmentTo != null)
            fwd = (_segmentTo.transform.position - _segmentFrom.transform.position).normalized;
        else
        {
            Vector3 toTarget = laneTarget - transform.position;
            fwd = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
        }

        Vector3 toBlock = _currentSegment.BlockPositionToward(_segmentTo) - transform.position;
        float along = Vector3.Dot(toBlock, fwd);
        if (along <= 0f) return false;

        float clearance = HalfLengthAlongForward(this) + blockStopGap;
        stopPoint = transform.position + fwd * Mathf.Max(0f, along - clearance);
        Debug.Log($"{name}: blocked segment detected {_currentSegment.segmentID}");
        return true;
    }

    // ─────────────────────────────────────────
    //  NODE ARRIVAL
    // ─────────────────────────────────────────

    private void OnReachedNode(RoadIntersection node)
    {
        // Capture outgoing lane offset BEFORE clearing segment state.
        _prevLaneOffsetVec = Vector3.zero;
        if (_currentSegment != null && _segmentFrom != null && _segmentTo != null)
            _prevLaneOffsetVec = _currentSegment.GetLaneOffsetVector(_segmentFrom, _segmentTo);

        // Remember the segment we just finished travelling — used to grow
        // the jam backward (Req 3) if we end up waiting at this node.
        _arrivalSegment = _currentSegment;
        _arrivalFrom = _segmentFrom;

        if (_currentSegment != null)
        {
            _currentSegment.UnregisterCar(this);
            _currentSegment = null;
            _segmentFrom = null;
            _segmentTo = null;
        }

        _currentNode = node;
        _inArrival = false;

        // ── Reached destination → despawn ────────────────────────
        if (node == _destination)
        {
            Despawn();
            return;
        }

        // ── Follow the predefined A* path (junctions included) ────
        //  Req 1: no real-time A*. A junction node is treated like any
        //  other node — the car simply takes the next hop on its path.
        if (_pathIndex >= _path.Count)
        {
            // Path exhausted without reaching destination — despawn.
            Debug.LogWarning($"[CarAgent] {name}: path exhausted before reaching " +
                             $"{_destination?.intersectionID}. Despawning.");
            Despawn();
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
            Despawn();
            return;
        }

        RoadIntersection nextNode = _path[_pathIndex];
        RoadSegment seg = _currentNode.SegmentTo(nextNode);

        if (seg == null)
        {
            Debug.LogWarning($"[CarAgent] {name}: no segment to {nextNode.intersectionID}. Despawning.");
            Despawn();
            return;
        }

        // ── Lane blocked in OUR direction → stop and wait ────────
        //  Req 4: stop at the intersection only when the next segment is
        //  blocked in the lane we are about to travel. A block on the
        //  opposite lane does not stop us. No rerouting (Req 1).
        if (seg.IsBlockedToward(nextNode))
        {
            Debug.Log($"{name}: lane→{nextNode.intersectionID} on {seg.segmentID} blocked — stopping.");
            AcquireJamBlock();   // Req 3: extend the jam backward
            StartCoroutine(WaitForSegmentUnblock(seg, nextNode));
            return;
        }

        EnterSegment(seg, _currentNode, nextNode);
        _pathIndex++;
        Debug.Log(
    $"{name} moving from {_currentNode?.intersectionID} " +
    $"to {_targetNode?.intersectionID}");
    }

    // ─────────────────────────────────────────
    //  JAM BLOCK  (Req 3 — backward propagation)
    //
    //  When this car stops to wait for a crash-blocked lane ahead, it
    //  blocks the lane of the segment it just came from, so cars behind
    //  it queue up too. The block is released when the car moves on.
    // ─────────────────────────────────────────

    private void AcquireJamBlock()
    {
        if (_jamBlockSeg != null) return;                 // already holding one
        if (_arrivalSegment == null || _currentNode == null) return;

        _jamBlockSeg = _arrivalSegment;
        _jamBlockToward = _currentNode;                   // travelled _arrivalFrom → _currentNode
        _jamBlockSeg.SetBlockedToward(_jamBlockToward, true,
                                      _currentNode.transform.position, hasPosition: true);
    }

    private void ReleaseJamBlock()
    {
        if (_jamBlockSeg == null) return;
        _jamBlockSeg.SetBlockedToward(_jamBlockToward, false);
        _jamBlockSeg = null;
        _jamBlockToward = null;
    }

    private void EnterSegment(RoadSegment seg, RoadIntersection from, RoadIntersection next)
    {
        _currentSegment = seg;
        _segmentFrom = from;
        _segmentTo = next;
        _targetNode = next;
        _currentSpeed = Mathf.Min(baseSpeed, seg.speedLimit);
        seg.RegisterCar(this);

        // ── Detect lane offset change and set up transition ──
        _newLaneOffsetVec = seg.GetLaneOffsetVector(from, next);

        if ((_prevLaneOffsetVec - _newLaneOffsetVec).sqrMagnitude > 0.01f)
        {
            float transitionDist = Mathf.Min(laneTransitionDistance, seg.Length * 0.4f);
            float transitionT = transitionDist / Mathf.Max(0.01f, seg.Length);

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
    //  BLOCKED SEGMENT — STOP & WAIT
    //
    //  The car stops in place until the blocked segment is
    //  cleared, then resumes along the same A* path.
    //  No rerouting is performed.
    // ─────────────────────────────────────────

    private IEnumerator WaitForSegmentUnblock(RoadSegment seg, RoadIntersection towardNode)
    {
        _isQueued = true;

        while (seg.IsBlockedToward(towardNode) && _active && !_isCrashed)
        {
            yield return new WaitForSeconds(blockedCheckInterval);
            Debug.Log($"{name} waiting for lane→{towardNode.intersectionID} on {seg.segmentID}");
        }

        _isQueued = false;

        // Moving on — lift the backward jam block so the car behind can follow.
        ReleaseJamBlock();

        if (_active && !_isCrashed)
            AdvanceAlongPath();
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
        ReleaseJamBlock();
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

        ReleaseJamBlock();          // never leave a lane blocked behind us
        _arrivalSegment = null;
        _arrivalFrom = null;

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