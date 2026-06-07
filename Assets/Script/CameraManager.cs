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

        // ── Mouse Drag Settings ──────────────────────────────────
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

        // ── Position smooth settings ─────────────────────────────
        [Header("Position Smoothing")]
        [Tooltip("How quickly the camera position catches up to the target " +
                 "during zoom-toward-cursor. Lower = smoother but laggier.")]
        public float positionSmoothTime = 0.2f;
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
    private Vector3 _lastMousePos = Vector3.zero;

    // ── Smooth zoom-toward-cursor state ───────────────────────────
    private Vector3 _preZoomPosition;     // camera XZ before any zooming began
    private bool _hasStoredPreZoom = false;
    private Vector3 _targetXZPosition;    // where we WANT the camera XZ to be
    private Vector3 _positionVelocity;    // used by SmoothDamp for position

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

        // ── Zoom first (zoom level decides if drag / pan is allowed) ──
        if (cfg.enableZoom)
            HandleZoom(cfg);

        // ── Mouse drag ────────────────────────────────────────────
        if (cfg.enableMouseDrag)
            HandleMouseDrag(cfg);

        // ── Auto-pan — ONLY when fully zoomed out AND not dragging ──
        //    FIX #2 & #3: auto-pan was overwriting X position while
        //    zoomed in, making X-drag useless and snapping position
        //    to the pan boundary on drag start.
        //    ADDITIONAL FIX: also check _hasStoredPreZoom so auto-pan
        //    stops IMMEDIATELY on zoom-in, not after the actual FOV
        //    catches up (which caused the camera to drift to the left
        //    boundary during the SmoothDamp lag).
        if (!_isDragging && !IsZoomedIn(cfg) && !_hasStoredPreZoom)
            HandlePan(cfg);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper: is the camera currently zoomed in?
    // ─────────────────────────────────────────────────────────────
    private bool IsZoomedIn(SceneConfig cfg)
    {
        if (!cfg.enableZoom) return false;

        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        // Consider "zoomed in" if we're more than 1 unit below maxZoom
        return currentZoom < cfg.maxZoom - 1f;
    }

    // ─────────────────────────────────────────────────────────────
    //  PageManager detection (unchanged)
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
        _isDragging = false;
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        _targetXZPosition = cfg.startPosition;

        if (cfg.enableZoom)
        {
            cfg.targetZoom = cfg.zoomOutValue;
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, cfg.zoomOutValue);
        }

        Debug.Log($"[CameraManager] Switched to scene: '{cfg.sceneName}'");
    }

    // ─────────────────────────────────────────────────────────────
    //  Auto Pan  (only runs when fully zoomed out and not dragging)
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
    //  Zoom  (FIXED: smooth position interpolation toward cursor)
    //
    //  FIX #1: Instead of instantly jumping the camera position
    //  toward the cursor each scroll tick, we update a _targetXZ
    //  and SmoothDamp the actual position toward it every frame.
    //  This makes position and FOV animate in sync — no bounce.
    //
    //  FIX #3: ResetView only fires when the target zoom AND the
    //  actual zoom are both at maxZoom (truly fully zoomed out),
    //  preventing premature position snaps.
    // ─────────────────────────────────────────────────────────────
    private void HandleZoom(SceneConfig cfg)
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            float prevZoom = cfg.targetZoom;

            cfg.targetZoom -= scroll * cfg.zoomMultiplier;
            cfg.targetZoom = Mathf.Clamp(cfg.targetZoom, cfg.minZoom, cfg.maxZoom);

            float zoomDelta = cfg.targetZoom - prevZoom; // negative = zooming in

            // ── Store the pre-zoom camera position on first zoom-in ──
            if (!_hasStoredPreZoom && zoomDelta < 0f)
            {
                _preZoomPosition = cameraTransform.position;
                _targetXZPosition = cameraTransform.position;
                _positionVelocity = Vector3.zero;
                _hasStoredPreZoom = true;
            }

            // ── Zoom IN: shift the TARGET position toward cursor ──
            if (zoomDelta < 0f)
            {
                Ray ray = cameraDisplay.ScreenPointToRay(Input.mousePosition);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

                if (groundPlane.Raycast(ray, out float enter))
                {
                    Vector3 worldCursor = ray.GetPoint(enter);

                    float fraction = Mathf.Abs(zoomDelta) / (cfg.maxZoom - cfg.minZoom);
                    Vector3 direction = worldCursor - _targetXZPosition;
                    direction.y = 0f;
                    _targetXZPosition += direction * fraction * 1.5f;
                }
            }

            // ── Zoom OUT: blend the TARGET back toward pre-zoom position ──
            if (zoomDelta > 0f && _hasStoredPreZoom)
            {
                float t = Mathf.InverseLerp(cfg.minZoom, cfg.maxZoom, cfg.targetZoom);
                _targetXZPosition.x = Mathf.Lerp(_targetXZPosition.x, _preZoomPosition.x, t * 0.3f);
                _targetXZPosition.z = Mathf.Lerp(_targetXZPosition.z, _preZoomPosition.z, t * 0.3f);
            }
        }

        // ── Smoothly animate FOV/ortho toward target ──────────────
        SetCameraZoomSmooth(cameraDisplay, cfg.targetZoom, cfg);

        // ── Smoothly animate POSITION toward target (FIX #1) ──────
        //    Only when zoomed in — don't fight auto-pan when fully out
        if (_hasStoredPreZoom)
        {
            Vector3 pos = cameraTransform.position;
            Vector3 target = new Vector3(_targetXZPosition.x, pos.y, _targetXZPosition.z);
            Vector3 smoothed = Vector3.SmoothDamp(pos, target, ref _positionVelocity, cfg.positionSmoothTime);
            cameraTransform.position = smoothed;
        }

        // ── Fully zoomed out? Reset only when BOTH target AND actual
        //    are at maxZoom — prevents premature snaps (FIX #3) ────
        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        bool targetIsMax = Mathf.Abs(cfg.targetZoom - cfg.maxZoom) < 0.1f;
        bool actualIsMax = Mathf.Abs(currentZoom - cfg.maxZoom) < 0.5f;

        if (targetIsMax && actualIsMax && _hasStoredPreZoom)
        {
            cameraTransform.position = cfg.startPosition;
            cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);
            _hasStoredPreZoom = false;
            _positionVelocity = Vector3.zero;
        }
    }

    private void SetCameraZoomSmooth(Camera cam, float targetValue, SceneConfig cfg)
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

    private void SetCameraZoomImmediate(Camera cam, float value)
    {
        if (cam.orthographic)
            cam.orthographicSize = value;
        else
            cam.fieldOfView = value;
    }

    // ─────────────────────────────────────────────────────────────
    //  Mouse Drag Pan
    //
    //  FIX #2: X-axis drag now works because auto-pan is disabled
    //  while zoomed in (see Update). The drag position is also
    //  synced to _targetXZPosition so zoom and drag don't fight.
    //
    //  FIX #3: Drag starts from wherever the camera currently is
    //  (the zoom-toward-cursor position), not from the auto-pan
    //  boundary.
    // ─────────────────────────────────────────────────────────────
    private void HandleMouseDrag(SceneConfig cfg)
    {
        // ── Placement guard: don't pan camera while placing a device ──
        if (PlacementManager.Instance != null && PlacementManager.Instance.IsDragging)
        {
            _isDragging = false;
            return;
        }

        // ── Check if zoom level allows drag ───────────────────────
        bool zoomedInEnough = true;
        if (cfg.enableZoom && cfg.dragZoomThreshold > 0f)
        {
            float currentZoom = cameraDisplay.orthographic
                ? cameraDisplay.orthographicSize
                : cameraDisplay.fieldOfView;

            zoomedInEnough = currentZoom < cfg.dragZoomThreshold;
        }

        // ── Mouse button DOWN — start drag ────────────────────────
        if (Input.GetMouseButtonDown(0) && zoomedInEnough)
        {
            _isDragging = true;
            _lastMousePos = Input.mousePosition;
        }

        // ── Mouse button UP — end drag ────────────────────────────
        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        if (!_isDragging) return;

        // ── If zoom changed mid-drag and player is no longer zoomed in,
        //    cancel the drag gracefully
        if (!zoomedInEnough)
        {
            _isDragging = false;
            return;
        }

        // ── Frame-by-frame mouse delta ────────────────────────────
        Vector3 currentMouse = Input.mousePosition;
        Vector3 frameDelta = currentMouse - _lastMousePos;
        _lastMousePos = currentMouse;

        float currentZoomVal = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;
        float zoomFactor = currentZoomVal / cfg.maxZoom;

        float sensitivity = cfg.dragSensitivity * zoomFactor;

        // ── FIX #1: Use camera-relative axes so drag works
        //    regardless of camera Y rotation (e.g. -90°).
        //    Screen-horizontal → camera right, screen-vertical → camera forward,
        //    both projected onto the horizontal (XZ) plane.
        Vector3 camRight = cameraTransform.right;
        Vector3 camForward = cameraTransform.forward;
        camRight.y = 0f; camRight.Normalize();
        camForward.y = 0f; camForward.Normalize();

        Vector3 worldDelta = -(frameDelta.x * camRight + frameDelta.y * camForward) * sensitivity;

        float deltaX = worldDelta.x;
        float deltaZ = worldDelta.z;

        // ── Update the TARGET position (not the camera directly) ──
        //    This keeps drag and zoom-toward-cursor in sync.
        _targetXZPosition.x += deltaX;
        if (cfg.dragAxisZ)
            _targetXZPosition.z += deltaZ;

        // ── Clamp target within map bounds ────────────────────────
        _targetXZPosition.x = Mathf.Clamp(_targetXZPosition.x, cfg.minLocationX, cfg.maxLocationX);
        if (cfg.dragAxisZ)
            _targetXZPosition.z = Mathf.Clamp(_targetXZPosition.z, cfg.minLocationZ, cfg.maxLocationZ);

        // ── Apply directly during drag for responsive feel ────────
        //    (the SmoothDamp in HandleZoom will also run but the
        //    target matches so it won't fight)
        Vector3 newPos = cameraTransform.position;
        newPos.x = Mathf.Clamp(newPos.x + deltaX, cfg.minLocationX, cfg.maxLocationX);
        if (cfg.dragAxisZ)
            newPos.z = Mathf.Clamp(newPos.z + deltaZ, cfg.minLocationZ, cfg.maxLocationZ);

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