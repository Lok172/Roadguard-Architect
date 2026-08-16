using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

[CustomEditor(typeof(CheckpointScript))]
[CanEditMultipleObjects]
public class FastCheckpointSpawnerEditor : Editor
{
    private CheckpointScript lastScript;
    private GameObject parent;
    private int count = 0;

    public override void OnInspectorGUI()
    {
        GUILayout.Label("E - spawn checkpoint at mouse position.", EditorStyles.label);
        GUILayout.Label("Gizmos must be turned on for this to work.", EditorStyles.label);
        GUILayout.Label("C - with two checkpoints selected, connects the first as the source and the second as its next checkpoint.", EditorStyles.label);
        GUILayout.Label("", EditorStyles.label);

        base.OnInspectorGUI();
    }

    void OnSceneGUI()
    {
        if (lastScript == null)
        {
            GameObject[] objects = Selection.gameObjects;

            for (int i = 0; i < objects.Length; i++)
            {
                CheckpointScript script = objects[i].GetComponent<CheckpointScript>();
                if (script)
                {
                    lastScript = script;
                    parent = objects[i];
                }
            }
        }

        Event e = Event.current;

        if (e.type == EventType.KeyUp)
        {
            Debug.Log($"[FastCheckpointSpawnerEditor] KeyUp received: {e.keyCode}, selection count: {Selection.gameObjects.Length}");
        }

        //Spawn checkpoint
        if (e.type == EventType.KeyUp && e.keyCode == KeyCode.R)
        {
            if (parent == null)
            {
                parent = new GameObject("Checkpoints");
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            RaycastHit hit;
            Physics.Raycast(ray, out hit);

            GameObject checkpoint = GameObject.CreatePrimitive(PrimitiveType.Cube);
            checkpoint.name = "Checkpoint_" + count.ToString();
            count++;
            checkpoint.transform.position = hit.point;
            checkpoint.transform.localScale = Vector3.one;
            checkpoint.GetComponent<BoxCollider>().isTrigger = true;
            CheckpointScript script = checkpoint.AddComponent<CheckpointScript>();
            script.speedLimit = -1;
            if (lastScript != null)
            {
                lastScript.nextCheckpoints.Add(checkpoint.transform);
                EditorUtility.SetDirty(lastScript);
            }

            lastScript = script;

            checkpoint.transform.SetParent(parent.transform);
            EditorSceneManager.MarkSceneDirty(checkpoint.scene);
        }

        //Connect two selected checkpoints: first selected is the source, second is its next checkpoint.
        //Reuses the same logic as CarAIEditorScript's "Connect selected checkpoints" button.
        if (e.type == EventType.KeyUp && e.keyCode == KeyCode.C)
        {
            if (Selection.gameObjects.Length == 2)
            {
                CarAIEditorScript.ConnectSelectedCheckpoints();
                e.Use();
            }
        }
    }

}