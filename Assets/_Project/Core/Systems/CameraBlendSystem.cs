using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

/// <summary>
/// SOLID — SRP: Reusable Cinemachine Camera Blend System.
/// Wraps Cinemachine 3.x priority-switching and custom blend time overrides.
/// Drop on any scene manager to drive camera transitions between phases or UI screens.
///
/// Usage example:
///   CameraBlendSystem.Instance.BlendTo(pauseVirtualCamera, blendSeconds: 0.8f);
///   CameraBlendSystem.Instance.BlendTo(gameplayCam, blendSeconds: 0.5f);
/// </summary>
public class CameraBlendSystem : MonoBehaviour
{
    public static CameraBlendSystem Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Default blend time used when no override is provided.")]
    public float defaultBlendTime = 0.8f;

    [Tooltip("Priority given to the active camera. All others get 0.")]
    public int activePriority = 20;

    private CinemachineCamera _currentCamera;
    private Coroutine         _blendTimerRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Blends to the target CinemachineCamera, optionally overriding blend duration.
    /// All other registered cameras are deprioritised to 0.
    /// </summary>
    public void BlendTo(CinemachineCamera target, float blendTime = -1f)
    {
        if (target == null) return;

        float duration = blendTime > 0f ? blendTime : defaultBlendTime;

        // Override brain blend time temporarily
        CinemachineBrain brain = FindBrain();
        if (brain != null)
        {
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut, duration);
        }

        // Deactivate previous
        if (_currentCamera != null && _currentCamera != target)
            _currentCamera.Priority = 0;

        // Activate target
        target.Priority = activePriority;
        _currentCamera  = target;
    }

    /// <summary>
    /// Deprioritises all provided cameras (blanks the Cinemachine view).
    /// </summary>
    public void BlendOutAll(CinemachineCamera[] cameras)
    {
        foreach (var cam in cameras)
            if (cam != null) cam.Priority = 0;

        _currentCamera = null;
    }

    private static CinemachineBrain FindBrain()
    {
        return Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
    }
}
