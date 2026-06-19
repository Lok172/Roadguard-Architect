using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  LEVEL AUDIO MANAGER
//
//  Place this on any GameObject inside each Level scene (Level 1, 2, 3).
//
//  The "2-D vector" is modelled as a List<LevelSoundEntry> where each entry
//  holds:
//    • One AudioClip
//    • A List of GameObjects (Buttons / ClickProxy objects) that trigger it
//
//  Preset slots are labelled in the Inspector for:
//    Game Start · Car Driving (loop) · Car Accident · Failed to Put Device
//    Success to Put Device · Game Over · Win Game
//
//  Call the named Play* methods from your game logic, or wire GameObjects in
//  the inspector so clicks auto-trigger the sound.
//
//  All playback goes through MusicManager.PlaySFX so master & sfx volume
//  sliders are respected automatically.
// ─────────────────────────────────────────────────────────────────────────────

public class LevelAudioManager : MonoBehaviour
{
    // =========================================================================
    //  Data class — one sound → many clickable objects
    // =========================================================================

    [System.Serializable]
    public class LevelSoundEntry
    {
        [Tooltip("Descriptive label shown in the Inspector (e.g. 'Car Accident').")]
        public string label;

        [Tooltip("The audio clip to play.")]
        public AudioClip clip;

        [Tooltip("Should this clip loop? (e.g. Car Driving ambient)")]
        public bool loop = false;

        [Tooltip("GameObjects whose Button / ClickProxy click triggers this sound.")]
        public List<GameObject> clickTargets = new List<GameObject>();
    }

    // =========================================================================
    //  Inspector — preset named entries + free-form extras
    // =========================================================================

    [Header("── Preset Level Sounds ─────────────────────────────")]

    [Tooltip("Played once when the level begins (Game Start).")]
    [SerializeField] private LevelSoundEntry gameStartSound    = new LevelSoundEntry { label = "Game Start" };

    [Tooltip("Looping ambient car driving sound.")]
    [SerializeField] private LevelSoundEntry carDrivingSound   = new LevelSoundEntry { label = "Car Driving", loop = true };

    [Tooltip("One-shot played on a car accident event.")]
    [SerializeField] private LevelSoundEntry carAccidentSound  = new LevelSoundEntry { label = "Car Accident" };

    [Tooltip("Played when the player fails to place a device.")]
    [SerializeField] private LevelSoundEntry failedPlaceSound  = new LevelSoundEntry { label = "Failed to Put Device" };

    [Tooltip("Played when the player successfully places a device.")]
    [SerializeField] private LevelSoundEntry successPlaceSound = new LevelSoundEntry { label = "Success to Put Device" };

    [Tooltip("Played on game over.")]
    [SerializeField] private LevelSoundEntry gameOverSound     = new LevelSoundEntry { label = "Game Over" };

    [Tooltip("Played when the player wins.")]
    [SerializeField] private LevelSoundEntry winGameSound      = new LevelSoundEntry { label = "Win Game" };

    [Header("── Extra Sound Entries (optional) ───────────────────")]
    [Tooltip("Add any additional clip → clickable-object mappings here.")]
    [SerializeField] private List<LevelSoundEntry> extraSounds = new List<LevelSoundEntry>();

    // ─────────────────────────────────────────────────────────────────────────
    //  Private state — dedicated AudioSource for looping car driving sound
    // ─────────────────────────────────────────────────────────────────────────
    private AudioSource _loopSource;   // used only for carDrivingSound (or other loop entries)

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    private void Awake()
    {
        // Dedicated looping source so it can be stopped independently
        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.playOnAwake = false;
        _loopSource.loop = true;
    }

    private void Start()
    {
        // Wire all inspector-assigned click targets
        WireAllEntries();
    }

    // =========================================================================
    //  Wire click targets
    // =========================================================================

    private void WireAllEntries()
    {
        List<LevelSoundEntry> all = AllEntries();
        foreach (LevelSoundEntry entry in all)
        {
            LevelSoundEntry captured = entry;   // capture for lambda
            foreach (GameObject go in entry.clickTargets)
            {
                if (go == null) continue;

                // ── ClickProxy (Navigation system) ──────────────────────────
                ClickProxy proxy = go.GetComponent<ClickProxy>();
                if (proxy != null)
                {
                    System.Action existing = proxy.GetCurrentAction();
                    proxy.Setup(() =>
                    {
                        PlayEntry(captured);
                        existing?.Invoke();
                    });
                    continue;
                }

                // ── Standard Unity Button ────────────────────────────────────
                Button btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => PlayEntry(captured));
                }
            }
        }
    }

    // =========================================================================
    //  Named public API — call from GameManager / event system
    // =========================================================================

    /// <summary>Call when the level officially starts (after intro, countdown, etc.).</summary>
    public void PlayGameStart()     => PlayEntry(gameStartSound);

    /// <summary>Begin looping the car-driving ambient sound.</summary>
    public void PlayCarDriving()    => PlayLoopEntry(carDrivingSound);

    /// <summary>Stop the car-driving ambient loop.</summary>
    public void StopCarDriving()    => StopLoop();

    /// <summary>One-shot car accident sound.</summary>
    public void PlayCarAccident()   => PlayEntry(carAccidentSound);

    /// <summary>Played when device placement fails.</summary>
    public void PlayFailedPlace()   => PlayEntry(failedPlaceSound);

    /// <summary>Played when device placement succeeds.</summary>
    public void PlaySuccessPlace()  => PlayEntry(successPlaceSound);

    /// <summary>Played on game over screen.</summary>
    public void PlayGameOver()
    {
        StopLoop();             // stop car driving before game over sting
        PlayEntry(gameOverSound);
    }

    /// <summary>Played on win screen.</summary>
    public void PlayWinGame()
    {
        StopLoop();
        PlayEntry(winGameSound);
    }

    // =========================================================================
    //  Internal play helpers
    // =========================================================================

    /// <summary>Play a one-shot (or kick-start a loop entry) via MusicManager.</summary>
    private void PlayEntry(LevelSoundEntry entry)
    {
        if (entry == null || entry.clip == null) return;

        if (entry.loop)
        {
            PlayLoopEntry(entry);
        }
        else
        {
            if (MusicManager.Instance != null)
                MusicManager.Instance.PlaySFX(entry.clip);
            else
                PlayFallback(entry.clip, false);
        }
    }

    private void PlayLoopEntry(LevelSoundEntry entry)
    {
        if (entry == null || entry.clip == null) return;
        if (_loopSource.clip == entry.clip && _loopSource.isPlaying) return;

        float vol = MusicManager.Instance != null
            ? MusicManager.Instance.MasterVolume * MusicManager.Instance.SFXVolume
            : 1f;

        _loopSource.clip   = entry.clip;
        _loopSource.volume = vol;
        _loopSource.Play();
    }

    private void StopLoop()
    {
        if (_loopSource.isPlaying)
            _loopSource.Stop();
    }

    /// <summary>Fallback if MusicManager is absent (edge case / testing).</summary>
    private void PlayFallback(AudioClip clip, bool loop)
    {
        if (!loop)
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private List<LevelSoundEntry> AllEntries()
    {
        var list = new List<LevelSoundEntry>
        {
            gameStartSound, carDrivingSound, carAccidentSound,
            failedPlaceSound, successPlaceSound, gameOverSound, winGameSound
        };
        list.AddRange(extraSounds);
        return list;
    }
}
