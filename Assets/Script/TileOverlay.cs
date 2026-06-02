using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  TileOverlay  (v2 — transparency + brightness pulse fixed)
//
//  ROOT CAUSE OF PREVIOUS BUG:
//    "Unlit/Color" ignores the alpha channel entirely.
//    No matter what alpha you set, the quad renders 100% opaque.
//    This version uses "Unlit/Transparent" which DOES honour alpha.
//    Additionally, the pulse now animates both ALPHA and RGB
//    BRIGHTNESS simultaneously, so the throb is clearly visible.
//
//  States:
//    Default       — grey, static, semi-transparent
//    Valid         — green, alpha + brightness pulse
//    PoorPlacement — orange, alpha + brightness pulse
//    Occupied      — faded red, static (no pulse)
//    Hidden        — quad fully deactivated
//
//  URP / HDRP note:
//    Change Shader.Find("Unlit/Transparent") to
//    Shader.Find("Universal Render Pipeline/Unlit") and make sure
//    Surface Type is set to Transparent in the material, OR pass
//    your own transparent material into the shaderOverride field.
// ─────────────────────────────────────────────────────────────────

public enum OverlayState
{
    Default,
    Valid,
    PoorPlacement,
    Occupied,
    Hidden
}

[RequireComponent(typeof(BoxCollider))]
public class TileOverlay : MonoBehaviour
{
    // ── Colours (Inspector colour pickers) ────────────────────────
    [Header("State Colours")]
    public Color colourDefault = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color colourValid = new Color(0.10f, 0.90f, 0.40f, 1f);
    public Color colourPoor = new Color(1.00f, 0.60f, 0.00f, 1f);
    public Color colourOccupied = new Color(0.85f, 0.15f, 0.15f, 1f);

    // ── Pulse settings ────────────────────────────────────────────
    [Header("Pulse  (Valid & PoorPlacement only)")]
    [Tooltip("Cycles per second — higher = faster breathing")]
    public float pulseSpeed = 2f;

    [Tooltip("Alpha oscillates between X (dim) and Y (bright) — range 0 to 1")]
    public Vector2 pulseAlphaRange = new Vector2(0.15f, 0.80f);

    [Tooltip("RGB multiplier oscillates between X (dim) and Y (bright). " +
             "Values > 1 make the colour brighter than its base colour.")]
    public Vector2 pulseBrightRange = new Vector2(0.5f, 1.5f);

    // ── Static alphas ─────────────────────────────────────────────
    [Header("Static State Alphas")]
    [Range(0f, 1f)] public float defaultAlpha = 0.30f;
    [Range(0f, 1f)] public float occupiedAlpha = 0.25f;

    // ── Quad ──────────────────────────────────────────────────────
    [Header("Quad Position")]
    [Tooltip("Metres above the tile pivot. Increase if quad clips into road.")]
    public float yOffset = 0.06f;

    [Tooltip("Optional: assign your own transparent shader/material here. " +
             "Leave empty to use the auto-generated Unlit/Transparent material.")]
    public Material shaderOverride;

    // ── Runtime ───────────────────────────────────────────────────
    private OverlayState _state = OverlayState.Default;
    private GameObject _quadGO;
    private MeshRenderer _renderer;
    private Material _mat;
    private float _pulseTimer = 0f;
    private Color _baseColour;   // base RGB for the currently pulsing state

    public OverlayState CurrentState => _state;

