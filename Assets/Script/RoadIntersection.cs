using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD INTERSECTION
//
//  Represents a node in the road graph.
//  Place one at every junction / end-point in your city.
//
//  Neighbours are assigned automatically by RoadSegment on Awake,
//  or manually via the Inspector list for editor-time previews.
//
//  IntersectionGenerator can create these automatically.
// ─────────────────────────────────────────────────────────────────

public class RoadIntersection : MonoBehaviour
{
    [Header("Identity")]
    public string intersectionID = "Intersection_00";

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
            if (seg == null || seg.IsBlocked) continue;
            var other = seg.Other(this);
            if (other != null && !result.Contains(other))
                result.Add(other);
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
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
        Gizmos.DrawSphere(transform.position, 0.4f);
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.8f,
            intersectionID
        );
    }
#endif
}
