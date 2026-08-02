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
    [Tooltip("The AudioSource that plays the intense chase/horror sting. Set it to loop in the Inspector.")]
    public AudioSource intenseAudioSource;

    [Tooltip("Seconds to cross-fade between calm and intense states.")]
    [Range(0.1f, 5f)]
    public float crossfadeDuration = 1.5f;

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
    }

    void Start()
    {
        ValidateAudioSources();
        StartBackgroundLoop();
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
        if (_fadeCoroutine   != null) StopCoroutine(_fadeCoroutine);

        if (bgAudioSource     != null) bgAudioSource.Stop();
        if (intenseAudioSource != null) intenseAudioSource.Stop();
    }

    /// <summary>
    /// Restarts the background loop (e.g. after returning to lobby).
    /// </summary>
    public void StartBackgroundLoop()
    {
        if (_bgLoopCoroutine != null) StopCoroutine(_bgLoopCoroutine);
        _bgLoopCoroutine = StartCoroutine(BackgroundLoopRoutine());
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

        // Fade bg in from silence at game start
        bgAudioSource.volume = 0f;
        bgAudioSource.Play();

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
            bgAudioSource.Play();

            float targetVol = _isIntenseActive ? bgMaxVolume * 0.3f : bgMaxVolume;
            yield return StartCoroutine(FadeVolume(bgAudioSource, bgAudioSource.volume, targetVol, 1.5f));

            // Wait for the track to finish
            yield return new WaitForSeconds(clip.length - 1.5f);

            // Fade out before switching
            yield return StartCoroutine(FadeVolume(bgAudioSource, bgAudioSource.volume, 0f, 1.5f));
            bgAudioSource.Stop();

            // Gap between tracks
            if (timeBetweenTracks > 0f)
                yield return new WaitForSeconds(timeBetweenTracks);
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
            elapsed       += Time.deltaTime;
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
        if (bgAudioSource == null)
            Debug.LogWarning("[GameMusicManager] 'bgAudioSource' is not assigned in the Inspector!");

        if (intenseAudioSource == null)
            Debug.LogWarning("[GameMusicManager] 'intenseAudioSource' is not assigned. Intense mode will be skipped.");

        if (backgroundTracks == null || backgroundTracks.Length == 0)
            Debug.LogWarning("[GameMusicManager] No background tracks assigned. Music will not play.");
    }
}
