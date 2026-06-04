using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ─────────────────────────────────────────────────────────────────
//  GAME MANAGER
//
//  Owns global resources: Capital, Happiness, AccidentRate, Calendar.
//
//  CHANGE vs. v1:
//    • TryPlaceDevice now takes a TileCorner parameter and forwards
//      it to RoadTile.PlaceDevice. Required for the new multi-device
//      corner system.
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
        public int level;
        public float startCapitalRM;
        public int startAccidentRate;

        [Range(0, 100)]
        [Tooltip("Starting happiness for this level (0 = miserable, 100 = fully happy).")]
        public float startHappiness;
    }

    public LevelConfig[] levelConfigs = new LevelConfig[]
    {
        new LevelConfig { level = 1, startCapitalRM = 1000f, startAccidentRate = 10, startHappiness = 100f },
        new LevelConfig { level = 2, startCapitalRM = 2500f, startAccidentRate = 15, startHappiness = 80f  },
        new LevelConfig { level = 3, startCapitalRM = 3500f, startAccidentRate = 25, startHappiness = 60f  }
    };

    // ── Game Rules ────────────────────────────
    [Header("Game Rules")]
    public float secondsPerDay = 2f;
    public int totalDays = 90;
    public int safetyThreshold = 3;
    public float safetyMultiplier = 1.5f;
    public float baseTaxPerDay = 50f;

    // ── Runtime State ─────────────────────────
    [Header("Runtime State (read-only in Inspector)")]
    [SerializeField] private float _capital;
    [SerializeField] private float _happiness;
    [SerializeField] private int _accidentRate;
    [SerializeField] private int _daysPassed;
    [SerializeField] private bool _gameRunning;

    public float Capital => _capital;
    public float Happiness => _happiness;
    public int AccidentRate => _accidentRate;
    public int DaysPassed => _daysPassed;
    public int TotalDays => totalDays;
    public bool GameRunning => _gameRunning;

    // ── Tile Registry ─────────────────────────
    private readonly List<RoadTile> _allTiles = new List<RoadTile>();

    // ── Unity Events ──────────────────────────
    [Header("Events — also auto-wired by HUDController")]
    public UnityEvent<float> OnCapitalChanged;
    public UnityEvent<float> OnHappinessChanged;
    public UnityEvent<int> OnAccidentRateChanged;
    public UnityEvent<int, int> OnDayChanged;
    public UnityEvent OnGameOver;
    public UnityEvent OnVictory;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
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
        StopAllCoroutines();

        LevelConfig cfg = GetLevelConfig(level);

        _capital = cfg.startCapitalRM;
        _accidentRate = cfg.startAccidentRate;
        _happiness = Mathf.Clamp(cfg.startHappiness, 0f, 100f);
        _daysPassed = 0;
        _gameRunning = true;

        StartCoroutine(BroadcastNextFrame());
        StartCoroutine(DayTickRoutine());

        Debug.Log($"[GameManager] Level {level} started. " +
                  $"Capital=RM{_capital} AccidentRate={_accidentRate}");
    }

    private IEnumerator BroadcastNextFrame()
    {
        yield return null;
        BroadcastState();
        CheckVictory();
    }

    // ─────────────────────────────────────────
    //  TILE REGISTRY
    // ─────────────────────────────────────────

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
    //  DEVICE PLACEMENT
    // ─────────────────────────────────────────

    /// <summary>
    /// Place a device at a specific corner of a tile. Returns the placement
    /// result; the corner determines rotation and "correct side" check.
    /// </summary>
    public PlacementResult TryPlaceDevice(
        RoadTile tile,
        TrafficDeviceType device,
        TileCorner corner,
        GameObject deviceObject)
    {
        PlacementResult result = tile.PlaceDevice(
            device, corner, deviceObject, _capital,
            out float happinessDelta,
            out float costSpent);

        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
        {
            return result;
        }

        ModifyCapital(-costSpent);
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
        float prev = _happiness;
        _happiness = Mathf.Clamp(_happiness + delta, 0f, 100f);

        if (!Mathf.Approximately(prev, _happiness))
            OnHappinessChanged?.Invoke(_happiness);

        if (_happiness <= 0f)
            TriggerGameOver();
    }

    private float CalculateDailyTaxRevenue()
    {
        float happinessFactor = _happiness / 100f;
        float tax = baseTaxPerDay * happinessFactor;

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
            OnDayChanged?.Invoke(_daysPassed, totalDays);

            float tax = CalculateDailyTaxRevenue();
            ModifyCapital(tax);

            if (_daysPassed >= totalDays)
                TriggerGameOver();
        }
    }

    // ─────────────────────────────────────────
    //  WIN / LOSE
    // ─────────────────────────────────────────

    private void CheckVictory()
    {
        if (_accidentRate == 0 && _happiness > 0f && _gameRunning)
        {
            _gameRunning = false;
            StopAllCoroutines();
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
        if (levelConfigs == null || levelConfigs.Length == 0)
        {
            Debug.LogError("[GameManager] levelConfigs is empty! Using defaults.");
            return new LevelConfig { level = 1, startCapitalRM = 1000f, startAccidentRate = 10 };
        }

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
        OnDayChanged?.Invoke(_daysPassed, totalDays);
    }

    // ─────────────────────────────────────────
    //  SCORE
    // ─────────────────────────────────────────

    public int CalculateFinalScore(float totalBudgetSpent)
    {
        float accidentScore = Mathf.Max(0, 100 - _accidentRate * 4f);
        float happinessScore = _happiness;
        float budgetScore = Mathf.Clamp(100f - (totalBudgetSpent / 100f), 0f, 100f);

        float raw = (accidentScore * 0.5f) +
                    (happinessScore * 0.3f) +
                    (budgetScore * 0.2f);

        return Mathf.Clamp(Mathf.RoundToInt(raw * 100f), 1, 10000);
    }
}