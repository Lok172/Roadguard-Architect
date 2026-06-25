using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────────
//  PAUSE MENU CONTROLLER
//
//  Place on the gear/settings icon's GameObject (or any object that
//  can see both the settings button and the Pause Panel).
//
//  • Pause Panel starts INACTIVE.
//  • Clicking the settings icon activates it and sets Time.timeScale = 0,
//    which freezes everything driven by Time.deltaTime / WaitForSeconds:
//    car movement, crash spawning, the day-tick (capital / happiness /
//    accident rate stop counting).
//  • Clicking Resume (or the settings icon again, if wired) deactivates
//    the panel and restores Time.timeScale = 1.
//
//  FIX (Req 3): SFX (including the car driving loop) are muted while
//  the pause panel is open via LevelAudioManager.SetSFXPaused. BGM
//  continues unaffected.
//
//  FIX (Req 4): On open, MusicManager.HookPausePanel is called so that
//  every Button and volume Slider inside the panel gets a click sound
//  and the sliders drive the correct volume + percentage labels.
//
//  Wiring options:
//    A) Drag settingsButton / resumeButton into the Inspector slots —
//       this script wires their OnClick automatically in Awake().
//    B) Leave them empty and instead hook a button's OnClick() (in the
//       Inspector) directly to PauseMenuController.OpenPause() /
//       ClosePause() — useful if the settings icon uses ClickProxy
//       instead of a Button.
// ─────────────────────────────────────────────────────────────────

public class PauseMenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional — the gear/settings icon Button that opens the pause panel.")]
    [SerializeField] private Button settingsButton;

    [Tooltip("The Pause Panel GameObject. Inactive by default.")]
    [SerializeField] private GameObject pausePanel;

    [Tooltip("Optional — a Resume/Close Button inside the Pause Panel.")]
    [SerializeField] private Button resumeButton;

    [Tooltip("Optional — a Restart Button inside the Pause Panel. Reloads the current level.")]
    [SerializeField] private Button restartButton;

    public bool IsPaused { get; private set; }

    /// <summary>
    /// Static accessor so CameraManager (or any drag script) can check
    /// pause state without needing a direct reference.
    /// Usage: if (PauseMenuController.GameIsPaused) return;
    /// </summary>
    public static bool GameIsPaused { get; private set; }

    private void Awake()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(OpenPause);

        if (resumeButton != null)
            resumeButton.onClick.AddListener(ClosePause);

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartLevel);
    }

    /// <summary>Shows the pause panel and freezes the game.</summary>
    public void OpenPause()
    {
        if (pausePanel != null)
            pausePanel.SetActive(true);

        Time.timeScale = 0f;
        IsPaused = true;
        GameIsPaused = true;

        LevelAudioManager.Instance?.SetSFXPaused(true);

        if (pausePanel != null && MusicManager.Instance != null)
            MusicManager.Instance.HookPausePanel(pausePanel);
    }

    /// <summary>Hides the pause panel and resumes the game.</summary>
    public void ClosePause()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);

        // Safety: don't un-freeze a level that already ended (Game Over /
        // Victory) while the pause panel happened to be open.
        if (GameManager.Instance == null || GameManager.Instance.GameRunning)
            Time.timeScale = 1f;

        IsPaused = false;
        GameIsPaused = false;

        LevelAudioManager.Instance?.SetSFXPaused(false);
    }

    public void TogglePause()
    {
        if (IsPaused) ClosePause();
        else OpenPause();
    }

    /// <summary>
    /// Fully reloads the current level scene via PageManager so that all
    /// placed devices, cars, road state, and UI are rebuilt from scratch —
    /// not just the GameManager numbers.
    ///
    /// Flow:
    ///   1. Restore Time.timeScale and un-mute SFX (ClosePause).
    ///   2. Ask PageManager to switch to the current level scene by name,
    ///      which unloads it and reloads it with all its additional scenes
    ///      (City, LvUI & Manager, etc.) exactly as when the player first
    ///      entered the level from LevelSelect.
    ///   3. Fallback: if PageManager is not present, use SceneManager
    ///      directly (single-scene builds / editor testing).
    /// </summary>
    public void RestartLevel()
    {
        // Must restore time BEFORE the scene switch so coroutines in the
        // new scene start with timeScale = 1.
        ClosePause();

        // Determine the current level scene name from GameManager.
        string levelSceneName = null;
        if (GameManager.Instance != null)
            levelSceneName = LevelSceneNameFor(GameManager.Instance.currentLevel);

        // Prefer PageManager so the full scene group (City, LvUI & Manager …)
        // is unloaded and reloaded cleanly.
        if (PageManager.Instance != null && !string.IsNullOrEmpty(levelSceneName))
        {
            // Force PageManager to treat this as a fresh load even if
            // currentLoadedUI already equals levelSceneName (same scene restart).
            PageManager.Instance.ForceChangeUI(levelSceneName);
        }
        else if (!string.IsNullOrEmpty(levelSceneName) && PageManager.Instance != null)
        {
            PageManager.Instance.ChangeUI(levelSceneName);
        }
        else
        {
            // Fallback for single-scene / editor setups without PageManager.
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }

    /// <summary>
    /// Maps a GameManager level number to the scene name used in PageManager.
    /// Edit this if your scene naming convention differs (e.g. "Level1", "LV_01").
    /// </summary>
    private static string LevelSceneNameFor(int level) => $"LV{level}";

    private void OnDestroy()
    {
        // Safety: never leave the engine frozen if this object goes away mid-pause
        // (e.g. scene unload while paused).
        if (IsPaused)
        {
            Time.timeScale = 1f;
            GameIsPaused = false;
            LevelAudioManager.Instance?.SetSFXPaused(false);
        }
    }
}