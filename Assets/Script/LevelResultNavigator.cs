using System.Collections;
using UnityEngine;

// Scene navigation to the level-results screen is triggered here in response to GameManager's victory and game-over events.

public class LevelResultNavigator : MonoBehaviour
{
    [SceneName] public string levelResultSceneName = "LevelResult";
    private GameManager _subscribedGameManager;

    private void OnEnable()
    {
        StartCoroutine(TrackGameManager());
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureNavigatorExists()
    {
        if (FindFirstObjectByType<LevelResultNavigator>() != null) return;

        GameObject navigator = new GameObject("LevelResultNavigator");
        DontDestroyOnLoad(navigator);
        navigator.AddComponent<LevelResultNavigator>();
    }

    private IEnumerator TrackGameManager()
    {
        while (enabled)
        {
            GameManager current = GameManager.Instance;
            if (current != _subscribedGameManager)
            {
                Unsubscribe();
                _subscribedGameManager = current;

                if (_subscribedGameManager != null)
                {
                    _subscribedGameManager.OnVictory.AddListener(OpenLevelResult);
                    _subscribedGameManager.OnGameOver.AddListener(OpenLevelResult);
                }
            }

            yield return null;
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (_subscribedGameManager == null) return;
        _subscribedGameManager.OnVictory.RemoveListener(OpenLevelResult);
        _subscribedGameManager.OnGameOver.RemoveListener(OpenLevelResult);
        _subscribedGameManager = null;
    }

    private void OpenLevelResult()
    {
        Time.timeScale = 1f;

        if (PageManager.Instance != null)
            PageManager.Instance.ChangeUI(levelResultSceneName);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(levelResultSceneName);
    }
}
