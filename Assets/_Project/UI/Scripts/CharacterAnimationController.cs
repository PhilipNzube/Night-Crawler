using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Drives character preview animations for the lobby screens.
///
/// Responsibilities:
///   1. Play a one-shot cinematic intro sequence per character type, then return to idle.
///   2. Run a natural, non-synchronized idle gesture loop with randomized delays.
///   3. Loop the dance animation for the girl's exclusive screen.
///
/// OCP: New character types only need a new case in BuildDefaultSequence().
/// DIP: Talks only to the Animator component — no coupling to gameplay scripts.
///
/// ─── SETUP ────────────────────────────────────────────────────────────────────
///  • Add this component to the root of each character preview prefab, OR
///    let the spawning script (CharacterSelectUI / SquadLineupDisplay) add it
///    at runtime via AddComponent().
///  • Set CharacterType in the Inspector or via code before calling Play methods.
///  • Animation State names (idleStateName, gestureStateNames, custom sequence
///    steps) must EXACTLY match states in the character's Animator Controller.
///  • The built-in default sequences use the state names listed in the guide.
///    Override them via customCinematicSequence in the Inspector.
/// </summary>
public class CharacterAnimationController : MonoBehaviour
{
    // =========================================================================
    //  Enums & Data Structs
    // =========================================================================

    /// <summary>Maps each lobby role to its animation behaviour.</summary>
    public enum CharacterType { Priest, Miner, Medic, Protector, Adventurer, Girl }

    /// <summary>One step in a cinematic animation sequence.</summary>
    [System.Serializable]
    public class AnimSequenceStep
    {
        [Tooltip("Exact Animator state name to play. Must match your Animator Controller.")]
        public string stateName;

        [Tooltip("Crossfade blend duration in seconds when entering this state.")]
        public float blendTime = 0.25f;

        [Tooltip("How many seconds to hold in this state before proceeding.")]
        public float holdTime = 2f;
    }

    // =========================================================================
    //  Inspector Fields
    // =========================================================================

    [Header("Character Identity")]
    [Tooltip("Determines which built-in cinematic sequence and defaults to use.")]
    public CharacterType characterType = CharacterType.Adventurer;

    [Header("Core State Names")]
    [Tooltip("Idle resting state. Used between sequences and after gestures.")]
    public string idleStateName = "Idle";

    [Header("Cinematic Intro Sequence")]
    [Tooltip("Leave empty to use the built-in default sequence for this CharacterType. " +
             "Populate with custom steps to fully override the sequence.")]
    public List<AnimSequenceStep> customCinematicSequence = new List<AnimSequenceStep>();

    [Header("Gesture Loop (Natural Idle Behaviour)")]
    [Tooltip("State names played at random while the character is lingering idle. " +
             "Leave empty to skip the gesture loop entirely.")]
    public List<string> gestureStateNames = new List<string>();

    [Tooltip("Minimum idle seconds before a gesture is triggered.")]
    [Range(2f, 30f)]
    public float minGestureDelay = 5f;

    [Tooltip("Maximum idle seconds before a gesture is triggered.")]
    [Range(3f, 60f)]
    public float maxGestureDelay = 15f;

    [Tooltip("How long each gesture plays before returning to idle.")]
    public float gestureDuration = 2.5f;

    [Tooltip("Crossfade blend time entering and exiting gesture states.")]
    public float gestureBlendTime = 0.3f;

    [Header("Girl Dance")]
    [Tooltip("Animator state used for the girl's dance on her exclusive screen.")]
    public string danceStateName = "Dance";

    [Tooltip("How long the dance animation lasts in seconds before returning to idle. " +
             "Match this to the actual length of your Dance animation clip.")]
    public float danceDuration = 4.5f;

    [Tooltip("Minimum idle seconds between dance repetitions.")]
    [Range(2f, 20f)]
    public float minDanceRepeatDelay = 5f;

    [Tooltip("Maximum idle seconds between dance repetitions.")]
    [Range(3f, 40f)]
    public float maxDanceRepeatDelay = 12f;

    // =========================================================================
    //  Private State
    // =========================================================================

