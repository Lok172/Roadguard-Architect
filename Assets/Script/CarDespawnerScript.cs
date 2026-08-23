using UnityEngine;

// This script is used to remove a car from the scene once it enters this
// object's trigger collider.

[RequireComponent(typeof(Collider))]
public class CarDespawnerScript : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null) return;

        Debug.Log($"[CarDespawnerScript] '{car.gameObject.name}' reached despawner '{gameObject.name}'.");

        Destroy(car.gameObject);
    }
}