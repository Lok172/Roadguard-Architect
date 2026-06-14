using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────
//  ROAD NETWORK GENERATOR
//
//  Attach to an empty GameObject. Configure in the Inspector,
//  then use the editor buttons to generate intersections and/or
//  road segments as children of this object.
//
//  Generate Modes (booleans):
//    generateIntersections  → spawns a line of RoadIntersection nodes
//    generateSegments       → connects consecutive intersections with
//                             RoadSegment objects
//
//  Both can be true at once for a quick straight road layout.
//  After generation, link intersections in non-linear layouts
//  by assigning RoadSegment.intersectionA / B manually.
//
//  CHILD FOLLOWING
//    Generated objects are parented as LOCAL children
//    (worldPositionStays: false), so moving or rotating this
//    generator in the editor moves the entire road network with it.
// ─────────────────────────────────────────────────────────────────

public class RoadNetworkGenerator : MonoBehaviour
{
    // ── Mode ──────────────────────────────────
    [Header("Generate Mode")]
    [Tooltip("Spawn RoadIntersection nodes along the line.")]
    public bool generateIntersections = true;

    [Tooltip("Connect consecutive intersections with RoadSegment objects.")]
    public bool generateSegments = true;

    // ── Layout ────────────────────────────────
    [Header("Layout")]
    [Tooltip("Local-space position of the first intersection relative to this generator.")]
    public Vector3 startLocalPosition = Vector3.zero;

    [Tooltip("Local-space direction the line runs (auto-normalised).")]
    public Vector3 direction = Vector3.forward;

    [Tooltip("Number of intersections to generate.")]
    [Min(2)] public int nodeCount = 4;

    [Tooltip("Distance between intersection centres (local space).")]
    [Min(1f)] public float nodeSpacing = 10f;

    // ── Intersection Defaults ─────────────────
    [Header("Intersection Defaults")]
    [Tooltip("Prefix for intersection IDs. e.g. 'Int' → Int_00, Int_01 …")]
    public string intersectionIDPrefix = "Int";

    // ── Segment Defaults ──────────────────────
    [Header("Segment Defaults")]
    [Tooltip("Prefix for segment IDs. e.g. 'Seg' → Seg_00, Seg_01 …")]
    public string segmentIDPrefix = "Seg";

    [Tooltip("Default speed limit on generated segments.")]
    [Min(1f)] public float defaultSpeedLimit = 10f;

    [Tooltip("Default base risk on generated segments [0..1].")]
    [Range(0f, 1f)] public float defaultBaseRisk = 0.1f;

    [Tooltip("Default density risk factor on generated segments.")]
    [Min(0f)] public float defaultDensityRisk = 0.02f;

    [Tooltip("Default lateral lane offset on generated segments (world units). " +
             "Cars travelling in opposite directions are offset this far from the centre-line.")]
    [Min(0f)] public float defaultLaneOffset = 0.5f;

    // ── Runtime (editor-only tracking) ────────
    [HideInInspector] public List<RoadIntersection> generatedIntersections = new List<RoadIntersection>();
    [HideInInspector] public List<RoadSegment> generatedSegments = new List<RoadSegment>();

    // ─────────────────────────────────────────
    //  GENERATE
    // ─────────────────────────────────────────

    public void Generate()
    {
        Clear();

        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        // ── Step 1: Intersections ──────────────
        if (generateIntersections)
        {
            for (int i = 0; i < nodeCount; i++)
            {
                // Use LOCAL position so the node is offset relative to this generator.
                Vector3 localPos = startLocalPosition + dir * (nodeSpacing * i);

                GameObject go = new GameObject($"{intersectionIDPrefix}_{i:D2}");

                // worldPositionStays: false → localPosition = localPos,
                // so the child moves with the parent automatically.
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = localPos;

                RoadIntersection node = go.AddComponent<RoadIntersection>();
                node.intersectionID = $"{intersectionIDPrefix}_{i:D2}";

                generatedIntersections.Add(node);
            }
        }

        // ── Step 2: Segments ───────────────────
        if (generateSegments && generatedIntersections.Count >= 2)
        {
            for (int i = 0; i < generatedIntersections.Count - 1; i++)
            {
                RoadIntersection a = generatedIntersections[i];
                RoadIntersection b = generatedIntersections[i + 1];

                // Midpoint in local space.
                Vector3 localMid = (a.transform.localPosition + b.transform.localPosition) * 0.5f;

                GameObject go = new GameObject($"{segmentIDPrefix}_{i:D2}");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = localMid;

                RoadSegment seg = go.AddComponent<RoadSegment>();
                seg.segmentID = $"{segmentIDPrefix}_{i:D2}";
                seg.intersectionA = a;
                seg.intersectionB = b;
                seg.speedLimit = defaultSpeedLimit;
                seg.baseRisk = defaultBaseRisk;
                seg.densityRiskFactor = defaultDensityRisk;
                seg.laneOffset = defaultLaneOffset;

                generatedSegments.Add(seg);
            }
        }

        Debug.Log($"[RoadNetworkGenerator] Generated: " +
                  $"{generatedIntersections.Count} intersections, " +
                  $"{generatedSegments.Count} segments.");
    }

