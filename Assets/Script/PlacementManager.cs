using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────
//  PlacementProxy — unchanged.
// ─────────────────────────────────────────────────────────────────

public class PlacementProxy : MonoBehaviour, IPointerDownHandler
{
    public System.Action onDown;
    public void OnPointerDown(PointerEventData _) => onDown?.Invoke();
}

// ─────────────────────────────────────────────────────────────────
//  PlacementManager (v3)
//
//  NEW vs v2:
//    • Middle mouse click cycles ghost facing direction (N→E→S→W).
//      Once clicked, user's facing overrides the corner default
//      for the remainder of the drag.
//    • Facing direction passed through to RoadTile.PlaceDevice.
//    • Ghost tint: green = correct, orange = placeable but incorrect
//      (NA / wrong corner / wrong facing), red = blocked.
//    • Speed bump locks to Center and ignores rotation overrides.
// ─────────────────────────────────────────────────────────────────

public class PlacementManager : MonoBehaviour
{
    public static PlacementManager Instance { get; private set; }

    [System.Serializable]
    public class DeviceClickTarget
    {
        public GameObject clickableObject;
        public TrafficDeviceType deviceType;
    }

    [Header("Universal Click Targets")]
    public DeviceClickTarget[] clickTargets;

    [Header("City Scene")]
    [SceneName] public string citySceneName = "City";

    [Header("Device Prefabs (3D world objects)")]
    public GameObject stopSignPrefab;
    public GameObject speedBumpPrefab;
    public GameObject trafficLightPrefab;

    [Header("Ghost Material  (leave null — generated in code)")]
    public Material ghostMaterial;

    [Header("Ghost Tint Colours")]
    public Color tintValid = new Color(0.20f, 1.00f, 0.30f, 0.55f);
    public Color tintWarning = new Color(1.00f, 0.60f, 0.00f, 0.55f);
    public Color tintBlocked = new Color(1.00f, 0.15f, 0.15f, 0.55f);
    public Color tintNeutral = new Color(0.80f, 0.80f, 0.80f, 0.45f);

    [Header("Raycast")]
    public LayerMask roadTileLayer;
    public Camera cityCamera;

    [Header("Ghost Height Offset")]
    public float ghostYOffset = 0.1f;

    // Runtime
    private TrafficDeviceType _selectedDevice = TrafficDeviceType.None;
    private GameObject _ghostObject = null;
    private bool _isDragging = false;
    private RoadTile _hoveredTile = null;
    private TileCorner _hoveredCorner = TileCorner.None;
    private Material _generatedGhostMat;

    // Middle-mouse rotation: user's facing override for the current drag (None = use corner default)
    private FacingDirection _userFacing = FacingDirection.None;

    public bool IsDragging => _isDragging;

    private Vector2 MousePosition => Mouse.current.position.ReadValue();
    private bool LeftButtonUp => Mouse.current.leftButton.wasReleasedThisFrame;
    private bool RightButtonDown => Mouse.current.rightButton.wasPressedThisFrame;
    private bool MiddleButtonDown => Mouse.current.middleButton.wasPressedThisFrame;

