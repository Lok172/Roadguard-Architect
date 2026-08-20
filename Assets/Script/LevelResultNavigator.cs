using System.Collections;
using UnityEngine;

// Scene navigation to the level-results screen is triggered here in response to GameManager's victory and game-over events.

public class LevelResultNavigator : MonoBehaviour
{
    [SceneName] public string levelResultSceneName = "LevelResult";

    // Was tracking GameManager and listening to OnVictory/OnGameOver directly, which
    // fired the scene change the instant the level ended — before the player ever saw
    // the Victory/Lost text. Now tracks PhaseManager instead and waits for
    // OnResultSequenceComplete, which only fires after PhaseManager's slide-in
    // animation has played and held on screen (see PhaseManager.ShowResultText).
    private PhaseManager _subscribedPhaseManager;

    private void OnEnable()
    {
        StartCoroutine(TrackPhaseManager());
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureNavigatorExists()
    {
        if (FindFirstObjectByType<LevelResultNavigator>() != null) return;

        GameObject navigator = new GameObject("LevelResultNavigator");
        DontDestroyOnLoad(navigator);
        navigator.AddComponent<LevelResultNavigator>();
    }

    private IEnumerator TrackPhaseManager()
    {
        while (enabled)
        {
            PhaseManager current = PhaseManager.Instance;
            if (current != _subscribedPhaseManager)
            {
                Unsubscribe();
                _subscribedPhaseManager = current;

                if (_subscribedPhaseManager != null)
                    _subscribedPhaseManager.OnResultSequenceComplete.AddListener(OpenLevelResult);
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
        if (_subscribedPhaseManager == null) return;
        _subscribedPhaseManager.OnResultSequenceComplete.RemoveListener(OpenLevelResult);
        _subscribedPhaseManager = null;
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