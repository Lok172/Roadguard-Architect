using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to a RoadTile by RoadTile.ActivateDeviceZone when a Stop Sign
/// is placed. While a CarAIController is inside the tile's trigger
/// collider, its overrideSpeedLimit is set to half of its own speedLimit
/// (rounded down); the override is cleared when the car exits.
/// </summary>
public class StopSignZone : MonoBehaviour
{
    // Track cars currently inside so we can clear them cleanly.
    private readonly HashSet<CarAIController> _carsInZone = new HashSet<CarAIController>();

    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null || _carsInZone.Contains(car)) return;

        _carsInZone.Add(car);

        // Halve the car's own speed limit (at least 1 km/h so the car still moves).
        int halfSpeed = Mathf.Max(1, car.speedLimit / 2);
        car.overrideSpeedLimit = halfSpeed;

        Debug.Log($"[StopSignZone] {car.name} entered — speed capped to {halfSpeed} km/h (was {car.speedLimit}).");
    }

    private void OnTriggerExit(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null || !_carsInZone.Contains(car)) return;

        _carsInZone.Remove(car);

        // Only clear our override — don't touch other device overrides.
        if (car.overrideSpeedLimit == Mathf.Max(1, car.speedLimit / 2))
            car.overrideSpeedLimit = -1;

        Debug.Log($"[StopSignZone] {car.name} exited — speed restored.");
    }

    private void OnDestroy()
    {
        // Clean up if tile is destroyed while cars are still inside.
        foreach (var car in _carsInZone)
        {
            if (car != null) car.overrideSpeedLimit = -1;
        }
        _carsInZone.Clear();
    }
}
