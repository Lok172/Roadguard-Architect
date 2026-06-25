using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add to a RoadTile (automatically by RoadTile.ActivateDeviceZone) when a
/// Traffic Light is placed.
///
/// BEHAVIOUR:
///   OnTriggerEnter — sets forceStop = true on the car so it brakes to a
///                    complete halt at the light.
///   After RED_DURATION seconds — clears forceStop so the car resumes.
///   Cars that exit the zone early (e.g. pushed out) are also cleaned up.
///
/// Only one stop sequence runs per car visit.  If the car exits before the
/// timer fires (unusual but possible), the stop flag is cleared immediately.
/// </summary>
public class TrafficLightZone : MonoBehaviour
{
    [Tooltip("How long (seconds) cars wait at a red light before being released.")]
    public float redDuration = 2f;

    // Cars currently stopped at this light.
    private readonly HashSet<CarAIController> _waitingCars = new HashSet<CarAIController>();
    private readonly Dictionary<CarAIController, Coroutine> _timers
        = new Dictionary<CarAIController, Coroutine>();

    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null || _waitingCars.Contains(car)) return;

        _waitingCars.Add(car);
        car.forceStop = true;

        Debug.Log($"[TrafficLightZone] {car.name} stopped at red light.");

        Coroutine t = StartCoroutine(ReleaseAfterRed(car));
        _timers[car] = t;
    }

    private void OnTriggerExit(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null || !_waitingCars.Contains(car)) return;

        // Car left the zone (already released or pushed out) — clean up.
        Releasecar(car);
    }

    private IEnumerator ReleaseAfterRed(CarAIController car)
    {
        yield return new WaitForSeconds(redDuration);

        if (car != null && _waitingCars.Contains(car))
        {
            Debug.Log($"[TrafficLightZone] {car.name} — green light, resuming.");
            Releasecar(car);
        }
    }

    private void Releasecar(CarAIController car)
    {
        if (car != null) car.forceStop = false;

        if (_timers.TryGetValue(car, out Coroutine t) && t != null)
            StopCoroutine(t);

        _timers.Remove(car);
        _waitingCars.Remove(car);
    }

    private void OnDestroy()
    {
        foreach (var car in _waitingCars)
        {
            if (car != null) car.forceStop = false;
        }
        _waitingCars.Clear();
    }
}
