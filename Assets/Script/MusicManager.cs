using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  MUSIC MANAGER  (Singleton — place on a DontDestroyOnLoad GameObject)
//
//  Responsibilities
//  ────────────────
//  • Plays the correct BGM for the currently loaded UI / scene (2-D mapping:
//    one BGM entry → many scene names).
//  • Auto-attaches a button-click sound to every ClickProxy / Button in the
//    scenes listed under each ButtonSoundEntry.
//  • Exposes SetMasterVolume / SetMusicVolume / SetSFXVolume so your Settings
//    sliders (and the Pause Panel) can drive all three AudioSources at once.
//  • Persists volume prefs via PlayerPrefs.
//
//  AudioSource layout (auto-created if missing)
//  ─────────────────────────────────────────────
//    _bgmSource   – looping background music   (affected by master + music)
//    _sfxSource   – one-shot SFX               (affected by master + sfx)
//    _sliderSource– plays while slider is held  (affected by master + sfx)
// ─────────────────────────────────────────────────────────────────────────────

public class MusicManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static MusicManager Instance { get; private set; }

    // =========================================================================
    //  Inspector-exposed data classes
    // =========================================================================

    /// <summary>One BGM clip that plays across one or more named scenes.</summary>
    [System.Serializable]
    public class BGMEntry
    {
        [Tooltip("The background music clip.")]
        public AudioClip clip;

        [SceneName]
        [Tooltip("Scene names (from Build Settings) that should play this BGM.")]
        public List<string> sceneNames = new List<string>();
    }

    /// <summary>One button-click sound applied to clickable objects in one or more scenes.</summary>
    [System.Serializable]
    public class ButtonSoundEntry
    {
        [Tooltip("The sound to play when any Button / ClickProxy in the listed scenes is clicked.")]
        public AudioClip clip;

        [SceneName]
        [Tooltip("Scene names where this button sound will be auto-attached.")]
        public List<string> sceneNames = new List<string>();
    }

    // =========================================================================
    //  Inspector Fields
    // =========================================================================

    [Header("BGM Entries  (clip → scenes)")]
    [SerializeField] private List<BGMEntry> bgmEntries = new List<BGMEntry>();

    [Header("Button Sound Entries  (clip → scenes)")]
    [SerializeField] private List<ButtonSoundEntry> buttonSoundEntries = new List<ButtonSoundEntry>();

    [Header("Slider Sound")]
    [Tooltip("Sound to play while the user drags a volume slider.")]
    [SerializeField] private AudioClip sliderClip;

    [SceneName]
    [Tooltip("Scene names where the slider sound should be attached (Settings, Pause panel scenes).")]
    public List<string> sliderSceneNames = new List<string>();

    [Header("Volume Settings Sliders")]
    [Tooltip("Slider that controls Master volume.")]
    [SerializeField] private Slider masterSlider;

    [Tooltip("Slider that controls Music volume.")]
    [SerializeField] private Slider musicSlider;

    [Tooltip("Slider that controls SFX volume.")]
    [SerializeField] private Slider sfxSlider;

    [Header("Volume Percentage Labels")]
    [Tooltip("Text that shows the Master volume as a percentage, e.g. '75%'.")]
    [SerializeField] private TextMeshProUGUI masterPercentText;

    [Tooltip("Text that shows the Music volume as a percentage, e.g. '75%'.")]
    [SerializeField] private TextMeshProUGUI musicPercentText;

    [Tooltip("Text that shows the SFX volume as a percentage, e.g. '75%'.")]
    [SerializeField] private TextMeshProUGUI sfxPercentText;

    [Header("BGM Transition")]
    [SerializeField] private float crossFadeDuration = 0.5f;

    // =========================================================================
    //  Private state
    // =========================================================================

    private AudioSource _bgmSource;
    private AudioSource _sfxSource;
    private AudioSource _sliderSource;

    private float _masterVolume = 1f;
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;

    private string _currentScene = null;
    private AudioClip _currentBGM = null;

    private Coroutine _fadeCoroutine;

    // PlayerPrefs keys
    private const string PREF_MASTER = "Vol_Master";
    private const string PREF_MUSIC = "Vol_Music";
    private const string PREF_SFX = "Vol_SFX";

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        CreateAudioSources();
        LoadVolumes();
    }

    private void OnEnable() => StartCoroutine(PollPageManager());
    private void OnDisable() => StopAllCoroutines();

    // =========================================================================
    //  PageManager polling — mirrors CameraManager's detection pattern
    // =========================================================================

    private IEnumerator PollPageManager()
    {
        // FIX (Issue 5): skip the very first frame so that GameManager.Start()
        // and LevelAudioManager deferred coroutines have already run before we
        // detect the initial scene and kick off a BGM crossfade.  Without this
        // skip, PollPageManager fired on frame 0, started CrossFadeBGM, which
        // reset _sfxSource state mid-init — causing the game-start SFX to play
        // into an uninitialised AudioSource and then silently disappear.
        yield return null;

        while (true)
        {
            PageManager pm = Object.FindFirstObjectByType<PageManager>();
            if (pm != null)
            {
                string ui = pm.currentLoadedUI;
                if (ui != _currentScene)
                {
                    _currentScene = ui;
                    OnSceneChanged(ui);
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    private void OnSceneChanged(string sceneName)
    {
        TryPlayBGMForScene(sceneName);
        AttachButtonSoundsForScene(sceneName);
        AttachSliderSoundsForScene(sceneName);
        HookVolumeSliders(sceneName);
    }

    // =========================================================================
    //  BGM
    // =========================================================================

    private void TryPlayBGMForScene(string sceneName)
    {
        AudioClip target = null;
        foreach (BGMEntry entry in bgmEntries)
        {
            if (entry.sceneNames.Contains(sceneName))
            {
                target = entry.clip;
                break;
            }
        }

        // Same clip already playing — do nothing
        if (target == _currentBGM) return;

        _currentBGM = target;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(CrossFadeBGM(target));
    }

    private IEnumerator CrossFadeBGM(AudioClip nextClip)
    {
        // Fade out
        float startVol = _bgmSource.volume;
        for (float t = 0; t < crossFadeDuration; t += Time.unscaledDeltaTime)
        {
            _bgmSource.volume = Mathf.Lerp(startVol, 0f, t / crossFadeDuration);
            yield return null;
        }
        _bgmSource.Stop();

        if (nextClip == null) { _fadeCoroutine = null; yield break; }

        // Swap clip & fade in
        _bgmSource.clip = nextClip;
        _bgmSource.Play();
        float targetVol = _masterVolume * _musicVolume;
        for (float t = 0; t < crossFadeDuration; t += Time.unscaledDeltaTime)
        {
            _bgmSource.volume = Mathf.Lerp(0f, targetVol, t / crossFadeDuration);
            yield return null;
        }
        _bgmSource.volume = targetVol;
        _fadeCoroutine = null;
    }

    // =========================================================================
    //  Button sounds — auto-attach to ClickProxy & Button components
    // =========================================================================

    private void AttachButtonSoundsForScene(string sceneName)
    {
        foreach (ButtonSoundEntry entry in buttonSoundEntries)
        {
            if (!entry.sceneNames.Contains(sceneName) || entry.clip == null) continue;
            AttachClipToAllClickables(entry.clip);
        }
    }

    /// <summary>
    /// Finds every ClickProxy and Button in the scene and hooks PlayButtonSound
    /// unless they already have it registered (prevents duplicates on re-entry).
    /// </summary>
    private void AttachClipToAllClickables(AudioClip clip)
    {
        // ClickProxy (Navigation system)
        foreach (ClickProxy proxy in Object.FindObjectsByType<ClickProxy>(FindObjectsSortMode.None))
        {
            AudioClip captured = clip;
            // Re-wrap so each proxy stores a fresh reference; Setup replaces the action.
            // We piggy-back by wrapping the existing action.
            System.Action existing = proxy.GetCurrentAction();
            if (existing == null) continue;                 // not yet set up

            proxy.Setup(() =>
            {
                PlayButtonSound(captured);
                existing.Invoke();
            });
        }

        // Standard Unity Buttons
        foreach (Button btn in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            AudioClip captured = clip;
            // Remove previous MusicManager listener to avoid stacking
            btn.onClick.RemoveListener(() => PlayButtonSound(captured));
            btn.onClick.AddListener(() => PlayButtonSound(captured));
        }

        // TMP_Dropdown — play button sound when the value changes (item selected)
        foreach (TMPro.TMP_Dropdown dropdown in Object.FindObjectsByType<TMPro.TMP_Dropdown>(FindObjectsSortMode.None))
        {
            AudioClip captured = clip;
            dropdown.onValueChanged.RemoveListener(_ => PlayButtonSound(captured));
            dropdown.onValueChanged.AddListener(_ => PlayButtonSound(captured));
        }
    }

    // =========================================================================
    //  Slider sound
    // =========================================================================

    private void AttachSliderSoundsForScene(string sceneName)
    {
        if (sliderClip == null || !sliderSceneNames.Contains(sceneName)) return;

        foreach (Slider slider in Object.FindObjectsByType<Slider>(FindObjectsSortMode.None))
        {
            // Skip the volume control sliders themselves if desired — remove this guard to include them
            Slider captured = slider;
            slider.onValueChanged.RemoveListener(_ => PlaySliderSound());
            slider.onValueChanged.AddListener(_ => PlaySliderSound());
        }
    }

    // =========================================================================
    //  Volume sliders — hook master / music / sfx in Settings & Pause
    // =========================================================================

    private void HookVolumeSliders(string sceneName)
    {
        // Try to find sliders in scene if inspector references are empty
        if (masterSlider == null || musicSlider == null || sfxSlider == null)
        {
            Slider[] all = Object.FindObjectsByType<Slider>(FindObjectsSortMode.None);
            foreach (Slider s in all)
            {
                string n = s.gameObject.name.ToLower();
                if (masterSlider == null && n.Contains("master")) masterSlider = s;
                if (musicSlider == null && n.Contains("music")) musicSlider = s;
                if (sfxSlider == null && n.Contains("sfx")) sfxSlider = s;
            }
        }

        // Try to find percentage labels in scene if inspector references are empty
        if (masterPercentText == null || musicPercentText == null || sfxPercentText == null)
        {
            TextMeshProUGUI[] allText = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
            foreach (TextMeshProUGUI t in allText)
            {
                string n = t.gameObject.name.ToLower();
                bool looksLikePercent = n.Contains("percent") || n.Contains("%") || n.Contains("value") || n.Contains("pct");
                if (!looksLikePercent) continue;

                if (masterPercentText == null && n.Contains("master")) masterPercentText = t;
                if (musicPercentText == null && n.Contains("music")) musicPercentText = t;
                if (sfxPercentText == null && n.Contains("sfx")) sfxPercentText = t;
            }
        }

        if (masterSlider != null)
        {
            masterSlider.value = _masterVolume;
            masterSlider.onValueChanged.RemoveAllListeners();
            masterSlider.onValueChanged.AddListener(SetMasterVolume);
        }
        if (musicSlider != null)
        {
            musicSlider.value = _musicVolume;
            musicSlider.onValueChanged.RemoveAllListeners();
            musicSlider.onValueChanged.AddListener(SetMusicVolume);
        }
        if (sfxSlider != null)
        {
            sfxSlider.value = _sfxVolume;
            sfxSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.AddListener(SetSFXVolume);
        }

        // Initialise the percentage labels to match the loaded volumes.
        UpdatePercentLabel(masterPercentText, _masterVolume);
        UpdatePercentLabel(musicPercentText, _musicVolume);
        UpdatePercentLabel(sfxPercentText, _sfxVolume);
    }

    // =========================================================================
    //  Public volume API  (call from sliders or code)
    // =========================================================================

    /// <summary>Master volume (0–1). Scales both BGM and SFX.</summary>
    public void SetMasterVolume(float value)
    {
        _masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        PlayerPrefs.SetFloat(PREF_MASTER, _masterVolume);
        UpdatePercentLabel(masterPercentText, _masterVolume);
    }

    /// <summary>Music volume (0–1). Only scales BGM.</summary>
    public void SetMusicVolume(float value)
    {
        _musicVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        PlayerPrefs.SetFloat(PREF_MUSIC, _musicVolume);
        UpdatePercentLabel(musicPercentText, _musicVolume);
    }

    /// <summary>SFX volume (0–1). Scales button / slider / level sounds.</summary>
    public void SetSFXVolume(float value)
    {
        _sfxVolume = Mathf.Clamp01(value);
        ApplyVolumes();
        PlayerPrefs.SetFloat(PREF_SFX, _sfxVolume);
        UpdatePercentLabel(sfxPercentText, _sfxVolume);
    }

    // ── Convenience getters ──────────────────────────────────────────────────
    public float MasterVolume => _masterVolume;
    public float MusicVolume => _musicVolume;
    public float SFXVolume => _sfxVolume;

    // =========================================================================
    //  Play helpers (public so LevelAudioManager can reuse SFX source)
    // =========================================================================

    public void PlayButtonSound(AudioClip clip)
    {
        if (clip == null) return;
        _sfxSource.PlayOneShot(clip, _masterVolume * _sfxVolume);
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        _sfxSource.PlayOneShot(clip, _masterVolume * _sfxVolume);
    }

    public void PlaySliderSound()
    {
        if (sliderClip == null) return;
        if (!_sliderSource.isPlaying)
            _sliderSource.PlayOneShot(sliderClip, _masterVolume * _sfxVolume);
    }

    /// <summary>
    /// Mutes or unmutes the Slider AudioSource without changing its volume value.
    /// Called by LevelAudioManager.SetSFXPaused when the game is paused.
    ///
    /// NOTE: _sfxSource is intentionally NOT muted here because it is also used
    /// by HookPausePanel to play button-click sounds while the pause panel is
    /// open. Muting it would silence every button inside the pause panel.
    /// LevelAudioManager already mutes its own dedicated _loopSource (the car
    /// driving ambient) independently, so level SFX loops are still silenced.
    /// </summary>
    public void SetSFXMuted(bool muted)
    {
        // _sfxSource is kept unmuted so pause-panel button clicks still play.
        _sliderSource.mute = muted;
    }

    /// <summary>
    /// FIX (Req 4): Called by PauseMenuController when the pause panel opens.
    /// Wires every Button and volume Slider inside the panel so that:
    ///   • Buttons play the button click sound.
    ///   • Sliders named "master", "music", or "sfx" drive the matching volume
    ///     AND update their paired percentage TextMeshPro label.
    ///
    /// Uses Time.unscaledDeltaTime-safe AudioSource.PlayOneShot so sounds work
    /// even though Time.timeScale is 0 while the panel is visible.
    /// </summary>
    public void HookPausePanel(GameObject panel)
    {
        if (panel == null) return;

        // ── Buttons — click sound ────────────────────────────────────────────
        foreach (Button btn in panel.GetComponentsInChildren<Button>(true))
        {
            // Capture to avoid closure capturing the loop variable.
            Button captured = btn;
            // Remove then add to prevent stacking on repeated opens.
            captured.onClick.RemoveListener(OnPausePanelButtonClick);
            captured.onClick.AddListener(OnPausePanelButtonClick);
        }

        // ── TMP_Dropdowns — click sound on value change ──────────────────────
        foreach (TMPro.TMP_Dropdown dropdown in panel.GetComponentsInChildren<TMPro.TMP_Dropdown>(true))
        {
            dropdown.onValueChanged.RemoveListener(_ => OnPausePanelButtonClick());
            dropdown.onValueChanged.AddListener(_ => OnPausePanelButtonClick());
        }

        // ── Volume Sliders — drive volume + label ────────────────────────────
        foreach (Slider slider in panel.GetComponentsInChildren<Slider>(true))
        {
            string n = slider.gameObject.name.ToLower();

            if (n.Contains("master"))
            {
                slider.value = _masterVolume;
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(SetMasterVolume);
                // Pair label
                TextMeshProUGUI lbl = FindSiblingLabel(slider);
                if (lbl != null) UpdatePercentLabel(lbl, _masterVolume);
                Slider masterRef = slider; TextMeshProUGUI masterLbl = lbl;
                slider.onValueChanged.AddListener(v => UpdatePercentLabel(masterLbl, v));
            }
            else if (n.Contains("music"))
            {
                slider.value = _musicVolume;
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(SetMusicVolume);
                TextMeshProUGUI lbl = FindSiblingLabel(slider);
                if (lbl != null) UpdatePercentLabel(lbl, _musicVolume);
                slider.onValueChanged.AddListener(v => UpdatePercentLabel(lbl, v));
            }
            else if (n.Contains("sfx"))
            {
                slider.value = _sfxVolume;
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(SetSFXVolume);
                TextMeshProUGUI lbl = FindSiblingLabel(slider);
                if (lbl != null) UpdatePercentLabel(lbl, _sfxVolume);
                slider.onValueChanged.AddListener(v => UpdatePercentLabel(lbl, v));
            }
        }
    }

    /// <summary>
    /// Attaches the current scene's button sound to every Button and
    /// TMP_Dropdown inside <paramref name="panel"/>.  Call this from any
    /// manager that activates a panel after the initial scene-load sweep
    /// (e.g. AuthManager activating confirmationPanel or nameErrorPanel).
    /// </summary>
    public void HookPanel(GameObject panel)
    {
        if (panel == null) return;

        // Resolve the clip for the current scene (same fallback as pause panel).
        AudioClip clip = null;
        AudioClip firstAvailable = null;
        foreach (ButtonSoundEntry entry in buttonSoundEntries)
        {
            if (entry.clip == null) continue;
            if (firstAvailable == null) firstAvailable = entry.clip;
            if (_currentScene != null && entry.sceneNames.Contains(_currentScene))
            {
                clip = entry.clip;
                break;
            }
        }
        AudioClip resolved = clip != null ? clip : firstAvailable;
        if (resolved == null) return;

        foreach (Button btn in panel.GetComponentsInChildren<Button>(true))
        {
            AudioClip captured = resolved;
            btn.onClick.RemoveListener(() => PlayButtonSound(captured));
            btn.onClick.AddListener(() => PlayButtonSound(captured));
        }

        foreach (TMPro.TMP_Dropdown dropdown in panel.GetComponentsInChildren<TMPro.TMP_Dropdown>(true))
        {
            AudioClip captured = resolved;
            dropdown.onValueChanged.RemoveListener(_ => PlayButtonSound(captured));
            dropdown.onValueChanged.AddListener(_ => PlayButtonSound(captured));
        }
    }

    // Called via RemoveListener/AddListener — needs a stable method reference.
    private void OnPausePanelButtonClick()
    {
        // Try to find a clip that matches the current scene first.
        // If none match (e.g. the level scene isn't in any ButtonSoundEntry list),
        // fall back to the very first available clip so pause-panel buttons always
        // make a sound regardless of which scene is active.
        AudioClip clip = null;
        AudioClip firstAvailable = null;

        foreach (ButtonSoundEntry entry in buttonSoundEntries)
        {
            if (entry.clip == null) continue;
            if (firstAvailable == null) firstAvailable = entry.clip;
            if (_currentScene != null && entry.sceneNames.Contains(_currentScene))
            {
                clip = entry.clip;
                break;
            }
        }

        // Use scene-matched clip, or the universal fallback.
        PlayButtonSound(clip != null ? clip : firstAvailable);
    }

    /// <summary>
    /// Looks for a TextMeshProUGUI sibling (or child of the slider's parent)
    /// whose name contains "percent", "%", "value", or "pct" to use as the label.
    /// </summary>
    private TextMeshProUGUI FindSiblingLabel(Slider slider)
    {
        if (slider.transform.parent == null) return null;
        foreach (TextMeshProUGUI t in slider.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            string n = t.gameObject.name.ToLower();
            if (n.Contains("percent") || n.Contains("%") || n.Contains("value") || n.Contains("pct"))
                return t;
        }
        return null;
    }

    // =========================================================================
    //  Internal helpers
    // =========================================================================

    private void CreateAudioSources()
    {
        _bgmSource = CreateSource("BGM", loop: true, volume: 1f);
        _sfxSource = CreateSource("SFX", loop: false, volume: 1f);
        _sliderSource = CreateSource("Slider", loop: false, volume: 1f);

        // SFX must play even when Time.timeScale = 0 (pause panel open).
        // ignoreListenerPause keeps the AudioListener active for these sources.
        _sfxSource.ignoreListenerPause = true;
        _sliderSource.ignoreListenerPause = true;
    }

    private AudioSource CreateSource(string label, bool loop, float volume)
    {
        GameObject go = new GameObject($"AudioSource_{label}");
        go.transform.SetParent(transform);
        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.volume = volume;
        return src;
    }

    private void ApplyVolumes()
    {
        // If no fade is running, set BGM volume directly
        if (_fadeCoroutine == null)
            _bgmSource.volume = _masterVolume * _musicVolume;

        _sfxSource.volume = _masterVolume * _sfxVolume;
        _sliderSource.volume = _masterVolume * _sfxVolume;
    }

    private void LoadVolumes()
    {
        _masterVolume = PlayerPrefs.GetFloat(PREF_MASTER, 1f);
        _musicVolume = PlayerPrefs.GetFloat(PREF_MUSIC, 1f);
        _sfxVolume = PlayerPrefs.GetFloat(PREF_SFX, 1f);
        ApplyVolumes();
    }

    /// <summary>Writes "75%" style text into a percentage label. Safe to call with a null label.</summary>
    private void UpdatePercentLabel(TextMeshProUGUI label, float volume01)
    {
        if (label == null) return;
        label.text = $"{Mathf.RoundToInt(volume01 * 100f)}%";
    }
}