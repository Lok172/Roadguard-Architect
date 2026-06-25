using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// SpawnerManager
///
/// Detects which level scene is currently loaded and activates only the
/// spawners assigned to that scene. No PlayerPrefs dependency.
///
/// SETUP:
///   1. Attach this script to a persistent GameObject (e.g. GameManager).
///   2. In the Inspector, expand "Level Spawner Groups".
///   3. For each entry, set "Scene Name" to the exact level scene name
///      (use the [SceneName] dropdown) and drag in the spawner GameObjects.
///   4. Press Play — SpawnerManager matches the active scene and activates
///      only the matching group's spawners.
/// </summary>
public class SpawnerManager : MonoBehaviour
{
    // ─── Data ─────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class LevelSpawnerGroup
    {
        [Tooltip("The level scene this group belongs to. Must exactly match the " +
                 "scene name in Build Settings.")]
        [SceneName] public string sceneName;

        [Tooltip("All spawner GameObjects that should be active when this scene is loaded.")]
        public List<GameObject> spawners = new List<GameObject>();
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Level Spawner Groups")]
    [Tooltip("One entry per level scene. Assign the scene name and its spawners.")]
    public List<LevelSpawnerGroup> levelSpawnerGroups = new List<LevelSpawnerGroup>();

    [Header("Fallback")]
    [Tooltip("Activated if no group matches the active scene (optional).")]
    public List<GameObject> fallbackSpawners = new List<GameObject>();

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Deactivate ALL spawners immediately so nothing fires before the
        // correct group is determined.
        foreach (LevelSpawnerGroup group in levelSpawnerGroups)
            SetGroupActive(group.spawners, false);
        SetGroupActive(fallbackSpawners, false);
    }

    private void Start()
    {

    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the currently active scene name and activates the matching
    /// spawner group. Call this again if the scene changes at runtime.
    /// </summary>
    /// <summary>
    /// Called by PageManager after a UI/level scene switch completes.
    /// Checks which level scenes are currently loaded and activates matching spawners.
    /// </summary>
    public void RefreshSpawnersForLoadedScenes()
    {
        // Deactivate all first.
        foreach (LevelSpawnerGroup group in levelSpawnerGroups)
            SetGroupActive(group.spawners, false);
        SetGroupActive(fallbackSpawners, false);

        bool anyMatched = false;

        foreach (LevelSpawnerGroup group in levelSpawnerGroups)
        {
            Scene scene = SceneManager.GetSceneByName(group.sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SetGroupActive(group.spawners, true);
                Debug.Log($"[SpawnerManager] Activated {group.spawners.Count} spawner(s) for loaded scene '{group.sceneName}'.");
                anyMatched = true;
            }
        }

        if (!anyMatched)
        {
            SetGroupActive(fallbackSpawners, true);
            Debug.LogWarning($"[SpawnerManager] No matching level scenes loaded. Activated {fallbackSpawners.Count} fallback spawner(s).");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void SetGroupActive(List<GameObject> spawners, bool active)
    {
        foreach (GameObject spawner in spawners)
        {
            if (spawner != null)
                spawner.SetActive(active);
        }
    }
}