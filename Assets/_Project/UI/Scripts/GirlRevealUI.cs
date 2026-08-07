using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

/// <summary>
/// SOLID — SRP: High-end cinematic slot-machine reveal animation for the Vengeful Spirit selection.
///
/// Features:
///   • 3-Row Ticker Reel Display (Above, Main Focus, Below) for an authentic slot reel look.
///   • Audio tick playback on every name flip with pitch modulation.
///   • Smooth acceleration, high-speed blur phase, and deceleration easing onto the winner.
///   • Gold winner reveal with scale pop and camera impulse trigger.
/// </summary>
public class GirlRevealUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Panels
    // -------------------------------------------------------------------------
    [Header("Panels")]
    [Tooltip("Full-screen panel covering the lobby during the reveal.")]
    public GameObject revealPanel;

    [Tooltip("Winner card panel shown after the spin locks in.")]
    public GameObject winnerPanel;

    // -------------------------------------------------------------------------
    //  Inspector — Ticker Reel Text Elements
    // -------------------------------------------------------------------------
    [Header("Ticker Reel (3-Row Slot Display)")]
    [Tooltip("Optional text line above main focus (rolling out).")]
    public TextMeshProUGUI aboveNameText;

    [Tooltip("Main large central text line (currently highlighted name).")]
    public TextMeshProUGUI focusNameText;

    [Tooltip("Optional text line below main focus (rolling in).")]
    public TextMeshProUGUI belowNameText;

    [Tooltip("Header text displaying the reveal status.")]
    public TextMeshProUGUI headerTitleText;

    [Header("Winner Panel Elements")]
    [Tooltip("TMP showing winner display name.")]
    public TextMeshProUGUI winnerNameText;

    [Tooltip("Subtitle beneath winner name, e.g. 'VENGEFUL SPIRIT'.")]
    public TextMeshProUGUI winnerSubtitleText;

    // -------------------------------------------------------------------------
    //  Inspector — Audio & FX
    // -------------------------------------------------------------------------
    [Header("Audio SFX (Optional)")]
    public AudioSource audioSource;
    public AudioClip tickSound;
    public AudioClip winnerLockSound;

    [Header("Background FX")]
    public Image backgroundPulse;

    // -------------------------------------------------------------------------
    //  Inspector — Timing Configuration
    // -------------------------------------------------------------------------
    [Header("Phase Durations (seconds)")]
    public float slowPhaseDuration  = 1.5f;
    public float fastPhaseDuration  = 2.0f;
    public float stopPhaseDuration  = 1.2f;
    public float winnerHoldDuration = 3.0f;

    [Header("Scroll Speeds (seconds per flip)")]
    public float slowScrollInterval = 0.20f;
    public float fastScrollInterval = 0.04f;

    // -------------------------------------------------------------------------
    //  Inspector — Styling & Animations
    // -------------------------------------------------------------------------
    [Header("Colours")]
    public Color normalNameColor    = new Color(0.9f, 0.9f, 0.95f, 1f);
    public Color outerNameColor     = new Color(0.5f, 0.5f, 0.6f, 0.45f);
    public Color winnerNameColor    = new Color(1f, 0.85f, 0.25f, 1f); // Gold
    public Color bgColorSlow        = new Color(0.06f, 0.06f, 0.14f, 0.94f);
    public Color bgColorFast        = new Color(0.40f, 0.05f, 0.05f, 0.96f);
    public Color bgColorWinner      = new Color(0.65f, 0.45f, 0.05f, 0.98f);

    [Header("Winner Scale Animation")]
    public float winnerScale        = 1.35f;
    public float winnerScaleDuration = 0.35f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private string[]      _playerNames;
    private ulong[]       _clientIds;
    private ulong         _girlClientId;
    private int           _girlNameIndex;
    private Coroutine     _spinCoroutine;
    private Action<ulong> _onComplete;

    void Awake()
    {
        // Nothing auto-wired — all references dragged in Inspector.
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    public void StartSpin(ulong girlClientId, string[] playerNames, ulong[] clientIds, Action<ulong> onComplete)
    {
        _girlClientId = girlClientId;
        _playerNames  = playerNames;
        _clientIds    = clientIds;
        _onComplete   = onComplete;

        // Find winner index
        _girlNameIndex = 0;
        if (clientIds != null)
        {
            for (int i = 0; i < clientIds.Length; i++)
            {
                if (clientIds[i] == girlClientId) { _girlNameIndex = i; break; }
            }
        }

        if (_spinCoroutine != null) StopCoroutine(_spinCoroutine);

        if (revealPanel != null) revealPanel.SetActive(true);
        if (winnerPanel != null) winnerPanel.SetActive(false);
        if (focusNameText != null) focusNameText.transform.localScale = Vector3.one;

        if (headerTitleText != null)
            headerTitleText.text = "SELECTING VENGEFUL SPIRIT";

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.Reveal);

        _spinCoroutine = StartCoroutine(RunSpinSequence());
    }

    // =========================================================================
    //  Spin Coroutine
    // =========================================================================

    private IEnumerator RunSpinSequence()
    {
        if (_playerNames == null || _playerNames.Length == 0)
        {
            _onComplete?.Invoke(_girlClientId);
            yield break;
        }

        int currentIndex = 0;

        // ── Phase 1: Slow Acceleration ──────────────────────────────────────
        SetBgColor(bgColorSlow);
        float elapsed = 0f;
        while (elapsed < slowPhaseDuration)
        {
            ShowReelNames(currentIndex, normalNameColor);
            PlayTickSFX(1.0f);
            currentIndex = (currentIndex + 1) % _playerNames.Length;
            yield return new WaitForSecondsRealtime(slowScrollInterval);
            elapsed += slowScrollInterval;
        }

        // ── Phase 2: High Speed Spin ────────────────────────────────────────
        SetBgColor(bgColorFast);
        elapsed = 0f;
        while (elapsed < fastPhaseDuration)
        {
            ShowReelNames(currentIndex, normalNameColor);
            PlayTickSFX(1.3f);
            currentIndex = (currentIndex + 1) % _playerNames.Length;
            yield return new WaitForSecondsRealtime(fastScrollInterval);
            elapsed += fastScrollInterval;
        }

        // ── Phase 3: Smooth Deceleration onto Winner ────────────────────────
        int stepsToWinner = 0;
        {
            int tmp = currentIndex;
            while (tmp != _girlNameIndex)
            {
                tmp = (tmp + 1) % _playerNames.Length;
                stepsToWinner++;
            }
        }

        int minSteps = Mathf.CeilToInt(stopPhaseDuration / (slowScrollInterval * 0.5f));
        while (stepsToWinner < minSteps)
            stepsToWinner += _playerNames.Length;

        for (int step = 0; step < stepsToWinner; step++)
        {
            ShowReelNames(currentIndex, normalNameColor);

            float t        = (float)step / stepsToWinner;
            float interval = Mathf.Lerp(fastScrollInterval, slowScrollInterval * 2.2f, Mathf.SmoothStep(0f, 1f, t));
            float pitch    = Mathf.Lerp(1.3f, 0.8f, t);

            PlayTickSFX(pitch);
            currentIndex = (currentIndex + 1) % _playerNames.Length;

            if (backgroundPulse != null)
                backgroundPulse.color = Color.Lerp(bgColorFast, bgColorWinner, t);

            yield return new WaitForSecondsRealtime(interval);
        }

        // ── Winner Lock-In ───────────────────────────────────────────────────
        ShowReelNames(_girlNameIndex, winnerNameColor);
        SetBgColor(bgColorWinner);

        if (headerTitleText != null)
            headerTitleText.text = "VENGEFUL SPIRIT REVEALED";

        PlaySound(winnerLockSound, 1.0f);

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.FireRevealImpulse();

        // Winner Scale Pop
        if (focusNameText != null)
            yield return StartCoroutine(AnimateScale(focusNameText.transform, 1f, winnerScale, winnerScaleDuration));

        // Display Winner Overlay Panel
        if (winnerPanel != null)
        {
            winnerPanel.SetActive(true);
            if (winnerNameText != null)
                winnerNameText.text = _playerNames[_girlNameIndex];
            if (winnerSubtitleText != null)
                winnerSubtitleText.text = "THE VENGEFUL SPIRIT HAS AWAKENED";
        }

        yield return new WaitForSecondsRealtime(winnerHoldDuration);

        // ── Cleanup & Route ──────────────────────────────────────────────────
        if (revealPanel != null) revealPanel.SetActive(false);
        _spinCoroutine = null;
        _onComplete?.Invoke(_girlClientId);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private void ShowReelNames(int focusIndex, Color focusColor)
    {
        int count = _playerNames.Length;
        if (count == 0) return;

        // With 1-2 players the above/below slots look broken (same names cycling),
        // so we hide them and only show the single focus reel row.
        bool useOuterRows = count >= 3;

        int prevIndex = (focusIndex - 1 + count) % count;
        int nextIndex = (focusIndex + 1) % count;

        if (focusNameText != null)
        {
            focusNameText.text  = _playerNames[focusIndex];
            focusNameText.color = focusColor;
        }

        if (aboveNameText != null)
        {
            aboveNameText.gameObject.SetActive(useOuterRows);
            if (useOuterRows)
            {
                aboveNameText.text  = _playerNames[prevIndex];
                aboveNameText.color = outerNameColor;
            }
        }

        if (belowNameText != null)
        {
            belowNameText.gameObject.SetActive(useOuterRows);
            if (useOuterRows)
            {
                belowNameText.text  = _playerNames[nextIndex];
                belowNameText.color = outerNameColor;
            }
        }
    }

    private void PlayTickSFX(float pitch)
    {
        if (audioSource != null && tickSound != null)
        {
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(tickSound, 0.6f);
        }
    }

    private void PlaySound(AudioClip clip, float volume = 1f)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.pitch = 1.0f;
            audioSource.PlayOneShot(clip, volume);
        }
    }

    private void SetBgColor(Color target)
    {
        if (backgroundPulse != null)
            backgroundPulse.color = target;
    }

    private IEnumerator AnimateScale(Transform t, float from, float to, float duration)
    {
        if (t == null) yield break;
        float elapsed = 0f;
        t.localScale = Vector3.one * from;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float scale = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            t.localScale = Vector3.one * scale;
            yield return null;
        }
        t.localScale = Vector3.one * to;
    }
}
