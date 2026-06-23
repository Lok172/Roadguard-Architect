using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD MANAGER
//
//  Attach to a parent GameObject (named "RoadManager") whose
//  children include all RoadSection objects.
//
//  Owns the global daily-accident-mechanic parameters and the
//  complexity multiplier.  Each in-game day, GameManager calls
//  TickAllSections(), which iterates every child section with:
//
//      rate += dailyAccidentGain
//      rate -= perCorrectDeviceReduction * correctCount * complexityMultiplier
//
//  and sums the resulting happiness penalties.
// ─────────────────────────────────────────────────────────────────

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

    [Tooltip("Accident rate removed per CORRECT device per section per day (before complexity).")]
    public float perCorrectDeviceReduction = 2f;

    [Tooltip("Happiness lost per +1 of a section's accident rate, per day.")]
    public float happinessPerAccidentRate = 3f;

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
    //  DAILY TICK  (called by GameManager)
    // ─────────────────────────────────────────

    /// <summary>
    /// Advances every child section by one day.
    /// Returns the total happiness delta (always &lt;= 0).
    /// </summary>
    public float TickAllSections()
    {
        float effectiveReduction = perCorrectDeviceReduction * complexityMultiplier;
        float totalDelta = 0f;

        foreach (var s in _sections)
        {
            if (s == null) continue;
            totalDelta += s.TickDay(dailyAccidentGain, effectiveReduction, happinessPerAccidentRate);
        }

        return totalDelta;
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