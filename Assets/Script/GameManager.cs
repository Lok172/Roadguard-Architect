using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────
//  GAME MANAGER (v8)
//
//  CHANGES vs v7:
//    • Added levelResultSceneName (public string) below Level Configuration.
//      On end-game, after FinaliseAndSubmitPayload(), the game opens the
//      Level Results scene via PageManager.ChangeUI(levelResultSceneName)
//      or falls back to SceneManager.LoadScene().
//    • Safety Score formula changed to three weighted factors:
//        Accident Rate       40%
//        Device Effectiveness 30%
//        Happiness           30%
//    • _totalBudgetSpent and TotalBudgetSpent removed (no longer needed).
//    • FinaliseAndSubmitPayload now computes overallDeviceEffectiveness
//      from RoadManager before scoring, then writes it to the payload.
//    • CalculateFinalScore(float) signature updated to
//      CalculateFinalScore(float accidentRate, float deviceEff, float happiness).
//    • SpendCapital no longer tracks _totalBudgetSpent.
//    • Custom Inspector live stat "Budget Spent" line removed.
// ─────────────────────────────────────────────────────────────────

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Level Configuration ───────────────────────────────────────
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
        new LevelConfig { level = 1, startCapitalRM = 1000f,  startAccidentRate = 10, startHappiness = 100f },
        new LevelConfig { level = 2, startCapitalRM = 2500f,  startAccidentRate = 15, startHappiness = 80f  },
        new LevelConfig { level = 3, startCapitalRM = 3500f,  startAccidentRate = 25, startHappiness = 60f  }
    };

    [Tooltip("Name of the UI page / scene to open when a level ends. " +
             "Used with PageManager.ChangeUI(); falls back to SceneManager.LoadScene().")]
    public string levelResultSceneName = "LevelResult";

    // ── Game Rules ────────────────────────────────────────────────
    [Header("Game Rules")]
    public float secondsPerDay = 2f;
    public int totalDays = 90;
    public int safetyThreshold = 3;
    public float safetyMultiplier = 1.5f;
    public float baseTaxPerDay = 50f;

    [Header("Baseline Accident Decay")]
    [Tooltip("How much the baseline accident rate decreases each in-game day.")]
    [Min(0f)] public float baselineDecayPerDay = 1f;

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

    // ── Developer Cheats ─────────────────────────────────────────
    [Header("Developer Cheats")]
    [Tooltip("When active, happiness is locked, money and days are overridden.")]
    public bool devMode = false;
    [Tooltip("Happiness is clamped to this value every frame while devMode is on.")]
    [Range(0f, 100f)] public float devHappiness = 100f;
    [Tooltip("Capital is set to this value when dev mode is activated.")]
    public float devMoney = 99999f;
    [Tooltip("Total days is set to this value when dev mode is activated.")]
    public int devTotalDays = 1000;

    // ── Accident Simulation ───────────────────────────────────────
    [Header("Accident Simulation")]
    [Tooltip("Assign the AreaTargetManager in the scene. If null, accident simulation is skipped.")]
    public AreaTargetManager areaTargetManager;
    [Tooltip("How often (real seconds) GameManager picks a random car and triggers an accident.")]
    [Min(1f)] public float accidentIntervalSeconds = 10f;

    // ── Runtime State ─────────────────────────────────────────────
    [Header("Runtime State (read-only)")]
    [SerializeField] private float _capital;
    [SerializeField] private float _happiness;
    [SerializeField] private int _accidentRate;
    [SerializeField] private int _baselineAccidentRate;
    [SerializeField] private int _daysPassed;
    [SerializeField] private bool _gameRunning;
    [SerializeField] private int _consecutiveLowAccidentDays;
    private bool _dayTickPaused;

    // ── Public accessors ─────────────────────────────────────────
    public float Capital => _capital;
    public float Happiness => _happiness;
    public int AccidentRate => _accidentRate;
    public int DaysPassed => _daysPassed;
    public int TotalDays => totalDays;
    public bool GameRunning => _gameRunning;
    public int ConsecutiveLowAccidentDays => _consecutiveLowAccidentDays;

    // ── Internal registries ───────────────────────────────────────
    private readonly List<RoadTile> _allTiles = new List<RoadTile>();
    private RoadManager _roadManager;

    // ── Result payload built throughout the level ─────────────────
    private LevelResultPayload _payload;

    // ── Events ────────────────────────────────────────────────────
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

    private void Start()
    {
        currentLevel = PlayerPrefs.GetInt("CurrentLevel", currentLevel);

        GameObject obj = GameObject.Find("AreaTargetManager");
        if (obj != null)
            areaTargetManager = obj.GetComponent<AreaTargetManager>();
        else
            Debug.LogWarning("[GameManager] No GameObject named 'AreaTargetManager' found in scene.");

        InitLevel(currentLevel);
    }

    private void LateUpdate()
    {
        if (devMode && _gameRunning && !Mathf.Approximately(_happiness, devHappiness))
        {
            _happiness = devHappiness;
            OnHappinessChanged?.Invoke(_happiness);
        }
    }

    // ─────────────────────────────────────────
    //  LEVEL INITIALISATION
    // ─────────────────────────────────────────

    public void InitLevel(int level)
    {
        StopAllCoroutines();
        Time.timeScale = 1f;

        LevelConfig cfg = GetLevelConfig(level);

        _capital = cfg.startCapitalRM;
        _baselineAccidentRate = cfg.startAccidentRate;
        _accidentRate = _baselineAccidentRate;
        _happiness = Mathf.Clamp(cfg.startHappiness, 0f, 100f);
        _daysPassed = 0;
        _gameRunning = true;
        _dayTickPaused = false;
        _consecutiveLowAccidentDays = 0;

        PlayerPrefs.SetInt("StartAccidentRate", _baselineAccidentRate);
        PlayerPrefs.Save();

        _payload = new LevelResultPayload
        {
            userId = UserSession.IsLoggedIn ? UserSession.CurrentUser.userId : 0,
            level = level
        };

        if (devMode) ApplyDevOverrides();

        StartCoroutine(BroadcastNextFrame());
        StartCoroutine(DayTickRoutine());
        StartCoroutine(AccidentSimulationRoutine());

        Debug.Log($"[GameManager] Level {level} started. Capital=RM{_capital} " +
                  $"Baseline={_baselineAccidentRate} DecayPerDay={baselineDecayPerDay}" +
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

    public void ActivateDevMode()
    {
        devMode = true;
        ApplyDevOverrides();
        Debug.Log("[GameManager] DEV MODE ACTIVATED — " +
                  $"Happiness={devHappiness}, Money=RM{devMoney}, Days={devTotalDays}");
    }

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

    public void UnregisterTile(RoadTile tile) { _allTiles.Remove(tile); }

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
    //  DAY TICK PAUSE
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

        SpendCapital(costSpent);

        if (!Mathf.Approximately(happinessDelta, 0f))
            ModifyHappiness(happinessDelta);

        RecomputeCityAccidentRate();
        return result;
    }

    // ─────────────────────────────────────────
    //  BUDGET / ECONOMY
    // ─────────────────────────────────────────

    /// <summary>Deducts cost from capital. Returns false if insufficient funds.</summary>
    public bool SpendCapital(float cost)
    {
        if (_capital < cost) return false;
        _capital -= cost;
        OnCapitalChanged?.Invoke(_capital);
        return true;
    }

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

            // 2) Decay baseline accident rate toward zero.
            if (baselineDecayPerDay > 0f && _baselineAccidentRate > 0)
            {
                _baselineAccidentRate -= Mathf.CeilToInt(baselineDecayPerDay);
                _baselineAccidentRate = Mathf.Max(0, _baselineAccidentRate);
            }

            // 3) Update city aggregate rate.
            RecomputeCityAccidentRate();

            // 4) Record daily accident snapshot for the trend graph.
            LevelProgress.RecordDailyAccidentRate(_payload, _daysPassed, _accidentRate);

            // 5) Low-accident streak bonus.
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

            // 6) Tax revenue.
            float tax = CalculateDailyTaxRevenue();
            ModifyCapital(tax);

            if (_daysPassed >= totalDays && !devMode) TriggerGameOver();
        }
    }

    // ─────────────────────────────────────────
    //  ACCIDENT SIMULATION
    // ─────────────────────────────────────────

    /// <summary>
    /// Every accidentIntervalSeconds, picks a random car inside the
    /// designated area cubes and triggers the accident sequence on it.
    /// Stops automatically when the game ends.
    /// </summary>
    private IEnumerator AccidentSimulationRoutine()
    {
        if (areaTargetManager == null)
        {
            Debug.LogWarning("[GameManager] AccidentSimulationRoutine: no AreaTargetManager assigned — skipping.");
            yield break;
        }

        while (_gameRunning)
        {
            yield return new WaitForSeconds(accidentIntervalSeconds);

            if (!_gameRunning) break;
            if (_dayTickPaused) continue;

            Debug.Log($"[GameManager] Triggering accident simulation (every {accidentIntervalSeconds}s).");
            areaTargetManager.PickTargetCar();
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
            LevelAudioManager.Instance?.PlayWinGame();
            FinaliseAndSubmitPayload(won: true);
            OnVictory?.Invoke();
            Time.timeScale = 0f;
            OpenLevelResultScene();
        }
    }

    private void TriggerGameOver()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        StopAllCoroutines();
        Debug.Log("[GameManager] GAME OVER");
        LevelAudioManager.Instance?.PlayGameOver();
        FinaliseAndSubmitPayload(won: false);
        OnGameOver?.Invoke();
        Time.timeScale = 0f;
        OpenLevelResultScene();
    }

    // ─────────────────────────────────────────
    //  LEVEL RESULT SCENE
    // ─────────────────────────────────────────

    /// <summary>
    /// Opens the Level Results screen via PageManager if available,
    /// otherwise loads the scene by name.
    /// </summary>
    private void OpenLevelResultScene()
    {
        if (string.IsNullOrWhiteSpace(levelResultSceneName))
        {
            Debug.LogWarning("[GameManager] levelResultSceneName is empty — cannot open Level Results.");
            return;
        }

        if (PageManager.Instance != null)
        {
            PageManager.Instance.ChangeUI(levelResultSceneName);
            Debug.Log($"[GameManager] Opening Level Results via PageManager: '{levelResultSceneName}'");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(levelResultSceneName);
            Debug.Log($"[GameManager] Opening Level Results via SceneManager: '{levelResultSceneName}'");
        }
    }

    // ─────────────────────────────────────────
    //  PAYLOAD FINALISATION
    // ─────────────────────────────────────────

    private void FinaliseAndSubmitPayload(bool won)
    {
        // Collect device effectiveness data first (needed for scoring).
        if (_roadManager != null)
            _roadManager.PopulateDeviceEffectiveness(_payload.deviceEffectiveness);

        // Compute overall device effectiveness: total correct / total placed * 100.
        float overallDeviceEff = ComputeOverallDeviceEffectiveness(_payload.deviceEffectiveness);

        int score = CalculateFinalScore(_accidentRate, overallDeviceEff, _happiness);

        _payload.daysUsed = _daysPassed;
        _payload.finalAccidentRate = _accidentRate;
        _payload.finalHappiness = _happiness;
        _payload.safetyScore = score;
        _payload.overallDeviceEffectiveness = overallDeviceEff;

        // Bridge to the Level Results scene.
        LastLevelResult.Set(_payload);

        // Submit to backend and mark cleared locally (both win and loss).
        LevelProgress.MarkLevelCleared(currentLevel, _payload);
    }

    /// <summary>
    /// Aggregates correctness across all device entries to produce
    /// an overall effectiveness percentage (0–100).
    /// </summary>
    private static float ComputeOverallDeviceEffectiveness(List<DeviceEffectivenessEntry> entries)
    {
        if (entries == null || entries.Count == 0) return 0f;

        int totalPlaced = 0;
        int totalCorrect = 0;

        foreach (var e in entries)
        {
            totalPlaced += e.placedCount;
            // effectivenessPercent = correct/placed*100, so correct = placedCount * pct / 100
            totalCorrect += Mathf.RoundToInt(e.placedCount * e.effectivenessPercent / 100f);
        }

        return totalPlaced > 0 ? (float)totalCorrect / totalPlaced * 100f : 0f;
    }

    // ─────────────────────────────────────────
    //  SCORE FORMULA
    // ─────────────────────────────────────────

    /// <summary>
    /// Safety Score (1–10 000) weighted:
    ///   40% accident-rate performance   (100 − accidentRate × 4, clamped 0–100)
    ///   30% overall device effectiveness (0–100)
    ///   30% final happiness              (0–100)
    /// </summary>
    public int CalculateFinalScore(float accidentRate, float deviceEffectiveness, float happiness)
    {
        float accidentScore = Mathf.Clamp(100f - accidentRate * 4f, 0f, 100f);
        float raw = accidentScore * 0.40f
                  + deviceEffectiveness * 0.30f
                  + happiness * 0.30f;
        return Mathf.Clamp(Mathf.RoundToInt(raw * 100f), 1, 10000);
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
}

// ─────────────────────────────────────────────────────────────────
//  LAST LEVEL RESULT
// ─────────────────────────────────────────────────────────────────

public static class LastLevelResult
{
    public static LevelResultPayload Payload { get; private set; }

    public static void Set(LevelResultPayload p) => Payload = p;
    public static void Clear() => Payload = null;
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

        if (gm.devMode)
            EditorGUILayout.HelpBox(
                "DEV MODE ACTIVE\n" +
                $"Happiness locked to {gm.devHappiness}\n" +
                $"Money = RM {gm.devMoney:N0}\n" +
                $"Total days = {gm.devTotalDays}\n" +
                "Game-over from happiness/day-limit is disabled.",
                MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Dev mode is OFF. Click below to activate.", MessageType.Info);

        EditorGUILayout.Space(4);

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

        if (Application.isPlaying)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Live Stats", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Capital:        RM {gm.Capital:N0}");
            EditorGUILayout.LabelField($"Happiness:      {gm.Happiness:F1}");
            EditorGUILayout.LabelField($"Accident Rate:  {gm.AccidentRate}");
            EditorGUILayout.LabelField($"Day:            {gm.DaysPassed} / {gm.TotalDays}");
            Repaint();
        }
    }
}
#endif