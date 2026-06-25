using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// ─────────────────────────────────────────────────────────────────
//  CAR MANAGER  (v10 — crash eligibility pool)
//
//  CHANGES vs v9:
//    • Crash eligibility pool (Req 4 revised): generateCrash no
//      longer force-spawns a crash every evaluation cycle. Instead,
//      segments with generateCrash == true form an eligibility pool.
//      Crashes are risk-based and only spawn on pool members. The
//      flag stays on (not auto-cleared). If no segments are in the
//      pool, the system falls back to evaluating any segment that
//      has active traffic (original behaviour).
//    • Tile-to-segment linking and staged crash spawning retained.
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

    // ── Endpoints ─────────────────────────────
    [Header("Spawn / Despawn Endpoints")]
    [Tooltip("Cars spawn FROM and despawn AT these intersections.\n" +
             "Each car picks a random endpoint as its goal.\n" +
             "Leave empty to use all intersections.")]
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
             "segments AHEAD of or BEHIND the vehicle, at that segment's centre.")]
    [Min(0f)] public float crashClearanceDistance = 50f;

    [Header("Crash Blocking")]
    [Tooltip("Also block one extra segment past the DOWNSTREAM end of the wreck " +
             "footprint (in the blocked lane's travel direction).")]
    public bool blockBufferSegments = true;

    [Tooltip("When the preferred crash segment is occupied, how many extra " +
             "segments outward to search for an empty one before cancelling " +
             "the crash (Req 5).")]
    [Min(0)] public int crashSearchExtraSegments = 6;

    [Tooltip("Max seconds a staged crash may take to reach impact before it is " +
             "force-cleaned.")]
    [Min(1f)] public float crashImpactTimeout = 15f;

    // ── Tile Linking (Req 5) ──────────────────
    [Header("Tile Linking")]
    [Tooltip("Maximum lateral distance (world units) from a segment centre-line " +
             "for a tile to be considered part of that segment.")]
    [Min(0.5f)] public float tileLinkMaxDistance = 3f;

    // ── Internal Pool ─────────────────────────
    private readonly Dictionary<int, Queue<CarAgent>> _pool =
        new Dictionary<int, Queue<CarAgent>>();
    private readonly List<CarAgent> _active = new List<CarAgent>();
    private readonly Dictionary<CarAgent, int> _agentPrefabIndex = new Dictionary<CarAgent, int>();

    private readonly HashSet<RoadSegment> _crashInProgress = new HashSet<RoadSegment>();

    private readonly List<float> _recentAccidents = new List<float>();

    private readonly List<RoadTile> _roadTiles = new List<RoadTile>();

    // All segments in the scene (for generateCrash scanning + tile linking)
    private readonly List<RoadSegment> _allSegments = new List<RoadSegment>();

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

        // ── Collect all segments and link tiles (Req 5) ──────────
        _allSegments.Clear();
        _allSegments.AddRange(FindObjectsOfType<RoadSegment>());
        LinkTilesToSegments();

        BuildPool();
        SpawnInitialBatch();             // one car per spawn point (Req 7)
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
    //  TILE-TO-SEGMENT LINKING  (Req 5)
    //
    //  For each RoadSegment, scans all RoadTiles in the scene and
    //  links those whose centre projects onto the segment within
    //  tileLinkMaxDistance.  This creates the missing segment↔tile
    //  relationship that CarAgent needs for device-aware stopping.
    // ─────────────────────────────────────────

    private void LinkTilesToSegments()
    {
        int totalLinked = 0;
        foreach (var seg in _allSegments)
        {
            if (seg == null) continue;
            seg.CollectNearbyTiles(tileLinkMaxDistance);
            totalLinked += seg.LinkedTiles.Count;
        }
        Debug.Log($"[CarManager] Linked {totalLinked} tile(s) across {_allSegments.Count} segment(s).");
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

    /// <summary>
    /// Seeds the network with ONE car per distinct spawn point (Req 7),
    /// capped at maxActiveCars. The remaining population is ramped up
    /// gradually by MaintainPopulationLoop.
    /// </summary>
    private void SpawnInitialBatch()
    {
        var endpoints = (endpointIntersections != null && endpointIntersections.Count > 0)
                         ? endpointIntersections : intersections;

        int spawned = 0;
        foreach (var start in endpoints)
        {
            if (start == null) continue;
            if (ActiveTrafficCount() >= maxActiveCars) break;
            SpawnFrom(start, endpoints);
            spawned++;
        }

        Debug.Log($"[CarManager] Initial batch: one car per spawn point — {spawned} cars.");
    }

    /// <summary>Spawns a single car from a RANDOM spawn point (used for ramp-up).</summary>
    private void SpawnOne()
    {
        if (intersections.Count < 2) return;

        var endpoints = (endpointIntersections != null && endpointIntersections.Count > 0)
                         ? endpointIntersections : intersections;
        if (endpoints.Count == 0) return;

        var start = endpoints[Random.Range(0, endpoints.Count)];
        SpawnFrom(start, endpoints);
    }

    /// <summary>Spawns a single car from a SPECIFIC spawn point toward a random goal.</summary>
    private void SpawnFrom(RoadIntersection start, List<RoadIntersection> endpoints)
    {
        if (start == null || endpoints == null || endpoints.Count == 0) return;

        var agent = CheckoutAgent();
        if (agent == null) return;

        // ── Pick a goal (different from start) ────────────────────
        var goalCandidates = new List<RoadIntersection>();
        foreach (var ep in endpoints)
            if (ep != null && ep != start) goalCandidates.Add(ep);

        if (goalCandidates.Count == 0)
        {
            ReturnCarToPool(agent);
            return;
        }

        var goal = goalCandidates[Random.Range(0, goalCandidates.Count)];

        PlaceAtLaneStart(agent, start);

        agent.gameObject.SetActive(true);
        agent.Initialise(start, intersections, endpoints, goal);

        _active.Add(agent);

        Debug.Log($"SPAWN: {start.intersectionID} → goal {goal.intersectionID}");
    }

    private void PlaceAtLaneStart(CarAgent agent, RoadIntersection startNode)
    {
        foreach (var seg in startNode.ConnectedSegments)
        {
            if (seg == null) continue;
            var other = seg.Other(startNode);
            if (other == null) continue;
            if (seg.IsBlockedToward(other)) continue;
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

    /// <summary>
    /// Periodically checks whether the active count has dropped below
    /// maxActiveCars and spawns one replacement after respawnDelay.
    /// </summary>
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
    //  ACCIDENT SYSTEM
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

        // ── Build the crash-eligible pool (segments with generateCrash ticked) ──
        var crashPool = new List<RoadSegment>();
        foreach (var seg in _allSegments)
        {
            if (seg == null || !seg.generateCrash) continue;
            crashPool.Add(seg);
        }

        // ── Risk-based evaluation — only on crash-eligible segments ──
        //  If the pool is non-empty, restrict crash generation to those
        //  segments. If the pool is empty, fall back to normal evaluation
        //  on any segment that has an active car.
        if (crashPool.Count > 0)
        {
            foreach (var seg in crashPool)
            {
                if (seg == null || seg.IsBlocked) continue;
                if (evaluatedSegments.Contains(seg)) continue;
                if (_crashInProgress.Contains(seg)) continue;
                evaluatedSegments.Add(seg);

                // Use any car's speed for risk calc, or the segment's own speed limit.
                float carSpeed = seg.speedLimit;
                foreach (var car in seg.CarsOnSegment)
                {
                    if (car != null && !car.IsCrashCar)
                    {
                        carSpeed = Mathf.Min(car.baseSpeed, seg.speedLimit);
                        break;
                    }
                }

                float risk = seg.CalculateRisk(carSpeed);
                if (Random.value >= risk) continue;

                SpawnStagedCrash(seg);

                Debug.Log($"[CarManager] Crash spawned on pool segment '{seg.segmentID}' (risk={risk:F2})");
            }
        }
        else
        {
            // No crash pool — fall back to evaluating any segment with traffic.
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
            // Req 5: find the nearest EMPTY (CarCount == 0) usable segment,
            // searching outward; cancel the crash if none is available.
            if (!TryFindEmptyCrashSegment(riskSeg, reference, out crashSeg, out from, out to))
                return;

            frontPos = GetCrashSpawnPosition(
                crashSeg,
                from,
                to);
        }
        else
        {
            // No reference car: the risk segment itself must be empty + usable.
            crashSeg = riskSeg;
            if (!IsSegmentUsable(crashSeg)) return;
            bool towardsB = (Random.value > 0.5f);
            from = towardsB ? riskSeg.intersectionA : riskSeg.intersectionB;
            to = towardsB ? riskSeg.intersectionB : riskSeg.intersectionA;
            float fallbackT = towardsB ? crashFrontT : (1f - crashFrontT);
            frontPos = GetCrashSpawnPosition(
                crashSeg,
                from,
                to);
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

    /// <summary>
    /// Req 5: starting at the preferred clearance distance and walking
    /// outward (both directions), returns the first segment that is usable
    /// AND empty (CarCount == 0). Returns false if none is available within
    /// the search range — the caller then cancels the crash.
    /// </summary>
    private bool TryFindEmptyCrashSegment(RoadSegment riskSeg, CarAgent reference,
                                          out RoadSegment crashSeg,
                                          out RoadIntersection from, out RoadIntersection to)
    {
        crashSeg = null; from = null; to = null;

        float segLen = Mathf.Max(0.01f, riskSeg.Length);
        int startAway = Mathf.Max(1, Mathf.CeilToInt(crashClearanceDistance / segLen));
        int maxAway = startAway + Mathf.Max(0, crashSearchExtraSegments);

        bool preferAhead = Random.value > 0.5f;

        for (int away = startAway; away <= maxAway; away++)
        {
            for (int d = 0; d < 2; d++)
            {
                bool ahead = (d == 0) ? preferAhead : !preferAhead;
                var seg = ResolveCrashSegment(riskSeg, reference, away, ahead,
                                              out var f, out var t);
                if (seg != null && seg.CarCount == 0 && IsSegmentUsable(seg))
                {
                    crashSeg = seg; from = f; to = t;
                    return true;
                }
            }
        }
        return false;
    }

    private bool TouchesEndpoint(RoadSegment seg)
    {
        if (seg == null || endpointIntersections == null || endpointIntersections.Count == 0)
            return false;
        return endpointIntersections.Contains(seg.intersectionA)
            || endpointIntersections.Contains(seg.intersectionB);
    }

    private bool TouchesJunction(RoadSegment seg)
    {
        if (seg == null) return false;
        if (seg.intersectionA != null && seg.intersectionA.isJunction) return true;
        if (seg.intersectionB != null && seg.intersectionB.isJunction) return true;
        return false;
    }

    /// <summary>
    /// Returns true if the segment is safe for a staged crash: not blocked,
    /// not already crashing, has both endpoints, touches no endpoint/junction,
    /// and is EMPTY (CarCount == 0, Req 5).
    /// </summary>
    private bool IsSegmentUsable(RoadSegment seg)
        => seg != null && !seg.IsBlocked && !_crashInProgress.Contains(seg)
           && seg.intersectionA != null && seg.intersectionB != null
           && !TouchesEndpoint(seg)
           && !TouchesJunction(seg)
           && seg.CarCount == 0;

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
    private Vector3 GetCrashSpawnPosition(
    RoadSegment seg,
    RoadIntersection from,
    RoadIntersection to)
    {
        Vector3 start = from.transform.position;
        Vector3 end = to.transform.position;

        float t = 0.6f;

        Vector3 center =
            Vector3.Lerp(start, end, t);

        return center +
               seg.GetLaneOffsetVector(from, to);
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

        LevelAudioManager.Instance?.PlayCarAccident();

        _active.Remove(carA);
        _active.Remove(carB);

        // Req 2: block only the crashed lane, downstream along its travel
        // direction (never the segment behind, never the opposite lane).
        ComputeBlockedLanes(carA, carB, seg, out var blockedSegs, out var blockedTos);

        Vector3 wreckPos = (carA.transform.position + carB.transform.position) * 0.5f;
        for (int i = 0; i < blockedSegs.Count; i++)
            if (blockedSegs[i] != null)
                blockedSegs[i].SetBlockedToward(blockedTos[i], true, wreckPos, hasPosition: true);

        SpawnCrashScene(carA, carB, seg, blockedSegs, blockedTos);

        Debug.Log($"[CarManager] Crash impact on '{seg?.segmentID}' — scene built, " +
                  $"{blockedSegs.Count} lane segment(s) blocked downstream.");
    }

    /// <summary>
    /// Req 2: computes the segments to block for a wreck and, for each, the
    /// node that identifies the blocked lane (the travel direction). Walks
    /// ONLY downstream (the direction the crashing cars were heading); the
    /// segment behind the wreck and the opposite lane stay open.
    /// </summary>
    private void ComputeBlockedLanes(CarAgent carA, CarAgent carB, RoadSegment seg,
                                     out List<RoadSegment> segs, out List<RoadIntersection> tos)
    {
        segs = new List<RoadSegment>();
        tos = new List<RoadIntersection>();
        if (seg == null) return;

        RoadIntersection from = carA.SegmentFrom != null ? carA.SegmentFrom : seg.intersectionA;
        RoadIntersection to = carA.SegmentTo != null ? carA.SegmentTo : seg.intersectionB;

        float totalCarLength = carA.NoseToTailLength() + carB.NoseToTailLength();
        float segLen = Mathf.Max(0.01f, seg.Length);
        int occupiedCount = Mathf.Max(1, Mathf.CeilToInt(totalCarLength / segLen));

        // Crash segment, blocked in the travel direction (toward `to`).
        segs.Add(seg);
        tos.Add(to);

        // Extend the wreck footprint DOWNSTREAM only.
        RoadSegment frontSeg = seg;
        RoadIntersection frontNode = to;
        while (segs.Count < occupiedCount)
        {
            RoadSegment next = PickContinuingSegment(frontSeg, frontNode);
            if (next == null || segs.Contains(next)) break;
            RoadIntersection nextTo = next.Other(frontNode);
            if (nextTo == null) break;
            segs.Add(next);
            tos.Add(nextTo);
            frontNode = nextTo;
            frontSeg = next;
        }

        // Optional one-segment downstream buffer.
        if (blockBufferSegments)
        {
            RoadSegment bufferFront = PickContinuingSegment(frontSeg, frontNode);
            if (bufferFront != null && !segs.Contains(bufferFront))
            {
                RoadIntersection bufferTo = bufferFront.Other(frontNode);
                if (bufferTo != null)
                {
                    segs.Add(bufferFront);
                    tos.Add(bufferTo);
                }
            }
        }
    }

    // ─────────────────────────────────────────
    //  CRASH SCENE FACTORY
    // ─────────────────────────────────────────

    private void SpawnCrashScene(CarAgent carA, CarAgent carB, RoadSegment seg,
                                 List<RoadSegment> blockedSegments,
                                 List<RoadIntersection> blockedTowards)
    {
        Vector3 midpoint = (carA.transform.position + carB.transform.position) * 0.5f;

        var go = new GameObject($"CrashScene_{seg.segmentID}_{Time.frameCount}");
        go.transform.position = midpoint;

        var scene = go.AddComponent<CrashScene>();
        scene.carA = carA;
        scene.carB = carB;
        scene.segment = seg;
        scene.blockedSegments = blockedSegments;
        scene.blockedTowards = blockedTowards;
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