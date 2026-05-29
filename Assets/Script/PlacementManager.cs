using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────
//
//  Handles click-and-drag placement from the UI panel onto the
//  3D city scene — across scenes using additive loading.
//
//  HOW DRAG WORKS:
//    1. Player clicks a device icon in the UI (UIScene).
//    2. A ghost 3D prefab follows the mouse in world space.
//    3. Raycast hits the RoadTile collider layer in CityScene.
//    4. On mouse-up over a valid tile → PlaceDevice via GameManager.
//    5. On mouse-up over nothing → cancel, destroy ghost.
//
//  OVERLAY INTEGRATION (new):
//    • BeginDrag    → all tiles switch from Default to their
//                     per-device state (Valid / PoorPlacement /
//                     Occupied / Hidden).
//    • Hover change → previously hovered tile reverts to its
//                     device state; newly hovered tile stays the
//                     same (state already reflects validity).
//    • Confirm/Cancel → all tiles reset to Default (grey).
// ─────────────────────────────────────────────────────────────────

public class PlacementManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────
    public static PlacementManager Instance { get; private set; }

    // ── UI Buttons ─────────────────────────────
    [System.Serializable]
    public class DeviceClickTarget
    {
        [Tooltip("Any clickable object")]
        public GameObject clickableObject;
        public TrafficDeviceType deviceType;
    }

    [Header("Universal Click Targets")]
    public DeviceClickTarget[] clickTargets;

    // ── Inspector Config ──────────────────────
    [Header("Device Prefabs (3D world objects)")]
    public GameObject stopSignPrefab;
    public GameObject speedBumpPrefab;
    public GameObject trafficLightPrefab;

    [Header("Ghost Material")]
    [Tooltip("Semi-transparent material applied to the ghost preview")]
    public Material ghostMaterial;

    [Header("Raycast")]
    [Tooltip("Layer mask for RoadTile colliders (set to your 'RoadTile' layer)")]
    public LayerMask roadTileLayer;

    [Tooltip("Camera used to cast rays into the city scene")]
    public Camera cityCamera;

    [Header("Ghost Height Offset")]
    [Tooltip("How high above the tile surface the ghost floats")]
    public float ghostYOffset = 0.1f;

    // ── Runtime State ─────────────────────────
    private TrafficDeviceType _selectedDevice = TrafficDeviceType.None;
    private GameObject _ghostObject = null;
    private bool _isDragging = false;
    private RoadTile _hoveredTile = null;

    // ── Input System ──────────────────────────
    private Vector2 MousePosition => Mouse.current.position.ReadValue();
    private bool LeftButtonUp => Mouse.current.leftButton.wasReleasedThisFrame;

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
            cityCamera = Camera.main;

        SetupClickTargets();
    }

    private void Update()
    {
        if (!_isDragging) return;
        if (Mouse.current == null) return;

        UpdateHoverOverlay();   // must come before MoveGhostToMouse so
        MoveGhostToMouse();     // ghost position is set after overlay

        if (LeftButtonUp)
            ConfirmPlacement();
    }

    // ── Universal Clickable Objects ───────────────────────────────

    private void SetupClickTargets()
    {
        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null) continue;

            ClickProxy proxy = data.clickableObject.GetComponent<ClickProxy>();
            if (proxy == null)
                proxy = data.clickableObject.AddComponent<ClickProxy>();

            TrafficDeviceType type = data.deviceType;
            proxy.Setup(() => BeginDrag(type));
        }
    }

    // ─────────────────────────────────────────
    //  CALLED BY UI BUTTONS
    // ─────────────────────────────────────────

    public void BeginDrag(TrafficDeviceType deviceType)
    {
        if (!GameManager.Instance.GameRunning) return;

        float cost = DeviceData.GetCost(deviceType);
        if (GameManager.Instance.Capital < cost)
        {
            Debug.Log($"[PlacementManager] Cannot afford {deviceType} (RM{cost})");
            return;
        }

        _selectedDevice = deviceType;
        _isDragging = true;

        // ── Overlay: paint every registered tile with its validity
        //    state for the chosen device.
        ShowPlacementOverlays(deviceType);

        SpawnGhost(deviceType);
    }

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
            Debug.LogWarning($"[PlacementManager] No prefab assigned for {deviceType}");
            return;
        }

        _ghostObject = Instantiate(prefab);
        _ghostObject.name = $"Ghost_{deviceType}";

        if (ghostMaterial != null)
        {
            foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
                r.material = ghostMaterial;
        }

        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        Scene cityScene = SceneManager.GetSceneByName("City");
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
    }

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null) return;

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

    /// <summary>
    /// Called every Update() while dragging.
    /// Keeps _hoveredTile in sync and refreshes the overlay on tile
    /// transitions so the hovered tile can pulse brighter if needed.
    /// (Currently the overlay state is the same whether hovered or not;
    /// you can extend this to a brighter pulse for the hovered tile.)
    /// </summary>
    private void UpdateHoverOverlay()
    {
        RoadTile newHover = RaycastToTile(out _);

        if (newHover == _hoveredTile) return;   // no change, skip

        // Tile the cursor just left — overlay already correct, no change needed.
        // Tile the cursor just entered — also already correct.
        // We just keep _hoveredTile updated for ConfirmPlacement.
        _hoveredTile = newHover;
    }

    // ─────────────────────────────────────────
    //  OVERLAY — ALL-TILE BROADCAST
    // ─────────────────────────────────────────

    /// <summary>
    /// Sets every registered tile's overlay to reflect its validity
    /// for <paramref name="device"/>. Call on BeginDrag.
    /// </summary>
    private void ShowPlacementOverlays(TrafficDeviceType device)
    {
        if (GameManager.Instance == null) return;

        // GameManager exposes _allTiles via the tile registry; we access
        // it through the public RegisterTile / UnregisterTile pattern —
        // but we need to iterate. We use FindObjectsByType as a simple
        // cross-scene gather (only called once per drag begin).
        RoadTile[] allTiles = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);

        foreach (RoadTile tile in allTiles)
        {
            if (tile.Overlay == null) continue;
            tile.Overlay.SetState(tile.GetOverlayState(device));
        }
    }

    /// <summary>
    /// Resets every registered tile's overlay back to Default (grey).
    /// Call on ConfirmPlacement / CancelPlacement.
    /// </summary>
    private void ResetAllOverlays()
    {
        RoadTile[] allTiles = FindObjectsByType<RoadTile>(FindObjectsSortMode.None);

        foreach (RoadTile tile in allTiles)
        {
            if (tile.Overlay == null) continue;

            // Occupied tiles keep their Occupied (red) state so the
            // player can see which tiles are already filled at a glance.
            if (tile.isOccupied)
                tile.Overlay.SetState(OverlayState.Occupied);
            else
                tile.Overlay.SetState(OverlayState.Default);
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
            CancelPlacement();
            return;
        }

        GameObject prefab = GetPrefab(_selectedDevice);
        if (prefab == null) { CancelPlacement(); return; }

        GameObject deviceObj = Instantiate(prefab);

        Scene cityScene = SceneManager.GetSceneByName("CityScene");
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
                Debug.Log("[PlacementManager] Poor placement! Happiness penalty applied.");
                break;

            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Tile already has a device.");
                Destroy(deviceObj);
                break;

            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Not enough capital.");
                Destroy(deviceObj);
                break;

            default:
                Destroy(deviceObj);
                break;
        }

        DestroyGhost();
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;

        // ── Overlay: reset all tiles (occupied tiles stay red, rest go grey)
        ResetAllOverlays();
    }

    public void CancelPlacement()
    {
        _isDragging = false;
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        DestroyGhost();

        // ── Overlay: reset all tiles
        ResetAllOverlays();
    }

    // ─────────────────────────────────────────
    //  RAYCAST INTO CITY SCENE
    // ─────────────────────────────────────────

    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return null;

        Ray ray = cityCamera.ScreenPointToRay(MousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, roadTileLayer))
        {
            hitPoint = hit.point;
            return hit.collider.GetComponent<RoadTile>();
        }

        return null;
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    private GameObject GetPrefab(TrafficDeviceType type)
    {
        return type switch
        {
            TrafficDeviceType.StopSign => stopSignPrefab,
            TrafficDeviceType.SpeedBump => speedBumpPrefab,
            TrafficDeviceType.TrafficLight => trafficLightPrefab,
            _ => null
        };
    }
}