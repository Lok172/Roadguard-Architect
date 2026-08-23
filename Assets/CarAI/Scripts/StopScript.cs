using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Holds a car at speed 0 while it remains in this trigger and stop is true,
// and releases it back to its speed limit when stop is false or the car
// exits. priority counts cars present in the trigger and resets every
// FixedUpdate, used by IntersectionScript to detect when a stop has cleared.
public class StopScript : MonoBehaviour
{
    [Tooltip("If true the car that touches the trigger will stop.")]
    public bool stop = true;
    public int priority = 0;


    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    void FixedUpdate()
    {
        priority = 0;
    }

    private void OnTriggerStay(Collider other)
    {
        CarAIController carController = other.gameObject.GetComponentInParent<CarAIController>();

        if (carController != null)
        {
            priority++;
            if (stop && !carController.objectDetected)
            {
                carController.SetSpeed(0);
                carController.CheckPointSearch = false;
            }

            if (!stop && !carController.objectDetected)
            {
                carController.SetSpeed(carController.speedLimit);
                carController.CheckPointSearch = true;
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        CarAIController carController = other.gameObject.GetComponentInParent<CarAIController>();

        if (carController != null)
        {
            carController.SetSpeed(carController.speedLimit);
            carController.CheckPointSearch = true;
        }
    }

}