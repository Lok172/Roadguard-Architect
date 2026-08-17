using System.Collections.Generic;
using UnityEngine;

// CrashAlertIndicatorManager is the single owner of "what a crash looks like": it holds the
// crash marker prefab, the world-space height/depth offset used to sit it above a wreck, and the
// shared bubble + exclamation appearance (sprite, colour, size) used by both the in-world
// CrashAlertMarker and the screen-edge CrashAlertEdgeIndicator. It also tracks every active
// CrashAlertMarker and, for any marker currently outside the camera's view, displays a
// screen-edge bubble indicator pointing toward it, clamped so it never renders behind the top or
// bottom HUD panels.
//
// CHANGES:
//   - Crash alert spawning moved here from CarCollisionHandler. Call SpawnCrashAlert(position)
//     instead of instantiating the marker prefab directly — CarCollisionHandler just finds this
//     singleton (CrashAlertIndicatorManager.Instance) and asks it to spawn the alert.
//   - crashMarkerHeightOffset / crashMarkerDepthOffset moved here from CarCollisionHandler so
//     every car in the level uses the same offset instead of each car having its own copy.
//   - Added an Icon Appearance section: exclamation + bubble sprite/colour/size, settable
//     independently, applied to both the world marker and the edge indicator on spawn.
//   - "Icon" now means the bubble + exclamation combo, not the exclamation alone — the world
//     marker spawns both, and ApplyAppearance() pushes the shared look onto whichever one was
//     just instantiated (marker or edge indicator).
public class CrashAlertIndicatorManager : MonoBehaviour
{
    public static CrashAlertIndicatorManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Camera used to determine whether a crash marker is currently visible. Defaults to Camera.main if left empty.")]
    public Camera targetCamera;

    [Tooltip("RectTransform of the full-screen overlay canvas the edge indicators are parented to. Must be centre-anchored (Anchor Min/Max = 0.5, 0.5).")]
    public RectTransform overlayCanvasRect;

    [Tooltip("Prefab containing a CrashAlertEdgeIndicator component, instantiated for each off-screen marker.")]
    public GameObject edgeIndicatorPrefab;

    [Header("Crash Marker")]
    [Tooltip("Prefab with a CrashAlertMarker component, spawned at the crash position so the player can locate accidents on the map. Shared by every car.")]
    public GameObject crashAlertMarkerPrefab;

    [Tooltip("World-units to raise the crash marker above the car's pivot so it renders above the wreck instead of inside/below it. Applies to every car.")]
    public float crashMarkerHeightOffset = 2f;

    [Tooltip("World-units to push the crash marker forward on the Z axis, so it doesn't render flush with/behind the wreck. Applies to every car.")]
    public float crashMarkerDepthOffset = 0f;

    [Header("Icon Appearance — Exclamation")]
    public Sprite exclamationIcon;
    public Color exclamationColor = Color.white;
    [Tooltip("Uniform scale multiplier applied to the exclamation sprite, both in-world and on the edge indicator.")]
    public float exclamationSize = 1f;

    [Header("Icon Appearance — Bubble")]
    public Sprite bubbleIcon;
    public Color bubbleColor = Color.white;
    [Tooltip("Uniform scale multiplier applied to the bubble sprite, both in-world and on the edge indicator.")]
    public float bubbleSize = 1f;

    [Header("HUD Safe Area")]
    [Tooltip("Height in pixels of the Top HUD panel. Edge indicators will not render above this line.")]
    public float topHUDHeight = 166f;

    [Tooltip("Height in pixels of the Bottom HUD panel. Edge indicators will not render below this line.")]
    public float bottomHUDHeight = 187f;

    [Tooltip("Extra inset from the screen edges so the indicator bubble is never clipped.")]
    public float edgeMargin = 40f;

    private readonly List<CrashAlertMarker> _activeMarkers = new List<CrashAlertMarker>();
    private readonly Dictionary<CrashAlertMarker, RectTransform> _edgeIndicators = new Dictionary<CrashAlertMarker, RectTransform>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Spawns a crash marker (bubble + exclamation icon) at originPosition + the configured
    /// height/depth offset, and applies the shared icon appearance to it. Called by
    /// CarCollisionHandler instead of it instantiating the marker prefab itself.
    /// </summary>
    public CrashAlertMarker SpawnCrashAlert(Vector3 originPosition)
    {
        if (crashAlertMarkerPrefab == null)
        {
            Debug.LogWarning("[CrashAlertIndicatorManager] crashAlertMarkerPrefab not assigned — no crash icon will spawn.");
            return null;
        }

        Vector3 markerPos = originPosition
            + Vector3.up * crashMarkerHeightOffset
            + Vector3.forward * crashMarkerDepthOffset;

        GameObject instance = Instantiate(crashAlertMarkerPrefab, markerPos, Quaternion.identity);
        CrashAlertMarker marker = instance.GetComponent<CrashAlertMarker>();

        if (marker != null)
        {
            marker.ApplyAppearance(exclamationIcon, exclamationColor, exclamationSize, bubbleIcon, bubbleColor, bubbleSize);
        }
        else
        {
            Debug.LogWarning("[CrashAlertIndicatorManager] crashAlertMarkerPrefab has no CrashAlertMarker component.");
        }

        return marker;
    }

