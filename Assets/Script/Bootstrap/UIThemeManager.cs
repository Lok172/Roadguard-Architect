using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIThemeManager : MonoBehaviour
{
    [Header("Brightness Multipliers")]
    [SerializeField] private float hoverBrightness = 1.2f;
    [SerializeField] private float pressedBrightness = 0.85f;
    [SerializeField] private float disabledBrightness = 0.5f;

    [Header("Transition Settings")]
    [SerializeField] private float fadeDuration = 0.1f;

    public void ApplyThemeToAllButtons()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Button button in buttons)
        {
            ApplyTheme(button);
        }

        Debug.Log($"Applied theme to {buttons.Length} buttons.");
    }

    private void ApplyTheme(Button button)
    {
        Image targetImage = button.targetGraphic as Image;
        if (targetImage == null) return;

        Color baseColor = targetImage.color;

        ColorBlock colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = MultiplyColor(baseColor, hoverBrightness);
        colors.pressedColor = MultiplyColor(baseColor, pressedBrightness);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = MultiplyColor(baseColor, disabledBrightness);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = fadeDuration;

        button.transition = Selectable.Transition.ColorTint;
        button.colors = colors;
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
}