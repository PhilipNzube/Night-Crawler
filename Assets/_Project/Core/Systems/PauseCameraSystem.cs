using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// SOLID — SRP: Manages Cinemachine virtual camera blending transitions when opening/closing the Pause menu.
/// Integrates smoothly with SlimUI's camera view and CinemachineBrain.
/// </summary>
public class PauseCameraSystem : MonoBehaviour
{
    public static PauseCameraSystem Instance { get; private set; }

    [Header("Cinemachine Cameras")]
    [Tooltip("The Virtual Camera aligned with the SlimUI pause menu view.")]
    public CinemachineCamera pauseVirtualCamera;

    [Header("Priority Settings")]
    [Tooltip("Priority assigned when game is paused to trigger Cinemachine blend.")]
    public int activePausePriority = 20;

    [Tooltip("Priority assigned when unpaused so camera blends back to gameplay camera.")]
    public int inactivePausePriority = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (pauseVirtualCamera != null)
        {
            pauseVirtualCamera.Priority = inactivePausePriority;
        }
    }

    /// <summary>
    /// Activates or deactivate the pause virtual camera, triggering Cinemachine camera animation blend.
    /// </summary>
    public void SetPauseCameraActive(bool active)
    {
        if (pauseVirtualCamera == null)
        {
            // Auto-search if unassigned
            pauseVirtualCamera = GetComponentInChildren<CinemachineCamera>();
        }

        if (pauseVirtualCamera != null)
        {
            pauseVirtualCamera.Priority = active ? activePausePriority : inactivePausePriority;
            Debug.Log($"[PauseCameraSystem] Pause camera priority set to {pauseVirtualCamera.Priority} (Active: {active})");
        }
    }
}
