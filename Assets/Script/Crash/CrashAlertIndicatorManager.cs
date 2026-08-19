using UnityEngine;

// CrashAlertIndicatorManager is the single owner of "what a crash looks like": it holds the
// crash marker prefab, the world-space X/height/depth offset used to position it relative to a
// wreck, and the shared bubble + exclamation appearance (sprite, colour, size) used by the
// in-world CrashAlertMarker.
//
// CHANGES:
//   - Added crashMarkerXOffset so the marker's sideways (X-axis) position can be tuned in the
//     Inspector, alongside the existing height (Y) and depth (Z) offsets.
//   - REMOVED the off-screen edge indicator feature entirely (CrashAlertEdgeIndicator support:
//     targetCamera, overlayCanvasRect, edgeIndicatorPrefab, HUD safe-area fields, marker
//     tracking, LateUpdate, and the UpdateEdgeIndicator/RemoveEdgeIndicator methods). This
//     manager now only spawns the in-world crash marker — nothing tracks whether it's on/off
//     screen anymore. CrashAlertMarker no longer needs to register with this manager either (see
//     its own CHANGES note) since there's nothing left here to register with.
//   - CrashAlertEdgeIndicator.cs and its prefab are unused now but left untouched in the project
//     in case you want to revisit them later — nothing in this script references them anymore.
public class CrashAlertIndicatorManager : MonoBehaviour
{
    public static CrashAlertIndicatorManager Instance { get; private set; }

    [Header("Crash Marker")]
    [Tooltip("Prefab with a CrashAlertMarker component, spawned at the crash position so the player can locate accidents on the map. Shared by every car.")]
    public GameObject crashAlertMarkerPrefab;

    [Tooltip("World-units to shift the crash marker sideways on the X axis. Applies to every car.")]
    public float crashMarkerXOffset = 0f;

    [Tooltip("World-units to raise the crash marker above the car's pivot so it renders above the wreck instead of inside/below it. Applies to every car.")]
    public float crashMarkerHeightOffset = 2f;

    [Tooltip("World-units to push the crash marker forward on the Z axis, so it doesn't render flush with/behind the wreck. Applies to every car.")]
    public float crashMarkerDepthOffset = 0f;

    [Header("Icon Appearance — Exclamation")]
    public Sprite exclamationIcon;
    public Color exclamationColor = Color.white;
    [Tooltip("Uniform scale multiplier applied to the exclamation sprite.")]
    public float exclamationSize = 1f;

    [Header("Icon Appearance — Bubble")]
    public Sprite bubbleIcon;
    public Color bubbleColor = Color.white;
    [Tooltip("Uniform scale multiplier applied to the bubble sprite.")]
    public float bubbleSize = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Spawns a crash marker (bubble + exclamation icon) at originPosition + the configured
    /// X/height/depth offset, and applies the shared icon appearance to it. Called by
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
            + Vector3.right * crashMarkerXOffset
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
}