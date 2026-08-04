using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

/// <summary>
/// SOLID — SRP: The full-screen slot-machine reveal animation for the Vengeful Spirit selection.
///
/// Three animated phases:
///   Phase 1 — Slow (slowPhaseDuration):
///     Names cycle at a readable pace. Players recognize names, anticipation builds.
///   Phase 2 — Fast (fastPhaseDuration):
///     Names fly past in a blur. Tension peaks.
///   Phase 3 — Decelerate &amp; Stop (stopPhaseDuration):
///     The scroll decelerates precisely, always landing on the winner's name.
///     Winner name scales up with a gold glow reveal.
///
/// Called by GirlRevealManager.BeginReveal() via RPC.
/// Fires the onComplete callback when the reveal finishes so GirlRevealManager
/// can route each client locally.
///
/// ─── SETUP ────────────────────────────────────────────────────────────────────
///  Create a full-screen Canvas panel with:
///    • revealPanel           — root that covers everything (starts inactive)
///    • focusNameText         — large centred TMP label for the spinning name
///    • backgroundPulse       — optional Image that colour-shifts with the phases
///    • winnerPanel           — panel shown after the stop (starts inactive)
///      └─ winnerNameText     — TMP for the winner's display name
///      └─ winnerSubtitleText — TMP for "VENGEFUL SPIRIT"
/// </summary>
public class GirlRevealUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Inspector — Panels
    // -------------------------------------------------------------------------
    [Header("Panels")]
    [Tooltip("Full-screen panel that covers everything during the reveal. Inactive by default.")]
    public GameObject revealPanel;

    [Tooltip("Panel revealed after the spin stops showing the winner. Inactive by default.")]
    public GameObject winnerPanel;

    // -------------------------------------------------------------------------
    //  Inspector — Text Elements
    // -------------------------------------------------------------------------
    [Header("Text")]
    [Tooltip("Large TMP label in the centre — shows the name currently 'in focus' as the ticker spins.")]
    public TextMeshProUGUI focusNameText;

    [Tooltip("TMP showing the winner's name in the winner panel.")]
    public TextMeshProUGUI winnerNameText;

    [Tooltip("Subtitle beneath the winner name, e.g. 'VENGEFUL SPIRIT'.")]
    public TextMeshProUGUI winnerSubtitleText;

    // -------------------------------------------------------------------------
    //  Inspector — Background FX
    // -------------------------------------------------------------------------
    [Header("Background FX")]
    [Tooltip("Optional Image that colour-shifts through the reveal phases for atmosphere.")]
    public Image backgroundPulse;

    // -------------------------------------------------------------------------
    //  Inspector — Phase Timing
    // -------------------------------------------------------------------------
    [Header("Phase Timing (seconds)")]
    public float slowPhaseDuration   = 1.5f;
    public float fastPhaseDuration   = 2.0f;
    public float stopPhaseDuration   = 0.9f;

    [Tooltip("Seconds to hold the winner panel before firing the onComplete callback.")]
    public float winnerHoldDuration  = 2.8f;

    // -------------------------------------------------------------------------
    //  Inspector — Scroll Intervals (seconds per name change)
    // -------------------------------------------------------------------------
    [Header("Scroll Speed (seconds per name flip)")]
    public float slowScrollInterval  = 0.22f;
    public float fastScrollInterval  = 0.04f;

    // -------------------------------------------------------------------------
    //  Inspector — Visual Style
    // -------------------------------------------------------------------------
    [Header("Colours")]
    public Color normalNameColor   = Color.white;
    public Color winnerNameColor   = new Color(1f, 0.85f, 0.25f, 1f);  // gold
    public Color bgColorSlow       = new Color(0.08f, 0.08f, 0.18f, 0.92f);
    public Color bgColorFast       = new Color(0.45f, 0.06f, 0.06f, 0.95f);
    public Color bgColorWinner     = new Color(0.70f, 0.50f, 0.05f, 1.00f);

    [Header("Winner Scale Animation")]
    [Tooltip("Scale of focusNameText when the winner is locked in.")]
    public float winnerScale       = 1.4f;
    [Tooltip("Duration of the scale-up pop on the winner.")]
    public float winnerScaleDuration = 0.4f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private string[]        _playerNames;
    private ulong[]         _clientIds;
    private ulong           _girlClientId;
    private int             _girlNameIndex;
    private Coroutine       _spinCoroutine;
    private Action<ulong>   _onComplete;

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Begins the slot-machine reveal animation.
    /// </summary>
    /// <param name="girlClientId">The client selected as Vengeful Spirit.</param>
    /// <param name="playerNames">Display names of all players (parallel array with clientIds).</param>
    /// <param name="clientIds">Client IDs of all players (parallel array with playerNames).</param>
    /// <param name="onComplete">Callback fired after the winner panel hold; passes girlClientId.</param>
    public void StartSpin(ulong girlClientId, string[] playerNames, ulong[] clientIds, Action<ulong> onComplete)
    {
        _girlClientId = girlClientId;
        _playerNames  = playerNames;
        _clientIds    = clientIds;
        _onComplete   = onComplete;

        // Locate the winner in the name array
        _girlNameIndex = 0;
        for (int i = 0; i < clientIds.Length; i++)
        {
            if (clientIds[i] == girlClientId) { _girlNameIndex = i; break; }
        }

        if (_spinCoroutine != null) StopCoroutine(_spinCoroutine);

        if (revealPanel != null) revealPanel.SetActive(true);
        if (winnerPanel != null) winnerPanel.SetActive(false);
        if (focusNameText != null) focusNameText.transform.localScale = Vector3.one;

        // Trigger Cinemachine camera blend for the reveal screen
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

        // ── Phase 1: Slow ────────────────────────────────────────────────────
        SetBgColor(bgColorSlow);
        float elapsed = 0f;
        while (elapsed < slowPhaseDuration)
        {
            ShowName(currentIndex, normalNameColor);
            currentIndex = (currentIndex + 1) % _playerNames.Length;
            yield return new WaitForSecondsRealtime(slowScrollInterval);
            elapsed += slowScrollInterval;
        }

        // ── Phase 2: Fast ────────────────────────────────────────────────────
        SetBgColor(bgColorFast);
        elapsed = 0f;
        while (elapsed < fastPhaseDuration)
        {
            ShowName(currentIndex, normalNameColor);
            currentIndex = (currentIndex + 1) % _playerNames.Length;
            yield return new WaitForSecondsRealtime(fastScrollInterval);
            elapsed += fastScrollInterval;
        }

        // ── Phase 3: Decelerate precisely onto the winner ────────────────────
        // Count how many steps from currentIndex to winner index.
        int stepsToWinner = 0;
        {
            int tmp = currentIndex;
            while (tmp != _girlNameIndex)
            {
                tmp = (tmp + 1) % _playerNames.Length;
                stepsToWinner++;
            }
        }

        // Ensure we decelerate across at least stopPhaseDuration total time.
        // Add full loops of the name list if needed.
        int minSteps = Mathf.CeilToInt(stopPhaseDuration / (slowScrollInterval * 0.5f));
        while (stepsToWinner < minSteps)
            stepsToWinner += _playerNames.Length;

        for (int step = 0; step < stepsToWinner; step++)
        {
            ShowName(currentIndex, normalNameColor);
            currentIndex = (currentIndex + 1) % _playerNames.Length;

            // t goes 0→1 over the decel phase; lerp interval fast→slow
            float t        = (float)step / stepsToWinner;
            float interval = Mathf.Lerp(fastScrollInterval, slowScrollInterval * 2f,
                                        Mathf.SmoothStep(0f, 1f, t));

            yield return new WaitForSecondsRealtime(interval);

            // Colour-shift background from fast-red toward gold winner colour
            if (backgroundPulse != null)
                backgroundPulse.color = Color.Lerp(bgColorFast, bgColorWinner, t);
        }

        // ── Winner Lock-In ───────────────────────────────────────────────────
        ShowName(_girlNameIndex, winnerNameColor);
        SetBgColor(bgColorWinner);

        // Fire camera impulse/shake when winner locks in
        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.FireRevealImpulse();

        // Scale-up pop
        yield return StartCoroutine(AnimateScale(focusNameText?.transform, 1f, winnerScale, winnerScaleDuration));

        // Show winner panel
        if (winnerPanel != null)
        {
            winnerPanel.SetActive(true);
            if (winnerNameText    != null) winnerNameText.text    = _playerNames[_girlNameIndex];
            if (winnerSubtitleText != null) winnerSubtitleText.text = "VENGEFUL SPIRIT";
        }

        yield return new WaitForSecondsRealtime(winnerHoldDuration);

        // ── Clean up & callback ───────────────────────────────────────────────
        if (revealPanel != null) revealPanel.SetActive(false);
        _spinCoroutine = null;
        _onComplete?.Invoke(_girlClientId);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private void ShowName(int index, Color color)
    {
        if (focusNameText == null || _playerNames == null) return;
        focusNameText.text  = _playerNames[Mathf.Abs(index) % _playerNames.Length];
        focusNameText.color = color;
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
