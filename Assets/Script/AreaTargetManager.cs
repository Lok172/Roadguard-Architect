using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// This script is used to manage accident-target selection within designated
// areas, including recklessness boosting and its gradual reduction as correct
// devices are placed.

public class AreaTargetManager : MonoBehaviour
{
    public static AreaTargetManager Instance { get; private set; }

    [Header("Area Selection")]
    [Tooltip("Assign the cube GameObjects (with Box Colliders set to Is Trigger) that define selectable areas.")]
    public List<Collider> targetAreas = new List<Collider>();

    [Header("Recklessness")]
    [Tooltip("The recklessnessThreshold value applied to the chosen car.")]
    public int boostedRecklessness = 50;

    [Header("Recklessness Mitigation")]
    [Tooltip("Number of CORRECTLY placed devices (city-wide) needed before the recklessness " +
             "boost is fully neutralised (reduced to 0). Values in between linearly reduce it.")]
    [Min(1)] public int correctDevicesToZeroRecklessness = 2;

    [Header("Accident Settings")]
    [Tooltip("Smoke/particle prefab spawned at the car's centre on collision.")]
    public GameObject smokePrefab;

    [Tooltip("Seconds before the faded-out car starts shrinking.")]
    public float fadeDelay = 3f;

    [Tooltip("How long the shrink-fade animation takes.")]
    public float fadeDuration = 1.5f;

    [Tooltip("StopScript whose 'stop' flag will be set true when the accident triggers.")]
    public StopScript accidentStopper;

    private int _baseBoostedRecklessness;
    private int _correctDeviceCount = 0;

    private CarAIController _currentTarget;

    public void NotifyCorrectDevicePlaced()
    {
        _correctDeviceCount++;

        float t = Mathf.Clamp01((float)_correctDeviceCount / correctDevicesToZeroRecklessness);
        boostedRecklessness = Mathf.RoundToInt(Mathf.Lerp(_baseBoostedRecklessness, 0f, t));

        Debug.Log($"[AreaTargetManager] Correct devices city-wide: {_correctDeviceCount} → " +
                  $"boostedRecklessness = {boostedRecklessness}");

        if (_currentTarget != null)
        {
            _currentTarget.recklessnessThreshold = boostedRecklessness;
            Debug.Log($"[AreaTargetManager] Live target '{_currentTarget.name}' recklessnessThreshold " +
                      $"→ {boostedRecklessness}");
        }
    }

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

        _currentTarget = chosen;

        chosen.recklessnessThreshold = boostedRecklessness;
        Debug.Log($"[AreaTargetManager] recklessnessThreshold → {boostedRecklessness}");

        CarCollisionHandler handler = chosen.GetComponent<CarCollisionHandler>();
        if (handler == null)
            handler = chosen.gameObject.AddComponent<CarCollisionHandler>();

        handler.Configure(smokePrefab, fadeDelay, fadeDuration, accidentStopper);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AreaTargetManager] Duplicate — destroying.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _baseBoostedRecklessness = boostedRecklessness;

        if (accidentStopper == null)
            accidentStopper = FindObjectOfType<StopScript>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    private List<CarAIController> GatherCarsInAreas()
    {
        List<CarAIController> found = new List<CarAIController>();

        foreach (Collider area in targetAreas)
        {
            if (area == null) continue;

            BoxCollider box = area as BoxCollider;
            if (box == null)
            {
                Debug.LogWarning($"[AreaTargetManager] '{area.gameObject.name}' is not a BoxCollider – skipping.");
                continue;
            }

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