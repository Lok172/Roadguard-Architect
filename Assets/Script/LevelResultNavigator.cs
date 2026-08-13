using System.Collections;
using UnityEngine;

// Scene navigation to the level-results screen is triggered here in response to GameManager's victory and game-over events.

public class LevelResultNavigator : MonoBehaviour
{
    [SceneName] public string levelResultSceneName = "LevelResult";

    private void OnEnable()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.OnVictory.AddListener(OpenLevelResult);
        GameManager.Instance.OnGameOver.AddListener(OpenLevelResult);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnVictory.RemoveListener(OpenLevelResult);
            GameManager.Instance.OnGameOver.RemoveListener(OpenLevelResult);
        }
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
