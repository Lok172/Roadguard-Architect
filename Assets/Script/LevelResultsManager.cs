using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────────────────────────
//  LEVEL RESULTS MANAGER  (v7 — uses SimpleLineChart.cs)
//
//  SimpleLineChart must be in its own file in the same project.
//
//  INSPECTOR SETUP:
//   • chartContainer   → the RectTransform of your chart panel
//   • axisLabelPrefab  → prefab with only RectTransform + TMP_Text
//   • axisLabelParent  → same RectTransform as chartContainer (or any canvas parent)
//   • All other label/style fields are on the SimpleLineChart component
//     that gets added automatically to chartContainer at runtime.
// ─────────────────────────────────────────────────────────────────

public class LevelResultsManager : MonoBehaviour
{
    // ── Result Header ────────────────────────────────────────────
    [Header("Result Header")]
    public TMP_Text levelCompleteLabel;
    public TMP_Text sceneLabel;

    private static readonly Color ColorSuccess = new Color(120f / 255f, 255f / 255f, 0f / 255f);
    private static readonly Color ColorFail = new Color(255f / 255f, 49f / 255f, 0f / 255f);

    // ── Safety Score ─────────────────────────────────────────────
    [Header("Safety Score")]
    public TMP_Text safetyScoreLabel;

    // ── Summary Panel ────────────────────────────────────────────
    [Header("Summary Panel")]
    public TMP_Text devicesPlacedLabel;
    public TMP_Text overallEffectivenessLabel;
    public TMP_Text finalAccidentRateLabel;
    public TMP_Text finalHappinessLabel;

    // ── Device Effectiveness Table ───────────────────────────────
    [Header("Device Effectiveness Table — Stop Sign")]
    public TMP_Text stopSignQuantityLabel;
    public TMP_Text stopSignEffectivenessLabel;

    [Header("Device Effectiveness Table — Speed Bump")]
    public TMP_Text speedBumpQuantityLabel;
    public TMP_Text speedBumpEffectivenessLabel;

    [Header("Device Effectiveness Table — Traffic Light")]
    public TMP_Text trafficLightQuantityLabel;
    public TMP_Text trafficLightEffectivenessLabel;

    // ── Chart ────────────────────────────────────────────────────
    [Header("Chart")]
    [Tooltip("RectTransform of the chart panel. SimpleLineChart will be added here automatically.")]
    public RectTransform chartContainer;

    [Tooltip("Prefab with only RectTransform + TMP_Text — used for axis tick numbers and titles.")]
    public GameObject axisLabelPrefab;

    [Tooltip("Parent under which axis labels are spawned. Assign chartContainer or any canvas panel.")]
    public RectTransform axisLabelParent;

    // ── Testing ───────────────────────────────────────────────────
    [Header("Testing")]
    public bool testMode = false;

    // ── Private ───────────────────────────────────────────────────
    private SimpleLineChart _lineChart;

    private static readonly Dictionary<int, string> LevelSceneNames = new Dictionary<int, string>
    {
        { 1, "Residential Zone"      },
        { 2, "T-Junction"            },
        { 3, "Urban 4-way Crossroad" },
    };

    private static readonly string[] AllDeviceTypes = { "StopSign", "SpeedBump", "TrafficLight" };

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private IEnumerator Start()
    {
        yield return null; // let Unity finish its own Awake/Start pass

        // ── Build / retrieve the chart component ──────────────────
        if (chartContainer != null)
        {
            _lineChart = chartContainer.GetComponent<SimpleLineChart>();
            if (_lineChart == null)
                _lineChart = chartContainer.gameObject.AddComponent<SimpleLineChart>();

            // Wire up label spawning fields
            _lineChart.axisLabelPrefab = axisLabelPrefab;
            _lineChart.axisLabelParent = axisLabelParent != null ? axisLabelParent : chartContainer;
        }
        else
        {
            Debug.LogError("[LevelResultsManager] chartContainer is not assigned!");
        }

        // ── Populate ──────────────────────────────────────────────
        if (testMode)
        {
            DrawTestGraph();
            yield break;
        }

        LevelResultPayload payload = LastLevelResult.Payload;
        if (payload == null)
        {
            Debug.LogWarning("[LevelResultsManager] No result payload found.");
            yield break;
        }

        PopulateHeader(payload);
        PopulateSafetyScore(payload);
        PopulateSummary(payload);
        PopulateDeviceTable(payload.deviceEffectiveness);
        DrawAccidentTrend(payload.accidentSnapshots, PlayerPrefs.GetInt("StartAccidentRate", -1));

        if (ShouldUploadResult() && ApiClient.Instance != null && UserSession.IsLoggedIn)
            StartCoroutine(UploadLevelResult(payload));
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (testMode) DrawTestGraph();
    }

