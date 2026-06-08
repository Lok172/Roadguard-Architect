using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CameraManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Camera mode — only one can be active at a time
    // ─────────────────────────────────────────────────────────────
    public enum CameraMode { None, AutoPan, Zoom }

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

        [Tooltip("X-axis bounds — used by auto-pan and zoom-pan")]
        public float minLocationX = 110f;
        [Tooltip("X-axis bounds — used by auto-pan and zoom-pan")]
        public float maxLocationX = 270f;

        [Tooltip("Z-axis bounds — used by zoom-pan only")]
        public float minLocationZ = 80f;
        [Tooltip("Z-axis bounds — used by zoom-pan only")]
        public float maxLocationZ = 140f;

        // ── Camera Mode ────────────────────────────────────────
        [Header("Camera Mode (Auto Pan or Zoom — pick one)")]
        public CameraMode cameraMode = CameraMode.AutoPan;

        // ── Auto Pan ───────────────────────────────────────────
        [Header("Auto Pan Settings")]
        public float movementSpeed = 5f;

        // ── Zoom ───────────────────────────────────────────────
        [Header("Zoom Settings")]
        public float zoomMultiplier = 50f;
        public float minZoom = 30f;
        public float maxZoom = 90f;

        [Tooltip("FOV/ortho size the reveal STARTS at. Must be between Min Zoom " +
                 "and Max Zoom — values below Min Zoom are clamped automatically.")]
        public float initialZoom = 60f;

        [Tooltip("Automatically transition from Initial Zoom to Max Zoom when the scene loads")]
        public bool autoTransitionOnStart = false;

        [Tooltip("Duration (seconds) of the auto-transition from Initial Zoom to Max Zoom")]
        public float transitionDuration = 2f;

        [Tooltip("Seconds to wait before the zoom transition begins")]
        public float transitionDelay = 2f;

        public float zoomSmoothTime = 0.25f;

        [Tooltip("How quickly the camera position catches up to the target " +
                 "during zoom-toward-cursor. Lower = smoother but laggier.")]
        public float positionSmoothTime = 0.2f;

        [Tooltip("How sensitive the drag is — higher = moves more per pixel")]
        public float dragSensitivity = 0.3f;

        [Tooltip("Only allow drag when zoomed in beyond this threshold. " +
                 "Set to 0 to always allow drag regardless of zoom level. " +
                 "Also marks where the camera starts gliding back to start as you zoom out — " +
                 "a LOWER value gives a longer, smoother return.")]
        public float dragZoomThreshold = 55f;

        // ── Internal zoom state (not shown in inspector) ───────
        [HideInInspector] public float targetZoom;
        [HideInInspector] public float zoomVelocity;
    }

    // ─────────────────────────────────────────────────────────────
    //  Inspector fields
    // ─────────────────────────────────────────────────────────────
    [Header("Scene List")]
    [SerializeField] private List<SceneConfig> scenes = new List<SceneConfig>();

    [Header("Camera Reference (auto-found if empty)")]
    [SerializeField] private Camera cameraDisplay;

    [Header("Working Area Boundary")]
    [Tooltip("Assign a flat Quad/Plane on the ground. The camera will be " +
             "clamped so the screen NEVER shows anything beyond this rectangle. " +
             "Leave empty to use per-scene min/max bounds only.")]
    [SerializeField] private Transform workingArea;

    // ─────────────────────────────────────────────────────────────
    //  Private state
    // ─────────────────────────────────────────────────────────────
    private Transform cameraTransform;
    private int panDirection = 1;
    private int activeSceneIndex = -1;

    // ── Drag state ──────────────────────────────────────────────
    private bool _isDragging = false;
    private Vector3 _lastMousePos = Vector3.zero;

    // ── Smooth zoom-toward-cursor state ─────────────────────────
    private Vector3 _preZoomPosition;
    private bool _hasStoredPreZoom = false;
    private Vector3 _targetXZPosition;
    private Vector3 _positionVelocity;

    // ── Zoom transition coroutine handle ────────────────────────
    private Coroutine _zoomTransitionCoroutine;

    // ── Cached working-area bounds (refreshed every frame) ──────
    private bool _hasWorkingAreaBounds;
    private float _waMinX, _waMaxX, _waMinZ, _waMaxZ, _waGroundY;

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

        switch (cfg.cameraMode)
        {
            case CameraMode.Zoom:
                HandleZoom(cfg);
                HandleMouseDrag(cfg);
                break;

            case CameraMode.AutoPan:
                HandlePan(cfg);
                break;
        }

        // ── Final hard clamp: guarantee nothing beyond workingArea is visible ──
        ClampCameraToWorkingArea();
    }

    // ─────────────────────────────────────────────────────────────
    //  Working-area frustum clamp
    //  Raycasts the four screen corners onto the ground plane and
    //  pushes the camera back if any visible point spills outside.
    // ─────────────────────────────────────────────────────────────
    private void RefreshWorkingAreaBounds()
    {
        _hasWorkingAreaBounds = false;
        if (workingArea == null) return;

        Renderer rend = workingArea.GetComponent<Renderer>();
        if (rend != null)
        {
            Bounds b = rend.bounds;
            _waMinX = b.min.x;
            _waMaxX = b.max.x;
            _waMinZ = b.min.z;
            _waMaxZ = b.max.z;
            _waGroundY = b.center.y;
            _hasWorkingAreaBounds = true;
            return;
        }

        // Fallback for objects with no Renderer — use localScale of a Quad
        // (Unity default Quad is 1×1; Plane is 10×10).
        Collider col = workingArea.GetComponent<Collider>();
        if (col != null)
        {
            Bounds b = col.bounds;
            _waMinX = b.min.x;
            _waMaxX = b.max.x;
            _waMinZ = b.min.z;
            _waMaxZ = b.max.z;
            _waGroundY = b.center.y;
            _hasWorkingAreaBounds = true;
        }
    }

    private void ClampCameraToWorkingArea()
    {
        RefreshWorkingAreaBounds();
        if (!_hasWorkingAreaBounds || cameraDisplay == null) return;

        Plane ground = new Plane(Vector3.up, new Vector3(0f, _waGroundY, 0f));

        // Raycast all four screen corners to the ground plane.
        float visMinX = float.MaxValue, visMaxX = float.MinValue;
        float visMinZ = float.MaxValue, visMaxZ = float.MinValue;
        int hits = 0;

        Vector3[] corners = new Vector3[]
        {
            new Vector3(0f,            0f,             0f),
            new Vector3(Screen.width,  0f,             0f),
            new Vector3(Screen.width,  Screen.height,  0f),
            new Vector3(0f,            Screen.height,  0f)
        };

        foreach (Vector3 corner in corners)
        {
            Ray ray = cameraDisplay.ScreenPointToRay(corner);
            if (ground.Raycast(ray, out float enter))
            {
                Vector3 pt = ray.GetPoint(enter);
                if (pt.x < visMinX) visMinX = pt.x;
                if (pt.x > visMaxX) visMaxX = pt.x;
                if (pt.z < visMinZ) visMinZ = pt.z;
                if (pt.z > visMaxZ) visMaxZ = pt.z;
                hits++;
            }
        }

        // If not all corners hit (camera nearly parallel to ground), bail out.
        if (hits < 4) return;

        // Calculate how much the visible rect overshoots.
        float shiftX = 0f;
        if (visMinX < _waMinX) shiftX = _waMinX - visMinX;
        else if (visMaxX > _waMaxX) shiftX = _waMaxX - visMaxX;

        float shiftZ = 0f;
        if (visMinZ < _waMinZ) shiftZ = _waMinZ - visMinZ;
        else if (visMaxZ > _waMaxZ) shiftZ = _waMaxZ - visMaxZ;

        if (Mathf.Abs(shiftX) < 0.001f && Mathf.Abs(shiftZ) < 0.001f) return;

        // Push the camera back inside.
        Vector3 pos = cameraTransform.position;
        pos.x += shiftX;
        pos.z += shiftZ;
        cameraTransform.position = pos;

        // Keep the smooth-damp target in sync so it doesn't fight back.
        _targetXZPosition.x += shiftX;
        _targetXZPosition.z += shiftZ;
    }

    // ─────────────────────────────────────────────────────────────
    //  Helper: is the camera currently zoomed in?
    // ─────────────────────────────────────────────────────────────
    private bool IsZoomedIn(SceneConfig cfg)
    {
        if (cfg.cameraMode != CameraMode.Zoom) return false;

        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        return currentZoom < cfg.maxZoom - 1f;
    }

    // ─────────────────────────────────────────────────────────────
    //  PageManager detection
    // ─────────────────────────────────────────────────────────────
    private string lastDetectedUI = null;

    private void DetectAndApplyCurrentUI()
    {
        PageManager pm = Object.FindFirstObjectByType<PageManager>();
        if (pm == null) return;

        string currentUI = pm.currentLoadedUI;
        if (currentUI == lastDetectedUI) return;

        lastDetectedUI = currentUI;

        int idx = scenes.FindIndex(s => s.sceneName == currentUI);
        if (idx >= 0)
        {
            // Switching to another configured level → full apply + reveal.
            SwitchToScene(idx);
        }
        else
        {
            // We just left a configured level for a UI that has NO camera
            // config (e.g. a menu). Reset the camera we were showing so its
            // zoom/position don't linger, then disable camera control until
            // a configured level is loaded again.
            ResetActiveCameraOnExit();
            activeSceneIndex = -1;
        }
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

    /// <summary>
    /// Smoothly transitions the camera from initialZoom to maxZoom
    /// over the given duration (in seconds). Useful for cinematic
    /// zoom-out reveals or resetting the view programmatically.
    /// </summary>
    public void TransitionToMaxZoom(float duration = 2f)
    {
        SceneConfig cfg = ActiveConfig;
        if (cfg == null || cfg.cameraMode != CameraMode.Zoom)
        {
            Debug.LogWarning("[CameraManager] TransitionToMaxZoom requires an active scene with Zoom mode.");
            return;
        }

        if (_zoomTransitionCoroutine != null)
        {
            StopCoroutine(_zoomTransitionCoroutine);

            if (GameManager.Instance != null)
                GameManager.Instance.ResumeDayTick();
        }

        _zoomTransitionCoroutine = StartCoroutine(ZoomTransitionRoutine(cfg, duration));
    }

    private IEnumerator ZoomTransitionRoutine(SceneConfig cfg, float duration)
    {
        // Pause the game day tick while the transition plays
        if (GameManager.Instance != null)
            GameManager.Instance.PauseDayTick();

        // FIX: clamp the reveal's start zoom into the valid range so a
        // misconfigured Initial Zoom (e.g. below Min Zoom) can't produce a
        // degenerate "pinhole" view at the start of the reveal.
        float startZoom = Mathf.Clamp(cfg.initialZoom, cfg.minZoom, cfg.maxZoom);
        float endZoom = cfg.maxZoom;

        // Snap camera to initial zoom as the starting point
        SetCameraZoomImmediate(cameraDisplay, startZoom);
        cfg.targetZoom = startZoom;
        cfg.zoomVelocity = 0f;

        // Reset position to start (like a fresh scene load)
        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;
        _isDragging = false;

        // Optional delay before the reveal begins
        if (cfg.transitionDelay > 0f)
            yield return new WaitForSeconds(cfg.transitionDelay);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth ease-in-out curve
            float smooth = t * t * (3f - 2f * t);

            float currentZoom = Mathf.Lerp(startZoom, endZoom, smooth);

            if (cameraDisplay.orthographic)
                cameraDisplay.orthographicSize = currentZoom;
            else
                cameraDisplay.fieldOfView = currentZoom;

            cfg.targetZoom = currentZoom;

            yield return null;
        }

        // Ensure we land exactly on maxZoom
        SetCameraZoomImmediate(cameraDisplay, endZoom);
        cfg.targetZoom = endZoom;
        cfg.zoomVelocity = 0f;

        // Hold for a moment before the game starts
        yield return new WaitForSeconds(2f);

        // Resume the game day tick now that the transition is done
        if (GameManager.Instance != null)
            GameManager.Instance.ResumeDayTick();

        _zoomTransitionCoroutine = null;
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

        // Stop any running zoom transition (and resume day tick if it was paused)
        if (_zoomTransitionCoroutine != null)
        {
            StopCoroutine(_zoomTransitionCoroutine);
            _zoomTransitionCoroutine = null;

            if (GameManager.Instance != null)
                GameManager.Instance.ResumeDayTick();
        }

        panDirection = 1;
        _isDragging = false;
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        _targetXZPosition = cfg.startPosition;

        if (cfg.cameraMode == CameraMode.Zoom)
        {
            // FIX: clamp Initial Zoom into [minZoom, maxZoom] so a stray value
            // (like 1 when Min Zoom is 4) can't snap the camera to a pinhole.
            float startZoom = Mathf.Clamp(cfg.initialZoom, cfg.minZoom, cfg.maxZoom);
            cfg.targetZoom = startZoom;
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, startZoom);

            // Auto-transition from initialZoom → maxZoom on scene load
            if (cfg.autoTransitionOnStart)
                _zoomTransitionCoroutine = StartCoroutine(ZoomTransitionRoutine(cfg, cfg.transitionDuration));
        }

        Debug.Log($"[CameraManager] Switched to scene: '{cfg.sceneName}'");
    }

    /// <summary>
    /// Called when the player leaves a configured level for a UI that has no
    /// camera config (e.g. a menu). Snaps the camera back to a clean, fully
    /// zoomed-out resting state and clears all interactive zoom/drag state so
    /// nothing lingers and re-entering the level starts fresh.
    /// </summary>
    private void ResetActiveCameraOnExit()
    {
        SceneConfig cfg = ActiveConfig;
        if (cfg == null || cameraTransform == null) return;

        if (_zoomTransitionCoroutine != null)
        {
            StopCoroutine(_zoomTransitionCoroutine);
            _zoomTransitionCoroutine = null;

            if (GameManager.Instance != null)
                GameManager.Instance.ResumeDayTick();
        }

        _isDragging = false;
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);
        _targetXZPosition = cfg.startPosition;

        if (cfg.cameraMode == CameraMode.Zoom)
        {
            cfg.targetZoom = cfg.maxZoom;   // clean, fully zoomed-out state
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, cfg.maxZoom);
        }

        Debug.Log("[CameraManager] Left configured level → camera reset to resting state.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Auto Pan
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
    //  Zoom (smooth position interpolation toward cursor)
    // ─────────────────────────────────────────────────────────────
    private void HandleZoom(SceneConfig cfg)
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            float prevZoom = cfg.targetZoom;

            cfg.targetZoom -= scroll * cfg.zoomMultiplier;
            cfg.targetZoom = Mathf.Clamp(cfg.targetZoom, cfg.minZoom, cfg.maxZoom);

            float zoomDelta = cfg.targetZoom - prevZoom;

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

            // NOTE: Zoom-OUT no longer blends here. The return-to-start is
            // computed every frame below and LOCKED to the zoom level, so it
            // keeps working after you stop scrolling and after you drag, and
            // always lands EXACTLY on startPosition at full zoom-out.
        }

        // ── Smoothly animate FOV/ortho toward target ──────────────
        SetCameraZoomSmooth(cameraDisplay, cfg.targetZoom, cfg);

        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        // ── Animate POSITION, blending toward start as we zoom out ──
        if (_hasStoredPreZoom)
        {
            // returnT is 0 while inside the draggable (zoomed-in) range, then
            // ramps to 1 at max zoom. Because it is a pure function of the
            // current zoom level, there is no idle drift while you hold a zoom,
            // dragging stays fully free below the threshold, and the target is
            // EXACTLY startPosition once zoom reaches max — even after a drag.
            float bandStart = (cfg.dragZoomThreshold > 0f) ? cfg.dragZoomThreshold : cfg.minZoom;
            float returnT = Mathf.InverseLerp(bandStart, cfg.maxZoom, currentZoom);

            float blendedX = Mathf.Lerp(_targetXZPosition.x, cfg.startPosition.x, returnT);
            float blendedZ = Mathf.Lerp(_targetXZPosition.z, cfg.startPosition.z, returnT);

            Vector3 pos = cameraTransform.position;
            Vector3 target = new Vector3(blendedX, pos.y, blendedZ);
            Vector3 smoothed = Vector3.SmoothDamp(pos, target, ref _positionVelocity, cfg.positionSmoothTime);
            cameraTransform.position = smoothed;
        }

        // ── Fully zoomed out? Land EXACTLY on start, then clear state ──
        // (No position-distance gate: by this point returnT is 1 so the camera
        //  is already at start — this just guarantees a pixel-perfect landing.)
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
    //  Mouse Drag Pan (always active when Zoom mode is enabled)
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
        if (cfg.dragZoomThreshold > 0f)
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

        // ── Camera-relative axes so drag works regardless of rotation ──
        Vector3 camRight = cameraTransform.right;
        Vector3 camForward = cameraTransform.forward;
        camRight.y = 0f; camRight.Normalize();
        camForward.y = 0f; camForward.Normalize();

        Vector3 worldDelta = -(frameDelta.x * camRight + frameDelta.y * camForward) * sensitivity;

        float deltaX = worldDelta.x;
        float deltaZ = worldDelta.z;

        // ── Update the TARGET position (keeps drag and zoom in sync) ──
        _targetXZPosition.x += deltaX;
        _targetXZPosition.z += deltaZ;

        // ── Clamp target within map bounds ────────────────────────
        _targetXZPosition.x = Mathf.Clamp(_targetXZPosition.x, cfg.minLocationX, cfg.maxLocationX);
        _targetXZPosition.z = Mathf.Clamp(_targetXZPosition.z, cfg.minLocationZ, cfg.maxLocationZ);

        // ── Apply directly during drag for responsive feel ────────
        Vector3 newPos = cameraTransform.position;
        newPos.x = Mathf.Clamp(newPos.x + deltaX, cfg.minLocationX, cfg.maxLocationX);
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

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Transition: Initial Zoom → Max Zoom"))
            manager.TransitionToMaxZoom();
    }
}
#endif