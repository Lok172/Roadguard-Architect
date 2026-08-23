using UnityEngine;

// This script is used to rotate an object each frame so that it continually
// faces the active camera.

public class BillboardSprite : MonoBehaviour
{
    private Camera _cam;

    private void Start()
    {
        _cam = Camera.main;
    }

    private void LateUpdate()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            if (_cam == null) return;
        }

        transform.rotation = Quaternion.LookRotation(
            _cam.transform.forward,
            _cam.transform.up);
    }
}
