using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class UISceneGroup
{
    [Header("UI Scene")]
    [SceneName]
    public string uiSceneName;

    [Header("Additional Scenes To Load With This UI")]
    [SceneName]
    public string[] additionalScenes;
}

public class PageManager : MonoBehaviour
{
    public static PageManager Instance { get; private set; }

    [Header("Master Permanent Scenes (Always Loaded)")]
    [SceneName]
    [SerializeField] private string[] masterPermanentScenes;

    [Header("UI Scene Groups")]
    [SerializeField] private UISceneGroup[] uiSceneGroups;

    public string currentLoadedUI { get; private set; } = "";
    private string pageManagerSceneName;
    private UIThemeManager uiThemeManager;

    private readonly List<string> currentAdditionalScenes = new List<string>();

    // Guard: prevents a second ChangeUI from running while one is in progress.
    private bool _isSwitching = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        pageManagerSceneName = gameObject.scene.name;
        uiThemeManager = GetComponent<UIThemeManager>();
    }

    private void Start()
    {
        StartCoroutine(InitialBoot());
    }

    private IEnumerator InitialBoot()
    {
        if (uiSceneGroups.Length > 0)
        {
            yield return StartCoroutine(SwitchUIScene(uiSceneGroups[0].uiSceneName));
        }

        foreach (string sceneName in masterPermanentScenes)
        {
            yield return StartCoroutine(LoadSceneIfNotLoaded(sceneName));
        }

        CleanupStrayScenes();
    }

    private IEnumerator LoadSceneIfNotLoaded(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            yield break;

        Debug.Log("LOADING => " + sceneName);

        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            Debug.Log($"LOADED {sceneName} : {SceneManager.GetSceneByName(sceneName).isLoaded}");
        }
    }

    private IEnumerator UnloadSceneIfLoaded(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            yield break;

        // SAFETY: never unload a permanent scene or the PageManager's own scene.
        if (IsProtectedScene(sceneName))
        {
            Debug.Log($"[PageManager] Skipping unload of protected scene: {sceneName}");
            yield break;
        }

        if (SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(sceneName);
        }
    }

    public void ChangeUI(string targetSceneName)
    {
        StartCoroutine(ChangeUIWhenReady(targetSceneName, force: false));
    }

    /// <summary>
    /// Like ChangeUI but forces a full unload + reload even if the target
    /// scene is already the current one. Used by PauseMenuController.RestartLevel
    /// so the level scene and all its additional scenes (City, LvUI & Manager …)
    /// are torn down and rebuilt from scratch.
    /// </summary>
    public void ForceChangeUI(string targetSceneName)
    {
        StartCoroutine(ChangeUIWhenReady(targetSceneName, force: true));
    }

    // If a switch is already in progress, wait for it to finish before starting
    // the next one. This prevents a race where LevelSelect loads then something
    // immediately fires ChangeUI("LV1"), causing City/Bootstrap to be unloaded
    // mid-switch.
    private IEnumerator ChangeUIWhenReady(string targetSceneName, bool force)
    {
        while (_isSwitching)
            yield return null;

        yield return StartCoroutine(SwitchUIScene(targetSceneName, force));
    }

    private IEnumerator SwitchUIScene(string newSceneName, bool force = false)
    {
        // Skip if already on this scene, UNLESS forced (e.g. Restart button).
        if (!force && currentLoadedUI == newSceneName)
            yield break;

        _isSwitching = true;

        UISceneGroup targetGroup = GetUISceneGroup(newSceneName);

        // Collect the full set of scenes the NEW group needs.
        HashSet<string> incomingAdditional = new HashSet<string>();
        if (targetGroup?.additionalScenes != null)
            foreach (string s in targetGroup.additionalScenes)
                if (!string.IsNullOrWhiteSpace(s)) incomingAdditional.Add(s);

        // On a forced restart of the same scene we must unload everything first,
        // then reload — otherwise Unity would see "already loaded" and skip it.
        if (force && currentLoadedUI == newSceneName)
        {
            // Unload all additional scenes for this group.
            foreach (string sceneName in currentAdditionalScenes)
                yield return StartCoroutine(UnloadSceneIfLoaded(sceneName));

            // Unload the UI scene itself.
            yield return StartCoroutine(UnloadSceneIfLoaded(newSceneName));

            currentLoadedUI = "";
            currentAdditionalScenes.Clear();
        }

        // 1. Load the new UI scene.
        yield return StartCoroutine(LoadSceneIfNotLoaded(newSceneName));

        // 2. Load any additional scenes the new group needs (skip ones already loaded).
        foreach (string sceneName in incomingAdditional)
            yield return StartCoroutine(LoadSceneIfNotLoaded(sceneName));

        // 3. Unload old UI scene (only relevant when switching to a different scene).
        if (!string.IsNullOrEmpty(currentLoadedUI) && currentLoadedUI != newSceneName)
            yield return StartCoroutine(UnloadSceneIfLoaded(currentLoadedUI));

        // 4. Unload old additional scenes that the new group does NOT need.
        //    Skip anything the incoming group shares, and skip permanent/protected scenes.
        foreach (string sceneName in currentAdditionalScenes)
        {
            if (!incomingAdditional.Contains(sceneName))
                yield return StartCoroutine(UnloadSceneIfLoaded(sceneName));
        }

        // 5. Update tracking.
        currentLoadedUI = newSceneName;
        currentAdditionalScenes.Clear();
        currentAdditionalScenes.AddRange(incomingAdditional);

        // 6. Set the new scene as active.
        Scene activeScene = SceneManager.GetSceneByName(newSceneName);
        if (activeScene.IsValid() && activeScene.isLoaded)
            SceneManager.SetActiveScene(activeScene);

        Debug.Log($"Successfully switched to: {newSceneName}");

        // 7. Apply UI theme.
        uiThemeManager?.ApplyThemeToAllButtons();
        Debug.Log($"Successfully applied theme to all buttons in: {newSceneName}");

        _isSwitching = false;
    }

    // Returns true for scenes that should NEVER be unloaded by SwitchUIScene.
    private bool IsProtectedScene(string sceneName)
    {
        if (sceneName == pageManagerSceneName) return true;
        return IsInArray(masterPermanentScenes, sceneName);
    }

    private UISceneGroup GetUISceneGroup(string uiSceneName)
    {
        foreach (UISceneGroup group in uiSceneGroups)
            if (group.uiSceneName == uiSceneName)
                return group;
        return null;
    }

    private void CleanupStrayScenes()
    {
        int sceneCount = SceneManager.sceneCount;
        for (int i = sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            bool isProtected =
                scene.name == pageManagerSceneName ||
                scene.name == currentLoadedUI ||
                IsInArray(masterPermanentScenes, scene.name) ||
                currentAdditionalScenes.Contains(scene.name);

            if (!isProtected && scene.isLoaded)
            {
                Debug.Log("Cleaning up stray editor scene: " + scene.name);
                SceneManager.UnloadSceneAsync(scene);
            }
        }
    }

    private bool IsInArray(string[] array, string value)
    {
        if (array == null) return false;
        foreach (string item in array)
            if (item == value) return true;
        return false;
    }

    public void QuitApplication()
    {
        Debug.Log("Exiting Game...");
        Application.Quit();
    }
}