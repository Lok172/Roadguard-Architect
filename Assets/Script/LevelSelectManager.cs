using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────
//  LEVEL SELECT MANAGER
//
//  Place on the "LevelSelect" Canvas (or any parent of LevelPanel 1/2/3).
//  Locks each level's Select button until the previous level has been
//  cleared (tracked via LevelProgress / PlayerPrefs).
//
//  Setup per entry in the Inspector:
//    level         → 1, 2, 3 …
//    panelRoot     → the LevelPanel 1 / 2 / 3 GameObject
//    selectButton  → the "Select" Button inside that panel
//    lockedOverlay → OPTIONAL lock icon / dimmer shown while locked
//    sceneName     → scene to load when this level's button is pressed
//
//  Tick "Developer Mode" in the Inspector to unlock every level
//  regardless of saved progress (handy for testing/demoing).
// ─────────────────────────────────────────────────────────────────

public class LevelSelectManager : MonoBehaviour
{
    [System.Serializable]
    public class LevelPanelEntry
    {
        [Tooltip("Level number this panel represents (1, 2, 3 …).")]
        public int level = 1;

        [Tooltip("The LevelPanel GameObject (e.g. 'LevelPanel 1').")]
        public GameObject panelRoot;

        [Tooltip("The 'Select' Button inside this panel.")]
        public Button selectButton;

        [Tooltip("Optional lock icon / dim overlay shown while the level is locked.")]
        public GameObject lockedOverlay;

        [Tooltip("Scene to load when this level's Select button is clicked. " +
                 "Used if PageManager is not present in the scene.")]
        [SceneName] public string sceneName;
    }

    [Header("Level Panels (in order)")]
    [SerializeField] private List<LevelPanelEntry> levelPanels = new List<LevelPanelEntry>();

    [Header("Developer Mode")]
    [Tooltip("When ON, every level panel is unlocked regardless of saved progress.")]
    public bool developerMode = false;

    private void OnEnable()
    {
        RefreshLevelLocks();
    }

    /// <summary>
    /// Re-evaluates the locked/unlocked state for every panel and
    /// (re-)wires each Select button to load its assigned scene.
    /// Safe to call repeatedly; old listeners are cleared first.
    /// </summary>
    public void RefreshLevelLocks()
    {
        foreach (LevelPanelEntry entry in levelPanels)
        {
            if (entry == null) continue;

            bool unlocked = developerMode || LevelProgress.IsLevelUnlocked(entry.level);

            if (entry.selectButton != null)
            {
                entry.selectButton.interactable = unlocked;

                // Clear old listeners to prevent stacking on repeated OnEnable calls.
                entry.selectButton.onClick.RemoveAllListeners();

                if (unlocked)
                {
                    // Capture loop variables for the closure.
                    int capturedLevel = entry.level;
                    string capturedSceneName = entry.sceneName;

                    entry.selectButton.onClick.AddListener(() =>
                    {
                        // Persist the chosen level so GameManager can read it
                        // even though it lives in a different scene.
                        PlayerPrefs.SetInt("CurrentLevel", capturedLevel);
                        PlayerPrefs.Save();

                        if (PageManager.Instance != null)
                            PageManager.Instance.ChangeUI(capturedSceneName);
                        else
                            SceneManager.LoadScene(capturedSceneName);
                    });
                }
            }

            if (entry.lockedOverlay != null)
                entry.lockedOverlay.SetActive(!unlocked);
        }
    }
}

// ─────────────────────────────────────────────────────────────────
//  CUSTOM INSPECTOR — Developer Controls
// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[CustomEditor(typeof(LevelSelectManager))]
public class LevelSelectManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelSelectManager manager = (LevelSelectManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Developer Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to reset progress or refresh locks at runtime.",
                MessageType.Info);
            return;
        }

        if (GUILayout.Button("🔄  Refresh Locks", GUILayout.Height(28)))
            manager.RefreshLevelLocks();

        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("🗑  Reset All Level Progress", GUILayout.Height(28)))
        {
            LevelProgress.ResetProgress();
            manager.RefreshLevelLocks();
        }
        GUI.backgroundColor = Color.white;
    }
}
#endif