using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CameraManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Per-scene configuration
    // ─────────────────────────────────────────────────────────────
    [System.Serializable]
    public class SceneConfig
    {
        [Header("Identification")]
        [SceneName]
        public string sceneName;

        [Header("Camera Transform")]
        public Vector3 startPosition = new Vector3(270f, 70f, 110f);
        public Vector3 startRotation = new Vector3(53f, -90f, 0f);

        [Header("Horizontal Pan")]
        public float minLocationX = 110f;
        public float maxLocationX = 270f;
        public float movementSpeed = 5f;

        [Header("Zoom (Optional)")]
        public bool enableZoom = false;
        [Tooltip("Field-of-view or orthographic size when zoomed in (smaller = closer)")]
        public float zoomInValue = 30f;
        [Tooltip("Field-of-view or orthographic size when zoomed out (larger = wider)")]
        public float zoomOutValue = 60f;
        [Tooltip("Seconds to complete one full zoom cycle (out → in → out)")]
        public float zoomCycleDuration = 4f;
    }

    // ─────────────────────────────────────────────────────────────
    //  Inspector fields
    // ─────────────────────────────────────────────────────────────
    [Header("Scene List")]
    [SerializeField] private List<SceneConfig> scenes = new List<SceneConfig>();

    [Header("Camera Reference (auto-found if empty)")]
    [SerializeField] private Camera cameraDisplay;

    // ─────────────────────────────────────────────────────────────
    //  Private state
    // ─────────────────────────────────────────────────────────────
    private Transform cameraTransform;
    private int panDirection = 1;
    private float zoomTimer = 0f;
    private int activeSceneIndex = -1;

    // ─────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────
    private void Start()
    {
        InitialiseCamera();
        DetectAndApplyCurrentUI();
    }

    private void Update()
    {
        if (cameraTransform == null) return;

        DetectAndApplyCurrentUI();

        SceneConfig cfg = ActiveConfig;
        if (cfg == null) return;

        HandlePan(cfg);

        if (cfg.enableZoom)
            HandleZoom(cfg);
    }

    // Reads PageManager.currentLoadedUI and switches config when it changes
    private string lastDetectedUI = null;

    private void DetectAndApplyCurrentUI()
    {
        PageManager pm = Object.FindFirstObjectByType<PageManager>();
        if (pm == null) return;

        string currentUI = pm.currentLoadedUI;
        if (currentUI == lastDetectedUI) return;  // no change

        lastDetectedUI = currentUI;
        SwitchToScene(currentUI);
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Switch to a different scene config at runtime by index.</summary>
    public void SwitchToScene(int index)
    {
        if (index < 0 || index >= scenes.Count)
        {
            Debug.LogWarning($"[CameraManager] Scene index {index} is out of range.");
            return;
        }
        activeSceneIndex = index;
        ApplyScene(index);
    }

    /// <summary>Switch to a scene config at runtime by name (first match).</summary>
    public void SwitchToScene(string sceneName)
    {
        int idx = scenes.FindIndex(s => s.sceneName == sceneName);
        if (idx < 0)
        {
            Debug.LogWarning($"[CameraManager] No scene named '{sceneName}' found.");
            return;
        }
        SwitchToScene(idx);
    }

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────
    private SceneConfig ActiveConfig =>
        (scenes != null && activeSceneIndex >= 0 && activeSceneIndex < scenes.Count)
            ? scenes[activeSceneIndex]
            : null;

    private void InitialiseCamera()
    {
        if (cameraDisplay == null)
        {
            cameraDisplay = Object.FindFirstObjectByType<Camera>();
            if (cameraDisplay != null)
                Debug.LogWarning($"[CameraManager] Camera auto-found: {cameraDisplay.name}");
            else
            {
                Debug.LogError("[CameraManager] No Camera found in scene!");
                return;
            }
        }

        cameraTransform = cameraDisplay.transform;
    }

    private void ApplyScene(int index)
    {
        SceneConfig cfg = ActiveConfig;
        if (cfg == null || cameraTransform == null) return;

        // Reset movement state for the new scene
        panDirection = 1;
        zoomTimer = 0f;

        cameraTransform.position = cfg.startPosition;
        cameraTransform.rotation = Quaternion.Euler(cfg.startRotation);

        // Snap zoom to the zoomed-out default when entering a scene
        if (cfg.enableZoom)
            SetCameraZoom(cameraDisplay, cfg.zoomOutValue);

        Debug.Log($"[CameraManager] Switched to scene: '{cfg.sceneName}'");
    }

    private void HandlePan(SceneConfig cfg)
    {
        float movement = cfg.movementSpeed * Time.deltaTime * panDirection;
        Vector3 pos = cameraTransform.position;
        pos.x += movement;

        if (pos.x >= cfg.maxLocationX || pos.x <= cfg.minLocationX)
            panDirection *= -1;

        cameraTransform.position = pos;
    }

    private void HandleZoom(SceneConfig cfg)
    {
        if (cfg.zoomCycleDuration <= 0f) return;

        zoomTimer += Time.deltaTime;

        // PingPong so the zoom smoothly oscillates between zoomOutValue and zoomInValue
        float t = Mathf.PingPong(zoomTimer / (cfg.zoomCycleDuration * 0.5f), 1f);
        float zoomValue = Mathf.Lerp(cfg.zoomOutValue, cfg.zoomInValue, t);
        SetCameraZoom(cameraDisplay, zoomValue);
    }

    private static void SetCameraZoom(Camera cam, float value)
    {
        if (cam.orthographic)
            cam.orthographicSize = value;
        else
            cam.fieldOfView = value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Custom Inspector  (Editor-only — stripped from builds automatically)
// ─────────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
[CustomEditor(typeof(CameraManager))]
public class CameraManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CameraManager manager = (CameraManager)target;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Runtime Scene Switcher", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to switch scenes at runtime.", MessageType.Info);
            return;
        }

        SerializedProperty scenesProp = serializedObject.FindProperty("scenes");
        for (int i = 0; i < scenesProp.arraySize; i++)
        {
            string name = scenesProp
                .GetArrayElementAtIndex(i)
                .FindPropertyRelative("sceneName")
                .stringValue;

            if (GUILayout.Button($"Switch → {name}"))
                manager.SwitchToScene(name);
        }
    }
}
#endif