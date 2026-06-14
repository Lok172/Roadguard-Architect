using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// ─────────────────────────────────────────────────────────────────
//  TileOverlay  (v8 — synced glow timer, Available=blue when no drag)
//
//  Builds a flat cube primitive in code and creates a URP/Lit
//  material with Surface Type forced to Transparent entirely in code.
//  No TileShader slot, no prefab slot needed.
//
//  Inspector-tunable (via TileOverlayManager):
//    • Colours per state
//    • Glow speed / alpha range
//    • Metallic value
//    • Smoothness value
//    • Cube offset & slab thickness
// ─────────────────────────────────────────────────────────────────

public enum OverlayState
{
    Available,
    Occupied,
    Suitable,
    NotSuitable,
    Hidden
}

[RequireComponent(typeof(BoxCollider))]
public class TileOverlay : MonoBehaviour
{
    // ── Settings from manager ─────────────────────────────────────
    private Vector3 _offset = new Vector3(0f, 0.02f, 0f);
    private float _slabThickness = 0.02f;
    private Color _colAvailable = new Color(0.20f, 0.40f, 1.00f, 1f);
    private Color _colOccupied = new Color(0.90f, 0.15f, 0.15f, 1f);
    private Color _colSuitable = new Color(0.10f, 0.90f, 0.40f, 1f);
    private Color _colNotSuitable = new Color(1.00f, 0.55f, 0.00f, 1f);
    private float _glowSpeed = 1.5f;
    private float _startAlpha = 0.15f;
    private float _endAlpha = 0.70f;
    private float _metallic = 0.0f;
    private float _smoothness = 0.5f;

    // ── Drag context ──────────────────────────────────────────────
    private bool _dragActive = false;
    private bool _cursorOnOccupiedCorner = false;
    private TrafficDeviceType _dragDevice = TrafficDeviceType.None;

    // ── Runtime ───────────────────────────────────────────────────
    private OverlayState _state = OverlayState.Hidden;
    private GameObject _cube;
    private Material _mat;
    // Shared across all TileOverlay instances so every tile pulses in sync.
    private static float _sharedGlowTimer;
    private static int _timerLastFrame = -1;  // prevents double-advance in same frame
    private Color _baseColour;
    private RoadTile _tile;
    private bool _ready = false;

    // URP Lit shader property IDs
    private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int PropColor = Shader.PropertyToID("_Color");       // Built-in fallback
    private static readonly int PropSurface = Shader.PropertyToID("_Surface");
    private static readonly int PropBlend = Shader.PropertyToID("_Blend");
    private static readonly int PropZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int PropSrcBlend = Shader.PropertyToID("_SrcBlend");
    private static readonly int PropDstBlend = Shader.PropertyToID("_DstBlend");
    private static readonly int PropMetallic = Shader.PropertyToID("_Metallic");
    private static readonly int PropSmoothness = Shader.PropertyToID("_Smoothness");

