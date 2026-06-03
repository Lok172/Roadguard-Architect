using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────
//  PlacementProxy
//
//  WHY THIS EXISTS INSTEAD OF ClickProxy:
//    ClickProxy uses IPointerClickHandler → OnPointerClick, which
//    Unity UI only fires when the pointer is PRESSED and RELEASED on
//    the same element without moving.  The instant you start dragging
//    away from the icon, Unity cancels the click → BeginDrag is never
//    called → complete silence.
//
//    This proxy uses IPointerDownHandler → OnPointerDown, which fires
//    the moment the button is pressed, regardless of subsequent
//    pointer movement.  That is exactly what drag-and-drop needs.
//
//  Navigation.cs keeps its own ClickProxy (OnPointerClick) for
//  scene-transition buttons — click-to-navigate is intentional.
// ─────────────────────────────────────────────────────────────────

public class PlacementProxy : MonoBehaviour, IPointerDownHandler
{
    public System.Action onDown;
    public void OnPointerDown(PointerEventData _) => onDown?.Invoke();
}

// ─────────────────────────────────────────────────────────────────
//  PlacementManager
//
//  Handles click-and-drag placement from the UI panel onto the
//  3D city scene — across scenes using additive loading.
//
//  DRAG FLOW:
//    1. Player presses (down) a device icon in BottomHUD (UIScene).
//    2. A ghost 3D prefab follows the mouse in world space.
//    3. Raycast hits the RoadTile collider layer in CityScene.
//    4. On mouse-up over a valid tile → PlaceDevice via GameManager.
//    5. On mouse-up over nothing, or right-click/Escape → cancel.
//
//  BUGS FIXED vs. original:
//    [1] ClickProxy → PlacementProxy (OnPointerDown not OnPointerClick).
//    [2] "CityScene" hardcoded in ConfirmPlacement → use citySceneName.
//    [3] IsPointerOverGameObject() blocks tile raycast during drag →
//        skipped while _isDragging is true; use RaycastAll instead.
//    [4] CameraManager also drags on left-click → expose IsDragging
//        so CameraManager can skip camera drag during placement.
// ─────────────────────────────────────────────────────────────────

