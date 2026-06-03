using UnityEngine;

public class KeyHandler : MonoBehaviour
{
    // Change the pause scene later to panel

    [System.Serializable]
    public class SceneList
    {
        [Tooltip("Current Scene")]
        [SceneName]
        public string currentScene;

        [SceneName]
        [Tooltip("Scene to change to")]
        public string sceneChangeTo;
    }

    [Header("References")]
    [SerializeField] private PageManager pageManager;

    [Header("Scene Names")]
    public SceneList[] sceneLists;

    [SceneName]
    [SerializeField] private string pauseScene = "PauseMenu";

    private bool pauseOpened = false;

    private void Start()
    {
        if (pageManager == null)
        {
            pageManager = FindFirstObjectByType<PageManager>();
        }
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        HandleEscape();
    }

    private void HandleEscape()
    {
        string currentScene = pageManager.currentLoadedUI;
        if (currentScene == pauseScene)
        {
            pageManager.ChangeUI(currentScene); // temporary

            Time.timeScale = 1f;

            pauseOpened = false;
        }
        for (int i = 0; i < sceneLists.Length; i++) {
            if (sceneLists[i].currentScene == currentScene) {
                pageManager.ChangeUI(sceneLists[i].sceneChangeTo);
               if (currentScene == "LV1" || currentScene == "LV2" || currentScene == "LV3") {
                    Time.timeScale = 1f;
                    pauseOpened = true;
                }
                return;
            }
        }


    }
}