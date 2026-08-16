using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// carspawnerscript spawns car instances at this object's position and assigns them a starting
// checkpoint. Spawning is triggered by GameManager.InitLevel() through ResetAndSpawn(), and the
// spawner registers itself with GameManager when created. Visibility of the spawner's material is
// controlled externally by LevelSpawnerActivator, not by this script.
public class carspawnerscript : MonoBehaviour
{
    [Tooltip("A list of car models that will be spawned randomly.")]
    public List<GameObject> cars = new List<GameObject>();

    [Tooltip("The number of cars that will be spawned.")]
    public int numberOfCarsToSpawn = 1;

    [Tooltip("The first checkpoint that the car(s) will be redirected to.")]
    public Transform startingCheckpoint;

    [Tooltip("The checkpoint assigned to cars spawned into an accident area. If left empty, this is detected automatically at Start by finding the checkpoint nearest to this spawner that sits inside one of AreaTargetManager's target areas. A manually assigned value here always takes priority over detection.")]
    public Transform accidentAreaCheckpoint;

    [Tooltip("Time interval between cars in seconds.")]
    public float timeIntervalBetweenCarsInSeconds = 0f;

    [Header("Distance kept from other objects (randomised per car)")]
    public float distanceKeptMin = 2f;
    public float distanceKeptMax = 2f;

    [Header("Recklessness threshold (randomised per car)")]
    public int recklessnessMin = 0;
    public int recklessnessMax = 0;

    [Tooltip("Internal: set false to block spawning (e.g. another car is in the trigger zone).")]
    public bool canSpawn = true;

    private Coroutine _spawnCoroutine;
    private readonly List<GameObject> _spawnedCars = new List<GameObject>();

    private void Awake()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterSpawner(this);
    }

    private void Start()
    {
        if (accidentAreaCheckpoint == null)
            accidentAreaCheckpoint = FindAccidentCheckpoint();
    }

    private Transform FindAccidentCheckpoint()
    {
        AreaTargetManager areaManager = FindFirstObjectByType<AreaTargetManager>();
        if (areaManager == null || areaManager.targetAreas == null) return null;

        CheckpointScript[] allCheckpoints = FindObjectsByType<CheckpointScript>(FindObjectsSortMode.None);

        Transform closest = null;
        float closestDistance = float.MaxValue;

        foreach (Collider area in areaManager.targetAreas)
        {
            if (area == null) continue;

            foreach (CheckpointScript cp in allCheckpoints)
            {
                if (!area.bounds.Contains(cp.transform.position)) continue;

                float distance = Vector3.Distance(transform.position, cp.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = cp.transform;
                }
            }
        }

        return closest;
    }

    public void ResetAndSpawn()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

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

        controller.nextCheckpoint = accidentAreaCheckpoint != null
            ? accidentAreaCheckpoint
            : startingCheckpoint;

        _spawnedCars.Add(newCar);
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.GetComponentInParent<CarAIController>())
            canSpawn = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<CarAIController>())
            canSpawn = true;
    }
}