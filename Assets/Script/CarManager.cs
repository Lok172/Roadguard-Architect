using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR MANAGER  (v3 — graph-based, drag-pause, fixed endpoints)
//
//  Single scene singleton. Attach to a "CarManager" GameObject.
//
//  ── Setup ─────────────────────────────────────────────────────
//  1. Assign car prefabs in the Inspector.
//  2. All RoadIntersection objects in the scene are auto-collected.
//  3. Spawn cars via the pool up to maxActiveCars.
//
//  ── Fixed Spawn/Despawn Points ────────────────────────────────
//  Assign spawnIntersections and despawnIntersections in the
//  Inspector. Cars will always start at one of the spawn nodes
//  and their A* destination will be one of the despawn nodes.
//  Leave both lists empty to use the old behaviour (random start,
//  random destination from all intersections).
//
//  ── Drag Pause ────────────────────────────────────────────────
//  CarManager tracks mouse/touch drag state via IsDragging (static).
//  CarAgent.Update() checks this flag and skips movement while true.
//  Crash recovery timers also pause so no phantom despawns occur.
//  Assign dragButton (default: right mouse button for camera orbit)
//  or change to 0 for left button depending on your camera rig.
//
//  ── Accident System ───────────────────────────────────────────
//  Every riskEvalInterval seconds, for each active car:
//    risk = segment.CalculateRisk(carSpeed)
//    if Random(0,1) < risk → SetCrashed()
//
//  Also callable per day-tick via EvaluateAccidents() from GameManager.
// ─────────────────────────────────────────────────────────────────

public class CarManager : MonoBehaviour
{
    public static CarManager Instance { get; private set; }

    // ── Drag Detection ────────────────────────
    /// <summary>
    /// True while the user is dragging a device from PlacementManager,
    /// OR while the camera-rotate button is held past the drag threshold.
    /// All CarAgents pause movement while this is true.
    /// </summary>
    public static bool IsDragging =>
        (PlacementManager.Instance != null && PlacementManager.Instance.IsDragging)
        || _cameraIsDragging;

    [Header("Drag Pause")]
    [Tooltip("Mouse button index that triggers a drag pause (0=left, 1=right, 2=middle). " +
             "Match this to your camera-rotate button.")]
    public int dragButton = 1;

    [Tooltip("Minimum pixel distance the mouse must travel after button-down before " +
             "the move is treated as a drag (prevents accidental pause on a click).")]
    [Min(0f)] public float dragThresholdPixels = 5f;

    private bool _buttonHeld;
    private Vector3 _dragStartScreen;
    private static bool _cameraIsDragging;

    // ── Car Prefabs ───────────────────────────
    [Header("Car Prefabs")]
    [Tooltip("All car prefabs. Each must have (or will get) a CarAgent component.")]
    public List<GameObject> carPrefabs = new List<GameObject>();

    [Tooltip("Initial pool size created per prefab at startup.")]
    [Min(1)] public int poolSizePerPrefab = 10;

    // ── Spawn Settings ────────────────────────
    [Header("Spawn Settings")]
    [Tooltip("Maximum cars active in the scene at any time.")]
    [Min(1)] public int maxActiveCars = 10;

    [Tooltip("Seconds between a car despawning and a new one spawning.")]
    [Min(0f)] public float respawnDelay = 3f;

    [Tooltip("If true, auto-collects all RoadIntersection objects in the scene on Start.")]
    public bool autoCollectIntersections = true;

    [Tooltip("Manual intersection list — used if autoCollectIntersections is false.")]
    public List<RoadIntersection> intersections = new List<RoadIntersection>();

    // ── Fixed Endpoints ───────────────────────
    [Header("Fixed Spawn / Despawn Points")]
    [Tooltip("Cars always spawn FROM one of these intersections. " +
             "Leave empty to spawn from a random intersection in the scene.")]
    public List<RoadIntersection> spawnIntersections = new List<RoadIntersection>();

    [Tooltip("Cars always route TOWARD one of these intersections as their destination, " +
             "then despawn on arrival. " +
             "Leave empty to use random destinations from all intersections.")]
    public List<RoadIntersection> despawnIntersections = new List<RoadIntersection>();

    // ── Accident System ───────────────────────
    [Header("Accident System")]
    [Tooltip("How often (real seconds) the risk evaluation loop runs.")]
    [Min(0.5f)] public float riskEvalInterval = 5f;

    [Tooltip("VFX prefab spawned at the crash world position.")]
    public GameObject crashVFXPrefab;

    [Tooltip("Seconds the wrecked car stays before despawning (segment unblocked after this).")]
    [Min(1f)] public float crashRecoveryDuration = 5f;

    // ── Internal Pool ─────────────────────────
    private readonly Dictionary<int, Queue<CarAgent>> _pool =
        new Dictionary<int, Queue<CarAgent>>();

    private readonly List<CarAgent> _active = new List<CarAgent>();

