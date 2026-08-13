using UnityEngine;

//  CAR DESPAWNER SCRIPT
//
//  Mirrors carspawnerscript's conventions: place at the end of a road
//  (or anywhere you want cars removed), attach a trigger Collider, and
//  any CarAIController that enters is destroyed immediately.
//
//  SETUP:
//    1. Add a BoxCollider (Is Trigger = ON) to a GameObject positioned
//       where cars should disappear.
//    2. Attach this script to the same GameObject.
//    3. (Optional) Assign "Linked Spawner" — purely for your own
//       organisation. It draws a gizmo line in the Scene view between
//       this despawner and its paired spawner so you can spot which
//       goes with which at a glance. It has NO effect on gameplay —
//       any car entering the trigger is removed regardless of which
//       spawner it actually came from.

[RequireComponent(typeof(Collider))]
public class CarDespawnerScript : MonoBehaviour
{
    [Tooltip("Optional: the spawner this despawner is paired with. Purely for " +
             "organisation/gizmo drawing in the editor — has no effect on behaviour.")]
    public carspawnerscript linkedSpawner;

    [Tooltip("If true, this despawner's material is visible at runtime. " +
             "If false, the MeshRenderer on this despawner is disabled so it " +
             "is invisible in-game (same behaviour as carspawnerscript's " +
             "'Show Spawner Material' toggle).")]
    public bool showDespawnerMaterial = true;

    private void Awake()
    {
        if (!showDespawnerMaterial)
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        CarAIController car = other.GetComponentInParent<CarAIController>();
        if (car == null) return;

        Debug.Log($"[CarDespawnerScript] '{car.gameObject.name}' reached despawner " +
                  $"'{gameObject.name}' — removing.");

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
