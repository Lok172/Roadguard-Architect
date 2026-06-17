using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  CRASH SCENE  (v2 — fixed fence placement + smoke height)
//
//  Manages the visual aftermath of a rear-end collision between
//  two CarAgents (carA = rear, carB = front) on a RoadSegment.
//
//  Spawns:
//    • Smoke VFX at the impact point ABOVE the cars.
//    • Barrier Fence prefabs arranged in a rectangle around both
//      stopped cars, aligned to the road direction (not AABB).
//
//  FENCE PLACEMENT ALGORITHM (v2):
//    1. Get renderer bounds of each car on XZ plane.
//    2. For each car: longer XZ edge = length, shorter = width.
//    3. Compare widths; the wider value / fence length = count
//       for the front & back walls (clamped floor, min 1).
//    4. Sum both car lengths / fence length = count for the
//       left & right walls (clamped floor, min 1).
//    5. All fences are oriented relative to the road forward
//       direction (carA → carB), NOT axis-aligned.
//
//  After the inspector-tunable disappear duration, all spawned
//  objects (smoke + fences) fade out over fadeDuration and are
//  destroyed. The segment is unblocked and both cars are despawned.
//
//  Created at runtime by CarManager.SpawnCrashScene().
// ─────────────────────────────────────────────────────────────────

public class CrashScene : MonoBehaviour
{
    // ── Set by CarManager before Build() ──────
    [HideInInspector] public CarAgent carA;            // rear car (the one that hit)
    [HideInInspector] public CarAgent carB;            // front car (the one that got hit)
    [HideInInspector] public RoadSegment segment;
    [HideInInspector] public List<RoadSegment> blockedSegments;  // all segments this wreck blocks
    [HideInInspector] public GameObject smokeVFXPrefab;
    [HideInInspector] public GameObject barrierFencePrefab;

    [Header("Timing")]
    [Tooltip("Seconds the crash scene stays fully visible before fading.")]
    [Min(1f)] public float disappearDuration = 8f;

    [Tooltip("Seconds for the fade-out after disappearDuration expires.")]
    [Min(0.5f)] public float fadeDuration = 2f;

    [Tooltip("Seconds before the fade fully completes that the smoke is stopped " +
             "and its particles cleared. Prevents the smoke lingering after the " +
             "cars have shrunk away. 0 = clear exactly when the cars vanish.")]
    [Min(0f)] public float smokeFadeLead = 1f;

    [Header("Barrier Padding")]
    [Tooltip("Clearance between the barrier fence and the cars (world units).")]
    [Min(0f)] public float barrierPaddingWithCar = 0.4f;

    [Tooltip("Extra spacing inserted lengthwise between the two crashed cars " +
             "so the fence does not hug them when they sit bumper-to-bumper " +
             "(world units).")]
    [Min(0f)] public float barrierPaddingInBetween = 0.4f;

    [Header("Smoke Height")]
    [Tooltip("Extra Y offset for smoke VFX above the car top.")]
    [Min(0f)] public float smokeHeightOffset = 0.5f;

    // ── Runtime ───────────────────────────────
    private GameObject _smokeInstance;
    private readonly List<GameObject> _fences = new List<GameObject>();
    private readonly List<Renderer> _allRenderers = new List<Renderer>();

    // ─────────────────────────────────────────
    //  BUILD SCENE
    // ─────────────────────────────────────────

    /// <summary>
    /// Call once after all public fields are assigned.
    /// Spawns smoke + barrier fences and starts the lifetime coroutine.
    /// </summary>
    public void Build()
    {
        // ── 1. Smoke VFX at impact point ABOVE the cars ──────────
        if (smokeVFXPrefab != null && carA != null)
        {
            Vector3 impactPos = GetFrontBumperPosition(carA);
            // Raise smoke above the tallest car so it's visible from above.
            float topY = GetTopY(carA, carB);
            impactPos.y = topY + smokeHeightOffset;
            _smokeInstance = Instantiate(smokeVFXPrefab, impactPos, Quaternion.identity, transform);
        }

        // ── 2. Barrier fences ─────────────────────────────────────
        if (barrierFencePrefab != null && carA != null && carB != null)
            SpawnBarrierRectangle();

        // Collect all renderers for fade-out later.
        _allRenderers.AddRange(GetComponentsInChildren<Renderer>());

        // ── 3. Start lifetime ─────────────────────────────────────
        StartCoroutine(LifetimeRoutine());
    }

