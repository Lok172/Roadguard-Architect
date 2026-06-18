using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  ROAD GRAPH
//
//  Stateless A* pathfinder over RoadIntersection nodes.
//  Used by CarAgent (per car) and CarManager (rerouting).
//
//  Heuristic: straight-line (Euclidean) distance.
//  Edge cost : segment length. A segment is skipped only when the
//              lane in the direction of travel is blocked (per-lane).
// ─────────────────────────────────────────────────────────────────

public static class RoadGraph
{
    // ─────────────────────────────────────────
    //  A*  PATHFINDING
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns an ordered list of intersections from <paramref name="start"/> to
    /// <paramref name="goal"/>, inclusive. Returns empty list if no path exists.
    /// Blocked segments are skipped automatically.
    /// </summary>
    public static List<RoadIntersection> FindPath(
        RoadIntersection start,
        RoadIntersection goal)
    {
        if (start == null || goal == null) return new List<RoadIntersection>();
        if (start == goal) return new List<RoadIntersection> { start };

        // Node record
        var gScore = new Dictionary<RoadIntersection, float>();
        var fScore = new Dictionary<RoadIntersection, float>();
        var cameFrom = new Dictionary<RoadIntersection, RoadIntersection>();
        var openSet = new HashSet<RoadIntersection>();
        var closedSet = new HashSet<RoadIntersection>();

        gScore[start] = 0f;
        fScore[start] = Heuristic(start, goal);
        openSet.Add(start);

        while (openSet.Count > 0)
        {
            RoadIntersection current = LowestF(openSet, fScore);

            if (current == goal)
                return ReconstructPath(cameFrom, current);

            openSet.Remove(current);
            closedSet.Add(current);

            foreach (var neighbour in current.GetNeighbours())
            {
                if (closedSet.Contains(neighbour)) continue;

                RoadSegment seg = current.SegmentTo(neighbour);
                // Directional: only the lane we'd travel (current → neighbour)
                // matters. A block on the opposite lane leaves this edge usable.
                if (seg == null || seg.IsBlockedToward(neighbour)) continue;

                float tentativeG = GetOrInfinity(gScore, current) + seg.Length;

                if (tentativeG < GetOrInfinity(gScore, neighbour))
                {
                    cameFrom[neighbour] = current;
                    gScore[neighbour] = tentativeG;
                    fScore[neighbour] = tentativeG + Heuristic(neighbour, goal);
                    openSet.Add(neighbour);
                }
            }
        }

        // No path found
        return new List<RoadIntersection>();
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    private static float Heuristic(RoadIntersection a, RoadIntersection b)
        => Vector3.Distance(a.transform.position, b.transform.position);

    private static float GetOrInfinity(Dictionary<RoadIntersection, float> dict, RoadIntersection key)
        => dict.TryGetValue(key, out float v) ? v : float.MaxValue;

    private static RoadIntersection LowestF(
        HashSet<RoadIntersection> openSet,
        Dictionary<RoadIntersection, float> fScore)
    {
        RoadIntersection best = null;
        float bestF = float.MaxValue;
        foreach (var node in openSet)
        {
            float f = GetOrInfinity(fScore, node);
            if (f < bestF) { bestF = f; best = node; }
        }
        return best;
    }

    private static List<RoadIntersection> ReconstructPath(
        Dictionary<RoadIntersection, RoadIntersection> cameFrom,
        RoadIntersection current)
    {
        var path = new List<RoadIntersection> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }
        return path;
    }
}
