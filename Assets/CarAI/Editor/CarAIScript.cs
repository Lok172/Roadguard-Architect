using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// CarAIEditorScript provides an editor window with tools for car setup, checkpoint creation,
// checkpoint editing, and intersection creation.
public class CarAIEditorScript : EditorWindow
{
    private bool setupcar = false;
    private bool spawncheckpoints = false;
    private bool createintersection = false;

    private GameObject carmodel;

    private Transform frontRight;
    private Transform frontLeft;
    private Transform rearRight;
    private Transform rearLeft;

    private WheelCollider frontRightCollider;
    private WheelCollider frontLeftCollider;
    private WheelCollider rearLeftCollider;
    private WheelCollider rearRightCollider;

    private float acceleration = 10000;
    private float breaking = 100000;
    private int speedLimit;
    private Transform check;
    private int count = 0;

    private int checkpointsBetweenCount = 1;

    private int stops = 0;

    [MenuItem("Window/Car AI")]

    public static void ShowWindow()
    {
        GetWindow<CarAIEditorScript>("Car AI");
    }

    void OnGUI()
    {
        if (GUILayout.Button("Setup car"))
        {
            setupcar = true;
            spawncheckpoints = false;
            createintersection = false;
        }
        else if (GUILayout.Button("Checkpoints"))
        {
            spawncheckpoints = true;
            setupcar = false;
            createintersection = false;
        }
        else if (GUILayout.Button("Create intersection"))
        {
            createintersection = true;
            setupcar = false;
            spawncheckpoints = false;
        }
        else if (GUILayout.Button("Spawn if block"))
        {
            GameObject p = new GameObject("If block");

            GameObject stopper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stopper.name = "Stopper";
            stopper.transform.localScale = Vector3.one;
            stopper.transform.SetParent(p.transform);
            stopper.GetComponent<BoxCollider>().isTrigger = true;
            StopScript stopScript = stopper.AddComponent<StopScript>();
            stopScript.stop = false;

            GameObject checker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            checker.name = "Checker";
            checker.transform.localScale = Vector3.one;
            checker.GetComponent<BoxCollider>().isTrigger = true;
            checker.transform.SetParent(p.transform);
            checker.transform.position = stopper.transform.forward * 2f;
            CheckerScript checkerScript = checker.AddComponent<CheckerScript>();

            checkerScript.stopScripts.Add(stopScript);
        }

        if (setupcar)
        {
            SetupCar();
        }
        else if (spawncheckpoints)
        {
            SpawnCheckpoints();
        }
        else if (createintersection)
        {
            CreateIntersection();
        }

        GUILayout.Label("", EditorStyles.boldLabel);
        GUILayout.Label("Check the file doc.pdf for documentation.", EditorStyles.boldLabel);

    }

