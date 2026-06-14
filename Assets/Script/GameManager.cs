using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────
//  GAME MANAGER (v6)
//
//  CHANGES vs v5:
//    • Added Developer Cheats section (Inspector-tunable):
//      - devMode toggle
//      - devHappiness, devMoney, devTotalDays overrides
//      - Custom Inspector with "Activate Dev Mode" / "Deactivate"
//        buttons.
//    • Everything else unchanged from v5.
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

    [Header("Low Accident Streak Bonus")]
    [Tooltip("Accident rate must stay strictly below this for the streak to count.")]
    public int lowAccidentThreshold = 3;

    [Tooltip("Consecutive days below threshold before the first bonus fires.")]
    public int lowAccidentStreakRequired = 5;

    [Tooltip("After the first bonus, a new bonus fires every this-many days (while streak holds).")]
    public int lowAccidentBonusInterval = 3;

    [Tooltip("Min happiness bonus per trigger.")]
    public float lowAccidentBonusMin = 3f;

    [Tooltip("Max happiness bonus per trigger.")]
    public float lowAccidentBonusMax = 7f;

    // ── Developer Cheats ──────────────────────────────────────────
    [Header("Developer Cheats")]
    [Tooltip("When active, happiness is locked, money and days are overridden.")]
    public bool devMode = false;

    [Tooltip("Happiness is clamped to this value every frame while devMode is on.")]
    [Range(0f, 100f)]
    public float devHappiness = 100f;

    [Tooltip("Capital is set to this value when dev mode is activated.")]
    public float devMoney = 99999f;

    [Tooltip("Total days is set to this value when dev mode is activated.")]
    public int devTotalDays = 1000;

    [Header("Runtime State (read-only)")]
    [SerializeField] private float _capital;
    [SerializeField] private float _happiness;
    [SerializeField] private int _accidentRate;
    [SerializeField] private int _baselineAccidentRate;
    [SerializeField] private int _daysPassed;
    [SerializeField] private bool _gameRunning;
    [SerializeField] private int _consecutiveLowAccidentDays;
    private bool _dayTickPaused;

    public float Capital => _capital;
    public float Happiness => _happiness;
    public int AccidentRate => _accidentRate;
    public int DaysPassed => _daysPassed;
    public int TotalDays => totalDays;
    public bool GameRunning => _gameRunning;
    public int ConsecutiveLowAccidentDays => _consecutiveLowAccidentDays;

    // ── Tile registry (kept for legacy event subscribers) ──
    private readonly List<RoadTile> _allTiles = new List<RoadTile>();

    // ── Road Manager ──
    private RoadManager _roadManager;

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

    private void LateUpdate()
    {
        // Dev mode: lock happiness every frame
        if (devMode && _gameRunning)
        {
            if (!Mathf.Approximately(_happiness, devHappiness))
            {
                _happiness = devHappiness;
                OnHappinessChanged?.Invoke(_happiness);
            }
        }
    }

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
        _consecutiveLowAccidentDays = 0;

        // Apply dev overrides if active at level start
        if (devMode) ApplyDevOverrides();

        StartCoroutine(BroadcastNextFrame());
        StartCoroutine(DayTickRoutine());

        Debug.Log($"[GameManager] Level {level} started. Capital=RM{_capital} " +
                  $"BaselineAccident={_baselineAccidentRate}" +
                  (devMode ? " [DEV MODE]" : ""));
    }

    private IEnumerator BroadcastNextFrame()
    {
        yield return null;
        BroadcastState();
    }

    // ─────────────────────────────────────────
    //  DEVELOPER CHEATS
    // ─────────────────────────────────────────

    /// <summary>
    /// Activates dev mode: sets money, days, and locks happiness.
    /// Safe to call from Inspector button or code at any time.
    /// </summary>
    public void ActivateDevMode()
    {
        devMode = true;
        ApplyDevOverrides();
        Debug.Log("[GameManager] DEV MODE ACTIVATED — " +
                  $"Happiness={devHappiness}, Money=RM{devMoney}, Days={devTotalDays}");
    }

    /// <summary>Deactivates dev mode. Current values are kept but no longer locked.</summary>
    public void DeactivateDevMode()
    {
        devMode = false;
        Debug.Log("[GameManager] DEV MODE DEACTIVATED — values unlocked.");
    }

    private void ApplyDevOverrides()
    {
        _happiness = devHappiness;
        _capital = devMoney;
        totalDays = devTotalDays;

        OnHappinessChanged?.Invoke(_happiness);
        OnCapitalChanged?.Invoke(_capital);
        OnDayChanged?.Invoke(_daysPassed, totalDays);
    }

    // ─────────────────────────────────────────
    //  TILE / ROAD MANAGER REGISTRY
    // ─────────────────────────────────────────

    public void RegisterTile(RoadTile tile)
    {
        if (!_allTiles.Contains(tile)) _allTiles.Add(tile);
    }

    public void UnregisterTile(RoadTile tile)
    {
        _allTiles.Remove(tile);
    }

    public void RegisterRoadManager(RoadManager rm)
    {
        _roadManager = rm;
        Debug.Log($"[GameManager] RoadManager registered ({rm.Sections.Count} sections).");
    }

    public void UnregisterRoadManager(RoadManager rm)
    {
        if (_roadManager == rm) _roadManager = null;
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
        GameObject deviceObject)
    {
        PlacementResult result = tile.PlaceDevice(
            device, corner, deviceObject, _capital,
            out float happinessDelta,
            out float costSpent);

        if (result == PlacementResult.AlreadyOccupied ||
            result == PlacementResult.DeviceNotAllowed ||
            result == PlacementResult.InsufficientFunds)
            return result;

        ModifyCapital(-costSpent);
        if (!Mathf.Approximately(happinessDelta, 0f))
            ModifyHappiness(happinessDelta);

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

        if (_happiness <= 0f && !devMode) TriggerGameOver();
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
        if (_roadManager != null)
            sum += _roadManager.GetTotalSectionAccidentRate();

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

            while (_dayTickPaused) yield return null;

            _daysPassed++;
            OnDayChanged?.Invoke(_daysPassed, totalDays);

            // 1) Sections advance (via RoadManager).
            if (_roadManager != null)
            {
                float sectionDelta = _roadManager.TickAllSections();
                if (!Mathf.Approximately(sectionDelta, 0f))
                    ModifyHappiness(sectionDelta);
            }

            // 2) Update city aggregate rate.
            RecomputeCityAccidentRate();

            // 3) Low-accident streak bonus.
            if (_accidentRate < lowAccidentThreshold)
            {
                _consecutiveLowAccidentDays++;

                if (_consecutiveLowAccidentDays >= lowAccidentStreakRequired)
                {
                    int daysPastStreak = _consecutiveLowAccidentDays - lowAccidentStreakRequired;
                    if (daysPastStreak % lowAccidentBonusInterval == 0)
                    {
                        float bonus = Random.Range(lowAccidentBonusMin, lowAccidentBonusMax);
                        ModifyHappiness(bonus);
                        Debug.Log($"[GameManager] Low-accident streak bonus: +{bonus:F1} happiness " +
                                  $"(streak day {_consecutiveLowAccidentDays})");
                    }
                }
            }
            else
            {
                _consecutiveLowAccidentDays = 0;
            }

            // 4) Tax revenue.
            float tax = CalculateDailyTaxRevenue();
            ModifyCapital(tax);

            if (_daysPassed >= totalDays && !devMode) TriggerGameOver();
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

// ─────────────────────────────────────────────────────────────────
//  CUSTOM INSPECTOR — Developer Cheat Buttons
// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GameManager gm = (GameManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Developer Controls", EditorStyles.boldLabel);

        // ── Status ───────────────────────────
        if (gm.devMode)
        {
            EditorGUILayout.HelpBox(
                "DEV MODE ACTIVE\n" +
                $"Happiness locked to {gm.devHappiness}\n" +
                $"Money = RM {gm.devMoney:N0}\n" +
                $"Total days = {gm.devTotalDays}\n" +
                "Game-over from happiness/day-limit is disabled.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Dev mode is OFF. Click below to activate.",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);

        // ── Buttons ──────────────────────────
        if (!gm.devMode)
        {
            GUI.backgroundColor = new Color(0.3f, 0.85f, 1f);
            if (GUILayout.Button("⚡  Activate Dev Mode", GUILayout.Height(32)))
            {
                Undo.RecordObject(gm, "Activate Dev Mode");
                gm.ActivateDevMode();
                EditorUtility.SetDirty(gm);
            }
        }
        else
        {
            GUI.backgroundColor = new Color(1f, 0.65f, 0.3f);
            if (GUILayout.Button("✕  Deactivate Dev Mode", GUILayout.Height(32)))
            {
                Undo.RecordObject(gm, "Deactivate Dev Mode");
                gm.DeactivateDevMode();
                EditorUtility.SetDirty(gm);
            }
        }

        GUI.backgroundColor = Color.white;

        // ── Runtime info ─────────────────────
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Live Stats", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Capital: RM {gm.Capital:N0}");
            EditorGUILayout.LabelField($"Happiness: {gm.Happiness:F1}");
            EditorGUILayout.LabelField($"Accident Rate: {gm.AccidentRate}");
            EditorGUILayout.LabelField($"Day: {gm.DaysPassed} / {gm.TotalDays}");
            Repaint();
        }
    }
}
#endif
