using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP &amp; OCP: Drives character preview animations for lobby, squad screen, and cinematic screens.
///
/// ─────────────────────────────────────────────────────────────────
/// HOW TO USE — SET THE STARTUP MODE IN THE INSPECTOR:
///
///   Lobby_LoopIdle     → Loops 'Lobby Idle State' forever. NO gestures, NO dance. Pure idle for investigators.
///   Girl_DanceRoutine  → Plays random dance steps with pauses, organic turns & repeating routines. Perfect for the Girl in the Lobby.
///   Squad_Gestures     → Idles with random gestures from 'Squad Gesture Steps' firing periodically.
///   Cinematic          → Plays 'Cinematic Intro Sequence' in order, then hands off to gestures.
///   Manual             → Does nothing on Enable. You call the public API yourself.
///
/// LEAVE LISTS EMPTY: Empty lists = character stays in idle. Nothing breaks.
/// ─────────────────────────────────────────────────────────────────
/// </summary>
public class CharacterAnimationController : MonoBehaviour
{
    // =========================================================================
    //  Enums & Data Structs
    // =========================================================================

    public enum StartupMode
    {
        [Tooltip("Loops one animation state endlessly. Gestures & Dances NEVER fire. Use for normal Investigators in Lobby.")]
        Lobby_LoopIdle = 0,

        [Tooltip("Plays a looping dance routine (random dance steps, repeat delay, organic turns). Perfect for the Girl in the Lobby.")]
        Girl_DanceRoutine = 1,

        [Tooltip("Idles and randomly plays gestures from 'Squad Gesture Steps'. Use for Investigators in Squad Screen.")]
        Squad_Gestures = 2,

        [Tooltip("Plays 'Cinematic Intro Sequence' then starts gesture loop. Use for character reveal.")]
        Cinematic = 3,

        [Tooltip("Does nothing on Enable. You control this via public API calls in code.")]
        Manual = 4,

        [Tooltip("Legacy alias for Girl_DanceRoutine.")]
        Squad_DanceLoop = Girl_DanceRoutine
    }

    /// <summary>Legacy enum kept for backward compatibility with SquadLineupDisplay and other callers.</summary>
    public enum CharacterType { Priest, Miner, Medic, Protector, Adventurer, Girl }

    [System.Serializable]
    public class AnimSequenceStep
    {
        [Tooltip("Exact Animator state name to play. Must match your Animator Controller.")]
        public string stateName;

        [Tooltip("Crossfade blend duration in seconds when entering this state.")]
        public float blendTime = 0.25f;

        [Tooltip("How many seconds to hold in this state before proceeding. Leave at 0 to auto-detect clip length.")]
        public float holdTime = 3.5f;

        [Tooltip("If true, this step loops endlessly and NEVER moves to the next step.")]
        public bool loop = false;
    }

    // =========================================================================
    //  Inspector Fields
    // =========================================================================

    [Header("Preset Asset (Optional — Overrides All Inspector Fields)")]
    [Tooltip("Drag a CharacterAnimPresetSO asset here for shared reusable presets across prefabs.")]
    public CharacterAnimPresetSO preset;

    [Header("Root Motion")]
    [Tooltip("Enables Animator.applyRootMotion on this preview character.")]
    public bool enableRootMotionInPreview = true;

    // =========================================================================
    //  STARTUP MODE
    //  This single dropdown decides what plays automatically when the screen opens.
    // =========================================================================

    [Header("══ STARTUP MODE ══════════════════════════════════════")]
    [Tooltip(
        "Lobby_LoopIdle     → Pure idle loop for investigators. Gestures never fire.\n" +
        "Girl_DanceRoutine  → Looping dance routine for Girl character in Lobby.\n" +
        "Squad_Gestures     → Idle + random gestures from 'Squad Gesture Steps'.\n" +
        "Cinematic          → Plays intro sequence then gestures.\n" +
        "Manual             → Does nothing automatically.")]
    public StartupMode startupMode = StartupMode.Lobby_LoopIdle;