    void SetupCar()
    {
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Car model", EditorStyles.boldLabel);
        carmodel = (GameObject)EditorGUILayout.ObjectField(carmodel, typeof(GameObject), true);

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Front right wheel transform", EditorStyles.boldLabel);
        frontRight = (Transform)EditorGUILayout.ObjectField(frontRight, typeof(Transform), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Front left wheel transform", EditorStyles.boldLabel);
        frontLeft = (Transform)EditorGUILayout.ObjectField(frontLeft, typeof(Transform), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Rear right wheel transform", EditorStyles.boldLabel);
        rearRight = (Transform)EditorGUILayout.ObjectField(rearRight, typeof(Transform), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Rear left wheel transform", EditorStyles.boldLabel);
        rearLeft = (Transform)EditorGUILayout.ObjectField(rearLeft, typeof(Transform), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Front right wheel collider", EditorStyles.boldLabel);
        frontRightCollider = (WheelCollider)EditorGUILayout.ObjectField(frontRightCollider, typeof(WheelCollider), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Front left wheel collider", EditorStyles.boldLabel);
        frontLeftCollider = (WheelCollider)EditorGUILayout.ObjectField(frontLeftCollider, typeof(WheelCollider), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Rear right wheel collider", EditorStyles.boldLabel);
        rearRightCollider = (WheelCollider)EditorGUILayout.ObjectField(rearRightCollider, typeof(WheelCollider), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Rear left wheel collider", EditorStyles.boldLabel);
        rearLeftCollider = (WheelCollider)EditorGUILayout.ObjectField(rearLeftCollider, typeof(WheelCollider), true);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Acceleration", EditorStyles.boldLabel);
        acceleration = EditorGUILayout.FloatField(acceleration);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Breaking", EditorStyles.boldLabel);
        breaking = EditorGUILayout.FloatField(breaking);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Speed limit", EditorStyles.boldLabel);
        speedLimit = EditorGUILayout.IntField(speedLimit);

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Check position", EditorStyles.boldLabel);
        check = (Transform)EditorGUILayout.ObjectField(check, typeof(Transform), true);

        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Apply") && !carmodel.GetComponent<CarAIController>())
        {
            CarAIController controller = carmodel.AddComponent<CarAIController>();

            controller.frontRight = frontRight;
            controller.frontLeft = frontLeft;
            controller.rearLeft = rearLeft;
            controller.rearRight = rearRight;

            controller.frontRightCollider = frontRightCollider;
            controller.frontLeftCollider = frontLeftCollider;
            controller.rearRightCollider = rearRightCollider;
            controller.rearLeftCollider = rearLeftCollider;

            controller.acceleration = acceleration;
            controller.breaking = breaking;
            controller.speedLimit = speedLimit;
            if (controller.checks[0] == null)
            {
                controller.checks[0] = check;
            }
        }
    }

    void SpawnCheckpoints()
    {
        GUILayout.Label("Spawn checkpoint and press on it. You will find instructions there.", EditorStyles.boldLabel);

        if (GUILayout.Button("Spawn checkpoint"))
        {
            GameObject parent = new GameObject("Checkpoints");
            CheckpointScript script = new CheckpointScript();
            spawnCheckpoint(Vector3.zero, Vector3.one, parent.transform, ref script);
        }
        else if (GUILayout.Button("Connect selected checkpoints"))
        {
            ConnectSelectedCheckpoints();
        }
        else if (GUILayout.Button("Disconnect selected checkpoints"))
        {
            GameObject[] selected = Selection.gameObjects;

            for (int i = 0; i + 1 < selected.Length; i++)
            {
                CheckpointScript script = selected[i].GetComponent<CheckpointScript>();
                if (script)
                {
                    script.nextCheckpoints.Remove(selected[i + 1].transform);
                    EditorUtility.SetDirty(script);
                    EditorSceneManager.MarkSceneDirty(selected[i].scene);
                }
            }
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Checkpoints to spawn between", EditorStyles.boldLabel);
        checkpointsBetweenCount = EditorGUILayout.IntField(checkpointsBetweenCount);
        EditorGUILayout.EndVertical();
        if (checkpointsBetweenCount < 1) checkpointsBetweenCount = 1;

        GUILayout.Label("First selected checkpoint is the start point, second selected is the end point.", EditorStyles.label);

        if (GUILayout.Button("Spawn checkpoint between two selected checkpoints"))
        {
            GameObject[] selected = Selection.gameObjects;

            if (selected.Length == 2)
            {
                GameObject start = selected[0];
                GameObject end = selected[1];

                CheckpointScript script0 = start.GetComponent<CheckpointScript>();
                CheckpointScript script1 = end.GetComponent<CheckpointScript>();

                if (script0 && script1)
                {
                    script0.nextCheckpoints.Remove(end.transform);

                    CheckpointScript previousScript = script0;

                    for (int i = 1; i <= checkpointsBetweenCount; i++)
                    {
                        float t = (float)i / (checkpointsBetweenCount + 1);
                        Vector3 pos = Vector3.Lerp(start.transform.position, end.transform.position, t);

                        CheckpointScript middleScript = new CheckpointScript();
                        GameObject checkpoint = spawnCheckpoint(pos, Vector3.one, start.transform.parent, ref middleScript);

                        previousScript.nextCheckpoints.Add(checkpoint.transform);
                        previousScript = middleScript;
                    }

                    previousScript.nextCheckpoints.Add(end.transform);

                    EditorUtility.SetDirty(script0);
                    EditorSceneManager.MarkSceneDirty(start.scene);
                }
            }
        }

        if (GUILayout.Button("Remove checkpoints between two selected checkpoints"))
        {
            GameObject[] selected = Selection.gameObjects;

            if (selected.Length == 2)
            {
                GameObject start = selected[0];
                GameObject end = selected[1];

                CheckpointScript script0 = start.GetComponent<CheckpointScript>();
                CheckpointScript script1 = end.GetComponent<CheckpointScript>();

                if (script0 && script1)
                {
                    List<GameObject> chain = FindLinearChain(start, end);

                    if (chain != null)
                    {
                        for (int i = 0; i < chain.Count; i++)
                        {
                            DestroyImmediate(chain[i]);
                        }

                        script0.nextCheckpoints.RemoveAll(t => t == null);

                        if (!isElementInList(script0.nextCheckpoints, end.transform))
                        {
                            script0.nextCheckpoints.Add(end.transform);
                        }

                        EditorUtility.SetDirty(script0);
                        EditorSceneManager.MarkSceneDirty(start.scene);
                    }
                    else
                    {
                        Debug.LogWarning("[CarAIEditorScript] No linear checkpoint chain found between the selected checkpoints (start to end must be a single unbranched path).");
                    }
                }
            }
        }
    }

    private List<GameObject> FindLinearChain(GameObject start, GameObject end)
    {
        List<GameObject> chain = new List<GameObject>();
        CheckpointScript current = start.GetComponent<CheckpointScript>();

        int safety = 0;
        while (safety < 10000)
        {
            safety++;

            if (current.nextCheckpoints.Count != 1)
                return null;

            Transform nextTransform = current.nextCheckpoints[0];
            if (nextTransform == null)
                return null;

            if (nextTransform.gameObject == end)
                return chain;

            chain.Add(nextTransform.gameObject);

            current = nextTransform.GetComponent<CheckpointScript>();
            if (current == null)
                return null;
        }

        return null;
    }

    private bool isElementInList(List<Transform> list, Transform element)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == element)
            {
                return true;
            }
        }

        return false;
    }

    void CreateIntersection()
    {
        EditorGUILayout.BeginHorizontal();

        GUILayout.Label("Number of stops", EditorStyles.boldLabel);
        stops = EditorGUILayout.IntField(stops);

        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Spawn intersection") && stops != 0)
        {
            GameObject intersection = new GameObject("Intersection");

            IntersectionScript intersectionScript = intersection.AddComponent<IntersectionScript>();

            for (int i = 1; i <= stops; i++)
            {
                GameObject stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stop.name = "Stop" + i.ToString();
                stop.transform.SetParent(intersection.transform);
                stop.GetComponent<BoxCollider>().isTrigger = true;

                StopScript stopScript = stop.AddComponent<StopScript>();
                stopScript.stop = true;

                intersectionScript.stops.Add(stop);

            }

        }

    }

    public static void ConnectSelectedCheckpoints()
    {
        GameObject[] selected = Selection.gameObjects;

        bool canConnect = true;

        for (int i = 0; i + 1 < selected.Length; i++)
        {
            CheckpointScript script = selected[i].GetComponent<CheckpointScript>();
            if (!script || isAlreadyConnected(script.nextCheckpoints, selected[i + 1].transform))
            {
                canConnect = false;
                break;
            }
        }

        if (canConnect)
        {
            for (int i = 0; i + 1 < selected.Length; i++)
            {
                CheckpointScript script = selected[i].GetComponent<CheckpointScript>();
                script.nextCheckpoints.Add(selected[i + 1].transform);
                EditorUtility.SetDirty(script);
                EditorSceneManager.MarkSceneDirty(selected[i].scene);
            }

            Debug.Log($"[CarAIEditorScript] Connected {selected.Length} selected checkpoint(s) in sequence.");
        }
        else
        {
            Debug.LogWarning("[CarAIEditorScript] Could not connect selected checkpoints — one is missing a CheckpointScript, or the connection already exists.");
        }
    }

    static bool isAlreadyConnected(List<Transform> nextCheckpoints, Transform checkpoint)
    {
        bool result = false;

        for (int i = 0; i < nextCheckpoints.Count; i++)
        {
            if (nextCheckpoints[i] == checkpoint)
            {
                result = true;
                break;
            }
        }

        return result;
    }

    private GameObject spawnCheckpoint(Vector3 pos, Vector3 scale, Transform parent, ref CheckpointScript script)
    {
        GameObject checkpoint = GameObject.CreatePrimitive(PrimitiveType.Cube);
        checkpoint.transform.position = pos;
        checkpoint.transform.localScale = scale;
        checkpoint.name = "Checkpoint_" + count.ToString();
        checkpoint.GetComponent<BoxCollider>().isTrigger = true;
        count++;

        if (parent != null)
        {
            checkpoint.transform.SetParent(parent);
        }

        script = checkpoint.AddComponent<CheckpointScript>();
        script.speedLimit = -1;

        return checkpoint;
    }

}