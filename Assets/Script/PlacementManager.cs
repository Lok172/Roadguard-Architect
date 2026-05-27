using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────
//  PlacementManager
//  Attach to: the same "GameManager" GameObject (or its own
//  empty called "PlacementManager") in your persistent scene.
//
//  Handles click-and-drag placement from the UI panel onto the
//  3D city scene — across scenes using additive loading.
//
//  MULTI-SCENE SETUP ASSUMED:
//    Scene A  — "UIScene"    : UI canvas, device icon buttons
//    Scene B  — "CityScene"  : 3D city model, road tile colliders
//    Both loaded additively at runtime.
//
//  HOW DRAG WORKS:
//    1. Player clicks a device icon in the UI (UIScene).
//    2. A ghost 3D prefab follows the mouse in world space.
//    3. Raycast hits the RoadTile collider layer in CityScene.
//    4. On mouse-up over a valid tile → PlaceDevice via GameManager.
//    5. On mouse-up over nothing → cancel, destroy ghost.
// ─────────────────────────────────────────────────────────────────

public class PlacementManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────
    public static PlacementManager Instance { get; private set; }

    // ── Inspector Config ──────────────────────
    [Header("Device Prefabs (3D world objects)")]
    [Tooltip("Assign the 3D prefab for each device type in matching order")]
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
    private GameObject        _ghostObject    = null;
    private bool              _isDragging     = false;
    private RoadTile          _hoveredTile    = null;

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
        // Auto-find city camera if not assigned
        if (cityCamera == null)
            cityCamera = Camera.main;
    }

    private void Update()
    {
        if (!_isDragging) return;

        MoveGhostToMouse();

        if (Input.GetMouseButtonUp(0))
            ConfirmPlacement();
    }

    // ─────────────────────────────────────────
    //  CALLED BY UI BUTTONS (UIScene)
    // ─────────────────────────────────────────

    /// <summary>
    /// Call this from the UI device icon button's OnPointerDown event.
    /// Starts the drag with the selected device type.
    /// Example: button.OnPointerDown → PlacementManager.Instance.BeginDrag(TrafficDeviceType.SpeedBump)
    /// </summary>
    public void BeginDrag(TrafficDeviceType deviceType)
    {
        if (!GameManager.Instance.GameRunning) return;

        // Check if player can afford it before even starting drag
        float cost = DeviceData.GetCost(deviceType);
        if (GameManager.Instance.Capital < cost)
        {
            Debug.Log($"[PlacementManager] Cannot afford {deviceType} (RM{cost})");
            // TODO: show UI "Insufficient funds" feedback here
            return;
        }

        _selectedDevice = deviceType;
        _isDragging     = true;

        SpawnGhost(deviceType);
    }

    // Convenience overloads for UI button wiring (int version avoids enum in Inspector)
    public void BeginDragStopSign()     => BeginDrag(TrafficDeviceType.StopSign);
    public void BeginDragSpeedBump()    => BeginDrag(TrafficDeviceType.SpeedBump);
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

        // Apply ghost (semi-transparent) material to all renderers
        if (ghostMaterial != null)
        {
            foreach (Renderer r in _ghostObject.GetComponentsInChildren<Renderer>())
                r.material = ghostMaterial;
        }

        // Disable all colliders on ghost so it doesn't interfere with raycasts
        foreach (Collider c in _ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        // Place ghost in the city scene (Scene B) so it renders correctly
        // with the city camera, NOT in UIScene
        Scene cityScene = SceneManager.GetSceneByName("CityScene");
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(_ghostObject, cityScene);
    }

    private void MoveGhostToMouse()
    {
        if (_ghostObject == null) return;

        RoadTile hit = RaycastToTile(out Vector3 hitPoint);
        _hoveredTile = hit;

        if (hit != null)
        {
            // Snap ghost to tile centre at hover height
            _ghostObject.transform.position =
                hit.transform.position + Vector3.up * ghostYOffset;
            _ghostObject.transform.rotation = hit.transform.rotation;

            // Tint green = valid, red = poor placement
            bool poor = DeviceData.IsPoorPlacement(_selectedDevice, hit.zoneType);
            SetGhostTint(poor ? Color.red : Color.green);
        }
        else
        {
            // Float ghost at mouse world position (Y = 0 plane)
            Ray ray = cityCamera.ScreenPointToRay(Input.mousePosition);
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
        {
            // Only works if ghost material supports _Color (Standard/URP Lit/etc.)
            r.material.color = color;
        }
    }

    private void DestroyGhost()
    {
        if (_ghostObject != null)
            Destroy(_ghostObject);
        _ghostObject = null;
    }

    // ─────────────────────────────────────────
    //  CONFIRM / CANCEL
    // ─────────────────────────────────────────

    private void ConfirmPlacement()
    {
        _isDragging = false;

        if (_hoveredTile == null)
        {
            // Dropped on empty space — cancel
            CancelPlacement();
            return;
        }

        // Instantiate the real device prefab (not ghost)
        GameObject prefab = GetPrefab(_selectedDevice);
        if (prefab == null) { CancelPlacement(); return; }

        GameObject deviceObj = Instantiate(prefab);

        // Move into CityScene
        Scene cityScene = SceneManager.GetSceneByName("CityScene");
        if (cityScene.IsValid())
            SceneManager.MoveGameObjectToScene(deviceObj, cityScene);

        // Ask GameManager to handle cost, happiness, tile state
        PlacementResult result = GameManager.Instance.TryPlaceDevice(
            _hoveredTile, _selectedDevice, deviceObj);

        switch (result)
        {
            case PlacementResult.Success:
                Debug.Log($"[PlacementManager] Placed {_selectedDevice} on {_hoveredTile.tileID}");
                break;

            case PlacementResult.PoorPlacement:
                Debug.Log($"[PlacementManager] Poor placement! Happiness penalty applied.");
                // TODO: show UI warning "Poor placement — happiness reduced!"
                break;

            case PlacementResult.AlreadyOccupied:
                Debug.Log("[PlacementManager] Tile already has a device.");
                Destroy(deviceObj);
                // TODO: show UI "Tile occupied"
                break;

            case PlacementResult.InsufficientFunds:
                Debug.Log("[PlacementManager] Not enough capital.");
                Destroy(deviceObj);
                // TODO: show UI "Insufficient funds"
                break;

            default:
                Destroy(deviceObj);
                break;
        }

        DestroyGhost();
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile    = null;
    }

    public void CancelPlacement()
    {
        _isDragging     = false;
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile    = null;
        DestroyGhost();
    }

    // ─────────────────────────────────────────
    //  RAYCAST INTO CITY SCENE
    // ─────────────────────────────────────────

    /// <summary>
    /// Casts a ray from the city camera through the mouse position.
    /// Only hits objects on the RoadTile layer.
    /// Returns the RoadTile hit (or null), and the world hit point.
    /// </summary>
    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        // Don't raycast if mouse is over UI elements
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return null;

        Ray ray = cityCamera.ScreenPointToRay(Input.mousePosition);

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
            TrafficDeviceType.StopSign     => stopSignPrefab,
            TrafficDeviceType.SpeedBump    => speedBumpPrefab,
            TrafficDeviceType.TrafficLight => trafficLightPrefab,
            _                              => null
        };
    }
}