    // =========================================================================
    //  LOBBY & CHARACTER SELECTION (Investigators)
    //  Used when in the Main Lobby / Character Select screen
    // =========================================================================

    [Header("── Lobby & Selection (Investigators)")]
    [Tooltip("Animator state to play during resting idle in the Lobby / Character Select screen.")]
    public string lobbyIdleState = "Idle";

    [Tooltip("If true (default), allows AFK gestures to play after inactivity in the Lobby / Character Select screen.\n" +
             "If false, character will only loop the resting idle animation.")]
    public bool allowLobbyGestures = true;

    [Tooltip("Gestures that play after the inactivity delay in the Lobby / Character Select screen.\n" +
             "If this list is empty, character will simply continue playing the resting idle animation.")]
    public List<AnimSequenceStep> lobbyGestureSteps = new List<AnimSequenceStep>();

    [Tooltip("Simple gesture state names for Lobby (fallback if lobbyGestureSteps is empty).")]
    public List<string> lobbySimpleGestureNames = new List<string>();

    // =========================================================================
    //  GIRL & DANCE ROUTINE SETTINGS (Lobby & Girl Screen)
    //  Used when startupMode == Girl_DanceRoutine or via PlayDanceLoop()
    // =========================================================================

    [Header("── Girl / Dance Routine Settings (Startup Mode: Girl_DanceRoutine)")]

    [Tooltip("Resting idle state used between dance routines.")]
    public string danceIdleState = "Idle";

    [Tooltip("List of dance animation states for the Girl. Steps are picked at random to create organic variety.")]
    public List<AnimSequenceStep> danceSteps = new List<AnimSequenceStep>();

    [Tooltip("If true (default), dance routine repeats after a delay. If false, plays through dance steps once then stays in idle.")]
    public bool allowDanceRepeat = true;

    [Range(2f, 20f)]
    [Tooltip("Min seconds of idle rest between dance routine repetitions.")]
    public float minDanceRepeatDelay = 5f;

    [Range(3f, 40f)]
    [Tooltip("Max seconds of idle rest between dance routine repetitions.")]
    public float maxDanceRepeatDelay = 12f;

    [Tooltip("Seconds to hold in resting idle after a dance move before turning and starting the next move.")]
    public float idleBetweenDancesDuration = 0.8f;

    [Tooltip("If true, character returns to idle and smoothly turns to a new facing angle before playing the next dance move.")]
    public bool enableRandomRotationOnDance = true;

    [Tooltip("Max rotation angle variation (degrees) from initial facing between dances.")]
    public float maxTurnAngle = 70f;

    [Tooltip("Smooth rotation lerp speed.")]
    public float turnSmoothSpeed = 2.5f;

    [Tooltip("Animator state name for turning left. Leave blank to skip turn anim.")]
    public string turnLeftStateName = "Turn_Left";

    [Tooltip("Animator state name for turning right. Leave blank to skip turn anim.")]
    public string turnRightStateName = "Turn_Right";

    [Tooltip("Chance (0–1) to turn to a new angle between dance moves.")]
    [Range(0f, 1f)]
    public float turnBeforeDanceChance = 0.8f;

    [Tooltip("Duration in seconds to execute the turn anim and rotation.")]
    public float turnDuration = 1.0f;

    [Tooltip("Default hold time (seconds) if a dance step's holdTime is left at 0.")]
    public float defaultDanceHoldTime = 3.5f;



    // =========================================================================
    //  SQUAD GESTURE SETTINGS (Investigators)
    //  Used when startupMode == Squad_Gestures
    // =========================================================================

    [Header("── Squad Gesture Settings (Investigators - Startup Mode: Squad_Gestures)")]

    [Tooltip("Resting idle state used between gestures in the Squad screen.")]
    public string squadIdleState = "Idle";

    [Tooltip("Random gestures that fire periodically while idling. Used by Squad_Gestures mode.\n" +
             "Typed version with per-entry hold times. Preferred over 'Squad Simple Gesture Names'.")]
    public List<AnimSequenceStep> squadGestureSteps = new List<AnimSequenceStep>();

