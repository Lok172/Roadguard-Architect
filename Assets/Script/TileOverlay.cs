using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  TileOverlay  (v2)
//
//  Changes from v1:
//    • All 4 state colours exposed as [SerializeField] — editable
//      in the Inspector with a colour picker per tile or globally
//      via a prefab.
//    • Every state (Default, Valid, PoorPlacement, Occupied) now
//      pulses using the SAME shared pulse parameters:
//        pulseSpeed        — how fast (radians / sec)
//        pulseAlphaRange   — min / max alpha per cycle
//      Hidden stays fully off (quad deactivated).
//    • Pulse timer resets to 0 on every state transition so the
//      animation always starts from the same phase.
// ─────────────────────────────────────────────────────────────────

public enum OverlayState
{
    Default,        // grey  — game start / no device selected
    Valid,          // green — device can be placed here
    PoorPlacement,  // orange — allowed but happiness penalty
    Occupied,       // red   — tile already has a device
    Hidden          // fully transparent — device not allowed
}

[RequireComponent(typeof(BoxCollider))]
public class TileOverlay : MonoBehaviour
{
    // ── Position ──────────────────────────────────────────────────
    [Header("Quad Position")]
    [Tooltip("How far above the tile pivot the quad floats")]
    public float yOffset = 0.06f;

    // ── Shared Pulse Parameters ───────────────────────────────────
    [Header("Pulse (shared by all states)")]
    [Tooltip("Pulse speed in radians per second — higher = faster breathing")]
    public float pulseSpeed = 3f;

    [Tooltip("Alpha oscillates between x (dim) and y (bright) each cycle")]
    public Vector2 pulseAlphaRange = new Vector2(0.20f, 0.55f);

    // ── State Colours (Inspector colour pickers) ──────────────────
    [Header("State Colours")]
    [Tooltip("Default state — shown at game start when no device is selected")]
    [SerializeField] private Color colDefault = new Color(1.00f, 0.60f, 0.00f, 1f);

    [Tooltip("Valid state — device can be placed on this tile")]
    [SerializeField] private Color colValid = new Color(0.10f, 0.90f, 0.40f, 1f);

    [Tooltip("Poor placement — device allowed but will cause a happiness penalty")]
    [SerializeField] private Color colPoorPlacement = new Color(1.00f, 0.60f, 0.00f, 1f);

    [Tooltip("Occupied — tile already contains a device, cannot place another")]
    [SerializeField] private Color colOccupied = new Color(0.85f, 0.15f, 0.15f, 1f);

    // ── Runtime ───────────────────────────────────────────────────
    private OverlayState _state = OverlayState.Default;
    private GameObject _quadGO;
    private MeshRenderer _renderer;
    private Material _mat;
    private float _pulseTimer = 0f;

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
        // Hidden state — quad is off, nothing to do
        if (_state == OverlayState.Hidden || _mat == null) return;

        // Advance shared timer and compute alpha
        _pulseTimer += Time.deltaTime * pulseSpeed;
        float t = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;   // 0 → 1
        float alpha = Mathf.Lerp(pulseAlphaRange.x, pulseAlphaRange.y, t);

        Color c = _mat.color;
        c.a = alpha;
        _mat.color = c;
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_quadGO != null) Destroy(_quadGO);
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Switch the overlay to a new visual state.</summary>
    public void SetState(OverlayState newState)
    {
        if (_state == newState) return;
        _state = newState;
        ApplyState(newState);
    }

    public OverlayState CurrentState => _state;

    // ─────────────────────────────────────────────────────────────
    //  INTERNAL — BUILD
    // ─────────────────────────────────────────────────────────────

    private void BuildQuad()
    {
        BoxCollider col = GetComponent<BoxCollider>();
        Vector3 colSize = col != null ? col.size : Vector3.one;
        Vector3 colCtr = col != null ? col.center : Vector3.zero;

        _quadGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quadGO.name = "TileOverlayQuad";
        _quadGO.transform.SetParent(transform, false);

        // Quad default faces +Z — rotate to face +Y (flat on ground)
        _quadGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _quadGO.transform.localPosition = new Vector3(colCtr.x, yOffset, colCtr.z);

        // Match collider XZ footprint
        _quadGO.transform.localScale = new Vector3(colSize.x, colSize.z, 1f);

        // Remove auto-added MeshCollider
        MeshCollider mc = _quadGO.GetComponent<MeshCollider>();
        if (mc != null) Destroy(mc);

        // Runtime Unlit/Color material with alpha blending
        _renderer = _quadGO.GetComponent<MeshRenderer>();
        _mat = new Material(Shader.Find("Unlit/Color"));
        _mat.renderQueue = 3000;

        _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _mat.SetInt("_ZWrite", 0);
        _mat.DisableKeyword("_ALPHATEST_ON");
        _mat.EnableKeyword("_ALPHABLEND_ON");
        _mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        _renderer.material = _mat;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  INTERNAL — STATE APPLICATION
    // ─────────────────────────────────────────────────────────────

    private void ApplyState(OverlayState state)
    {
        if (_mat == null || _quadGO == null) return;

        // Reset pulse phase so every new state starts from the same
        // point in the cycle — avoids a sudden alpha jump mid-pulse.
        _pulseTimer = 0f;

        if (state == OverlayState.Hidden)
        {
            _quadGO.SetActive(false);
            return;
        }

        _quadGO.SetActive(true);

        // Set the RGB from the matching colour field; alpha starts at
        // pulseAlphaRange.x and Update() will animate it from there.
        Color baseColor = StateToColor(state);
        baseColor.a = pulseAlphaRange.x;
        _mat.color = baseColor;
    }

    /// <summary>Maps a state to its Inspector-editable colour.</summary>
    private Color StateToColor(OverlayState state)
    {
        return state switch
        {
            OverlayState.Default => colDefault,
            OverlayState.Valid => colValid,
            OverlayState.PoorPlacement => colPoorPlacement,
            OverlayState.Occupied => colOccupied,
            _ => colDefault
        };
    }
}