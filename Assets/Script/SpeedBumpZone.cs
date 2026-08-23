using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This script manages the speed bump device's effect on car behaviour.
/// It is added to a RoadTile's GameObject when a Speed Bump is placed on it.
///
/// While a car is inside the trigger zone, its speed is overridden to a slow
/// crawl. After the car exits, the override is held briefly before being
/// cleared so the car resumes its normal speed limit smoothly.
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
