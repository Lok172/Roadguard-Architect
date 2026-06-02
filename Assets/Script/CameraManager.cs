using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CameraManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Per-scene configuration
    // ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public class SceneConfig
    {
        [Header("Identification")]
        [SceneName]
        public string sceneName;

        [Header("Camera Transform")]
        public Vector3 startPosition = new Vector3(270f, 70f, 110f);
        public Vector3 startRotation = new Vector3(53f, -90f, 0f);

        [Header("Auto Pan (disabled while player is dragging)")]
        public float minLocationX = 110f;
        public float maxLocationX = 270f;
        public float movementSpeed = 5f;

        [Header("Zoom (Optional)")]
        public bool enableZoom = false;

        [Header("Zoom Settings")]
        public float zoomMultiplier = 50f;
        public float minZoom = 30f;
        public float maxZoom = 90f;
        public float zoomSmoothTime = 0.25f;

        public float targetZoom;
        public float zoomVelocity;


        [Tooltip("FOV/ortho size when zoomed in (smaller = closer)")]
        public float zoomInValue = 30f;
        [Tooltip("FOV/ortho size when zoomed out (larger = wider)")]
        public float zoomOutValue = 60f;
        [Tooltip("Seconds to complete one full zoom cycle")]
        public float zoomCycleDuration = 4f;

        // ── NEW: Mouse Drag Settings ──────────────────────────────
        [Header("Mouse Drag Pan (left mouse button)")]
        [Tooltip("Enable left-mouse-drag to pan the camera")]
        public bool enableMouseDrag = true;

        [Tooltip("How sensitive the drag is — higher = moves more per pixel")]
        public float dragSensitivity = 0.3f;

        [Tooltip("Only allow drag when zoomed in beyond this threshold. " +
                 "Set to 0 to always allow drag regardless of zoom level.")]
        public float dragZoomThreshold = 55f;

        [Tooltip("Also allow Z-axis drag (forward/back)? " +
                 "Useful if your camera can pan vertically on screen.")]
        public bool dragAxisZ = true;

        [Tooltip("Z-axis clamp — min world Z the camera can drag to")]
        public float minLocationZ = 80f;
        [Tooltip("Z-axis clamp — max world Z the camera can drag to")]
        public float maxLocationZ = 140f;
    }

    // ─────────────────────────────────────────────────────────────
    //  Inspector fields
    // ─────────────────────────────────────────────────────────────
    [Header("Scene List")]
    [SerializeField] private List<SceneConfig> scenes = new List<SceneConfig>();

    [Header("Camera Reference (auto-found if empty)")]
    [SerializeField] private Camera cameraDisplay;

    // ─────────────────────────────────────────────────────────────
    //  Private state
    // ─────────────────────────────────────────────────────────────
    private Transform cameraTransform;
    private int panDirection = 1;
    private float zoomTimer = 0f;
    private int activeSceneIndex = -1;

    // ── Drag state ────────────────────────────────────────────────
    private bool _isDragging = false;
    private Vector3 _dragOriginMouse = Vector3.zero;  // mouse position when drag started
    private Vector3 _dragOriginCamPos = Vector3.zero;  // camera position when drag started

    // ─────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────
    private void Start()
    {
        InitialiseCamera();
        DetectAndApplyCurrentUI();
    }

    private void Update()
    {
        if (cameraTransform == null) return;

        DetectAndApplyCurrentUI();

        SceneConfig cfg = ActiveConfig;
        if (cfg == null) return;

        // ── Zoom first (zoom level decides if drag is allowed) ────
        if (cfg.enableZoom)
            HandleZoom(cfg);

        // ── Mouse drag ────────────────────────────────────────────
        if (cfg.enableMouseDrag)
            HandleMouseDrag(cfg);

        // ── Auto-pan (skipped while player is actively dragging) ──
        if (!_isDragging)
            HandlePan(cfg);
    }

    // ─────────────────────────────────────────────────────────────
    //  PageManager detection (unchanged from your original)
    // ─────────────────────────────────────────────────────────────
    private string lastDetectedUI = null;

    private void DetectAndApplyCurrentUI()
    {
        PageManager pm = Object.FindFirstObjectByType<PageManager>();
        if (pm == null) return;

        string currentUI = pm.currentLoadedUI;
        if (currentUI == lastDetectedUI) return;

        lastDetectedUI = currentUI;
        SwitchToScene(currentUI);
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    public void SwitchToScene(int index)
    {
        if (index < 0 || index >= scenes.Count)
        {
            Debug.LogWarning($"[CameraManager] Scene index {index} is out of range.");
            return;
        }
        activeSceneIndex = index;
        ApplyScene(index);
    }

    public void SwitchToScene(string sceneName)
    {
        int idx = scenes.FindIndex(s => s.sceneName == sceneName);
        if (idx < 0)
        {
            Debug.LogWarning($"[CameraManager] No scene named '{sceneName}' found.");
            return;
        }
        SwitchToScene(idx);
    }

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────
    private SceneConfig ActiveConfig =>
        (scenes != null && activeSceneIndex >= 0 && activeSceneIndex < scenes.Count)
            ? scenes[activeSceneIndex]
            : null;

    private void InitialiseCamera()
    {
        if (cameraDisplay == null)
        {
            cameraDisplay = Object.FindFirstObjectByType<Camera>();
            if (cameraDisplay != null)
                Debug.LogWarning($"[CameraManager] Camera auto-found: {cameraDisplay.name}");
            else
            {
                Debug.LogError("[CameraManager] No Camera found in scene!");
                return;
            }
        }
        cameraTransform = cameraDisplay.transform;
    }

    private void ApplyScene(int index)
    {
        SceneConfig cfg = ActiveConfig;
        if (cfg == null || cameraTransform == null) return;

        panDirection = 1;
        zoomTimer = 0f;
        _isDragging = false;  // cancel any active drag on scene switch

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        if (cfg.enableZoom)
        {
            cfg.targetZoom = cfg.zoomOutValue;
            SetCameraZoom(cameraDisplay, cfg.zoomOutValue, cfg);
        }

        Debug.Log($"[CameraManager] Switched to scene: '{cfg.sceneName}'");
    }

    // ─────────────────────────────────────────────────────────────
    //  Auto Pan  (original behaviour — pauses during drag)
    // ─────────────────────────────────────────────────────────────
    private void HandlePan(SceneConfig cfg)
    {
        float movement = cfg.movementSpeed * Time.deltaTime * panDirection;
        Vector3 pos = cameraTransform.position;
        pos.x += movement;

        if (pos.x >= cfg.maxLocationX || pos.x <= cfg.minLocationX)
            panDirection *= -1;

        cameraTransform.position = pos;
    }

    // ─────────────────────────────────────────────────────────────
    //  Zoom  (unchanged from your original)
    // ─────────────────────────────────────────────────────────────
    private void HandleZoom(SceneConfig cfg)
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            cfg.targetZoom -= scroll * cfg.zoomMultiplier;
            cfg.targetZoom = Mathf.Clamp(cfg.targetZoom, cfg.minZoom, cfg.maxZoom);
        }

        SetCameraZoom(cameraDisplay, cfg.targetZoom, cfg);

        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        // Fully zoomed out
        if (Mathf.Abs(currentZoom - cfg.maxZoom) < 0.5f)
        {
            ResetView(cfg);
        }
    }
    private void ResetView(SceneConfig cfg)
    {
        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        Debug.Log("[CameraManager] Camera reset to default view.");
    }

    private void SetCameraZoom(Camera cam, float targetValue, SceneConfig cfg)
    {
        if (cam.orthographic)
        {
            cam.orthographicSize = Mathf.SmoothDamp(
                cam.orthographicSize, targetValue,
                ref cfg.zoomVelocity, cfg.zoomSmoothTime);
        }
        else
        {
            cam.fieldOfView = Mathf.SmoothDamp(
                cam.fieldOfView, targetValue,
                ref cfg.zoomVelocity, cfg.zoomSmoothTime);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  NEW — Mouse Drag Pan
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Left-mouse-button drag to pan the camera.
    ///
    /// HOW IT WORKS:
    ///   On MouseDown  → record where the mouse was and where the camera was.
    ///   Each frame     → calculate how far the mouse has moved (delta).
    ///   Apply that delta (scaled by dragSensitivity) to the camera position.
    ///   On MouseUp    → end drag, resume auto-pan.
    ///
    /// ZOOM GUARD:
    ///   If dragZoomThreshold > 0, drag is only active when the current
    ///   FOV/orthoSize is below the threshold (i.e. player is zoomed in).
    ///   Set dragZoomThreshold = 0 to always allow drag.
    /// </summary>
    private void HandleMouseDrag(SceneConfig cfg)
    {
        // ── Check if zoom level allows drag ───────────────────────
        bool zoomedInEnough = true;
        if (cfg.enableZoom && cfg.dragZoomThreshold > 0f)
        {
            float currentZoom = cameraDisplay.orthographic
                ? cameraDisplay.orthographicSize
                : cameraDisplay.fieldOfView;

            // Allow drag only when zoomed in (smaller value = more zoomed)
            zoomedInEnough = currentZoom < cfg.dragZoomThreshold;
        }

        // ── Mouse button DOWN — start drag ────────────────────────
        if (Input.GetMouseButtonDown(0) && zoomedInEnough)
        {
            _isDragging = true;
            _dragOriginMouse = Input.mousePosition;       // pixel position on screen
            _dragOriginCamPos = cameraTransform.position;  // world position of camera
        }

        // ── Mouse button UP — end drag ────────────────────────────
        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        // ── Not dragging — nothing to do ──────────────────────────
        if (!_isDragging) return;

        // ── If zoom changed mid-drag and player is no longer zoomed in,
        //    cancel the drag gracefully
        if (!zoomedInEnough)
        {
            _isDragging = false;
            return;
        }

        // ── Calculate mouse movement delta in pixels ──────────────
        Vector3 mouseDelta = Input.mousePosition - _dragOriginMouse;

        // mouseDelta.x → move camera on world X axis (left/right)
        // mouseDelta.y → move camera on world Z axis (forward/back)
        //   (negative because dragging mouse up = camera moves forward = Z decreases)
        float deltaX = -mouseDelta.x * cfg.dragSensitivity * Time.deltaTime * 60f;
        float deltaZ = -mouseDelta.y * cfg.dragSensitivity * Time.deltaTime * 60f;

        Vector3 newPos = _dragOriginCamPos + new Vector3(deltaX, 0f, cfg.dragAxisZ ? deltaZ : 0f);

        // ── Clamp within map bounds ───────────────────────────────
        newPos.x = Mathf.Clamp(newPos.x, cfg.minLocationX, cfg.maxLocationX);
        if (cfg.dragAxisZ)
            newPos.z = Mathf.Clamp(newPos.z, cfg.minLocationZ, cfg.maxLocationZ);

        // Y stays fixed — camera height never changes during drag
        newPos.y = _dragOriginCamPos.y;

        cameraTransform.position = newPos;
    }
}

// ─────────────────────────────────────────────────────────────────
//  Custom Inspector
// ─────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
[CustomEditor(typeof(CameraManager))]
public class CameraManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraManager manager = (CameraManager)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Runtime Scene Switcher", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to switch scenes at runtime.", MessageType.Info);
            return;
        }

        SerializedProperty scenesProp = serializedObject.FindProperty("scenes");
        for (int i = 0; i < scenesProp.arraySize; i++)
        {
            string name = scenesProp
                .GetArrayElementAtIndex(i)
                .FindPropertyRelative("sceneName")
                .stringValue;

            if (GUILayout.Button($"Switch → {name}"))
                manager.SwitchToScene(name);
        }
    }
}
#endif