using UnityEngine;

public class CameraRotate : MonoBehaviour
{
    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private float minLocation = 180f;
    [SerializeField] private float maxLocation = 270f;

    [SerializeField] private Transform cameraTransform;
    


    private float currentLocation = 0;
    private int direction = 1;

    void Update()
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