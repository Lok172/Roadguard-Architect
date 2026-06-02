using UnityEngine;

public class CameraZoomController : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float minZoom = 15f;
    [SerializeField] private float maxZoom = 40f;

    [Header("Default View")]
    [SerializeField] private Vector3 defaultPosition;
    [SerializeField] private Vector3 defaultRotation;

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        defaultPosition = targetCamera.transform.position;
        defaultRotation = targetCamera.transform.eulerAngles;
    }

    private void Update()
    {
        HandleZoom();
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) < 0.01f)
            return;

        // Orthographic Camera
        if (targetCamera.orthographic)
        {
            targetCamera.orthographicSize -= scroll * zoomSpeed;

            targetCamera.orthographicSize =
                Mathf.Clamp(
                    targetCamera.orthographicSize,
                    minZoom,
                    maxZoom
                );

            // Fully zoomed out
            if (Mathf.Approximately(
                targetCamera.orthographicSize,
                maxZoom))
            {
                ResetView();
            }
        }
        else
        {
            // Perspective Camera
            targetCamera.fieldOfView -= scroll * zoomSpeed;

            targetCamera.fieldOfView =
                Mathf.Clamp(
                    targetCamera.fieldOfView,
                    minZoom,
                    maxZoom
                );

            if (Mathf.Approximately(
                targetCamera.fieldOfView,
                maxZoom))
            {
                ResetView();
            }
        }
    }

    private void ResetView()
    {
        targetCamera.transform.position = defaultPosition;
        targetCamera.transform.rotation =
            Quaternion.Euler(defaultRotation);
    }
}