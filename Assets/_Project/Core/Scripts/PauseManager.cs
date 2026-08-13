using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Unity.Netcode;
using System;
using System.Collections;

/// <summary>
/// SOLID — SRP: Pause System for any Unity PC game with Netcode for GameObjects.
///
/// FIX: wasPressedThisFrame polling moved to Awake + coroutine-deferred Start
/// so the first ESC press works reliably even when Unity's script execution
/// order hasn't settled yet at frame 0.
///
/// Setup:
///   • Add to a persistent Manager GameObject in your game scene.
///   • Drag PauseUI into the Inspector field (or leave blank — auto-found).
/// </summary>
public class PauseManager : MonoBehaviour
{
    // =========================================================================
    //  Static Event
    // =========================================================================
    /// <summary>
    /// Fired on every pause/resume. true = just paused, false = just resumed.
    /// Subscribe: PauseManager.OnPauseStateChanged += MyMethod;
    /// </summary>
    public static event Action<bool> OnPauseStateChanged;

    // =========================================================================
    //  Inspector
    // =========================================================================
    [Header("Pause UI Reference")]
    [Tooltip("Drag the PauseUI component here. Auto-found if blank.")]
    public PauseUI pauseUI;

    [Header("Pause Camera System (Optional)")]
    [Tooltip("Drag PauseCameraSystem if you have a dedicated pause Cinemachine camera.")]
    public PauseCameraSystem pauseCameraSystem;

    // =========================================================================
    //  Private State
    // =========================================================================
    private bool                  _isPaused    = false;
    private bool                  _ready       = false; // true once references are resolved
    private StarterAssetsInputs   _inputs;
    private ThirdPersonController _controller;

    public bool IsPaused => _isPaused;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    private void Awake()
    {
        // Resolve references immediately in Awake so they are ready before
        // the very first Update tick (fixes the "need to press ESC twice" bug).
        if (pauseUI == null)
            pauseUI = FindFirstObjectByType<PauseUI>();

        if (pauseCameraSystem == null)
            pauseCameraSystem = FindFirstObjectByType<PauseCameraSystem>();
    }

    private IEnumerator Start()
    {
        // Wait one extra frame after all Start() methods on other scripts have run.
        // This ensures PauseUI.Start() has already called HidePauseMenu() and the
        // SlimUI Animator has finished its own Start initialisation, so our first
        // ShowPauseMenu() call is never in a race with SlimUI's own init.
        yield return null;

        // Final safety check after the deferred frame.
        if (pauseUI == null)
            pauseUI = FindFirstObjectByType<PauseUI>();
        if (pauseCameraSystem == null)
            pauseCameraSystem = FindFirstObjectByType<PauseCameraSystem>();

        _ready = true;
        Debug.Log("[PauseManager] Ready. Press ESC to pause.");
    }

    private void Update()
    {
        if (!_ready) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            HandleEscapePress();
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    /// <summary>
    /// Handles ESC key with nested panel priority:
    ///   1st priority — close Exit dialog
    ///   2nd priority — close Settings
    ///   3rd priority — toggle pause
    /// </summary>
    public void HandleEscapePress()
    {
        if (pauseUI != null)
        {
            if (pauseUI.IsExitDialogOpen) { pauseUI.CloseExitDialog(); return; }
            if (pauseUI.IsSettingsOpen)   { pauseUI.CloseSettings();   return; }
        }
        TogglePause();
    }

    public void TogglePause()  => SetPaused(!_isPaused);
    public void ResumeGame()   => SetPaused(false);

    public void SetPaused(bool paused)
    {
        _isPaused = paused;

        TryCacheLocalPlayerComponents();

        // ── Cursor ──────────────────────────────────────────────────────────
        Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = _isPaused;

        // ── Player input & movement ─────────────────────────────────────────
        if (_inputs != null)
        {
            _inputs.cursorLocked       = !_isPaused;
            _inputs.cursorInputForLook = !_isPaused;
        }
        if (_controller != null)
            _controller.enabled = !_isPaused;

        // ── Camera blend ────────────────────────────────────────────────────
        if (pauseCameraSystem != null)
            pauseCameraSystem.SetPauseCameraActive(_isPaused);

        // ── UI visibility ───────────────────────────────────────────────────
        if (pauseUI != null)
        {
            if (_isPaused) pauseUI.ShowPauseMenu();
            else           pauseUI.HidePauseMenu();
        }

        // ── Broadcast ───────────────────────────────────────────────────────
        OnPauseStateChanged?.Invoke(_isPaused);

        Debug.Log($"[PauseManager] {(_isPaused ? "PAUSED" : "RESUMED")}");
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
