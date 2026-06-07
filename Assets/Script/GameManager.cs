using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ─────────────────────────────────────────────────────────────────
//  GAME MANAGER (v3)
//
//  CHANGES vs v2:
//    • Tracks a list of RoadSections (not individual tiles for
//      accident purposes — tiles are still registered for legacy
//      events). Each in-game day, every section ticks:
//          rate += dailyAccidentGain
//          rate -= perCorrectDeviceReduction × correct device count
//      then a happiness loss of (rate × happinessPerAccidentRate)
//      is applied across all sections.
//    • TryPlaceDevice now takes (RoadTile, device, corner, facing).
//    • AccidentRate (city total) = startAccidentRate baseline +
//      ceil(sum of all section rates).  Broadcast on change.
// ─────────────────────────────────────────────────────────────────

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Level Configuration")]
    public int currentLevel = 1;

    [System.Serializable]
    public struct LevelConfig
    {
        public int level;
        public float startCapitalRM;
        public int startAccidentRate;
        [Range(0, 100)] public float startHappiness;
    }

    public LevelConfig[] levelConfigs = new LevelConfig[]
    {
        new LevelConfig { level = 1, startCapitalRM = 1000f, startAccidentRate = 10, startHappiness = 100f },
        new LevelConfig { level = 2, startCapitalRM = 2500f, startAccidentRate = 15, startHappiness = 80f  },
        new LevelConfig { level = 3, startCapitalRM = 3500f, startAccidentRate = 25, startHappiness = 60f  }
    };

    [Header("Game Rules")]
    public float secondsPerDay = 2f;
    public int totalDays = 90;
    public int safetyThreshold = 3;
    public float safetyMultiplier = 1.5f;
    public float baseTaxPerDay = 50f;

    [Header("Runtime State (read-only)")]
    [SerializeField] private float _capital;
    [SerializeField] private float _happiness;
    [SerializeField] private int _accidentRate;
    [SerializeField] private int _baselineAccidentRate;
    [SerializeField] private int _daysPassed;
    [SerializeField] private bool _gameRunning;
    private bool _dayTickPaused;

    public float Capital => _capital;
    public float Happiness => _happiness;
    public int AccidentRate => _accidentRate;
    public int DaysPassed => _daysPassed;
    public int TotalDays => totalDays;
    public bool GameRunning => _gameRunning;

    private readonly List<RoadTile> _allTiles = new List<RoadTile>();
    private readonly List<RoadSection> _allSections = new List<RoadSection>();

    [Header("Events")]
    public UnityEvent<float> OnCapitalChanged;
    public UnityEvent<float> OnHappinessChanged;
    public UnityEvent<int> OnAccidentRateChanged;
    public UnityEvent<int, int> OnDayChanged;
    public UnityEvent OnGameOver;
    public UnityEvent OnVictory;

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => InitLevel(currentLevel);

    public void InitLevel(int level)
    {
        StopAllCoroutines();

        LevelConfig cfg = GetLevelConfig(level);

        _capital = cfg.startCapitalRM;
        _baselineAccidentRate = cfg.startAccidentRate;
        _accidentRate = _baselineAccidentRate;
        _happiness = Mathf.Clamp(cfg.startHappiness, 0f, 100f);
        _daysPassed = 0;
        _gameRunning = true;
        _dayTickPaused = false;

        StartCoroutine(BroadcastNextFrame());
        StartCoroutine(DayTickRoutine());

        Debug.Log($"[GameManager] Level {level} started. Capital=RM{_capital} BaselineAccident={_baselineAccidentRate}");
    }

    private IEnumerator BroadcastNextFrame()
    {
        yield return null;
        BroadcastState();
    }

    // ─────────────────────────────────────────
    //  TILE / SECTION REGISTRY
    // ─────────────────────────────────────────

    public void RegisterTile(RoadTile tile)
    {
        if (_allTiles.Contains(tile)) return;
        _allTiles.Add(tile);
    }

    public void UnregisterTile(RoadTile tile)
    {
        _allTiles.Remove(tile);
    }

    public void RegisterRoadSection(RoadSection section)
    {
        if (!_allSections.Contains(section))
        {
            _allSections.Add(section);
            Debug.Log($"[GameManager] Registered RoadSection '{section.name}' ({_allSections.Count} total).");
        }
    }

    public void UnregisterRoadSection(RoadSection section)
    {
        _allSections.Remove(section);
    }

    // ─────────────────────────────────────────
    //  DAY TICK PAUSE  (used during placement drag)
    // ─────────────────────────────────────────

    public void PauseDayTick() => _dayTickPaused = true;
    public void ResumeDayTick() => _dayTickPaused = false;

    // ─────────────────────────────────────────
    //  PLACEMENT
    // ─────────────────────────────────────────

    public PlacementResult TryPlaceDevice(
        RoadTile tile,
        TrafficDeviceType device,
        TileCorner corner,
        FacingDirection facing,
        GameObject deviceObject)
    {
        PlacementResult result = tile.PlaceDevice(
            device, corner, facing, deviceObject, _capital,
            out float happinessDelta,
            out float costSpent);

        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
            return result;

        ModifyCapital(-costSpent);
        if (!Mathf.Approximately(happinessDelta, 0f))
            ModifyHappiness(happinessDelta);

        // Recompute aggregate accident rate (sections may have changed correct counts)
        RecomputeCityAccidentRate();
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

        if (_happiness <= 0f) TriggerGameOver();
    }

    private float CalculateDailyTaxRevenue()
    {
        float happinessFactor = _happiness / 100f;
        float tax = baseTaxPerDay * happinessFactor;
        if (_accidentRate < safetyThreshold) tax *= safetyMultiplier;
        return tax;
    }

    private void RecomputeCityAccidentRate()
    {
        float sum = _baselineAccidentRate;
        foreach (var s in _allSections)
            if (s != null) sum += s.SectionAccidentRate;

        int newRate = Mathf.CeilToInt(sum);
        if (newRate != _accidentRate)
        {
            _accidentRate = newRate;
            OnAccidentRateChanged?.Invoke(_accidentRate);
            CheckVictory();
        }
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

            // Wait while placement drag is active
            while (_dayTickPaused)
                yield return null;

            _daysPassed++;
            OnDayChanged?.Invoke(_daysPassed, totalDays);

            // 1) Each section advances by one day. Sum up happiness penalties.
            float totalHappinessDelta = 0f;
            foreach (var s in _allSections)
            {
                if (s == null) continue;
                totalHappinessDelta += s.TickDay(); // negative
            }
            if (!Mathf.Approximately(totalHappinessDelta, 0f))
                ModifyHappiness(totalHappinessDelta);

            // 2) Update city aggregate rate
            RecomputeCityAccidentRate();

            // 3) Tax revenue
            float tax = CalculateDailyTaxRevenue();
            ModifyCapital(tax);

            if (_daysPassed >= totalDays) TriggerGameOver();
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
            Debug.LogError("[GameManager] levelConfigs is empty.");
            return new LevelConfig { level = 1, startCapitalRM = 1000f, startAccidentRate = 10, startHappiness = 100f };
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

    public int CalculateFinalScore(float totalBudgetSpent)
    {
        float accidentScore = Mathf.Max(0, 100 - _accidentRate * 4f);
        float happinessScore = _happiness;
        float budgetScore = Mathf.Clamp(100f - (totalBudgetSpent / 100f), 0f, 100f);
        float raw = accidentScore * 0.5f + happinessScore * 0.3f + budgetScore * 0.2f;
        return Mathf.Clamp(Mathf.RoundToInt(raw * 100f), 1, 10000);
    }
}