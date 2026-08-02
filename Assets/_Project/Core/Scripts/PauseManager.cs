using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Handles pause state toggling for the local player only.
///
/// DIP note: This script depends on the concrete StarterAssets types because
/// Unity's component system doesn't support constructor injection. If the input
/// system is ever swapped, only this file needs updating — all callers remain
/// unaffected (they never reference PauseManager directly).
///
/// LSP / ISP: Does not inherit from or implement any interface — intentional, as
/// pause is a singleton concern tied to one scene lifecycle.
/// </summary>
public class PauseManager : MonoBehaviour
{
    private bool _isPaused = false;

    // Cached lazily on first pause — avoids searching the scene on every frame
    private StarterAssetsInputs  _inputs;
    private ThirdPersonController _controller;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    // =========================================================================
    //  Pause Logic
    // =========================================================================
    private void TogglePause()
    {
        _isPaused = !_isPaused;

        // Lazy-cache local player components on first call (not every frame)
        TryCacheLocalPlayerComponents();

        // Cursor
        Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = _isPaused;

        // Disable look / movement while paused
        if (_inputs != null)
        {
            _inputs.cursorLocked       = !_isPaused;
            _inputs.cursorInputForLook = !_isPaused;
        }

        if (_controller != null)
            _controller.enabled = !_isPaused;

        Debug.Log($"[PauseManager] {(_isPaused ? "PAUSED" : "RESUMED")}");
    }

    /// <summary>
    /// Searches for components on the local player object only once.
    /// Safe to call repeatedly — exits immediately if already cached.
    /// </summary>
    private void TryCacheLocalPlayerComponents()
    {
        if (_inputs != null && _controller != null) return;

        var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer == null) return;

        _inputs     = localPlayer.GetComponent<StarterAssetsInputs>();
        _controller = localPlayer.GetComponent<ThirdPersonController>();
    }
}
