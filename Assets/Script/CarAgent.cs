using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR AGENT  (v3 — lane offsets + drag-pause)
//
//  Attach to every car prefab.
//
//  Life cycle:
//    Initialise(startNode, allNodes)
//    → A* to random destination (or fixed endpoint from CarManager)
//    → move along segment frame-by-frame, offset into own lane
//    → reach next intersection → A* again → repeat
//    → if segment blocked: join queue, wait, reroute when cleared
//    → SetCrashed() → VFX, freeze, recovery → ReturnToPool
//
//  LANE OFFSET
//    Each segment carries a laneOffset value.  CarAgent asks the
//    segment for the correct lateral offset based on travel direction
//    (from → to) and moves along the offset line rather than the
//    segment centre.  Approaching an intersection the car blends
//    back toward the node position so the path stays continuous.
//
//  DRAG PAUSE
//    CarManager.IsDragging flag pauses all movement and suspends
//    the crash VFX coroutine timing so nothing happens while the
//    user is panning/rotating the camera.
// ─────────────────────────────────────────────────────────────────

public class CarAgent : MonoBehaviour
{
    // ── Movement ──────────────────────────────
    [Header("Movement")]
    [Tooltip("Default cruising speed (units/sec). Clamped to segment speedLimit.")]
    public float baseSpeed = 8f;

    [Tooltip("How close to the next intersection centre before snapping to it.")]
    public float arrivalThreshold = 0.3f;

    // ── Lane Offset Blend ─────────────────────
    [Header("Lane Offset")]
    [Tooltip("World units from intersection at which the car begins blending " +
             "back to the node centre (for smooth turns).")]
    [Min(0f)] public float laneBlendDistance = 1.5f;

    // ── Car Following ─────────────────────────
    [Header("Car Following")]
    [Tooltip("Distance to look ahead for cars on the same segment.")]
    public float followCheckDistance = 4f;

    [Tooltip("Speed multiplier applied when following a car ahead (0=stop, 1=full speed).")]
    [Range(0f, 1f)] public float followSpeedMultiplier = 0.3f;

    [Tooltip("Layer mask for detecting other cars ahead.")]
    public LayerMask carLayerMask;

    // ── Traffic Light ─────────────────────────
    [Header("Traffic Light")]
    [Tooltip("Base stop time (n). Both-corner edge → n sec; one-corner edge → 2n sec.")]
    public float trafficLightWaitN = 3f;

    // ── Speed Bump ────────────────────────────
    [Header("Speed Bump")]
    public float speedBumpSpeed = 2f;
    public float speedBumpTransitionTime = 0.5f;

    // ── Stop Sign ─────────────────────────────
    [Header("Stop Sign")]
    [Range(0f, 1f)] public float stopSignStopChance = 0.5f;
    public float stopSignDuration = 2f;

    // ── Queue / Blocked ───────────────────────
    [Header("Blocked Segment")]
    [Tooltip("How often (seconds) a queued car re-checks if it can reroute.")]
    public float rerouteCheckInterval = 2f;

    // ── Runtime (read-only) ───────────────────
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
    private readonly HashSet<RoadTile> _processingTiles = new HashSet<RoadTile>();
    private bool _active;
    private Rigidbody _rb;

    // Track travel direction for lane offset.
    private RoadIntersection _segmentFrom;  // intersection we departed from
    private RoadIntersection _segmentTo;    // intersection we are heading to

    // ─────────────────────────────────────────
    //  INITIALISE  (called by CarManager after pool checkout)
    // ─────────────────────────────────────────

    public void Initialise(RoadIntersection startNode, List<RoadIntersection> allNodes)
    {
        _allNodes = allNodes;
        _currentNode = startNode;
        _currentSegment = null;
        _segmentFrom = null;
        _segmentTo = null;
        _isCrashed = false;
        _isStopped = false;
        _isQueued = false;
        _active = true;
        _currentSpeed = baseSpeed;
        _processingTiles.Clear();

        transform.position = startNode.transform.position;

        // Defer by one frame so the car's position is committed to the renderer
        // before movement begins — prevents a one-frame ghost at the intersection.
        StartCoroutine(InitialiseNextFrame());
    }

