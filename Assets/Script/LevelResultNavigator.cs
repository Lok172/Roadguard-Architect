using System.Collections;
using UnityEngine;

// This script is used to navigate to the level-results screen once PhaseManager's result
// sequence (the Victory/Lost text slide-in and hold) has finished playing.
public class LevelResultNavigator : MonoBehaviour
{
    [SceneName] public string levelResultSceneName = "LevelResult";

    // Tracks the current PhaseManager and waits for OnResultSequenceComplete, which fires
    // once its slide-in animation has played and held on screen (see
    // PhaseManager.ShowResultText).
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