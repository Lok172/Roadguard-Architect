using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────────────────────────
//  RoadTileGenerator
//  Attach to any empty GameObject in your scene.
//  Configure in the Inspector, then click "Generate Tiles".
//  All tiles are parented under this GameObject.
// ─────────────────────────────────────────────────────────────────

public class RoadTileGenerator : MonoBehaviour
{
    // ── Layout ────────────────────────────────────────────────────
    [Header("Layout")]
    [Tooltip("World-space centre of the FIRST tile")]
    public Vector3 startPosition = Vector3.zero;

    [Tooltip("Direction the road runs (auto-normalised)")]
    public Vector3 direction = Vector3.forward;

    [Tooltip("Number of tiles to generate")]
    public int tileCount = 5;

    [Tooltip("Distance between tile centres — usually equals tile length")]
    public float tileSpacing = 5f;

    // ── Collider ──────────────────────────────────────────────────
    [Header("Collider Size")]
    [Tooltip("X=road width, Y=collider height (keep thin e.g. 0.1), Z=tile length")]
    public Vector3 colliderSize   = new Vector3(4f, 0.1f, 5f);

    [Tooltip("Offset of the collider centre relative to tile pivot")]
    public Vector3 colliderOffset = Vector3.zero;

    // ── Tile defaults ─────────────────────────────────────────────
    [Header("Tile Defaults")]
    public TileType defaultTileType = TileType.Residential;
    public ZoneType defaultZoneType = ZoneType.Residential;

    [Tooltip("Accident-rate points this tile contributes at baseline (before devices)")]
    [Min(0)]
    public int defaultAccidentContribution = 1;

    [Tooltip("Devices allowed on every generated tile. Leave empty to allow all.")]
    public List<TrafficDeviceType> defaultAllowedDevices = new List<TrafficDeviceType>
    {
        TrafficDeviceType.StopSign,
        TrafficDeviceType.SpeedBump
        // Note: TrafficLight intentionally omitted for residential — poor placement
    };

    [Tooltip("Prefix for auto-generated tile IDs e.g. 'ResRoad' → ResRoad_00")]
    public string tileIDPrefix = "ResRoad";

    // ── Layer / Tag ───────────────────────────────────────────────
    [Header("Layer & Tag")]
    [Tooltip("Must exist in Edit > Project Settings > Tags & Layers")]
    public string tileLayer = "RoadTile";
    public string tileTag   = "RoadTile";

    // ── Runtime ───────────────────────────────────────────────────
    [HideInInspector]
    public List<RoadTile> generatedTiles = new List<RoadTile>();

    // ─────────────────────────────────────────────────────────────
    //  GENERATE
    // ─────────────────────────────────────────────────────────────

    public void GenerateTiles()
    {
        ClearTiles();

        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;

        int layerIndex = LayerMask.NameToLayer(tileLayer);
        if (layerIndex < 0)
        {
            Debug.LogWarning($"[RoadTileGenerator] Layer '{tileLayer}' not found. " +
                             "Go to Edit > Project Settings > Tags & Layers and add it. " +
                             "Tiles will use Default layer (0) until then — " +
                             "raycasts using LayerMask.GetMask(\"{tileLayer}\") will miss them.");
            layerIndex = 0;
        }

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 pos = startPosition + dir * (tileSpacing * i);

            GameObject tileGO   = new GameObject($"{tileIDPrefix}_{i:D2}");
            tileGO.transform.position = pos;
            tileGO.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            tileGO.transform.SetParent(transform, worldPositionStays: true);
            tileGO.layer = layerIndex;

            try   { tileGO.tag = tileTag; }
            catch { Debug.LogWarning($"[RoadTileGenerator] Tag '{tileTag}' not found. " +
                                     "Add it in Edit > Project Settings > Tags & Layers."); }

            BoxCollider col = tileGO.AddComponent<BoxCollider>();
            col.size        = colliderSize;
            col.center      = colliderOffset;
            col.isTrigger   = true;

            RoadTile tile = tileGO.AddComponent<RoadTile>();
            
            if (GameManager.Instance != null)
                GameManager.Instance.RegisterTile(tile);
            else
                Debug.LogWarning("GameManager not found, tile not registered.");

            tile.tileID                   = $"{tileIDPrefix}_{i:D2}";
            tile.tileType                 = defaultTileType;
            tile.zoneType                 = defaultZoneType;
            tile.baseAccidentContribution = defaultAccidentContribution;
            tile.allowedDevices           = new List<TrafficDeviceType>(defaultAllowedDevices);

            generatedTiles.Add(tile);

        }

