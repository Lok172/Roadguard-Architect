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
    private GameObject _ghostObject = null;
    private bool _isDragging = false;
    private RoadTile _hoveredTile = null;

    // ── Input System ──────────────────────────
    // Cached mouse position and button state read via new Input System.
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

        // Guard: Input System may not have a mouse device (e.g. touch-only build)
        if (Mouse.current == null) return;

        MoveGhostToMouse();

        if (LeftButtonUp)
            ConfirmPlacement();
    }

    // ── Universal Clickable Objects ───────────────────────────────

    private void SetupClickTargets()
    {
        foreach (DeviceClickTarget data in clickTargets)
        {
            if (data.clickableObject == null)
                continue;

            ClickProxy proxy =
                data.clickableObject.GetComponent<ClickProxy>();

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
        _hoveredTile = hit;

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
    }

    public void CancelPlacement()
    {
        _isDragging = false;
        _selectedDevice = TrafficDeviceType.None;
        _hoveredTile = null;
        DestroyGhost();
    }

    // ─────────────────────────────────────────
    //  RAYCAST INTO CITY SCENE
    // ─────────────────────────────────────────

    private RoadTile RaycastToTile(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;

        // IsPointerOverGameObject() is still valid with the new Input System
        // as long as the EventSystem has a PhysicsRaycaster or UI InputModule.
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