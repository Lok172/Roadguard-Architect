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
//
//  JUNCTION SYSTEM (v2):
//    Mark isJunction = true on shared intersections where two road
//    networks meet. Cars that need to TURN at a junction must
//    reserve it first; straight-through traffic passes freely.
//    Only one turning car may occupy a junction at a time.
// ─────────────────────────────────────────────────────────────────

public class RoadIntersection : MonoBehaviour
{
    [Header("Identity")]
    public string intersectionID = "Intersection_00";

    // ── Junction Settings ─────────────────────
    [Header("Junction Settings")]
    [Tooltip("Mark this intersection as a junction point where two road " +
             "networks meet. Turning cars must reserve the junction before " +
             "entering; straight-through cars pass freely. No crash scenes " +
             "are spawned on segments that touch a junction.")]
    public bool isJunction = false;

    [Tooltip("Angle threshold (degrees) above which a direction change " +
             "at this junction counts as a 'turn' requiring reservation. " +
             "Below this angle the car is considered to be going straight.")]
    [Range(5f, 90f)] public float turnAngleThreshold = 30f;

    [Header("Connected Segments (auto-populated at runtime)")]
    [SerializeField] private List<RoadSegment> _connectedSegments = new List<RoadSegment>();
    public IReadOnlyList<RoadSegment> ConnectedSegments => _connectedSegments;

    // ── Junction reservation ──────────────────
    private CarAgent _junctionReservedBy;

    /// <summary>True if a turning car currently holds this junction.</summary>
    public bool IsJunctionInUse => _junctionReservedBy != null;

    /// <summary>
    /// Attempts to reserve this junction for the given car.
    /// Returns true if the reservation succeeded (junction was free or
    /// already held by this car). Returns false if another car holds it.
    /// </summary>
    public bool TryReserveJunction(CarAgent car)
    {
        if (!isJunction) return true;   // not a junction — always pass
        if (_junctionReservedBy == null || _junctionReservedBy == car)
        {
            _junctionReservedBy = car;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Releases the junction reservation held by the given car.
    /// Safe to call even if the car does not hold the reservation.
    /// </summary>
    public void ReleaseJunction(CarAgent car)
    {
        if (_junctionReservedBy == car)
            _junctionReservedBy = null;
    }

    /// <summary>
    /// Determines whether travelling from <paramref name="incomingSeg"/>
    /// through this node to <paramref name="outgoingSeg"/> constitutes a
    /// turn (true) or straight-through movement (false), based on the
    /// angle between the two directions.
    /// </summary>
    public bool IsTurn(RoadSegment incomingSeg, RoadSegment outgoingSeg)
    {
        if (incomingSeg == null || outgoingSeg == null) return false;

        RoadIntersection inFrom = incomingSeg.Other(this);
        RoadIntersection outTo = outgoingSeg.Other(this);
        if (inFrom == null || outTo == null) return false;

        Vector3 inDir = (transform.position - inFrom.transform.position).normalized;
        Vector3 outDir = (outTo.transform.position - transform.position).normalized;

        float angle = Vector3.Angle(inDir, outDir);
        return angle > turnAngleThreshold;
    }

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
        // Junction nodes get a larger cyan wireframe sphere.
        if (isJunction)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.7f);
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 1.2f,
                $"{intersectionID} [JUNCTION]"
            );

            if (IsJunctionInUse)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
                Gizmos.DrawSphere(transform.position, 0.5f);
            }
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