    // ─────────────────────────────────────────
    //  CLEAR
    // ─────────────────────────────────────────

    public void Clear()
    {
        generatedIntersections.Clear();
        generatedSegments.Clear();

        var children = new List<GameObject>();
        foreach (Transform child in transform)
            children.Add(child.gameObject);

        foreach (var child in children)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child);
            else
#endif
                Destroy(child);
        }

        Debug.Log("[RoadNetworkGenerator] Cleared.");
    }

    // ─────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (nodeCount < 2) return;

        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        for (int i = 0; i < nodeCount; i++)
        {
            // Draw in world space by converting from local.
            Vector3 localPos = startLocalPosition + dir * (nodeSpacing * i);
            Vector3 worldPos = transform.TransformPoint(localPos);

            if (generateIntersections)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
                Gizmos.DrawSphere(worldPos, 0.4f);
                UnityEditor.Handles.Label(worldPos + Vector3.up * 0.7f,
                    $"{intersectionIDPrefix}_{i:D2}");
            }

            if (generateSegments && i < nodeCount - 1)
            {
                Vector3 nextLocal = startLocalPosition + dir * (nodeSpacing * (i + 1));
                Vector3 nextWorld = transform.TransformPoint(nextLocal);

                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.7f);
                Gizmos.DrawLine(worldPos, nextWorld);

                // Lane offset preview lines
                if (defaultLaneOffset > 0f)
                {
                    Vector3 fwd = (nextWorld - worldPos).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized * defaultLaneOffset;

                    Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
                    Gizmos.DrawLine(worldPos + right, nextWorld + right);

                    Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.4f);
                    Gizmos.DrawLine(worldPos - right, nextWorld - right);
                }

                Vector3 mid = (worldPos + nextWorld) * 0.5f;
                UnityEditor.Handles.Label(mid + Vector3.up * 0.5f,
                    $"{segmentIDPrefix}_{i:D2}");
            }
        }

        // Direction arrow
        Vector3 startWorld = transform.TransformPoint(startLocalPosition);
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(startWorld, transform.TransformDirection(dir) * nodeSpacing * (nodeCount - 1));
    }
#endif
}

// ─────────────────────────────────────────────────────────────────
//  CUSTOM INSPECTOR
// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[CustomEditor(typeof(RoadNetworkGenerator))]
public class RoadNetworkGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RoadNetworkGenerator gen = (RoadNetworkGenerator)target;

        EditorGUILayout.Space(6);

        int segCount = gen.generateSegments
            ? Mathf.Max(0, gen.nodeCount - 1)
            : 0;

        EditorGUILayout.HelpBox(
            $"Will generate:\n" +
            $"  • {(gen.generateIntersections ? gen.nodeCount : 0)} intersection(s)\n" +
            $"  • {segCount} segment(s)\n\n" +
            "Children are parented locally — moving this GameObject moves the entire network.\n\n" +
            "After generation, you can manually link additional segments by setting\n" +
            "RoadSegment.intersectionA / B in the Inspector for non-linear layouts.",
            MessageType.None
        );

        EditorGUILayout.Space(6);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
        if (GUILayout.Button("▶  Generate Network", GUILayout.Height(36)))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Generate Road Network");
            gen.Generate();
            EditorUtility.SetDirty(gen.gameObject);
        }

        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.35f);
        if (GUILayout.Button("✕  Clear Network", GUILayout.Height(28)))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Clear Road Network");
            gen.Clear();
            EditorUtility.SetDirty(gen.gameObject);
        }

        GUI.backgroundColor = Color.white;

        if (gen.generatedIntersections.Count > 0 || gen.generatedSegments.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"Active: {gen.generatedIntersections.Count} intersections, " +
                $"{gen.generatedSegments.Count} segments.",
                MessageType.Info
            );
        }
    }
}
#endif