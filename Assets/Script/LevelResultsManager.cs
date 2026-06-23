using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using XCharts.Runtime;

// ─────────────────────────────────────────────────────────────────
//  LEVEL RESULTS MANAGER  (v5)
//
//  CHANGES vs v4:
//    1. Accident trend line now renders correctly:
//         - XAxis type set to Value (required for x-y AddData).
//         - Line serie configured with LineType.Normal and visible symbols.
//         - Tooltip trigger changed to Item with Auto position (follows mouse).
//         - ClearData() called before adding new data for clean redraws.
//    2. Day 0 prepended to accident trend using payload.startAccidentRate,
//       so the graph visually starts at the level's initial accident rate.
//    3. X-axis labels use only intervals of {2, 5, 10}, always include day 0,
//       with at least 4 but no more than 10 labels.
//    4. Device table now shows a bold header row first:
//       "Device Type | Quantity | Effectiveness (%)".
//    5. All three device types (StopSign, SpeedBump, TrafficLight) always
//       appear in the table, defaulting to 0 quantity and 0.0% effectiveness
//       when not present in the payload.
// ─────────────────────────────────────────────────────────────────

public class LevelResultsManager : MonoBehaviour
{
    // ── Result Header ────────────────────────────────────────────
    [Header("Result Header")]
    [Tooltip("Shows 'Simulation Results: Level X Clear/Failed (Y days used)'")]
    public TMP_Text levelCompleteLabel;
    [Tooltip("Shows the scene name, e.g. 'Residential Zone'")]
    public TMP_Text sceneLabel;

    private static readonly Color ColorSuccess = new Color(120f / 255f, 255f / 255f, 0f / 255f); // #78FF00
    private static readonly Color ColorFail = new Color(255f / 255f, 49f / 255f, 0f / 255f); // #FF3100

    // ── Safety Score ─────────────────────────────────────────────
    [Header("Safety Score")]
    [Tooltip("Shows this run's Safety Score, e.g. 'Safety Score: 8 540 / 10 000'")]
    public TMP_Text safetyScoreLabel;

    // ── Summary Panel ────────────────────────────────────────────
    [Header("Summary Panel")]
    public TMP_Text devicesPlacedLabel;
    public TMP_Text overallEffectivenessLabel;
    public TMP_Text finalAccidentRateLabel;
    public TMP_Text finalHappinessLabel;

    // ── Device Effectiveness Table ───────────────────────────────
    [Header("Device Effectiveness Table")]
    public Transform deviceTableParent;
    [SerializeField] private GameObject _deviceRowPrefab;   // use SerializeField so Unity survives
                                                            // domain reload / scene transitions

    // ── XCharts Accident Trend Graph ─────────────────────────────
    [Header("Accident Trend Graph (XCharts)")]
    [Tooltip("The XCharts LineChart component.")]
    public LineChart accidentLineChart;

    // ── Testing ───────────────────────────────────────────────────
    [Header("Testing")]
    [Tooltip("When ON, draws a sample graph instead of real payload data. " +
             "Toggle in the Inspector at any time — no play-mode restart needed.")]
    public bool testMode = false;

    // ─────────────────────────────────────────────────────────────
    //  Level → scene name mapping
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<int, string> LevelSceneNames = new Dictionary<int, string>
    {
        { 1, "Residential Zone"      },
        { 2, "T-Junction"            },
        { 3, "Urban 4-way Crossroad" }
    };

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private IEnumerator Start()
    {
        if (_deviceRowPrefab == null)
            Debug.LogError("[LevelResultsManager] _deviceRowPrefab is not assigned!");

        yield return new WaitForEndOfFrame();

        // ✅ Force XCharts to re-initialize — critical after scene transitions
        accidentLineChart.Init();

        yield return new WaitForEndOfFrame();

        ConfigureChart();

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
        DrawAccidentTrendXCharts(payload.accidentSnapshots, GameManager.Instance.AccidentRate);

        if (ShouldUploadResult() && ApiClient.Instance != null && UserSession.IsLoggedIn)
            StartCoroutine(UploadLevelResult(payload));
    }

