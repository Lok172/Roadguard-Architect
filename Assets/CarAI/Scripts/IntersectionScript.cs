using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IntersectionScript : MonoBehaviour
{
    public List<GameObject> stops = new List<GameObject>();

    [Tooltip("The time a stop is green in seconds.")]
    public float wait = 5f;

    [Tooltip("If true, a car will always wait the full timer even if no other cars are present at other stops. " +
             "If false, the waiting car is allowed through immediately when all other stops are empty.")]
    public bool WaitIfNoOtherCar = false;

    [Tooltip("If true, the stop objects will show their red/green material at runtime. " +
             "If false, the MeshRenderer on each stop is disabled and no material is shown.")]
    public bool showStopMaterial = true;

    private int index = 0;

    private bool next = true;

    private List<StopScript> scripts = new List<StopScript>();

    void Awake()
    {
        for (int i = 0; i < stops.Count; i++)
        {
            StopScript script = stops[i].GetComponent<StopScript>();
            script.stop = true;
            scripts.Add(script);

            MeshRenderer mr = stops[i].GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (showStopMaterial)
                    mr.material.color = Color.red;
                else
                    mr.enabled = false;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (next)
        {
            index++;
            if (index >= stops.Count)
            {
                index = 0;
            }
            StartCoroutine(Cycle());
        }
    }

    IEnumerator Cycle()
    {
        next = false;

        if (showStopMaterial)
            stops[index].GetComponent<MeshRenderer>().material.color = Color.green;
        scripts[index].stop = false;

        if (WaitIfNoOtherCar)
        {
            // Always wait the full timer regardless of traffic at other stops.
            yield return new WaitForSeconds(wait);
        }
        else
        {
            // Bail early if THIS stop has no car waiting. Checking whether
            // OTHER stops have cars (the old condition) is backwards — it
            // made an empty active stop hold its full green duration simply
            // because traffic existed elsewhere, delaying whichever stop
            // actually had a car by a full cycle per empty stop in between.
            float elapsed = 0f;
            while (elapsed < wait)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;

                if (scripts[index].priority == 0)
                    break;
            }
        }

        // Wait until the car that was let through has fully cleared the stop.
        while (scripts[index].priority > 0)
        {
            yield return new WaitForSeconds(0.1f);
        }

        if (showStopMaterial)
            stops[index].GetComponent<MeshRenderer>().material.color = Color.red;
        scripts[index].stop = true;

        next = true;
    }


}