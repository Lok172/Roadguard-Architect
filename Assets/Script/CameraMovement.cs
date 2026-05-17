using UnityEngine;

public class CameraRotate : MonoBehaviour
{
    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private float minLocation = 180f;
    [SerializeField] private float maxLocation = 270f;

    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Camera cameraDisplay;

    private int direction = 1;

    private void Start()
    {
        cameraDisplay = FindObjectOfType<Camera>();

        if (cameraDisplay != null && cameraTransform == null)
        {
            cameraTransform = cameraDisplay.transform;
        }
    }

    private void Update()
    {
        float movement = movementSpeed * Time.deltaTime * direction;

        Vector3 pos = cameraTransform.position;
        pos.x += movement;

        if (pos.x >= maxLocation || pos.x <= minLocation)
        {
            direction *= -1;
        }

        cameraTransform.position = pos;
    }
}