using UnityEngine;

// CarDespawnerScript removes a CarAIController from the scene when it enters this object's
// trigger collider. Visibility of the despawner's material is controlled externally by
// LevelSpawnerActivator, not by this script.
[RequireComponent(typeof(Collider))]
public class CarDespawnerScript : MonoBehaviour
{
    public carspawnerscript linkedSpawner;

    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null) return;

        Debug.Log($"[CarDespawnerScript] '{car.gameObject.name}' reached despawner '{gameObject.name}'.");

        Destroy(car.gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (linkedSpawner == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, linkedSpawner.transform.position);
    }
#endif
}