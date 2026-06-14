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
//  PlacementManager (v7 — per-corner overlay hover)
//
//  CHANGES vs v6:
//    • ShowPlacementOverlays / UpdateHoverOverlay push drag context
//      into each tile's TileOverlay via SetDragState / ClearDragState,
//      enabling per-corner red-on-hover colouring.
//    • ResetAllOverlays calls ClearDragState on all tiles instead of
//      hard-setting Available/Occupied.
//    • Everything else identical to v6.
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

    [Header("Device Sprites (same assets used in UI)")]
    public Sprite stopSignSprite;
    public Sprite speedBumpSprite;
    public Sprite trafficLightSprite;

    [Header("Sprite Settings")]
    [Tooltip("World-space size of placed device icons (in Unity units).")]
    [Min(0.05f)] public float deviceIconWorldSize = 0.5f;

    [Tooltip("Sorting order for device sprites (higher = drawn on top).")]
    public int spriteSortingOrder = 10;

    [Tooltip("When true, device icons always face the camera (recommended for angled cameras).")]
    public bool billboardIcons = true;

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

    public bool IsDragging => _isDragging;

    private Vector2 MousePosition => Mouse.current.position.ReadValue();
    private bool LeftButtonUp => Mouse.current.leftButton.wasReleasedThisFrame;
    private bool RightButtonDown => Mouse.current.rightButton.wasPressedThisFrame;

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

    private void Update()
    {
        if (!_isDragging || Mouse.current == null) return;

        if (RightButtonDown ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        UpdateHoverOverlay();
        MoveGhostToMouse();

        if (LeftButtonUp) ConfirmPlacement();
    }

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

        ShowPlacementOverlays(deviceType);
        SpawnGhost(deviceType);

        GameManager.Instance.PauseDayTick();

        Debug.Log($"[PlacementManager] Drag start: {deviceType}  cost=RM{cost}");
    }

    public void BeginDragStopSign() => BeginDrag(TrafficDeviceType.StopSign);
    public void BeginDragSpeedBump() => BeginDrag(TrafficDeviceType.SpeedBump);
    public void BeginDragTrafficLight() => BeginDrag(TrafficDeviceType.TrafficLight);

    // ─────────────────────────────────────────
    //  SPRITE OBJECT FACTORY
    // ─────────────────────────────────────────

    private GameObject CreateSpriteObject(Sprite sprite, string name, bool isGhost = false)
    {
        GameObject go = new GameObject(name);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = spriteSortingOrder;

        // Scale so the sprite's widest dimension = deviceIconWorldSize.
        if (sprite != null && sprite.pixelsPerUnit > 0f)
        {
            float ppu = sprite.pixelsPerUnit;
            float maxPx = Mathf.Max(sprite.rect.width, sprite.rect.height);
            float currentWorldSize = maxPx / ppu;
            if (currentWorldSize > 0f)
            {
                float s = deviceIconWorldSize / currentWorldSize;
                go.transform.localScale = new Vector3(s, s, s);
            }
        }

        if (billboardIcons && !isGhost)
            go.AddComponent<BillboardSprite>();

        return go;
    }

    // ─────────────────────────────────────────
    //  GHOST
    // ─────────────────────────────────────────

    private void SpawnGhost(TrafficDeviceType deviceType)
    {
        DestroyGhost();

        Sprite sprite = GetSprite(deviceType);
        if (sprite == null) { Debug.LogWarning($"[PlacementManager] No sprite for {deviceType}."); return; }

        _ghostObject = CreateSpriteObject(sprite, $"Ghost_{deviceType}", isGhost: true);

        // Ghost always billboards so the player sees it while dragging.
        _ghostObject.AddComponent<BillboardSprite>();

        SetGhostTint(tintNeutral);

        // Disable any accidental colliders.
        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
    }

    private void DestroyGhost()
    {
        if (_ghostObject != null) Destroy(_ghostObject);
        _ghostObject = null;
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

            _ghostObject.transform.SetParent(hit.transform, worldPositionStays: false);
            _ghostObject.transform.localPosition =
                hit.GetCornerLocalPosition(corner) + Vector3.up * ghostYOffset;

            // Tint logic.
            bool cornerTaken = hit.IsCornerOccupied(corner);
            bool full = hit.PlacedCount >= hit.maxDevices;
            bool deviceAllowed = hit.allowedDevices.Count == 0 ||
                                 hit.allowedDevices.Contains(_selectedDevice);
            bool wouldBeCorrect = SimulateCorrectness(hit, _selectedDevice, corner);

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
                _ghostObject.transform.position = ray.GetPoint(dist) + Vector3.up * ghostYOffset;

            SetGhostTint(tintNeutral);
        }
    }

    private bool SimulateCorrectness(RoadTile tile, TrafficDeviceType device, TileCorner corner)
    {
        int limit = tile.GetCorrectCountLimit(device);
        if (limit <= 0) return false;

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
                return tile.IsAtFarEnd(corner);

            case TrafficDeviceType.TrafficLight:
                if (tile.segmentType == TileSegmentType.End)
                    return tile.IsAtFarEnd(corner);
                if (tile.segmentType == TileSegmentType.Intersection)
                {
                    foreach (var s in tile.Slots)
                        if (s.deviceType == TrafficDeviceType.TrafficLight && s.corner == corner)
                            return false;
                    return true;
                }
                return false;
        }
        return false;
    }

    private void SetGhostTint(Color color)
    {
        if (_ghostObject == null) return;
        SpriteRenderer sr = _ghostObject.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = color;
    }

    // ─────────────────────────────────────────
    //  OVERLAY  (v7 — per-corner hover via SetDragState)
    // ─────────────────────────────────────────

    /// <summary>
    /// Called every Update frame while dragging.
    /// Pushes the current hovered tile + corner into each tile's overlay
    /// so TileOverlay can colour the hovered occupied corner red.
    /// </summary>
    private void UpdateHoverOverlay()
    {
        RoadTile nowHovered = RaycastToTile(out Vector3 hitPoint);

        if (nowHovered != _hoveredTile)
        {
            // Refresh the tile we just left
            if (_hoveredTile != null && _hoveredTile.Overlay != null)
                _hoveredTile.Overlay.SetDragState(_selectedDevice, false);

            _hoveredTile = nowHovered;
        }

        if (_hoveredTile != null && _hoveredTile.Overlay != null)
        {
            TileCorner corner = _hoveredTile.GetNearestCorner(hitPoint, _selectedDevice);
            bool onOccupied = _hoveredTile.IsCornerOccupied(corner);
            _hoveredTile.Overlay.SetDragState(_selectedDevice, onOccupied);
        }
    }

    private void ShowPlacementOverlays(TrafficDeviceType device)
    {
        RoadTile[] all = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile t in all)
            if (t.Overlay != null)
                t.Overlay.SetDragState(device, cursorOnOccupiedCorner: false);
    }

    private void ResetAllOverlays()
    {
        RoadTile[] all = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile t in all)
            if (t.Overlay != null)
                t.Overlay.ClearDragState();
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

        Sprite sprite = GetSprite(_selectedDevice);
        if (sprite == null) { CancelPlacement(); return; }

        TileCorner targetCorner = _selectedDevice == TrafficDeviceType.SpeedBump
            ? TileCorner.Center
            : _hoveredCorner;

        if (targetCorner == TileCorner.None) { CancelPlacement(); return; }

        GameObject deviceObj = CreateSpriteObject(sprite, $"Device_{_selectedDevice}");

        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(deviceObj, cityScene);

        PlacementResult result = GameManager.Instance.TryPlaceDevice(
            _hoveredTile, _selectedDevice, targetCorner, deviceObj);

        switch (result)
        {
            case PlacementResult.Success:
                Debug.Log($"[PlacementManager] Placed CORRECT {_selectedDevice} @ {targetCorner} on {_hoveredTile.tileID}");
                break;
            case PlacementResult.PoorPlacement:
                Debug.Log($"[PlacementManager] Placed INCORRECT {_selectedDevice} @ {targetCorner} on {_hoveredTile.tileID}");
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

    private Sprite GetSprite(TrafficDeviceType type) => type switch
    {
        TrafficDeviceType.StopSign => stopSignSprite,
        TrafficDeviceType.SpeedBump => speedBumpSprite,
        TrafficDeviceType.TrafficLight => trafficLightSprite,
        _ => null
    };
}