using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  HUD CONTROLLER
//  Update the HUD elements based on GameManager's events.
//
//  Calendar:
//    DateText always starts from TODAY's real system date
// ─────────────────────────────────────────────────────────────────

public class HUDController : MonoBehaviour
{
    [Header("Accident Rate")]
    [SerializeField] private TextMeshProUGUI accidentText;

    [Header("Happiness")]
    [SerializeField] private Image happinessBar;  // Image Type → Filled
    [SerializeField] private TextMeshProUGUI happinessPct;  // "75 %"

    [Header("Calendar")]
    [SerializeField] private TextMeshProUGUI dateText;      // "28/5/2026"
    [SerializeField] private TextMeshProUGUI dayText;       // "Day 58/90"

    [Header("Capital")]
    [SerializeField] private TextMeshProUGUI capitalText;   // "RM1000"

    // Captured once in Awake — always "today" when the game launches.
    private System.DateTime _calendarStart;

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        // Use the real system date as day-0 of the in-game calendar.
        _calendarStart = System.DateTime.Today;
    }

    private void OnEnable()
    {
        // Subscribe early; GameManager defers BroadcastState by one
        // frame so we are guaranteed to receive the initial values.
        SubscribeToGameManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromGameManager();
    }

    // ─────────────────────────────────────────
    //  SUBSCRIBE / UNSUBSCRIBE
    // ─────────────────────────────────────────

    private void SubscribeToGameManager()
    {
        if (GameManager.Instance == null)
        {
            // GameManager Awake hasn't run yet — retry next frame.
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

    // ─────────────────────────────────────────
    //  EVENT HANDLERS
    // ─────────────────────────────────────────

    // "RM1000"
    private void HandleCapital(float capital)
    {
        if (capitalText != null)
            capitalText.text = $"RM{Mathf.RoundToInt(capital)}";
    }

    // Progress bar fill + "75 %"
    private void HandleHappiness(float happiness)
    {
        if (happinessBar != null)
            happinessBar.fillAmount = happiness / 100f;

        if (happinessPct != null)
            happinessPct.text = $"{Mathf.RoundToInt(happiness)} %";
    }

    // Accident rate counter
    private void HandleAccidentRate(int rate)
    {
        if (accidentText != null)
            accidentText.text = rate.ToString();
    }

    // "Day 58/90"  +  "28/5/2026" (today + days elapsed)
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