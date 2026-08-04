using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages all Cinemachine Virtual Camera transitions for the
///              pre-game lobby flow screens.
///
/// Written for Unity 6 / Cinemachine 3.x (Unity.Cinemachine namespace).
///
/// Each screen phase has a dedicated CinemachineCamera. This controller switches
/// between them using Cinemachine's priority system, which triggers automatic
/// blending via the CinemachineBrain on the Main Camera.
///
/// Phases and their cameras:
///   Lobby      — Connection / waiting room. Static, wide establishing shot.
///   Reveal     — Slot-machine reveal. Slightly dramatic low angle with impulse.
///   CharSelect — White room character preview. Slow gentle orbital sway.
///   Squad      — Rocks/trees lineup. Dolly pan across the full squad.
///   GirlScreen — Girl's exclusive screen. Slow pull-back / gentle breathe.
/// </summary>
public class LobbyCameraController : MonoBehaviour
{
    public static LobbyCameraController Instance { get; private set; }

    // =========================================================================
    //  Phase Enum
    // =========================================================================

    public enum CameraPhase
    {
        Lobby,      // Default lobby / connection screen
        Reveal,     // Slot-machine name reveal
        CharSelect, // White room — investigator character selection
        Squad,      // Squad rocks/trees lineup
        GirlScreen  // Girl player's exclusive screen
    }

    // =========================================================================
    //  Inspector — Virtual Cameras (Cinemachine 3.x CinemachineCamera)
    // =========================================================================

    [Header("Virtual Cameras — one per phase")]
    [Tooltip("Static establishing camera for the lobby/connection waiting room.")]
    public CinemachineCamera lobbyCam;

    [Tooltip("Dramatic camera for the slot-machine reveal screen.")]
    public CinemachineCamera revealCam;

    [Tooltip("White room camera for investigator character selection.")]
    public CinemachineCamera charSelectCam;

    [Tooltip("Cinematic squad lineup camera.")]
    public CinemachineCamera squadCam;

    [Tooltip("Girl player's exclusive screen camera.")]
    public CinemachineCamera girlCam;

    // =========================================================================
    //  Inspector — Blend Durations (seconds)
    // =========================================================================

    [Header("Blend Durations (seconds)")]
    [Tooltip("Default smooth blend time between most screens.")]
    public float defaultBlendTime   = 1.2f;

    [Tooltip("Faster, punchier blend into the reveal screen.")]
    public float revealBlendTime    = 0.5f;

    [Tooltip("Slow, cinematic blend into the squad world.")]
    public float squadBlendTime     = 2.2f;

    // =========================================================================
    //  Inspector — CharSelect Orbital Sway
    // =========================================================================

    [Header("CharSelect — Orbital Sway")]
    [Tooltip("Speed of the sinusoidal orbital sway (lower = slower and more subtle).")]
    [Range(0.1f, 3f)]
    public float charSelectSwaySpeed     = 0.5f;

    [Tooltip("Total angular range of the sway in degrees (the VCam rocks left/right by this amount).")]
    [Range(2f, 30f)]
    public float charSelectSwayAmplitude = 12f;

    // =========================================================================
    //  Inspector — Squad Spline Dolly Pan
    // =========================================================================

    [Header("Squad — Dolly Pan")]
    [Tooltip("Optional CinemachineSplineDolly component on the squad VCam. " +
             "Automatically drives position along spline from 0 to 1 to pan across the lineup.")]
    public CinemachineSplineDolly squadDollyComp;

    [Tooltip("How long the dolly pan takes to travel from start to end of the path.")]
    public float squadDollyPanDuration = 7f;

    [Tooltip("Ease curve for the dolly pan. Ease-in-out gives the most cinematic feel.")]
    public AnimationCurve squadDollyPanCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // =========================================================================
    //  Inspector — Reveal Impulse
    // =========================================================================

    [Header("Reveal — Camera Impulse on Winner Lock-In")]
    [Tooltip("CinemachineImpulseSource on this GameObject. " +
             "Fired when the slot machine stops on the winner.")]
    public CinemachineImpulseSource revealImpulseSource;

    // =========================================================================
    //  Inspector — Girl Screen Breathe
    // =========================================================================

    [Header("Girl Screen — FOV Breathe")]
    [Tooltip("Enable a subtle living FOV breathe on the girl screen camera.")]
    public bool girlScreenFOVBreathe     = true;

    [Tooltip("The base (centre) field of view.")]
    public float girlBaseFOV             = 50f;

    [Tooltip("How much the FOV pulses above and below the base value.")]
    [Range(0.5f, 8f)]
    public float girlFOVBreatheAmplitude = 2f;

    [Tooltip("Speed of one full breathe cycle in seconds.")]
    public float girlFOVBreathePeriod    = 4f;

    // =========================================================================
    //  Private Constants
    // =========================================================================

    private const int ACTIVE_PRIORITY   = 20;
    private const int INACTIVE_PRIORITY = 0;

    // =========================================================================
    //  Private State
    // =========================================================================

    private Coroutine _dollyCoroutine;
    private Coroutine _swayCoroutine;
    private Coroutine _breatheCoroutine;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Deactivate all cameras, then set lobby as the starting active cam
        SetAllPriorities(INACTIVE_PRIORITY);
        if (lobbyCam != null)
        {
            lobbyCam.Priority = ACTIVE_PRIORITY;
        }

