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

        // FIX Req 3: mute SFX (loop + one-shots) but leave BGM playing.
        LevelAudioManager.Instance?.SetSFXPaused(true);

        // FIX Req 4: wire all Buttons and volume Sliders inside the panel
        // through MusicManager so they have click sounds and the sliders
        // update both the audio volumes and their percentage labels.
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

        // FIX Req 3: restore SFX (loop resumes from where it was muted).
        LevelAudioManager.Instance?.SetSFXPaused(false);
    }

    public void TogglePause()
    {
        if (IsPaused) ClosePause();
        else OpenPause();
    }

    /// <summary>
    /// Restarts (reloads) the current level.
    /// Closes the pause panel, restores time scale, then delegates to
    /// GameManager.InitLevel so all game state is cleanly reset without a
    /// full scene reload — matching the same path used by the Retry button
    /// in the Level Results scene.
    /// Falls back to reloading the active scene if GameManager is absent.
    /// </summary>
    public void RestartLevel()
    {
        // Close the panel and restore time first so the level starts cleanly.
        ClosePause();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.InitLevel(GameManager.Instance.currentLevel);
        }
        else
        {
            // Fallback: reload the active scene directly.
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }

    private void OnDestroy()
    {
        // Safety: never leave the engine frozen if this object goes away mid-pause
        // (e.g. scene unload while paused).
        if (IsPaused)
        {
            Time.timeScale = 1f;
            LevelAudioManager.Instance?.SetSFXPaused(false);
        }
    }
}