    // Inspector toggle for testMode (works in Play mode without restart).
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (testMode) DrawTestGraph();
    }

    // ─────────────────────────────────────────
    //  CHART CONFIGURATION
    //  • XAxis set to Value type (required for x-y data pairs).
    //  • Tooltip follows mouse (Trigger=Item, Position=Auto).
    // ─────────────────────────────────────────

    private void ConfigureChart()
    {
        if (accidentLineChart == null) return;

        // ✅ REQUIRED for x-y AddData pairs — must be set before adding data
        var xAxis = accidentLineChart.EnsureChartComponent<XAxis>();
        xAxis.type = Axis.AxisType.Value;

        // Also set YAxis to Value to be safe
        var yAxis = accidentLineChart.EnsureChartComponent<YAxis>();
        yAxis.type = Axis.AxisType.Value;

        // Tooltip: follow the mouse pointer, trigger per data-point.
        var tooltip = accidentLineChart.EnsureChartComponent<Tooltip>();
        tooltip.show = true;
        tooltip.position = Tooltip.Position.Auto;

        accidentLineChart.RefreshChart();
    }

    /// <summary>
    /// Configures x-axis range and tick interval so that:
    ///   - Day 0 is always the first label.
    ///   - The interval is strictly one of {2, 5, 10}.
    ///   - Total label count is between 4 and 10 (inclusive).
    /// </summary>
    private void ConfigureXAxisInterval(int maxDay)
    {
        if (accidentLineChart == null) return;

        var xAxis = accidentLineChart.EnsureChartComponent<XAxis>();
        xAxis.min = 0;
        xAxis.minMaxType = Axis.AxisMinMaxType.Custom;

        // Choose the smallest interval from {2, 5, 10} that yields 4–10 labels.
        int[] candidates = { 2, 5, 10 };
        int chosenInterval = 2;
        foreach (int iv in candidates)
        {
            int roundedMax = Mathf.CeilToInt((float)maxDay / iv) * iv;
            int labelCount = (roundedMax / iv) + 1;   // 0, iv, 2iv, … roundedMax
            if (labelCount >= 4 && labelCount <= 10)
            {
                chosenInterval = iv;
                break;
            }
            if (labelCount > 10) continue;   // try a bigger interval
            chosenInterval = iv;              // fewer than 4 — best we can do
            break;
        }

        int axisMax = Mathf.CeilToInt((float)maxDay / chosenInterval) * chosenInterval;
        xAxis.max = axisMax;
        xAxis.splitNumber = axisMax / chosenInterval;

        accidentLineChart.RefreshChart();
    }

    // ─────────────────────────────────────────
    //  DEVELOPER-MODE GUARD
    // ─────────────────────────────────────────

    private static bool ShouldUploadResult()
    {
        if (GameManager.Instance != null && GameManager.Instance.devMode)
        {
            Debug.Log("[LevelResultsManager] Dev mode active — result NOT uploaded.");
            return false;
        }

        LevelSelectManager lsm = Object.FindFirstObjectByType<LevelSelectManager>();
        if (lsm != null && lsm.developerMode)
        {
            Debug.Log("[LevelResultsManager] LevelSelect developer mode active — result NOT uploaded.");
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────
    //  HEADER
    //    Success → green (#78FF00)  "… Level X Clear (Y days used)"
    //    Failure → red   (#FF3100)  "… Level X Failed (Y days used)"
    // ─────────────────────────────────────────

    private void PopulateHeader(LevelResultPayload p)
    {
        if (levelCompleteLabel != null)
        {
            bool success = p.finalAccidentRate == 0;
            string outcome = success ? "Clear" : "Failed";
            levelCompleteLabel.text = $"Simulation Results: Level {p.level} {outcome} ({p.daysUsed} days used)";
            levelCompleteLabel.color = success ? ColorSuccess : ColorFail;
        }

        if (sceneLabel != null)
        {
            string sn = LevelSceneNames.TryGetValue(p.level, out string name) ? name : $"Level {p.level}";
            sceneLabel.text = sn;
        }
    }

    // ─────────────────────────────────────────
    //  SAFETY SCORE  (this run only)
    // ─────────────────────────────────────────

    private void PopulateSafetyScore(LevelResultPayload p)
    {
        if (safetyScoreLabel != null)
            safetyScoreLabel.text = $"{p.safetyScore:N0} / 10,000";
    }

    // ─────────────────────────────────────────
    //  SUMMARY PANEL
    // ─────────────────────────────────────────

    private void PopulateSummary(LevelResultPayload p)
    {
        int totalPlaced = 0;
        if (p.deviceEffectiveness != null)
            foreach (var e in p.deviceEffectiveness)
                totalPlaced += e.placedCount;

        if (devicesPlacedLabel != null)
            devicesPlacedLabel.text = $"Total Devices Placed: {totalPlaced}";

        if (overallEffectivenessLabel != null)
            overallEffectivenessLabel.text =
                $"Overall Device Effectiveness: {p.overallDeviceEffectiveness:F1}%";

        if (finalAccidentRateLabel != null)
            finalAccidentRateLabel.text = $"Final Accident Rate: {p.finalAccidentRate}";

        if (finalHappinessLabel != null)
            finalHappinessLabel.text = $"Final Happiness: {p.finalHappiness:F0}";
    }

    // ─────────────────────────────────────────
    //  DEVICE EFFECTIVENESS TABLE
    //
    //  Issue fix: prefab is stored in _deviceRowPrefab (SerializeField).
    //  Unity occasionally drops a public field reference when switching
    //  scenes; SerializeField on a private backing field is more stable.
    // ─────────────────────────────────────────

    // All device types that should always appear in the table.
    private static readonly string[] AllDeviceTypes = { "StopSign", "SpeedBump", "TrafficLight" };

    private void PopulateDeviceTable(List<DeviceEffectivenessEntry> entries)
    {
        if (deviceTableParent == null)
        {
            Debug.LogWarning("[LevelResultsManager] deviceTableParent is null — skipping table.");
            return;
        }
        if (_deviceRowPrefab == null)
        {
            Debug.LogError("[LevelResultsManager] _deviceRowPrefab is null — cannot build table.");
            return;
        }

        // Clear old rows.
        foreach (Transform child in deviceTableParent)
            Destroy(child.gameObject);

        // ── Header row ──
        GameObject headerRow = Instantiate(_deviceRowPrefab, deviceTableParent);
        TMP_Text[] headerTexts = headerRow.GetComponentsInChildren<TMP_Text>();
        if (headerTexts.Length >= 3)
        {
            headerTexts[0].text = "Device Type";
            headerTexts[1].text = "Quantity";
            headerTexts[2].text = "Effectiveness (%)";

            // Bold the header labels.
            headerTexts[0].fontStyle = FontStyles.Bold;
            headerTexts[1].fontStyle = FontStyles.Bold;
            headerTexts[2].fontStyle = FontStyles.Bold;
        }

        // ── Build a lookup from the payload entries ──
        var lookup = new Dictionary<string, DeviceEffectivenessEntry>();
        if (entries != null)
            foreach (var e in entries)
                lookup[e.deviceType] = e;

        // ── Always show every device type; default to 0 / 0% ──
        foreach (string deviceType in AllDeviceTypes)
        {
            int count = 0;
            float eff = 0f;

            if (lookup.TryGetValue(deviceType, out DeviceEffectivenessEntry found))
            {
                count = found.placedCount;
                eff = found.effectivenessPercent;
            }

            GameObject row = Instantiate(_deviceRowPrefab, deviceTableParent);
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();
            if (texts.Length >= 3)
            {
                texts[0].text = deviceType;
                texts[1].text = count.ToString();
                texts[2].text = $"{eff:F1}%";
            }
        }
    }

    // ─────────────────────────────────────────
    //  ACCIDENT TREND GRAPH  (XCharts)
    //
    //  • ClearData() first so Retry / scene-reload redraws cleanly.
    //  • Day 0 is prepended with the level's startAccidentRate.
    //  • X-axis interval chosen from {2, 5, 10} for clean labels.
    //  • Line serie drawn with LineType.Normal + circle symbols.
    // ─────────────────────────────────────────

    private void DrawAccidentTrendXCharts(List<AccidentSnapshot> snapshots,
                                          int startAccidentRate = -1)
    {
        if (accidentLineChart == null || snapshots == null || snapshots.Count == 0)
            return;

        // ── Clear previous data so Retry / scene-reload redraws cleanly ──
        // ── Wipe all series and start fresh ──
        accidentLineChart.ClearData();  // clears series AND data
        Line lineSerie = accidentLineChart.AddSerie<Line>("Accident Rate");
        if (lineSerie != null)
        {
            lineSerie.show = true;
            lineSerie.lineType = LineType.Normal;
            lineSerie.lineStyle.show = true;
            lineSerie.lineStyle.width = 2f;
            lineSerie.lineStyle.opacity = 1f;
            lineSerie.symbol.show = true;
            lineSerie.symbol.type = SymbolType.Circle;
            lineSerie.symbol.size = 6f;

            // ✅ Disable animation so line isn't invisible mid-transition
            lineSerie.animation.enable = false;
        }

        // ── Prepend day 0 with the level's starting accident rate ──
        if (startAccidentRate >= 0)
            accidentLineChart.AddData(0, 0, startAccidentRate);

        // ── Add snapshot data ──
        int maxDay = 0;
        foreach (var snap in snapshots)
        {
            accidentLineChart.AddData(0, snap.day, snap.accidentRate);
            if (snap.day > maxDay) maxDay = snap.day;
        }

        // ── Configure x-axis interval (only 2/5/10, 4–10 labels) ──
        ConfigureXAxisInterval(maxDay);

        accidentLineChart.RefreshChart();
        accidentLineChart.SetVerticesDirty();
    }

    // ─────────────────────────────────────────
    //  TESTING — sample graph
    //
    //  Toggle testMode in the Inspector (Play mode or before Play).
    //  The sample data rises to a peak then drops to zero, mirroring
    //  a typical gameplay curve so the chart behaviour is visible
    //  without needing a real level run.
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

        Debug.Log("[LevelResultsManager] TEST MODE — drawing sample accident trend graph.");
        DrawAccidentTrendXCharts(samples, 2);

        // Fill in placeholder UI so the rest of the screen isn't blank.
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
    //  UPLOAD LEVEL RESULT
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
                    Debug.Log($"[LevelResultsManager] Result uploaded. Server id: {response?.id}");
            });
    }
}

// ─────────────────────────────────────────────────────────────────
//  DTOs
// ─────────────────────────────────────────────────────────────────

[System.Serializable]
public class LevelResultRequest
{
    public int playerId;
    public int levelNumber;
    public int safetyScore;
    public int daysUsed;
}

[System.Serializable]
public class LevelResultResponse
{
    public int id;
}

[System.Serializable]
public class PersonalRecordResponse
{
    public int highestScore;
    public int rank;
}