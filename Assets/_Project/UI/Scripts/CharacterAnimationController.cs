using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP & OCP: Drives character preview animations for lobby screens.
///
/// Features:
///   1. Cinematic Intro Sequences & Natural Linger Gesture Loops.
///   2. Multi-step Dance Routines for Vengeful Spirit / Girl.
///   3. Root Motion Control: Toggle `enableRootMotionInPreview` per character or preset.
///   4. Dance Random Rotation: Smoothly turns character to random facing angles while dancing,
///      keeping root motion movement organic and centered on the preview pivot.
/// </summary>
public class CharacterAnimationController : MonoBehaviour
{
    // =========================================================================
    //  Enums & Data Structs
    // =========================================================================

    public enum CharacterType { Priest, Miner, Medic, Protector, Adventurer, Girl }

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

    [Header("Root Motion Configuration")]
    [Tooltip("If true, Animator.applyRootMotion is enabled on this preview character. " +
             "Toggle this boolean in Inspector or Preset to enable/disable root motion for this character.")]
    public bool enableRootMotionInPreview = true;

    [Header("Character Identity")]
    [Tooltip("Determines which built-in default sequence to use if no custom steps are defined.")]
    public CharacterType characterType = CharacterType.Adventurer;

    [Header("Core State Names")]
    [Tooltip("Idle resting state. Used between sequences and after gestures.")]
    public string idleStateName = "Idle";

    [Header("Cinematic Intro Sequence")]
    public List<AnimSequenceStep> customCinematicSequence = new List<AnimSequenceStep>();

    [Header("Dance Routine Sequence")]
    public List<AnimSequenceStep> customDanceSequence = new List<AnimSequenceStep>();

    [Header("Dance Random Rotation & Organic Movement")]
    [Tooltip("If true, character smoothly turns to random facing angles while dancing, keeping movement organic.")]
    public bool enableRandomRotationOnDance = true;

    [Tooltip("Minimum seconds between picking a new dance facing angle.")]
    public float minTurnInterval = 1.5f;

    [Tooltip("Maximum seconds between picking a new dance facing angle.")]
    public float maxTurnInterval = 4.0f;

    [Tooltip("Maximum rotation angle variation (in degrees) relative to initial spawn facing.")]
    public float maxTurnAngle = 70f;

    [Tooltip("Smooth rotation lerp speed.")]
    public float turnSmoothSpeed = 2.5f;

    [Header("Gesture Loop (Natural Idle Behaviour)")]
    public List<string> gestureStateNames = new List<string>();

    [Range(2f, 30f)] public float minGestureDelay = 5f;
    [Range(3f, 60f)] public float maxGestureDelay = 15f;
    public float gestureDuration = 2.5f;
    public float gestureBlendTime = 0.3f;

    [Header("Dance Routine Repeat Timing")]
    [Range(2f, 20f)] public float minDanceRepeatDelay = 5f;
    [Range(3f, 40f)] public float maxDanceRepeatDelay = 12f;

    // =========================================================================
    //  Private State
    // =========================================================================

    private Animator   _animator;
    private Coroutine  _cinematicCoroutine;
    private Coroutine  _gestureCoroutine;
    private Coroutine  _rotationCoroutine;

    private Vector3    _initialPosition;
    private Quaternion _initialRotation;
    private bool       _pivotCaptured = false;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        if (_animator == null)
            _animator = GetComponent<Animator>();

