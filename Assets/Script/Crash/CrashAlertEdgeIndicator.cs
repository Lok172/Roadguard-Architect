using UnityEngine;
using UnityEngine.UI;

// Controls one screen-edge bubble indicating an off-screen crash. The bubble
// image (tail baked into the same sprite) rotates to point toward the actual
// off-screen crash location, while the exclamation image stays upright
// regardless of the bubble's rotation. Appearance for both images is applied
// once by CrashAlertIndicatorManager right after the indicator is instantiated.
public class CrashAlertEdgeIndicator : MonoBehaviour
{
    [Tooltip("RectTransform of the bubble Image (the 'Bubble Dialog' sprite, tail included in the art). This is the part that rotates.")]
    public RectTransform bubble;

    [Tooltip("Image component on the same object as 'bubble' — lets its sprite/colour be set independently of the exclamation.")]
    public Image bubbleImage;

    [Tooltip("Image of the exclamation sprite (the 'sign1' asset). A sibling of bubble, not its child, so it stays upright when the bubble rotates.")]
    public Image exclamationImage;

    [Tooltip("Degrees to add to the computed rotation, to compensate for the bubble art's own default tail direction (e.g. if the tail points bottom-left in the source image rather than directly right).")]
    public float tailDirectionOffsetDegrees = 0f;

    /// <summary>
    /// Applies the shared icon look (from CrashAlertIndicatorManager) to this edge indicator's
    /// bubble and exclamation images. Called once, right after this indicator is instantiated.
    /// </summary>
    public void ApplyAppearance(Sprite exclamationIcon, Color exclamationColor, float exclamationSize,
                                 Sprite bubbleIcon, Color bubbleColor, float bubbleSize)
    {
        if (exclamationImage != null)
        {
            if (exclamationIcon != null) exclamationImage.sprite = exclamationIcon;
            exclamationImage.color = exclamationColor;
            exclamationImage.rectTransform.localScale = Vector3.one * exclamationSize;
        }

        if (bubbleImage != null)
        {
            if (bubbleIcon != null) bubbleImage.sprite = bubbleIcon;
            bubbleImage.color = bubbleColor;
        }

        if (bubble != null)
            bubble.localScale = Vector3.one * bubbleSize;
    }

    public void SetBubbleRotation(float angleDegrees)
    {
        if (bubble != null)
            bubble.localRotation = Quaternion.Euler(0f, 0f, angleDegrees + tailDirectionOffsetDegrees);
    }
}