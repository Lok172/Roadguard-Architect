using System;

/// <summary>
/// Presentation state for the standard-level planning phase. This contains no
/// Unity view references, so it can be tested and reused independently of UI.
/// </summary>
public sealed class PlanningPhaseViewModel
{
    public bool IsVisible { get; private set; }
    public event Action<bool> VisibilityChanged;

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void ConfirmLayout()
    {
        GameManager.Instance?.ConfirmLayout();
    }

    private void SetVisible(bool visible)
    {
        if (IsVisible == visible) return;
        IsVisible = visible;
        VisibilityChanged?.Invoke(visible);
    }
}
