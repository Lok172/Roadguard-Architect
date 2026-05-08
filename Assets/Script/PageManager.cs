using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class PageManager : MonoBehaviour
{
    [Header("Permanent Scenes")]
    [SerializeField] private string environmentScene = "Environment";
    [SerializeField] private string cityScene = "City";

    [Header("UI Scenes")]
    [SerializeField] private string mainMenuUI = "MainMenu";
    [SerializeField] private string levelSelectUI = "LevelSelect";
    [SerializeField] private string settingsUI = "Settings";
    [SerializeField] private string safetyRecordUI = "SafetyRecord";

    private string currentLoadedUI = "";
    private string pageManagerSceneName;

    private void Awake()
    {
        pageManagerSceneName = gameObject.scene.name;
        DontDestroyOnLoad(this.gameObject);
    }

    private void Start()
    {
        StartCoroutine(InitialBoot());
    }

    private IEnumerator InitialBoot()
    {
        yield return StartCoroutine(LoadEnvironment(environmentScene));
        yield return StartCoroutine(LoadEnvironment(cityScene));

        yield return StartCoroutine(SwitchUIScene(mainMenuUI));

        CleanupStrayScenes();
    }

    private IEnumerator LoadEnvironment(string sceneName)
    {
        // Only load if it's not already there
        if (!SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }
    }

    public void ChangeUI(string targetSceneName)
    {
        StartCoroutine(SwitchUIScene(targetSceneName));
    }

    private IEnumerator SwitchUIScene(string newSceneName)
    {
        if (currentLoadedUI == newSceneName) yield break;

        //Unload the old UI
        if (!string.IsNullOrEmpty(currentLoadedUI))
        {
            if (SceneManager.GetSceneByName(currentLoadedUI).isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(currentLoadedUI);
            }
        }

        //Load the new UI
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(newSceneName, LoadSceneMode.Additive);
        yield return loadOp;

        currentLoadedUI = newSceneName;

        // Set as Active scnee
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(newSceneName));

        Debug.Log($"Successfully switched to: {newSceneName}");
    }

    private void CleanupStrayScenes()
    {
        int sceneCount = SceneManager.sceneCount;

        for (int i = sceneCount - 1; i >= 0; i--)
        {
            Scene s = SceneManager.GetSceneAt(i);

            bool isProtected = s.name == environmentScene ||
                               s.name == cityScene ||
                               s.name == currentLoadedUI ||
                               s.name == pageManagerSceneName;

            if (!isProtected && s.isLoaded)
            {
                Debug.Log("Cleaning up stray editor scene: " + s.name);
                SceneManager.UnloadSceneAsync(s);
            }
        }
    }

    public void QuitApplication()
    {
        Debug.Log("Exiting Game...");
        Application.Quit();
    }
}