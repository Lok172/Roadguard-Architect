using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  PlacementProxy 
// ─────────────────────────────────────────────────────────────────

public class PlacementProxy : MonoBehaviour, IPointerDownHandler
{
    public System.Action onDown;

    public void OnPointerDown(PointerEventData _)
    {

        Button btn = GetComponent<Button>();
        if (btn != null && !btn.interactable) return;
        onDown?.Invoke();
    }
}



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

    [Header("Device")]
    public Color DisabledTint = new Color(0.5f, 0.5f, 0.5f, 0.6f);
    public Color EnabledTint = new Color(1f, 1f, 1f, 1f);

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
        StartCoroutine(SubscribeToCapitalChanges());
    }

    // ─────────────────────────────────────────
    //  DEVICE BUTTON AFFORDABILITY  (Issue 4 fix)
    // ─────────────────────────────────────────

    private IEnumerator SubscribeToCapitalChanges()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.OnCapitalChanged.AddListener(RefreshDeviceButtonStates);
        RefreshDeviceButtonStates(GameManager.Instance.Capital);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnCapitalChanged.RemoveListener(RefreshDeviceButtonStates);
    }

    /// <summary>
    /// Tints the icon Image and TMP text of each device button when
    /// its cost exceeds the player's current capital, so the dim state
    /// is visible on the icon and label — not just the panel background.
    /// </summary>
    private void RefreshDeviceButtonStates(float capital)
    {
        if (clickTargets == null) return;

        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null) continue;

            float cost = DeviceData.GetCost(data.deviceType);
            bool affordable = capital >= cost;
            Color tint = affordable ? EnabledTint : DisabledTint;

            // Gate the button interactable state
            Button btn = data.clickableObject.GetComponent<Button>();
            if (btn != null) btn.interactable = affordable;

            // Tint all Image children (icon sprite + panel background)
            foreach (Image img in data.clickableObject.GetComponentsInChildren<Image>(true))
                img.color = tint;


        }
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

            StripChildInputHandlers(data.clickableObject);

            var proxy = data.clickableObject.GetComponent<PlacementProxy>()
                     ?? data.clickableObject.AddComponent<PlacementProxy>();
            TrafficDeviceType type = data.deviceType;
            proxy.onDown = () => BeginDrag(type);
        }
        Debug.Log($"[PlacementManager] Registered {clickTargets.Length} placement targets.");
    }

    /// <summary>
    /// Unity's UI input module bubbles a pointer press up from the actual hit
    /// GameObject to the nearest ancestor implementing IPointerDownHandler —
    /// but stops at the FIRST one it finds. A child (e.g. the icon) carrying
    /// its own Button/EventTrigger — usually left over from how the button
    /// prefab was assembled — intercepts the press there and it never reaches
    /// this click target's own PlacementProxy. Strip those children and turn
    /// off their raycastTarget so every part of the button (icon, label,
    /// background) starts a drag the same way. Never touches clickableObject
    /// itself, only its children.
    /// </summary>
    private static void StripChildInputHandlers(GameObject clickableObject)
    {
        foreach (Button childButton in clickableObject.GetComponentsInChildren<Button>(true))
        {
            if (childButton.gameObject == clickableObject) continue;
            Destroy(childButton);
        }

        foreach (EventTrigger childTrigger in clickableObject.GetComponentsInChildren<EventTrigger>(true))
        {
            if (childTrigger.gameObject == clickableObject) continue;
            Destroy(childTrigger);
        }

        foreach (Graphic graphic in clickableObject.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.gameObject == clickableObject) continue;
            graphic.raycastTarget = false;
        }
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
        SetCarMovement(false);
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

        if (sprite != null && sprite.pixelsPerUnit > 0f)
        {
            float maxDim = Mathf.Max(sprite.rect.width, sprite.rect.height) / sprite.pixelsPerUnit;
            float scale = maxDim > 0f ? deviceIconWorldSize / maxDim : 1f;
            go.transform.localScale = new Vector3(scale, scale, scale);
        }

        // Placed devices (not the drag ghost) always face the camera.
        // The ghost handles its own rotation in MoveGhostToMouse via billboardIcons.
        if (!isGhost)
            go.AddComponent<BillboardSprite>();

        return go;
    }

    private void SpawnGhost(TrafficDeviceType deviceType)
    {
        DestroyGhost();
        Sprite sprite = GetSprite(deviceType);
        if (sprite == null) return;

        _ghostObject = CreateSpriteObject(sprite, "PlacementGhost", isGhost: true);
        SetGhostTint(tintNeutral);
    }

    private void DestroyGhost()
    {
        if (_ghostObject != null) { Destroy(_ghostObject); _ghostObject = null; }
    }

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null || cityCamera == null) return;

        RoadTile tile = RaycastToTile(out Vector3 hitPoint);

        if (billboardIcons && _ghostObject != null)
            _ghostObject.transform.rotation = Quaternion.LookRotation(cityCamera.transform.forward);

        if (tile != null)
        {
            TileCorner corner = tile.GetNearestCorner(hitPoint, _selectedDevice);
            _hoveredCorner = corner;
            Vector3 localPos = tile.GetCornerLocalPosition(corner);
            _ghostObject.transform.SetParent(tile.transform, false);
            _ghostObject.transform.localPosition = localPos + Vector3.up * ghostYOffset;

            bool onOccupied = tile.IsCornerOccupied(corner);
            if (onOccupied)
                SetGhostTint(tintBlocked);
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
                // Mirrors RoadTile.IsSlotCorrect: far-end corners only.
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
    //  OVERLAY 
    // ─────────────────────────────────────────

    /// <summary>
    /// Passes the current hovered tile + corner + occupied flag into each
    /// tile's TileOverlay so it can colour itself based on whether that
    /// specific corner is correct for the dragged device type.
    /// </summary>
    private void UpdateHoverOverlay()
    {
        RoadTile nowHovered = RaycastToTile(out Vector3 hitPoint);

        if (nowHovered != _hoveredTile)
        {
            if (_hoveredTile != null && _hoveredTile.Overlay != null)
                _hoveredTile.Overlay.SetDragState(_selectedDevice, false, TileCorner.None);

            _hoveredTile = nowHovered;
        }

        if (_hoveredTile != null && _hoveredTile.Overlay != null)
        {
            TileCorner corner = _hoveredTile.GetNearestCorner(hitPoint, _selectedDevice);
            bool onOccupied = _hoveredTile.IsCornerOccupied(corner);

            _hoveredTile.Overlay.SetDragState(_selectedDevice, onOccupied, corner);
        }
    }

    private void ShowPlacementOverlays(TrafficDeviceType device)
    {
        RoadTile[] all = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile t in all)
            if (t.Overlay != null)
                // its general suitability state (HasAtLeastOneValidCorner).
                t.Overlay.SetDragState(device, cursorOnOccupiedCorner: false, TileCorner.None);
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
        SetCarMovement(true);

        if (GameManager.Instance != null)
            GameManager.Instance.ResumeDayTick();

        if (_hoveredTile == null)
        {
            Debug.Log("[PlacementManager] No tile under cursor.");
            CancelPlacement();
            return;
        }

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
                LevelAudioManager.Instance?.PlaySuccessPlace();
                break;
            case PlacementResult.PoorPlacement:
                Debug.Log($"[PlacementManager] Placed INCORRECT {_selectedDevice} @ {targetCorner} on {_hoveredTile.tileID}");
                LevelAudioManager.Instance?.PlayPoorPlacement();
                break;
            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Slot or tile full.");
                Destroy(deviceObj);
                LevelAudioManager.Instance?.PlayFailedPlace();
                break;
            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Insufficient capital.");
                Destroy(deviceObj);
                LevelAudioManager.Instance?.PlayFailedPlace();
                break;
            case PlacementResult.DeviceNotAllowed:
                Debug.Log("[PlacementManager] Device not allowed here.");
                Destroy(deviceObj);
                LevelAudioManager.Instance?.PlayFailedPlace();
                break;
            default:
                Destroy(deviceObj);
                LevelAudioManager.Instance?.PlayFailedPlace();
                break;
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
        SetCarMovement(true);

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

    private void SetCarMovement(bool enabled)
    {
        if (!enabled)
            Time.timeScale = 0f;
        else
            Time.timeScale = 1f;
    }
}