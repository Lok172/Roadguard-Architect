using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Intro panel for levels that skip Planning Phase and go straight into
/// Execution Phase (currently only Level 3). Lives on the lvUI &amp; manager
/// object: starts inactive, activates itself once GameManager confirms the
/// current level is 3, and on the Start button click deactivates itself and
/// tells GameManager to confirm — GameManager fires OnDirectExecutionConfirmed
/// (which PhaseManager listens for to show the phase panel immediately) and
/// then starts the camera transition.
/// </summary>
public class DirectExecutionIntroPanel : MonoBehaviour
{
    [SerializeField] private GameObject introPanel;
    [SerializeField] private Button startButton;

    private void Awake()
    {
        if (introPanel != null)
            introPanel.SetActive(false);

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnStartClicked);
        }
    }

    private IEnumerator Start()
    {
        // Wait until GameManager has actually initialised the level — currentLevel
        // is only guaranteed correct once GameRunning flips true inside InitLevel().
        while (GameManager.Instance == null || !GameManager.Instance.GameRunning)
            yield return null;

        if (GameManager.Instance.currentLevel == 3 && introPanel != null)
            introPanel.SetActive(true);
    }

    private void OnStartClicked()
    {
        if (introPanel != null)
            introPanel.SetActive(false);

    }
}