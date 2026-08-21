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

        // ── Camera Mode ────────────────────────────────────────
        [Header("Camera Mode (Auto Pan or Zoom — pick one)")]
        public CameraMode cameraMode = CameraMode.AutoPan;

        // ── Auto Pan ───────────────────────────────────────────
        [Header("Auto Pan Settings")]
        public float movementSpeed = 5f;

        [Tooltip("The X position the camera pans TO before reversing. " +
                 "Leave at (0,0,0) to fall back to the working-area bounds.")]
        public Vector3 panEndPosition = Vector3.zero;

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

        [Tooltip("Seconds to hold the view AFTER the zoom transition finishes, before the game starts")]
        public float postTransitionHold = 2f;

        public float zoomSmoothTime = 0.25f;

        [Tooltip("How quickly the camera position catches up to the target " +
                 "during zoom-toward-cursor. Lower = smoother but laggier.")]
        public float positionSmoothTime = 0.2f;

        [Tooltip("How sensitive the drag is — higher = moves more per pixel")]
        public float dragSensitivity = 0.3f;

        [Tooltip("Only allow drag when zoomed in beyond this threshold. " +
                 "Set to 0 to always allow drag regardless of zoom level.")]
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
    public bool IsLevelRevealTransitionRunning => _zoomTransitionCoroutine != null;

    // ── Working-area bounds (tag: "WorkingArea") ────────────────
    private bool _hasWorkingArea;
    private float _waMinX, _waMaxX;
    private float _waMinZ, _waMaxZ;
    private float _waGroundY;

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
        if (cameraTransform == null)
        {
            InitialiseCamera();
            if (cameraTransform == null) return;
        }

        DetectAndApplyCurrentUI();

        if (PauseMenuController.GameIsPaused) return;

        SceneConfig cfg = ActiveConfig;
        if (cfg == null) return;

        // Re-attempt WorkingArea caching if it wasn't found yet. This can
        // happen right after returning to this scene: PageManager can report
        // the new currentLoadedUI a frame or two before the additively-loaded
        // scene has finished registering the "WorkingArea"-tagged object.
        // If we never retry, _hasWorkingArea stays false for the whole visit,
        // AutoPan falls through to GetCameraBounds()'s unclamped fallback
        // (float.MinValue/MaxValue), and the camera pans in one direction
        // forever instead of bouncing back — which is the "drifts left until
        // it sees outside the model" symptom.
        if (!_hasWorkingArea)
        {
            CacheWorkingArea();
        }

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
            SwitchToScene(idx);
        }
        else
        {
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
    /// over the given duration (in seconds).
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
        if (GameManager.Instance != null)
            GameManager.Instance.PauseDayTick();

        float startZoom = Mathf.Clamp(cfg.initialZoom, cfg.minZoom, cfg.maxZoom);
        float endZoom = cfg.maxZoom;

        SetCameraZoomImmediate(cameraDisplay, startZoom);
        cfg.targetZoom = startZoom;
        cfg.zoomVelocity = 0f;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;
        _isDragging = false;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Planning Phase deliberately uses Time.timeScale = 0. The camera
            // reveal is presentation-only, so it must use real/unscaled time.
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);

            float currentZoom = Mathf.Lerp(startZoom, endZoom, smooth);

            if (cameraDisplay.orthographic)
                cameraDisplay.orthographicSize = currentZoom;
            else
                cameraDisplay.fieldOfView = currentZoom;

            cfg.targetZoom = currentZoom;

            yield return null;
        }

        SetCameraZoomImmediate(cameraDisplay, endZoom);
        cfg.targetZoom = endZoom;
        cfg.zoomVelocity = 0f;

        if (cfg.postTransitionHold > 0f)
            yield return new WaitForSecondsRealtime(cfg.postTransitionHold);

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

    // ─────────────────────────────────────────────────────────────
    //  Working-area detection
    // ─────────────────────────────────────────────────────────────
    private void CacheWorkingArea()
    {
        _hasWorkingArea = false;

        GameObject wa = GameObject.FindGameObjectWithTag("WorkingArea");
        if (wa == null)
        {
            Debug.LogWarning("[CameraManager] No GameObject tagged 'WorkingArea' found — " +
                             "camera bounds will be unclamped.");
            return;
        }

        Renderer rend = wa.GetComponent<Renderer>();
        if (rend == null)
        {
            Debug.LogWarning("[CameraManager] WorkingArea object has no Renderer — " +
                             "camera bounds will be unclamped.");
            return;
        }

        Bounds b = rend.bounds;
        _waMinX = b.min.x;
        _waMaxX = b.max.x;
        _waMinZ = b.min.z;
        _waMaxZ = b.max.z;
        _waGroundY = b.center.y;
        _hasWorkingArea = true;

        Debug.Log($"[CameraManager] WorkingArea cached — " +
                  $"X [{_waMinX:F1}, {_waMaxX:F1}]  " +
                  $"Z [{_waMinZ:F1}, {_waMaxZ:F1}]  " +
                  $"groundY {_waGroundY:F1}");
    }

    private void ApplyScene(int index)
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

        panDirection = 1;
        _isDragging = false;
        _hasStoredPreZoom = false;
        _positionVelocity = Vector3.zero;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        _targetXZPosition = cfg.startPosition;

        CacheWorkingArea();

        if (cfg.cameraMode == CameraMode.Zoom)
        {
            float startZoom = Mathf.Clamp(cfg.initialZoom, cfg.minZoom, cfg.maxZoom);
            cfg.targetZoom = startZoom;
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, startZoom);

            if (cfg.autoTransitionOnStart)
                _zoomTransitionCoroutine = StartCoroutine(ZoomTransitionRoutine(cfg, cfg.transitionDuration));
        }
        else // AutoPan
        {
            // FOV is a single shared property on this Camera component.
            // AutoPan scenes don't manage zoom interactively, so without
            // this, whatever FOV the previous scene (e.g. a Zoom-mode
            // level) left behind carries straight through — making the
            // pan look wider/narrower than intended even though position
            // and rotation get reset correctly. Pin it to this scene's
            // resting value every time it's applied.
            cfg.targetZoom = cfg.maxZoom;
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, cfg.maxZoom);
        }

        Debug.Log($"[CameraManager] Switched to scene: '{cfg.sceneName}'");
    }

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
            cfg.targetZoom = cfg.maxZoom;
            cfg.zoomVelocity = 0f;
            SetCameraZoomImmediate(cameraDisplay, cfg.maxZoom);
        }

        Debug.Log("[CameraManager] Left configured level → camera reset to resting state.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Auto Pan
    //
    //  If panEndPosition is set (non-zero), the camera bounces
    //  between startPosition.x and panEndPosition.x, clamped to
    //  whichever is the smaller/larger of the two.
    //  Otherwise falls back to the working-area X bounds.
    // ─────────────────────────────────────────────────────────────
    private void HandlePan(SceneConfig cfg)
    {
        bool hasExplicitEnd = cfg.panEndPosition != Vector3.zero;

        // Safety net: with no explicit pan range configured, we depend on
        // the WorkingArea bounds to know where to turn around. If those
        // bounds aren't cached yet, freeze in place for this frame rather
        // than panning with no clamp at all (which is what was pushing the
        // camera further and further left with no bounce-back).
        if (!hasExplicitEnd && !_hasWorkingArea) return;

        // Unscaled: AutoPan is presentation, not simulation — it must keep
        // moving even while Time.timeScale is 0 during a Planning Phase pause.
        float movement = cfg.movementSpeed * Time.unscaledDeltaTime * panDirection;
        Vector3 pos = cameraTransform.position;
        pos.x += movement;
        pos.y = cfg.startPosition.y;
        pos.z = cfg.startPosition.z;

        // Determine the X range for this scene.
        float panMinX, panMaxX;

        if (hasExplicitEnd)
        {
            panMinX = Mathf.Min(cfg.startPosition.x, cfg.panEndPosition.x);
            panMaxX = Mathf.Max(cfg.startPosition.x, cfg.panEndPosition.x);
        }
        else
        {
            var (wMinX, wMaxX, _, _) = GetCameraBounds(cfg);
            panMinX = wMinX;
            panMaxX = wMaxX;
        }

        if (pos.x >= panMaxX || pos.x <= panMinX)
            panDirection *= -1;

        pos.x = Mathf.Clamp(pos.x, panMinX, panMaxX);
        cameraTransform.position = pos;

        // Lock rotation every frame rather than relying solely on the
        // one-time reset in ApplyScene(). If anything else touches this
        // shared camera's rotation after we hand off (e.g. Level 2's own
        // camera control still running for a frame), this pulls it back
        // instead of letting it silently persist into the pan.
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);
    }

    // ─────────────────────────────────────────────────────────────
    //  Zoom — zoom-ratio approach for accurate cursor tracking
    //
    //  Uses  newZoom / oldZoom  to compute the proportional camera
    //  shift each scroll step.  This ensures the world-point under
    //  the cursor stays visually pinned during zoom-in, even when
    //  starting from max zoom.
    //
    //  On zoom-out the target position blends back toward
    //  startPosition so the camera returns home naturally.
    // ─────────────────────────────────────────────────────────────
    private void HandleZoom(SceneConfig cfg)
    {
        // ── Block input during opening transition ────────────────
        if (_zoomTransitionCoroutine != null) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            float prevZoom = cfg.targetZoom;

            cfg.targetZoom = Mathf.Clamp(
                cfg.targetZoom - scroll * cfg.zoomMultiplier,
                cfg.minZoom,
                cfg.maxZoom);

            float zoomDelta = cfg.targetZoom - prevZoom;

            // Clamped with no actual change — kill residual velocity
            if (Mathf.Approximately(zoomDelta, 0f))
            {
                _positionVelocity = Vector3.zero;
            }
            else
            {
                // ── Store pre-zoom position on first zoom-in ─────────
                if (!_hasStoredPreZoom && zoomDelta < 0f)
                {
                    _preZoomPosition = cameraTransform.position;
                    _targetXZPosition = cameraTransform.position;
                    _positionVelocity = Vector3.zero;
                    _hasStoredPreZoom = true;
                }

                // ── ZOOM IN → shift target toward cursor ─────────────
                if (zoomDelta < 0f)
                {
                    Ray ray = cameraDisplay.ScreenPointToRay(Input.mousePosition);
                    Plane groundPlane = new Plane(Vector3.up,
                        new Vector3(0f, _hasWorkingArea ? _waGroundY : 0f, 0f));

                    if (groundPlane.Raycast(ray, out float enter))
                    {
                        Vector3 worldCursor = ray.GetPoint(enter);

                        // Zoom-ratio: the fraction of the view that "collapsed"
                        // toward the cursor this step.  Gives a proportionally
                        // correct shift regardless of current zoom level.
                        float zoomRatio = cfg.targetZoom / prevZoom;  // < 1
                        Vector3 dir = worldCursor - _targetXZPosition;
                        dir.y = 0f;
                        _targetXZPosition += dir * (1f - zoomRatio);
                    }
                }
                // ── ZOOM OUT → blend target back toward start ────────
                else if (_hasStoredPreZoom)
                {
                    // How close we are to fully zoomed out (0 = minZoom, 1 = maxZoom)
                    float normalised = Mathf.InverseLerp(cfg.minZoom, cfg.maxZoom, cfg.targetZoom);

                    // Blend strength ramps up as we approach maxZoom so the
                    // camera arrives at startPosition right when fully zoomed out
                    float blendStrength = normalised * 0.25f;

                    _targetXZPosition = Vector3.Lerp(
                        _targetXZPosition, cfg.startPosition, blendStrength);
                }

                // ── Clamp target to working-area bounds ──────────────
                if (_hasWorkingArea)
                {
                    _targetXZPosition.x = Mathf.Clamp(_targetXZPosition.x, _waMinX, _waMaxX);
                    _targetXZPosition.z = Mathf.Clamp(_targetXZPosition.z, _waMinZ, _waMaxZ);
                }
            }
        }

        // ── Smoothly animate FOV / ortho toward target ───────────
        SetCameraZoomSmooth(cameraDisplay, cfg.targetZoom, cfg);

        float currentZoom = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;

        // ── Animate position toward _targetXZPosition ────────────
        if (_hasStoredPreZoom)
        {
            // Lock Y to startPosition — zoom never intentionally changes height
            Vector3 target = new Vector3(
                _targetXZPosition.x,
                cfg.startPosition.y,
                _targetXZPosition.z);

            Vector3 smoothed = Vector3.SmoothDamp(
                cameraTransform.position, target,
                ref _positionVelocity, cfg.positionSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);

            cameraTransform.position = smoothed;
        }

        // ── Fully zoomed out → snap exactly to start, clear state
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
        // Unscaled: player-driven zoom must keep responding even while
        // Time.timeScale is 0 during a Planning Phase pause. SmoothDamp's
        // short-form overload implicitly uses (scaled) Time.deltaTime, so we
        // pass unscaledDeltaTime explicitly via the full overload.
        if (cam.orthographic)
        {
            cam.orthographicSize = Mathf.SmoothDamp(
                cam.orthographicSize, targetValue,
                ref cfg.zoomVelocity, cfg.zoomSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
        }
        else
        {
            cam.fieldOfView = Mathf.SmoothDamp(
                cam.fieldOfView, targetValue,
                ref cfg.zoomVelocity, cfg.zoomSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
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
    //  Frustum-inset camera bounds
    // ─────────────────────────────────────────────────────────────
    private (float minX, float maxX, float minZ, float maxZ) GetCameraBounds(SceneConfig cfg)
    {
        if (!_hasWorkingArea)
            return (float.MinValue, float.MaxValue, float.MinValue, float.MaxValue);

        Plane ground = new Plane(Vector3.up, new Vector3(0f, _waGroundY, 0f));
        Vector3 camPos = cameraTransform.position;

        Vector3[] vpCorners =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 1f, 0f)
        };

        float visMinX = camPos.x, visMaxX = camPos.x;
        float visMinZ = camPos.z, visMaxZ = camPos.z;
        bool anyHit = false;

        foreach (var vp in vpCorners)
        {
            Ray ray = cameraDisplay.ViewportPointToRay(vp);
            if (ground.Raycast(ray, out float dist) && dist > 0f)
            {
                Vector3 hit = ray.GetPoint(dist);
                if (!anyHit)
                {
                    visMinX = visMaxX = hit.x;
                    visMinZ = visMaxZ = hit.z;
                    anyHit = true;
                }
                else
                {
                    visMinX = Mathf.Min(visMinX, hit.x);
                    visMaxX = Mathf.Max(visMaxX, hit.x);
                    visMinZ = Mathf.Min(visMinZ, hit.z);
                    visMaxZ = Mathf.Max(visMaxZ, hit.z);
                }
            }
        }

        if (!anyHit)
            return (_waMinX, _waMaxX, _waMinZ, _waMaxZ);

        float extLeft = camPos.x - visMinX;
        float extRight = visMaxX - camPos.x;
        float extBack = camPos.z - visMinZ;
        float extFront = visMaxZ - camPos.z;

        float clampMinX = _waMinX + extLeft;
        float clampMaxX = _waMaxX - extRight;
        float clampMinZ = _waMinZ + extBack;
        float clampMaxZ = _waMaxZ - extFront;

        if (clampMinX > clampMaxX)
            clampMinX = clampMaxX = (_waMinX + _waMaxX) * 0.5f;
        if (clampMinZ > clampMaxZ)
            clampMinZ = clampMaxZ = (_waMinZ + _waMaxZ) * 0.5f;

        return (clampMinX, clampMaxX, clampMinZ, clampMaxZ);
    }

    // ─────────────────────────────────────────────────────────────
    //  Mouse Drag Pan
    // ─────────────────────────────────────────────────────────────
    private void HandleMouseDrag(SceneConfig cfg)
    {
        // ── Block input during opening transition ────────────────
        if (_zoomTransitionCoroutine != null)
        {
            _isDragging = false;
            return;
        }

        if (PlacementManager.Instance != null && PlacementManager.Instance.IsDragging)
        {
            _isDragging = false;
            return;
        }

        bool zoomedInEnough = true;
        if (cfg.dragZoomThreshold > 0f)
        {
            float currentZoom = cameraDisplay.orthographic
                ? cameraDisplay.orthographicSize
                : cameraDisplay.fieldOfView;

            zoomedInEnough = currentZoom < cfg.dragZoomThreshold;
        }

        if (Input.GetMouseButtonDown(0) && zoomedInEnough)
        {
            _isDragging = true;
            _lastMousePos = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        if (!_isDragging) return;

        if (!zoomedInEnough)
        {
            _isDragging = false;
            return;
        }

        Vector3 currentMouse = Input.mousePosition;
        Vector3 frameDelta = currentMouse - _lastMousePos;
        _lastMousePos = currentMouse;

        float currentZoomVal = cameraDisplay.orthographic
            ? cameraDisplay.orthographicSize
            : cameraDisplay.fieldOfView;
        float zoomFactor = currentZoomVal / cfg.maxZoom;

        float sensitivity = cfg.dragSensitivity * zoomFactor;

        Vector3 camRight = cameraTransform.right;
        Vector3 camForward = cameraTransform.forward;
        camRight.y = 0f; camRight.Normalize();
        camForward.y = 0f; camForward.Normalize();

        Vector3 worldDelta = -(frameDelta.x * camRight + frameDelta.y * camForward) * sensitivity;

        float deltaX = worldDelta.x;
        float deltaZ = worldDelta.z;

        _targetXZPosition.x += deltaX;
        _targetXZPosition.z += deltaZ;

        var (minX, maxX, minZ, maxZ) = GetCameraBounds(cfg);
        _targetXZPosition.x = Mathf.Clamp(_targetXZPosition.x, minX, maxX);
        _targetXZPosition.z = Mathf.Clamp(_targetXZPosition.z, minZ, maxZ);

        Vector3 newPos = cameraTransform.position;
        newPos.x = Mathf.Clamp(newPos.x + deltaX, minX, maxX);
        newPos.z = Mathf.Clamp(newPos.z + deltaZ, minZ, maxZ);

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