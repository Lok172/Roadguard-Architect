using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  CAR SPAWNER SCRIPT  (v2)
//
//  CHANGES vs v1:
//    • Cars no longer auto-spawn in Start(). Spawning is triggered
//      exclusively by GameManager.InitLevel() via ResetAndSpawn().
//    • Awake() self-registers with GameManager so dynamically-placed
//      spawners are picked up automatically.
//    • ResetAndSpawn() kills any running spawn coroutine, destroys
//      any previously spawned cars, then restarts the cycle.
//    • New field: accidentAreaCheckpoint — optional Transform to
//      assign as nextCheckpoint for cars spawned into an accident area
//      (Feature 3). If null, startingCheckpoint is used as before.


public class carspawnerscript : MonoBehaviour
{
    [Tooltip("A list of car models that will be spawned randomly.")]
    public List<GameObject> cars = new List<GameObject>();

    [Tooltip("The number of cars that will be spawned.")]
    public int numberOfCarsToSpawn = 1;

    [Tooltip("The first checkpoint that the car(s) will be redirected to.")]
    public Transform startingCheckpoint;

    [Tooltip("(Feature 3) Optional: if this spawner sits inside an accident area, " +
             "assign the accident-area checkpoint here so cars navigate into it. " +
             "Leave empty to use startingCheckpoint.")]
    public Transform accidentAreaCheckpoint;

    [Tooltip("Time interval between cars in seconds.")]
    public float timeIntervalBetweenCarsInSeconds = 0f;

    [Header("Distance kept from other objects (randomised per car)")]
    public float distanceKeptMin = 2f;
    public float distanceKeptMax = 2f;

    [Header("Recklessness threshold (randomised per car)")]
    public int recklessnessMin = 0;
    public int recklessnessMax = 0;

    // ── Runtime 
    [Tooltip("Internal: set false to block spawning (e.g. another car is in the trigger zone).")]
    public bool canSpawn = true;

    [Tooltip("If true, the spawner object's material is visible at runtime. " +
             "If false, the MeshRenderer on this spawner is disabled so it is invisible in-game.")]
    public bool showSpawnerMaterial = true;

    private Coroutine _spawnCoroutine;
    private readonly List<GameObject> _spawnedCars = new List<GameObject>();


    //  LIFECYCLE


    private void Awake()
    {
        // Self-register with GameManager if it already exists.
        // If GameManager isn't ready yet, GameManager will auto-find us via
        // FindObjectsByType in StartSpawnersNextFrame.
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterSpawner(this);

        // Hide this spawner's own mesh at runtime if requested.
        if (!showSpawnerMaterial)
        {
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
    }

    // Start() intentionally left empty — spawning begins only when
    // GameManager calls ResetAndSpawn().


    //  PUBLIC API  (called by GameManager)


    /// <summary>
    /// Stops any running spawn coroutine, destroys previously spawned cars,
    /// resets state, then begins a fresh spawn cycle.
    /// Called by GameManager.InitLevel() at the start of every level.
    /// </summary>
    public void ResetAndSpawn()
    {
        // Stop previous cycle
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        // Destroy cars from a previous run (e.g. level restart)
        foreach (GameObject car in _spawnedCars)
        {
            if (car != null) Destroy(car);
        }
        _spawnedCars.Clear();

        canSpawn = true;

        _spawnCoroutine = StartCoroutine(SpawnCycle());
        Debug.Log($"[carspawnerscript] '{gameObject.name}' spawn cycle started " +
                  $"(count={numberOfCarsToSpawn}, interval={timeIntervalBetweenCarsInSeconds}s).");
    }


    //  SPAWN CYCLE


    private IEnumerator SpawnCycle()
    {
        int index = 0;
        while (index < numberOfCarsToSpawn)
        {
            if (canSpawn)
            {
                SpawnOneCar();
                index++;
                yield return new WaitForSeconds(timeIntervalBetweenCarsInSeconds);
            }
            else
            {
                yield return new WaitForSeconds(1f);
            }
        }
    }

    private void SpawnOneCar()
    {
        if (cars == null || cars.Count == 0)
        {
            Debug.LogWarning($"[carspawnerscript] '{gameObject.name}' has no car models assigned.");
            return;
        }

        GameObject model = cars[Random.Range(0, cars.Count)];
        GameObject newCar = Instantiate(model, transform.position, transform.rotation);

        CarAIController controller = newCar.GetComponent<CarAIController>();
        if (controller == null)
        {
            Debug.LogWarning($"[carspawnerscript] Spawned car '{newCar.name}' has no CarAIController.");
            _spawnedCars.Add(newCar);
            return;
        }

        controller.CheckPointSearch = true;
        controller.isCarControlledByAI = true;
        controller.distanceFromObjects = Random.Range(distanceKeptMin, distanceKeptMax);
        controller.recklessnessThreshold = Random.Range(recklessnessMin, recklessnessMax);

        // Feature 3: use accident-area checkpoint if assigned, otherwise default
        controller.nextCheckpoint = accidentAreaCheckpoint != null
            ? accidentAreaCheckpoint
            : startingCheckpoint;

        _spawnedCars.Add(newCar);
    }


    //  TRIGGER — block spawning while a car is waiting at the spawn point


    private void OnTriggerStay(Collider other)
    {
        if (other.GetComponent<CarAIController>())
            canSpawn = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<CarAIController>())
            canSpawn = true;
    }
}