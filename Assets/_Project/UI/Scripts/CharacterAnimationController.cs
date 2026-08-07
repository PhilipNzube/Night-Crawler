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

        [Tooltip("How many seconds to hold in this state before proceeding. Leave at 3.5s or 0 for auto-detect clip duration.")]
        public float holdTime = 3.5f;
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

    [Header("Turning Animations")]
    [Tooltip("Exact state name for turning left before or during dance.")]
    public string turnLeftStateName = "Turn_Left";

    [Tooltip("Exact state name for turning right before or during dance.")]
    public string turnRightStateName = "Turn_Right";

    [Tooltip("Chance (0 to 1) to execute a left or right turn animation before starting a dance.")]
    [Range(0f, 1f)]
    public float turnBeforeDanceChance = 0.6f;

    [Tooltip("Duration in seconds to execute the turn state and rotation before starting dance.")]
    public float turnDuration = 1.0f;

    [Header("Dance Sequence Fallbacks")]
    [Tooltip("Default hold time (seconds) if a dance step's holdTime in Inspector is left at 0.")]
    public float defaultDanceHoldTime = 3.5f;

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
    [Tooltip("Simple list of state names — gesture plays for 'gestureDuration' seconds then returns to idle.")]
    public List<string> gestureStateNames = new List<string>();

    [Tooltip("Typed gesture steps with custom hold times per gesture — used instead of gestureStateNames if populated. " +
             "Works exactly like customDanceSequence: drag in state names and set hold time per entry.")]
    public List<AnimSequenceStep> idleGestureSteps = new List<AnimSequenceStep>();

    [Range(2f, 30f)] public float minGestureDelay = 5f;
    [Range(3f, 60f)] public float maxGestureDelay = 15f;
    [Tooltip("Hold duration (seconds) used for simple gestureStateNames entries. Ignored when idleGestureSteps is used.")]
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
    private int        _lastDanceIndex = -1;

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
        AnimSequenceStep step = GetNextRandomDanceStep(danceSteps);
        if (step != null && !string.IsNullOrEmpty(step.stateName))
            CrossFadeTo(step.stateName, step.blendTime);
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
            yield return StartCoroutine(WaitStepHoldTime(step));
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

        float minDelay  = preset != null ? preset.minGestureDelay : minGestureDelay;
        float maxDelay  = preset != null ? preset.maxGestureDelay : maxGestureDelay;
        string idleName = GetActiveIdleState();

        // Prefer typed idleGestureSteps (with per-entry hold times) over simple name list
        List<AnimSequenceStep> typedGestures = GetActiveIdleGestureSteps();
        if (typedGestures != null && typedGestures.Count > 0)
        {
            // Typed gesture loop
            while (true)
            {
                float delay = Random.Range(minDelay, maxDelay);
                yield return new WaitForSecondsRealtime(delay);

                AnimSequenceStep step = typedGestures[Random.Range(0, typedGestures.Count)];
                if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

                CrossFadeTo(step.stateName, step.blendTime > 0f ? step.blendTime : gestureBlendTime);
                yield return StartCoroutine(WaitStepHoldTime(step));
                CrossFadeTo(idleName, gestureBlendTime);
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }
        else
        {
            // Simple name list gesture loop (legacy)
            List<string> gestures = GetActiveGestureStates();
            if (gestures.Count == 0) yield break;

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
    }

    private IEnumerator RunDanceLoop()
    {
        if (_animator == null) yield break;

        List<AnimSequenceStep> danceSteps = GetActiveDanceSequence();
        if (danceSteps == null || danceSteps.Count == 0) yield break;

        string idleName = GetActiveIdleState();
        int dancesToPlay = Mathf.Max(danceSteps.Count, 3);

        for (int i = 0; i < dancesToPlay; i++)
        {
            AnimSequenceStep step = GetNextRandomDanceStep(danceSteps);
            if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

            if (Random.value < GetActiveTurnBeforeDanceChance())
            {
                yield return StartCoroutine(PerformTurnBeforeDance());
            }

            CrossFadeTo(step.stateName, step.blendTime);
            yield return StartCoroutine(WaitStepHoldTime(step));
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
            if (danceSteps != null && danceSteps.Count > 0)
            {
                int dancesToPlay = Mathf.Max(danceSteps.Count, 3);
                for (int i = 0; i < dancesToPlay; i++)
                {
                    AnimSequenceStep step = GetNextRandomDanceStep(danceSteps);
                    if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

                    if (Random.value < GetActiveTurnBeforeDanceChance())
                    {
                        yield return StartCoroutine(PerformTurnBeforeDance());
                    }

                    CrossFadeTo(step.stateName, step.blendTime);
                    yield return StartCoroutine(WaitStepHoldTime(step));
                }

                CrossFadeTo(idleName, 0.45f);
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }
    }

    /// <summary>
    /// Smoothly turns the character to random facing angles while dancing.
    /// Uses Turn_Left or Turn_Right animations when available.
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

            // Trigger turn animation based on turning direction
            float angleDelta = Vector3.SignedAngle(transform.forward, targetRotation * Vector3.forward, Vector3.up);
            bool isTurningLeft = angleDelta < 0f;
            TryCrossFadeTurnState(isTurningLeft, 0.25f);

            // Smoothly lerp rotation to target angle
            float elapsed = 0f;
            float turnDurationVal = preset != null ? preset.turnDuration : turnDuration;
            if (turnDurationVal <= 0.1f) turnDurationVal = 1.2f;
            Quaternion startRotation = transform.rotation;

            while (elapsed < turnDurationVal)
            {
                elapsed += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(startRotation, targetRotation, (elapsed / turnDurationVal) * turnSmoothSpeed);
                yield return null;
            }

            yield return new WaitForSecondsRealtime(waitTime);
        }
    }

    // =========================================================================
    //  Private Helpers & Resolution
    // =========================================================================

    private AnimSequenceStep GetNextRandomDanceStep(List<AnimSequenceStep> danceSteps)
    {
        if (danceSteps == null || danceSteps.Count == 0) return null;

        if (danceSteps.Count == 1)
        {
            _lastDanceIndex = 0;
            return danceSteps[0];
        }

        int randomIndex;
        int maxAttempts = 10;
        int attempts = 0;
        do
        {
            randomIndex = Random.Range(0, danceSteps.Count);
            attempts++;
        } while (randomIndex == _lastDanceIndex && attempts < maxAttempts);

        _lastDanceIndex = randomIndex;
        return danceSteps[randomIndex];
    }

    private IEnumerator PerformTurnBeforeDance()
    {
        if (_animator == null) yield break;

        bool isTurnLeft = Random.value < 0.5f;
        TryCrossFadeTurnState(isTurnLeft, 0.2f);

        if (!_pivotCaptured) CaptureInitialPivot();

        float maxAngle = preset != null ? preset.maxTurnAngle : maxTurnAngle;
        float turnAngle = isTurnLeft ? Random.Range(-maxAngle, -25f) : Random.Range(25f, maxAngle);

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = _initialRotation * Quaternion.Euler(0f, turnAngle, 0f);

        float duration = preset != null ? preset.turnDuration : turnDuration;
        if (duration <= 0.1f) duration = 1.0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, (elapsed / duration) * turnSmoothSpeed);
            yield return null;
        }
    }

    private void TryCrossFadeTurnState(bool isLeft, float blendTime)
    {
        if (_animator == null) return;

        string configured = isLeft ? GetActiveTurnLeftState() : GetActiveTurnRightState();
        string[] candidates = isLeft
            ? new string[] { configured, "Turn_Left", "Turn Left", "TurnLeft" }
            : new string[] { configured, "Turn_Right", "Turn Right", "TurnRight" };

        // Check each candidate against every animator layer before calling CrossFade.
        // CrossFadeInFixedTime(name, blend) without a layer index crashes with
        // "Invalid Layer Index '-1'" when the state doesn't exist anywhere.
        // Using HasState(layer, hash) lets us skip safely.
        int layerCount = _animator.layerCount;
        foreach (string candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;

            int hash = Animator.StringToHash(candidate);
            for (int layer = 0; layer < layerCount; layer++)
            {
                if (_animator.HasState(layer, hash))
                {
                    _animator.CrossFadeInFixedTime(candidate, blendTime, layer);
                    return;
                }
            }
        }
        // No matching state found — silently skip (no crash, no log spam).
    }

    private IEnumerator WaitStepHoldTime(AnimSequenceStep step)
    {
        float hold = step.holdTime;

        if (hold <= 0.05f)
        {
            yield return null;

            if (_animator != null)
            {
                AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.length > 0.1f)
                {
                    hold = stateInfo.length;
                }
            }

            if (hold <= 0.05f)
            {
                hold = preset != null ? preset.defaultDanceHoldTime : defaultDanceHoldTime;
                if (hold <= 0.05f) hold = 3.5f;
            }
        }

        yield return new WaitForSecondsRealtime(hold);
    }

    private string GetActiveTurnLeftState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.turnLeftStateName))
            return preset.turnLeftStateName;
        return string.IsNullOrEmpty(turnLeftStateName) ? "Turn_Left" : turnLeftStateName;
    }

    private string GetActiveTurnRightState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.turnRightStateName))
            return preset.turnRightStateName;
        return string.IsNullOrEmpty(turnRightStateName) ? "Turn_Right" : turnRightStateName;
    }

    private float GetActiveTurnBeforeDanceChance()
    {
        if (preset != null)
            return preset.turnBeforeDanceChance;
        return turnBeforeDanceChance;
    }

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

        // Directly trigger crossfade so Unity Animator resolves state by name or layer
        _animator.CrossFadeInFixedTime(stateName, blendTime);
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
        return gestureStateNames ?? new List<string>();
    }

    /// <summary>
    /// Returns typed gesture steps (with per-entry hold times) if any are configured.
    /// Preset takes priority. Falls back to the inspector idleGestureSteps list.
    /// Returns null/empty when none — caller falls back to the simple name list.
    /// </summary>
    private List<AnimSequenceStep> GetActiveIdleGestureSteps()
    {
        if (preset != null && preset.idleGestureSteps != null && preset.idleGestureSteps.Count > 0)
            return preset.idleGestureSteps;
        return idleGestureSteps;
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