    // ─────────────────────────────────────────
    //  CHART
    // ─────────────────────────────────────────

    private void DrawAccidentTrend(List<AccidentSnapshot> snapshots, int startAccidentRate = -1)
    {
        if (_lineChart == null)
        {
            Debug.LogWarning("[LevelResultsManager] _lineChart is null — chartContainer not assigned?");
            return;
        }
        if (snapshots == null || snapshots.Count == 0)
        {
            Debug.LogWarning("[LevelResultsManager] No snapshots to draw.");
            return;
        }

        var points = new List<Vector2>();

        if (startAccidentRate >= 0)
            points.Add(new Vector2(0, startAccidentRate));

        foreach (var snap in snapshots)
            points.Add(new Vector2(snap.day, snap.accidentRate));

        // One call — redraws mesh AND rebuilds axis labels
        _lineChart.SetData(points);
    }

    // ─────────────────────────────────────────
    //  TEST GRAPH
    // ─────────────────────────────────────────

    private void DrawTestGraph()
    {
        var samples = new List<AccidentSnapshot>
        {
            new AccidentSnapshot { day =  1, accidentRate =  2 },
            new AccidentSnapshot { day =  5, accidentRate =  4 },
            new AccidentSnapshot { day = 10, accidentRate =  8 },
            new AccidentSnapshot { day = 20, accidentRate = 12 },
            new AccidentSnapshot { day = 30, accidentRate = 16 },
            new AccidentSnapshot { day = 40, accidentRate = 16 },
            new AccidentSnapshot { day = 50, accidentRate = 14 },
            new AccidentSnapshot { day = 60, accidentRate = 18 },
            new AccidentSnapshot { day = 70, accidentRate = 13 },
            new AccidentSnapshot { day = 80, accidentRate =  8 },
            new AccidentSnapshot { day = 88, accidentRate =  0 },
        };

        DrawAccidentTrend(samples, 2);

        if (levelCompleteLabel != null)
        {
            levelCompleteLabel.text = "Simulation Results: Level 2 Clear (58 days used)  [TEST]";
            levelCompleteLabel.color = ColorSuccess;
        }
        if (sceneLabel != null) sceneLabel.text = "T-Junction  [TEST]";
        if (safetyScoreLabel != null) safetyScoreLabel.text = "Safety Score: 8,972 / 10,000  [TEST]";
        if (devicesPlacedLabel != null) devicesPlacedLabel.text = "Total Devices Placed: 6  [TEST]";
        if (overallEffectivenessLabel != null) overallEffectivenessLabel.text = "Overall Device Effectiveness: 83.3%  [TEST]";
        if (finalAccidentRateLabel != null) finalAccidentRateLabel.text = "Final Accident Rate: 0  [TEST]";
        if (finalHappinessLabel != null) finalHappinessLabel.text = "Final Happiness: 78  [TEST]";
    }

    // ─────────────────────────────────────────
    //  POPULATE HELPERS
    // ─────────────────────────────────────────

    private static bool ShouldUploadResult()
    {
        if (GameManager.Instance != null && GameManager.Instance.devMode) return false;
        var lsm = Object.FindFirstObjectByType<LevelSelectManager>();
        return lsm == null || !lsm.developerMode;
    }

