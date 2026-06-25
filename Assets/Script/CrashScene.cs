using System.Collections.Generic;
using UnityEngine;

using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class CrashScene : MonoBehaviour
{
    // ── Set by CarManager before Build() 
    // CarAgent stubs kept for CarManager compile-compatibility.
    [HideInInspector] public CarAgent carA;               // rear  car
    [HideInInspector] public CarAgent carB;               // front car
    // Road-graph stubs — no longer used at runtime.
    [HideInInspector] public RoadSegment segment;
    [HideInInspector] public List<RoadSegment> blockedSegments;
    [HideInInspector] public List<RoadIntersection> blockedTowards;

    [HideInInspector] public GameObject smokeVFXPrefab;
    [HideInInspector] public GameObject barrierFencePrefab;

    [Header("Timing")]
    [Tooltip("Seconds the crash scene stays fully visible before fading.")]
    [Min(1f)] public float disappearDuration = 8f;

    [Tooltip("Seconds for the fade-out after disappearDuration expires.")]
    [Min(0.5f)] public float fadeDuration = 2f;

    [Tooltip("Seconds before the fade fully completes that the smoke is stopped " +
             "and its particles cleared.")]
    [Min(0f)] public float smokeFadeLead = 1f;

    [Header("Barrier Padding")]
    [Tooltip("Clearance between the barrier fence and the cars (world units).")]
    [Min(0f)] public float barrierPaddingWithCar = 0.4f;

    [Tooltip("Extra lengthwise spacing between the two crashed cars.")]
    [Min(0f)] public float barrierPaddingInBetween = 0.4f;

    [Header("Smoke Height")]
    [Tooltip("Extra Y offset for smoke VFX above the car top.")]
    [Min(0f)] public float smokeHeightOffset = 0.5f;

    // ── Runtime ───────────────────────────────────────────────────
    private CarAIController _controllerA;   // derived from carA.gameObject
    private CarAIController _controllerB;   // derived from carB.gameObject
    private GameObject _smokeInstance;
    private readonly List<GameObject> _fences = new List<GameObject>();

    // ── Pause tracking ────────────────────────────────────────────
    private PauseMenuController _pauseController;
    private bool _wasPaused = false;

    // ─────────────────────────────────────────────────────────────
    //  BUILD
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by CarManager after all public fields are assigned.
    /// Resolves CarAIController from the CarAgent GameObjects,
    /// spawns VFX + fences, and starts the lifetime coroutine.
    /// </summary>
    public void Build()
    {
        // Resolve CarAIController — same GameObject as CarAgent.
        // Works across additive scenes because we already hold the reference.
        _controllerA = carA != null ? carA.GetComponent<CarAIController>() : null;
        _controllerB = carB != null ? carB.GetComponent<CarAIController>() : null;

        // Cache PauseMenuController (searches all loaded scenes).
        _pauseController = Object.FindObjectOfType<PauseMenuController>();

        // Stop both cars immediately.
        StopCar(_controllerA);
        StopCar(_controllerB);

        // ── Smoke VFX ─────────────────────────────────────────────
        if (smokeVFXPrefab != null && carA != null)
        {
            Vector3 impactPos = GetFrontBumperPosition(carA);
            float topY = GetTopY(carA, carB);
            impactPos.y = topY + smokeHeightOffset;
            _smokeInstance = Instantiate(smokeVFXPrefab, impactPos, Quaternion.identity, transform);
        }

        // ── Barrier fences ────────────────────────────────────────
        if (barrierFencePrefab != null && carA != null && carB != null)
            SpawnBarrierRectangle();

        StartCoroutine(LifetimeRoutine());
    }

    // ─────────────────────────────────────────────────────────────
    //  CAR PAUSE / RESUME  (Req 1)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Freezes a car: disables AI and holds brakes.</summary>
    private static void PauseCar(CarAIController car)
    {
        if (car == null) return;
        car.isCarControlledByAI = false;
        car.CheckPointSearch = false;
        car.Break(car.breaking);
    }

    /// <summary>
    /// After unpause, crashed cars stay stopped (wrecked — not driving again).
    /// Re-apply brakes in case physics nudged them during the pause.
    /// </summary>
    private static void ResumeCrashedCar(CarAIController car)
    {
        if (car == null) return;
        car.Accelerate(0f);
        car.Break(car.breaking);
    }

    /// <summary>Stops a car at crash time: kill AI and hold brakes.</summary>
    private static void StopCar(CarAIController car)
    {
        if (car == null) return;
        car.isCarControlledByAI = false;
        car.CheckPointSearch = false;
        car.Accelerate(0f);
        car.Break(car.breaking);
    }

    // ─────────────────────────────────────────────────────────────
    //  PAUSE HELPERS
    // ─────────────────────────────────────────────────────────────

    private bool IsPaused()
        => _pauseController != null && _pauseController.IsPaused;

    /// <summary>
    /// Detects pause/resume edge each frame and freezes or restores
    /// the crashed cars accordingly.
    /// </summary>
    private void HandlePauseState()
    {
        bool nowPaused = IsPaused();
        if (nowPaused == _wasPaused) return;

        _wasPaused = nowPaused;
        if (nowPaused)
        {
            PauseCar(_controllerA);
            PauseCar(_controllerB);
        }
        else
        {
            ResumeCrashedCar(_controllerA);
            ResumeCrashedCar(_controllerB);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  BARRIER RECTANGLE  (road-aligned — algorithm unchanged)
    // ─────────────────────────────────────────────────────────────

    private void SpawnBarrierRectangle()
    {
        GetCarLengthAndWidth(carA, out float lengthA, out float widthA);
        GetCarLengthAndWidth(carB, out float lengthB, out float widthB);

        Bounds fenceBounds = GetPrefabBounds(barrierFencePrefab);
        float fenceLength = Mathf.Max(0.1f, fenceBounds.size.x);

        Vector3 forward = (carB.transform.position - carA.transform.position).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = carA.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        Vector3 center = (carA.transform.position + carB.transform.position) * 0.5f;
        center.y = Mathf.Min(
            GetWorldBounds(carA.gameObject).min.y,
            GetWorldBounds(carB.gameObject).min.y);

        float halfLength = (lengthA + lengthB) * 0.5f
                           + barrierPaddingWithCar
                           + barrierPaddingInBetween * 0.5f;
        float halfWidth = Mathf.Max(widthA, widthB) * 0.5f + barrierPaddingWithCar;

        Quaternion alongForward = Quaternion.LookRotation(right, Vector3.up);
        Quaternion alongRight = Quaternion.LookRotation(forward, Vector3.up);

        BuildWall(center - right * halfWidth, forward, halfLength * 2f, fenceLength, alongForward);
        BuildWall(center + right * halfWidth, forward, halfLength * 2f, fenceLength, alongForward);
        BuildWall(center + forward * halfLength, right, halfWidth * 2f, fenceLength, alongRight);
        BuildWall(center - forward * halfLength, right, halfWidth * 2f, fenceLength, alongRight);
    }

    private void BuildWall(Vector3 wallCenter, Vector3 axis, float wallLength,
                           float fenceLength, Quaternion rot)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(wallLength / fenceLength));
        float spacing = wallLength / count;
        Vector3 start = wallCenter - axis * (wallLength * 0.5f);
        for (int i = 0; i < count; i++)
            PlaceFence(start + axis * (spacing * (i + 0.5f)), rot);
    }

    private void PlaceFence(Vector3 position, Quaternion rotation)
        => _fences.Add(Instantiate(barrierFencePrefab, position, rotation, transform));

    // ─────────────────────────────────────────────────────────────
    //  LIFETIME + FADE
    // ─────────────────────────────────────────────────────────────

    private IEnumerator LifetimeRoutine()
    {
        float elapsed = 0f;
        while (elapsed < disappearDuration)
        {
            HandlePauseState();
            if (!IsPaused()) elapsed += Time.deltaTime;
            yield return null;
        }

        yield return StartCoroutine(FadeOutAll());

        // Despawn via CarAgent.ForceDespawn() (handles pooling / cleanup).
        if (carA != null) carA.ForceDespawn();
        if (carB != null) carB.ForceDespawn();

        Destroy(gameObject);
    }

    private IEnumerator FadeOutAll()
    {
        if (carA != null) carA.transform.SetParent(transform, worldPositionStays: true);
        if (carB != null) carB.transform.SetParent(transform, worldPositionStays: true);

        ParticleSystem[] smokeSystems = _smokeInstance != null
            ? _smokeInstance.GetComponentsInChildren<ParticleSystem>(true)
            : new ParticleSystem[0];
        foreach (var ps in smokeSystems)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        float smokeClearAt = Mathf.Max(0f, fadeDuration - smokeFadeLead);
        bool smokeCleared = false;

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            HandlePauseState();
            if (!IsPaused()) elapsed += Time.deltaTime;

            if (!smokeCleared && elapsed >= smokeClearAt)
            {
                ClearSmoke(smokeSystems);
                smokeCleared = true;
            }

            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            yield return null;
        }

        transform.localScale = Vector3.zero;
        if (!smokeCleared) ClearSmoke(smokeSystems);

        if (carA != null) carA.transform.SetParent(null, worldPositionStays: true);
        if (carB != null) carB.transform.SetParent(null, worldPositionStays: true);
    }

    // ─────────────────────────────────────────────────────────────
    //  STATIC HELPERS
    // ─────────────────────────────────────────────────────────────

    private static void GetCarLengthAndWidth(CarAgent car, out float length, out float width)
    {
        Bounds b = GetWorldBounds(car.gameObject);
        float xSize = b.size.x;
        float zSize = b.size.z;
        if (xSize >= zSize) { length = xSize; width = zSize; }
        else { length = zSize; width = xSize; }
    }

    private static Vector3 GetFrontBumperPosition(CarAgent car)
    {
        Renderer r = car.GetComponentInChildren<Renderer>();
        return r != null
            ? car.transform.position + car.transform.forward * r.bounds.extents.z
            : car.transform.position + car.transform.forward * 1f;
    }

    private static float GetTopY(CarAgent carA, CarAgent carB)
    {
        float topA = carA != null ? GetWorldBounds(carA.gameObject).max.y : 0f;
        float topB = carB != null ? GetWorldBounds(carB.gameObject).max.y : 0f;
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

    private void ClearSmoke(ParticleSystem[] smokeSystems)
    {
        foreach (var ps in smokeSystems)
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_smokeInstance != null) _smokeInstance.SetActive(false);
    }
}