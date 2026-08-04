using UnityEngine;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages the white-room character selection environment.
///
/// Responsibilities:
///   • Enable/disable the white room world root and its dedicated camera.
///   • Track how long the user has been idle on a character preview.
///   • After a configurable idle period, trigger a natural gesture loop on the
///     current preview model via CharacterAnimationController.
///   • Reset the idle timer whenever the user switches characters (call ResetIdleTimer).
///
/// ─── SETUP ────────────────────────────────────────────────────────────────────
///  1. In your LobbyScene, create a "White Room" GameObject group with all
///     your white environment meshes and lighting as children.
///  2. Create a Camera inside the white room aimed at the character pivot.
///  3. Attach this script to any persistent manager object in the scene.
///  4. Drag whiteRoomRoot and whiteRoomCamera into the Inspector.
///  5. The CharacterSelectUI calls Enable/Disable and notifies this controller
///     when the preview model changes.
/// </summary>
public class CharacterSceneController : MonoBehaviour
{
    public static CharacterSceneController Instance { get; private set; }

    // -------------------------------------------------------------------------
    //  Inspector — Environment
    // -------------------------------------------------------------------------
    [Header("White Room Environment")]
    [Tooltip("Root GameObject of the white room set (meshes, lighting, etc.). " +
             "Disabled initially; enabled during character selection.")]
    public GameObject whiteRoomRoot;

    [Tooltip("Camera dedicated to the character selection screen. " +
             "Enabled when white room is active; disabled otherwise.")]
    public Camera whiteRoomCamera;

    [Header("Other Cameras / Environments to Disable")]
    [Tooltip("Optional: Squad world camera to disable when white room activates.")]
    public Camera squadCameraToDisable;

    // -------------------------------------------------------------------------
    //  Inspector — Idle Gesture Trigger
    // -------------------------------------------------------------------------
    [Header("Idle Gesture Trigger")]
    [Tooltip("Seconds of user inactivity before the gesture loop starts on the preview model.")]
    [Range(2f, 30f)]
    public float idleBeforeGestureDelay = 8f;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private Coroutine                    _gestureDelayCoroutine;
    private CharacterAnimationController _currentPreviewAnim;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // White room starts hidden
        SetEnvironmentVisible(false);
    }

    // =========================================================================
    //  Public API — Environment Control
    // =========================================================================

    /// <summary>
    /// Activates the white room and its camera.
    /// Call this when CharacterSelectUI opens.
    /// </summary>
    public void EnableCharacterSelectEnvironment()
    {
        if (squadCameraToDisable != null) squadCameraToDisable.enabled = false;
        SetEnvironmentVisible(true);

        if (LobbyCameraController.Instance != null)
            LobbyCameraController.Instance.SetPhase(LobbyCameraController.CameraPhase.CharSelect);
    }

    /// <summary>
    /// Deactivates the white room.
    /// Call this when the character selection is confirmed or cancelled.
    /// </summary>
    public void DisableCharacterSelectEnvironment()
    {
        CancelGestureDelay();
        SetEnvironmentVisible(false);
    }

    // =========================================================================
    //  Public API — Gesture Loop
    // =========================================================================

    /// <summary>
    /// Informs this controller that the current preview model has changed.
    /// Resets the idle gesture timer so the new model gets a fresh delay.
    /// </summary>
    /// <param name="animController">
    ///   The CharacterAnimationController on the newly spawned preview model.
    ///   Pass null to clear the reference without starting a new timer.
    /// </param>
    public void NotifyPreviewModelChanged(CharacterAnimationController animController)
    {
        CancelGestureDelay();
        _currentPreviewAnim = animController;

        if (_currentPreviewAnim != null)
            _gestureDelayCoroutine = StartCoroutine(DelayThenStartGestureLoop());
    }

    /// <summary>
    /// Resets the idle gesture timer without changing the current model.
    /// Call when the user interacts (e.g. clicks another character card) so the
    /// gesture loop doesn't fire immediately after every interaction.
    /// </summary>
    public void ResetIdleTimer()
    {
        CancelGestureDelay();

        if (_currentPreviewAnim != null)
        {
            _currentPreviewAnim.ReturnToIdle();
            _gestureDelayCoroutine = StartCoroutine(DelayThenStartGestureLoop());
        }
    }

    // =========================================================================
    //  Private
    // =========================================================================

    private IEnumerator DelayThenStartGestureLoop()
    {
        yield return new WaitForSecondsRealtime(idleBeforeGestureDelay);

        if (_currentPreviewAnim != null)
        {
            Debug.Log("[CharacterSceneController] Idle timeout reached — starting gesture loop.");
            _currentPreviewAnim.StartNaturalGestureLoop();
        }

        _gestureDelayCoroutine = null;
    }

    private void CancelGestureDelay()
    {
        if (_gestureDelayCoroutine != null)
        {
            StopCoroutine(_gestureDelayCoroutine);
            _gestureDelayCoroutine = null;
        }
    }

    private void SetEnvironmentVisible(bool visible)
    {
        if (whiteRoomRoot   != null) whiteRoomRoot.SetActive(visible);
        if (whiteRoomCamera != null) whiteRoomCamera.enabled = visible;
    }
}
