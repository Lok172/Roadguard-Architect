using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add to a RoadTile (automatically by RoadTile.ActivateDeviceZone) when a
/// Speed Bump is placed.
///
/// BEHAVIOUR:
///   • OnTriggerEnter  — car's overrideSpeedLimit is set to BUMP_SPEED (slow
///                        crawl) so it brakes before and while crossing.
///   • OnTriggerExit   — after a short resume delay the override is cleared
///                        and the car accelerates back to its normal speedLimit.
///
/// The resume delay gives a natural feel: the car keeps crawling for one extra
/// beat after the front wheels clear the bump before picking up speed again.
/// </summary>
public class SpeedBumpZone : MonoBehaviour
{
    [Tooltip("Speed the car slows to while crossing the bump (km/h).")]
    public int bumpSpeed = 5;

    [Tooltip("Seconds to wait after exiting the bump before restoring full speed.")]
    public float resumeDelay = 0.8f;

    private readonly HashSet<CarAIController> _carsInZone = new HashSet<CarAIController>();

    // Track per-car resume coroutines so we can cancel them if the car re-enters.
    private readonly Dictionary<CarAIController, Coroutine> _resumeCoroutines
        = new Dictionary<CarAIController, Coroutine>();

    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null) return;

        // If a resume was pending, cancel it — car is back on the bump.
        if (_resumeCoroutines.TryGetValue(car, out Coroutine pending) && pending != null)
        {
            StopCoroutine(pending);
            _resumeCoroutines.Remove(car);
        }

        _carsInZone.Add(car);
        car.overrideSpeedLimit = bumpSpeed;

        Debug.Log($"[SpeedBumpZone] {car.name} entered — slowing to {bumpSpeed} km/h.");
    }

    private void OnTriggerExit(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null || !_carsInZone.Contains(car)) return;

        _carsInZone.Remove(car);

        // Start a delayed restore so the car keeps crawling briefly past the bump.
        Coroutine c = StartCoroutine(ResumeAfterDelay(car));
        _resumeCoroutines[car] = c;
    }

    private IEnumerator ResumeAfterDelay(CarAIController car)
    {
        yield return new WaitForSeconds(resumeDelay);

        if (car != null && car.overrideSpeedLimit == bumpSpeed)
        {
            car.overrideSpeedLimit = -1;
            Debug.Log($"[SpeedBumpZone] {car.name} cleared bump — speed restored.");
        }

        _resumeCoroutines.Remove(car);
    }

    private void OnDestroy()
    {
        foreach (var car in _carsInZone)
        {
            if (car != null && car.overrideSpeedLimit == bumpSpeed)
                car.overrideSpeedLimit = -1;
        }
        _carsInZone.Clear();
    }
}