    private IEnumerator InitialiseNextFrame()
    {
        yield return null;   // wait one frame
        if (_active && !_isCrashed)
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
        // Pause everything while the user is click-dragging.
        if (CarManager.IsDragging) return;

        if (!_active || _isCrashed || _isStopped || _isQueued) return;
        if (_targetNode == null) return;
        MoveTowardTarget();
    }

    // ─────────────────────────────────────────
    //  MOVEMENT
    // ─────────────────────────────────────────

    private void MoveTowardTarget()
    {
        float speed = _currentSpeed;

        // Car following — raycast from the FRONT of the car's bounds
        // so we don't hit our own collider and get accurate spacing.
        Bounds bounds = GetComponentInChildren<Renderer>()?.bounds
                             ?? new Bounds(transform.position, Vector3.one);
        Vector3 rayOrigin = transform.position + transform.forward * (bounds.extents.z + 0.1f);

        if (Physics.Raycast(rayOrigin, transform.forward,
                            out RaycastHit hit, followCheckDistance, carLayerMask))
        {
            if (hit.collider.GetComponent<CarAgent>() != null)
                speed *= followSpeedMultiplier;
        }

        // ── Compute laned target position ─────
        // The car travels along its offset lane the entire segment.
        // Within laneBlendDistance of the next intersection, blend the
        // offset to zero so the car converges to the node centre for clean turns.
        Vector3 nodePos = _targetNode.transform.position;
        Vector3 lanedTarget;

        if (_currentSegment != null && _segmentFrom != null && _segmentTo != null)
        {
            Vector3 offset = _currentSegment.GetLaneOffsetVector(_segmentFrom, _segmentTo);
            float distToNode = Vector3.Distance(transform.position, nodePos);

            // blend = 1.0 when far away (full offset), 0.0 at the node (centre).
            float blend = Mathf.Clamp01(distToNode / Mathf.Max(0.001f, laneBlendDistance));
            lanedTarget = nodePos + offset * blend;
        }
        else
        {
            lanedTarget = nodePos;
        }

        Vector3 dir = lanedTarget - transform.position;
        float dist = dir.magnitude;

        if (dist > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        float step = speed * Time.deltaTime;

        if (step >= dist || dist <= arrivalThreshold)
        {
            transform.position = nodePos;   // snap to node centre on arrival
            OnReachedNode(_targetNode);
        }
        else
        {
            transform.position += dir.normalized * step;
        }
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

        if (node == _destination || _pathIndex >= _path.Count)
        {
            PickNewDestinationAndRoute();
            return;
        }

        AdvanceAlongPath();
    }

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
            StartCoroutine(WaitForSegmentOrReroute(seg, nextNode));
            return;
        }