    private void PopulateHeader(LevelResultPayload p)
    {
        if (levelCompleteLabel != null)
        {
            bool success = p.finalAccidentRate == 0;
            levelCompleteLabel.text = $"Simulation Results: Level {p.level} {(success ? "Clear" : "Failed")} ({p.daysUsed} days used)";
            levelCompleteLabel.color = success ? ColorSuccess : ColorFail;
        }
        if (sceneLabel != null)
            sceneLabel.text = LevelSceneNames.TryGetValue(p.level, out string n) ? n : $"Level {p.level}";
    }

    private void PopulateSafetyScore(LevelResultPayload p)
    {
        if (safetyScoreLabel != null)
            safetyScoreLabel.text = $"{p.safetyScore:N0} / 10,000";
    }

    private void PopulateSummary(LevelResultPayload p)
    {
        int total = 0;
        if (p.deviceEffectiveness != null)
            foreach (var e in p.deviceEffectiveness) total += e.placedCount;

        if (devicesPlacedLabel != null)
            devicesPlacedLabel.text = $"Total Devices Placed: {total}";
        if (overallEffectivenessLabel != null)
            overallEffectivenessLabel.text = $"Overall Device Effectiveness: {p.overallDeviceEffectiveness:F1}%";
        if (finalAccidentRateLabel != null)
            finalAccidentRateLabel.text = $"Final Accident Rate: {p.finalAccidentRate}";
        if (finalHappinessLabel != null)
            finalHappinessLabel.text = $"Final Happiness: {p.finalHappiness:F0}";
    }

    private void PopulateDeviceTable(List<DeviceEffectivenessEntry> entries)
    {
        var lookup = new Dictionary<string, DeviceEffectivenessEntry>();
        if (entries != null)
            foreach (var e in entries) lookup[e.deviceType] = e;

        SetDeviceRow("StopSign", stopSignQuantityLabel, stopSignEffectivenessLabel, lookup);
        SetDeviceRow("SpeedBump", speedBumpQuantityLabel, speedBumpEffectivenessLabel, lookup);
        SetDeviceRow("TrafficLight", trafficLightQuantityLabel, trafficLightEffectivenessLabel, lookup);
    }

    private static void SetDeviceRow(string deviceType,
                                     TMP_Text quantityLabel,
                                     TMP_Text effectivenessLabel,
                                     Dictionary<string, DeviceEffectivenessEntry> lookup)
    {
        int count = 0;
        float eff = 0f;
        if (lookup.TryGetValue(deviceType, out DeviceEffectivenessEntry found))
        {
            count = found.placedCount;
            eff = found.effectivenessPercent;
        }

        if (quantityLabel != null) quantityLabel.text = count.ToString();
        if (effectivenessLabel != null) effectivenessLabel.text = $"{eff:F0}";
    }

    // ─────────────────────────────────────────
    //  UPLOAD
    // ─────────────────────────────────────────

    private IEnumerator UploadLevelResult(LevelResultPayload p)
    {
        var body = new LevelResultRequest
        {
            playerId = p.userId,
            levelNumber = p.level,
            safetyScore = p.safetyScore,
            daysUsed = p.daysUsed
        };

        yield return ApiClient.Instance.Post<LevelResultResponse>(
            "api/results", body,
            (response, error) =>
            {
                if (error != null)
                    Debug.LogWarning($"[LevelResultsManager] Upload failed: {error}");
                else
                    Debug.Log($"[LevelResultsManager] Uploaded. Server id: {response?.id}");
            });
    }
}

// ─────────────────────────────────────────────────────────────────
//  DTOs  (keep here or move to their own files)
// ─────────────────────────────────────────────────────────────────

[System.Serializable] public class LevelResultRequest { public int playerId, levelNumber, safetyScore, daysUsed; }
[System.Serializable] public class LevelResultResponse { public int id; }
[System.Serializable] public class PersonalRecordResponse { public int highestScore, rank; }