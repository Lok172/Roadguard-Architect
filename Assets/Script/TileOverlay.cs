using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  TileOverlay  (v3 — Cube-based with TileShader material)
//
//  Spawns a cube prefab (with TileShader material) instead of a
//  quad.  The cube is sized and positioned to match the parent
//  RoadTile's BoxCollider, with an Inspector-tunable 3-axis offset.
//
//  The material's alpha oscillates between startAlpha and endAlpha
//  to produce a simple glowing animation.
//
//  Four states:
//    Available   (Blue)   — tile has at least one empty slot
//    Occupied    (Red)    — all slots on this tile are filled
//    Suitable    (Green)  — the held device is suitable for a slot
//    NotSuitable (Orange) — the held device is NOT suitable
//
//  Attach this script to the TileOverlayManager GameObject (or
//  let the system add it per-tile).  Assign the cube prefab and
//  tune colours / glow in the Inspector.
//
//  URP note:
//    The cube prefab should use a URP/Lit material with Surface
//    Type = Transparent and Blending Mode = Alpha (the "TileShader"
//    material shown in the screenshot).  This script drives the
//    Base Map colour (including alpha) at runtime.
// ─────────────────────────────────────────────────────────────────

public enum OverlayState
{
    Available,      // Blue  — empty slot(s) exist
    Occupied,       // Red   — all slots filled
    Suitable,       // Green — device fits the slot
    NotSuitable,    // Orange — device does not fit
    Hidden          // cube deactivated
}

[RequireComponent(typeof(BoxCollider))]
public class TileOverlay : MonoBehaviour
{
    // ── Prefab ────────────────────────────────────────────────────
    [Header("Cube Prefab")]
    [Tooltip("Drag the cube prefab (with TileShader material) here. " +
             "If left empty, a default cube primitive is created at runtime.")]
    public GameObject cubePrefab;

    // ── Offset ────────────────────────────────────────────────────
    [Header("Cube Offset (local-space)")]
    [Tooltip("3-axis offset applied on top of the collider centre.")]
    public Vector3 cubeOffset = new Vector3(0f, 0.06f, 0f);

    // ── State Colours ─────────────────────────────────────────────
    [Header("State Colours (RGB — alpha is driven by glow)")]
    [Tooltip("Available = at least one empty slot")]
    public Color colourAvailable = new Color(0.20f, 0.40f, 1.00f, 1f);   // Blue

    [Tooltip("Occupied = every slot filled")]
    public Color colourOccupied = new Color(0.90f, 0.15f, 0.15f, 1f);   // Red

    [Tooltip("Suitable = device fits")]
    public Color colourSuitable = new Color(0.10f, 0.90f, 0.40f, 1f);   // Green

    [Tooltip("Not suitable = device does not fit")]
    public Color colourNotSuitable = new Color(1.00f, 0.55f, 0.00f, 1f);   // Orange

    // ── Glow / Alpha Animation ────────────────────────────────────
    [Header("Glow Animation")]
    [Tooltip("Glow cycles per second (higher = faster pulse).")]
    public float glowSpeed = 1.5f;

    [Tooltip("Minimum alpha during the glow cycle.")]
    [Range(0f, 1f)] public float startAlpha = 0.15f;

    [Tooltip("Maximum alpha during the glow cycle.")]
    [Range(0f, 1f)] public float endAlpha = 0.70f;

    // ── Scale Override ────────────────────────────────────────────
    [Header("Scale")]
    [Tooltip("If true, the cube's local scale is set to match the " +
             "parent tile's BoxCollider size.  Disable if the prefab " +
             "is already sized correctly.")]
    public bool matchColliderSize = true;

    // ── Runtime ───────────────────────────────────────────────────
    private OverlayState _state = OverlayState.Available;
    private GameObject _cubeInstance;
    private Renderer _renderer;
    private Material _mat;
    private float _glowTimer;
    private Color _baseColour;

    // Shader property IDs (cached for speed)
    private static readonly int _BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int _Color = Shader.PropertyToID("_Color");

    public OverlayState CurrentState => _state;

