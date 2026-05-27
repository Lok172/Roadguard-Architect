using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ─────────────────────────────────────────────────────────────────
//  GAME MANAGER
//
//  Owns all global resources:
//    Capital, TaxRevenue, AccidentRate, Happiness, Calendar (days)
//
//  Other scripts talk to it via GameManager.Instance (singleton).
// ─────────────────────────────────────────────────────────────────

public class GameManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────
    public static GameManager Instance { get; private set; }

    // ── Level Config ──────────────────────────
    [Header("Level Configuration")]
    public int currentLevel = 1;

    [System.Serializable]
    public struct LevelConfig
    {
        public int   level;
        public float startCapitalRM;
        public int   startAccidentRate;
    }

    public LevelConfig[] levelConfigs = new LevelConfig[]
    {
        new LevelConfig { level = 1, startCapitalRM = 1000f,  startAccidentRate = 10 },
        new LevelConfig { level = 2, startCapitalRM = 2500f,  startAccidentRate = 15 },
        new LevelConfig { level = 3, startCapitalRM = 3500f,  startAccidentRate = 25 }
    };

    // ── Game Rules ────────────────────────────
    [Header("Game Rules")]
    [Tooltip("Real seconds per in-game day (GDD: 1 day = 2 seconds)")]
    public float secondsPerDay = 2f;

    [Tooltip("Total in-game days before game ends")]
    public int   totalDays    = 90;

    [Tooltip("Accident rate threshold to trigger Safety Multiplier on tax revenue")]
    public int   safetyThreshold = 3;

    [Tooltip("Multiplier applied to tax revenue when accident rate < safetyThreshold")]
    public float safetyMultiplier = 1.5f;

    [Tooltip("Base tax revenue collected per day (scales with happiness)")]
    public float baseTaxPerDay = 50f;

    // ── Runtime State ─────────────────────────
    [Header("Runtime State (read-only in Inspector)")]
    [SerializeField] private float _capital;
    [SerializeField] private float _happiness;       // 0–100
    [SerializeField] private int   _accidentRate;    // city-wide sum of all tile contributions
    [SerializeField] private int   _daysPassed;
    [SerializeField] private bool  _gameRunning;

    public float Capital      => _capital;
    public float Happiness    => _happiness;
    public int   AccidentRate => _accidentRate;
    public int   DaysPassed   => _daysPassed;
    public bool  GameRunning  => _gameRunning;

    // ── Tile Registry ─────────────────────────
    // All RoadTiles in the city register themselves here at Start.
    private readonly List<RoadTile> _allTiles = new List<RoadTile>();

    // ── Unity Events (wire up UI in Inspector) ─
    [Header("Events — wire to UI in Inspector")]
    public UnityEvent<float> OnCapitalChanged;      // float = new capital
    public UnityEvent<float> OnHappinessChanged;    // float = new happiness (0-100)
    public UnityEvent<int>   OnAccidentRateChanged; // int   = new rate
    public UnityEvent<int>   OnDayChanged;          // int   = new day number
    public UnityEvent        OnGameOver;
    public UnityEvent        OnVictory;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        InitLevel(currentLevel);
    }

    // ─────────────────────────────────────────
    //  INITIALISATION
    // ─────────────────────────────────────────

    public void InitLevel(int level)
    {
        LevelConfig cfg = GetLevelConfig(level);

        _capital      = cfg.startCapitalRM;
        _accidentRate = cfg.startAccidentRate;
        _happiness    = 100f;   // always start at full happiness
        _daysPassed   = 0;
        _gameRunning  = true;

        BroadcastState();
        StartCoroutine(DayTickRoutine());

        Debug.Log($"[GameManager] Level {level} started. " +
                  $"Capital=RM{_capital} AccidentRate={_accidentRate}");
    }

    // ─────────────────────────────────────────
    //  TILE REGISTRY
    // ─────────────────────────────────────────

    /// <summary>
    /// Called by each RoadTile in its Start().
    /// Subscribes to tile events so GameManager reacts to changes.
    /// </summary>
    public void RegisterTile(RoadTile tile)
    {
        if (_allTiles.Contains(tile)) return;
        _allTiles.Add(tile);
        tile.OnContributionChanged += HandleTileContributionChanged;
    }

    public void UnregisterTile(RoadTile tile)
    {
        _allTiles.Remove(tile);
        tile.OnContributionChanged -= HandleTileContributionChanged;
    }

    private void HandleTileContributionChanged(RoadTile tile, int oldVal, int newVal)
    {
        // Recalculate total from all tiles (safe, avoids drift)
        RecalculateAccidentRate();
    }

    private void RecalculateAccidentRate()
    {
        int total = 0;
        foreach (RoadTile t in _allTiles)
            total += t.currentAccidentContribution;

        int prev = _accidentRate;
        _accidentRate = total;

        if (prev != _accidentRate)
        {
            OnAccidentRateChanged?.Invoke(_accidentRate);
            CheckVictory();
        }
    }

    // ─────────────────────────────────────────
    //  DEVICE PLACEMENT  (called by PlacementManager)
    // ─────────────────────────────────────────

    /// <summary>
    /// Entry point for placing a device. PlacementManager calls this
    /// after the player drops a device onto a tile.
    /// </summary>
    public PlacementResult TryPlaceDevice(
        RoadTile          tile,
        TrafficDeviceType device,
        GameObject        deviceObject)
    {
        PlacementResult result = tile.PlaceDevice(
            device, deviceObject, _capital,
            out float happinessDelta,
            out float costSpent);

        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
        {
            return result; // caller shows error feedback
        }

        // Deduct cost
        ModifyCapital(-costSpent);

        // Apply happiness change (clamped 0–100)
        ModifyHappiness(happinessDelta);

        return result;
    }

    // ─────────────────────────────────────────
    //  ECONOMY
    // ─────────────────────────────────────────

    public void ModifyCapital(float delta)
    {
        _capital = Mathf.Max(0f, _capital + delta);
        OnCapitalChanged?.Invoke(_capital);
    }

    public void ModifyHappiness(float delta)
    {
        float prev   = _happiness;
        _happiness   = Mathf.Clamp(_happiness + delta, 0f, 100f);

        if (!Mathf.Approximately(prev, _happiness))
            OnHappinessChanged?.Invoke(_happiness);

        if (_happiness <= 0f)
            TriggerGameOver();
    }

    private float CalculateDailyTaxRevenue()
    {
        // Tax scales with happiness (0–100 → 0–1 factor)
        float happinessFactor = _happiness / 100f;
        float tax             = baseTaxPerDay * happinessFactor;

        // Safety multiplier if accident rate is low
        if (_accidentRate < safetyThreshold)
            tax *= safetyMultiplier;

        return tax;
    }

    // ─────────────────────────────────────────
    //  DAY TICK
    // ─────────────────────────────────────────

    private IEnumerator DayTickRoutine()
    {
        while (_gameRunning)
        {
            yield return new WaitForSeconds(secondsPerDay);

            if (!_gameRunning) break;

            _daysPassed++;
            OnDayChanged?.Invoke(_daysPassed);

            // Collect tax revenue
            float tax = CalculateDailyTaxRevenue();
            ModifyCapital(tax);

            Debug.Log($"[GameManager] Day {_daysPassed}: " +
                      $"+RM{tax:F0} tax | Capital=RM{_capital:F0} " +
                      $"Happiness={_happiness:F0} AccidentRate={_accidentRate}");

            // Check termination
            if (_daysPassed >= totalDays)
                TriggerGameOver();
        }
    }

    // ─────────────────────────────────────────
    //  WIN / LOSE
    // ─────────────────────────────────────────

    private void CheckVictory()
    {
        if (_accidentRate == 0 && _happiness > 0f)
        {
            _gameRunning = false;
            Debug.Log("[GameManager] VICTORY — Accident rate = 0!");
            OnVictory?.Invoke();
        }
    }

    private void TriggerGameOver()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        StopAllCoroutines();
        Debug.Log("[GameManager] GAME OVER");
        OnGameOver?.Invoke();
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    private LevelConfig GetLevelConfig(int level)
    {
        foreach (LevelConfig cfg in levelConfigs)
            if (cfg.level == level) return cfg;

        Debug.LogWarning($"[GameManager] No config for level {level}, using level 1.");
        return levelConfigs[0];
    }

    private void BroadcastState()
    {
        OnCapitalChanged?.Invoke(_capital);
        OnHappinessChanged?.Invoke(_happiness);
        OnAccidentRateChanged?.Invoke(_accidentRate);
        OnDayChanged?.Invoke(_daysPassed);
    }

    // ─────────────────────────────────────────
    //  SCORE  (called at level end)
    // ─────────────────────────────────────────

    /// <summary>
    /// GDD formula: based on Device Effectiveness, final AccidentRate,
    /// Happiness, and total budget spent.
    /// Returns a score 1–10000.
    /// </summary>
    public int CalculateFinalScore(float totalBudgetSpent)
    {
        float accidentScore  = Mathf.Max(0, 100 - _accidentRate * 4f); // lower rate = higher score
        float happinessScore = _happiness;                              // 0–100
        float budgetScore    = Mathf.Clamp(100f - (totalBudgetSpent / 100f), 0f, 100f);

        float raw = (accidentScore * 0.5f) +
                    (happinessScore * 0.3f) +
                    (budgetScore    * 0.2f);

        return Mathf.Clamp(Mathf.RoundToInt(raw * 100f), 1, 10000);
    }
}
