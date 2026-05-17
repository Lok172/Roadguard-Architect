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
    [Header("Master Permanent Scenes (Always Loaded)")]
    [SerializeField] private string[] masterPermanentScenes;

    [Header("UI Scene Groups")]
    [SerializeField] private UISceneGroup[] uiSceneGroups;

    private string currentLoadedUI = "";
    private string pageManagerSceneName;
    private UIThemeManager uiThemeManager;

    private readonly List<string> currentAdditionalScenes = new List<string>();

    private void Awake()
    {
        pageManagerSceneName = gameObject.scene.name;

        //Check later necessary to put here or not
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

        // Load all permanent scenes
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

        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }
    }

    private IEnumerator UnloadSceneIfLoaded(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            yield break;

        if (SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(sceneName); 
        }
    }

    public void ChangeUI(string targetSceneName)
    {
        StartCoroutine(SwitchUIScene(targetSceneName));
    }

    private IEnumerator SwitchUIScene(string newSceneName)
    {
        if (currentLoadedUI == newSceneName)
            yield break;

        UISceneGroup targetGroup = GetUISceneGroup(newSceneName);

        // 1. Load the new UI first
        yield return StartCoroutine(LoadSceneIfNotLoaded(newSceneName));

        // 2. Load additional scenes
        if (targetGroup != null && targetGroup.additionalScenes != null)
        {
            foreach (string sceneName in targetGroup.additionalScenes)
            {
                if (!currentAdditionalScenes.Contains(sceneName))
                {
                    yield return StartCoroutine(LoadSceneIfNotLoaded(sceneName));
                }
            }
        }

        // 3. Unload old UI
        if (!string.IsNullOrEmpty(currentLoadedUI))
        {
            yield return StartCoroutine(UnloadSceneIfLoaded(currentLoadedUI));
        }

        // 4. Unload old additional scenes
        foreach (string sceneName in currentAdditionalScenes)
        {
            if (targetGroup == null ||
                targetGroup.additionalScenes == null ||
                System.Array.IndexOf(targetGroup.additionalScenes, sceneName) < 0)
            {
                yield return StartCoroutine(UnloadSceneIfLoaded(sceneName));
            }
        }

        // 5. Update tracking
        currentLoadedUI = newSceneName;
        currentAdditionalScenes.Clear();

        if (targetGroup != null && targetGroup.additionalScenes != null)
        {
            currentAdditionalScenes.AddRange(targetGroup.additionalScenes);
        }

        // 6. Set active scene
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(newSceneName));

        Debug.Log($"Successfully switched to: {newSceneName}");

        // 6. Apply UI theme to all buttons
        uiThemeManager?.ApplyThemeToAllButtons();
        Debug.Log($"Successfully applied theme to all buttons in: {newSceneName}");
    }

    private UISceneGroup GetUISceneGroup(string uiSceneName)
    {
        foreach (UISceneGroup group in uiSceneGroups)
        {
            if (group.uiSceneName == uiSceneName)
            {
                return group;
            }
        }

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
        if (array == null)
            return false;

        foreach (string item in array)
        {
            if (item == value)
                return true;
        }

        return false;
    }

    public void QuitApplication()
    {
        Debug.Log("Exiting Game...");
        Application.Quit();
    }
}