    // ─────────────────────────────────────────
    //  BARRIER RECTANGLE  (v2 — road-aligned)
    // ─────────────────────────────────────────

    private void SpawnBarrierRectangle()
    {
        // ── Step A: Get per-car dimensions on XZ plane ────────────
        GetCarLengthAndWidth(carA, out float lengthA, out float widthA);
        GetCarLengthAndWidth(carB, out float lengthB, out float widthB);

        // ── Step B: Fence prefab length (along its local X) ───────
        Bounds fenceBounds = GetPrefabBounds(barrierFencePrefab);
        float fenceLength = Mathf.Max(0.1f, fenceBounds.size.x);

        // ── Step C: Road-aligned axes ─────────────────────────────
        // Forward = from rear car (A) toward front car (B).
        Vector3 forward = (carB.transform.position - carA.transform.position).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = carA.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // Rectangle centre is the midpoint between both cars, dropped to ground.
        Vector3 center = (carA.transform.position + carB.transform.position) * 0.5f;
        center.y = Mathf.Min(
            GetWorldBounds(carA.gameObject).min.y,
            GetWorldBounds(carB.gameObject).min.y);

        // ── Step D: Rectangle half-extents (with padding) ─────────
        // halfLength runs along the road (the axis separating the two cars):
        //   • barrierPaddingWithCar  → clearance at the outer ends.
        //   • barrierPaddingInBetween → extra lengthwise room so small cars
        //     sitting bumper-to-bumper don't look glued together inside a
        //     tight fence (see picture 1). Tune down for long vehicles whose
        //     fence already shows a gap (picture 2).
        float halfLength = (lengthA + lengthB) * 0.5f
                           + barrierPaddingWithCar
                           + barrierPaddingInBetween * 0.5f;
        float halfWidth = Mathf.Max(widthA, widthB) * 0.5f + barrierPaddingWithCar;

        // ── Step E: Fence orientation ─────────────────────────────
        // The fence's long axis is its local +X. Quaternion.LookRotation(dir)
        // aligns local +Z to dir, which puts local +X = Cross(up, dir) — i.e.
        // PERPENDICULAR to dir. So to make a fence lie ALONG `forward` we must
        // LookRotation(right); to lie ALONG `right` we LookRotation(forward).
        Quaternion alongForward = Quaternion.LookRotation(right, Vector3.up);
        Quaternion alongRight = Quaternion.LookRotation(forward, Vector3.up);

        // ── Step F: Build the four walls ──────────────────────────
        // Left & right walls run parallel to `forward`.
        BuildWall(center - right * halfWidth, forward, halfLength * 2f, fenceLength, alongForward);
        BuildWall(center + right * halfWidth, forward, halfLength * 2f, fenceLength, alongForward);

        // Front & back walls run parallel to `right`.
        BuildWall(center + forward * halfLength, right, halfWidth * 2f, fenceLength, alongRight);
        BuildWall(center - forward * halfLength, right, halfWidth * 2f, fenceLength, alongRight);
    }

