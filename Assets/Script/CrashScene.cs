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
    [HideInInspector] public GameObject smokeVFXPrefab;
    [HideInInspector] public GameObject barrierFencePrefab;

    [Header("Timing")]
    [Tooltip("Seconds the crash scene stays fully visible before fading.")]
    [Min(1f)] public float disappearDuration = 8f;

    [Tooltip("Seconds for the fade-out after disappearDuration expires.")]
    [Min(0.5f)] public float fadeDuration = 2f;

    [Header("Barrier Padding")]
    [Tooltip("Extra clearance around the car rectangle (world units).")]
    [Min(0f)] public float barrierPadding = 0.4f;

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

        // ── Step C: Fence counts ──────────────────────────────────
        // Wider car's width → front/back wall fence count.
        float maxWidth = Mathf.Max(widthA, widthB);
        int frontBackCount = Mathf.Max(1, Mathf.FloorToInt(maxWidth / fenceLength));

        // Combined length → left/right wall fence count.
        float totalLength = lengthA + lengthB;
        int sideCount = Mathf.Max(1, Mathf.FloorToInt(totalLength / fenceLength));

        // ── Step D: Road-aligned axes ─────────────────────────────
        // Forward = from rear car (A) toward front car (B).
        Vector3 forward = (carB.transform.position - carA.transform.position).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = carA.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        // Rectangle centre is the midpoint between both cars.
        Vector3 center = (carA.transform.position + carB.transform.position) * 0.5f;

        // ── Step E: Rectangle half-extents (with padding) ─────────
        float halfLength = (totalLength * 0.5f) + barrierPadding;
        float halfWidth = (maxWidth * 0.5f) + barrierPadding;

        float groundY = Mathf.Min(
            GetWorldBounds(carA.gameObject).min.y,
            GetWorldBounds(carB.gameObject).min.y);
        center.y = groundY;

        // ── Step F: Place fences ──────────────────────────────────

        // Left & Right walls (run along the length direction).
        // Fences face perpendicular to the wall (face outward = ±right).
        for (int i = 0; i < sideCount; i++)
        {
            float t = (sideCount == 1) ? 0f
                : Mathf.Lerp(-halfLength, halfLength, (float)i / (sideCount - 1));

            Vector3 posLeft = center + forward * t - right * halfWidth;
            Vector3 posRight = center + forward * t + right * halfWidth;

            // Fences on left/right walls are oriented along the forward direction.
            Quaternion wallRot = Quaternion.LookRotation(forward, Vector3.up);
            PlaceFence(posLeft, wallRot);
            PlaceFence(posRight, wallRot);
        }

        // Front & Back walls (run along the width direction).
        for (int i = 0; i < frontBackCount; i++)
        {
            float t = (frontBackCount == 1) ? 0f
                : Mathf.Lerp(-halfWidth, halfWidth, (float)i / (frontBackCount - 1));

            Vector3 posFront = center + forward * halfLength + right * t;
            Vector3 posBack = center - forward * halfLength + right * t;

            // Fences on front/back walls are oriented along the right direction.
            Quaternion endRot = Quaternion.LookRotation(right, Vector3.up);
            PlaceFence(posFront, endRot);
            PlaceFence(posBack, endRot);
        }
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

        // Cleanup.
        if (segment != null) segment.SetBlocked(false);
        if (carA != null) carA.ForceDespawn();
        if (carB != null) carB.ForceDespawn();

        Destroy(gameObject);
    }

    private IEnumerator FadeOutAll()
    {
        // Snapshot original colors.
        var originals = new List<(Renderer r, Color c)>();
        foreach (var r in _allRenderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
                originals.Add((r, mat.color));
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);

            int idx = 0;
            foreach (var r in _allRenderers)
            {
                if (r == null) continue;
                foreach (var mat in r.materials)
                {
                    if (idx < originals.Count)
                    {
                        Color c = originals[idx].c;
                        c.a = alpha;
                        mat.color = c;
                        SetMaterialTransparent(mat);
                    }
                    idx++;
                }
            }
            yield return null;
        }
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