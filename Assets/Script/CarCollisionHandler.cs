using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to a car (manually or via AreaTargetManager.PickTargetCar()).
/// On any physics collision this component:
///   3. Spawns a smoke prefab at the car centre.
///   4. After fadeDelay seconds, shrinks + fades the car to nothing.
///   5. Sets the linked StopScript.stop = true (box-corridor stopper).
///
/// The component cleans itself up if the car is already being destroyed.
/// </summary>
[RequireComponent(typeof(CarAIController))]
public class CarCollisionHandler : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    [Header("Runtime State (read-only)")]
    [Tooltip("True once a collision has been detected. Smoke spawns immediately.")]
    public bool colliderCollide = false;

    // ─── Config (set by AreaTargetManager or Inspector) ───────────────────────

    [Header("Accident Config")]
    public GameObject smokePrefab;
    public float fadeDelay = 3f;
    public float fadeDuration = 1.5f;
    public StopScript accidentStopper;

    // ─── Private ──────────────────────────────────────────────────────────────

    private CarAIController _controller;
    private bool _accidentStarted = false;
    private GameObject _smokeInstance;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<CarAIController>();
    }

    /// <summary>Called by AreaTargetManager to (re-)configure the handler.</summary>
    public void Configure(GameObject smoke, float delay, float duration, StopScript stopper)
    {
        smokePrefab      = smoke;
        fadeDelay        = delay;
        fadeDuration     = duration;
        accidentStopper  = stopper;

        // Reset state so the handler is ready for a fresh accident
        colliderCollide  = false;
        _accidentStarted = false;

        if (_smokeInstance != null)
        {
            Destroy(_smokeInstance);
            _smokeInstance = null;
        }
    }

    // ─── Collision detection ──────────────────────────────────────────────────

    /// <summary>
    /// OnCollisionEnter works with non-trigger colliders (the car's body).
    /// If you prefer trigger-based detection, swap for OnTriggerEnter.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // Ignore collisions with other CarAIControllers only if you want car-vs-world only.
        // Leave as-is to trigger on ANY collision (walls, barriers, other cars).
        TriggerAccident();
    }

    // ─── Accident sequence ────────────────────────────────────────────────────

    private void TriggerAccident()
    {
        if (_accidentStarted) return;   // Only trigger once
        _accidentStarted = true;
        colliderCollide  = true;

        Debug.Log($"[CarCollisionHandler] Accident triggered on {gameObject.name}");

        // 3 – Spawn smoke at car centre
        SpawnSmoke();

        // 5 – Tell the stopper immediately
        TriggerStopper();

        // 4 – Stop the car, then begin fade after delay
        _controller.CheckPointSearch = false;
        _controller.SetSpeed(0);

        StartCoroutine(FadeOutSequence());
    }

    // ─── Step 3: Smoke ────────────────────────────────────────────────────────

    private void SpawnSmoke()
    {
        if (smokePrefab == null)
        {
            Debug.LogWarning("[CarCollisionHandler] No smokePrefab assigned – skipping smoke spawn.");
            return;
        }

        // Spawn parented to car so it follows during the shrink phase
        _smokeInstance = Instantiate(smokePrefab, transform.position, Quaternion.identity, transform);
        _smokeInstance.transform.localPosition = Vector3.zero;
        Debug.Log("[CarCollisionHandler] Smoke spawned.");
    }

    // ─── Step 4: Shrink / fade out ────────────────────────────────────────────

    private IEnumerator FadeOutSequence()
    {
        // Wait before starting to shrink
        yield return new WaitForSeconds(fadeDelay);

        Debug.Log("[CarCollisionHandler] Starting fade-out.");

        Vector3 originalScale = transform.localScale;
        float elapsed = 0f;

        // Collect all renderers so we can fade their alpha
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        // Cache original materials and enable fade mode
        Dictionary<Material, Color> originalColors = new Dictionary<Material, Color>();
        foreach (Renderer r in renderers)
        {
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

            // Shrink scale
            transform.localScale = originalScale * eased;

            // Fade alpha on all materials
            foreach (Renderer r in renderers)
            {
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

        // Destroy smoke separately to avoid parenting issue
        if (_smokeInstance != null)
            Destroy(_smokeInstance);

        Destroy(gameObject);
    }

    // ─── Step 5: Stopper ──────────────────────────────────────────────────────

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

    // ─── Material helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Switches a Standard or URP/Lit material to Transparent/Fade rendering mode
    /// so that setting color.a actually fades the surface.
    /// </summary>
    private static void SetMaterialFadeMode(Material mat)
    {
        // URP Lit
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1);   // 1 = Transparent
            mat.SetFloat("_Blend", 0);     // 0 = Alpha
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        // Built-in Standard
        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 2);      // 2 = Fade
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        // If car is destroyed externally while smoke is still alive, clean it up
        if (_smokeInstance != null)
            Destroy(_smokeInstance);
    }
}
