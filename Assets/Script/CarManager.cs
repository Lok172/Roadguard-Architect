using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR MANAGER  (v4 — merged endpoint list, PlaceAtLaneStart)
//
//  CHANGES vs v3:
//    • spawnIntersections + despawnIntersections merged into a single
//      endpointIntersections list. Cars spawn FROM and despawn AT
//      any intersection in this list.
//    • PlaceAtLaneStart() places the car correctly in its lane
//      BEFORE SetActive(true) so no ghost appears on first frame.
//    • Initialise() signature updated to pass endPoints list.
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
    public GameObject crashVFXPrefab;
    [Min(1f)] public float crashRecoveryDuration = 5f;

    // ── Internal Pool ─────────────────────────
    private readonly Dictionary<int, Queue<CarAgent>> _pool =
        new Dictionary<int, Queue<CarAgent>>();
    private readonly List<CarAgent> _active = new List<CarAgent>();
    private readonly Dictionary<CarAgent, int> _agentPrefabIndex = new Dictionary<CarAgent, int>();

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

        // Pick start node from endpointIntersections if set, else any node.
        var endpoints = (endpointIntersections != null && endpointIntersections.Count > 0)
                         ? endpointIntersections : intersections;
        var start = endpoints[Random.Range(0, endpoints.Count)];

        // ── Place car in its first lane BEFORE activating ─────────────
        // This prevents the one-frame ghost at node centre on spawn.
        PlaceAtLaneStart(agent, start);

        agent.gameObject.SetActive(true);
        agent.Initialise(start, intersections, endpoints);

        _active.Add(agent);
    }

    /// <summary>
    /// Finds the first segment connected to <paramref name="startNode"/> and positions
    /// the car at startNode.position + laneOffset so it spawns already in its lane.
    /// </summary>
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
        // Fallback: place at node centre if no segment found.
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
    //  ACCIDENT SYSTEM
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
        var snapshot = new List<CarAgent>(_active);
        foreach (var agent in snapshot)
        {
            if (agent == null) continue;
            var seg = agent.CurrentSegment;
            if (seg == null || seg.IsBlocked) continue;
            float risk = seg.CalculateRisk(agent.baseSpeed);
            if (Random.value < risk)
            {
                agent.SetCrashed(crashVFXPrefab, crashRecoveryDuration);
                Debug.Log($"[CarManager] Accident on '{seg.segmentID}' (risk={risk:F2})");
            }
        }
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
    }
#endif
}