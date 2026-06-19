using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD SECTION
//
//  Owns a group of child RoadTiles and tracks this section's
//  accident rate.  Daily-tick parameters (gain, reduction,
//  happiness-per-rate) are now held by RoadManager, which calls
//  TickDay() with the values each in-game day.
//
//  Still owns the device-count threshold penalty independently.
// ─────────────────────────────────────────────────────────────────

public class RoadSection : MonoBehaviour
{
    [Header("Device Count Threshold")]
    [Tooltip("Total devices (across all tiles) allowed before the over-threshold penalty fires.")]
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

    // ─────────────────────────────────────────
    //  TILE REGISTRATION  (for dynamically-spawned tiles)
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
    //  DAILY TICK  (called by RoadManager each in-game day)
    // ─────────────────────────────────────────

    /// <summary>
    /// Advances this section by one day using parameters supplied by
    /// <see cref="RoadManager"/>.
    /// Returns the happiness delta (always &lt;= 0).
    /// </summary>
    public float TickDay(float accidentGain, float reductionPerCorrectDevice, float happinessPerRate)
    {
        Debug.Log(
    $"[{name}] Correct={CountCorrectDevices()} " +
    $"Before={_sectionAccidentRate}"
);
        _sectionAccidentRate += accidentGain;
        _sectionAccidentRate -= reductionPerCorrectDevice * CountCorrectDevices();
        _sectionAccidentRate = Mathf.Max(0f, _sectionAccidentRate);
        Debug.Log(
    $"[{name}] After={_sectionAccidentRate}"
);

        return -_sectionAccidentRate * happinessPerRate;
    }

    // ─────────────────────────────────────────
    //  OVER-THRESHOLD CHECK  (called by tiles after placement)
    // ─────────────────────────────────────────

    /// <summary>
    /// Call after a device is placed on a child tile.
    /// Returns a negative happiness penalty if the threshold was just crossed,
    /// or 0 if no penalty applies.  Penalty fires at most once until the
    /// section drops back to &lt;= threshold (then re-arms).
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