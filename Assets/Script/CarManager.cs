using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// ─────────────────────────────────────────────────────────────────
//  CAR MANAGER  (v6 — junction-aware crash system)
//
//  CHANGES vs v5:
//    • Issue 4c: Segments that touch a junction intersection are
//      excluded from crash spawning (IsSegmentUsable now also
//      checks TouchesJunction). No crash scenes at junctions.
// ─────────────────────────────────────────────────────────────────

public class CarManager : MonoBehaviour
{
    public static CarManager Instance { get; private set; }

    // ── Drag Detection ────────────────────────
    public static bool IsDragging =>
        (PlacementManager.Instance != null && PlacementManager.Instance.IsDragging)
        || _cameraIsDragging;

    [Header("Drag Pause")]
    [Tooltip("Mouse button for camera rotate (0=left,1=right,2=middle).")]
    public int dragButton = 1;
    [Tooltip("Pixel distance before treating mouse movement as a drag.")]
    [Min(0f)] public float dragThresholdPixels = 5f;

    private bool _buttonHeld;
    private Vector3 _dragStartScreen;
    private static bool _cameraIsDragging;

    // ── Car Prefabs ───────────────────────────
    [Header("Car Prefabs")]
    public List<GameObject> carPrefabs = new List<GameObject>();
    [Min(1)] public int poolSizePerPrefab = 10;

    // ── Spawn Settings ────────────────────────
    [Header("Spawn Settings")]
    [Tooltip("Maximum cars active in the scene at any time.")]
    [Min(1)] public int maxActiveCars = 10;
    [Tooltip("Seconds between a despawn and the next spawn.")]
    [Min(0f)] public float respawnDelay = 3f;
    [Tooltip("Auto-collect all RoadIntersection objects on Start.")]
    public bool autoCollectIntersections = true;
    [Tooltip("Full graph node list (auto-filled if autoCollectIntersections is true).")]
    public List<RoadIntersection> intersections = new List<RoadIntersection>();

    [Tooltip("Cars ignore A* routing and pick a random direction at every junction, " +
             "instead of pathing to a random destination.")]
    public bool randomTurnsAtJunctions = false;

    // ── Endpoints ─────────────────────────────
    [Header("Spawn / Despawn Endpoints")]
    [Tooltip("Cars spawn FROM and despawn AT these intersections.\n" +
             "Leave empty to use all intersections (random anywhere).")]
    public List<RoadIntersection> endpointIntersections = new List<RoadIntersection>();

    // ── Adaptive Risk Eval ────────────────────
    [Header("Adaptive Risk Eval")]
    [Tooltip("Shortest gap between risk evaluations (when accident rate is high).")]
    [FormerlySerializedAs("riskEvalIntervalMin")]
    [Min(0.5f)] public float minRiskEval = 1.5f;

    [Tooltip("Longest gap between risk evaluations (used when no recent accidents).")]
    [FormerlySerializedAs("riskEvalInterval")]
    [Min(0.5f)] public float maxRiskEval = 5f;

    [Tooltip("Seconds an accident keeps counting toward the 'recent rate'.")]
    [FormerlySerializedAs("accidentRateWindow")]
    [Min(1f)] public float recentRateInterval = 30f;

    [Tooltip("Recent-accident count at which the eval interval hits its minimum.")]
    [Min(1)] public int accidentsForMinInterval = 5;

    // ── Accident System ───────────────────────
    [Header("Accident System")]
    [Tooltip("Smoke / fire VFX spawned at the impact point of a rear-end crash.")]
    public GameObject smokeVFXPrefab;

    [Tooltip("Barrier fence prefab. Cloned in a rectangle around crashed cars.")]
    public GameObject barrierFencePrefab;

    [Tooltip("Seconds the crash scene stays fully visible before fading.")]
    [Min(1f)] public float crashDisappearDuration = 8f;

