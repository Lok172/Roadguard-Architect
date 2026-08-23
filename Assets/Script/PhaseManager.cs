using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using UnityEngine.Events;

/// <summary>
/// This script manages the shared phase panel view — planning and execution phase
/// visibility, the simulate button and its icon/label state, the pre-simulation
/// countdown, and the Victory/Lost result display with its slide-in animation.
/// </summary>
public class PhaseManager : MonoBehaviour
{
    /// <summary>
    /// Current active PhaseManager, mirroring GameManager's singleton pattern so
    /// other scripts (e.g. LevelResultNavigator) can track it across scene loads.
    /// </summary>
    public static PhaseManager Instance { get; private set; }

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

    [Header("Result Animation")]
    [Tooltip("How far above its normal position the Victory/Lost text starts before sliding down.")]
    [SerializeField] private float resultTextStartOffsetY = 300f;
    [Tooltip("How long the slide-down animation takes, in seconds.")]
    [SerializeField] private float resultTextSlideDuration = 0.5f;
    [Tooltip("Total time the result text is held on screen before OnResultSequenceComplete fires. Must be >= Slide Duration.")]
    [SerializeField] private float resultDisplayDuration = 3f;
    [Tooltip("Eases the slide-down motion. Default goes from the start offset (0) to the resting position (1).")]
    [SerializeField] private AnimationCurve resultTextSlideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Events")]
    [Tooltip("Fires once the Victory/Lost text has finished sliding in and has been held for Result Display Duration. Hook your Level Results scene transition to THIS event instead of GameManager.OnVictory/OnGameOver directly, so the player sees the result text first.")]
    public UnityEvent OnResultSequenceComplete;

    private PhaseViewModel _vm;
    private Coroutine _countdownRoutine;
    private Coroutine _resultRoutine;

    private void Awake()
    {
        Instance = this;

        _vm = new PhaseViewModel();
        _vm.VisibilityChanged += ApplyVisibility;

        ConfigurePanelRaycasts();
        ApplyVisibility(false);
    }

    private IEnumerator Start()
    {
      
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
        GameManager.Instance.OnVictory.AddListener(ShowVictoryResult);
        GameManager.Instance.OnGameOver.AddListener(ShowGameOverResult);

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
        if (Instance == this)
            Instance = null;

        if (simulateButton != null)
            simulateButton.onClick.RemoveListener(_vm.StartExecution);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlanningPhaseStarted.RemoveListener(_vm.Show);
            GameManager.Instance.OnPlanningPhaseReady.RemoveListener(EnablePlanningInteraction);
            GameManager.Instance.OnExecutionPhaseStarted.RemoveListener(ShowExecutionPhase);
            GameManager.Instance.OnVictory.RemoveListener(ShowVictoryResult);
            GameManager.Instance.OnGameOver.RemoveListener(ShowGameOverResult);
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

            RectTransform labelRect = confirmButtonLabel.rectTransform;
            Vector2 offsetMax = labelRect.offsetMax;
            offsetMax.x = 66f; 
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

    /// <summary>Called via GameManager.OnVictory when the level is cleared.</summary>
    private void ShowVictoryResult()
    {
        ShowResultText("Victory!");
    }

    /// <summary>Called via GameManager.OnGameOver when the level is lost.</summary>
    private void ShowGameOverResult()
    {
        ShowResultText("Lost!");
    }

    private void ShowResultText(string text)
    {
        // Stop any in-flight countdown so it can't overwrite the result text.
        if (_countdownRoutine != null)
        {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }

        // Stop any previous result animation (e.g. rapid re-trigger) before starting a new one.
        if (_resultRoutine != null)
        {
            StopCoroutine(_resultRoutine);
            _resultRoutine = null;
        }

        if (phasePanel != null)
            phasePanel.SetActive(true);

        if (simulateButton != null)
            simulateButton.interactable = false;

        _resultRoutine = StartCoroutine(AnimateResultText(text));
    }

    /// <summary>
    /// Slides the result text down from above its resting position, holds it for
    /// Result Display Duration (real time — Time.timeScale is 0 at this point), then
    /// fires OnResultSequenceComplete. Hook the Level Results transition to that event
    /// rather than GameManager.OnVictory/OnGameOver so the player sees this first.
    /// </summary>
    private IEnumerator AnimateResultText(string text)
    {
        if (countdownLabel != null)
        {
            countdownLabel.gameObject.SetActive(true);
            countdownLabel.text = text;

            RectTransform rect = countdownLabel.rectTransform;

            // Capture the CURRENT centred resting position right before overriding it, instead
            // of a value cached once in Awake(). Awake() runs before layout (and before things
            // like ShowExecutionPhase's confirmButtonLabel resize) can shift sibling positions,
            // so that snapshot could go stale — which is why Victory!/Lost were rendering off to
            // the side while the countdown numbers (which never touch anchoredPosition at all)
            // rendered correctly.
            Vector2 restingPos = rect.anchoredPosition;
            Vector2 startPos = restingPos + new Vector2(0f, resultTextStartOffsetY);
            rect.anchoredPosition = startPos;

            float elapsed = 0f;
            while (elapsed < resultTextSlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalised = resultTextSlideDuration > 0f ? Mathf.Clamp01(elapsed / resultTextSlideDuration) : 1f;
                float eased = resultTextSlideCurve.Evaluate(normalised);
                rect.anchoredPosition = Vector2.LerpUnclamped(startPos, restingPos, eased);
                yield return null;
            }

            rect.anchoredPosition = restingPos;
        }

        float remainingHold = resultDisplayDuration - resultTextSlideDuration;
        if (remainingHold > 0f)
            yield return new WaitForSecondsRealtime(remainingHold);

        _resultRoutine = null;
        OnResultSequenceComplete?.Invoke();
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