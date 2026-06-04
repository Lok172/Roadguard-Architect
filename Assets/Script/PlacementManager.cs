using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────
//  PlacementProxy — see original file for rationale.
// ─────────────────────────────────────────────────────────────────

public class PlacementProxy : MonoBehaviour, IPointerDownHandler
{
    public System.Action onDown;
    public void OnPointerDown(PointerEventData _) => onDown?.Invoke();
}

// ─────────────────────────────────────────────────────────────────
//  PlacementManager (v2 — corner placement, multi-device, generated ghost)
//
//  CHANGES vs. v1:
//    • Ghost snaps to the NEAREST CORNER of the hovered tile and
//      rotates to face oncoming traffic (or stays centered for
//      SpeedBumps). Driven by RoadTile.GetNearestCorner /
//      GetCornerLocalPosition / GetCornerLocalRotation.
//    • Ghost tint shows placement quality in real time:
//        green  = clean placement
//        orange = wrong corner / wrong zone / over the soft cap
//        red    = corner taken, tile full, or other hard reject
//    • Ghost material is GENERATED IN CODE (Unlit/Transparent with
//      fallback to URP Unlit and Standard). No Inspector drag needed.
//      Manual override via the Inspector field still works.
//    • Corner is passed through to GameManager.TryPlaceDevice and
//      RoadTile.PlaceDevice.
// ─────────────────────────────────────────────────────────────────