    public void RegisterMarker(CrashAlertMarker marker)
    {
        if (!_activeMarkers.Contains(marker))
            _activeMarkers.Add(marker);
    }

    public void UnregisterMarker(CrashAlertMarker marker)
    {
        _activeMarkers.Remove(marker);
        RemoveEdgeIndicator(marker);
    }

    private void LateUpdate()
    {
        // Matches BillboardSprite.cs's pattern elsewhere in this project:
        // keep retrying Camera.main each frame instead of only checking once
        // in Awake(), since the main camera may not be tagged/ready yet at
        // that point (multiple per-scene cameras are managed by CameraManager).
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || overlayCanvasRect == null) return;

        for (int i = _activeMarkers.Count - 1; i >= 0; i--)
        {
            CrashAlertMarker marker = _activeMarkers[i];
            if (marker == null)
            {
                _activeMarkers.RemoveAt(i);
                continue;
            }

            Vector3 viewportPoint = targetCamera.WorldToViewportPoint(marker.transform.position);
            bool onScreen = viewportPoint.z > 0f &&
                             viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                             viewportPoint.y >= 0f && viewportPoint.y <= 1f;

            if (onScreen)
                RemoveEdgeIndicator(marker);
            else
                UpdateEdgeIndicator(marker, viewportPoint);
        }
    }

    private void RemoveEdgeIndicator(CrashAlertMarker marker)
    {
        if (_edgeIndicators.TryGetValue(marker, out RectTransform indicator))
        {
            if (indicator != null)
                Destroy(indicator.gameObject);
            _edgeIndicators.Remove(marker);
        }
    }

    private void UpdateEdgeIndicator(CrashAlertMarker marker, Vector3 viewportPoint)
    {
        if (!_edgeIndicators.TryGetValue(marker, out RectTransform indicator) || indicator == null)
        {
            if (edgeIndicatorPrefab == null) return;

            GameObject instance = Instantiate(edgeIndicatorPrefab, overlayCanvasRect);
            indicator = instance.GetComponent<RectTransform>();
            _edgeIndicators[marker] = indicator;

            CrashAlertEdgeIndicator edgeScript = instance.GetComponent<CrashAlertEdgeIndicator>();
            edgeScript?.ApplyAppearance(exclamationIcon, exclamationColor, exclamationSize, bubbleIcon, bubbleColor, bubbleSize);
        }

        if (viewportPoint.z < 0f)
        {
            viewportPoint.x = 1f - viewportPoint.x;
            viewportPoint.y = 1f - viewportPoint.y;
        }

        Rect canvasRect = overlayCanvasRect.rect;

        Vector2 target = new Vector2(
            (viewportPoint.x - 0.5f) * canvasRect.width,
            (viewportPoint.y - 0.5f) * canvasRect.height);

        Vector2 direction = target;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.up;
        direction.Normalize();

        float halfWidth = canvasRect.width * 0.5f - edgeMargin;
        float topLimit = canvasRect.height * 0.5f - topHUDHeight - edgeMargin;
        float bottomLimit = -(canvasRect.height * 0.5f - bottomHUDHeight - edgeMargin);

        float scaleX = halfWidth / Mathf.Max(Mathf.Abs(direction.x), 0.0001f);
        float verticalLimit = direction.y >= 0f ? topLimit : -bottomLimit;
        float scaleY = verticalLimit / Mathf.Max(Mathf.Abs(direction.y), 0.0001f);
        float scale = Mathf.Min(scaleX, scaleY);

        Vector2 clampedPos = direction * scale;
        clampedPos.y = Mathf.Clamp(clampedPos.y, bottomLimit, topLimit);
        clampedPos.x = Mathf.Clamp(clampedPos.x, -halfWidth, halfWidth);

        indicator.anchoredPosition = clampedPos;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        indicator.GetComponent<CrashAlertEdgeIndicator>()?.SetBubbleRotation(angle);
    }
}