        // Squad dolly starts at position 0
        if (squadDollyComp != null)
            squadDollyComp.CameraPosition = 0f;
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Transitions to the camera for the given phase with an appropriate blend.
    /// Call this whenever the flow advances to a new screen:
    ///   • GirlRevealUI.StartSpin()           → SetPhase(Reveal)
    ///   • CharacterSceneController.Enable()  → SetPhase(CharSelect)
    ///   • SquadSceneController.Enable()      → SetPhase(Squad)
    ///   • GirlPlayerScreen.Show()            → SetPhase(GirlScreen)
    /// </summary>
    public void SetPhase(CameraPhase phase)
    {
        StopSideCoroutines();

        switch (phase)
        {
            case CameraPhase.Lobby:
                BlendTo(lobbyCam, defaultBlendTime);
                break;

            case CameraPhase.Reveal:
                BlendTo(revealCam, revealBlendTime);
                break;

            case CameraPhase.CharSelect:
                BlendTo(charSelectCam, defaultBlendTime);
                StartCharSelectSway();
                break;

            case CameraPhase.Squad:
                BlendTo(squadCam, squadBlendTime);
                // Reset dolly to start then pan
                if (squadDollyComp != null) squadDollyComp.CameraPosition = 0f;
                _dollyCoroutine = StartCoroutine(RunDollyPan());
                break;

            case CameraPhase.GirlScreen:
                BlendTo(girlCam, defaultBlendTime);
                if (girlScreenFOVBreathe)
                    _breatheCoroutine = StartCoroutine(RunFOVBreathe());
                break;
        }
    }

    /// <summary>
    /// Fires a camera impulse (e.g. a subtle shake) at the reveal winner moment.
    /// Called by GirlRevealUI when the slot machine stops.
    /// </summary>
    public void FireRevealImpulse()
    {
        if (revealImpulseSource != null)
            revealImpulseSource.GenerateImpulse();
        else
            Debug.LogWarning("[LobbyCameraController] revealImpulseSource not wired. " +
                             "Add CinemachineImpulseSource to this GameObject.");
    }

    // =========================================================================
    //  Private — Blending
    // =========================================================================

    private void BlendTo(CinemachineCamera targetCam, float blendTime)
    {
        // Adjust the brain's default blend time for this specific transition
        CinemachineBrain brain = FindBrain();
        if (brain != null)
            brain.DefaultBlend.Time = blendTime;

        // Lower all cameras then raise the target
        SetAllPriorities(INACTIVE_PRIORITY);
        if (targetCam != null)
            targetCam.Priority = ACTIVE_PRIORITY;
    }

    private void SetAllPriorities(int priority)
    {
        if (lobbyCam      != null) lobbyCam.Priority      = priority;
        if (revealCam     != null) revealCam.Priority     = priority;
        if (charSelectCam != null) charSelectCam.Priority = priority;
        if (squadCam      != null) squadCam.Priority      = priority;
        if (girlCam       != null) girlCam.Priority       = priority;
    }

    private static CinemachineBrain FindBrain()
    {
        return Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
    }

    // =========================================================================
    //  Private — CharSelect Sway
    // =========================================================================

    private void StartCharSelectSway()
    {
        if (charSelectCam == null) return;

        var orbital = charSelectCam.GetComponent<CinemachineOrbitalFollow>();
        if (orbital != null)
        {
            _swayCoroutine = StartCoroutine(RunOrbitalSway(orbital));
        }
    }

    private IEnumerator RunOrbitalSway(CinemachineOrbitalFollow orbital)
    {
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime * charSelectSwaySpeed;
            orbital.HorizontalAxis.Value = Mathf.Sin(t) * charSelectSwayAmplitude;
            yield return null;
        }
    }

    // =========================================================================
    //  Private — Squad Dolly Pan
    // =========================================================================

    private IEnumerator RunDollyPan()
    {
        if (squadDollyComp == null) yield break;

        float elapsed = 0f;
        while (elapsed < squadDollyPanDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / squadDollyPanDuration);
            squadDollyComp.CameraPosition = squadDollyPanCurve.Evaluate(t);
            yield return null;
        }

        squadDollyComp.CameraPosition = 1f;
        _dollyCoroutine = null;
    }

    // =========================================================================
    //  Private — Girl Screen FOV Breathe
    // =========================================================================

    private IEnumerator RunFOVBreathe()
    {
        if (girlCam == null) yield break;

        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            float offset = Mathf.Sin((t / girlFOVBreathePeriod) * Mathf.PI * 2f)
                           * girlFOVBreatheAmplitude;
            girlCam.Lens.FieldOfView = girlBaseFOV + offset;
            yield return null;
        }
    }

    // =========================================================================
    //  Private — Cleanup
    // =========================================================================

    private void StopSideCoroutines()
    {
        if (_dollyCoroutine   != null) { StopCoroutine(_dollyCoroutine);   _dollyCoroutine   = null; }
        if (_swayCoroutine    != null) { StopCoroutine(_swayCoroutine);    _swayCoroutine    = null; }
        if (_breatheCoroutine != null) { StopCoroutine(_breatheCoroutine); _breatheCoroutine = null; }
    }
}