    [Tooltip("Seconds for the fade-out after disappearDuration expires.")]
    [Min(0.5f)] public float crashFadeDuration = 2f;

    [Tooltip("Seconds before the crash fully fades that the smoke is stopped " +
             "and its particles cleared, so the smoke doesn't linger after the " +
             "cars have vanished. Increase to make the smoke end earlier.")]
    [Min(0f)] public float crashSmokeFadeLead = 1f;

    [Tooltip("Clearance between the barrier fence and the cars (world units).")]
    [FormerlySerializedAs("barrierPadding")]
    [Min(0f)] public float barrierPaddingWithCar = 0.4f;

    [Tooltip("Extra spacing inserted lengthwise between the two crashed cars " +
             "so the fence does not hug them when they sit bumper-to-bumper " +
             "(world units).")]
    [Min(0f)] public float barrierPaddingInBetween = 0.4f;

    [Tooltip("Legacy single-car crash recovery duration (fallback).")]
    [Min(1f)] public float crashRecoveryDuration = 5f;

    [Header("Staged Crash Settings")]
    [Tooltip("Speed of the front car in a staged crash (slow / stopped).")]
    [Min(0f)] public float crashFrontSpeed = 2f;

    [Tooltip("Speed of the rear car chasing the front car.")]
    [Min(1f)] public float crashRearSpeed = 12f;

    [Tooltip("How far behind the front car the rear car spawns (world units).")]
    [Min(2f)] public float crashSpawnGap = 8f;

    [Tooltip("Normalised t position [0..1] along segment where front car spawns.\n" +
             "0.5 = midpoint, 0.7 = 70% toward the target end.")]
    [Range(0.1f, 0.9f)] public float crashFrontT = 0.6f;

    [Tooltip("How far away from a moving vehicle the crash scene is staged " +
             "(world units). The distance is quantised to whole road segments: " +
             "the crash spawns ceil(CrashClearanceDistance / segmentLength) " +
             "segments AHEAD of or BEHIND the vehicle, at that segment's centre. " +
             "Example: distance 50, segment length 30 → ceil(50/30)=2 segments " +
             "away. Only used when a moving vehicle is present to measure from.")]
    [Min(0f)] public float crashClearanceDistance = 50f;

    [Header("Crash Blocking")]
    [Tooltip("Also block one extra segment past each END of the wreck footprint " +
             "(the ±1 buffer). Wreck on segment 03 → additionally blocks the " +
             "segment before and the segment after it.")]
    public bool blockBufferSegments = true;

    [Tooltip("Max seconds a staged crash may take to reach impact before it is " +
             "force-cleaned (stops stuck crash cars leaking the active cap).")]
    [Min(1f)] public float crashImpactTimeout = 15f;

    // ── Internal Pool ─────────────────────────
    private readonly Dictionary<int, Queue<CarAgent>> _pool =
        new Dictionary<int, Queue<CarAgent>>();
    private readonly List<CarAgent> _active = new List<CarAgent>();
    private readonly Dictionary<CarAgent, int> _agentPrefabIndex = new Dictionary<CarAgent, int>();

    private readonly HashSet<RoadSegment> _crashInProgress = new HashSet<RoadSegment>();

    private readonly List<float> _recentAccidents = new List<float>();

    private readonly List<RoadTile> _roadTiles = new List<RoadTile>();

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (autoCollectIntersections)
        {
            intersections.Clear();
            intersections.AddRange(FindObjectsOfType<RoadIntersection>());
            Debug.Log($"[CarManager] Collected {intersections.Count} intersections.");
        }

        if (intersections.Count < 2)
        {
            Debug.LogWarning("[CarManager] Need ≥2 intersections.");
            return;
        }

        _roadTiles.Clear();
        _roadTiles.AddRange(FindObjectsOfType<RoadTile>());

