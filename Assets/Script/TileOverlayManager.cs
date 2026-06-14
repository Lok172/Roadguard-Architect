using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  TileOverlayManager  (v6 — no prefab/shader slots)
//
//  All transparency is handled in code by TileOverlay.BuildCube().
//  This manager only exposes what needs to be tunable in the Inspector:
//    • Colours per state
//    • Glow animation
//    • Metallic & Smoothness surface values
//    • Cube offset & slab thickness
// ─────────────────────────────────────────────────────────────────

public class TileOverlayManager : MonoBehaviour
{
    public static TileOverlayManager Instance { get; private set; }

    // ── Cube Shape ────────────────────────────────────────────────
    [Header("Cube Shape")]
    [Tooltip("X/Z: lateral shift from tile centre.  Y: gap above the collider top face.")]
    public Vector3 cubeOffset = new Vector3(0f, 0.02f, 0f);

    [Tooltip("Local Y scale of the overlay slab (keep small, e.g. 0.02–0.05).")]
    [Min(0.001f)] public float slabThickness = 0.02f;

    // ── State Colours ─────────────────────────────────────────────
    [Header("State Colours")]
    [Tooltip("Available (Blue) — legacy; rarely visible in normal play.")]
    public Color colourAvailable = new Color(0.20f, 0.40f, 1.00f, 1f);

    [Tooltip("Occupied (Red) — cursor over a blocked slot, or tile fully occupied during drag.")]
    public Color colourOccupied = new Color(0.90f, 0.15f, 0.15f, 1f);

    [Tooltip("Suitable (Green) — tile has a free slot for the dragged device.")]
    public Color colourSuitable = new Color(0.10f, 0.90f, 0.40f, 1f);

    [Tooltip("Not Suitable (Orange) — the dragged device type doesn't fit this tile.")]
    public Color colourNotSuitable = new Color(1.00f, 0.55f, 0.00f, 1f);

    // ── Glow Animation ────────────────────────────────────────────
    [Header("Glow Animation")]
    [Tooltip("Alpha pulse cycles per second.")]
    public float glowSpeed = 1.5f;

    [Tooltip("Minimum alpha during the pulse (0 = fully transparent).")]
    [Range(0f, 1f)] public float startAlpha = 0.15f;

    [Tooltip("Maximum alpha during the pulse (1 = fully opaque).")]
    [Range(0f, 1f)] public float endAlpha = 0.70f;

    // ── Surface Properties ────────────────────────────────────────
    [Header("Surface Properties (URP/Lit)")]
    [Tooltip("Metallic value applied to the overlay material (0 = dielectric, 1 = metal).")]
    [Range(0f, 1f)] public float metallic = 0f;

    [Tooltip("Smoothness / glossiness of the overlay surface.")]
    [Range(0f, 1f)] public float smoothness = 0.5f;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[TileOverlayManager] Duplicate — destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}