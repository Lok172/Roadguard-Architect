using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// LevelSpawnerActivator activates the spawners, despawners, and intersections assigned to the
// currently loaded level scene. Each list entry may be a leaf object or a parent object
// containing multiple children of the relevant type — activation and material visibility are
// applied to the entry and every descendant beneath it. Checkpoints are not scoped to a level
// entry: whichever CheckpointScript objects are currently active in the scene, from wherever
// they live, have their material visibility set from checkpointsVisible. Material visibility for
// spawners, despawners, intersections, and checkpoints is controlled globally across all levels,
// not per level. This is called by PageManager after a scene switch completes.
public class LevelSpawnerActivator : MonoBehaviour
{
    public static LevelSpawnerActivator Instance { get; private set; }

    [System.Serializable]
    public class LevelGroup
    {
        [SceneName]
        public string sceneName;

        [Tooltip("Spawner objects for this level. An entry may be a single spawner or a parent containing several.")]
        public List<GameObject> spawners = new List<GameObject>();

        [Tooltip("Despawner objects for this level. An entry may be a single despawner or a parent containing several.")]
        public List<GameObject> despawners = new List<GameObject>();

        [Tooltip("Intersection objects for this level. An entry may be a single intersection or a parent containing several.")]
        public List<GameObject> intersections = new List<GameObject>();
    }

    [Header("Level Groups")]
    public List<LevelGroup> levelGroups = new List<LevelGroup>();

    [Header("Components Materials")]
    public bool spawnersVisible = true;
    public bool despawnersVisible = true;
    public bool checkpointsVisible = true;
    public bool intersectionsVisible = true;

    [Header("Test Function")]
    [Tooltip("If true, the spawners in testSpawners (including their children) begin spawning automatically in Start(), for quick testing without going through PageManager/GameManager.")]
    public bool testSpawnOnStart = false;

    [Tooltip("Spawner objects used for testing. An entry may be a single spawner or a parent containing several.")]
    public List<GameObject> testSpawners = new List<GameObject>();

    [Tooltip("If true, skips the Planning Phase / Simulate-button flow by calling GameManager's existing StartSimulationAfterCountdown() directly once GameManager.PhaseFlowStarted becomes true, so cars begin moving without clicking Simulate. GameManager is not modified — this only calls its existing public members.")]
    public bool bypassPhaseFlowForTesting = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        DeactivateAll();
    }

    private void Start()
    {
        ApplyCheckpointVisibility();

        if (testSpawnOnStart)
            TestSpawnAssignedSpawners();

        if (bypassPhaseFlowForTesting)
            StartCoroutine(BypassPhaseFlowRoutine());
    }

    private IEnumerator BypassPhaseFlowRoutine()
    {
        while (GameManager.Instance == null || !GameManager.Instance.PhaseFlowStarted)
            yield return null;

        GameManager.Instance.StartSimulationAfterCountdown();
        Debug.Log("[LevelSpawnerActivator] Test bypass: called GameManager.StartSimulationAfterCountdown() directly.");
    }

    [ContextMenu("Test Spawn Assigned Spawners")]
    public void TestSpawnAssignedSpawners()
    {
        Time.timeScale = 1f;

        int count = 0;

        foreach (GameObject entry in testSpawners)
        {
            if (entry == null) continue;

            entry.SetActive(true);

            carspawnerscript[] spawnersInEntry = entry.GetComponentsInChildren<carspawnerscript>(true);
            foreach (carspawnerscript spawner in spawnersInEntry)
            {
                if (spawner != null)
                {
                    spawner.ResetAndSpawn();
                    count++;
                }
            }
        }

        Debug.Log($"[LevelSpawnerActivator] Test spawn triggered on {count} spawner(s).");
    }

    public void Activate(string loadedSceneName)
    {
        DeactivateAll();

        LevelGroup match = null;
        foreach (LevelGroup group in levelGroups)
        {
            if (group.sceneName == loadedSceneName)
            {
                match = group;
                break;
            }
        }

        if (match == null)
        {
            Debug.Log($"[LevelSpawnerActivator] No group for scene '{loadedSceneName}'.");
            return;
        }

        SetGroupActive(match.spawners, true, spawnersVisible);
        SetGroupActive(match.despawners, true, despawnersVisible);
        SetGroupActive(match.intersections, true, intersectionsVisible);
        ApplyCheckpointVisibility();

        Debug.Log($"[LevelSpawnerActivator] Activated {match.spawners.Count} spawner(s), " +
                  $"{match.despawners.Count} despawner(s), {match.intersections.Count} intersection(s) for '{loadedSceneName}'.");

        StartCoroutine(NotifyGameManager());
    }

    private IEnumerator NotifyGameManager()
    {
        yield return null;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.InitLevel();
        }
        else
        {
            Debug.LogWarning("[LevelSpawnerActivator] GameManager.Instance not found.");
        }
    }

    private void DeactivateAll()
    {
        foreach (LevelGroup group in levelGroups)
        {
            SetGroupActive(group.spawners, false, spawnersVisible);
            SetGroupActive(group.despawners, false, despawnersVisible);
            SetGroupActive(group.intersections, false, intersectionsVisible);
        }
    }

    /// <summary>
    /// Sets material visibility on every CheckpointScript currently active in the scene,
    /// regardless of which level or hierarchy it belongs to.
    /// </summary>
    public void ApplyCheckpointVisibility()
    {
        CheckpointScript[] activeCheckpoints = FindObjectsByType<CheckpointScript>(FindObjectsSortMode.None);

        foreach (CheckpointScript cp in activeCheckpoints)
        {
            if (cp == null || !cp.gameObject.activeInHierarchy) continue;

            MeshRenderer mr = cp.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = checkpointsVisible;
        }
    }

    private static void SetGroupActive(List<GameObject> objects, bool active, bool materialVisible)
    {
        foreach (GameObject obj in objects)
        {
            if (obj == null) continue;

            obj.SetActive(active);

            if (active)
            {
                MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer mr in renderers)
                    mr.enabled = materialVisible;
            }
        }
    }
}