using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Sets the stop flag on a list of linked StopScript components whenever a
// car enters this trigger zone, and clears it again when the car exits.
public class CheckerScript : MonoBehaviour
{
    public List<StopScript> stopScripts;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnTriggerEnter(Collider c)
    {
        CarAIController controller = c.GetComponentInParent<CarAIController>();

        if (controller)
        {
            for (int i = 0; i < stopScripts.Count; i++)
                stopScripts[i].stop = true;
        }
    }

    void OnTriggerExit(Collider c)
    {
        CarAIController controller = c.GetComponentInParent<CarAIController>();

        if (controller)
        {
            for (int i = 0; i < stopScripts.Count; i++)
                stopScripts[i].stop = false;
        }
    }
}