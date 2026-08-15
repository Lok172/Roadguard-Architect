using UnityEngine;

// CarDespawnerScript removes a CarAIController from the scene when it enters this object's
// trigger collider. Visibility of the despawner's material is controlled externally by
// LevelSpawnerActivator, not by this script.
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