    public OverlayState CurrentState => _state;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _tile = GetComponent<RoadTile>();
    }

    private void Start()
    {
        StartCoroutine(InitNextFrame());
    }

    private IEnumerator InitNextFrame()
    {
        yield return null;   // ensure TileOverlayManager.Awake has run
        ReadManagerSettings();
        BuildCube();
        _ready = true;
        EvaluateAndApply();
    }

    private void Update()
    {
        if (_ready) AnimateGlow();
    }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_cube != null) Destroy(_cube);
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    public void SetDragState(TrafficDeviceType device, bool cursorOnOccupiedCorner)
    {
        _dragActive = true;
        _dragDevice = device;
        _cursorOnOccupiedCorner = cursorOnOccupiedCorner;
        if (_ready) EvaluateAndApply();
    }

    public void ClearDragState()
    {
        _dragActive = false;
        _dragDevice = TrafficDeviceType.None;
        _cursorOnOccupiedCorner = false;
        if (_ready) EvaluateAndApply();
    }

    public void SetState(OverlayState newState)
    {
        if (!_ready || _state == newState) return;
        _state = newState;
        ApplyState(newState);
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE EVALUATION
    // ─────────────────────────────────────────────────────────────

    private void EvaluateAndApply()
    {
        if (_tile == null) { ApplyState(OverlayState.Hidden); return; }

        bool allCorners = AllCornersFilled();
        bool centerFilled = _tile.IsCornerOccupied(TileCorner.Center);
        bool fullyOccupied = allCorners && centerFilled;

        OverlayState next;

        if (!_dragActive)
        {
            next = fullyOccupied ? OverlayState.Hidden : OverlayState.Available;
        }
        else
        {
            bool allowed = _tile.allowedDevices.Count == 0 ||
                           _tile.allowedDevices.Contains(_dragDevice);

            if (!allowed)
                next = OverlayState.NotSuitable;
            else if (fullyOccupied)
                next = OverlayState.Occupied;
            else if (_cursorOnOccupiedCorner)
                next = OverlayState.Occupied;
            else if (_dragDevice == TrafficDeviceType.SpeedBump)
                next = centerFilled ? OverlayState.Occupied : OverlayState.Suitable;
            else
                next = allCorners ? OverlayState.Occupied : OverlayState.Suitable;

            if (next == OverlayState.Suitable &&
                _tile.GetOverlayState(_dragDevice) == OverlayState.NotSuitable)
                next = OverlayState.NotSuitable;
        }

        if (_state != next)
        {
            _state = next;
            ApplyState(next);
        }
    }

    private bool AllCornersFilled()
    {
        if (_tile == null) return false;
        return _tile.IsCornerOccupied(TileCorner.NorthWest)
            && _tile.IsCornerOccupied(TileCorner.NorthEast)
            && _tile.IsCornerOccupied(TileCorner.SouthEast)
            && _tile.IsCornerOccupied(TileCorner.SouthWest);
    }

    // ─────────────────────────────────────────────────────────────
    //  GLOW ANIMATION
    // ─────────────────────────────────────────────────────────────

    private void AnimateGlow()
    {
        if (_mat == null || _cube == null || !_cube.activeSelf) return;
        if (_state == OverlayState.Hidden) return;

        RefreshLiveParams();

        // Advance the shared timer once per frame only — guarded by frame count
        // so multiple active tiles don't multiply the advance speed.
        if (Time.frameCount != _timerLastFrame)
        {
            _sharedGlowTimer += Time.deltaTime * _glowSpeed;
            _timerLastFrame = Time.frameCount;
        }

        float t = (Mathf.Sin(_sharedGlowTimer * Mathf.PI * 2f) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(_startAlpha, _endAlpha, t);

        Color c = _baseColour;
        c.a = alpha;
        SetColour(c);
    }

    // ─────────────────────────────────────────────────────────────
    //  SETTINGS
    // ─────────────────────────────────────────────────────────────

    private void ReadManagerSettings()
    {
        var mgr = TileOverlayManager.Instance;
        if (mgr == null) return;

        _offset = mgr.cubeOffset;
        _slabThickness = mgr.slabThickness;
        _colAvailable = mgr.colourAvailable;
        _colOccupied = mgr.colourOccupied;
        _colSuitable = mgr.colourSuitable;
        _colNotSuitable = mgr.colourNotSuitable;
        _glowSpeed = mgr.glowSpeed;
        _startAlpha = mgr.startAlpha;
        _endAlpha = mgr.endAlpha;
        _metallic = mgr.metallic;
        _smoothness = mgr.smoothness;
    }

    /// <summary>Hot-reloads colours, glow, metallic, smoothness each frame.</summary>
    private void RefreshLiveParams()
    {
        var mgr = TileOverlayManager.Instance;
        if (mgr == null) return;

        _glowSpeed = mgr.glowSpeed;
        _startAlpha = mgr.startAlpha;
        _endAlpha = mgr.endAlpha;
        _colAvailable = mgr.colourAvailable;
        _colOccupied = mgr.colourOccupied;
        _colSuitable = mgr.colourSuitable;
        _colNotSuitable = mgr.colourNotSuitable;
        _baseColour = ColourForState(_state);

        // Live-update surface properties
        if (_mat != null)
        {
            float m = mgr.metallic;
            float s = mgr.smoothness;
            if (!Mathf.Approximately(_metallic, m) || !Mathf.Approximately(_smoothness, s))
            {
                _metallic = m;
                _smoothness = s;
                _mat.SetFloat(PropMetallic, _metallic);
                _mat.SetFloat(PropSmoothness, _smoothness);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CUBE CONSTRUCTION
    // ─────────────────────────────────────────────────────────────

    private void BuildCube()
    {
        // 1. Primitive cube
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "TileEffectCube";
        _cube.transform.SetParent(transform, false);

        // Strip the auto-added collider — must never block tile raycasts.
        var bc = _cube.GetComponent<BoxCollider>();
        if (bc != null) Destroy(bc);

        // 2. Size & position — flat slab sitting on top of the BoxCollider surface.
        var col = GetComponent<BoxCollider>();
        Vector3 sz = col != null ? col.size : Vector3.one;
        Vector3 ctr = col != null ? col.center : Vector3.zero;

        _cube.transform.localPosition = new Vector3(
            ctr.x + _offset.x,
            ctr.y + sz.y * 0.5f + _offset.y,   // top surface + gap
            ctr.z + _offset.z);
        _cube.transform.localRotation = Quaternion.identity;
        _cube.transform.localScale = new Vector3(sz.x, _slabThickness, sz.z);

        // 3. Build a URP/Lit Transparent material entirely in code.
        _mat = BuildTransparentMaterial();

        var rend = _cube.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material = _mat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }
        else
        {
            Debug.LogWarning("[TileOverlay] No Renderer on primitive cube — this shouldn't happen.");
        }

        // Start deactivated; EvaluateAndApply enables it if a slot is free.
        _cube.SetActive(false);
    }

    /// <summary>
    /// Creates a URP/Lit material with Surface Type = Transparent,
    /// Blending Mode = Alpha, ZWrite off — all set in code so no
    /// material asset or shader slot is needed in the Inspector.
    /// </summary>
    private Material BuildTransparentMaterial()
    {
        // Find the URP Lit shader. This is always present in a URP project.
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            // Project is not URP — fall back to the legacy transparent shader.
            Debug.LogWarning("[TileOverlay] URP/Lit shader not found. Falling back to Transparent/Diffuse.");
            Shader legacy = Shader.Find("Transparent/Diffuse")
                         ?? Shader.Find("Unlit/Transparent");
            return legacy != null ? new Material(legacy) : new Material(Shader.Find("Standard"));
        }

        var mat = new Material(urpLit);
        mat.name = "TileOverlay_Transparent (instance)";

        // ── URP transparency setup ────────────────────────────────
        // _Surface 0 = Opaque, 1 = Transparent
        mat.SetFloat(PropSurface, 1f);
        // _Blend  0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
        mat.SetFloat(PropBlend, 0f);
        // ZWrite must be off for transparent geometry
        mat.SetFloat(PropZWrite, 0f);
        // Standard alpha-blend src/dst factors
        mat.SetFloat(PropSrcBlend, (float)BlendMode.SrcAlpha);
        mat.SetFloat(PropDstBlend, (float)BlendMode.OneMinusSrcAlpha);

        // Required URP keywords
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");      // needed by some URP versions
        mat.DisableKeyword("_ALPHABLEND_ON");

        // Place in the Transparent render queue
        mat.renderQueue = (int)RenderQueue.Transparent;

        // ── Surface inputs ────────────────────────────────────────
        // Start with a plain white base; colour is driven per-frame by SetColour().
        mat.SetColor(PropBaseColor, Color.white);
        mat.SetFloat(PropMetallic, _metallic);
        mat.SetFloat(PropSmoothness, _smoothness);

        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE APPLICATION
    // ─────────────────────────────────────────────────────────────

    private void ApplyState(OverlayState state)
    {
        if (_cube == null) return;

        // Note: we do NOT reset _sharedGlowTimer here — it is shared
        // across all tiles, so resetting it on one tile would cause
        // all others to jump phase, producing the colour-cycle desync.

        if (state == OverlayState.Hidden)
        {
            _cube.SetActive(false);
            return;
        }

        _cube.SetActive(true);
        _baseColour = ColourForState(state);

        Color c = _baseColour;
        c.a = _startAlpha;
        SetColour(c);
    }

    private Color ColourForState(OverlayState state) => state switch
    {
        OverlayState.Available => _colAvailable,
        OverlayState.Occupied => _colOccupied,
        OverlayState.Suitable => _colSuitable,
        OverlayState.NotSuitable => _colNotSuitable,
        _ => _colSuitable,
    };

    // ─────────────────────────────────────────────────────────────
    //  MATERIAL COLOUR
    // ─────────────────────────────────────────────────────────────

    private void SetColour(Color c)
    {
        if (_mat == null) return;
        if (_mat.HasProperty(PropBaseColor)) _mat.SetColor(PropBaseColor, c);
        if (_mat.HasProperty(PropColor)) _mat.SetColor(PropColor, c);
    }
}