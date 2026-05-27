using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  HUD CONTROLLER
//
//  Attach this script to the TopHUD GameObject (or any persistent
//  object in the scene).
//
//  It subscribes to GameManager events and pushes formatted text /
//  values to every HUD element — NO Inspector event wiring needed
//  for the display fields.
//
//  Hierarchy wiring (drag in Inspector):
//    accidentText   → AccidentRate / AccidentText  (TMP)
//    happinessBar   → Happiness / ProgressBar1 / Image  (Image, filled)
//    happinessPct   → Happiness / TextPanel / Percentage  (TMP)
//    dateText       → Calendar / DateText  (TMP)
//    dayText        → Calendar / DayText   (TMP)
//    capitalText    → Capital / CapitalText  (TMP)
// ─────────────────────────────────────────────────────────────────

public class HUDController : MonoBehaviour
{
    [Header("Accident Rate")]
    [SerializeField] private TextMeshProUGUI accidentText;

    [Header("Happiness")]
    [SerializeField] private Image           happinessBar;   // fillAmount 0–1
    [SerializeField] private TextMeshProUGUI happinessPct;   // "75 %"

    [Header("Calendar")]
    [SerializeField] private TextMeshProUGUI dateText;       // "17/5/2026"
    [SerializeField] private TextMeshProUGUI dayText;        // "Day 58/90"

    [Header("Capital")]
    [SerializeField] private TextMeshProUGUI capitalText;    // "RM1000"

    // ── Calendar helpers ──────────────────────
    // We simulate a real start date and advance one day per game day.
    [Header("Calendar Start Date")]
    public int startDay   = 1;
    public int startMonth = 1;
    public int startYear  = 2026;

    private System.DateTime _calendarStart;

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        _calendarStart = new System.DateTime(startYear, startMonth, startDay);
    }

    private void OnEnable()
    {
        // Subscribe as soon as possible (before GameManager.Start fires).
        // GameManager defers BroadcastState by one frame, so we are safe.
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
            // GameManager not yet awake — retry next frame.
            StartCoroutine(RetrySubscribe());
            return;
        }

        GameManager.Instance.OnCapitalChanged      .AddListener(HandleCapital);
        GameManager.Instance.OnHappinessChanged    .AddListener(HandleHappiness);
        GameManager.Instance.OnAccidentRateChanged .AddListener(HandleAccidentRate);
        GameManager.Instance.OnDayChanged          .AddListener(HandleDay);
    }

    private System.Collections.IEnumerator RetrySubscribe()
    {
        yield return null; // wait one frame
        SubscribeToGameManager();
    }

    private void UnsubscribeFromGameManager()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.OnCapitalChanged      .RemoveListener(HandleCapital);
        GameManager.Instance.OnHappinessChanged    .RemoveListener(HandleHappiness);
        GameManager.Instance.OnAccidentRateChanged .RemoveListener(HandleAccidentRate);
        GameManager.Instance.OnDayChanged          .RemoveListener(HandleDay);
    }

    // ─────────────────────────────────────────
    //  HANDLERS
    // ─────────────────────────────────────────

    /// <summary>Updates CapitalText — formats float as "RM1000".</summary>
    private void HandleCapital(float capital)
    {
        if (capitalText != null)
            capitalText.text = $"RM{Mathf.RoundToInt(capital)}";
    }

    /// <summary>Updates happiness bar fill and percentage label.</summary>
    private void HandleHappiness(float happiness)
    {
        float t = happiness / 100f;

        if (happinessBar != null)
            happinessBar.fillAmount = t;

        if (happinessPct != null)
            happinessPct.text = $"{Mathf.RoundToInt(happiness)} %";
    }

    /// <summary>Updates the accident rate counter.</summary>
    private void HandleAccidentRate(int rate)
    {
        if (accidentText != null)
            accidentText.text = rate.ToString();
    }

    /// <summary>
    /// Updates DayText ("Day 58/90") and DateText ("17/5/2026").
    /// Receives (daysPassed, totalDays) from GameManager.OnDayChanged.
    /// </summary>
    private void HandleDay(int daysPassed, int totalDays)
    {
        // Day counter label
        if (dayText != null)
            dayText.text = $"Day {daysPassed}/{totalDays}";

        // Real-calendar date (advances from startDate)
        if (dateText != null)
        {
            System.DateTime current = _calendarStart.AddDays(daysPassed);
            dateText.text = $"{current.Day}/{current.Month}/{current.Year}";
        }
    }
}
