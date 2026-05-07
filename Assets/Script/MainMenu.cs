using UnityEngine;
using UnityEngine.SceneManagement;
public class MainMenu : MonoBehaviour
{
    [SerializeField] private string levelSelectSceneName = "LevelSelect";
    [SerializeField] private string SafetyRecordSceneName = "SafetyRecord";
    [SerializeField] private string SettingSceneName = "Setting";
    [SerializeField] private string MainMenuSceneName = "MainMenu";

    [Header("Environment Scenes")]
    [SerializeField] private string CitySceneName = "City";
    [SerializeField] private string BackgroundSceneName = "Background";



    private float currentAngle = 0f;
    private int direction = 1;

    private string currentUIScene = "";

    private void Start()
    {
        LoadEnvironmentIfNotLoaded(CitySceneName);
        LoadEnvironmentIfNotLoaded(BackgroundSceneName);

        currentUIScene = MainMenuSceneName;
        CleanupExtraScenes();
    }

 
    private void LoadEnvironmentIfNotLoaded(string name)
    {
        if (!SceneManager.GetSceneByName(name).isLoaded)
        {
            SceneManager.LoadScene(name, LoadSceneMode.Additive);
        }
    }

    private void CleanupExtraScenes()
    {
        Scene levelSelect = SceneManager.GetSceneByName(levelSelectSceneName);
        if (levelSelect.isLoaded)
        {
            SceneManager.UnloadSceneAsync(levelSelectSceneName);
        }

        if (SceneManager.GetSceneByName(SettingSceneName).isLoaded)
            SceneManager.UnloadSceneAsync(SettingSceneName);
    }

    public void LoadUIScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(currentUIScene) && currentUIScene != sceneName)
        {
            SceneManager.UnloadSceneAsync(currentUIScene);
        }
        currentUIScene = sceneName;
        SceneManager.LoadScene(currentUIScene, LoadSceneMode.Additive);
        
    }

    public void StartGame()
    {
        Debug.Log("Start button clicked");
        LoadUIScene(levelSelectSceneName);
        Debug.Log("Start button Whats wrong");
    }

    public void SafetyRecordChangeScene()
    {
        Debug.Log("SafetyRecord button clicked");
        LoadUIScene(SafetyRecordSceneName);
    }
    public void SettingsChangeScene()
    {
        Debug.Log("Settings button clicked");
        LoadUIScene(SettingSceneName);
    }

    public void EndGame() {
        Debug.Log("Exit button clicked");
        Application.Quit();
    }
}