        CaptureInitialPivot();
        ApplyRootMotionSettings();
    }

    void OnEnable()
    {
        CaptureInitialPivot();
        ApplyRootMotionSettings();
    }

    void OnDisable()
    {
        StopAllRoutines();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>Applies root motion setting to Animator according to Inspector or Preset.</summary>
    public void ApplyRootMotionSettings()
    {
        if (_animator == null) return;
        bool useRootMotion = preset != null ? preset.enableRootMotionInPreview : enableRootMotionInPreview;
        _animator.applyRootMotion = useRootMotion;
    }

    public void PlayCinematicSequence(bool startGestureLoopAfter = true)
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        _cinematicCoroutine = StartCoroutine(RunCinematicSequence(startGestureLoopAfter));
    }

    public void StartNaturalGestureLoop()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        CrossFadeTo(GetActiveIdleState(), 0.3f);
        if (GetActiveGestureStates().Count > 0)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    public void PlayDanceLoop()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        _cinematicCoroutine = StartCoroutine(RunDanceLoop());

        bool useRotation = preset != null ? preset.enableRandomRotationOnDance : enableRandomRotationOnDance;
        if (useRotation)
            _rotationCoroutine = StartCoroutine(RunRandomRotationLoop());
    }

    public void PlayDance()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        List<AnimSequenceStep> danceSteps = GetActiveDanceSequence();
        if (danceSteps.Count > 0)
            CrossFadeTo(danceSteps[0].stateName, danceSteps[0].blendTime);
    }

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

        CrossFadeTo(idleName, 0.45f);
        _cinematicCoroutine = null;

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

    /// <summary>
    /// Smoothly turns the character to random facing angles while dancing.
    /// If root motion moves her too far from origin, turns back toward pivot center.
    /// </summary>
    private IEnumerator RunRandomRotationLoop()
    {
        if (!_pivotCaptured) CaptureInitialPivot();

        float minInterval = preset != null ? preset.minTurnInterval : minTurnInterval;
        float maxInterval = preset != null ? preset.maxTurnInterval : maxTurnInterval;
        float maxAngle    = preset != null ? preset.maxTurnAngle    : maxTurnAngle;

        while (true)
        {
            float waitTime = Random.Range(minInterval, maxInterval);

            // Determine target facing angle
            Quaternion targetRotation;
            float distanceFromPivot = Vector3.Distance(transform.position, _initialPosition);

            if (distanceFromPivot > 1.8f)
            {
                // Drifted away — face back toward center pivot
                Vector3 directionToCenter = (_initialPosition - transform.position).normalized;
                directionToCenter.y = 0f;
                targetRotation = Quaternion.LookRotation(directionToCenter, Vector3.up);
            }
            else
            {
                // Pick a random organic angle relative to initial spawn facing
                float randomOffsetAngle = Random.Range(-maxAngle, maxAngle);
                targetRotation = _initialRotation * Quaternion.Euler(0f, randomOffsetAngle, 0f);
            }

            // Smoothly lerp rotation to target angle
            float elapsed = 0f;
            float turnDuration = 1.2f;
            Quaternion startRotation = transform.rotation;

            while (elapsed < turnDuration)
            {
                elapsed += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(startRotation, targetRotation, (elapsed / turnDuration) * turnSmoothSpeed);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(waitTime);
        }
    }

    // =========================================================================
    //  Private Helpers & Resolution
    // =========================================================================

    private void CaptureInitialPivot()
    {
        if (!_pivotCaptured)
        {
            _initialPosition = transform.position;
            _initialRotation = transform.rotation;
            _pivotCaptured   = true;
        }
    }

    private void CrossFadeTo(string stateName, float blendTime)
    {
        if (_animator == null || string.IsNullOrEmpty(stateName)) return;

        int stateHash = Animator.StringToHash(stateName);
        if (_animator.HasState(0, stateHash))
        {
            _animator.CrossFadeInFixedTime(stateName, blendTime);
        }
        else
        {
            int fallbackHash = Animator.StringToHash("Dance");
            if (stateName.StartsWith("Dance") && _animator.HasState(0, fallbackHash))
            {
                _animator.CrossFadeInFixedTime("Dance", blendTime);
                return;
            }

            Debug.LogWarning($"[CharacterAnimationController] State '{stateName}' not found in Animator Controller on '{_animator.gameObject.name}'. " +
                             $"Ensure your Animator Controller contains a state named '{stateName}'.");
        }
    }

    private void StopAllRoutines()
    {
        if (_cinematicCoroutine != null) { StopCoroutine(_cinematicCoroutine); _cinematicCoroutine = null; }
        if (_gestureCoroutine   != null) { StopCoroutine(_gestureCoroutine);   _gestureCoroutine   = null; }
        if (_rotationCoroutine  != null) { StopCoroutine(_rotationCoroutine);  _rotationCoroutine  = null; }
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
