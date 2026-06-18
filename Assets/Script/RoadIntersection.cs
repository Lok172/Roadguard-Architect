using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD INTERSECTION  (v3 — simplified junction)
//
//  Represents a node in the road graph.
//  Place one at every junction / end-point in your city.
//
//  Neighbours are assigned automatically by RoadSegment on Awake,
//  or manually via the Inspector list for editor-time previews.
//
//  IntersectionGenerator can create these automatically.
//
//  JUNCTION SYSTEM (v4 — predefined-path):
//    Mark isJunction = true on shared intersections where two road
//    networks meet. Junctions are still meaningful as graph nodes, but
//    cars no longer pick a random direction here: each car follows the
//    A* path it was given at spawn (computed once, no real-time A*),
//    so it takes whichever connected segment its predefined path uses.
// ─────────────────────────────────────────────────────────────────

public class RoadIntersection : MonoBehaviour
{
    [Header("Identity")]
    public string intersectionID = "Intersection_00";

    // ── Junction Settings ─────────────────────
    [Header("Junction Settings")]
    [Tooltip("Mark this intersection as a junction point where two road " +
             "networks meet. Cars arriving here randomly pick one of the " +
             "available directions to continue their journey.")]
    public bool isJunction = false;

    [Header("Connected Segments (auto-populated at runtime)")]
    [SerializeField] private List<RoadSegment> _connectedSegments = new List<RoadSegment>();
    public IReadOnlyList<RoadSegment> ConnectedSegments => _connectedSegments;

    // ── Graph helpers ─────────────────────────

    public void RegisterSegment(RoadSegment seg)
    {
        if (!_connectedSegments.Contains(seg))
            _connectedSegments.Add(seg);
    }

    public void UnregisterSegment(RoadSegment seg)
    {
        _connectedSegments.Remove(seg);
    }

    /// <summary>
    /// Returns all intersections reachable in one hop from this node
    /// (undirected: both endpoints of every connected segment).
    /// </summary>
    public List<RoadIntersection> GetNeighbours()
    {
        var result = new List<RoadIntersection>();
        foreach (var seg in _connectedSegments)
        {
            if (seg == null) continue;
            var other = seg.Other(this);
            if (other == null) continue;
            // Directional: this lane runs (this → other). The opposite lane
            // being blocked must NOT remove this neighbour.
            if (seg.IsBlockedToward(other)) continue;
            if (!result.Contains(other)) result.Add(other);
        }
        return result;
    }

    /// <summary>
    /// Returns the segment connecting this node to <paramref name="neighbour"/>, or null.
    /// </summary>
    public RoadSegment SegmentTo(RoadIntersection neighbour)
    {
        foreach (var seg in _connectedSegments)
        {
            if (seg == null) continue;
            if (seg.Other(this) == neighbour) return seg;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Junction nodes get a larger cyan wireframe sphere.
        if (isJunction)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.7f);
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.2f,
                $"{intersectionID} [JUNCTION]"
            );
        }
        else
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.4f);
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.8f,
                intersectionID
            );
        }
    }
#endif
}