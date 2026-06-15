using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR MANAGER  (v5 — staged crash spawning)
//
//  CHANGES vs v4:
//    • Accident system now SPAWNS two dedicated crash cars on the
//      segment rather than crashing existing traffic.
//    • Front car (carB) placed ahead on the lane, drives slowly.
//    • Rear car (carA) placed behind, drives faster toward carB.
//    • When carA's front bumper reaches carB's rear bumper (snap
//      point), both stop and the CrashScene is built.
//    • OnCrashImpact() callback from CarAgent triggers scene build.
//    • Existing traffic is never directly crashed by risk eval.
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
             "Leave empty to use all intersections (random anywhere).")]
    public List<RoadIntersection> endpointIntersections = new List<RoadIntersection>();

    // ── Accident System ───────────────────────
    [Header("Accident System")]
    [Min(0.5f)] public float riskEvalInterval = 5f;

    [Tooltip("Smoke / fire VFX spawned at the impact point of a rear-end crash.")]
    public GameObject smokeVFXPrefab;

    [Tooltip("Barrier fence prefab. Cloned in a rectangle around crashed cars.")]
    public GameObject barrierFencePrefab;

    [Tooltip("Legacy single-car crash VFX (used only for non-rear-end crashes).")]
    public GameObject crashVFXPrefab;

    [Tooltip("Seconds the crash scene stays fully visible before fading.")]
    [Min(1f)] public float crashDisappearDuration = 8f;

    [Tooltip("Seconds for the fade-out after disappearDuration expires.")]
    [Min(0.5f)] public float crashFadeDuration = 2f;

    [Tooltip("Extra clearance around the car rectangle for barrier placement.")]
    [Min(0f)] public float barrierPadding = 0.4f;

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

    // ── Internal Pool ─────────────────────────
    private readonly Dictionary<int, Queue<CarAgent>> _pool =
        new Dictionary<int, Queue<CarAgent>>();
    private readonly List<CarAgent> _active = new List<CarAgent>();
    private readonly Dictionary<CarAgent, int> _agentPrefabIndex = new Dictionary<CarAgent, int>();

    // Track segments that already have a crash in progress.
    private readonly HashSet<RoadSegment> _crashInProgress = new HashSet<RoadSegment>();

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

        BuildPool();
        FillToMax();
        StartCoroutine(RiskEvalLoop());
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

    private void FillToMax()
    {
        int n = maxActiveCars - _active.Count;
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
        StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (_active.Count < maxActiveCars) SpawnOne();
    }

    // ─────────────────────────────────────────
    //  ACCIDENT SYSTEM  (v5 — staged crash)
    // ─────────────────────────────────────────

    private IEnumerator RiskEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(riskEvalInterval);
            if (!IsDragging) EvaluateAccidents();
        }
    }

    public void EvaluateAccidents()
    {
        // Collect all non-blocked segments and evaluate risk.
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

            // ── Spawn a staged rear-end crash on this segment ─────
            SpawnStagedCrash(seg);

            Debug.Log($"[CarManager] Staged crash spawned on '{seg.segmentID}' (risk={risk:F2})");
        }
    }

    // ─────────────────────────────────────────
    //  STAGED CRASH SPAWNING
    // ─────────────────────────────────────────

    /// <summary>
    /// Spawns two dedicated crash cars on the given segment.
    /// Front car (carB) placed ahead at crashFrontT, driving slowly.
    /// Rear car (carA) placed behind, driving fast toward carB.
    /// </summary>
    private void SpawnStagedCrash(RoadSegment seg)
    {
        if (seg.intersectionA == null || seg.intersectionB == null) return;

        _crashInProgress.Add(seg);

        // Pick a travel direction (A→B or B→A, randomly).
        bool towardsB = (Random.value > 0.5f);
        RoadIntersection from = towardsB ? seg.intersectionA : seg.intersectionB;
        RoadIntersection to = towardsB ? seg.intersectionB : seg.intersectionA;

        // Lane offset for this direction.
        Vector3 laneOffset = seg.GetLaneOffsetVector(from, to);
        Vector3 forward = (to.transform.position - from.transform.position).normalized;

        // ── Front car (carB) ──────────────────────────────────────
        CarAgent carB = CheckoutAgent();
        if (carB == null) { _crashInProgress.Remove(seg); return; }

        // Place front car at crashFrontT along the segment.
        Vector3 frontPos = seg.GetPositionAt(towardsB ? crashFrontT : (1f - crashFrontT)) + laneOffset;
        carB.transform.position = frontPos;
        carB.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        carB.gameObject.SetActive(true);
        carB.InitialiseAsCrashFront(seg, from, to, crashFrontSpeed);
        _active.Add(carB);

        // ── Rear car (carA) ───────────────────────────────────────
        CarAgent carA = CheckoutAgent();
        if (carA == null)
        {
            // Couldn't get a second car — abort.
            carB.ForceDespawn();
            _crashInProgress.Remove(seg);
            return;
        }

        // Place rear car behind front car by crashSpawnGap.
        Vector3 rearPos = frontPos - forward * crashSpawnGap;
        carA.transform.position = rearPos;
        carA.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        carA.gameObject.SetActive(true);
        carA.InitialiseAsCrashRear(carB, seg, from, to, crashRearSpeed);
        _active.Add(carA);

        Debug.Log($"[CarManager] Crash pair spawned: rear={carA.name}, front={carB.name} on {seg.segmentID}");
    }

    // ─────────────────────────────────────────
    //  CRASH IMPACT CALLBACK
    // ─────────────────────────────────────────

    /// <summary>
    /// Called by CarAgent.OnCrashImpact() when the rear car reaches the
    /// front car's snap point. Builds the CrashScene with barriers and smoke.
    /// </summary>
    public void OnCrashImpact(CarAgent carA, CarAgent carB, RoadSegment seg)
    {
        if (seg != null)
        {
            seg.RecordAccident();
            _crashInProgress.Remove(seg);
        }

        // Remove from active list (CrashScene will manage them now).
        _active.Remove(carA);
        _active.Remove(carB);

        SpawnCrashScene(carA, carB, seg);

        Debug.Log($"[CarManager] Crash impact on '{seg?.segmentID}' — scene built.");
    }

    // ─────────────────────────────────────────
    //  CRASH SCENE FACTORY
    // ─────────────────────────────────────────

    private void SpawnCrashScene(CarAgent carA, CarAgent carB, RoadSegment seg)
    {
        Vector3 midpoint = (carA.transform.position + carB.transform.position) * 0.5f;

        var go = new GameObject($"CrashScene_{seg.segmentID}_{Time.frameCount}");
        go.transform.position = midpoint;

        var scene = go.AddComponent<CrashScene>();
        scene.carA = carA;
        scene.carB = carB;
        scene.segment = seg;
        scene.smokeVFXPrefab = smokeVFXPrefab;
        scene.barrierFencePrefab = barrierFencePrefab;
        scene.disappearDuration = crashDisappearDuration;
        scene.fadeDuration = crashFadeDuration;
        scene.barrierPadding = barrierPadding;

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

        // Show crash-in-progress segments in red.
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