using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CAR ROUTE
//
//  Attach to a child GameObject under CarManager.
//  Defines one directed lane that cars follow (start → end).
//
//  Waypoints can be:
//    (a) Manually assigned Transform list in the Inspector, OR
//    (b) Auto-built from a RoadSection's child RoadTile centres
//        by calling BuildFromSection() at runtime.
//
//  CarManager reads WaypointCount / GetWaypoint() to drive CarAgents.
// ─────────────────────────────────────────────────────────────────

public enum RouteRoadType { RoadSection, MainRoad }

public class CarRoute : MonoBehaviour
{
    // ── Identity ──────────────────────────────
    [Header("Route Identity")]
    public string routeID = "Route_00";
    public RouteRoadType roadType = RouteRoadType.RoadSection;

    // ── Source ────────────────────────────────
    [Header("Waypoint Source")]
    [Tooltip("Assign Transforms manually, OR leave empty and call BuildFromSection() at runtime.")]
    public List<Transform> manualWaypoints = new List<Transform>();

    [Tooltip("If set, BuildFromSection() will auto-order tiles by their world position " +
             "along the section's first tile's forward axis.")]
    public RoadSection sourceSection;

    // ── Spawn / Despawn Points ────────────────
    [Header("Spawn & Despawn")]
    [Tooltip("Cars spawn here (world position). If null, uses first waypoint.")]
    public Transform spawnPoint;

    [Tooltip("Cars are despawned when they reach this point. If null, uses last waypoint.")]
    public Transform despawnPoint;

    // ── Pool Settings ─────────────────────────
    [Header("Pool Settings")]
    [Tooltip("How many car slots are active on this route at any time.")]
    [Min(1)] public int concurrentCars = 2;

    [Tooltip("Seconds between a despawn event and the next spawn on this route.")]
    [Min(0f)] public float respawnDelay = 3f;

    // ── Runtime ───────────────────────────────
    private List<Vector3> _builtWaypoints = new List<Vector3>();

    public int WaypointCount => manualWaypoints.Count > 0
        ? manualWaypoints.Count
        : _builtWaypoints.Count;

    public Vector3 GetWaypoint(int index)
    {
        if (manualWaypoints.Count > 0)
        {
            if (index < 0 || index >= manualWaypoints.Count) return transform.position;
            return manualWaypoints[index] != null
                ? manualWaypoints[index].position
                : transform.position;
        }
        if (index < 0 || index >= _builtWaypoints.Count) return transform.position;
        return _builtWaypoints[index];
    }

    public Vector3 SpawnPosition  => spawnPoint  != null ? spawnPoint.position  : GetWaypoint(0);
    public Vector3 DespawnPosition => despawnPoint != null ? despawnPoint.position : GetWaypoint(WaypointCount - 1);

    // ─────────────────────────────────────────
    //  AUTO-BUILD FROM SECTION
    // ─────────────────────────────────────────

    /// <summary>
    /// Populates _builtWaypoints from the tiles of sourceSection,
    /// ordered along the section's forward axis.
    /// Call this from CarManager.Start() for section-based routes.
    /// </summary>
    public void BuildFromSection()
    {
        if (sourceSection == null)
        {
            Debug.LogWarning($"[CarRoute] {routeID}: sourceSection not assigned.");
            return;
        }

        _builtWaypoints.Clear();

        var tiles = new List<RoadTile>(sourceSection.ChildTiles);
        if (tiles.Count == 0) return;

        // Use the first tile's forward axis to determine sort direction.
        Vector3 axis = tiles[0].transform.TransformDirection(tiles[0].LocalForward);

        tiles.Sort((a, b) =>
        {
            float da = Vector3.Dot(a.transform.position, axis);
            float db = Vector3.Dot(b.transform.position, axis);
            return da.CompareTo(db);
        });

        foreach (var t in tiles)
            _builtWaypoints.Add(t.transform.position);

        Debug.Log($"[CarRoute] {routeID}: built {_builtWaypoints.Count} waypoints from section '{sourceSection.name}'.");
    }

    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        int count = WaypointCount;
        if (count == 0) return;

        Color lineCol = roadType == RouteRoadType.MainRoad
            ? new Color(0.9f, 0.6f, 0.1f, 0.8f)
            : new Color(0.2f, 0.8f, 1.0f, 0.8f);

        Gizmos.color = lineCol;

        for (int i = 0; i < count; i++)
        {
            Vector3 wp = GetWaypoint(i);
            Gizmos.DrawSphere(wp + Vector3.up * 0.3f, 0.25f);

            if (i < count - 1)
                Gizmos.DrawLine(wp + Vector3.up * 0.3f,
                                GetWaypoint(i + 1) + Vector3.up * 0.3f);
        }

        // Spawn / despawn markers
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(SpawnPosition + Vector3.up * 0.5f, 0.35f);

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(DespawnPosition + Vector3.up * 0.5f, 0.35f);

        UnityEditor.Handles.Label(
            GetWaypoint(0) + Vector3.up * 1f,
            $"{routeID} [{roadType}]\n{count} waypoints | ×{concurrentCars} cars"
        );
    }
#endif
}