    /// <summary>
    /// Tiles fence prefabs edge-to-edge along <paramref name="axis"/>,
    /// centred on <paramref name="wallCenter"/>, forming one continuous wall.
    /// </summary>
    private void BuildWall(Vector3 wallCenter, Vector3 axis, float wallLength,
                           float fenceLength, Quaternion rot)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(wallLength / fenceLength));
        float spacing = wallLength / count;
        Vector3 startEdge = wallCenter - axis * (wallLength * 0.5f);
        for (int i = 0; i < count; i++)
            PlaceFence(startEdge + axis * (spacing * (i + 0.5f)), rot);
    }

    /// <summary>
    /// Computes the length (longer XZ edge) and width (shorter XZ edge)
    /// of a car from its world-space renderer bounds.
    /// </summary>
    private static void GetCarLengthAndWidth(CarAgent car, out float length, out float width)
    {
        Bounds b = GetWorldBounds(car.gameObject);
        float xSize = b.size.x;
        float zSize = b.size.z;

        if (xSize >= zSize)
        {
            length = xSize;
            width = zSize;
        }
        else
        {
            length = zSize;
            width = xSize;
        }
    }

    private void PlaceFence(Vector3 position, Quaternion rotation)
    {
        var go = Instantiate(barrierFencePrefab, position, rotation, transform);
        _fences.Add(go);
    }

    // ─────────────────────────────────────────
    //  LIFETIME + FADE
    // ─────────────────────────────────────────

    private IEnumerator LifetimeRoutine()
    {
        // Wait for the full visible duration (pause-aware via IsDragging).
        float elapsed = 0f;
        while (elapsed < disappearDuration)
        {
            if (!CarManager.IsDragging) elapsed += Time.deltaTime;
            yield return null;
        }

        // Fade out all renderers.
        yield return StartCoroutine(FadeOutAll());

        // Cleanup — unblock every segment the wreck was occupying.
        if (blockedSegments != null && blockedSegments.Count > 0)
        {
            foreach (var s in blockedSegments)
                if (s != null) s.SetBlocked(false);
        }
        else if (segment != null)
        {
            segment.SetBlocked(false);
        }

        if (carA != null) carA.ForceDespawn();
        if (carB != null) carB.ForceDespawn();

        Destroy(gameObject);
    }

    /// <summary>
    /// Shader-independent disappear: shrink the whole crash scene (smoke +
    /// fences + both cars) down to nothing using an ease-out cubic curve.
    /// Avoids transparency artifacts and road clipping.
    /// </summary>
    private IEnumerator FadeOutAll()
    {
        // Pull both cars under this scene so they shrink along with it.
        if (carA != null) carA.transform.SetParent(transform, worldPositionStays: true);
        if (carB != null) carB.transform.SetParent(transform, worldPositionStays: true);

        // Smoke: stop emitting straight away so no new puffs spawn during the
        // fade, and remember its particle systems so we can clear lingering
        // particles before the cars finish shrinking (otherwise World-space
        // particles outlive the wreck by ~1s — see req: smoke timing).
        ParticleSystem[] smokeSystems = _smokeInstance != null
            ? _smokeInstance.GetComponentsInChildren<ParticleSystem>(true)
            : new ParticleSystem[0];
        foreach (var ps in smokeSystems)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // Clear the smoke this many seconds before the cars fully vanish.
        float smokeClearAt = Mathf.Max(0f, fadeDuration - smokeFadeLead);
        bool smokeCleared = false;

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            if (!CarManager.IsDragging) elapsed += Time.deltaTime;

            if (!smokeCleared && elapsed >= smokeClearAt)
            {
                ClearSmoke(smokeSystems);
                smokeCleared = true;
            }

            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);   // ease-out cubic
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            yield return null;
        }
        transform.localScale = Vector3.zero;

        // Safety: make sure the smoke is gone even if the loop exited early.
        if (!smokeCleared) ClearSmoke(smokeSystems);

        // Un-parent the cars before they get pooled (their scale is reset on reuse).
        if (carA != null) carA.transform.SetParent(null, worldPositionStays: true);
        if (carB != null) carB.transform.SetParent(null, worldPositionStays: true);
    }

    /// <summary>
    /// Stops and clears all particles on the smoke instance, and disables the
    /// object so any non-particle smoke also disappears immediately.
    /// </summary>
    private void ClearSmoke(ParticleSystem[] smokeSystems)
    {
        foreach (var ps in smokeSystems)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_smokeInstance != null) _smokeInstance.SetActive(false);
    }

    private static void SetMaterialTransparent(Material mat)
    {
        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
    }

    // ─────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────

    private static Vector3 GetFrontBumperPosition(CarAgent car)
    {
        Renderer r = car.GetComponentInChildren<Renderer>();
        if (r != null)
            return car.transform.position + car.transform.forward * r.bounds.extents.z;
        return car.transform.position + car.transform.forward * 1f;
    }

    /// <summary>
    /// Returns the highest Y point among the two cars' renderer bounds.
    /// </summary>
    private static float GetTopY(CarAgent carA, CarAgent carB)
    {
        float topA = GetWorldBounds(carA.gameObject).max.y;
        float topB = GetWorldBounds(carB.gameObject).max.y;
        return Mathf.Max(topA, topB);
    }

    private static Bounds GetWorldBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static Bounds GetPrefabBounds(GameObject prefab)
    {
        Renderer r = prefab.GetComponentInChildren<Renderer>();
        if (r != null) return r.bounds;
        Collider c = prefab.GetComponentInChildren<Collider>();
        if (c != null) return c.bounds;
        return new Bounds(Vector3.zero, Vector3.one);
    }
}