    // ─────────────────────────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildQuad();
        ApplyState(OverlayState.Default);
    }

    private void Update()
    {
        if (_mat == null) return;

        bool shouldPulse = (_state == OverlayState.Valid ||
                            _state == OverlayState.PoorPlacement);
        if (!shouldPulse) return;

        _pulseTimer += Time.deltaTime * pulseSpeed;

        // Smooth sine wave: t goes 0 → 1 → 0 → …
        float t = (Mathf.Sin(_pulseTimer * Mathf.PI * 2f) + 1f) * 0.5f;

        float alpha = Mathf.Lerp(pulseAlphaRange.x, pulseAlphaRange.y, t);
        float bright = Mathf.Lerp(pulseBrightRange.x, pulseBrightRange.y, t);

        // Scale RGB by brightness, set alpha independently
        Color c = _baseColour * bright;
        c.a = alpha;
        _mat.color = c;
    }

    private void OnDestroy()
    {
        // Only destroy the material we created ourselves
        if (shaderOverride == null && _mat != null)
            Destroy(_mat);

        if (_quadGO != null)
            Destroy(_quadGO);
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
    //  QUAD CONSTRUCTION
    // ─────────────────────────────────────────────────────────────

    private void BuildQuad()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 cSize = col != null ? col.size : Vector3.one;
        Vector3 cCentre = col != null ? col.center : Vector3.zero;

        // Create primitive and parent it
        _quadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quadGO.name = "TileOverlayQuad";
        _quadGO.transform.SetParent(transform, false);

        // Rotate flat: Quad default faces +Z, we need it facing +Y (floor)
        _quadGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _quadGO.transform.localPosition = new Vector3(cCentre.x, yOffset, cCentre.z);
        _quadGO.transform.localScale = new Vector3(cSize.x, cSize.z, 1f);

        // Remove the auto-added MeshCollider so it never blocks raycasts
        MeshCollider mc = _quadGO.GetComponent<MeshCollider>();
        if (mc != null) Destroy(mc);

        _renderer = _quadGO.GetComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        // ── Material setup ────────────────────────────────────────
        if (shaderOverride != null)
        {
            // User provided their own material — instance it so we
            // can change colour without affecting other objects.
            _mat = new Material(shaderOverride);
        }
        else
        {
            // ── KEY FIX: "Unlit/Transparent" reads alpha; "Unlit/Color" does NOT ──
            Shader shader = Shader.Find("Unlit/Transparent");

            if (shader == null)
            {
                Debug.LogError(
                    "[TileOverlay] 'Unlit/Transparent' shader not found.\n" +
                    "• Built-in RP: make sure 'Always Included Shaders' in\n" +
                    "  Graphics Settings contains Unlit/Transparent.\n" +
                    "• URP: assign a transparent URP/Unlit material to the\n" +
                    "  'Shader Override' field on this component instead.");

                // Graceful fallback — at least something renders
                shader = Shader.Find("Unlit/Color");
            }

            _mat = new Material(shader);
        }

        // Force transparent blending regardless of shader defaults
        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_ZWrite", 0);
        _mat.renderQueue = 3000;   // Transparent queue

        _renderer.material = _mat;
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE APPLICATION
    // ─────────────────────────────────────────────────────────────

    private void ApplyState(OverlayState state)
    {
        if (_mat == null || _quadGO == null) return;

        _pulseTimer = 0f;

        switch (state)
        {
            case OverlayState.Default:
                _quadGO.SetActive(true);
                _baseColour = colourDefault;
                WriteColour(colourDefault, defaultAlpha);
                break;

            case OverlayState.Valid:
                _quadGO.SetActive(true);
                _baseColour = colourValid;
                WriteColour(colourValid, pulseAlphaRange.x);  // pulse takes over in Update
                break;

            case OverlayState.PoorPlacement:
                _quadGO.SetActive(true);
                _baseColour = colourPoor;
                WriteColour(colourPoor, pulseAlphaRange.x);
                break;

            case OverlayState.Occupied:
                _quadGO.SetActive(true);
                _baseColour = colourOccupied;
                WriteColour(colourOccupied, occupiedAlpha);
                break;

            case OverlayState.Hidden:
                _quadGO.SetActive(false);
                break;
        }
    }

    private void WriteColour(Color baseCol, float alpha)
    {
        Color c = baseCol;
        c.a = alpha;
        _mat.color = c;
    }
}