using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// CarCollisionHandler detects a car flipping over or colliding, and on either triggers the
// accident sequence: playing the accident sound, spawning smoke and a crash alert icon, halting
// the car, notifying the assigned StopScript, and fading the car out before destroying it.
//
// CHANGES:
//   - Two-car collisions used to spawn TWO CrashAlertMarker instances (one from each car's
//     OnCollisionEnter), which made the off-screen edge indicator show two bubbles for a single
//     crash. OnCollisionEnter now deterministically suppresses the marker on one of the two cars
//     so only one marker is spawned per crash. Flip-over accidents (single car) are unaffected.
//   - Crash alert spawning (prefab, world-position offset, appearance) moved out of this script
//     and into CrashAlertIndicatorManager, since that config previously had to be duplicated
//     identically on every car. This script now just finds the singleton
//     (CrashAlertIndicatorManager.Instance — there's only ever one in a level) and asks it to
//     spawn the alert at this car's position.
[RequireComponent(typeof(CarAIController))]
public class CarCollisionHandler : MonoBehaviour
{
    [Header("Runtime State (read-only)")]
    [Tooltip("True once a collision has been detected. Smoke spawns immediately.")]
    public bool colliderCollide = false;

    [Header("Accident Config")]
    public GameObject smokePrefab;
    public float fadeDelay = 3f;
    public float fadeDuration = 1.5f;
    public StopScript accidentStopper;

    [Header("Flip Detection")]
    [Tooltip("Z-axis rotation degrees beyond which the car counts as flipped.")]
    public float flipAngleThreshold = 30f;
    [Tooltip("How often (seconds) to re-check for a flip while the car is running.")]
    public float flipCheckInterval = 1f;

    private CarAIController _controller;
    private bool _accidentStarted = false;
    private GameObject _smokeInstance;

    private void Awake()
    {
        _controller = GetComponent<CarAIController>();
    }

    private void Start()
    {
        StartCoroutine(FlipDetectionLoop());
    }

    private IEnumerator FlipDetectionLoop()
    {
        while (!_accidentStarted)
        {
            yield return new WaitForSeconds(flipCheckInterval);
            if (_accidentStarted) yield break;

            float z = transform.eulerAngles.z;
            if (z > 180f) z -= 360f;

            if (Mathf.Abs(z) > flipAngleThreshold)
            {
                Debug.Log($"[CarCollisionHandler] Flip detected on {gameObject.name} (Z={z:F1}°) — triggering accident.");
                TriggerAccident();
            }
        }
    }

    public void Configure(GameObject smoke, float delay, float duration, StopScript stopper)
    {
        smokePrefab = smoke;
        fadeDelay = delay;
        fadeDuration = duration;
        accidentStopper = stopper;

        colliderCollide = false;
        _accidentStarted = false;

        if (_smokeInstance != null)
        {
            Destroy(_smokeInstance);
            _smokeInstance = null;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        bool suppressMarker = false;

        // If the thing we hit is also a car with its own CarCollisionHandler, only ONE of the
        // two cars should spawn the shared crash marker — otherwise a single two-car crash
        // spawns two markers at two slightly different positions, which the edge-indicator
        // system then shows as two separate off-screen bubbles.
        CarCollisionHandler other = collision.collider.GetComponentInParent<CarCollisionHandler>();
        if (other != null && other != this)
        {
            suppressMarker = GetInstanceID() > other.GetInstanceID();
        }

        TriggerAccident(suppressMarker);
    }

    private void TriggerAccident(bool suppressMarker = false)
    {
        if (_accidentStarted) return;
        _accidentStarted = true;
        colliderCollide = true;

        Debug.Log($"[CarCollisionHandler] Accident triggered on {gameObject.name}");

        LevelAudioManager.Instance?.PlayCarAccident();

        SpawnSmoke();

        if (!suppressMarker)
        {
            CrashAlertIndicatorManager manager = CrashAlertIndicatorManager.Instance;
            if (manager != null)
            {
                manager.SpawnCrashAlert(transform.position);
            }
            else
            {
                Debug.LogWarning($"[CarCollisionHandler] No CrashAlertIndicatorManager found in scene — no crash icon will spawn.");
            }
        }

        TriggerStopper();

        _controller.CheckPointSearch = false;
        _controller.SetSpeed(0);

        StartCoroutine(FadeOutSequence());
    }

    private void SpawnSmoke()
    {
        if (smokePrefab == null)
        {
            Debug.LogWarning("[CarCollisionHandler] No smokePrefab assigned – skipping smoke spawn.");
            return;
        }

        _smokeInstance = Instantiate(smokePrefab, transform.position, Quaternion.identity, transform);
        _smokeInstance.transform.localPosition = Vector3.zero;
        Debug.Log("[CarCollisionHandler] Smoke spawned.");
    }

    private IEnumerator FadeOutSequence()
    {
        yield return new WaitForSeconds(fadeDelay);

        Debug.Log("[CarCollisionHandler] Starting fade-out.");

        Vector3 originalScale = transform.localScale;
        float elapsed = 0f;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        Dictionary<Material, Color> originalColors = new Dictionary<Material, Color>();
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            foreach (Material mat in r.materials)
            {
                if (!originalColors.ContainsKey(mat))
                {
                    originalColors[mat] = mat.color;
                    SetMaterialFadeMode(mat);
                }
            }
        }

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float eased = 1f - Mathf.SmoothStep(0f, 1f, t);

            transform.localScale = originalScale * eased;

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;

                foreach (Material mat in r.materials)
                {
                    if (originalColors.TryGetValue(mat, out Color baseColor))
                    {
                        Color c = baseColor;
                        c.a = eased;
                        mat.color = c;
                    }
                }
            }

            yield return null;
        }

        Debug.Log($"[CarCollisionHandler] {gameObject.name} faded out – destroying.");

        if (_smokeInstance != null)
            Destroy(_smokeInstance);

        Destroy(gameObject);
    }

    private void TriggerStopper()
    {
        if (accidentStopper == null)
        {
            Debug.LogWarning("[CarCollisionHandler] No accidentStopper assigned – other cars won't stop.");
            return;
        }

        accidentStopper.stop = true;
        Debug.Log($"[CarCollisionHandler] StopScript '{accidentStopper.gameObject.name}' → stop = true");
    }

    private static void SetMaterialFadeMode(Material mat)
    {
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 2);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    private void OnDestroy()
    {
        if (_smokeInstance != null)
            Destroy(_smokeInstance);
    }
}