    private Animator  _animator;
    private Coroutine _cinematicCoroutine;
    private Coroutine _gestureCoroutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null)
            _animator = GetComponent<Animator>();
    }

    void OnDisable()
    {
        StopAllRoutines();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Plays the character's cinematic intro sequence once (non-looping), then
    /// optionally starts the natural gesture loop while the player lingers.
    /// Safe to call multiple times — stops any in-progress sequence first.
    /// </summary>
    /// <param name="startGestureLoopAfter">
    ///   If true, the gesture loop begins automatically after the sequence ends.
    ///   Pass false if you want manual control (e.g. squad screen where timing
    ///   is staggered externally).
    /// </param>
    public void PlayCinematicSequence(bool startGestureLoopAfter = true)
    {
        StopAllRoutines();
        _cinematicCoroutine = StartCoroutine(RunCinematicSequence(startGestureLoopAfter));
    }

    /// <summary>
    /// Starts the natural idle gesture loop immediately, skipping the cinematic intro.
    /// Useful when the character should already be settled and only gesture occasionally.
    /// </summary>
    public void StartNaturalGestureLoop()
    {
        StopAllRoutines();
        CrossFadeTo(idleStateName, 0.3f);
        if (gestureStateNames.Count > 0)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    /// <summary>
    /// Plays the dance once, returns to idle, then after a random delay dances
    /// again — indefinitely. This is the natural, non-loopy behaviour for the
    /// girl's exclusive screen: she dances, rests, dances again organically.
    ///
    /// The dance animation clip itself should NOT be set to Loop Time in the
    /// Animator — the script manages the repetition timing via danceDuration.
    /// </summary>
    public void PlayDanceLoop()
    {
        StopAllRoutines();
        _cinematicCoroutine = StartCoroutine(RunDanceLoop());
    }

    /// <summary>
    /// Plays the dance state once without returning to idle or repeating.
    /// Useful for quick previews or when you need manual control.
    /// For the girl's screen, prefer PlayDanceLoop().
    /// </summary>
    public void PlayDance()
    {
        StopAllRoutines();
        CrossFadeTo(danceStateName, 0.5f);
    }

    /// <summary>
    /// Stops all animation routines and smoothly returns to idle.
    /// Call this when the screen is hidden or the character is deselected.
    /// </summary>
    public void ReturnToIdle()
    {
        StopAllRoutines();
        CrossFadeTo(idleStateName, 0.35f);
    }

    // =========================================================================
    //  Coroutines
    // =========================================================================

    private IEnumerator RunCinematicSequence(bool loopGesturesAfter)
    {
        if (_animator == null) yield break;

        List<AnimSequenceStep> sequence = GetActiveSequence();

        foreach (AnimSequenceStep step in sequence)
        {
            if (string.IsNullOrEmpty(step.stateName)) continue;
            CrossFadeTo(step.stateName, step.blendTime);
            yield return new WaitForSecondsRealtime(step.holdTime);
        }

        // Settle back into idle
        CrossFadeTo(idleStateName, 0.4f);
        _cinematicCoroutine = null;

        if (loopGesturesAfter && gestureStateNames.Count > 0)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    private IEnumerator RunGestureLoop()
    {
        if (_animator == null || gestureStateNames.Count == 0) yield break;

        while (true)
        {
            // Randomized idle pause — this is what makes gestures feel organic.
            // Each character instance has its own independent timer, so they
            // never sync up across the squad lineup.
            float delay = Random.Range(minGestureDelay, maxGestureDelay);
            yield return new WaitForSecondsRealtime(delay);

            // Pick and play a random gesture
            string gesture = gestureStateNames[Random.Range(0, gestureStateNames.Count)];
            if (!string.IsNullOrEmpty(gesture))
            {
                CrossFadeTo(gesture, gestureBlendTime);
                yield return new WaitForSecondsRealtime(gestureDuration);
                CrossFadeTo(idleStateName, gestureBlendTime);
                // Brief buffer so the return-to-idle crossfade settles
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
    }

    /// <summary>
    /// Dance-loop coroutine: plays the dance once, returns to idle, waits a random
    /// delay, then dances again — repeating indefinitely.
    ///
    /// This is the "natural" dance behaviour for the girl's exclusive screen.
    /// The timing is randomized so it never feels mechanical or predictable.
    /// </summary>
    private IEnumerator RunDanceLoop()
    {
        if (_animator == null) yield break;

        // ── First dance (plays immediately on Show) ──────────────────────────
        CrossFadeTo(danceStateName, 0.5f);
        yield return new WaitForSecondsRealtime(danceDuration);

        // Return to idle naturally
        CrossFadeTo(idleStateName, 0.45f);
        _cinematicCoroutine = null;

        // ── Repeat loop with random idle gaps ────────────────────────────────
        _gestureCoroutine = StartCoroutine(RunDanceRepeatLoop());
    }

    private IEnumerator RunDanceRepeatLoop()
    {
        if (_animator == null) yield break;

        while (true)
        {
            // Wait a random idle period — gives her a natural resting moment
            float delay = Random.Range(minDanceRepeatDelay, maxDanceRepeatDelay);
            yield return new WaitForSecondsRealtime(delay);

            // Dance again
            CrossFadeTo(danceStateName, 0.4f);
            yield return new WaitForSecondsRealtime(danceDuration);

            // Back to idle
            CrossFadeTo(idleStateName, 0.45f);
            // Brief settle buffer
            yield return new WaitForSecondsRealtime(0.6f);
        }
    }

    // =========================================================================
    //  Private Helpers
    // =========================================================================

    private void CrossFadeTo(string stateName, float blendTime)
    {
        if (_animator != null && !string.IsNullOrEmpty(stateName))
            _animator.CrossFadeInFixedTime(stateName, blendTime);
    }

    private void StopAllRoutines()
    {
        if (_cinematicCoroutine != null) { StopCoroutine(_cinematicCoroutine); _cinematicCoroutine = null; }
        if (_gestureCoroutine   != null) { StopCoroutine(_gestureCoroutine);   _gestureCoroutine   = null; }
    }

    /// <summary>
    /// Returns the Inspector-defined sequence if provided, otherwise falls back
    /// to the built-in default sequence for this CharacterType.
    /// </summary>
    private List<AnimSequenceStep> GetActiveSequence()
    {
        if (customCinematicSequence != null && customCinematicSequence.Count > 0)
            return customCinematicSequence;

        return BuildDefaultSequence(characterType);
    }

    // =========================================================================
    //  Default Sequences
    // =========================================================================

    /// <summary>
    /// OCP: Built-in default cinematic sequences per character type.
    /// Adding a new character type only requires a new case here — no other
    /// class changes needed.
    ///
    /// ─── STATE NAME GUIDE ─────────────────────────────────────────────────
    ///  Priest    → "Pray_Kneel", "Stand_Rise", "Pray_Standing", "Idle"
    ///  Miner     → "Squat", "Stand_Rise", "Idle"
    ///  Medic     → "Inspect_Hands", "Kick_Ground", "Idle"
    ///  Protector → "Point_Forward", "Idle"
    ///  Adventurer→ "LookAround_L", "LookAround_R", "Idle"
    ///  Girl      → "Dance", "Idle"
    ///
    /// These names must match states in each character's Animator Controller.
    /// Use the Inspector's customCinematicSequence to override with your actual
    /// state names if they differ.
    /// </summary>
    private static List<AnimSequenceStep> BuildDefaultSequence(CharacterType type)
    {
        switch (type)
        {
            case CharacterType.Priest:
                // Kneels and prays → stands → both-hands prayer → idle
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Pray_Kneel",    blendTime = 0.4f, holdTime = 2.5f },
                    new AnimSequenceStep { stateName = "Stand_Rise",    blendTime = 0.5f, holdTime = 1.2f },
                    new AnimSequenceStep { stateName = "Pray_Standing", blendTime = 0.4f, holdTime = 2.2f },
                    new AnimSequenceStep { stateName = "Idle",          blendTime = 0.5f, holdTime = 0.1f },
                };

            case CharacterType.Miner:
                // Squats → stands → idle
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Squat",      blendTime = 0.35f, holdTime = 1.8f },
                    new AnimSequenceStep { stateName = "Stand_Rise", blendTime = 0.4f,  holdTime = 1.0f },
                    new AnimSequenceStep { stateName = "Idle",       blendTime = 0.4f,  holdTime = 0.1f },
                };

            case CharacterType.Medic:
                // Inspects hands → kicks ground → idle
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Inspect_Hands", blendTime = 0.35f, holdTime = 2.2f },
                    new AnimSequenceStep { stateName = "Kick_Ground",   blendTime = 0.30f, holdTime = 1.5f },
                    new AnimSequenceStep { stateName = "Idle",          blendTime = 0.50f, holdTime = 0.1f },
                };

            case CharacterType.Protector:
                // Points forward → idle
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Point_Forward", blendTime = 0.30f, holdTime = 2.0f },
                    new AnimSequenceStep { stateName = "Idle",          blendTime = 0.50f, holdTime = 0.1f },
                };

            case CharacterType.Adventurer:
                // Looks left → looks right → idle
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "LookAround_L", blendTime = 0.30f, holdTime = 1.5f },
                    new AnimSequenceStep { stateName = "LookAround_R", blendTime = 0.30f, holdTime = 1.5f },
                    new AnimSequenceStep { stateName = "Idle",         blendTime = 0.40f, holdTime = 0.1f },
                };

            case CharacterType.Girl:
            default:
                // Dance loop entry then settle to idle (used only in cinematic mode;
                // PlayDance() keeps it looping for the girl's exclusive screen)
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Dance", blendTime = 0.5f, holdTime = 4.0f },
                    new AnimSequenceStep { stateName = "Idle",  blendTime = 0.5f, holdTime = 0.1f },
                };
        }
    }
}
