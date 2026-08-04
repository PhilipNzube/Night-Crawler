using UnityEngine;

/// <summary>
/// SOLID — SRP: Manages the squad rocks/trees world environment and its
///              cinematic camera.
///
/// Works alongside SquadLineupDisplay which handles model placement.
/// This controller only deals with enabling/disabling the environment root
/// and the dedicated squad camera.
///
/// ─── SETUP ────────────────────────────────────────────────────────────────────
///  1. In your LobbyScene, your squad world (rocks, trees, positioned camera)
///     should all be children of a single root GameObject.
///  2. Keep that root disabled initially.
///  3. Drag that root into squadWorldRoot.
///  4. Drag the cinematic camera (the one you already positioned) into squadCamera.
///  5. SquadLineupDisplay.ShowSquadLineup() calls EnableSquadEnvironment() via
///     SquadSceneController.Instance before it builds the lineup.
/// </summary>
public class SquadSceneController : MonoBehaviour
{
    public static SquadSceneController Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------
    [Header("Squad World Environment")]
    [Tooltip("Root GameObject containing all squad world geometry (rocks, trees, etc.). " +
             "Disabled initially; enabled when the squad lineup is shown.")]
    public GameObject squadWorldRoot;

    [Tooltip("The cinematic camera aimed at the squad lineup positions. " +
             "This is the camera you positioned to look cool.")]
    public Camera squadCamera;

    [Header("Other Cameras to Disable on Enable")]
    [Tooltip("Optional: white room camera to disable when squad world activates.")]
    public Camera whiteRoomCameraToDisable;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Squad world starts hidden
        SetEnvironmentVisible(false);
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Activates the squad rocks/trees world and its camera.
    /// Called by SquadLineupDisplay before building the lineup.
    /// </summary>
    public void EnableSquadEnvironment()
    {
        if (whiteRoomCameraToDisable != null) whiteRoomCameraToDisable.enabled = false;
        SetEnvironmentVisible(true);

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.Squad);
    }

    /// <summary>
    /// Deactivates the squad world.
    /// Called by SquadLineupDisplay after the showcase completes.
    /// </summary>
    public void DisableSquadEnvironment()
    {
        SetEnvironmentVisible(false);
    }

    // =========================================================================
    //  Private
    // =========================================================================

    private void SetEnvironmentVisible(bool visible)
    {
        if (squadWorldRoot != null) squadWorldRoot.SetActive(visible);
        if (squadCamera    != null) squadCamera.enabled = visible;
    }
}
