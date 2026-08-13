using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Level selection panels are rendered here, with locked/unlocked state reflected per level.

public class LevelSelectManager : MonoBehaviour
{
    [System.Serializable]
    public class LevelPanelEntry
    {
        public int level = 1;
        public GameObject panelRoot;
        public Button selectButton;
        public GameObject lockedOverlay;
        [SceneName] public string sceneName;
    }

    [Header("Level Panels (in order)")]
    [SerializeField] private List<LevelPanelEntry> levelPanels = new List<LevelPanelEntry>();

    [Header("Developer Mode")]
    public bool developerMode = false;

    private readonly LevelSelectViewModel _vm = new LevelSelectViewModel();

    private void OnEnable()
    {
        RefreshLevelLocks();
    }

    public void RefreshLevelLocks()
    {
        foreach (LevelPanelEntry entry in levelPanels)
        {
            if (entry == null) continue;

            bool unlocked = _vm.IsUnlocked(entry.level, developerMode);

            if (entry.selectButton != null)
            {
                entry.selectButton.interactable = unlocked;
                entry.selectButton.onClick.RemoveAllListeners();

                if (unlocked)
                {
                    int capturedLevel = entry.level;
                    string capturedSceneName = entry.sceneName;

                    entry.selectButton.onClick.AddListener(() =>
                    {
                        _vm.SelectLevel(capturedLevel);
                        Navigate(capturedSceneName);
                    });
                }
            }

            if (entry.lockedOverlay != null)
                entry.lockedOverlay.SetActive(!unlocked);
        }
    }

    private void Navigate(string sceneName)
    {
        if (PageManager.Instance != null)
            PageManager.Instance.ChangeUI(sceneName);
        else
            SceneManager.LoadScene(sceneName);
    }
}

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