    // ─────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (cityCamera == null)
        {
            cityCamera = Camera.main;
            if (cityCamera == null)
                Debug.LogError("[PlacementManager] cityCamera is null and Camera.main not found.");
        }
        SetupClickTargets();
    }

    private void OnDestroy()
    {
        if (_generatedGhostMat != null) Destroy(_generatedGhostMat);
    }

    private void Update()
    {
        if (!_isDragging || Mouse.current == null) return;

        if (RightButtonDown ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        // Middle mouse: cycle facing — only meaningful for stop sign / traffic light
        if (MiddleButtonDown && _selectedDevice != TrafficDeviceType.SpeedBump)
        {
            if (_userFacing == FacingDirection.None)
                _userFacing = _hoveredTile != null
                    ? _hoveredTile.GetDefaultFacing(
                        _hoveredCorner != TileCorner.None ? _hoveredCorner : TileCorner.NorthWest,
                        _selectedDevice)
                    : FacingDirection.PosZ;

            _userFacing = NextFacing(_userFacing);
        }

        UpdateHoverOverlay();
        MoveGhostToMouse();

        if (LeftButtonUp) ConfirmPlacement();
    }

    private static FacingDirection NextFacing(FacingDirection f) => f switch
    {
        FacingDirection.PosZ => FacingDirection.PosX,
        FacingDirection.PosX => FacingDirection.NegZ,
        FacingDirection.NegZ => FacingDirection.NegX,
        FacingDirection.NegX => FacingDirection.PosZ,
        _ => FacingDirection.PosZ
    };

    // ─────────────────────────────────────────
    //  CLICK TARGETS
    // ─────────────────────────────────────────

    private void SetupClickTargets()
    {
        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null) continue;
            var proxy = data.clickableObject.GetComponent<PlacementProxy>()
                     ?? data.clickableObject.AddComponent<PlacementProxy>();
            TrafficDeviceType type = data.deviceType;
            proxy.onDown = () => BeginDrag(type);
        }
        Debug.Log($"[PlacementManager] Registered {clickTargets.Length} placement targets.");
    }

    public void BeginDrag(TrafficDeviceType deviceType)
    {
        if (GameManager.Instance == null) { Debug.LogError("[PlacementManager] GameManager null."); return; }
        if (!GameManager.Instance.GameRunning) return;

        float cost = DeviceData.GetCost(deviceType);
        if (GameManager.Instance.Capital < cost)
        {
            Debug.Log($"[PlacementManager] Cannot afford {deviceType} (RM{cost}).");
            return;
        }

        _selectedDevice = deviceType;
        _isDragging = true;
        _hoveredCorner = TileCorner.None;
        _userFacing = FacingDirection.None; // reset on each new drag

        ShowPlacementOverlays(deviceType);
        SpawnGhost(deviceType);

        GameManager.Instance.PauseDayTick();

        Debug.Log($"[PlacementManager] Drag start: {deviceType}  cost=RM{cost}");
    }

    public void BeginDragStopSign() => BeginDrag(TrafficDeviceType.StopSign);
    public void BeginDragSpeedBump() => BeginDrag(TrafficDeviceType.SpeedBump);
    public void BeginDragTrafficLight() => BeginDrag(TrafficDeviceType.TrafficLight);

    // ─────────────────────────────────────────
    //  GHOST MATERIAL
    // ─────────────────────────────────────────

    private Material GetOrCreateGhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        if (_generatedGhostMat != null) return _generatedGhostMat;

        Shader shader = Shader.Find("Unlit/Transparent")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("HDRP/Unlit")
                     ?? Shader.Find("Standard");

        if (shader == null)
        {
            Debug.LogError("[PlacementManager] No usable shader found for ghost material.");
            return null;
        }

        _generatedGhostMat = new Material(shader) { name = "GhostMaterial_Generated" };
        _generatedGhostMat.color = tintNeutral;

        if (_generatedGhostMat.HasProperty("_SrcBlend"))
            _generatedGhostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (_generatedGhostMat.HasProperty("_DstBlend"))
            _generatedGhostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_generatedGhostMat.HasProperty("_ZWrite"))
            _generatedGhostMat.SetInt("_ZWrite", 0);
        if (_generatedGhostMat.HasProperty("_Mode"))
            _generatedGhostMat.SetFloat("_Mode", 3f);
        if (_generatedGhostMat.HasProperty("_Surface"))
            _generatedGhostMat.SetFloat("_Surface", 1f);

        _generatedGhostMat.DisableKeyword("_ALPHATEST_ON");
        _generatedGhostMat.EnableKeyword("_ALPHABLEND_ON");
        _generatedGhostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _generatedGhostMat.renderQueue = 3000;

        Debug.Log($"[PlacementManager] Generated ghost material using shader '{shader.name}'.");
        return _generatedGhostMat;
    }

    private void SpawnGhost(TrafficDeviceType deviceType)
    {
        DestroyGhost();

        GameObject prefab = GetPrefab(deviceType);
        if (prefab == null) return;

        _ghostObject = Instantiate(prefab);
        _ghostObject.name = $"Ghost_{deviceType}";

        Material gm = GetOrCreateGhostMaterial();
        if (gm != null)
            foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
                r.material = gm;

        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
    }

    // ─────────────────────────────────────────
    //  GHOST POSITION + TINT
    // ─────────────────────────────────────────

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null || cityCamera == null) return;

        RoadTile hit = RaycastToTile(out Vector3 hitPoint);

        if (hit != null)
        {
            TileCorner corner = hit.GetNearestCorner(hitPoint, _selectedDevice);
            _hoveredCorner = corner;

            // Decide facing: user override (if any) takes priority, except speed bump
            FacingDirection facing = (_selectedDevice == TrafficDeviceType.SpeedBump)
                ? hit.GetDefaultFacing(TileCorner.Center, _selectedDevice)
                : (_userFacing != FacingDirection.None
                    ? _userFacing
                    : hit.GetDefaultFacing(corner, _selectedDevice));

            _ghostObject.transform.SetParent(hit.transform, worldPositionStays: false);
            _ghostObject.transform.localPosition =
                hit.GetCornerLocalPosition(corner) + Vector3.up * ghostYOffset;
            _ghostObject.transform.localRotation = hit.FacingToLocalRotation(facing);

            // Apply speed bump width preview
            if (_selectedDevice == TrafficDeviceType.SpeedBump)
            {
                Vector3 s = (hit.forwardAxis == ForwardAxis.LocalPosZ ||
                             hit.forwardAxis == ForwardAxis.LocalNegZ)
                    ? new Vector3(1f, 1f, hit.speedBumpWidthScale)
                    : new Vector3(hit.speedBumpWidthScale, 1f, 1f);
                _ghostObject.transform.localScale = s;
            }
            else
            {
                _ghostObject.transform.localScale = Vector3.one;
            }

            // Tint logic
            bool cornerTaken = hit.IsCornerOccupied(corner);
            bool full = hit.PlacedCount >= hit.maxDevices;
            bool deviceAllowed = hit.allowedDevices.Count == 0 || hit.allowedDevices.Contains(_selectedDevice);

            // Simulate the would-be slot's correctness
            bool wouldBeCorrect = SimulateCorrectness(hit, _selectedDevice, corner, facing);

            if (!deviceAllowed || cornerTaken || full)
                SetGhostTint(tintBlocked);
            else if (!wouldBeCorrect)
                SetGhostTint(tintWarning);
            else
                SetGhostTint(tintValid);
        }
        else
        {
            _ghostObject.transform.SetParent(null);
            _hoveredCorner = TileCorner.None;

            Ray ray = cityCamera.ScreenPointToRay(MousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float dist))
            {
                _ghostObject.transform.position = ray.GetPoint(dist) + Vector3.up * ghostYOffset;
                _ghostObject.transform.rotation = Quaternion.identity;
            }
            SetGhostTint(tintNeutral);
        }
    }

    /// <summary>
    /// Predicts whether placing this device here would count as correct,
    /// without actually placing anything. Mirrors RoadTile.IsSlotCorrect.
    /// </summary>
    private bool SimulateCorrectness(RoadTile tile, TrafficDeviceType device, TileCorner corner, FacingDirection facing)
    {
        int limit = tile.GetCorrectCountLimit(device);
        if (limit <= 0) return false;

        // Count existing same-type slots — if already at limit, this one wouldn't count.
        int existingSameType = 0;
        foreach (var s in tile.Slots)
            if (s.deviceType == device) existingSameType++;
        if (existingSameType >= limit) return false;

        switch (device)
        {
            case TrafficDeviceType.SpeedBump:
                return corner == TileCorner.Center;

            case TrafficDeviceType.StopSign:
                if (tile.segmentType != TileSegmentType.End) return false;
                return tile.IsAtFarEnd(corner) && facing == tile.BackwardFacing;

            case TrafficDeviceType.TrafficLight:
                if (tile.segmentType == TileSegmentType.End)
                    return tile.IsAtFarEnd(corner) && facing == tile.BackwardFacing;
                if (tile.segmentType == TileSegmentType.Intersection)
                {
                    foreach (var s in tile.Slots)
                        if (s.deviceType == TrafficDeviceType.TrafficLight && s.facing == facing)
                            return false; // duplicate facing
                    return true;
                }
                return false;
        }
        return false;
    }

    private void SetGhostTint(Color color)
    {
        if (_ghostObject == null) return;
        foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
            r.material.color = color;
    }

    private void DestroyGhost()
    {
        if (_ghostObject != null) Destroy(_ghostObject);
        _ghostObject = null;
    }

    // ─────────────────────────────────────────
    //  OVERLAY
    // ─────────────────────────────────────────

    private void UpdateHoverOverlay() => _hoveredTile = RaycastToTile(out _);

    private void ShowPlacementOverlays(TrafficDeviceType device)
    {
        RoadTile[] all = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile t in all)
            if (t.Overlay != null) t.Overlay.SetState(t.GetOverlayState(device));
    }

    private void ResetAllOverlays()
    {
        RoadTile[] all = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile t in all)
            if (t.Overlay != null)
                t.Overlay.SetState(t.isOccupied ? OverlayState.Occupied : OverlayState.Default);
    }

    // ─────────────────────────────────────────
    //  CONFIRM / CANCEL
    // ─────────────────────────────────────────

    private void ConfirmPlacement()
    {
        _isDragging = false;

        if (GameManager.Instance != null)
            GameManager.Instance.ResumeDayTick();

        if (_hoveredTile == null) { Debug.Log("[PlacementManager] No tile under cursor."); CancelPlacement(); return; }

        GameObject prefab = GetPrefab(_selectedDevice);
        if (prefab == null) { CancelPlacement(); return; }

        TileCorner targetCorner = _selectedDevice == TrafficDeviceType.SpeedBump
            ? TileCorner.Center
            : _hoveredCorner;

        FacingDirection facing = (_selectedDevice == TrafficDeviceType.SpeedBump)
            ? _hoveredTile.GetDefaultFacing(TileCorner.Center, _selectedDevice)
            : (_userFacing != FacingDirection.None
                ? _userFacing
                : _hoveredTile.GetDefaultFacing(targetCorner, _selectedDevice));

        if (targetCorner == TileCorner.None) { CancelPlacement(); return; }

        GameObject deviceObj = Instantiate(prefab);
        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid()) SceneManager.MoveGameObjectToScene(deviceObj, cityScene);

        PlacementResult result = GameManager.Instance.TryPlaceDevice(
            _hoveredTile, _selectedDevice, targetCorner, facing, deviceObj);

        switch (result)
        {
            case PlacementResult.Success:
                Debug.Log($"[PlacementManager] Placed CORRECT {_selectedDevice} @ {targetCorner} facing {facing} on {_hoveredTile.tileID}");
                break;
            case PlacementResult.PoorPlacement:
                Debug.Log($"[PlacementManager] Placed INCORRECT {_selectedDevice} @ {targetCorner} facing {facing} on {_hoveredTile.tileID}");
                break;
            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Slot or tile full."); Destroy(deviceObj); break;
            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Insufficient capital."); Destroy(deviceObj); break;
            case PlacementResult.DeviceNotAllowed:
                Debug.Log("[PlacementManager] Device not allowed here."); Destroy(deviceObj); break;
            default:
                Destroy(deviceObj); break;
        }

        DestroyGhost();
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        _hoveredCorner = TileCorner.None;
        _userFacing = FacingDirection.None;
        ResetAllOverlays();
    }

    public void CancelPlacement()
    {
        _isDragging = false;

        if (GameManager.Instance != null)
            GameManager.Instance.ResumeDayTick();
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        _hoveredCorner = TileCorner.None;
        _userFacing = FacingDirection.None;
        DestroyGhost();
        ResetAllOverlays();
        Debug.Log("[PlacementManager] Placement cancelled.");
    }

    // ─────────────────────────────────────────
    //  RAYCAST
    // ─────────────────────────────────────────

    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (cityCamera == null) return null;
        if (!_isDragging && IsPointerOverUI()) return null;

        Ray ray = cityCamera.ScreenPointToRay(MousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, roadTileLayer))
        {
            hitPoint = hit.point;
            return hit.collider.GetComponent<RoadTile>();
        }
        return null;
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        var pd = new PointerEventData(EventSystem.current) { position = MousePosition };
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pd, results);
        return results.Count > 0;
    }

    private GameObject GetPrefab(TrafficDeviceType type) => type switch
    {
        TrafficDeviceType.StopSign => stopSignPrefab,
        TrafficDeviceType.SpeedBump => speedBumpPrefab,
        TrafficDeviceType.TrafficLight => trafficLightPrefab,
        _ => null
    };
}