using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Owns all game music playback. One script, one job.
///
/// Manages two audio layers:
///   1. Background / Ambient — loops through all assigned tracks in a random,
///      non-repeating order (Fisher-Yates shuffle per cycle).
///   2. Intense / Chase      — a separate loop that cross-fades in/out on demand
///      (e.g. when the demon is nearby).
///
/// OCP: Adding new music states only requires calling SetIntenseMode(true/false)
/// or extending the enum — no core loop logic needs changing.
///
/// Usage:
///   • Drag this onto a persistent GameObject in your scene.
///   • Assign your audio clips to 'backgroundTracks[]' in the Inspector.
///   • Assign separate AudioSources for bg and intense layers.
///   • Call GameMusicManager.Instance.SetIntenseMode(true) to blend in chase music.
/// </summary>
public class GameMusicManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Singleton
    // -------------------------------------------------------------------------
    public static GameMusicManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector — Background Tracks
    // -------------------------------------------------------------------------
    [Header("Background / Ambient Tracks")]
    [Tooltip("Drag all ambient / background audio clips here. They will play in a random, non-repeating order.")]
    public AudioClip[] backgroundTracks;

    [Tooltip("The AudioSource that will play the background / ambient music layer.")]
    public AudioSource bgAudioSource;

    [Tooltip("Seconds of silence between tracks (0 = seamless).")]
    [Range(0f, 10f)]
    public float timeBetweenTracks = 2f;

    // -------------------------------------------------------------------------
    //  Inspector — Intense / Chase Layer
    // -------------------------------------------------------------------------
    [Header("Intense / Chase Music")]
    [Tooltip("The audio clip for the intense/chase music. This is what plays when the demon is near.")]
    public AudioClip intenseTrack;

    [Tooltip("The AudioSource that plays the intense chase/horror sting. Set it to loop in the Inspector.")]
    public AudioSource intenseAudioSource;

    [Tooltip("Seconds to cross-fade between calm and intense states.")]
    [Range(0.1f, 5f)]
    public float crossfadeDuration = 1.5f;

    [Header("Playback Behavior")]
    [Tooltip("If true, background music will pause during normal match gameplay in the mine, letting environmental ambience play, and only resume while paused in the pause menu.")]
    public bool onlyPlayWhenPaused = true;

    // -------------------------------------------------------------------------
    //  Inspector — Volume
    // -------------------------------------------------------------------------
    [Header("Volume")]
    [Range(0f, 1f)]
    public float bgMaxVolume    = 0.6f;
    [Range(0f, 1f)]
    public float intenseMaxVolume = 0.8f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private List<int>  _shuffledIndices = new List<int>();
    private int        _currentShufflePos = 0;
    private bool       _isIntenseActive   = false;
    private Coroutine  _bgLoopCoroutine;
    private Coroutine  _fadeCoroutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        // Singleton — persist across scenes
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureAudioListener();
        ValidateAudioSources();

        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (IsMenuOrLoadingScene(currentScene) || !onlyPlayWhenPaused)
        {
            StartBackgroundLoop();
        }
    }

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Start()
    {
        EnsureAudioListener();
        ValidateAudioSources();

        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        HandleSceneMusic(currentScene);
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        EnsureAudioListener();
        HandleSceneMusic(scene.name);
    }

    private void HandleSceneMusic(string sceneName)
    {
        bool isMenuOrLoading = IsMenuOrLoadingScene(sceneName);

        if (isMenuOrLoading || !onlyPlayWhenPaused)
        {
            StartBackgroundLoop();
        }
        else
        {
            // In gameplay match with onlyPlayWhenPaused enabled: mute for environmental ambience
            if (bgAudioSource != null && bgAudioSource.isPlaying)
            {
                if (_bgLoopCoroutine != null) StopCoroutine(_bgLoopCoroutine);
                _bgLoopCoroutine = null;
                bgAudioSource.Stop();
                bgAudioSource.volume = 0f;
            }
        }
    }

    private bool IsMenuOrLoadingScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return true;
        string lower = sceneName.ToLowerInvariant();
        return lower.Contains("load") || lower.Contains("boot") || lower.Contains("lobby") || lower.Contains("menu");
    }

    private void EnsureAudioListener()
    {
        AudioListener.pause = false;
        if (AudioListener.volume <= 0.01f)
        {
            AudioListener.volume = 1f;
        }

        AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        if (listeners == null || listeners.Length == 0)
        {
            Camera cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.gameObject.AddComponent<AudioListener>();
            }
            else
            {
                gameObject.AddComponent<AudioListener>();
            }
        }
        else if (listeners.Length > 1)
        {
            AudioListener ourListener = GetComponent<AudioListener>();
            if (ourListener != null)
            {
                Destroy(ourListener);
            }
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Toggle the intense/chase music layer.
    /// Cross-fades the bg layer down and intense layer up (or vice-versa).
    /// </summary>
    public void SetIntenseMode(bool intense)
    {
        if (_isIntenseActive == intense) return;
        _isIntenseActive = intense;

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(CrossfadeRoutine(intense));
    }

    /// <summary>
    /// Immediately stops all music (e.g. on match end).
    /// </summary>
    public void StopAll()
    {
        if (_bgLoopCoroutine != null) StopCoroutine(_bgLoopCoroutine);
        _bgLoopCoroutine = null;
        if (_fadeCoroutine   != null) StopCoroutine(_fadeCoroutine);

        if (bgAudioSource     != null) bgAudioSource.Stop();
        if (intenseAudioSource != null) intenseAudioSource.Stop();
    }

    /// <summary>
    /// Restarts the background loop (e.g. after returning to lobby or on boot).
    /// </summary>
    public void StartBackgroundLoop()
    {
        if (bgAudioSource != null && bgAudioSource.isPlaying && _bgLoopCoroutine != null)
        {
            // Already running smoothly, don't interrupt track
            return;
        }

        if (_bgLoopCoroutine != null) StopCoroutine(_bgLoopCoroutine);
        _bgLoopCoroutine = StartCoroutine(BackgroundLoopRoutine());
    }

    /// <summary>
    /// Controls music playback based on game pause state when onlyPlayWhenPaused is enabled.
    /// </summary>
    public void SetPauseMusicState(bool isPaused)
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (IsMenuOrLoadingScene(sceneName)) return; // Always keep playing in menus/loading

        if (!onlyPlayWhenPaused) return;

        if (isPaused)
        {
            StartBackgroundLoop();
        }
        else
        {
            StopAll();
        }
    }

    // =========================================================================
    //  Private — Background Loop
    // =========================================================================

    /// <summary>
    /// Loops through all background tracks in a shuffled order.
    /// Re-shuffles each cycle so no track repeats back-to-back across cycles.
    /// </summary>
    private IEnumerator BackgroundLoopRoutine()
    {
        if (backgroundTracks == null || backgroundTracks.Length == 0 || bgAudioSource == null)
        {
            Debug.LogWarning("[GameMusicManager] No background tracks assigned or no AudioSource set.");
            yield break;
        }

        while (true)
        {
            // Build / rebuild shuffle list when exhausted
            if (_currentShufflePos >= _shuffledIndices.Count)
                RebuildShuffledList();

            int trackIndex = _shuffledIndices[_currentShufflePos];
            _currentShufflePos++;

            AudioClip clip = backgroundTracks[trackIndex];
            if (clip == null) continue;

            bgAudioSource.clip = clip;
            float targetVol = _isIntenseActive ? bgMaxVolume * 0.3f : bgMaxVolume;

            // Start playing immediately at target volume so music is heard right away from frame 0
            bgAudioSource.volume = targetVol;
            bgAudioSource.Play();

            // Wait for the track to finish (minus fade duration)
            float waitDuration = Mathf.Max(0f, clip.length - 1.5f);
            yield return new WaitForSecondsRealtime(waitDuration);

            // Fade out before switching
            yield return StartCoroutine(FadeVolume(bgAudioSource, bgAudioSource.volume, 0f, 1.5f));
            bgAudioSource.Stop();

            // Gap between tracks
            if (timeBetweenTracks > 0f)
                yield return new WaitForSecondsRealtime(timeBetweenTracks);
        }
    }

    /// <summary>
    /// Fisher-Yates shuffle of track indices.
    /// Ensures the last track of the previous cycle isn't the first of the next.
    /// </summary>
    private void RebuildShuffledList()
    {
        int lastIndex = (_shuffledIndices.Count > 0)
            ? _shuffledIndices[_shuffledIndices.Count - 1]
            : -1;

        _shuffledIndices.Clear();
        for (int i = 0; i < backgroundTracks.Length; i++)
            _shuffledIndices.Add(i);

        // Fisher-Yates
        for (int i = _shuffledIndices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_shuffledIndices[i], _shuffledIndices[j]) = (_shuffledIndices[j], _shuffledIndices[i]);
        }

        // Prevent the same track repeating across cycle boundary
        if (_shuffledIndices.Count > 1 && _shuffledIndices[0] == lastIndex)
        {
            int swap = _shuffledIndices[0];
            _shuffledIndices[0] = _shuffledIndices[1];
            _shuffledIndices[1] = swap;
        }

        _currentShufflePos = 0;
    }

    // =========================================================================
    //  Private — Crossfade
    // =========================================================================
    private IEnumerator CrossfadeRoutine(bool toIntense)
    {
        float bgTarget     = toIntense ? bgMaxVolume * 0.3f : bgMaxVolume;
        float intenseTarget = toIntense ? intenseMaxVolume  : 0f;

        if (toIntense && intenseAudioSource != null && !intenseAudioSource.isPlaying)
        {
            intenseAudioSource.volume = 0f;
            intenseAudioSource.Play();
        }

        // Run both fades in parallel using separate coroutines
        Coroutine fadeBg      = StartCoroutine(FadeVolume(bgAudioSource,      bgAudioSource?.volume ?? 0f,     bgTarget,     crossfadeDuration));
        Coroutine fadeIntense = StartCoroutine(FadeVolume(intenseAudioSource, intenseAudioSource?.volume ?? 0f, intenseTarget, crossfadeDuration));

        yield return fadeBg;
        yield return fadeIntense;

        if (!toIntense && intenseAudioSource != null && intenseAudioSource.isPlaying)
            intenseAudioSource.Stop();
    }

    private IEnumerator FadeVolume(AudioSource source, float from, float to, float duration)
    {
        if (source == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed       += Time.unscaledDeltaTime;
            source.volume  = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        source.volume = to;
    }

    // =========================================================================
    //  Validation
    // =========================================================================
    private void ValidateAudioSources()
    {
        if (bgMaxVolume <= 0.05f) bgMaxVolume = 0.6f;

        var sources = GetComponents<AudioSource>();
        if (sources.Length == 0)
        {
            bgAudioSource = gameObject.AddComponent<AudioSource>();
            intenseAudioSource = gameObject.AddComponent<AudioSource>();
        }
        else if (sources.Length == 1)
        {
            bgAudioSource = sources[0];
            intenseAudioSource = gameObject.AddComponent<AudioSource>();
        }
        else
        {
            if (bgAudioSource == null && intenseAudioSource == null)
            {
                bgAudioSource = sources[0];
                intenseAudioSource = sources[1];
            }
            else if (bgAudioSource == intenseAudioSource || bgAudioSource == null || intenseAudioSource == null)
            {
                bgAudioSource = sources[0];
                intenseAudioSource = sources[1];
            }
        }

        if (bgAudioSource != null)
        {
            bgAudioSource.loop = false;
            bgAudioSource.playOnAwake = false;
            bgAudioSource.spatialBlend = 0f; // Pure 2D global background
            bgAudioSource.mute = false;
        }

        if (intenseAudioSource != null)
        {
            intenseAudioSource.loop = true;
            intenseAudioSource.playOnAwake = false;
            intenseAudioSource.spatialBlend = 0f; // Pure 2D global audio
            intenseAudioSource.mute = false;
            if (intenseTrack != null)
                intenseAudioSource.clip = intenseTrack;
        }

        if (bgAudioSource == null)
            Debug.LogWarning("[GameMusicManager] 'bgAudioSource' is not assigned or created!");

        if (intenseAudioSource == null)
            Debug.LogWarning("[GameMusicManager] 'intenseAudioSource' is not assigned. Intense mode will be skipped.");

        if (intenseTrack == null)
            Debug.LogWarning("[GameMusicManager] 'intenseTrack' clip is not assigned. Intense mode will be silent.");

        if (backgroundTracks == null || backgroundTracks.Length == 0)
            Debug.LogWarning("[GameMusicManager] No background tracks assigned. Music will not play.");
    }
}
