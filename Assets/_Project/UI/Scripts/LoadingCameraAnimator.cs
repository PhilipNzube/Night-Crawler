using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// SOLID — SRP: Animates the loading screen camera with a slow yaw rotation and gentle
///              pitch breathe, giving the skybox a sense of drifting motion.
///
/// Drop this component on your loading scene Camera or CinemachineCamera root.
/// Completely standalone — no external dependencies.
///
/// Setup:
///   1. In your Boot/Loading scene, select the Camera (or CinemachineCamera).
///   2. Add this script as a component.
///   3. Tune yawSpeed (try 8), pitchAmplitude (try 2), pitchSpeed (try 0.3).
///   4. Optionally assign cinemachineCam if you want to drive a CinemachineCamera's
///      local transform instead of the Camera directly.
/// </summary>
[RequireComponent(typeof(Camera))]
public class LoadingCameraAnimator : MonoBehaviour
{
    // =========================================================================
    //  Inspector
    // =========================================================================

    [Header("Sky Drift — Yaw")]
    [Tooltip("Degrees per second the camera slowly rotates horizontally. " +
             "Try 6–12 for a subtle sky drift effect.")]
    [Range(0f, 30f)]
    public float yawSpeed = 8f;

    [Tooltip("Initial yaw angle (degrees). Use this to set where the camera starts looking.")]
    public float initialYaw = 0f;

    [Header("Vertical Breathe — Pitch")]
    [Tooltip("Maximum pitch offset in degrees (camera tilts gently up and down). " +
             "Set to 0 to disable. Try 1–3.")]
    [Range(0f, 8f)]
    public float pitchAmplitude = 2f;

    [Tooltip("How many full pitch cycles per second. Try 0.2–0.4 for a slow breathing feel.")]
    [Range(0.05f, 2f)]
    public float pitchSpeed = 0.3f;

    [Header("Base Pitch")]
    [Tooltip("The resting (centre) pitch angle of the camera in degrees. " +
             "Negative values tilt upward (toward the sky). Try -10 to -20.")]
    public float basePitch = -10f;

    [Header("CinemachineCamera (Optional)")]
    [Tooltip("If this camera is driven by a CinemachineCamera, drag it here and the animator " +
             "will move the VCam's transform instead. Leave empty to drive the Camera directly.")]
    public CinemachineCamera cinemachineCam;

    // =========================================================================
    //  Private State
    // =========================================================================

    private float _currentYaw;
    private float _timeAccumulator;
    private Transform _targetTransform;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        _currentYaw      = initialYaw;
        _timeAccumulator = 0f;
        // Drive the CinemachineCamera transform if assigned; otherwise drive this Camera
        _targetTransform = cinemachineCam != null ? cinemachineCam.transform : transform;
    }

    void Start()
    {
        // Apply initial rotation immediately
        ApplyRotation();
    }

    void Update()
    {
        _currentYaw      += yawSpeed * Time.deltaTime;
        _timeAccumulator += pitchSpeed * Time.deltaTime;

        ApplyRotation();
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private void ApplyRotation()
    {
        float pitch = basePitch + Mathf.Sin(_timeAccumulator * Mathf.PI * 2f) * pitchAmplitude;
        _targetTransform.rotation = Quaternion.Euler(pitch, _currentYaw, 0f);
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>Pauses the camera animation (call when loading is complete).</summary>
    public void Pause()  => enabled = false;

    /// <summary>Resumes the camera animation.</summary>
    public void Resume() => enabled = true;
}
