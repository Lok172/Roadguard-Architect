using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LevelSpawnerActivator
///
/// Simple, self-contained spawner (and now checkpoint) activator.
/// Assign one entry per level scene, drag in its spawner GameObjects
/// AND its checkpoint GameObjects.
/// Call Activate(sceneName) from PageManager after a scene switch.
///
/// SETUP:
///   1. Attach to any persistent GameObject in your Bootstrap/City scene.
///   2. Expand "Level Groups" in the Inspector.
///   3. For each level, set Scene Name and drag in its Car Spawner
///      GameObjects AND its Checkpoint GameObjects.
///
/// NOTE: This gating is only needed for spawners/checkpoints that live in
/// a SHARED/persistent scene (like your spawners currently do). If a set
/// of checkpoints instead lives inside a level-specific scene (LV1, LV2,
/// LV3) that PageManager loads/unloads on its own, Unity already removes
/// them when that scene unloads — you don't need to list those here too.
/// Only list checkpoints that sit in a scene that stays loaded across
/// levels and would otherwise stay active/interfering with the wrong level.
/// </summary>
public class LevelSpawnerActivator : MonoBehaviour
{
    public static LevelSpawnerActivator Instance { get; private set; }

    [System.Serializable]
    public class LevelGroup
    {
        [Tooltip("Exact scene name as in Build Settings (e.g. LV1, LV2, LV3)")]
        [SceneName]
        public string sceneName;

        [Tooltip("All Car Spawner GameObjects that belong to this level")]
        public List<GameObject> spawners = new List<GameObject>();

        [Tooltip("All Checkpoint GameObjects that belong to this level")]
        public List<GameObject> checkpoints = new List<GameObject>();
    }

    [Header("Level Groups")]
    public List<LevelGroup> levelGroups = new List<LevelGroup>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Deactivate everything on boot
        DeactivateAll();
    }

    /// <summary>
    /// Called by PageManager after a scene switch.
    /// Scans all loaded scenes, activates matching spawners + checkpoints,
    /// then tells GameManager to re-init.
    /// </summary>
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
            Debug.Log($"[LevelSpawnerActivator] No group for scene '{loadedSceneName}' — all spawners/checkpoints off.");
            return;
        }

        foreach (GameObject spawner in match.spawners)
        {
            if (spawner != null)
                spawner.SetActive(true);
        }

        foreach (GameObject checkpoint in match.checkpoints)
        {
            if (checkpoint != null)
                checkpoint.SetActive(true);
        }

        Debug.Log($"[LevelSpawnerActivator] Activated {match.spawners.Count} spawner(s) and " +
                  $"{match.checkpoints.Count} checkpoint(s) for '{loadedSceneName}'.");

        // Wait a frame then tell GameManager to scan and start them
        StartCoroutine(NotifyGameManager());
    }

    private IEnumerator NotifyGameManager()
    {
        yield return null; // let SetActive propagate

        if (GameManager.Instance != null)
        {
            Debug.Log("[LevelSpawnerActivator] Notifying GameManager to re-init spawners.");
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
            foreach (GameObject spawner in group.spawners)
                if (spawner != null)
                    spawner.SetActive(false);

            foreach (GameObject checkpoint in group.checkpoints)
                if (checkpoint != null)
                    checkpoint.SetActive(false);
        }
    }
}