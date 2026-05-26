using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoadTileGenerator : MonoBehaviour
{
    [Header("Layout")]

    [Tooltip("World-space position of the FIRST tile's centre")]
    public Vector3 startPosition = Vector3.zero;

    [Tooltip("Direction the road runs (will be normalised automatically)")]
    public Vector3 direction = Vector3.forward;

    [Tooltip("Number of tiles to generate along the road")]
    public int tileCount = 5;

    [Tooltip("Gap between consecutive tile centres (usually equals tile length)")]
    public float tileSpacing = 5f;
    

    [Header("Collider Size")]

    [Tooltip("X = road width, Y = collider height (keep thin, e.g. 0.1), Z = tile length")]
    public Vector3 colliderSize = new Vector3(4f, 0.1f, 5f);

    [Tooltip("Offset the collider centre relative to the tile pivot (leave at zero for flush to ground)")]
    public Vector3 colliderOffset = Vector3.zero;

    // ── Tile Data Defaults ────────────────────────────────────────
    [Header("Tile Defaults (applied to all generated tiles)")]

    public TileType defaultTileType = TileType.Residential;
    public ZoneType defaultZoneType = ZoneType.Residential;

    [Range(0f, 1f)]
    public float defaultAccidentRate    = 0.3f;

    [Tooltip("Devices that will be allowed on every generated tile")]
    public List<TrafficDeviceType> defaultAllowedDevices = new List<TrafficDeviceType>
    {
        TrafficDeviceType.StopSign,
        TrafficDeviceType.SpeedBump,
        TrafficDeviceType.TrafficLight
    };

    [Tooltip("Prefix for auto-generated tile IDs, e.g. 'ResRoad' → ResRoad_00, ResRoad_01 …")]
    public string tileIDPrefix = "ResRoad";

    // ── Layer / Tag ───────────────────────────────────────────────
    [Header("Layer & Tag")]

    [Tooltip("Layer name for the tile colliders ")]
    public string tileLayer = "RoadTile";

    [Tooltip("Tag for the tile colliders")]
    public string tileTag = "RoadTile";

    [HideInInspector]
    public List<RoadTile> generatedTiles = new List<RoadTile>();

    // ─────────────────────────────────────────────────────────────
    //  GENERATE
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys existing tiles parented here, then spawns fresh ones.
    /// Safe to call at edit-time (via the custom Inspector button) or at runtime.
    /// </summary>
    public void GenerateTiles()
    {
        ClearTiles();

        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        // Resolve layer index once
        int layerIndex = LayerMask.NameToLayer(tileLayer);
        if (layerIndex < 0)
        {
            Debug.LogWarning($"[RoadTileGenerator] Layer '{tileLayer}' not found. " +
                             "Create it in Edit > Project Settings > Tags & Layers.");
            layerIndex = 0;
        }

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 tilePos = startPosition + dir * (tileSpacing * i);

            // ── Create GameObject ──────────────────────────────
            GameObject tileGO = new GameObject($"{tileIDPrefix}_{i:D2}");
            tileGO.transform.position = tilePos;
            tileGO.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            tileGO.transform.SetParent(transform, worldPositionStays: true);
            tileGO.layer = layerIndex;

            // Apply tag safely
            try   { tileGO.tag = tileTag; }
            catch { Debug.LogWarning($"[RoadTileGenerator] Tag '{tileTag}' not found. Add it in Edit > Project Settings > Tags & Layers."); }

            // ── BoxCollider ────────────────────────────────────
            BoxCollider col   = tileGO.AddComponent<BoxCollider>();
            col.size          = colliderSize;
            col.center        = colliderOffset;
            col.isTrigger     = true;

            // ── RoadTile data ──────────────────────────────────
            RoadTile tile               = tileGO.AddComponent<RoadTile>();
            tile.tileID                 = $"{tileIDPrefix}_{i:D2}";
            tile.tileType               = defaultTileType;
            tile.zoneType               = defaultZoneType;
            tile.baseAccidentRate       = defaultAccidentRate;
            tile.allowedDevices         = new List<TrafficDeviceType>(defaultAllowedDevices);

            generatedTiles.Add(tile);
        }

        Debug.Log($"[RoadTileGenerator] Generated {tileCount} tiles along '{tileIDPrefix}' road.");
    }

    // ─────────────────────────────────────────────────────────────
    //  CLEAR
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all child GameObjects (i.e. previously generated tiles).
    /// </summary>
    public void ClearTiles()
    {
        generatedTiles.Clear();

        // Collect children first to avoid modifying collection during iteration
        List<GameObject> children = new List<GameObject>();
        foreach (Transform child in transform)
            children.Add(child.gameObject);

        foreach (GameObject child in children)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child);
            else
#endif
                Destroy(child);
        }

        Debug.Log("[RoadTileGenerator] Cleared all generated tiles.");
    }

    // ─────────────────────────────────────────────────────────────
    //  GIZMOS  — preview before generating
    // ─────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (tileCount <= 0) return;

        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 pos = startPosition + dir * (tileSpacing * i);

            // Ghost tile
            Gizmos.color  = new Color(0.3f, 0.9f, 0.4f, 0.15f);
            Gizmos.matrix = Matrix4x4.TRS(pos + rot * colliderOffset, rot, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, colliderSize);

            // Wire outline
            Gizmos.color  = new Color(0.3f, 0.9f, 0.4f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, colliderSize);

            // Index label
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(pos + Vector3.up * 0.4f, $"{tileIDPrefix}_{i:D2}");
        }

        // Direction arrow from start
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color  = Color.cyan;
        Gizmos.DrawRay(startPosition, dir * tileSpacing * tileCount);
    }
#endif
}

// ─────────────────────────────────────────────────────────────────
//  CUSTOM INSPECTOR
//  Adds "Generate Tiles" and "Clear Tiles" buttons in the Inspector.
// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[CustomEditor(typeof(RoadTileGenerator))]
public class RoadTileGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RoadTileGenerator gen = (RoadTileGenerator)target;

        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
        if (GUILayout.Button("▶  Generate Tiles", GUILayout.Height(36)))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Generate Road Tiles");
            gen.GenerateTiles();
            EditorUtility.SetDirty(gen.gameObject);
        }

        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.35f);
        if (GUILayout.Button("✕  Clear Tiles", GUILayout.Height(28)))
        {
            Undo.RegisterFullObjectHierarchyUndo(gen.gameObject, "Clear Road Tiles");
            gen.ClearTiles();
            EditorUtility.SetDirty(gen.gameObject);
        }

        GUI.backgroundColor = Color.white;

        if (gen.generatedTiles.Count > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                $"{gen.generatedTiles.Count} tiles generated. " +
                "Visible in Scene view as green ghost boxes.",
                MessageType.Info);
        }
    }
}
#endif