    [Tooltip("Simple gesture state names (fallback if Gesture Steps above is empty).")]
    public List<string> squadSimpleGestureNames = new List<string>();

    [Range(2f, 30f)]
    [Tooltip("Min seconds to wait before firing a gesture.")]
    public float minGestureDelay = 5f;

    [Range(3f, 60f)]
    [Tooltip("Max seconds to wait before firing a gesture.")]
    public float maxGestureDelay = 15f;

    [Tooltip("Hold duration in seconds used for Simple Gesture Names entries (ignored when Gesture Steps are used).")]
    public float gestureDuration  = 2.5f;
    public float gestureBlendTime = 0.3f;

    // =========================================================================
    //  CINEMATIC SETTINGS
    //  Used when startupMode == Cinematic
    // =========================================================================

    [Header("── Cinematic Settings (Startup Mode: Cinematic)")]

    [Tooltip("Idle resting state used after the cinematic sequence finishes.")]
    public string cinematicIdleState = "Idle";

    [Tooltip("Ordered sequence of animation states played when startupMode is Cinematic.\n" +
             "If empty, character immediately enters idle — no crash, no default animations.")]
    public List<AnimSequenceStep> cinematicIntroSequence = new List<AnimSequenceStep>();

    [Tooltip("If true, random gestures from 'Cinematic Gesture Steps' fire after the intro finishes.")]
    public bool cinematicStartGesturesAfterIntro = true;

    [Tooltip("Gesture steps that fire after the cinematic intro sequence finishes.")]
    public List<AnimSequenceStep> cinematicGestureSteps = new List<AnimSequenceStep>();

    [Tooltip("Simple gesture state names (fallback if Cinematic Gesture Steps is empty).")]
    public List<string> cinematicSimpleGestureNames = new List<string>();

    // =========================================================================
    //  LEGACY FIELDS (kept for backward compatibility with existing assets & scripts)
    // =========================================================================
    [HideInInspector] public string idleStateName = "Idle";
    [HideInInspector] public string previewIdleState = "Idle";
    [HideInInspector] public bool allowGestures = true;
    [HideInInspector] public bool allowPreviewGestures = true;
    [HideInInspector] public CharacterType characterType = CharacterType.Adventurer;
    [HideInInspector] public List<AnimSequenceStep> customCinematicSequence = new List<AnimSequenceStep>();
    [HideInInspector] public List<AnimSequenceStep> customDanceSequence      = new List<AnimSequenceStep>();
    [HideInInspector] public List<AnimSequenceStep> squadDanceSteps          = new List<AnimSequenceStep>();
    [HideInInspector] public List<AnimSequenceStep> previewGestureSteps      = new List<AnimSequenceStep>();
    [HideInInspector] public List<string>           previewSimpleGestureNames= new List<string>();
    [HideInInspector] public List<AnimSequenceStep> idleGestureSteps         = new List<AnimSequenceStep>();
    [HideInInspector] public List<string>           gestureStateNames        = new List<string>();

    // =========================================================================
    //  Private State
    // =========================================================================

