using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// ─────────────────────────────────────────────────────────────────
//  TileOverlay  (v9 — corner-aware correctness colouring)
//
//  REQ 2 FIX: EvaluateAndApply now mirrors the same corner-checking
//  logic that IsSlotCorrect uses so the overlay colour matches what
//  the placement system will actually score.
//
//  Old behaviour:
//    • Suitable = "tile has any free corner slot"
//    • Occupied = "cursor is on an already-occupied corner"
//    The overlay ignored whether the hovered corner would be CORRECT
//    for the dragged device type, so it glowed green even when the
//    placement was going to be scored as PoorPlacement.
//
//  New behaviour (matched to IsSlotCorrect):
//    • Suitable  = free slot AND the target corner is correct for
//                  this device on this tile type.
//    • NotSuitable = tile type / segment type doesn't accept the
//                    device at all (GetCorrectCountLimit == 0).
//    • Occupied  = that specific corner is already taken, or the
//                  correct-count limit is already reached.
//
//  The TileOverlay now receives the hovered corner from PlacementManager
//  via SetDragState (corner parameter added), so it can evaluate exactly
//  which corner the user is hovering.
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
    private Vector3 _offset        = new Vector3(0f, 0.02f, 0f);
    private float   _slabThickness = 0.02f;
    private Color   _colAvailable    = new Color(0.20f, 0.40f, 1.00f, 1f);
    private Color   _colOccupied     = new Color(0.90f, 0.15f, 0.15f, 1f);
    private Color   _colSuitable     = new Color(0.10f, 0.90f, 0.40f, 1f);
    private Color   _colNotSuitable  = new Color(1.00f, 0.55f, 0.00f, 1f);
    private float   _glowSpeed   = 1.5f;
    private float   _startAlpha  = 0.15f;
    private float   _endAlpha    = 0.70f;
    private float   _metallic    = 0.0f;
    private float   _smoothness  = 0.5f;

    // ── Drag context ──────────────────────────────────────────────
    private bool             _dragActive           = false;
    private bool             _cursorOnOccupiedCorner = false;
    private TrafficDeviceType _dragDevice          = TrafficDeviceType.None;

    // REQ 2: we now track which corner the cursor is over.
    private TileCorner       _hoveredCorner        = TileCorner.None;

    // ── Runtime ───────────────────────────────────────────────────
    private OverlayState _state = OverlayState.Hidden;
    private GameObject   _cube;
    private Material     _mat;
    private static float _sharedGlowTimer;
    private static int   _timerLastFrame = -1;
    private Color        _baseColour;
    private RoadTile     _tile;
    private bool         _ready = false;

    // URP Lit shader property IDs
    private static readonly int PropBaseColor  = Shader.PropertyToID("_BaseColor");
    private static readonly int PropColor      = Shader.PropertyToID("_Color");
    private static readonly int PropSurface    = Shader.PropertyToID("_Surface");
    private static readonly int PropBlend      = Shader.PropertyToID("_Blend");
    private static readonly int PropZWrite     = Shader.PropertyToID("_ZWrite");
    private static readonly int PropSrcBlend   = Shader.PropertyToID("_SrcBlend");
    private static readonly int PropDstBlend   = Shader.PropertyToID("_DstBlend");
    private static readonly int PropMetallic   = Shader.PropertyToID("_Metallic");
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
        yield return null;
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
        if (_mat  != null) Destroy(_mat);
        if (_cube != null) Destroy(_cube);
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every frame while the user is dragging a device.
    /// REQ 2: now also accepts the corner the cursor is currently
    /// hovering so the colour reflects whether that specific corner
    /// is correct for the chosen device.
    /// </summary>
    public void SetDragState(TrafficDeviceType device,
                             bool cursorOnOccupiedCorner,
                             TileCorner hoveredCorner = TileCorner.None)
    {
        _dragActive             = true;
        _dragDevice             = device;
        _cursorOnOccupiedCorner = cursorOnOccupiedCorner;
        _hoveredCorner          = hoveredCorner;
        if (_ready) EvaluateAndApply();
    }

    public void ClearDragState()
    {
        _dragActive             = false;
        _dragDevice             = TrafficDeviceType.None;
        _cursorOnOccupiedCorner = false;
        _hoveredCorner          = TileCorner.None;
        if (_ready) EvaluateAndApply();
    }

    public void SetState(OverlayState newState)
    {
        if (!_ready || _state == newState) return;
        _state = newState;
        ApplyState(newState);
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE EVALUATION  (REQ 2 — corner-aware)
    // ─────────────────────────────────────────────────────────────

    private void EvaluateAndApply()
    {
        if (_tile == null) { ApplyState(OverlayState.Hidden); return; }

        OverlayState next;

        if (!_dragActive)
        {
            // No drag: show whether the tile has any capacity left.
            next = TileFullyOccupied() ? OverlayState.Hidden : OverlayState.Available;
        }
        else
        {
            next = EvaluateDragState();
        }

        if (_state != next)
        {
            _state = next;
            ApplyState(next);
        }
    }

    /// <summary>
    /// REQ 2 core: evaluates the overlay colour using the same rules
    /// that IsSlotCorrect and CanPlace use, so the colour always matches
    /// what the placement system will actually do.
    /// </summary>
    private OverlayState EvaluateDragState()
    {
        // 1. Device not in allowed list → hidden (tile irrelevant for this device).
        bool allowed = _tile.allowedDevices.Count == 0 ||
                       _tile.allowedDevices.Contains(_dragDevice);
        if (!allowed)
            return OverlayState.NotSuitable;

        // 2. Tile has no correct placement rule for this device type at all.
        if (_tile.GetCorrectCountLimit(_dragDevice) <= 0)
            return OverlayState.NotSuitable;

        // 3. SpeedBump: only the center slot matters.
        if (_dragDevice == TrafficDeviceType.SpeedBump)
        {
            return _tile.IsCornerOccupied(TileCorner.Center)
                ? OverlayState.Occupied
                : OverlayState.Suitable;
        }

        // 4. Corner cap already reached (e.g. all 4 stop-sign slots filled).
        int correctLimit = _tile.GetCorrectCountLimit(_dragDevice);
        int existingOfType = 0;
        foreach (var s in _tile.Slots)
            if (s.deviceType == _dragDevice) existingOfType++;

        if (existingOfType >= correctLimit)
            return OverlayState.Occupied;   // no more correct slots of this type

        // 5. All four corner slots (physical) are already taken.
        if (AllCornersFilled())
            return OverlayState.Occupied;

        // 6. The cursor is directly on an already-occupied corner.
        if (_cursorOnOccupiedCorner)
            return OverlayState.Occupied;

        // 7. REQ 2: check whether the hovered corner would be CORRECT.
        //    We mirror IsSlotCorrect's per-device corner rules here.
        if (_hoveredCorner != TileCorner.None && _hoveredCorner != TileCorner.Center)
        {
            bool cornerWouldBeCorrect = IsCornerCorrectForDevice(_dragDevice, _hoveredCorner);
            return cornerWouldBeCorrect ? OverlayState.Suitable : OverlayState.NotSuitable;
        }

        // 8. No specific corner known yet (cursor not on any corner) → Suitable
        //    as long as there is at least one valid corner available.
        if (HasAtLeastOneValidCorner(_dragDevice))
            return OverlayState.Suitable;

        return OverlayState.NotSuitable;
    }

    // ─────────────────────────────────────────────────────────────
    //  CORNER CORRECTNESS HELPERS  (REQ 2)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if placing <paramref name="device"/> at
    /// <paramref name="corner"/> on this tile would be considered
    /// correct by IsSlotCorrect — without actually placing anything.
    /// Mirrors the switch block in IsSlotCorrect exactly.
    /// </summary>
    private bool IsCornerCorrectForDevice(TrafficDeviceType device, TileCorner corner)
    {
        switch (device)
        {
            case TrafficDeviceType.StopSign:
                if (_tile.segmentType != TileSegmentType.End) return false;
                // REQ 4: all four corners are correct on End tiles.
                return corner == TileCorner.NorthWest
                    || corner == TileCorner.NorthEast
                    || corner == TileCorner.SouthEast
                    || corner == TileCorner.SouthWest;

            case TrafficDeviceType.TrafficLight:
                if (_tile.segmentType == TileSegmentType.End)
                {
                    // REQ 3: up to 2 lights, each at a unique far-end corner.
                    if (!_tile.IsAtFarEnd(corner)) return false;
                    // Check whether this far-end corner is already taken by a light.
                    foreach (var s in _tile.Slots)
                        if (s.deviceType == TrafficDeviceType.TrafficLight && s.corner == corner)
                            return false;
                    return true;
                }
                if (_tile.segmentType == TileSegmentType.Intersection)
                {
                    foreach (var s in _tile.Slots)
                        if (s.deviceType == TrafficDeviceType.TrafficLight && s.corner == corner)
                            return false;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns true when at least one free, correct corner exists for
    /// <paramref name="device"/> — used when the cursor is not yet
    /// hovering a specific corner.
    /// </summary>
    private bool HasAtLeastOneValidCorner(TrafficDeviceType device)
    {
        TileCorner[] corners =
        {
            TileCorner.NorthWest, TileCorner.NorthEast,
            TileCorner.SouthEast, TileCorner.SouthWest
        };
        foreach (TileCorner c in corners)
        {
            if (_tile.IsCornerOccupied(c)) continue;
            if (IsCornerCorrectForDevice(device, c)) return true;
        }
        return false;
    }

    private bool AllCornersFilled()
    {
        if (_tile == null) return false;
        return _tile.IsCornerOccupied(TileCorner.NorthWest)
            && _tile.IsCornerOccupied(TileCorner.NorthEast)
            && _tile.IsCornerOccupied(TileCorner.SouthEast)
            && _tile.IsCornerOccupied(TileCorner.SouthWest);
    }

    private bool TileFullyOccupied()
    {
        if (_tile == null) return true;
        return _tile.isOccupied;
    }

    // ─────────────────────────────────────────────────────────────
    //  GLOW ANIMATION
    // ─────────────────────────────────────────────────────────────

    private void AnimateGlow()
    {
        if (_mat == null || _cube == null || !_cube.activeSelf) return;
        if (_state == OverlayState.Hidden) return;

        RefreshLiveParams();

        if (Time.frameCount != _timerLastFrame)
        {
            _sharedGlowTimer += Time.deltaTime * _glowSpeed;
            _timerLastFrame   = Time.frameCount;
        }

        float t     = (Mathf.Sin(_sharedGlowTimer * Mathf.PI * 2f) + 1f) * 0.5f;
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

        _offset        = mgr.cubeOffset;
        _slabThickness = mgr.slabThickness;
        _colAvailable  = mgr.colourAvailable;
        _colOccupied   = mgr.colourOccupied;
        _colSuitable   = mgr.colourSuitable;
        _colNotSuitable = mgr.colourNotSuitable;
        _glowSpeed     = mgr.glowSpeed;
        _startAlpha    = mgr.startAlpha;
        _endAlpha      = mgr.endAlpha;
        _metallic      = mgr.metallic;
        _smoothness    = mgr.smoothness;
    }

    private void RefreshLiveParams()
    {
        var mgr = TileOverlayManager.Instance;
        if (mgr == null) return;

        _glowSpeed      = mgr.glowSpeed;
        _startAlpha     = mgr.startAlpha;
        _endAlpha       = mgr.endAlpha;
        _colAvailable   = mgr.colourAvailable;
        _colOccupied    = mgr.colourOccupied;
        _colSuitable    = mgr.colourSuitable;
        _colNotSuitable = mgr.colourNotSuitable;
        _baseColour     = ColourForState(_state);

        if (_mat != null)
        {
            float m = mgr.metallic;
            float s = mgr.smoothness;
            if (!Mathf.Approximately(_metallic, m) || !Mathf.Approximately(_smoothness, s))
            {
                _metallic   = m;
                _smoothness = s;
                _mat.SetFloat(PropMetallic,   _metallic);
                _mat.SetFloat(PropSmoothness, _smoothness);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  CUBE CONSTRUCTION
    // ─────────────────────────────────────────────────────────────

    private void BuildCube()
    {
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "TileEffectCube";
        _cube.transform.SetParent(transform, false);

        var bc = _cube.GetComponent<BoxCollider>();
        if (bc != null) Destroy(bc);

        var col = GetComponent<BoxCollider>();
        Vector3 sz  = col != null ? col.size   : Vector3.one;
        Vector3 ctr = col != null ? col.center : Vector3.zero;

        _cube.transform.localPosition = new Vector3(
            ctr.x + _offset.x,
            ctr.y + sz.y * 0.5f + _offset.y,
            ctr.z + _offset.z);
        _cube.transform.localRotation = Quaternion.identity;
        _cube.transform.localScale    = new Vector3(sz.x, _slabThickness, sz.z);

        _mat = BuildTransparentMaterial();

        var rend = _cube.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material           = _mat;
            rend.shadowCastingMode  = ShadowCastingMode.Off;
            rend.receiveShadows     = false;
        }

        _cube.SetActive(false);
    }

    private Material BuildTransparentMaterial()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogWarning("[TileOverlay] URP/Lit shader not found. Falling back to Transparent/Diffuse.");
            Shader legacy = Shader.Find("Transparent/Diffuse")
                         ?? Shader.Find("Unlit/Transparent");
            return legacy != null ? new Material(legacy) : new Material(Shader.Find("Standard"));
        }

        var mat = new Material(urpLit);
        mat.name = "TileOverlay_Transparent (instance)";

        mat.SetFloat(PropSurface,  1f);
        mat.SetFloat(PropBlend,    0f);
        mat.SetFloat(PropZWrite,   0f);
        mat.SetFloat(PropSrcBlend, (float)BlendMode.SrcAlpha);
        mat.SetFloat(PropDstBlend, (float)BlendMode.OneMinusSrcAlpha);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");

        mat.renderQueue = (int)RenderQueue.Transparent;

        mat.SetColor(PropBaseColor,  Color.white);
        mat.SetFloat(PropMetallic,   _metallic);
        mat.SetFloat(PropSmoothness, _smoothness);

        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    //  STATE APPLICATION
    // ─────────────────────────────────────────────────────────────

    private void ApplyState(OverlayState state)
    {
        if (_cube == null) return;

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
        OverlayState.Available   => _colAvailable,
        OverlayState.Occupied    => _colOccupied,
        OverlayState.Suitable    => _colSuitable,
        OverlayState.NotSuitable => _colNotSuitable,
        _                        => _colSuitable,
    };

    // ─────────────────────────────────────────────────────────────
    //  MATERIAL COLOUR
    // ─────────────────────────────────────────────────────────────

    private void SetColour(Color c)
    {
        if (_mat == null) return;
        if (_mat.HasProperty(PropBaseColor)) _mat.SetColor(PropBaseColor, c);
        if (_mat.HasProperty(PropColor))     _mat.SetColor(PropColor,     c);
    }
}