using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  BILLBOARD SPRITE
//
//  LateUpdate rotates the object so its XY plane always faces the
//  active camera (perpendicular to the view direction).
// ─────────────────────────────────────────────────────────────────

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

        // Align the sprite plane perpendicular to the camera view.
        transform.rotation = Quaternion.LookRotation(
            _cam.transform.forward,
            _cam.transform.up);
    }
}
