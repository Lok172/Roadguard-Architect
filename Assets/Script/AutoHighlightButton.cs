using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button), typeof(Image))]
public class AutoHighlightButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [SerializeField] private Color baseColor = new Color(0.70f, 0.85f, 1.0f); // pale blue
    [SerializeField] private float hoverBrightness = 1.25f;
    [SerializeField] private float pressedBrightness = 0.85f;

    private Image image;
    private Color hoverColor;
    private Color pressedColor;

    private void Awake()
    {
        image = GetComponent<Image>();

        hoverColor = MultiplyColor(baseColor, hoverBrightness);
        pressedColor = MultiplyColor(baseColor, pressedBrightness);

        image.color = baseColor;
    }

    private Color MultiplyColor(Color c, float multiplier)
    {
        return new Color(
            Mathf.Clamp01(c.r * multiplier),
            Mathf.Clamp01(c.g * multiplier),
            Mathf.Clamp01(c.b * multiplier),
            c.a
        );
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        image.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        image.color = baseColor;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        image.color = pressedColor;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        image.color = hoverColor;
    }
}