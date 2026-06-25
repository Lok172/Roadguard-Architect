using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────
//  TUTORIAL MANAGER
//
//  Controls a list of tutorial panels with Previous / Next navigation.
//  • Previous button is hidden at index 0.
//  • Next button becomes a "Finish" button at the last panel.
//  • On finish, marks tutorial as read in PlayerPrefs ("TutorialIsRead").
//  • On Start, reads "TutorialIsRead" from PlayerPrefs:
//      true  → show Skip button
//      false → hide Skip button
//
//  SETUP:
//    1. Attach to any GameObject in the Tutorial scene.
//    2. Assign all tutorial panel GameObjects to "Tutorial Panels" list (in order).
//    3. Wire Previous Button, Next Button, Skip Button, Finish Button in Inspector.
//    4. Assign the scene/page name to navigate to when Skip or Finish is pressed.
// ─────────────────────────────────────────────────────────────────

public class TutorialManager : MonoBehaviour
{
    // ── PlayerPrefs Key ──────────────────────────────────────────
    public const string TUTORIAL_IS_READ_KEY = "TutorialIsRead";

    // ── Inspector ────────────────────────────────────────────────

    [Header("Tutorial Panels (in order)")]
    [Tooltip("Drag all tutorial panel GameObjects here in the order they should appear.")]
    public List<GameObject> tutorialPanels = new List<GameObject>();

    [Header("Navigation Buttons")]
    [Tooltip("Button to go to the previous panel. Auto-hidden at index 0.")]
    public Button previousButton;

    [Tooltip("Button to go to the next panel. At the last panel, shows Finish button instead.")]
    public Button nextButton;

    [Tooltip("Skip button — only visible if TutorialIsRead = true (player has seen it before).")]
    public Button skipButton;

    [Header("Navigation Target")]
    [Tooltip("Scene or UI page to navigate to when tutorial ends or is skipped.")]
    [SceneName]
    public string level1SceneName = "Lv1";

    // ── Private State ─────────────────────────────────────────────

    private int _currentIndex = 0;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        SetupButtons();
        ApplySkipVisibility();
        ShowPanel(_currentIndex);
    }

    private void SetupButtons()
    {
        previousButton?.onClick.AddListener(OnPrevious);
        nextButton?.onClick.AddListener(OnNext);
        skipButton?.onClick.AddListener(OnSkip);
    }

    // ── Skip Button Visibility ────────────────────────────────────

    /// <summary>
    /// Reads TutorialIsRead from PlayerPrefs.
    /// true  → player has already seen the tutorial → show Skip button.
    /// false → first time → hide Skip button (must go through all panels).
    /// </summary>
    private void ApplySkipVisibility()
    {
        bool isRead = PlayerPrefs.GetInt(TUTORIAL_IS_READ_KEY, 0) == 1;

        if (skipButton != null)
            skipButton.gameObject.SetActive(isRead);

        Debug.Log($"[TutorialManager] TutorialIsRead = {isRead} → Skip button {(isRead ? "shown" : "hidden")}.");
    }

    // ── Panel Navigation ──────────────────────────────────────────

    private void OnPrevious()
    {
        if (_currentIndex > 0)
            ShowPanel(_currentIndex - 1);
    }

    private void OnNext()
    {
        if (_currentIndex < tutorialPanels.Count - 1)
            ShowPanel(_currentIndex + 1);
        else
        {
            // No more panels - mark as read and navigate to Level Select.
            MarkTutorialRead();
            PageManager.Instance.ChangeUI(level1SceneName);
        }
    }

    /// <summary>
    /// Activates the panel at the given index, deactivates all others,
    /// and refreshes button states.
    /// </summary>
    private void ShowPanel(int index)
    {
        if (tutorialPanels == null || tutorialPanels.Count == 0) return;

        _currentIndex = Mathf.Clamp(index, 0, tutorialPanels.Count - 1);

        for (int i = 0; i < tutorialPanels.Count; i++)
        {
            if (tutorialPanels[i] != null)
                tutorialPanels[i].SetActive(i == _currentIndex);
        }

        RefreshNavigationButtons();

        Debug.Log($"[TutorialManager] Showing panel {_currentIndex + 1} / {tutorialPanels.Count}.");
    }

    /// <summary>
    /// Previous: hidden at index 0.
    /// Next: hidden at last panel (Finish button takes over).
    /// Finish: only visible at last panel.
    /// </summary>
    private void RefreshNavigationButtons()
    {
        bool isFirst = _currentIndex == 0;
        bool isLast  = _currentIndex == tutorialPanels.Count - 1;

        if (previousButton != null)
            previousButton.gameObject.SetActive(!isFirst);

        // Next is always visible - on the last panel it redirects instead of advancing.
        if (nextButton != null)
            nextButton.gameObject.SetActive(true);
    }

    // ── Finish / Skip ─────────────────────────────────────────────

    private void OnSkip()
    {
        // Skip doesn't re-mark (already read); just navigate.
        NavigateAway();
    }

    /// <summary>
    /// Saves TutorialIsRead = 1 in PlayerPrefs.
    /// Called when the player reaches the last panel and presses Finish.
    /// </summary>
    private void MarkTutorialRead()
    {
        PlayerPrefs.SetInt(TUTORIAL_IS_READ_KEY, 1);
        PlayerPrefs.Save();
        Debug.Log("[TutorialManager] Tutorial marked as read.");
    }

    private void NavigateAway()
    {
        PageManager.Instance.ChangeUI(level1SceneName);
    }
}
