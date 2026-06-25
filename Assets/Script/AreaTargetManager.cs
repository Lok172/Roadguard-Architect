using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class AreaTargetManager : MonoBehaviour
{
    [Header("Area Selection")]
    [Tooltip("Assign the cube GameObjects (with Box Colliders set to Is Trigger) that define selectable areas.")]
    public List<Collider> targetAreas = new List<Collider>();

    [Header("Recklessness")]
    [Tooltip("The recklessnessThreshold value applied to the chosen car.")]
    public int boostedRecklessness = 50;

    [Header("Accident Settings")]
    [Tooltip("Smoke/particle prefab spawned at the car's centre on collision.")]
    public GameObject smokePrefab;

    [Tooltip("Seconds before the faded-out car starts shrinking.")]
    public float fadeDelay = 3f;

    [Tooltip("How long the shrink-fade animation takes.")]
    public float fadeDuration = 1.5f;

    [Tooltip("StopScript whose 'stop' flag will be set true when the accident triggers.")]
    public StopScript accidentStopper;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Randomly picks one car that is currently inside any of the target areas,
    /// boosts its recklessness, and wires it up for collision-based accident behaviour.
    /// Safe to call multiple times – each call targets a freshly chosen car.
    /// </summary>
    [ContextMenu("Pick Target Car")]
    public void PickTargetCar()
    {
        List<CarAIController> candidates = GatherCarsInAreas();

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[AreaTargetManager] No CarAIController found inside the target areas.");
            return;
        }

        CarAIController chosen = candidates[Random.Range(0, candidates.Count)];
        Debug.Log($"[AreaTargetManager] Target selected: {chosen.gameObject.name}");

        // 1 & 2 – Set recklessness
        chosen.recklessnessThreshold = boostedRecklessness;
        Debug.Log($"[AreaTargetManager] recklessnessThreshold → {boostedRecklessness}");

        // 3, 4, 5 – Attach or refresh the collision handler
        CarCollisionHandler handler = chosen.GetComponent<CarCollisionHandler>();
        if (handler == null)
            handler = chosen.gameObject.AddComponent<CarCollisionHandler>();

        handler.Configure(smokePrefab, fadeDelay, fadeDuration, accidentStopper);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (accidentStopper == null)
            accidentStopper = FindObjectOfType<StopScript>();
    }
    private List<CarAIController> GatherCarsInAreas()
    {
        List<CarAIController> found = new List<CarAIController>();

        // Use OverlapBox for each area collider
        foreach (Collider area in targetAreas)
        {
            if (area == null) continue;

            BoxCollider box = area as BoxCollider;
            if (box == null)
            {
                Debug.LogWarning($"[AreaTargetManager] '{area.gameObject.name}' is not a BoxCollider – skipping.");
                continue;
            }

            // World-space centre and half-extents, accounting for scale
            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, box.transform.lossyScale);
            Quaternion orientation = box.transform.rotation;

            Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, orientation);
            foreach (Collider hit in hits)
            {
                CarAIController car = hit.GetComponent<CarAIController>();
                if (car != null && !found.Contains(car))
                    found.Add(car);
            }
        }

        return found;
    }

    // ─── Editor visualisation ────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
        foreach (Collider area in targetAreas)
        {
            if (area == null) continue;
            BoxCollider box = area as BoxCollider;
            if (box == null) continue;

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                box.transform.TransformPoint(box.center),
                box.transform.rotation,
                box.transform.lossyScale);

            Gizmos.DrawCube(Vector3.zero, box.size);

            Gizmos.color = new Color(0f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, box.size);
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
            Gizmos.matrix = old;
        }
    }
#endif
}