public class PlacementManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────
    public static PlacementManager Instance { get; private set; }

    // ── UI Buttons ─────────────────────────────
    [System.Serializable]
    public class DeviceClickTarget
    {
        [Tooltip("The HUD button root — StopSign / SpeedBump / TrafficLight GO")]
        public GameObject clickableObject;
        public TrafficDeviceType deviceType;
    }

    [Header("Universal Click Targets")]
    public DeviceClickTarget[] clickTargets;

    [Header("City Scene")]
    [Tooltip("The additively-loaded 3D city scene (must match the scene name EXACTLY)")]
    [SceneName]
    public string citySceneName = "City";

    // ── Inspector Config ──────────────────────
    [Header("Device Prefabs (3D world objects)")]
    public GameObject stopSignPrefab;
    public GameObject speedBumpPrefab;
    public GameObject trafficLightPrefab;

    [Header("Ghost Material")]
    [Tooltip("Semi-transparent material applied to the ghost preview. " +
             "Leave null to tint the original materials instead.")]
    public Material ghostMaterial;

    [Header("Raycast")]
    [Tooltip("Layer mask for RoadTile colliders — must be the 'RoadTile' layer")]
    public LayerMask roadTileLayer;

    [Tooltip("Camera rendering the city. MUST be assigned in the Inspector. " +
             "Drag the camera from the Environment/City scene here. " +
             "Camera.main fallback is unreliable in multi-scene setups.")]
    public Camera cityCamera;

    [Header("Ghost Height Offset")]
    [Tooltip("How high above the tile pivot the ghost floats (metres)")]
    public float ghostYOffset = 0.1f;

    // ── Runtime ───────────────────────────────
    private TrafficDeviceType _selectedDevice = TrafficDeviceType.None;
    private GameObject _ghostObject = null;
    private bool _isDragging = false;
    private RoadTile _hoveredTile = null;

    /// <summary>
    /// True while the player is dragging a device.
    /// CameraManager reads this to suppress camera-drag simultaneously.
    /// </summary>
    public bool IsDragging => _isDragging;

    // ── Input (new Input System) ──────────────
    private Vector2 MousePosition => Mouse.current.position.ReadValue();
    private bool LeftButtonUp => Mouse.current.leftButton.wasReleasedThisFrame;
    private bool RightButtonDown => Mouse.current.rightButton.wasPressedThisFrame;

    // ─────────────────────────────────────────
    //  UNITY LIFECYCLE
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
                Debug.LogError("[PlacementManager] cityCamera is null and Camera.main was " +
                               "not found.\nDrag the city camera into the Inspector field!");
            else
                Debug.LogWarning("[PlacementManager] cityCamera was not assigned — fell back " +
                                 "to Camera.main. In multi-scene setups this is unreliable. " +
                                 "Assign it explicitly in the Inspector.");
        }

        SetupClickTargets();
    }

    private void Update()
    {
        if (!_isDragging) return;
        if (Mouse.current == null) return;

        // Cancel on right-click or Escape
        if (RightButtonDown ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        UpdateHoverOverlay();
        MoveGhostToMouse();

        if (LeftButtonUp)
            ConfirmPlacement();
    }

    // ─────────────────────────────────────────
    //  CLICK TARGET SETUP
    // ─────────────────────────────────────────

    // BUG FIX [1]: Use PlacementProxy (IPointerDownHandler) so BeginDrag
    // fires the instant the mouse button is pressed, not on release.
    // ClickProxy (IPointerClickHandler) cancels as soon as the pointer
    // moves — a drag never starts.

    private void SetupClickTargets()
    {
        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null)
            {
                Debug.LogWarning("[PlacementManager] A clickTarget has no clickableObject assigned.");
                continue;
            }

            PlacementProxy proxy = data.clickableObject.GetComponent<PlacementProxy>();
            if (proxy == null)
                proxy = data.clickableObject.AddComponent<PlacementProxy>();

            TrafficDeviceType type = data.deviceType;
            proxy.onDown = () => BeginDrag(type);
        }

        Debug.Log($"[PlacementManager] Registered {clickTargets.Length} placement targets.");
    }

    // ─────────────────────────────────────────
    //  BEGIN DRAG  (called by PlacementProxy)
    // ─────────────────────────────────────────

    public void BeginDrag(TrafficDeviceType deviceType)
    {
        // Guard: GameManager must exist and game must be running
        if (GameManager.Instance == null)
        {
            Debug.LogError("[PlacementManager] GameManager.Instance is null — " +
                           "is GameManager in the scene?");
            return;
        }

        if (!GameManager.Instance.GameRunning)
        {
            Debug.Log("[PlacementManager] Game is not running — placement blocked.");
            return;
        }

        // Guard: can the player afford this device?
        float cost = DeviceData.GetCost(deviceType);
        if (GameManager.Instance.Capital < cost)
        {
            Debug.Log($"[PlacementManager] Cannot afford {deviceType} " +
                      $"(need RM{cost}, have RM{GameManager.Instance.Capital:F0})");
            return;
        }

        _selectedDevice = deviceType;
        _isDragging = true;

        ShowPlacementOverlays(deviceType);
        SpawnGhost(deviceType);

        Debug.Log($"[PlacementManager] Drag started: {deviceType}  cost=RM{cost}");
    }

    // Legacy direct-wiring helpers (kept for backward compat with
    // any Inspector-wired OnClick() events you may have set up)
    public void BeginDragStopSign() => BeginDrag(TrafficDeviceType.StopSign);
    public void BeginDragSpeedBump() => BeginDrag(TrafficDeviceType.SpeedBump);
    public void BeginDragTrafficLight() => BeginDrag(TrafficDeviceType.TrafficLight);

    // ─────────────────────────────────────────
    //  GHOST
    // ─────────────────────────────────────────

    private void SpawnGhost(TrafficDeviceType deviceType)
    {
        DestroyGhost();

        GameObject prefab = GetPrefab(deviceType);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlacementManager] No prefab assigned for {deviceType}. " +
                             "Check the 'Device Prefabs' section in the Inspector.");
            return;
        }

        _ghostObject = Instantiate(prefab);
        _ghostObject.name = $"Ghost_{deviceType}";

        if (ghostMaterial != null)
        {
            foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
                r.material = ghostMaterial;
        }

        // Disable all colliders so the ghost never interferes with raycasts
        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        // Move ghost into the city scene so it is lit/culled correctly
        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
        else
            Debug.LogWarning($"[PlacementManager] City scene '{citySceneName}' is not loaded. " +
                             "Ghost will live in the active scene instead.");
    }

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null || cityCamera == null) return;

        RoadTile hit = RaycastToTile(out Vector3 hitPoint);

        if (hit != null)
        {
            _ghostObject.transform.position =
                hit.transform.position + Vector3.up * ghostYOffset;
            _ghostObject.transform.rotation = hit.transform.rotation;

            bool poor = DeviceData.IsPoorPlacement(_selectedDevice, hit.zoneType);
            SetGhostTint(poor ? Color.red : Color.green);
        }
        else
        {
            // Ghost floats on the ground plane when not over a tile
            Ray ray = cityCamera.ScreenPointToRay(MousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float dist))
            {
                _ghostObject.transform.position =
                    ray.GetPoint(dist) + Vector3.up * ghostYOffset;
            }
            SetGhostTint(Color.grey);
        }
    }

    private void SetGhostTint(Color color)
    {
        if (_ghostObject == null) return;
        color.a = 0.5f;
        foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
            r.material.color = color;
    }

    private void DestroyGhost()
    {
        if (_ghostObject != null)
            Destroy(_ghostObject);
        _ghostObject = null;
    }

    // ─────────────────────────────────────────
    //  OVERLAY — HOVER TRACKING
    // ─────────────────────────────────────────

    private void UpdateHoverOverlay()
    {
        RoadTile newHover = RaycastToTile(out _);
        _hoveredTile = newHover;
    }

    // ─────────────────────────────────────────
    //  OVERLAY — ALL-TILE BROADCAST
    // ─────────────────────────────────────────

    private void ShowPlacementOverlays(TrafficDeviceType device)
    {
        if (GameManager.Instance == null) return;

        RoadTile[] allTiles = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile tile in allTiles)
        {
            if (tile.Overlay == null) continue;
            tile.Overlay.SetState(tile.GetOverlayState(device));
        }
    }

    private void ResetAllOverlays()
    {
        RoadTile[] allTiles = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);
        foreach (RoadTile tile in allTiles)
        {
            if (tile.Overlay == null) continue;
            tile.Overlay.SetState(tile.isOccupied
                ? OverlayState.Occupied
                : OverlayState.Default);
        }
    }

    // ─────────────────────────────────────────
    //  CONFIRM / CANCEL
    // ─────────────────────────────────────────

    private void ConfirmPlacement()
    {
        _isDragging = false;

        if (_hoveredTile == null)
        {
            Debug.Log("[PlacementManager] Released over no tile — cancelling.");
            CancelPlacement();
            return;
        }

        GameObject prefab = GetPrefab(_selectedDevice);
        if (prefab == null)
        {
            CancelPlacement();
            return;
        }

        GameObject deviceObj = Instantiate(prefab);

        // BUG FIX [2]: Original code had "CityScene" hardcoded here.
        // The actual scene is named by citySceneName ("City").
        // "CityScene" never matched → device lived in the wrong scene,
        // potentially causing render / physics quirks or destroy leaks.
        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(deviceObj, cityScene);

        PlacementResult result = GameManager.Instance.TryPlaceDevice(
            _hoveredTile, _selectedDevice, deviceObj);

        switch (result)
        {
            case PlacementResult.Success:
                Debug.Log($"[PlacementManager] Placed {_selectedDevice} on {_hoveredTile.tileID}");
                break;

            case PlacementResult.PoorPlacement:
                Debug.Log("[PlacementManager] Poor placement — happiness penalty applied.");
                break;

            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Tile already occupied.");
                Destroy(deviceObj);
                break;

            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Insufficient capital.");
                Destroy(deviceObj);
                break;

            case PlacementResult.DeviceNotAllowed:
                Debug.Log("[PlacementManager] Device not allowed on this tile type.");
                Destroy(deviceObj);
                break;

            default:
                Destroy(deviceObj);
                break;
        }

        DestroyGhost();
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        ResetAllOverlays();
    }

    public void CancelPlacement()
    {
        _isDragging = false;
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        DestroyGhost();
        ResetAllOverlays();
        Debug.Log("[PlacementManager] Placement cancelled.");
    }

    // ─────────────────────────────────────────
    //  RAYCAST INTO CITY SCENE
    // ─────────────────────────────────────────

    // BUG FIX [3]: Original code called IsPointerOverGameObject() every
    // frame during drag.  Two problems:
    //   a) With InputSystemUIInputModule, IsPointerOverGameObject() without
    //      a pointer ID returns inconsistent results — it may always be
    //      true, permanently blocking tile detection.
    //   b) Even when it works, it returns true for ANY UI Graphic with
    //      Raycast Target enabled.  During drag, the cursor moves over
    //      the game world, but transparent canvas elements may still block.
    //
    // FIX: skip the UI-over check while actively dragging.  The
    // roadTileLayer mask already ensures we only hit road colliders.

    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (cityCamera == null) return null;

        // Only block on UI when NOT dragging (e.g., if you ever call this
        // outside of the drag loop, respect the UI hierarchy).
        if (!_isDragging && IsPointerOverUI())
            return null;

        Ray ray = cityCamera.ScreenPointToRay(MousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, roadTileLayer))
        {
            hitPoint = hit.point;
            return hit.collider.GetComponent<RoadTile>();
        }

        return null;
    }

    /// <summary>
    /// Reliable UI-over check for the new Input System.
    /// Uses GraphicRaycaster-based RaycastAll so the pointer ID doesn't matter.
    /// </summary>
    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = MousePosition
        };
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        return results.Count > 0;
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    private GameObject GetPrefab(TrafficDeviceType type) => type switch
    {
        TrafficDeviceType.StopSign => stopSignPrefab,
        TrafficDeviceType.SpeedBump => speedBumpPrefab,
        TrafficDeviceType.TrafficLight => trafficLightPrefab,
        _ => null
    };
}