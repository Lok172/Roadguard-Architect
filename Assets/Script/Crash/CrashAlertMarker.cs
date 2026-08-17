using UnityEngine;

// CrashAlertMarker sits at the location of a car accident and displays the bubble + exclamation
// icon combo so the crash location is visible on the map. It registers with
// CrashAlertIndicatorManager while active, which drives the off-screen edge indicator, and
// removes itself after displayDuration by shrinking to zero scale (matching the same
// "shrink then disappear" despawn style used for the crashed car).
//
// CHANGES:
//   - Added bubbleRenderer + ApplyAppearance(), called once by CrashAlertIndicatorManager right
//     after this marker is instantiated. "Icon" now means bubble + exclamation together, so both
//     renderers get their sprite/colour/size from the manager instead of just the exclamation.
//   - Removed the old public exclamationIcon field — appearance is now fully owned by
//     CrashAlertIndicatorManager (single source of truth), not duplicated on the prefab.
public class CrashAlertMarker : MonoBehaviour
{
    [Header("Icon References")]
    [Tooltip("SpriteRenderer on a child object showing the bubble sprite behind the exclamation icon.")]
    public SpriteRenderer bubbleRenderer;
    [Tooltip("SpriteRenderer on a child object, showing the exclamation icon in the scene at the crash position.")]
    public SpriteRenderer exclamationRenderer;

    [Header("Lifetime")]
    [Tooltip("How long (seconds) this marker stays at full size before it starts shrinking.")]
    public float displayDuration = 1.5f;

    [Tooltip("How long (seconds) the shrink-to-zero animation takes before the marker is destroyed.")]
    public float shrinkDuration = 0.4f;

    private float _elapsed;
    private Vector3 _baseScale;
    private bool _shrinking;

    private void Start()
    {
        _baseScale = transform.localScale;

        CrashAlertIndicatorManager.Instance?.RegisterMarker(this);
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        if (Camera.main != null)
        {
            Quaternion camRot = Camera.main.transform.rotation;
            if (exclamationRenderer != null) exclamationRenderer.transform.rotation = camRot;
            if (bubbleRenderer != null) bubbleRenderer.transform.rotation = camRot;
        }

        if (!_shrinking && _elapsed >= displayDuration)
        {
            _shrinking = true;
            _elapsed = 0f;
        }

        if (_shrinking)
        {
            float t = shrinkDuration <= 0f ? 1f : Mathf.Clamp01(_elapsed / shrinkDuration);
            transform.localScale = Vector3.Lerp(_baseScale, Vector3.zero, t);

            if (t >= 1f)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// Applies the shared icon look (from CrashAlertIndicatorManager) to this marker's bubble
    /// and exclamation sprites. Called once, right after this marker is instantiated — before
    /// Start() runs, so both renderers are correct on the very first visible frame.
    /// </summary>
    public void ApplyAppearance(Sprite exclamationIcon, Color exclamationColor, float exclamationSize,
                                 Sprite bubbleIcon, Color bubbleColor, float bubbleSize)
    {
        if (exclamationRenderer != null)
        {
            if (exclamationIcon != null) exclamationRenderer.sprite = exclamationIcon;
            exclamationRenderer.color = exclamationColor;
            exclamationRenderer.transform.localScale = Vector3.one * exclamationSize;
        }

        if (bubbleRenderer != null)
        {
            if (bubbleIcon != null) bubbleRenderer.sprite = bubbleIcon;
            bubbleRenderer.color = bubbleColor;
            bubbleRenderer.transform.localScale = Vector3.one * bubbleSize;
        }
    }

    private void OnDestroy()
    {
        CrashAlertIndicatorManager.Instance?.UnregisterMarker(this);
    }
}