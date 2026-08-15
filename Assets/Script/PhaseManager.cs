using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// MVVM view binding for the shared phase panel. Attach this component to an
/// active Canvas child (not the panel itself), then assign the panel and text.
/// </summary>
public class PhaseManager : MonoBehaviour
{
    [Header("View References")]
    [FormerlySerializedAs("planningPanel")]
    [SerializeField] private GameObject phasePanel;
    [FormerlySerializedAs("confirmLayoutButton")]
    [SerializeField] private Button simulateButton;
    [SerializeField] private TMP_Text confirmButtonLabel;
    [SerializeField] private TMP_Text phaseStatusLabel;
    [SerializeField] private TMP_Text countdownLabel;
    [SerializeField] private TMP_Text instructionLabel;

    [Header("Phase Colours")]
    [SerializeField] private Color planningColour = new Color(1f, 0.74f, 0.18f); // amber
    [SerializeField] private Color executionColour = new Color(0.30f, 0.90f, 0.55f); // green

    private PhaseViewModel _vm;
    private Coroutine _countdownRoutine;

    private void Awake()
    {
        _vm = new PhaseViewModel();
        _vm.VisibilityChanged += ApplyVisibility;

        ConfigurePanelRaycasts();
        ApplyVisibility(false);
    }

    private IEnumerator Start()
    {
        // Wiring the button here — not in Awake() — guarantees every other
        // script's Awake() (including PauseMenuController's, which may add a
        // RestartLevel listener to this same button by mistake) has already
        // run, so RemoveAllListeners() here is the one that wins.
        if (simulateButton != null)
        {
            simulateButton.onClick.RemoveAllListeners();
            simulateButton.onClick.AddListener(_vm.StartExecution);
        }

        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.OnPlanningPhaseStarted.AddListener(_vm.Show);
        GameManager.Instance.OnExecutionPhaseStarted.AddListener(ShowExecutionPhase);

        // Handles GameManager being initialized before this view subscribes.
        if (!GameManager.Instance.PhaseFlowStarted)
            yield break;

        if (GameManager.Instance.PlanningPhaseActive)
            _vm.Show();
        else if (GameManager.Instance.GameRunning)
            ShowExecutionPhase();
    }

    private void OnDestroy()
    {
        if (simulateButton != null)
            simulateButton.onClick.RemoveListener(_vm.StartExecution);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanningPhaseStarted.RemoveListener(_vm.Show);
            GameManager.Instance.OnExecutionPhaseStarted.RemoveListener(ShowExecutionPhase);
        }

        if (_vm != null)
            _vm.VisibilityChanged -= ApplyVisibility;
    }

    private void ApplyVisibility(bool visible)
    {
        if (phasePanel != null)
            phasePanel.SetActive(visible);

        if (confirmButtonLabel != null)
            confirmButtonLabel.text = "Simulate";

        if (phaseStatusLabel != null)
        {
            phaseStatusLabel.text = "Planning Phase";
            phaseStatusLabel.color = planningColour;
        }

        if (instructionLabel != null)
            instructionLabel.text = "Place traffic infrastructures before simulation.";

        if (countdownLabel != null)
            countdownLabel.gameObject.SetActive(false);

        if (simulateButton != null)
        {
            simulateButton.gameObject.SetActive(true);
            simulateButton.interactable = true;
        }
    }

    private void ShowExecutionPhase()
    {
        if (phasePanel != null)
            phasePanel.SetActive(true);

        if (phaseStatusLabel != null)
        {
            phaseStatusLabel.text = "Execution Phase";
            phaseStatusLabel.color = executionColour;
        }

        if (instructionLabel != null)
            instructionLabel.text = "Monitor traffic flow and place infrastructures.";

        if (confirmButtonLabel != null)
            confirmButtonLabel.text = "Simulating";

        if (simulateButton != null)
        {
            // Keep the button visible as feedback, but prevent a second start.
            simulateButton.gameObject.SetActive(true);
            simulateButton.interactable = false;
        }

        if (!GameManager.Instance.SimulationStarted && _countdownRoutine == null)
            _countdownRoutine = StartCoroutine(CountdownThenStart());
    }

    private IEnumerator CountdownThenStart()
    {
        LevelAudioManager.Instance?.PlayGameStart();
        LevelAudioManager.Instance?.PlayCarDriving();

        if (countdownLabel != null)
            countdownLabel.gameObject.SetActive(true);

        for (int seconds = 3; seconds >= 1; seconds--)
        {
            if (countdownLabel != null)
                countdownLabel.text = seconds.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownLabel != null)
            countdownLabel.gameObject.SetActive(false);

        GameManager.Instance?.StartSimulationAfterCountdown();
        _countdownRoutine = null;
    }

    // The panel is informational except for the Simulate button. Let Settings
    // and road clicks pass through its background and text.
    private void ConfigurePanelRaycasts()
    {
        if (phasePanel == null) return;

        foreach (Graphic graphic in phasePanel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = graphic.gameObject == simulateButton?.gameObject;
    }
}