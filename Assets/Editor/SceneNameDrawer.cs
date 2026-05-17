using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomPropertyDrawer(typeof(SceneNameAttribute))]
public class SceneNameDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        //Ensure the property is a string
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "Use [SceneName] with string.");
            return;
        }

        // Get all scene names from Build Settings
        int sceneCount = SceneManager.sceneCountInBuildSettings;
        string[] sceneNames = new string[sceneCount];
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            sceneNames[i] = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        int currentIndex = System.Array.IndexOf(sceneNames, property.stringValue);
        if (currentIndex == -1) currentIndex = 0;

        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, sceneNames);
        property.stringValue = sceneNames[newIndex];
    }
}