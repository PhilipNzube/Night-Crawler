using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP & OCP: Drives character preview animations for the lobby screens.
///
/// Responsibilities:
///   1. Play a one-shot cinematic intro sequence per character type, then return to idle.
///   2. Run a natural, non-synchronized idle gesture loop with randomized delays.
///   3. Loop dance routine sequence for the girl's exclusive screen.
///
/// OCP: Zero-code modification! You can change state names, add 3rd/4th states, or change
///      timings directly in the Inspector or via CharacterAnimPresetSO assets without touching code.
/// DIP: Talks only to the Animator component — no coupling to gameplay scripts.
/// </summary>
public class CharacterAnimationController : MonoBehaviour
{
    // =========================================================================
    //  Enums & Data Structs
    // =========================================================================

    /// <summary>Maps each lobby role to its default animation behaviour.</summary>
    public enum CharacterType { Priest, Miner, Medic, Protector, Adventurer, Girl }

    /// <summary>One step in a cinematic or dance animation sequence.</summary>
    [System.Serializable]
    public class AnimSequenceStep
    {
        [Tooltip("Exact Animator state name to play. Must match your Animator Controller.")]
        public string stateName;

        [Tooltip("Crossfade blend duration in seconds when entering this state.")]
        public float blendTime = 0.25f;

        [Tooltip("How many seconds to hold in this state before proceeding.")]
        public float holdTime = 2.5f;
    }

    // =========================================================================
    //  Inspector Fields
    // =========================================================================

    [Header("Preset Asset (Optional - Overrides Inspector Settings)")]
    [Tooltip("Drag a CharacterAnimPresetSO asset here for shared reusable presets across prefabs.")]
    public CharacterAnimPresetSO preset;

    [Header("Character Identity")]
    [Tooltip("Determines which built-in default sequence to use if no custom steps are defined.")]
    public CharacterType characterType = CharacterType.Adventurer;

    [Header("Core State Names")]
    [Tooltip("Idle resting state. Used between sequences and after gestures.")]
    public string idleStateName = "Idle";

    [Header("Cinematic Intro Sequence")]
    [Tooltip("Populate with custom steps to fully override the intro sequence in Inspector. " +
             "Leave empty to use built-in defaults or preset.")]
    public List<AnimSequenceStep> customCinematicSequence = new List<AnimSequenceStep>();

    [Header("Dance Routine Sequence")]
    [Tooltip("Populate with dance steps (e.g. Dance_01, Dance_02) for the girl or showcase screen. " +
             "Leave empty to use built-in defaults or preset.")]
    public List<AnimSequenceStep> customDanceSequence = new List<AnimSequenceStep>();

    [Header("Gesture Loop (Natural Idle Behaviour)")]
    [Tooltip("State names played at random while the character is lingering idle.")]
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

    [Header("Dance Routine Repeat Timing")]
    [Tooltip("Minimum idle seconds between dance sequence repetitions.")]
    [Range(2f, 20f)]
    public float minDanceRepeatDelay = 5f;

    [Tooltip("Maximum idle seconds between dance sequence repetitions.")]
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
    public void PlayCinematicSequence(bool startGestureLoopAfter = true)
    {
        StopAllRoutines();
        _cinematicCoroutine = StartCoroutine(RunCinematicSequence(startGestureLoopAfter));
    }

