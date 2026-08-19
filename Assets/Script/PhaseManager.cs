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
    [SerializeField] private Image simulateButtonIcon;
    [SerializeField] private TMP_Text confirmButtonLabel;
    [SerializeField] private TMP_Text phaseStatusLabel;
    [SerializeField] private TMP_Text countdownLabel;

    [Header("Phase Colours")]
    [SerializeField] private Color planningColour = new Color(1f, 0.74f, 0.18f); // amber
    [SerializeField] private Color executionColour = new Color(0.30f, 0.90f, 0.55f); // green

    [Header("Phase Icons")]
    [Tooltip("Shown on the Simulate button while in the Planning phase.")]
    [SerializeField] private Sprite planningIcon; // play
    [Tooltip("Shown on the Simulate button once the Execution phase starts.")]
    [SerializeField] private Sprite executionIcon; // pause

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
        // Wiring the button here � not in Awake() � guarantees every other
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
        GameManager.Instance.OnPlanningPhaseReady.AddListener(EnablePlanningInteraction);
        GameManager.Instance.OnExecutionPhaseStarted.AddListener(ShowExecutionPhase);

        // Late-subscription safety: GameManager.Start() may already have run
        // (and already invoked the events above) before this view's Start()
        // gets here, depending on script execution order.
        if (GameManager.Instance.PlanningPhaseActive)
        {
            _vm.Show();
            if (GameManager.Instance.PhaseFlowStarted)
                EnablePlanningInteraction();
        }
        else if (GameManager.Instance.PhaseFlowStarted && GameManager.Instance.GameRunning)
        {
            ShowExecutionPhase();
        }
    }

    private void OnDestroy()
    {
        if (simulateButton != null)
            simulateButton.onClick.RemoveListener(_vm.StartExecution);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanningPhaseStarted.RemoveListener(_vm.Show);
            GameManager.Instance.OnPlanningPhaseReady.RemoveListener(EnablePlanningInteraction);
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

        if (countdownLabel != null)
            countdownLabel.gameObject.SetActive(false);

        if (simulateButton != null)
        {
            simulateButton.gameObject.SetActive(true);
            // Disabled until OnPlanningPhaseReady fires (camera reveal done) —
            // the panel/instructions now show before that, so clicking early
            // would otherwise silently no-op inside GameManager.ConfirmLayout.
            simulateButton.interactable = false;
        }

        if (simulateButtonIcon != null && planningIcon != null)
            simulateButtonIcon.sprite = planningIcon;
    }

    /// <summary>Called via GameManager.OnPlanningPhaseReady once the camera reveal finishes.</summary>
    private void EnablePlanningInteraction()
    {
        if (simulateButton != null)
            simulateButton.interactable = true;
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

        if (confirmButtonLabel != null)
        {
            confirmButtonLabel.text = "Simulating";

            // Shrink the label's right margin to -66 so it re-centres against
            // the icon once the button locks into its post-click state.
            RectTransform labelRect = confirmButtonLabel.rectTransform;
            Vector2 offsetMax = labelRect.offsetMax;
            offsetMax.x = 66f; // Inspector "Right" = -offsetMax.x, so this reads as Right = -66
            labelRect.offsetMax = offsetMax;
        }

        if (simulateButton != null)
        {
            // Keep the button visible as feedback, but prevent a second start.
            simulateButton.gameObject.SetActive(true);
            simulateButton.interactable = false;
        }

        if (simulateButtonIcon != null && executionIcon != null)
            simulateButtonIcon.sprite = executionIcon;

        if (!GameManager.Instance.SimulationStarted && _countdownRoutine == null)
            _countdownRoutine = StartCoroutine(CountdownThenStart());
    }

    private IEnumerator CountdownThenStart()
    {
        LevelAudioManager.Instance?.PlayGameStart();
        LevelAudioManager.Instance?.PlayCarDriving();

        if (countdownLabel != null)
            countdownLabel.gameObject.SetActive(true);

        if (countdownLabel != null)
            countdownLabel.text = "Ready?";
        yield return new WaitForSecondsRealtime(1f);

        for (int seconds = 3; seconds >= 1; seconds--)
        {
            if (countdownLabel != null)
                countdownLabel.text = seconds.ToString();
            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownLabel != null)
            countdownLabel.text = "GO!";

        yield return new WaitForSecondsRealtime(0.6f);

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