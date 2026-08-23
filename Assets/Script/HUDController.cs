using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// This script is used to manage the HUD elements — Capital, Happiness, Accident Rate, and the
// calendar — based on GameManager events. The happiness bar shows normalColour at or above
// warningThreshold and warningColour below it. The calendar starts from today's real system date.
public class HUDController : MonoBehaviour
{
    [Header("Accident Rate")]
    [SerializeField] private TextMeshProUGUI accidentText;

    [Header("Happiness")]
    [Tooltip("The Image component used as the fill bar (Image Type → Filled).")]
    [SerializeField] private Image happinessBar;

    [Tooltip("The percentage label, e.g. '75 %'.")]
    [SerializeField] private TextMeshProUGUI happinessPct;

    [Header("Happiness Bar Colour")]
    [Tooltip("Bar colour when happiness is AT or ABOVE the warning threshold.")]
    public Color normalColour = new Color(0.20f, 0.85f, 0.35f, 1f);   // Green

    [Tooltip("Bar colour when happiness drops BELOW the warning threshold.")]
    public Color warningColour = new Color(1.00f, 0.60f, 0.00f, 1f);   // Orange

    [Tooltip("Happiness percentage below which the bar turns orange (0–100).")]
    [Range(0f, 100f)]
    public float warningThreshold = 50f;

    [Header("Calendar")]
    [SerializeField] private TextMeshProUGUI dateText;      // "28/5/2026"
    [SerializeField] private TextMeshProUGUI dayText;       // "Day 58/90"

    [Header("Capital")]
    [SerializeField] private TextMeshProUGUI capitalText;   // "RM1000"

    // Captured once in Awake — always "today" when the game launches.
    private System.DateTime _calendarStart;

    private void Awake()
    {
        _calendarStart = System.DateTime.Today;
    }

    private void OnEnable()
    {
        SubscribeToGameManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromGameManager();
    }

    private void SubscribeToGameManager()
    {
        if (GameManager.Instance == null)
        {
            StartCoroutine(RetrySubscribe());
            return;
        }

        GameManager.Instance.OnCapitalChanged.AddListener(HandleCapital);
        GameManager.Instance.OnHappinessChanged.AddListener(HandleHappiness);
        GameManager.Instance.OnAccidentRateChanged.AddListener(HandleAccidentRate);
        GameManager.Instance.OnDayChanged.AddListener(HandleDay);
    }

    private IEnumerator RetrySubscribe()
    {
        yield return null;
        SubscribeToGameManager();
    }

    private void UnsubscribeFromGameManager()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.OnCapitalChanged.RemoveListener(HandleCapital);
        GameManager.Instance.OnHappinessChanged.RemoveListener(HandleHappiness);
        GameManager.Instance.OnAccidentRateChanged.RemoveListener(HandleAccidentRate);
        GameManager.Instance.OnDayChanged.RemoveListener(HandleDay);
    }

    private void HandleCapital(float capital)
    {
        if (capitalText != null)
            capitalText.text = $"RM{Mathf.RoundToInt(capital)}";
    }

    private void HandleHappiness(float happiness)
    {
        if (happinessBar != null)
        {
            happinessBar.fillAmount = happiness / 100f;
            happinessBar.color = happiness < warningThreshold ? warningColour : normalColour;
        }

        if (happinessPct != null)
            happinessPct.text = $"{Mathf.RoundToInt(happiness)} %";
    }

    private void HandleAccidentRate(int rate)
    {
        if (accidentText != null)
            accidentText.text = rate.ToString();
        Debug.Log($"Accident rate updated: {rate}") ;
    }

    private void HandleDay(int daysPassed, int totalDays)
    {
        if (dayText != null)
            dayText.text = $"Day {daysPassed}/{totalDays}";

        if (dateText != null)
        {
            System.DateTime current = _calendarStart.AddDays(daysPassed);
            dateText.text = $"{current.Day}/{current.Month}/{current.Year}";
        }
    }
}