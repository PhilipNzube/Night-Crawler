using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Handles pause state toggling for the local player.
/// Multiplayer-friendly: Shows UI overlay and unlocks cursor without altering Time.timeScale.
/// </summary>
public class PauseManager : MonoBehaviour
{
    [Header("Pause UI Reference")]
    [Tooltip("Optional reference to PauseUI component. Automatically found if unassigned.")]
    public PauseUI pauseUI;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private bool _isPaused = false;
    private StarterAssetsInputs   _inputs;
    private ThirdPersonController _controller;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
        if (pauseUI == null)
            pauseUI = FindFirstObjectByType<PauseUI>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================
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

        // UI Panel visibility
        if (pauseUI != null)
        {
            if (_isPaused) pauseUI.ShowPauseMenu();
            else pauseUI.HidePauseMenu();
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