    // ─────────────────────────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        SpawnCube();
        ApplyState(OverlayState.Available);
    }

    private void Update()
    {
        if (_mat == null || _cubeInstance == null || !_cubeInstance.activeSelf) return;

        // All four visible states pulse (glow animation).
        if (_state == OverlayState.Hidden) return;

        _glowTimer += Time.deltaTime * glowSpeed;

        // Smooth sine: t oscillates 0 → 1 → 0
        float t = (Mathf.Sin(_glowTimer * Mathf.PI * 2f) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(startAlpha, endAlpha, t);

        Color c = _baseColour;
        c.a = alpha;
        SetMaterialColour(c);
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_cubeInstance != null) Destroy(_cubeInstance);
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Switch this tile's overlay to a new visual state.</summary>
    public void SetState(OverlayState newState)
    {
        if (_state == newState) return;
        _state = newState;
        ApplyState(newState);
    }

    // ─────────────────────────────────────────────────────────────
    //  CUBE CONSTRUCTION
    // ─────────────────────────────────────────────────────────────

    private void SpawnCube()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 cSize = col != null ? col.size : Vector3.one;
        Vector3 cCentre = col != null ? col.center : Vector3.zero;

        // ── Instantiate or create ─────────────────────────────────
        if (cubePrefab != null)
        {
            _cubeInstance = Instantiate(cubePrefab, transform, false);
        }
        else
        {
            _cubeInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeInstance.transform.SetParent(transform, false);

            // Remove the auto-added BoxCollider so it doesn't
            // interfere with raycasts on the tile.
            BoxCollider autoColl = _cubeInstance.GetComponent<BoxCollider>();
            if (autoColl != null) Destroy(autoColl);
        }

        _cubeInstance.name = "TileOverlayCube";

        // ── Position: collider centre + user offset ───────────────
        _cubeInstance.transform.localPosition = cCentre + cubeOffset;

        // ── No extra rotation — inherit tile's orientation ────────
        _cubeInstance.transform.localRotation = Quaternion.identity;

        // ── Scale to collider size ────────────────────────────────
        if (matchColliderSize)
            _cubeInstance.transform.localScale = cSize;

        // ── Remove any collider on the spawned cube ───────────────
        Collider spawnedCol = _cubeInstance.GetComponent<Collider>();
        if (spawnedCol != null) Destroy(spawnedCol);

        // ── Renderer & Material ───────────────────────────────────
        _renderer = _cubeInstance.GetComponent<Renderer>();
        if (_renderer == null)
            _renderer = _cubeInstance.GetComponentInChildren<Renderer>();

        if (_renderer != null)
        {
            // Instance the material so per-tile colour changes
            // don't bleed into the shared asset.
            _mat = new Material(_renderer.sharedMaterial);
            _renderer.material = _mat;

            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }
        else
        {
            Debug.LogWarning("[TileOverlay] No Renderer found on the cube prefab.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE APPLICATION
    // ─────────────────────────────────────────────────────────────

    private void ApplyState(OverlayState state)
    {
        if (_cubeInstance == null) return;

        _glowTimer = 0f;

        switch (state)
        {
            case OverlayState.Available:
                _cubeInstance.SetActive(true);
                _baseColour = colourAvailable;
                break;

            case OverlayState.Occupied:
                _cubeInstance.SetActive(true);
                _baseColour = colourOccupied;
                break;

            case OverlayState.Suitable:
                _cubeInstance.SetActive(true);
                _baseColour = colourSuitable;
                break;

            case OverlayState.NotSuitable:
                _cubeInstance.SetActive(true);
                _baseColour = colourNotSuitable;
                break;

            case OverlayState.Hidden:
                _cubeInstance.SetActive(false);
                return;
        }

        // Set initial colour at startAlpha
        Color c = _baseColour;
        c.a = startAlpha;
        SetMaterialColour(c);
    }

    // ─────────────────────────────────────────────────────────────
    //  MATERIAL HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the colour on both "_BaseColor" (URP/Lit) and "_Color"
    /// (Built-in) so the same script works in either pipeline.
    /// </summary>
    private void SetMaterialColour(Color c)
    {
        if (_mat == null) return;

        if (_mat.HasProperty(_BaseColor))
            _mat.SetColor(_BaseColor, c);

        if (_mat.HasProperty(_Color))
            _mat.SetColor(_Color, c);
    }
}