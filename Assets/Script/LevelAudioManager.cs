using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// This script is used to play the level's sound effects and its looping ambient car-driving
// sound, and wires each configured entry to its clickable UI targets.
public class LevelAudioManager : MonoBehaviour
{
    public static LevelAudioManager Instance { get; private set; }

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

    [Header("Preset Level Sounds")]

    [Tooltip("Played once when the level begins (Game Start).")]
    [SerializeField] private LevelSoundEntry gameStartSound = new LevelSoundEntry { label = "Game Start" };

    [Tooltip("Looping ambient car driving sound.")]
    [SerializeField] private LevelSoundEntry carDrivingSound = new LevelSoundEntry { label = "Car Driving", loop = true };

    [Tooltip("One-shot played on a car accident event.")]
    [SerializeField] private LevelSoundEntry carAccidentSound = new LevelSoundEntry { label = "Car Accident" };

    [Tooltip("Played when the player fails to place a device.")]
    [SerializeField] private LevelSoundEntry failedPlaceSound = new LevelSoundEntry { label = "Failed to Put Device" };

    [Tooltip("Played when the player successfully places a device.")]
    [SerializeField] private LevelSoundEntry successPlaceSound = new LevelSoundEntry { label = "Success to Put Device" };

    [Tooltip("Played when a device is placed correctly but in a suboptimal/incorrect spot.")]
    [SerializeField] private LevelSoundEntry poorPlaceSound = new LevelSoundEntry { label = "Poor Placement" };

    [Tooltip("Played on game over.")]
    [SerializeField] private LevelSoundEntry gameOverSound = new LevelSoundEntry { label = "Game Over" };

    [Tooltip("Played when the player wins.")]
    [SerializeField] private LevelSoundEntry winGameSound = new LevelSoundEntry { label = "Win Game" };

    [Header("Extra Sound Entries (optional)")]
    [Tooltip("Add any additional clip → clickable-object mappings here.")]
    [SerializeField] private List<LevelSoundEntry> extraSounds = new List<LevelSoundEntry>();

    private AudioSource _loopSource;

    private void Awake()
    {
        Instance = this;

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.playOnAwake = false;
        _loopSource.loop = true;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        WireAllEntries();
    }

    private void WireAllEntries()
    {
        List<LevelSoundEntry> all = AllEntries();
        foreach (LevelSoundEntry entry in all)
        {
            LevelSoundEntry captured = entry;
            foreach (GameObject go in entry.clickTargets)
            {
                if (go == null) continue;

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

                Button btn = go.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => PlayEntry(captured));
            }
        }
    }

    // Game Start and Car Driving are the only sounds fired automatically at level load, and are
    // deferred by one frame so MusicManager is initialised and its BGM cross-fade has begun
    // before the SFX source is used.

    /// <summary>Plays the level-start sound, deferred by one frame so MusicManager is ready.</summary>
    public void PlayGameStart() => StartCoroutine(PlayGameStartDeferred());

    private IEnumerator PlayGameStartDeferred()
    {
        yield return null;
        PlayEntry(gameStartSound);
    }

    /// <summary>Begins looping the car-driving ambient sound, deferred by one frame.</summary>
    public void PlayCarDriving() => StartCoroutine(PlayCarDrivingDeferred());

    private IEnumerator PlayCarDrivingDeferred()
    {
        yield return null;
        PlayLoopEntry(carDrivingSound);
    }

    /// <summary>Stops the car-driving ambient loop immediately.</summary>
    public void StopCarDriving() => StopLoop();

    /// <summary>Plays the one-shot car accident sound.</summary>
    public void PlayCarAccident() => PlayEntry(carAccidentSound);

    /// <summary>Plays the failed-device-placement sound.</summary>
    public void PlayFailedPlace() => PlayEntry(failedPlaceSound);

    /// <summary>Plays the successful-device-placement sound.</summary>
    public void PlaySuccessPlace() => PlayEntry(successPlaceSound);

    /// <summary>Plays the poor/suboptimal-placement sound.</summary>
    public void PlayPoorPlacement() => PlayEntry(poorPlaceSound);

    /// <summary>Plays the game-over sound and stops the ambient loop.</summary>
    public void PlayGameOver()
    {
        StopLoop();
        PlayEntry(gameOverSound);
    }

    /// <summary>Plays the win-game sound and stops the ambient loop.</summary>
    public void PlayWinGame()
    {
        StopLoop();
        PlayEntry(winGameSound);
    }

    /// <summary>Mutes or unmutes the looping SFX source without stopping it.</summary>
    public void SetSFXPaused(bool paused)
    {
        _loopSource.mute = paused;
        if (MusicManager.Instance != null)
            MusicManager.Instance.SetSFXMuted(paused);
    }

    private void PlayEntry(LevelSoundEntry entry)
    {
        if (entry == null || entry.clip == null) return;

        if (entry.loop)
            PlayLoopEntry(entry);
        else
        {
            if (MusicManager.Instance != null)
                MusicManager.Instance.PlaySFX(entry.clip);
            else
                PlayFallback(entry.clip);
        }
    }

    private void PlayLoopEntry(LevelSoundEntry entry)
    {
        if (entry == null || entry.clip == null) return;
        if (_loopSource.clip == entry.clip && _loopSource.isPlaying) return;

        float vol = MusicManager.Instance != null
            ? MusicManager.Instance.MasterVolume * MusicManager.Instance.SFXVolume
            : 1f;

        _loopSource.clip = entry.clip;
        _loopSource.volume = vol;
        _loopSource.Play();
    }

    private void StopLoop()
    {
        if (_loopSource.isPlaying)
            _loopSource.Stop();
    }

    private void PlayFallback(AudioClip clip)
    {
        AudioSource.PlayClipAtPoint(clip,
            Camera.main != null ? Camera.main.transform.position : Vector3.zero);
    }

    private List<LevelSoundEntry> AllEntries()
    {
        var list = new List<LevelSoundEntry>
        {
            gameStartSound, carDrivingSound, carAccidentSound,
            failedPlaceSound, successPlaceSound, poorPlaceSound,
            gameOverSound, winGameSound
        };
        list.AddRange(extraSounds);
        return list;
    }
}