    private readonly Dictionary<CarAgent, int> _agentPrefabIndex =
        new Dictionary<CarAgent, int>();

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CarManager] Duplicate — destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (autoCollectIntersections)
        {
            intersections.Clear();
            intersections.AddRange(FindObjectsOfType<RoadIntersection>());
            Debug.Log($"[CarManager] Auto-collected {intersections.Count} intersections.");
        }

        if (intersections.Count < 2)
        {
            Debug.LogWarning("[CarManager] Need at least 2 intersections to spawn cars.");
            return;
        }

        BuildPool();
        FillToMax();
        StartCoroutine(RiskEvalLoop());
    }

    private void Update()
    {
        TrackDrag();
    }

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
            {
                _cameraIsDragging = true;
                Debug.Log("[CarManager] Camera drag started — cars paused.");
            }
        }

        if (Input.GetMouseButtonUp(dragButton))
        {
            _buttonHeld = false;
            if (_cameraIsDragging)
            {
                _cameraIsDragging = false;
                Debug.Log("[CarManager] Camera drag ended — cars resumed.");
            }
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
                CarAgent a = CreateAgent(i);
                a.gameObject.SetActive(false);
                _pool[i].Enqueue(a);
            }
        }
        Debug.Log($"[CarManager] Pool built: {carPrefabs.Count} prefab(s) × {poolSizePerPrefab}.");
    }

    private CarAgent CreateAgent(int prefabIdx)
    {
        GameObject go = Instantiate(carPrefabs[prefabIdx], transform);
        go.name = $"{carPrefabs[prefabIdx].name}_pool";

        CarAgent agent = go.GetComponent<CarAgent>() ?? go.AddComponent<CarAgent>();
        _agentPrefabIndex[agent] = prefabIdx;
        return agent;
    }

    private CarAgent CheckoutAgent()
    {
        if (carPrefabs.Count == 0) return null;

        int prefabIdx = Random.Range(0, carPrefabs.Count);

        if (_pool.TryGetValue(prefabIdx, out var queue) && queue.Count > 0)
            return queue.Dequeue();

        if (carPrefabs[prefabIdx] != null)
        {
            CarAgent extra = CreateAgent(prefabIdx);
            return extra;
        }
        return null;
    }

    // ─────────────────────────────────────────
    //  SPAWN
    // ─────────────────────────────────────────

    private void FillToMax()
    {
        int toSpawn = maxActiveCars - _active.Count;
        for (int i = 0; i < toSpawn; i++)
            SpawnOne();
    }

    private void SpawnOne()
    {
        if (intersections.Count < 2) return;

        CarAgent agent = CheckoutAgent();
        if (agent == null) return;

        // ── Choose start node ──────────────────
        RoadIntersection start;
        if (spawnIntersections != null && spawnIntersections.Count > 0)
            start = spawnIntersections[Random.Range(0, spawnIntersections.Count)];
        else
            start = intersections[Random.Range(0, intersections.Count)];

        // ── Choose destination node ────────────
        // If fixed despawn points are defined, override the agent's random routing
        // by handing it a filtered allNodes list that contains only the despawn nodes,
        // so PickNewDestinationAndRoute() will route to one of them.
        List<RoadIntersection> nodeList;
        if (despawnIntersections != null && despawnIntersections.Count > 0)
            nodeList = despawnIntersections;
        else
            nodeList = intersections;

        agent.gameObject.SetActive(true);
        agent.Initialise(start, nodeList);

        _active.Add(agent);
    }

    /// <summary>
    /// Called by CarAgent when it despawns (route complete or crash recovery done).
    /// </summary>
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
        if (_active.Count < maxActiveCars)
            SpawnOne();
    }

    // ─────────────────────────────────────────
    //  ACCIDENT SYSTEM — REAL-TIME LOOP
    // ─────────────────────────────────────────

    private IEnumerator RiskEvalLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(riskEvalInterval);
            // Skip accident rolls while dragging so nothing crashes during camera moves.
            if (!IsDragging)
                EvaluateAccidents();
        }
    }

    /// <summary>
    /// Evaluates accident risk for every active car.
    /// Also callable from GameManager on a day tick.
    /// </summary>
    public void EvaluateAccidents()
    {
        var snapshot = new List<CarAgent>(_active);

        foreach (var agent in snapshot)
        {
            if (agent == null) continue;

            RoadSegment seg = agent.CurrentSegment;
            if (seg == null || seg.IsBlocked) continue;

            float risk = seg.CalculateRisk(agent.baseSpeed);

            if (Random.value < risk)
            {
                agent.SetCrashed(crashVFXPrefab, crashRecoveryDuration);
                Debug.Log($"[CarManager] Accident on segment '{seg.segmentID}' " +
                          $"(risk={risk:F2}).");
            }
        }
    }

    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Active cars
        Gizmos.color = Color.yellow;
        foreach (var a in _active)
        {
            if (a == null) continue;
            Gizmos.DrawSphere(a.transform.position + Vector3.up * 0.5f, 0.25f);
        }

        // Spawn nodes
        Gizmos.color = Color.green;
        foreach (var s in spawnIntersections)
        {
            if (s == null) continue;
            Gizmos.DrawWireSphere(s.transform.position, 0.8f);
            UnityEditor.Handles.Label(s.transform.position + Vector3.up * 1.5f, "SPAWN");
        }

        // Despawn nodes
        Gizmos.color = Color.red;
        foreach (var d in despawnIntersections)
        {
            if (d == null) continue;
            Gizmos.DrawWireSphere(d.transform.position, 0.8f);
            UnityEditor.Handles.Label(d.transform.position + Vector3.up * 1.5f, "DESPAWN");
        }
    }
#endif
}