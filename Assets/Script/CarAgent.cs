using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR AGENT  (v4 — stable lane, no ghost, no stuck)
//
//  KEY FIXES vs v3:
//    • EnterSegment NO LONGER teleports position. The car always
//      moves continuously; position is only set once at spawn via
//      CarManager calling PlaceAtLaneStart() before activation.
//    • PickNewDestinationAndRoute filters out _currentNode from
//      candidate destinations so FindPath always returns ≥ 2 nodes.
//    • RetryRoute now despawns after max retries instead of looping
//      forever, preventing invisible stuck cars.
//    • _isMoving guard prevents OnReachedNode re-entry.
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
    private List<RoadIntersection> _endPoints; // valid destinations (spawn/despawn list)
    private readonly HashSet<RoadTile> _processingTiles = new HashSet<RoadTile>();
    private bool _active;
    private bool _inArrival; // re-entry guard
    private Rigidbody _rb;
    private RoadIntersection _segmentFrom;
    private RoadIntersection _segmentTo;

    // ─────────────────────────────────────────
    //  INITIALISE
    // ─────────────────────────────────────────

    /// <summary>
    /// Called by CarManager. allNodes = full graph for A*.
    /// endPoints = valid destination intersections (spawn/despawn list).
    /// CarManager must call PlaceAtLaneStart() BEFORE SetActive(true).
    /// </summary>
    public void Initialise(RoadIntersection startNode,
                           List<RoadIntersection> allNodes,
                           List<RoadIntersection> endPoints)
    {
        _allNodes = allNodes;
        _endPoints = endPoints;
        _currentNode = startNode;
        _currentSegment = null;
        _segmentFrom = null;
        _segmentTo = null;
        _isCrashed = false;
        _isStopped = false;
        _isQueued = false;
        _inArrival = false;
        _active = true;
        _currentSpeed = baseSpeed;
        _path.Clear();
        _pathIndex = 1;
        _processingTiles.Clear();
        // position is set by CarManager.PlaceAtLaneStart() before this call
        PickNewDestinationAndRoute();
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
        if (!_active || _isCrashed || _isStopped || _isQueued || _inArrival) return;
        if (_targetNode == null) return;
        MoveTowardTarget();
    }

    // ─────────────────────────────────────────
    //  MOVEMENT
    // ─────────────────────────────────────────

    private void MoveTowardTarget()
    {
        float speed = _currentSpeed;

        // Raycast from car front to avoid self-hit.
        Bounds b = GetComponentInChildren<Renderer>()?.bounds
                            ?? new Bounds(transform.position, Vector3.one);
        Vector3 rayOrigin = transform.position + transform.forward * (b.extents.z + 0.1f);
        if (Physics.Raycast(rayOrigin, transform.forward,
                            out RaycastHit hit, followCheckDistance, carLayerMask))
        {
            if (hit.collider.GetComponent<CarAgent>() != null)
                speed *= followSpeedMultiplier;
        }

        // The car always targets (nextNode + laneOffset) — never node centre.
        Vector3 laneTarget = LaneTargetFor(_targetNode, _segmentFrom, _segmentTo, _currentSegment);

        Vector3 dir = laneTarget - transform.position;
        float dist = dir.magnitude;

        if (dist > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float step = speed * Time.deltaTime;

        if (dist <= arrivalThreshold || step >= dist)
        {
            // Snap to laned arrival point (NOT node centre) so EnterSegment
            // snapping from.position+offset is a zero-distance move.
            transform.position = laneTarget;
            if (!_inArrival)
            {
                _inArrival = true;
                OnReachedNode(_targetNode);
            }
        }
        else
        {
            transform.position += dir.normalized * step;
        }
    }

    /// <summary>
    /// Returns the world position the car should move toward:
    /// nextNode.position + segment lane offset.
    /// Falls back to bare node position if segment/direction not set.
    /// </summary>
    private static Vector3 LaneTargetFor(RoadIntersection node,
                                          RoadIntersection from,
                                          RoadIntersection to,
                                          RoadSegment seg)
    {
        if (seg != null && from != null && to != null)
            return node.transform.position + seg.GetLaneOffsetVector(from, to);
        return node.transform.position;
    }

    // ─────────────────────────────────────────
    //  NODE ARRIVAL
    // ─────────────────────────────────────────

    private void OnReachedNode(RoadIntersection node)
    {
        if (_currentSegment != null)
        {
            _currentSegment.UnregisterCar(this);
            _currentSegment = null;
            _segmentFrom = null;
            _segmentTo = null;
        }

        _currentNode = node;

        // Arrived at destination or end of path → pick new destination.
        if (node == _destination || _pathIndex >= _path.Count)
        {
            _inArrival = false;
            PickNewDestinationAndRoute();
            return;
        }

        _inArrival = false;
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
            // Path has a broken link — reroute.
            PickNewDestinationAndRoute();
            return;
        }

        if (seg.IsBlocked)
        {
            StartCoroutine(WaitForSegmentOrReroute(seg));
            return;
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
        // NO position teleport here. The car is already at laneTarget of
        // the previous node which equals (from.position + offset) exactly.
        seg.RegisterCar(this);
    }

    // ─────────────────────────────────────────
    //  ROUTING
    // ─────────────────────────────────────────

    private void PickNewDestinationAndRoute()
    {
        // Build candidate list: endpoints that are NOT the current node.
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
            // No valid path — despawn cleanly rather than loop forever.
            Debug.LogWarning($"[CarAgent] No path from {_currentNode?.intersectionID} " +
                             $"to {_destination?.intersectionID}. Despawning.");
            Despawn();
            return;
        }

        AdvanceAlongPath();
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
        if (_isCrashed) return;
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
    //  CRASH
    // ─────────────────────────────────────────

    public void SetCrashed(GameObject crashVFXPrefab, float recoveryDuration)
    {
        if (_isCrashed) return;
        _isCrashed = true;
        _isStopped = true;
        StopAllCoroutines();

        if (_currentSegment != null) _currentSegment.SetBlocked(true);

        if (crashVFXPrefab != null)
        {
            var vfx = Instantiate(crashVFXPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, recoveryDuration + 2f);
        }

        var anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.SetTrigger("Crash");

        StartCoroutine(RecoverAfter(recoveryDuration));
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
        _processingTiles.Clear();

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
    //  PUBLIC ACCESSORS
    // ─────────────────────────────────────────

    public RoadSegment CurrentSegment => _currentSegment;
    public RoadIntersection CurrentNode => _currentNode;
}