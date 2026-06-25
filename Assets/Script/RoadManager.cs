using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class RoadManager : MonoBehaviour
{
    // ── Complexity ────────────────────────────
    [Header("Complexity")]
    [Tooltip("Scales per-correct-device accident-rate reduction across all sections. " +
             ">1 = more reduction (easier), <1 = less reduction (harder).")]
    [Min(0.01f)] public float complexityMultiplier = 1f;

    // ── Daily Accident Mechanics ──────────────
    [Header("Daily Accident Mechanics")]
    [Tooltip("Accident rate every section gains each in-game day.")]
    public float dailyAccidentGain = 2f;

    [Tooltip("Accident rate removed per CORRECT device per section per day (before complexity). " +
             "Each device type has its own value — see 'Per-Device Accident Reduction' below.")]
    [System.Obsolete("Use perDeviceAccidentReduction instead.")]
    public float perCorrectDeviceReduction = 2f;

    [Tooltip("Happiness lost per +1 of a section's accident rate, per day.")]
    public float happinessPerAccidentRate = 3f;

    // ── Per-Device Accident Reduction ─────────
    [Header("Per-Device Accident Reduction (per correct device, per section, per day — before complexity)")]
    [Tooltip("Accident rate reduction per correct Stop Sign.")]
    [Min(0f)] public float reductionStopSign = 0.5f;

    [Tooltip("Accident rate reduction per correct Speed Bump.")]
    [Min(0f)] public float reductionSpeedBump = 1f;

    [Tooltip("Accident rate reduction per correct Traffic Light.")]
    [Min(0f)] public float reductionTrafficLight = 4f;

    // ── Per-Device Placement Happiness Bonus ──
    [Header("Per-Device Placement Happiness Bonus (awarded on each CORRECT placement)")]
    [Tooltip("Happiness gained when a Stop Sign is placed correctly.")]
    [Min(0f)] public float placementHappinessStopSign = 2f;

    [Tooltip("Happiness gained when a Speed Bump is placed correctly.")]
    [Min(0f)] public float placementHappinessSpeedBump = 4f;

    [Tooltip("Happiness gained when a Traffic Light is placed correctly.")]
    [Min(0f)] public float placementHappinessTrafficLight = 10f;

    // ── Runtime ───────────────────────────────
    [Header("Runtime (read-only)")]
    [SerializeField] private List<RoadSection> _sections = new List<RoadSection>();

    public IReadOnlyList<RoadSection> Sections => _sections;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        // Harvest all child RoadSections (any depth).
        _sections.Clear();
        _sections.AddRange(GetComponentsInChildren<RoadSection>(includeInactive: true));
    }

    private void Start()
    {
        StartCoroutine(RegisterWhenReady());
    }

    private IEnumerator RegisterWhenReady()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.RegisterRoadManager(this);
        Debug.Log($"[RoadManager] Registered with GameManager. Sections: {_sections.Count}  " +
                  $"Complexity: {complexityMultiplier}");
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterRoadManager(this);
    }

    // ─────────────────────────────────────────
    //  SECTION REGISTRATION  (for dynamically-spawned sections)
    // ─────────────────────────────────────────

    public void RegisterSection(RoadSection section)
    {
        if (!_sections.Contains(section))
            _sections.Add(section);
    }

    public void UnregisterSection(RoadSection section)
    {
        _sections.Remove(section);
    }

    // ─────────────────────────────────────────
    //  PLACEMENT HAPPINESS
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the happiness bonus awarded when the given device type
    /// is placed correctly. Returns 0 for incorrect/unknown types.
    /// </summary>
    public float GetPlacementHappiness(TrafficDeviceType device) => device switch
    {
        TrafficDeviceType.StopSign => placementHappinessStopSign,
        TrafficDeviceType.SpeedBump => placementHappinessSpeedBump,
        TrafficDeviceType.TrafficLight => placementHappinessTrafficLight,
        _ => 0f
    };

    // ─────────────────────────────────────────
    //  DAILY TICK  (called by GameManager)
    // ─────────────────────────────────────────

    /// <summary>
    /// Advances every child section by one day.
    /// Returns the total happiness delta (always &lt;= 0).
    ///
    /// Per-device accident reduction is applied by computing a weighted
    /// effective reduction per section (sum of correct-device counts ×
    /// their individual reduction values × complexityMultiplier), then
    /// passing that single value to RoadSection.TickDay so the existing
    /// RoadSection contract is preserved.
    /// </summary>
    public float TickAllSections()
    {
        float totalDelta = 0f;

        foreach (var s in _sections)
        {
            if (s == null) continue;

            // Count correct devices by type across all tiles in this section.
            int correctStopSigns = 0;
            int correctSpeedBumps = 0;
            int correctTrafficLights = 0;

            foreach (var tile in s.ChildTiles)
            {
                if (tile == null) continue;
                foreach (var slot in tile.Slots)
                {
                    if (!tile.IsSlotCorrect(slot)) continue;
                    switch (slot.deviceType)
                    {
                        case TrafficDeviceType.StopSign: correctStopSigns++; break;
                        case TrafficDeviceType.SpeedBump: correctSpeedBumps++; break;
                        case TrafficDeviceType.TrafficLight: correctTrafficLights++; break;
                    }
                }
            }

            // Weighted effective reduction for this section.
            float effectiveReduction =
                (correctStopSigns * reductionStopSign +
                 correctSpeedBumps * reductionSpeedBump +
                 correctTrafficLights * reductionTrafficLight)
                * complexityMultiplier;

            totalDelta += s.TickDay(GetRampedAccidentGain(), effectiveReduction, happinessPerAccidentRate);
        }

        return totalDelta;
    }

    // ─────────────────────────────────────────
    //  ACCIDENT GAIN RAMP
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the effective dailyAccidentGain for the current day.
    /// Linearly interpolates from GameManager.rampAccidentGainMin to
    /// rampAccidentGainMax over rampDurationDays, then locks at max.
    /// Falls back to the local dailyAccidentGain field if GameManager
    /// is unavailable.
    /// </summary>
    private float GetRampedAccidentGain()
    {
        var gm = GameManager.Instance;
        if (gm == null) return dailyAccidentGain;

        int day = gm.DaysPassed;
        int rampDays = gm.RampDurationDays;
        float min = gm.RampAccidentGainMin;
        float max = gm.RampAccidentGainMax;

        if (day >= rampDays) return max;
        return Mathf.Lerp(min, max, (float)day / rampDays);
    }

    // ─────────────────────────────────────────
    //  AGGREGATES  (used by GameManager)
    // ─────────────────────────────────────────

    /// <summary>Sum of all section accident rates.</summary>
    public float GetTotalSectionAccidentRate()
    {
        float sum = 0f;
        foreach (var s in _sections)
            if (s != null) sum += s.SectionAccidentRate;
        return sum;
    }

    /// <summary>Total devices across every section.</summary>
    public int GetTotalDeviceCount()
    {
        int n = 0;
        foreach (var s in _sections)
            if (s != null) n += s.CountTotalDevices();
        return n;
    }

    /// <summary>Total correct devices across every section.</summary>
    public int GetTotalCorrectDeviceCount()
    {
        int n = 0;
        foreach (var s in _sections)
            if (s != null) n += s.CountCorrectDevices();
        return n;
    }

    // ─────────────────────────────────────────
    //  DEVICE EFFECTIVENESS  (called by GameManager on end-game)
    // ─────────────────────────────────────────

    /// <summary>
    /// Iterates all sections/tiles to compute per-device-type effectiveness
    /// and writes DeviceEffectivenessEntry records into the provided list.
    /// Called by GameManager.FinaliseAndSubmitPayload().
    /// </summary>
    public void PopulateDeviceEffectiveness(List<DeviceEffectivenessEntry> list)
    {
        if (list == null) return;
        list.Clear();

        var totals = new Dictionary<TrafficDeviceType, int>();
        var correct = new Dictionary<TrafficDeviceType, int>();

        foreach (var section in _sections)
        {
            if (section == null) continue;
            foreach (var tile in section.ChildTiles)
            {
                if (tile == null) continue;
                foreach (var slot in tile.Slots)
                {
                    if (slot.deviceType == TrafficDeviceType.None) continue;

                    if (!totals.ContainsKey(slot.deviceType))
                    {
                        totals[slot.deviceType] = 0;
                        correct[slot.deviceType] = 0;
                    }

                    totals[slot.deviceType]++;
                    if (tile.IsSlotCorrect(slot))
                        correct[slot.deviceType]++;
                }
            }
        }

        foreach (var kv in totals)
        {
            int t = kv.Value;
            int c = correct[kv.Key];
            list.Add(new DeviceEffectivenessEntry
            {
                deviceType = kv.Key.ToString(),
                placedCount = t,
                effectivenessPercent = t > 0 ? (float)c / t * 100f : 0f
            });
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_sections == null || _sections.Count == 0) return;

        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool hasAny = false;
        foreach (var s in _sections)
        {
            if (s == null) continue;
            foreach (var t in s.ChildTiles)
            {
                if (t == null) continue;
                var col = t.GetComponent<BoxCollider>();
                if (col == null) continue;
                Vector3 w = t.transform.TransformPoint(col.center);
                if (!hasAny) { b = new Bounds(w, col.size); hasAny = true; }
                else b.Encapsulate(new Bounds(w, col.size));
            }
        }
        if (!hasAny) return;

        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.35f);
        Gizmos.DrawWireCube(b.center, b.size + Vector3.one * 1f);

        float totalRate = GetTotalSectionAccidentRate();
        UnityEditor.Handles.Label(
            b.center + Vector3.up * 2.5f,
            $"[RoadManager]\n" +
            $"Sections: {_sections.Count}  Complexity: {complexityMultiplier}\n" +
            $"Total accident rate: {totalRate:F1}\n" +
            $"Devices: {GetTotalDeviceCount()} ({GetTotalCorrectDeviceCount()} correct)"
        );
    }
#endif
}