using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// LevelSpawnerActivator activates the spawners, despawners, and checkpoints assigned to the
// currently loaded level scene. Each list entry may be a leaf object or a parent object
// containing multiple children of the relevant type — activation and material visibility are
// applied to the entry and every descendant beneath it. Material visibility for spawners,
// despawners, and checkpoints is controlled globally across all levels, not per level. It is
// called by PageManager after a scene switch completes.
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

        [Tooltip("Checkpoint objects for this level. An entry may be a single checkpoint or a parent containing several.")]
        public List<GameObject> checkpoints = new List<GameObject>();
    }

    [Header("Level Groups")]
    public List<LevelGroup> levelGroups = new List<LevelGroup>();

    [Header("Components Materials")]
    public bool spawnersVisible = true;
    public bool despawnersVisible = true;
    public bool checkpointsVisible = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        DeactivateAll();
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
        SetGroupActive(match.checkpoints, true, checkpointsVisible);

        Debug.Log($"[LevelSpawnerActivator] Activated {match.spawners.Count} spawner(s), " +
                  $"{match.despawners.Count} despawner(s), {match.checkpoints.Count} checkpoint(s) for '{loadedSceneName}'.");

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
            SetGroupActive(group.checkpoints, false, checkpointsVisible);
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