        EnterSegment(seg, _currentNode, nextNode);
        _pathIndex++;
    }

    private void EnterSegment(RoadSegment seg, RoadIntersection from, RoadIntersection next)
    {
        bool isFirstSegment = (_currentSegment == null);

        _currentSegment = seg;
        _segmentFrom = from;
        _segmentTo = next;
        _targetNode = next;
        _currentSpeed = Mathf.Min(baseSpeed, seg.speedLimit);

        // On the very first segment after spawn the car starts at the node centre.
        // Snap it into its lane immediately so it never moves at a diagonal.
        // On subsequent segments (arriving from a node) the car is already near
        // centre — let MoveTowardTarget steer it naturally into the offset lane.
        if (isFirstSegment)
        {
            Vector3 offset = seg.GetLaneOffsetVector(from, next);
            transform.position = from.transform.position + offset;
        }

        seg.RegisterCar(this);
    }

    // ─────────────────────────────────────────
    //  ROUTING
    // ─────────────────────────────────────────

    private void PickNewDestinationAndRoute()
    {
        if (_allNodes == null || _allNodes.Count < 2) { Despawn(); return; }

        RoadIntersection dest = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var candidate = _allNodes[Random.Range(0, _allNodes.Count)];
            if (candidate != _currentNode) { dest = candidate; break; }
        }

        if (dest == null) { Despawn(); return; }

        _destination = dest;
        _path = RoadGraph.FindPath(_currentNode, _destination);
        _pathIndex = 1;

        if (_path.Count < 2)
        {
            StartCoroutine(RetryRouteNextFrame());
            return;
        }

        AdvanceAlongPath();
    }

    private IEnumerator RetryRouteNextFrame()
    {
        yield return new WaitForSeconds(1f);
        if (_active && !_isCrashed)
            PickNewDestinationAndRoute();
    }

    // ─────────────────────────────────────────
    //  BLOCKED SEGMENT — QUEUE & REROUTE
    // ─────────────────────────────────────────

    private IEnumerator WaitForSegmentOrReroute(RoadSegment seg, RoadIntersection next)
    {
        _isQueued = true;

        while (seg.IsBlocked && _active && !_isCrashed)
        {
            yield return new WaitForSeconds(rerouteCheckInterval);

            if (!seg.IsBlocked) break;

            // Try alternate route
            var altPath = RoadGraph.FindPath(_currentNode, _destination);
            if (altPath.Count >= 2)
            {
                _path = altPath;
                _pathIndex = 1;
                _isQueued = false;
                AdvanceAlongPath();
                yield break;
            }
        }

        _isQueued = false;
        if (_active && !_isCrashed)
            AdvanceAlongPath();
    }

    // ─────────────────────────────────────────
    //  TILE TRIGGER  (device reactions)
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
        // Traffic Light
        float lightWait = GetTrafficLightWait(tile);
        if (lightWait > 0f)
        {
            _isStopped = true;
            yield return new WaitForSeconds(lightWait);
            _isStopped = false;
        }

        // Speed Bump
        if (tile.HasDeviceAtCorner(TileCorner.Center, TrafficDeviceType.SpeedBump))
            yield return StartCoroutine(SpeedBumpRoutine());

        // Stop Sign
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

    // ─────────────────────────────────────────
    //  TRAFFIC LIGHT EDGE LOGIC
    // ─────────────────────────────────────────

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

    // ─────────────────────────────────────────
    //  SPEED BUMP
    // ─────────────────────────────────────────

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

        if (_currentSegment != null)
            _currentSegment.SetBlocked(true);

        if (crashVFXPrefab != null)
        {
            GameObject vfx = Instantiate(crashVFXPrefab, transform.position, Quaternion.identity);
            Destroy(vfx, recoveryDuration + 2f);
        }

        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.SetTrigger("Crash");

        StartCoroutine(RecoverAfter(recoveryDuration));
    }

    private IEnumerator RecoverAfter(float duration)
    {
        // Use real elapsed time so drag-pause doesn't mess up recovery timing.
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Only advance timer when not dragging and not paused.
            if (!CarManager.IsDragging)
                elapsed += Time.deltaTime;
            yield return null;
        }

        if (_currentSegment != null)
            _currentSegment.SetBlocked(false);
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
        _processingTiles.Clear();

        if (_currentSegment != null)
        {
            _currentSegment.UnregisterCar(this);
            _currentSegment = null;
        }

        _segmentFrom = null;
        _segmentTo = null;

        CarManager.Instance?.ReturnCarToPool(this);
    }

    // ─────────────────────────────────────────
    //  PUBLIC ACCESSORS
    // ─────────────────────────────────────────

    public RoadSegment CurrentSegment => _currentSegment;
    public RoadIntersection CurrentNode => _currentNode;
}