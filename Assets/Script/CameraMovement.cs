using UnityEngine;

public class CameraRotate : MonoBehaviour
{
    [Header("Camera Movement Settings")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Camera cameraDisplay;
    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private float minLocationX = 110f;
    [SerializeField] private float maxLocationX = 270f;

    [Header("Camera Start Settings")]
    [SerializeField] private float posX = 270f;
    [SerializeField] private float posY = 70f;
    [SerializeField] private float posZ = 110f;
    [SerializeField] private float rotX = 53f;
    [SerializeField] private float rotY = -90f;
    [SerializeField] private float rotZ = 0f;



    private int direction = 1;

    private void Start()
    {
        if (cameraDisplay == null)
        {
            cameraDisplay = Object.FindFirstObjectByType<Camera>();
            Debug.LogWarning("Camera Display not assigned. Automatically found: " + cameraDisplay.name);
        }

        if (cameraDisplay != null && cameraTransform == null)
        {
            cameraTransform = cameraDisplay.transform;
            cameraTransform.position = new Vector3 (posX, posY, posZ);
            cameraTransform.rotation = Quaternion.Euler(rotX, rotY, rotZ);
            Debug.LogWarning("Camera Transform not assigned. Automatically found: " + cameraTransform.name);
        }
    }

    private void Update()
    {
        float movement = movementSpeed * Time.deltaTime * direction;

        Vector3 pos = cameraTransform.position;
        pos.x += movement;

        if (pos.x >= maxLocationX || pos.x <= minLocationX)
        {
            direction *= -1;
        }

        cameraTransform.position = pos;
    }
}