    private Animator   _animator;
    private Coroutine  _mainCoroutine;
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
        ExecuteStartupMode();
    }

    void OnDisable()
    {
        StopAllRoutines();
    }

    // =========================================================================
    //  Startup Dispatch
    // =========================================================================

    private void ExecuteStartupMode()
    {
        // Always check for preset override first
        if (preset != null)
        {
            PlayCinematicSequence(true);
            return;
        }

        switch (startupMode)
        {
            case StartupMode.Lobby_LoopIdle:
                LoopAnimation(lobbyIdleState);        // Pure idle: Gestures & Dance NEVER start in this path
                break;

            case StartupMode.Girl_DanceRoutine:
                PlayDanceLoop();                      // Dances + repeats for the Girl in Lobby
                break;

            case StartupMode.Squad_Gestures:
                StartNaturalGestureLoop();
                break;

            case StartupMode.Cinematic:
                PlayCinematicSequence(cinematicStartGesturesAfterIntro);
                break;

            case StartupMode.Manual:
                // Do nothing — caller controls this via public API
                break;
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>Applies root motion setting from Inspector or Preset.</summary>
    public void ApplyRootMotionSettings()
    {
        if (_animator == null) return;
        bool useRootMotion = preset != null ? preset.enableRootMotionInPreview : enableRootMotionInPreview;
        _animator.applyRootMotion = useRootMotion;
    }

    /// <summary>
    /// Loops a specific animation state endlessly.
    /// Gestures & Dances NEVER fire when using this — it is a pure, isolated loop.
    /// </summary>
    public void LoopAnimation(string stateName)
    {
        StopAllRoutines(); // cancels any gesture or dance coroutines too
        ApplyRootMotionSettings();
        if (string.IsNullOrEmpty(stateName)) stateName = GetResolvedIdleState();
        _mainCoroutine = StartCoroutine(RunSingleAnimationLoop(stateName));
    }

    /// <summary>Loops the lobby idle state. Shortcut for LoopAnimation(lobbyIdleState).</summary>
    public void LoopCurrentIdle() => LoopAnimation(GetResolvedIdleState());

    /// <summary>
    /// Plays the cinematic intro sequence, then optionally starts gestures.
    /// Uses cinematicIntroSequence (or Preset introSequence if assigned).
    /// </summary>
    public void PlayCinematicSequence(bool startGestureLoopAfter = true)
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        _mainCoroutine = StartCoroutine(RunCinematicSequence(startGestureLoopAfter));
    }

    /// <summary>
    /// Starts the idle + gesture loop using Squad gesture settings.
    /// Gestures fire randomly from squadGestureSteps / squadSimpleGestureNames.
    /// Has no effect if allowGestures is false.
    /// </summary>
    public void StartNaturalGestureLoop()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        CrossFadeTo(GetResolvedSquadIdleState(), 0.3f);

        if (!CanStartGestures()) return;  // hard gate

        bool hasTyped  = squadGestureSteps != null && squadGestureSteps.Count > 0;
        bool hasSimple = squadSimpleGestureNames != null && squadSimpleGestureNames.Count > 0;
        if (hasTyped || hasSimple)
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    /// <summary>
    /// Starts the idle gesture loop for the Lobby / Character Selection Screen.
    /// Uses 'lobbyGestureSteps'. If no gestures are assigned or allowLobbyGestures is false,
    /// holds the resting idle animation.
    /// </summary>
    public void StartPreviewGestureLoop()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        CrossFadeTo(GetResolvedIdleState(), 0.3f);

        if (!allowLobbyGestures) return;

        var typed  = GetActiveLobbyGestureSteps();
        var simple = GetActiveLobbySimpleGestureNames();
        if ((typed != null && typed.Count > 0) || (simple != null && simple.Count > 0))
            _gestureCoroutine = StartCoroutine(RunPreviewGestureLoop());
    }

    /// <summary>
    /// Plays random dance steps from danceSteps in a loop.
    /// Each dance finishes, returns to idle, then turns to a new angle before the next dance.
    /// </summary>
    public void PlayDanceLoop()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        _mainCoroutine = StartCoroutine(RunDanceLoop());
    }

    public void PlayDance()
    {
        StopAllRoutines();
        ApplyRootMotionSettings();
        List<AnimSequenceStep> danceList = GetActiveDanceSteps();
        AnimSequenceStep step = GetNextRandomDanceStep(danceList);
        if (step != null && !string.IsNullOrEmpty(step.stateName))
            CrossFadeTo(step.stateName, step.blendTime);
    }

    public void ReturnToIdle()
    {
        StopAllRoutines();
        CrossFadeTo(GetResolvedIdleState(), 0.35f);
    }

    // =========================================================================
    //  Coroutines
    // =========================================================================

    private IEnumerator RunSingleAnimationLoop(string stateName)
    {
        if (_animator == null) yield break;
        CrossFadeTo(stateName, 0.3f);
        while (true)
            yield return null;
    }

    private IEnumerator RunCinematicSequence(bool startGesturesAfter)
    {
        if (_animator == null) yield break;

        List<AnimSequenceStep> sequence = GetActiveCinematicSequence();

        if (sequence == null || sequence.Count == 0)
        {
            CrossFadeTo(GetResolvedCinematicIdleState(), 0.4f);
            _mainCoroutine = null;
            yield break;
        }

        foreach (AnimSequenceStep step in sequence)
        {
            if (string.IsNullOrEmpty(step.stateName)) continue;
            CrossFadeTo(step.stateName, step.blendTime);

            if (step.loop)
            {
                while (true) yield return null; // hold this step forever
            }

            yield return StartCoroutine(WaitStepHoldTime(step));
        }

        CrossFadeTo(GetResolvedCinematicIdleState(), 0.4f);
        _mainCoroutine = null;

        if (startGesturesAfter && CanStartGestures() && HasAnyCinematicGestures())
            _gestureCoroutine = StartCoroutine(RunGestureLoop());
    }

    private IEnumerator RunGestureLoop()
    {
        if (_animator == null) yield break;

        float minDelay  = preset != null ? preset.minGestureDelay : minGestureDelay;
        float maxDelay  = preset != null ? preset.maxGestureDelay : maxGestureDelay;
        string idleName = GetResolvedGestureIdleState();

        // Typed gesture steps take priority
        List<AnimSequenceStep> typedGestures = GetActiveGestureSteps();
        if (typedGestures != null && typedGestures.Count > 0)
        {
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
            // Simple name list
            List<string> simpleGestures = GetActiveSimpleGestureNames();
            if (simpleGestures == null || simpleGestures.Count == 0) yield break;

            while (true)
            {
                float delay = Random.Range(minDelay, maxDelay);
                yield return new WaitForSecondsRealtime(delay);

                string gesture = simpleGestures[Random.Range(0, simpleGestures.Count)];
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

    private IEnumerator RunPreviewGestureLoop()
    {
        if (_animator == null) yield break;

        float minDelay  = preset != null ? preset.minGestureDelay : minGestureDelay;
        float maxDelay  = preset != null ? preset.maxGestureDelay : maxGestureDelay;
        string idleName = GetResolvedIdleState();

        var typed  = GetActiveLobbyGestureSteps();
        var simple = GetActiveLobbySimpleGestureNames();

        if (typed != null && typed.Count > 0)
        {
            while (true)
            {
                float delay = Random.Range(minDelay, maxDelay);
                yield return new WaitForSecondsRealtime(delay);

                AnimSequenceStep step = typed[Random.Range(0, typed.Count)];
                if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

                CrossFadeTo(step.stateName, step.blendTime > 0f ? step.blendTime : gestureBlendTime);
                yield return StartCoroutine(WaitStepHoldTime(step));
                CrossFadeTo(idleName, gestureBlendTime);
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }
        else if (simple != null && simple.Count > 0)
        {
            while (true)
            {
                float delay = Random.Range(minDelay, maxDelay);
                yield return new WaitForSecondsRealtime(delay);

                string gesture = simple[Random.Range(0, simple.Count)];
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

        List<AnimSequenceStep> danceList = GetActiveDanceSteps();
        if (danceList == null || danceList.Count == 0)
        {
            CrossFadeTo(GetResolvedDanceIdleState(), 0.4f);
            yield break;
        }

        string idleName    = GetResolvedDanceIdleState();
        int dancesToPlay   = Mathf.Max(danceList.Count, 3);

        for (int i = 0; i < dancesToPlay; i++)
        {
            AnimSequenceStep step = GetNextRandomDanceStep(danceList);
            if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

            // 1. Play the dance move (she dances facing her current direction — no rotating while dancing)
            CrossFadeTo(step.stateName, step.blendTime);

            if (step.loop)
            {
                while (true) yield return null;
            }

            yield return StartCoroutine(WaitStepHoldTime(step));

            // 2. Revert back to resting idle after the dance move finishes
            CrossFadeTo(idleName, 0.35f);
            yield return new WaitForSecondsRealtime(idleBetweenDancesDuration);

            // 3. Now play the turn animation and rotate to face the new angle before next dance
            bool useRotation = preset != null ? preset.enableRandomRotationOnDance : enableRandomRotationOnDance;
            if (useRotation && (i < dancesToPlay - 1) && Random.value < GetActiveTurnBeforeDanceChance())
            {
                yield return StartCoroutine(PerformTurnToNewAngle());
                CrossFadeTo(idleName, 0.25f);
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        CrossFadeTo(idleName, 0.45f);
        _mainCoroutine = null;

        if (allowDanceRepeat)
            _gestureCoroutine = StartCoroutine(RunDanceRepeatLoop());
    }

    private IEnumerator RunDanceRepeatLoop()
    {
        if (_animator == null) yield break;

        float minDelay  = preset != null ? preset.minDanceRepeatDelay : minDanceRepeatDelay;
        float maxDelay  = preset != null ? preset.maxDanceRepeatDelay : maxDanceRepeatDelay;
        string idleName = GetResolvedDanceIdleState();

        while (true)
        {
            float delay = Random.Range(minDelay, maxDelay);
            yield return new WaitForSecondsRealtime(delay);

            List<AnimSequenceStep> danceList = GetActiveDanceSteps();
            if (danceList != null && danceList.Count > 0)
            {
                int dancesToPlay = Mathf.Max(danceList.Count, 3);
                for (int i = 0; i < dancesToPlay; i++)
                {
                    // Turn to a new angle between dances
                    bool useRotation = preset != null ? preset.enableRandomRotationOnDance : enableRandomRotationOnDance;
                    if (useRotation && Random.value < GetActiveTurnBeforeDanceChance())
                    {
                        yield return StartCoroutine(PerformTurnToNewAngle());
                        CrossFadeTo(idleName, 0.25f);
                        yield return new WaitForSecondsRealtime(0.2f);
                    }

                    AnimSequenceStep step = GetNextRandomDanceStep(danceList);
                    if (step == null || string.IsNullOrEmpty(step.stateName)) continue;

                    // 1. Play the dance move
                    CrossFadeTo(step.stateName, step.blendTime);
                    yield return StartCoroutine(WaitStepHoldTime(step));

                    // 2. Revert back to resting idle after the dance move finishes
                    CrossFadeTo(idleName, 0.35f);
                    yield return new WaitForSecondsRealtime(idleBetweenDancesDuration);
                }

                CrossFadeTo(idleName, 0.45f);
                yield return new WaitForSecondsRealtime(0.6f);
            }
        }
    }

    private IEnumerator PerformTurnToNewAngle()
    {
        if (_animator == null) yield break;
        if (!_pivotCaptured) CaptureInitialPivot();

        float maxAngle = preset != null ? preset.maxTurnAngle : maxTurnAngle;

        // Determine target facing angle
        Quaternion targetRot;
        float distanceFromPivot = Vector3.Distance(transform.position, _initialPosition);

        if (distanceFromPivot > 1.8f)
        {
            // If drifted away from root motion, face back toward center pivot
            Vector3 directionToCenter = (_initialPosition - transform.position).normalized;
            directionToCenter.y = 0f;
            targetRot = Quaternion.LookRotation(directionToCenter, Vector3.up);
        }
        else
        {
            // Pick a random organic angle relative to initial spawn facing
            float randomOffsetAngle = Random.Range(-maxAngle, maxAngle);
            targetRot = _initialRotation * Quaternion.Euler(0f, randomOffsetAngle, 0f);
        }

        // Determine turning direction and play turn animation
        float angleDelta = Vector3.SignedAngle(transform.forward, targetRot * Vector3.forward, Vector3.up);
        if (Mathf.Abs(angleDelta) > 10f)
        {
            bool isTurnLeft = angleDelta < 0f;
            TryCrossFadeTurnState(isTurnLeft, 0.2f);
        }

        Quaternion startRot = transform.rotation;
        float duration = preset != null ? preset.turnDuration : turnDuration;
        if (duration <= 0.1f) duration = 1.0f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.rotation = Quaternion.Slerp(startRot, targetRot, t * turnSmoothSpeed);
            yield return null;
        }

        transform.rotation = targetRot;
    }

    // =========================================================================
    //  Private Helpers
    // =========================================================================

    private AnimSequenceStep GetNextRandomDanceStep(List<AnimSequenceStep> danceList)
    {
        if (danceList == null || danceList.Count == 0) return null;
        if (danceList.Count == 1) { _lastDanceIndex = 0; return danceList[0]; }

        int randomIndex;
        int attempts = 0;
        do { randomIndex = Random.Range(0, danceList.Count); attempts++; }
        while (randomIndex == _lastDanceIndex && attempts < 10);

        _lastDanceIndex = randomIndex;
        return danceList[randomIndex];
    }

    private void TryCrossFadeTurnState(bool isLeft, float blendTime)
    {
        if (_animator == null) return;

        string configured = isLeft ? GetActiveTurnLeftState() : GetActiveTurnRightState();
        string[] candidates = isLeft
            ? new string[] { configured, "Turn_Left",  "Turn Left",  "TurnLeft"  }
            : new string[] { configured, "Turn_Right", "Turn Right", "TurnRight" };

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
                    hold = stateInfo.length;
            }
            if (hold <= 0.05f)
            {
                hold = preset != null ? preset.defaultDanceHoldTime : defaultDanceHoldTime;
                if (hold <= 0.05f) hold = 3.5f;
            }
        }

        yield return new WaitForSecondsRealtime(hold);
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
        _animator.CrossFadeInFixedTime(stateName, blendTime);
    }

    private void StopAllRoutines()
    {
        if (_mainCoroutine     != null) { StopCoroutine(_mainCoroutine);     _mainCoroutine     = null; }
        if (_gestureCoroutine  != null) { StopCoroutine(_gestureCoroutine);  _gestureCoroutine  = null; }
        if (_rotationCoroutine != null) { StopCoroutine(_rotationCoroutine); _rotationCoroutine = null; }
    }

    /// <summary>
    /// Hard gate for gesture coroutines. Returns false when allowGestures is off,
    /// ensuring NO gesture can ever start regardless of caller.
    /// </summary>
    private bool CanStartGestures() => allowGestures;

    // ─── State Name Resolution ───────────────────────────────────────────────

    private List<AnimSequenceStep> GetActiveLobbyGestureSteps()
    {
        if (lobbyGestureSteps != null && lobbyGestureSteps.Count > 0)
            return lobbyGestureSteps;
        if (previewGestureSteps != null && previewGestureSteps.Count > 0)
            return previewGestureSteps;
        return null;
    }

    private List<string> GetActiveLobbySimpleGestureNames()
    {
        if (lobbySimpleGestureNames != null && lobbySimpleGestureNames.Count > 0)
            return lobbySimpleGestureNames;
        if (previewSimpleGestureNames != null && previewSimpleGestureNames.Count > 0)
            return previewSimpleGestureNames;
        return null;
    }

    private string GetResolvedIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName)) return preset.idleStateName;
        if (!string.IsNullOrEmpty(lobbyIdleState)) return lobbyIdleState;
        return "Idle";
    }

    private string GetResolvedPreviewIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName)) return preset.idleStateName;
        if (!string.IsNullOrEmpty(previewIdleState)) return previewIdleState;
        if (!string.IsNullOrEmpty(lobbyIdleState)) return lobbyIdleState;
        return "Idle";
    }

    private string GetResolvedDanceIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName)) return preset.idleStateName;
        if (!string.IsNullOrEmpty(danceIdleState)) return danceIdleState;
        return !string.IsNullOrEmpty(lobbyIdleState) ? lobbyIdleState : "Idle";
    }

    private string GetResolvedSquadIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName)) return preset.idleStateName;
        return !string.IsNullOrEmpty(squadIdleState) ? squadIdleState : "Idle";
    }

    private string GetResolvedCinematicIdleState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.idleStateName)) return preset.idleStateName;
        return !string.IsNullOrEmpty(cinematicIdleState) ? cinematicIdleState : "Idle";
    }

    private string GetResolvedGestureIdleState()
    {
        if (squadGestureSteps != null && squadGestureSteps.Count > 0)
            return GetResolvedSquadIdleState();
        if (squadSimpleGestureNames != null && squadSimpleGestureNames.Count > 0)
            return GetResolvedSquadIdleState();
        return GetResolvedCinematicIdleState();
    }

    // ─── Sequence Resolution ─────────────────────────────────────────────────

    private List<AnimSequenceStep> GetActiveCinematicSequence()
    {
        if (preset != null && preset.introSequence != null && preset.introSequence.Count > 0)
            return preset.introSequence;
        return cinematicIntroSequence ?? new List<AnimSequenceStep>();
    }

    private List<AnimSequenceStep> GetActiveDanceSteps()
    {
        if (preset != null && preset.danceSequence != null && preset.danceSequence.Count > 0)
            return preset.danceSequence;
        if (danceSteps != null && danceSteps.Count > 0)
            return danceSteps;
        if (squadDanceSteps != null && squadDanceSteps.Count > 0)
            return squadDanceSteps;
        return customDanceSequence ?? new List<AnimSequenceStep>();
    }

    private List<AnimSequenceStep> GetActiveGestureSteps()
    {
        if (preset != null && preset.idleGestureSteps != null && preset.idleGestureSteps.Count > 0)
            return preset.idleGestureSteps;
        if (squadGestureSteps != null && squadGestureSteps.Count > 0)
            return squadGestureSteps;
        if (cinematicGestureSteps != null && cinematicGestureSteps.Count > 0)
            return cinematicGestureSteps;
        return idleGestureSteps ?? new List<AnimSequenceStep>();
    }

    private List<string> GetActiveSimpleGestureNames()
    {
        if (preset != null && preset.gestureStateNames != null && preset.gestureStateNames.Count > 0)
            return preset.gestureStateNames;
        if (squadSimpleGestureNames != null && squadSimpleGestureNames.Count > 0)
            return squadSimpleGestureNames;
        if (cinematicSimpleGestureNames != null && cinematicSimpleGestureNames.Count > 0)
            return cinematicSimpleGestureNames;
        return gestureStateNames ?? new List<string>();
    }

    private bool HasAnyCinematicGestures()
    {
        if (preset != null)
        {
            bool p1 = preset.idleGestureSteps   != null && preset.idleGestureSteps.Count   > 0;
            bool p2 = preset.gestureStateNames   != null && preset.gestureStateNames.Count   > 0;
            return p1 || p2;
        }
        bool t = cinematicGestureSteps       != null && cinematicGestureSteps.Count       > 0;
        bool s = cinematicSimpleGestureNames != null && cinematicSimpleGestureNames.Count > 0;
        return t || s;
    }

    private string GetActiveTurnLeftState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.turnLeftStateName)) return preset.turnLeftStateName;
        return string.IsNullOrEmpty(turnLeftStateName) ? "Turn_Left" : turnLeftStateName;
    }

    private string GetActiveTurnRightState()
    {
        if (preset != null && !string.IsNullOrEmpty(preset.turnRightStateName)) return preset.turnRightStateName;
        return string.IsNullOrEmpty(turnRightStateName) ? "Turn_Right" : turnRightStateName;
    }

    private float GetActiveTurnBeforeDanceChance()
    {
        if (preset != null) return preset.turnBeforeDanceChance;
        return turnBeforeDanceChance;
    }
}