        BuildPool();
        FillToMax();
        StartCoroutine(RiskEvalLoop());
        StartCoroutine(MaintainPopulationLoop());
    }

    private void Update() => TrackDrag();

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _cameraIsDragging = false;
    }

    // ─────────────────────────────────────────
    //  DRAG DETECTION
    // ─────────────────────────────────────────

    private void TrackDrag()
    {
        if (Input.GetMouseButtonDown(dragButton))
        {
            _buttonHeld = true;
            _dragStartScreen = Input.mousePosition;
        }

        if (_buttonHeld && Input.GetMouseButton(dragButton))
        {
            float moved = Vector3.Distance(Input.mousePosition, _dragStartScreen);
            if (!_cameraIsDragging && moved >= dragThresholdPixels)
                _cameraIsDragging = true;
        }

        if (Input.GetMouseButtonUp(dragButton))
        {
            _buttonHeld = false;
            _cameraIsDragging = false;
        }
    }

    // ─────────────────────────────────────────
    //  POOL
    // ─────────────────────────────────────────

    private void BuildPool()
    {
        for (int i = 0; i < carPrefabs.Count; i++)
        {
            if (carPrefabs[i] == null) continue;
            _pool[i] = new Queue<CarAgent>();
            for (int n = 0; n < poolSizePerPrefab; n++)
            {
                var a = CreateAgent(i);
                a.gameObject.SetActive(false);
                _pool[i].Enqueue(a);
            }
        }
    }

    private CarAgent CreateAgent(int idx)
    {
        var go = Instantiate(carPrefabs[idx], transform);
        go.name = $"{carPrefabs[idx].name}_pool";
        var agent = go.GetComponent<CarAgent>() ?? go.AddComponent<CarAgent>();
        _agentPrefabIndex[agent] = idx;
        return agent;
    }

    private CarAgent CheckoutAgent()
    {
        if (carPrefabs.Count == 0) return null;
        int idx = Random.Range(0, carPrefabs.Count);
        if (_pool.TryGetValue(idx, out var q) && q.Count > 0) return q.Dequeue();
        return carPrefabs[idx] != null ? CreateAgent(idx) : null;
    }

    // ─────────────────────────────────────────
    //  SPAWN
    // ─────────────────────────────────────────

    private int ActiveTrafficCount()
    {
        int n = 0;
        foreach (var a in _active) if (a != null && !a.IsCrashCar) n++;
        return n;
    }

    private void FillToMax()
    {
        int n = maxActiveCars - ActiveTrafficCount();
        for (int i = 0; i < n; i++) SpawnOne();
    }

    private void SpawnOne()
    {
        if (intersections.Count < 2) return;

        var agent = CheckoutAgent();
        if (agent == null) return;

        var endpoints = (endpointIntersections != null && endpointIntersections.Count > 0)
                         ? endpointIntersections : intersections;
        var start = endpoints[Random.Range(0, endpoints.Count)];

        PlaceAtLaneStart(agent, start);

        agent.gameObject.SetActive(true);
        agent.randomWalk = randomTurnsAtJunctions;
        agent.Initialise(start, intersections, endpoints);

        _active.Add(agent);
    }

    private void PlaceAtLaneStart(CarAgent agent, RoadIntersection startNode)
    {
        foreach (var seg in startNode.ConnectedSegments)
        {
            if (seg == null || seg.IsBlocked) continue;
            var other = seg.Other(startNode);
            if (other == null) continue;
            var offset = seg.GetLaneOffsetVector(startNode, other);
            agent.transform.position = startNode.transform.position + offset;
            return;
        }
        agent.transform.position = startNode.transform.position;
    }

    public void ReturnCarToPool(CarAgent agent)
    {
        _active.Remove(agent);
        agent.gameObject.SetActive(false);
        if (_agentPrefabIndex.TryGetValue(agent, out int idx) && _pool.ContainsKey(idx))
            _pool[idx].Enqueue(agent);
    }

    private IEnumerator MaintainPopulationLoop()
    {
        var wait = new WaitForSeconds(Mathf.Max(0.25f, respawnDelay));
        while (true)
        {
            yield return wait;
            if (IsDragging) continue;
            if (ActiveTrafficCount() < maxActiveCars) SpawnOne();
        }
    }

    // ─────────────────────────────────────────
    //  ACCIDENT SYSTEM  (v6 — junction-aware)
    // ─────────────────────────────────────────

    private IEnumerator RiskEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(CurrentRiskEvalInterval());
            if (!IsDragging) EvaluateAccidents();
        }
    }

    private float CurrentRiskEvalInterval()
    {
        float cutoff = Time.time - recentRateInterval;
        _recentAccidents.RemoveAll(ts => ts < cutoff);

        float t = Mathf.Clamp01((float)_recentAccidents.Count / Mathf.Max(1, accidentsForMinInterval));
        return Mathf.Lerp(maxRiskEval, minRiskEval, t);
    }

    public void EvaluateAccidents()
    {
        var evaluatedSegments = new HashSet<RoadSegment>();

        var snapshot = new List<CarAgent>(_active);
        foreach (var agent in snapshot)
        {
            if (agent == null || agent.IsCrashCar) continue;
            var seg = agent.CurrentSegment;
            if (seg == null || seg.IsBlocked) continue;
            if (evaluatedSegments.Contains(seg)) continue;
            if (_crashInProgress.Contains(seg)) continue;
            evaluatedSegments.Add(seg);

            float risk = seg.CalculateRisk(Mathf.Min(agent.baseSpeed, seg.speedLimit));
            if (Random.value >= risk) continue;

            SpawnStagedCrash(seg);

            Debug.Log($"[CarManager] Staged crash spawned on '{seg.segmentID}' (risk={risk:F2})");
        }
    }

    // ─────────────────────────────────────────
    //  STAGED CRASH SPAWNING
    // ─────────────────────────────────────────

    private void SpawnStagedCrash(RoadSegment riskSeg)
    {
        if (riskSeg.intersectionA == null || riskSeg.intersectionB == null) return;

        RoadSegment crashSeg;
        RoadIntersection from, to;
        Vector3 frontPos;

        CarAgent reference = FindReferenceCarOnSegment(riskSeg);
        if (reference != null && reference.SegmentFrom != null && reference.SegmentTo != null)
        {
            float segLen = Mathf.Max(0.01f, riskSeg.Length);
            int segmentsAway = Mathf.CeilToInt(crashClearanceDistance / segLen);

            bool ahead = Random.value > 0.5f;
            crashSeg = ResolveCrashSegment(riskSeg, reference, segmentsAway, ahead, out from, out to);

            if (!IsSegmentUsable(crashSeg))
                crashSeg = ResolveCrashSegment(riskSeg, reference, segmentsAway, !ahead, out from, out to);

            if (!IsSegmentUsable(crashSeg)) return;

            if (!TryGetTileSpawnPosition(crashSeg, from, to, 0.5f, out frontPos))
                return;
        }
        else
        {
            crashSeg = riskSeg;
            if (!IsSegmentUsable(crashSeg)) return;
            bool towardsB = (Random.value > 0.5f);
            from = towardsB ? riskSeg.intersectionA : riskSeg.intersectionB;
            to = towardsB ? riskSeg.intersectionB : riskSeg.intersectionA;
            float fallbackT = towardsB ? crashFrontT : (1f - crashFrontT);
            if (!TryGetTileSpawnPosition(crashSeg, from, to, fallbackT, out frontPos))
                return;
        }

        _crashInProgress.Add(crashSeg);

        Vector3 forward = (to.transform.position - from.transform.position).normalized;

        // ── Front car (carB) ──────────────────────────────────────
        CarAgent carB = CheckoutAgent();
        if (carB == null) { _crashInProgress.Remove(crashSeg); return; }

        carB.transform.position = frontPos;
        carB.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        carB.gameObject.SetActive(true);
        carB.InitialiseAsCrashFront(crashSeg, from, to, crashFrontSpeed);
        _active.Add(carB);

        // ── Rear car (carA) ───────────────────────────────────────
        CarAgent carA = CheckoutAgent();
        if (carA == null)
        {
            carB.ForceDespawn();
            _crashInProgress.Remove(crashSeg);
            return;
        }

        Vector3 rearPos = frontPos - forward * crashSpawnGap;
        carA.transform.position = rearPos;
        carA.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        carA.gameObject.SetActive(true);
        carA.InitialiseAsCrashRear(carB, crashSeg, from, to, crashRearSpeed);
        _active.Add(carA);

        StartCoroutine(CrashWatchdog(crashSeg, carA, carB));

        Debug.Log($"[CarManager] Crash pair spawned: rear={carA.name}, front={carB.name} on {crashSeg.segmentID}");
    }

    private IEnumerator CrashWatchdog(RoadSegment crashSeg, CarAgent carA, CarAgent carB)
    {
        float elapsed = 0f;
        while (elapsed < crashImpactTimeout)
        {
            if (!IsDragging) elapsed += Time.deltaTime;
            if (!_crashInProgress.Contains(crashSeg)) yield break;
            yield return null;
        }

        if (_crashInProgress.Contains(crashSeg))
        {
            _crashInProgress.Remove(crashSeg);
            if (crashSeg != null) crashSeg.SetBlocked(false);
            if (carA != null) { _active.Remove(carA); carA.ForceDespawn(); }
            if (carB != null) { _active.Remove(carB); carB.ForceDespawn(); }
            Debug.LogWarning($"[CarManager] Crash on '{crashSeg?.segmentID}' timed out — cleaned up.");
        }
    }

    private RoadSegment ResolveCrashSegment(RoadSegment startSeg, CarAgent reference,
                                            int segmentsAway, bool ahead,
                                            out RoadIntersection from, out RoadIntersection to)
    {
        RoadIntersection dirFrom = ahead ? reference.SegmentFrom : reference.SegmentTo;
        RoadIntersection dirTo = ahead ? reference.SegmentTo : reference.SegmentFrom;
        return WalkSegments(startSeg, dirFrom, dirTo, segmentsAway, out from, out to);
    }

    private bool TouchesEndpoint(RoadSegment seg)
    {
        if (seg == null || endpointIntersections == null || endpointIntersections.Count == 0)
            return false;
        return endpointIntersections.Contains(seg.intersectionA)
            || endpointIntersections.Contains(seg.intersectionB);
    }

    // ── Issue 4c: NEW — check whether a segment touches a junction ──
    private bool TouchesJunction(RoadSegment seg)
    {
        if (seg == null) return false;
        if (seg.intersectionA != null && seg.intersectionA.isJunction) return true;
        if (seg.intersectionB != null && seg.intersectionB.isJunction) return true;
        return false;
    }

    /// <summary>
    /// Returns true if the segment is safe for a staged crash.
    /// Issue 4c: now also excludes segments that touch a junction intersection.
    /// </summary>
    private bool IsSegmentUsable(RoadSegment seg)
        => seg != null && !seg.IsBlocked && !_crashInProgress.Contains(seg)
           && seg.intersectionA != null && seg.intersectionB != null
           && !TouchesEndpoint(seg)
           && !TouchesJunction(seg);      // ← Issue 4c: no crash at junctions

    private RoadSegment WalkSegments(RoadSegment startSeg,
                                     RoadIntersection from, RoadIntersection to,
                                     int count,
                                     out RoadIntersection outFrom, out RoadIntersection outTo)
    {
        RoadSegment seg = startSeg;
        RoadIntersection curFrom = from;
        RoadIntersection curTo = to;

        for (int i = 0; i < count; i++)
        {
            RoadSegment next = PickContinuingSegment(seg, curTo);
            if (next == null) break;
            RoadIntersection nextTo = next.Other(curTo);
            if (nextTo == null) break;
            curFrom = curTo;
            curTo = nextTo;
            seg = next;
        }

        outFrom = curFrom;
        outTo = curTo;
        return seg;
    }

    private RoadSegment PickContinuingSegment(RoadSegment fromSeg, RoadIntersection node)
    {
        if (node == null || fromSeg == null) return null;

        RoadIntersection entry = fromSeg.Other(node);
        if (entry == null) return null;
        Vector3 inDir = (node.transform.position - entry.transform.position).normalized;

        RoadSegment best = null;
        float bestDot = -2f;
        foreach (var s in node.ConnectedSegments)
        {
            if (s == null || s == fromSeg) continue;
            RoadIntersection other = s.Other(node);
            if (other == null) continue;
            Vector3 outDir = (other.transform.position - node.transform.position).normalized;
            float dot = Vector3.Dot(inDir, outDir);
            if (dot > bestDot) { bestDot = dot; best = s; }
        }
        return best;
    }

    private bool TryGetTileSpawnPosition(RoadSegment seg, RoadIntersection from,
                                         RoadIntersection to, float desiredT, out Vector3 pos)
    {
        pos = default;
        const int samples = 21;
        float bestT = -1f, bestDist = float.MaxValue;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / (samples - 1);
            Vector3 p = seg.GetPositionAt(t) + seg.GetLaneOffsetVector(from, to);
            if (!PointIsOnAnyTile(p)) continue;
            float d = Mathf.Abs(t - desiredT);
            if (d < bestDist) { bestDist = d; bestT = t; }
        }

        if (bestT < 0f) return false;
        pos = seg.GetPositionAt(bestT) + seg.GetLaneOffsetVector(from, to);
        return true;
    }

    private bool PointIsOnAnyTile(Vector3 worldPoint)
    {
        foreach (var tile in _roadTiles)
        {
            if (tile == null) continue;
            var col = tile.GetComponent<BoxCollider>();
            if (col == null) continue;
            Vector3 q = worldPoint; q.y = col.bounds.center.y;
            if ((col.ClosestPoint(q) - q).sqrMagnitude <= 0.0001f) return true;
        }
        return false;
    }

    private CarAgent FindReferenceCarOnSegment(RoadSegment seg)
    {
        CarAgent best = null;
        float bestT = float.NegativeInfinity;
        foreach (var c in seg.CarsOnSegment)
        {
            if (c == null || c.IsCrashCar) continue;
            if (c.SegmentFrom == null || c.SegmentTo == null) continue;

            float t = seg.GetTAtPosition(c.transform.position);
            if (c.SegmentTo != seg.intersectionB) t = 1f - t;
            if (t > bestT) { bestT = t; best = c; }
        }
        return best;
    }

    // ─────────────────────────────────────────
    //  CRASH IMPACT CALLBACK
    // ─────────────────────────────────────────

    public void OnCrashImpact(CarAgent carA, CarAgent carB, RoadSegment seg)
    {
        if (seg != null)
        {
            seg.RecordAccident();
            _crashInProgress.Remove(seg);
        }

        _recentAccidents.Add(Time.time);

        _active.Remove(carA);
        _active.Remove(carB);

        List<RoadSegment> blocked = ComputeBlockedSegments(carA, carB, seg);
        Vector3 wreckPos = (carA.transform.position + carB.transform.position) * 0.5f;
        foreach (var s in blocked)
            if (s != null) s.SetBlocked(true, wreckPos);

        SpawnCrashScene(carA, carB, seg, blocked);

        Debug.Log($"[CarManager] Crash impact on '{seg?.segmentID}' — scene built, " +
                  $"{blocked.Count} segment(s) blocked.");
    }

    private List<RoadSegment> ComputeBlockedSegments(CarAgent carA, CarAgent carB, RoadSegment seg)
    {
        var blocked = new List<RoadSegment>();
        if (seg == null) return blocked;

        RoadIntersection from = carA.SegmentFrom != null ? carA.SegmentFrom : seg.intersectionA;
        RoadIntersection to = carA.SegmentTo != null ? carA.SegmentTo : seg.intersectionB;

        float totalCarLength = carA.NoseToTailLength() + carB.NoseToTailLength();
        float segLen = Mathf.Max(0.01f, seg.Length);
        int occupiedCount = Mathf.Max(1, Mathf.CeilToInt(totalCarLength / segLen));

        var occupied = new List<RoadSegment> { seg };
        RoadSegment frontSeg = seg, backSeg = seg;
        RoadIntersection frontNode = to, backNode = from;

        while (occupied.Count < occupiedCount)
        {
            RoadSegment next = PickContinuingSegment(frontSeg, frontNode);
            if (next == null || occupied.Contains(next)) break;
            occupied.Add(next);
            frontNode = next.Other(frontNode);
            frontSeg = next;
        }
        while (occupied.Count < occupiedCount)
        {
            RoadSegment prev = PickContinuingSegment(backSeg, backNode);
            if (prev == null || occupied.Contains(prev)) break;
            occupied.Add(prev);
            backNode = prev.Other(backNode);
            backSeg = prev;
        }

        blocked.AddRange(occupied);

        if (blockBufferSegments)
        {
            RoadSegment bufferFront = PickContinuingSegment(frontSeg, frontNode);
            RoadSegment bufferBack = PickContinuingSegment(backSeg, backNode);
            if (bufferFront != null && !blocked.Contains(bufferFront)) blocked.Add(bufferFront);
            if (bufferBack != null && !blocked.Contains(bufferBack)) blocked.Add(bufferBack);
        }

        return blocked;
    }

    // ─────────────────────────────────────────
    //  CRASH SCENE FACTORY
    // ─────────────────────────────────────────

    private void SpawnCrashScene(CarAgent carA, CarAgent carB, RoadSegment seg,
                                 List<RoadSegment> blockedSegments)
    {
        Vector3 midpoint = (carA.transform.position + carB.transform.position) * 0.5f;

        var go = new GameObject($"CrashScene_{seg.segmentID}_{Time.frameCount}");
        go.transform.position = midpoint;

        var scene = go.AddComponent<CrashScene>();
        scene.carA = carA;
        scene.carB = carB;
        scene.segment = seg;
        scene.blockedSegments = blockedSegments;
        scene.smokeVFXPrefab = smokeVFXPrefab;
        scene.barrierFencePrefab = barrierFencePrefab;
        scene.disappearDuration = crashDisappearDuration;
        scene.fadeDuration = crashFadeDuration;
        scene.smokeFadeLead = crashSmokeFadeLead;
        scene.barrierPaddingWithCar = barrierPaddingWithCar;
        scene.barrierPaddingInBetween = barrierPaddingInBetween;

        scene.Build();
    }

    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        foreach (var a in _active)
            if (a != null) Gizmos.DrawSphere(a.transform.position + Vector3.up * 0.5f, 0.25f);

        Gizmos.color = new Color(0f, 1f, 0.5f, 0.9f);
        if (endpointIntersections != null)
            foreach (var e in endpointIntersections)
            {
                if (e == null) continue;
                Gizmos.DrawWireSphere(e.transform.position, 0.9f);
                UnityEditor.Handles.Label(e.transform.position + Vector3.up * 1.8f,
                    "ENDPOINT");
            }

        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        foreach (var seg in _crashInProgress)
        {
            if (seg == null) continue;
            Vector3 mid = (seg.intersectionA.transform.position + seg.intersectionB.transform.position) * 0.5f;
            Gizmos.DrawWireSphere(mid, 1.2f);
            UnityEditor.Handles.Label(mid + Vector3.up * 2f, "CRASH IN PROGRESS");
        }
    }
#endif
}