        Debug.Log($"[RoadTileGenerator] Generated {tileCount} tiles " +
                  $"for '{tileIDPrefix}' ({defaultZoneType} zone). " +
                  $"Total accident contribution at baseline: " +
                  $"{tileCount * defaultAccidentContribution}");
    }

    // ─────────────────────────────────────────────────────────────
    //  CLEAR
    // ─────────────────────────────────────────────────────────────

    public void ClearTiles()
    {
        foreach (Transform child in transform)
        {
            RoadTile tile = child.GetComponent<RoadTile>();
            if (tile != null && GameManager.Instance != null)
            {
                GameManager.Instance.UnregisterTile(tile);
            }
        }

        generatedTiles.Clear();

        var children = new List<GameObject>();
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
    //  GIZMOS
    // ─────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (tileCount <= 0) return;

        Vector3    dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

        // Colour preview by zone
        Color previewColor = defaultZoneType switch
        {
            ZoneType.Residential => new Color(0.2f, 0.8f, 0.3f, 0.12f),
            ZoneType.Commercial  => new Color(0.2f, 0.4f, 0.9f, 0.12f),
            ZoneType.Industrial  => new Color(0.9f, 0.6f, 0.1f, 0.12f),
            ZoneType.Highway     => new Color(0.9f, 0.2f, 0.2f, 0.12f),
            _                    => new Color(0.5f, 0.5f, 0.5f, 0.12f)
        };
        Color wireColor = previewColor;
        wireColor.a = 0.6f;

        for (int i = 0; i < tileCount; i++)
        {
            Vector3 pos = startPosition + dir * (tileSpacing * i);

            Gizmos.matrix = Matrix4x4.TRS(pos + rot * colliderOffset, rot, Vector3.one);
            Gizmos.color  = previewColor;
            Gizmos.DrawCube(Vector3.zero, colliderSize);

            Gizmos.color = wireColor;
            Gizmos.DrawWireCube(Vector3.zero, colliderSize);

            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(
                pos + Vector3.up * 0.4f,
                $"{tileIDPrefix}_{i:D2}\n+{defaultAccidentContribution} acc"
            );
        }

        // Direction arrow
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color  = Color.cyan;
        Gizmos.DrawRay(startPosition, dir * tileSpacing * tileCount);
    }
#endif
}

// ─────────────────────────────────────────────────────────────────
//  CUSTOM INSPECTOR
// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[CustomEditor(typeof(RoadTileGenerator))]
public class RoadTileGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        RoadTileGenerator gen = (RoadTileGenerator)target;

        // ── Info box ─────────────────────────
        EditorGUILayout.Space(6);

        int totalContrib = gen.tileCount * gen.defaultAccidentContribution;
        EditorGUILayout.HelpBox(
            $"This road segment will contribute {totalContrib} accident-rate points at baseline.\n" +
            $"Fully covered with SpeedBumps (−3 each): " +
            $"reduced to {Mathf.Max(0, totalContrib - gen.tileCount * 3)}.",
            MessageType.None
        );

        // ── Residential + TrafficLight warning ──
        if (gen.defaultZoneType == ZoneType.Residential &&
            gen.defaultAllowedDevices.Contains(TrafficDeviceType.TrafficLight))
        {
            EditorGUILayout.HelpBox(
                "TrafficLight is in the allowed list for a Residential zone.\n" +
                "Per game design, this causes a happiness penalty (poor placement).\n" +
                "Consider removing it from allowedDevices for this road.",
                MessageType.Warning
            );
        }

        EditorGUILayout.Space(6);

        // ── Buttons ──────────────────────────
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
                $"{gen.generatedTiles.Count} tiles active. " +
                "Green ghost boxes visible in Scene view.",
                MessageType.Info
            );
        }
    }
}
#endif