public class PlacementManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────
    public static PlacementManager Instance { get; private set; }

    // ── UI Buttons ─────────────────────────────
    [System.Serializable]
    public class DeviceClickTarget
    {
        public GameObject clickableObject;
        public TrafficDeviceType deviceType;
    }

    [Header("Universal Click Targets")]
    public DeviceClickTarget[] clickTargets;

    [Header("City Scene")]
    [SceneName]
    public string citySceneName = "City";

    // ── Inspector Config ──────────────────────
    [Header("Device Prefabs (3D world objects)")]
    public GameObject stopSignPrefab;
    public GameObject speedBumpPrefab;
    public GameObject trafficLightPrefab;

    [Header("Ghost Material")]
    [Tooltip("OPTIONAL. Leave null to use the auto-generated transparent material.\n" +
             "Only assign this if you need a custom URP/HDRP transparent material.")]
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

    // ── Runtime ───────────────────────────────
    private TrafficDeviceType _selectedDevice = TrafficDeviceType.None;
    private GameObject _ghostObject = null;
    private bool _isDragging = false;
    private RoadTile _hoveredTile = null;
    private TileCorner _hoveredCorner = TileCorner.None;

    // Auto-generated ghost material — cached so we don't leak.
    private Material _generatedGhostMat;

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
                                 "to Camera.main. In multi-scene setups this is unreliable.");
        }

        SetupClickTargets();
    }

    private void OnDestroy()
    {
        if (_generatedGhostMat != null)
            Destroy(_generatedGhostMat);
    }

    private void Update()
    {
        if (!_isDragging) return;
        if (Mouse.current == null) return;

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

    private void SetupClickTargets()
    {
        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null) continue;

            PlacementProxy proxy = data.clickableObject.GetComponent<PlacementProxy>();
            if (proxy == null)
                proxy = data.clickableObject.AddComponent<PlacementProxy>();

            TrafficDeviceType type = data.deviceType;
            proxy.onDown = () => BeginDrag(type);
        }

        Debug.Log($"[PlacementManager] Registered {clickTargets.Length} placement targets.");
    }

    // ─────────────────────────────────────────
    //  BEGIN DRAG
    // ─────────────────────────────────────────

    public void BeginDrag(TrafficDeviceType deviceType)
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("[PlacementManager] GameManager.Instance is null.");
            return;
        }

        if (!GameManager.Instance.GameRunning)
        {
            Debug.Log("[PlacementManager] Game not running — placement blocked.");
            return;
        }

        float cost = DeviceData.GetCost(deviceType);
        if (GameManager.Instance.Capital < cost)
        {
            Debug.Log($"[PlacementManager] Cannot afford {deviceType} " +
                      $"(need RM{cost}, have RM{GameManager.Instance.Capital:F0})");
            return;
        }

        _selectedDevice = deviceType;
        _isDragging = true;
        _hoveredCorner = TileCorner.None;

        ShowPlacementOverlays(deviceType);
        SpawnGhost(deviceType);

        Debug.Log($"[PlacementManager] Drag started: {deviceType}  cost=RM{cost}");
    }

    public void BeginDragStopSign() => BeginDrag(TrafficDeviceType.StopSign);
    public void BeginDragSpeedBump() => BeginDrag(TrafficDeviceType.SpeedBump);
    public void BeginDragTrafficLight() => BeginDrag(TrafficDeviceType.TrafficLight);

    // ─────────────────────────────────────────
    //  GHOST MATERIAL  (generated in code)
    // ─────────────────────────────────────────

    /// <summary>
    /// Returns the Inspector-assigned ghostMaterial if set, otherwise
    /// lazily creates and caches a transparent material in code.
    /// Works in Built-in RP, URP, and HDRP (falls back across shaders).
    /// </summary>
    private Material GetOrCreateGhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        if (_generatedGhostMat != null) return _generatedGhostMat;

        // Try transparent shaders in order of preference
        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("HDRP/Unlit");
        if (shader == null) shader = Shader.Find("Standard");

        if (shader == null)
        {
            Debug.LogError("[PlacementManager] Could not find any usable shader for the ghost " +
                           "material. Assign a transparent material to 'Ghost Material' manually.");
            return null;
        }

        _generatedGhostMat = new Material(shader) { name = "GhostMaterial_Generated" };
        _generatedGhostMat.color = tintNeutral;

        // Force transparent blending on Built-in RP / Standard shader
        if (_generatedGhostMat.HasProperty("_SrcBlend"))
            _generatedGhostMat.SetInt("_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (_generatedGhostMat.HasProperty("_DstBlend"))
            _generatedGhostMat.SetInt("_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (_generatedGhostMat.HasProperty("_ZWrite"))
            _generatedGhostMat.SetInt("_ZWrite", 0);

        // Standard shader rendering mode = Transparent
        if (_generatedGhostMat.HasProperty("_Mode"))
            _generatedGhostMat.SetFloat("_Mode", 3f);

        _generatedGhostMat.DisableKeyword("_ALPHATEST_ON");
        _generatedGhostMat.EnableKeyword("_ALPHABLEND_ON");
        _generatedGhostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        // URP Lit / Unlit surface type = transparent
        if (_generatedGhostMat.HasProperty("_Surface"))
            _generatedGhostMat.SetFloat("_Surface", 1f);

        _generatedGhostMat.renderQueue = 3000;

        Debug.Log($"[PlacementManager] Generated ghost material using shader '{shader.name}'.");
        return _generatedGhostMat;
    }

    private void SpawnGhost(TrafficDeviceType deviceType)
    {
        DestroyGhost();

        GameObject prefab = GetPrefab(deviceType);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlacementManager] No prefab assigned for {deviceType}.");
            return;
        }

        _ghostObject = Instantiate(prefab);
        _ghostObject.name = $"Ghost_{deviceType}";

        Material ghostMat = GetOrCreateGhostMaterial();
        if (ghostMat != null)
        {
            // r.material auto-instances the shared mat per renderer so each
            // renderer can be tinted independently if we ever need to.
            foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
                r.material = ghostMat;
        }

        // Disable colliders so the ghost never blocks the tile raycast
        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
    }

    // ─────────────────────────────────────────
    //  GHOST POSITIONING + TINT
    // ─────────────────────────────────────────

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null || cityCamera == null) return;

        RoadTile hit = RaycastToTile(out Vector3 hitPoint);

        if (hit != null)
        {
            // Determine target corner (or center for speed bumps)
            TileCorner corner = hit.GetNearestCorner(hitPoint, _selectedDevice);
            _hoveredCorner = corner;

            // Parent → set local pos/rot → resulting world pos respects tile rotation
            _ghostObject.transform.SetParent(hit.transform, worldPositionStays: false);
            _ghostObject.transform.localPosition =
                hit.GetCornerLocalPosition(corner) + Vector3.up * ghostYOffset;
            _ghostObject.transform.localRotation = hit.GetCornerLocalRotation(corner);
            _ghostObject.transform.localScale = Vector3.one;

            // Decide ghost tint based on placement quality
            bool cornerTaken = hit.IsCornerOccupied(corner);
            bool full = hit.PlacedCount >= hit.maxDevices;
            bool wrongCorner = !hit.IsCorrectCorner(corner, _selectedDevice);
            bool poorZone = DeviceData.IsPoorPlacement(_selectedDevice, hit.zoneType);
            bool overLimit = hit.WouldBeOverLimit();
            bool deviceAllowed = hit.allowedDevices.Count == 0 ||
                                 hit.allowedDevices.Contains(_selectedDevice);

            if (!deviceAllowed || cornerTaken || full)
                SetGhostTint(tintBlocked);
            else if (wrongCorner || poorZone || overLimit)
                SetGhostTint(tintWarning);
            else
                SetGhostTint(tintValid);
        }
        else
        {
            // Off-tile: detach and float on the ground plane
            _ghostObject.transform.SetParent(null);
            _hoveredCorner = TileCorner.None;

            Ray ray = cityCamera.ScreenPointToRay(MousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float dist))
            {
                _ghostObject.transform.position =
                    ray.GetPoint(dist) + Vector3.up * ghostYOffset;
                _ghostObject.transform.rotation = Quaternion.identity;
            }
            SetGhostTint(tintNeutral);
        }
    }

    private void SetGhostTint(Color color)
    {
        if (_ghostObject == null) return;
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
    //  OVERLAY
    // ─────────────────────────────────────────

    private void UpdateHoverOverlay()
    {
        _hoveredTile = RaycastToTile(out _);
    }

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
        if (prefab == null) { CancelPlacement(); return; }

        // For SpeedBumps, force the corner to Center regardless of cursor.
        TileCorner targetCorner = _selectedDevice == TrafficDeviceType.SpeedBump
            ? TileCorner.Center
            : _hoveredCorner;

        if (targetCorner == TileCorner.None)
        {
            Debug.Log("[PlacementManager] No valid corner — cancelling.");
            CancelPlacement();
            return;
        }

        GameObject deviceObj = Instantiate(prefab);

        Scene cityScene = SceneManager.GetSceneByName(citySceneName);
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(deviceObj, cityScene);

        PlacementResult result = GameManager.Instance.TryPlaceDevice(
            _hoveredTile, _selectedDevice, targetCorner, deviceObj);

        switch (result)
        {
            case PlacementResult.Success:
                Debug.Log($"[PlacementManager] Placed {_selectedDevice} @ {targetCorner} on {_hoveredTile.tileID}");
                break;

            case PlacementResult.PoorPlacement:
                Debug.Log($"[PlacementManager] Poor placement of {_selectedDevice} @ {targetCorner} — happiness penalty applied.");
                break;

            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Slot or tile already full.");
                Destroy(deviceObj);
                break;

            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Insufficient capital.");
                Destroy(deviceObj);
                break;

            case PlacementResult.DeviceNotAllowed:
                Debug.Log("[PlacementManager] Device not allowed on this tile / corner.");
                Destroy(deviceObj);
                break;

            default:
                Destroy(deviceObj);
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
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        _hoveredCorner = TileCorner.None;
        DestroyGhost();
        ResetAllOverlays();
        Debug.Log("[PlacementManager] Placement cancelled.");
    }

    // ─────────────────────────────────────────
    //  RAYCAST INTO CITY SCENE
    // ─────────────────────────────────────────

    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (cityCamera == null) return null;

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

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

        var pointerData = new PointerEventData(EventSystem.current) { position = MousePosition };
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