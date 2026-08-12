using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Reusable Pause System for any Unity PC game with Netcode for GameObjects.
/// Handles pause state, cursor lock, player input disabling, nested ESC navigation,
/// and optional Cinemachine camera transition via PauseCameraSystem.
///
/// Usage: Add to a persistent Manager GameObject in your game scene.
/// Wire pauseUI in Inspector or it auto-finds PauseUI in the scene.
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("Pause UI Reference")]
    [Tooltip("Optional reference to PauseUI component. Automatically found if unassigned.")]
    public PauseUI pauseUI;

    [Header("Pause Camera System (Optional)")]
    [Tooltip("If assigned, triggers a Cinemachine camera blend when pausing/unpausing.")]
    public PauseCameraSystem pauseCameraSystem;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private bool _isPaused = false;
    private StarterAssetsInputs   _inputs;
    private ThirdPersonController _controller;

    public bool IsPaused => _isPaused;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private void Start()
    {
        if (pauseUI == null)
            pauseUI = FindFirstObjectByType<PauseUI>();

        if (pauseCameraSystem == null)
            pauseCameraSystem = FindFirstObjectByType<PauseCameraSystem>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleEscapePress();
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void HandleEscapePress()
    {
        if (pauseUI != null)
        {
            // 1. If Exit Dialog pop-up is active, close it first
            if (pauseUI.IsExitDialogOpen)
            {
                pauseUI.CloseExitDialog();
                return;
            }

            // 2. If Settings panel is active, close it back to pause menu
            if (pauseUI.IsSettingsOpen)
            {
                pauseUI.CloseSettings();
                return;
            }
        }

        // 3. Otherwise toggle pause state
        TogglePause();
    }

    public void TogglePause()
    {
        SetPaused(!_isPaused);
    }

    public void ResumeGame()
    {
        SetPaused(false);
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;

        TryCacheLocalPlayerComponents();

        // Cursor state
        Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = _isPaused;

        // Player look and movement controls
        if (_inputs != null)
        {
            _inputs.cursorLocked       = !_isPaused;
            _inputs.cursorInputForLook = !_isPaused;
        }

        if (_controller != null)
            _controller.enabled = !_isPaused;

        // Cinemachine camera blend to/from pause menu view
        if (pauseCameraSystem != null)
            pauseCameraSystem.SetPauseCameraActive(_isPaused);

        // UI Panel visibility
        if (pauseUI != null)
        {
            if (_isPaused) pauseUI.ShowPauseMenu();
            else           pauseUI.HidePauseMenu();
        }

        Debug.Log($"[PauseManager] Game {(_isPaused ? "PAUSED" : "RESUMED")}");
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private void TryCacheLocalPlayerComponents()
    {
        if (_inputs != null && _controller != null) return;

        var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        _inputs     = localPlayer.GetComponent<StarterAssetsInputs>();
        _controller = localPlayer.GetComponent<ThirdPersonController>();
    }
}