    /// <summary>
    /// Starts the natural idle gesture loop immediately, skipping the cinematic intro.
    /// </summary>
    public void StartNaturalGestureLoop()
    {
        StopAllRoutines();
        CrossFadeTo(GetActiveIdleState(), 0.3f);
        if (GetActiveGestureStates().Count > 0)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    /// <summary>
    /// Plays the dance sequence (e.g. Dance_01 -> Dance_02), returns to idle,
    /// then after a random delay dances again — repeating indefinitely.
    ///
    /// Do NOT set "Loop Time" to true on the Dance clips inside Unity Animator —
    /// this script manages the sequence transitions and delay timing cleanly.
    /// </summary>
    public void PlayDanceLoop()
    {
        StopAllRoutines();
        _cinematicCoroutine = StartCoroutine(RunDanceLoop());
    }

    /// <summary>
    /// Plays the first dance state once without returning to idle or repeating.
    /// </summary>
    public void PlayDance()
    {
        StopAllRoutines();
        List<AnimSequenceStep> danceSteps = GetActiveDanceSequence();
        if (danceSteps.Count > 0)
            CrossFadeTo(danceSteps[0].stateName, danceSteps[0].blendTime);
    }

    /// <summary>
    /// Stops all animation routines and smoothly returns to idle.
    /// </summary>
    public void ReturnToIdle()
    {
        StopAllRoutines();
        CrossFadeTo(GetActiveIdleState(), 0.35f);
    }

    // =========================================================================
    //  Coroutines
    // =========================================================================

    private IEnumerator RunCinematicSequence(bool loopGesturesAfter)
    {
        if (_animator == null) yield break;

        List<AnimSequenceStep> sequence = GetActiveIntroSequence();

        foreach (AnimSequenceStep step in sequence)
        {
            if (string.IsNullOrEmpty(step.stateName)) continue;
            CrossFadeTo(step.stateName, step.blendTime);
            yield return new WaitForSecondsRealtime(step.holdTime);
        }

        // Settle back into idle
        CrossFadeTo(GetActiveIdleState(), 0.4f);
        _cinematicCoroutine = null;

        List<string> gestures = GetActiveGestureStates();
        if (loopGesturesAfter && gestures.Count > 0)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    private IEnumerator RunGestureLoop()
    {
        if (_animator == null) yield break;
        List<string> gestures = GetActiveGestureStates();
        if (gestures.Count == 0) yield break;

        float minDelay = preset != null ? preset.minGestureDelay : minGestureDelay;
        float maxDelay = preset != null ? preset.maxGestureDelay : maxGestureDelay;
        string idleName = GetActiveIdleState();

        while (true)
        {
            float delay = Random.Range(minDelay, maxDelay);
            yield return new WaitForSecondsRealtime(delay);

            string gesture = gestures[Random.Range(0, gestures.Count)];
            if (!string.IsNullOrEmpty(gesture))
            {
                CrossFadeTo(gesture, gestureBlendTime);
                yield return new WaitForSecondsRealtime(gestureDuration);
                CrossFadeTo(idleName, gestureBlendTime);
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
    }

    private IEnumerator RunDanceLoop()
    {
        if (_animator == null) yield break;

        List<AnimSequenceStep> danceSteps = GetActiveDanceSequence();
        string idleName = GetActiveIdleState();

        foreach (AnimSequenceStep step in danceSteps)
        {
            if (string.IsNullOrEmpty(step.stateName)) continue;
            CrossFadeTo(step.stateName, step.blendTime);
            yield return new WaitForSecondsRealtime(step.holdTime);
        }

        // Return to idle naturally
        CrossFadeTo(idleName, 0.45f);
        _cinematicCoroutine = null;

        // Repeat loop with random idle gaps
        _gestureCoroutine = StartCoroutine(RunDanceRepeatLoop());
    }

    private IEnumerator RunDanceRepeatLoop()
    {
        if (_animator == null) yield break;

        float minDelay = preset != null ? preset.minDanceRepeatDelay : minDanceRepeatDelay;
        float maxDelay = preset != null ? preset.maxDanceRepeatDelay : maxDanceRepeatDelay;
        string idleName = GetActiveIdleState();

        while (true)
        {
            float delay = Random.Range(minDelay, maxDelay);
            yield return new WaitForSecondsRealtime(delay);

            List<AnimSequenceStep> danceSteps = GetActiveDanceSequence();
            foreach (AnimSequenceStep step in danceSteps)
            {
                if (string.IsNullOrEmpty(step.stateName)) continue;
                CrossFadeTo(step.stateName, step.blendTime);
                yield return new WaitForSecondsRealtime(step.holdTime);
            }

            CrossFadeTo(idleName, 0.45f);
            yield return new WaitForSecondsRealtime(0.6f);
        }
    }

    // =========================================================================
    //  Private Helpers & Resolution
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

    private string GetActiveIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName))
            return preset.idleStateName;
        return string.IsNullOrEmpty(idleStateName) ? "Idle" : idleStateName;
    }

    private List<string> GetActiveGestureStates()
    {
        if (preset != null && preset.gestureStateNames != null && preset.gestureStateNames.Count > 0)
            return preset.gestureStateNames;
        return gestureStateNames;
    }

    private List<AnimSequenceStep> GetActiveIntroSequence()
    {
        if (preset != null && preset.introSequence != null && preset.introSequence.Count > 0)
            return preset.introSequence;

        if (customCinematicSequence != null && customCinematicSequence.Count > 0)
            return customCinematicSequence;

        return BuildDefaultIntroSequence(characterType);
    }

    private List<AnimSequenceStep> GetActiveDanceSequence()
    {
        if (preset != null && preset.danceSequence != null && preset.danceSequence.Count > 0)
            return preset.danceSequence;

        if (customDanceSequence != null && customDanceSequence.Count > 0)
            return customDanceSequence;

        return BuildDefaultDanceSequence(characterType);
    }

    // =========================================================================
    //  Default Sequences
    // =========================================================================

    /// <summary>
    /// Built-in default intro sequences per character type.
    /// Matches exact user specs:
    ///   Adventurer:         Idle -> Look_Around
    ///   Priest:             Idle -> Pray_Kneel -> Pray_Standing
    ///   Hazard Specialist:  Idle -> Point
    ///   Medic:              Idle -> Inspect_Hands -> Kick_Ground
    ///   Miner:              Idle -> Squat -> Standing
    ///   Girl:               Idle -> Dance_01 -> Dance_02
    /// </summary>
    private static List<AnimSequenceStep> BuildDefaultIntroSequence(CharacterType type)
    {
        switch (type)
        {
            case CharacterType.Priest:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Pray_Kneel",    blendTime = 0.4f, holdTime = 2.5f },
                    new AnimSequenceStep { stateName = "Pray_Standing", blendTime = 0.4f, holdTime = 2.2f },
                };

            case CharacterType.Miner:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Squat",    blendTime = 0.35f, holdTime = 1.8f },
                    new AnimSequenceStep { stateName = "Standing", blendTime = 0.4f,  holdTime = 1.2f },
                };

            case CharacterType.Medic:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Inspect_Hands", blendTime = 0.35f, holdTime = 2.2f },
                    new AnimSequenceStep { stateName = "Kick_Ground",   blendTime = 0.30f, holdTime = 1.5f },
                };

            case CharacterType.Protector:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Point", blendTime = 0.30f, holdTime = 2.0f },
                };

            case CharacterType.Adventurer:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Look_Around", blendTime = 0.30f, holdTime = 2.0f },
                };

            case CharacterType.Girl:
            default:
                return new List<AnimSequenceStep>
                {
                    new AnimSequenceStep { stateName = "Dance_01", blendTime = 0.4f, holdTime = 3.5f },
                    new AnimSequenceStep { stateName = "Dance_02", blendTime = 0.4f, holdTime = 3.5f },
                };
        }
    }

    private static List<AnimSequenceStep> BuildDefaultDanceSequence(CharacterType type)
    {
        return new List<AnimSequenceStep>
        {
            new AnimSequenceStep { stateName = "Dance_01", blendTime = 0.4f, holdTime = 3.5f },
            new AnimSequenceStep { stateName = "Dance_02", blendTime = 0.4f, holdTime = 3.5f },
        };
    }
}
