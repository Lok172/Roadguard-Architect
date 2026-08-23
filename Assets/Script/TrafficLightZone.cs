using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to a RoadTile by RoadTile.ActivateDeviceZone when a Traffic
/// Light is placed. On entering the trigger, a car's forceStop flag is
/// set so it halts at the light; the flag is cleared after redDuration
/// seconds, or immediately if the car leaves the zone early. Only one
/// stop sequence runs per car visit.
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
