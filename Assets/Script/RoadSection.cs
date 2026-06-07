using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD SECTION
//
//  Attach to any parent GameObject that has RoadTile children
//  (any depth). At Awake it harvests all child tiles, assigns
//  itself to them, and registers with GameManager so its
//  per-day mechanics tick.
//
//  Per-day mechanics (forwarded by GameManager each in-game day):
//    • _sectionAccidentRate += dailyAccidentGain
//    • _sectionAccidentRate -= perCorrectDeviceReduction × (correct device count)
//    • Happiness loss this day = _sectionAccidentRate × happinessPerAccidentRate
//
//  One-time mechanic:
//    • When a tile in this section places a device that pushes the
//      total device count over `maxTotalDevices`, a random
//      happiness penalty in `overThresholdPenaltyRange` fires.
//    • Penalty re-arms once the section drops back to <= threshold.
// ─────────────────────────────────────────────────────────────────

public class RoadSection : MonoBehaviour
{
    [Header("Daily Accident Mechanics")]
    [Tooltip("Accident rate this section gains every in-game day.")]
    public float dailyAccidentGain = 2f;

    [Tooltip("Accident rate removed per CORRECT device on this section's tiles, per day.")]
    public float perCorrectDeviceReduction = 2f;

    [Tooltip("Happiness lost per +1 of this section's accident rate, per day.")]
    public float happinessPerAccidentRate = 3f;

    [Header("Device Count Threshold")]
    [Tooltip("Total devices (across all tiles in this section) allowed before the over-threshold penalty fires.")]
    public int maxTotalDevices = 10;

    [Tooltip("Random one-time happiness penalty magnitude (X=min, Y=max) when device count exceeds the threshold.")]
    public Vector2 overThresholdPenaltyRange = new Vector2(10f, 15f);

    [Header("Runtime (read-only)")]
    [SerializeField] private float _sectionAccidentRate = 0f;
    [SerializeField] private List<RoadTile> _childTiles = new List<RoadTile>();
    [SerializeField] private bool _hasAppliedThresholdPenalty = false;

    public float SectionAccidentRate => _sectionAccidentRate;
    public IReadOnlyList<RoadTile> ChildTiles => _childTiles;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        _childTiles.Clear();
        _childTiles.AddRange(GetComponentsInChildren<RoadTile>(includeInactive: true));
        foreach (var t in _childTiles) t.AssignSection(this);
    }

    private void Start()
    {
        StartCoroutine(RegisterWhenReady());
    }

    private IEnumerator RegisterWhenReady()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.RegisterRoadSection(this);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterRoadSection(this);
    }

    // ─────────────────────────────────────────
    //  TILE REGISTRATION  (used by generator-spawned tiles)
    // ─────────────────────────────────────────

    public void RegisterTile(RoadTile tile)
    {
        if (!_childTiles.Contains(tile))
        {
            _childTiles.Add(tile);
            tile.AssignSection(this);
        }
    }

    public void UnregisterTile(RoadTile tile)
    {
        _childTiles.Remove(tile);
    }

    // ─────────────────────────────────────────
    //  AGGREGATES
    // ─────────────────────────────────────────

    public int CountTotalDevices()
    {
        int n = 0;
        foreach (var t in _childTiles)
            if (t != null) n += t.PlacedCount;
        return n;
    }

    public int CountCorrectDevices()
    {
        int n = 0;
        foreach (var t in _childTiles)
            if (t != null) n += t.CountCorrectSlots();
        return n;
    }

    // ─────────────────────────────────────────
    //  DAILY TICK  (called by GameManager once per in-game day)
    // ─────────────────────────────────────────

    /// <summary>
    /// Advances this section by one day.
    /// Returns the happiness delta the section produced (always <=0).
    /// </summary>
    public float TickDay()
    {
        _sectionAccidentRate += dailyAccidentGain;
        _sectionAccidentRate -= perCorrectDeviceReduction * CountCorrectDevices();
        _sectionAccidentRate = Mathf.Max(0f, _sectionAccidentRate);

        return -_sectionAccidentRate * happinessPerAccidentRate;
    }

    // ─────────────────────────────────────────
    //  OVER-THRESHOLD CHECK  (called by tiles after placement)
    // ─────────────────────────────────────────

    /// <summary>
    /// Call after a device is placed on a child tile.
    /// Returns a (negative) happiness penalty if the threshold was just crossed,
    /// or 0 if no penalty applies. Penalty fires at most once until the section
    /// drops back to <= threshold.
    /// </summary>
    public float CheckOverThresholdPenalty()
    {
        int total = CountTotalDevices();

        if (total <= maxTotalDevices)
        {
            _hasAppliedThresholdPenalty = false;
            return 0f;
        }

        if (_hasAppliedThresholdPenalty) return 0f;

        float magnitude = Random.Range(overThresholdPenaltyRange.x, overThresholdPenaltyRange.y);
        _hasAppliedThresholdPenalty = true;
        return -magnitude;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (_childTiles == null || _childTiles.Count == 0) return;

        // Lasso the child tiles with a faint outline
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool hasAny = false;
        foreach (var t in _childTiles)
        {
            if (t == null) continue;
            var col = t.GetComponent<BoxCollider>();
            if (col == null) continue;
            Vector3 world = t.transform.TransformPoint(col.center);
            if (!hasAny) { b = new Bounds(world, col.size); hasAny = true; }
            else b.Encapsulate(new Bounds(world, col.size));
        }
        if (!hasAny) return;

        Gizmos.color = new Color(1f, 1f, 0.2f, 0.4f);
        Gizmos.DrawWireCube(b.center, b.size + Vector3.one * 0.5f);

        UnityEditor.Handles.Label(
            b.center + Vector3.up * 1.5f,
            $"{name}\n" +
            $"Accident rate: {_sectionAccidentRate:F1}\n" +
            $"Devices: {CountTotalDevices()}/{maxTotalDevices} " +
            $"({CountCorrectDevices()} correct)"
        );
    }
#endif
}