using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Cycles a set of stops through green in turn, holding each green for up to
// wait seconds (or less, once its stop is clear, when WaitIfNoOtherCar is
// false), then giving cars time to clear the intersection before returning
// the stop to red and advancing to the next one.
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
            // Bail early once this stop has no car waiting, rather than
            // holding the full green duration while other stops still have
            // traffic.
            float elapsed = 0f;
            while (elapsed < wait)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;

                if (scripts[index].priority == 0)
                    break;
            }
        }

        // Deadlock guard: if priority is still above 0 after 1 seconds, force
        // it to 0 instead of blocking this stop, and the whole intersection
        // cycle behind it, indefinitely.
        float clearElapsed = 0f;
        while (scripts[index].priority > 0)
        {
            yield return new WaitForSeconds(0.1f);
            clearElapsed += 0.1f;

            if (clearElapsed >= 1f)
            {
                scripts[index].priority = 0;
                break;
            }
        }

        // Extra grace period so the vehicle has time to fully clear the
        // physical intersection (not just exit the trigger sensor) before
        // the light switches back to red.
        yield return new WaitForSeconds(1f);

        if (showStopMaterial)
            stops[index].GetComponent<MeshRenderer>().material.color = Color.red;
        scripts[index].stop = true;

        next = true;
    }


}