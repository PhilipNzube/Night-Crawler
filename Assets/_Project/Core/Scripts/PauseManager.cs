using UnityEngine;
using UnityEngine.InputSystem;
using StarterAssets;
using Unity.Netcode;

public class PauseManager : MonoBehaviour
{
    private bool _isPaused = false;
    private StarterAssetsInputs _inputs;
    private ThirdPersonController _controller;

    void Update()
    {
        // 1. Listen for Escape Key
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;

        // 2. Find local player components if not already cached
        if (_inputs == null || _controller == null)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                _inputs = localPlayer.GetComponent<StarterAssetsInputs>();
                _controller = localPlayer.GetComponent<ThirdPersonController>();
            }
        }

        // 3. Handle Cursor Lock
        Cursor.lockState = _isPaused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _isPaused;

        // 4. Disable Input/Movement while paused
        if (_inputs != null)
        {
            _inputs.cursorLocked = !_isPaused;
            _inputs.cursorInputForLook = !_isPaused;
        }

        if (_controller != null)
        {
            _controller.enabled = !_isPaused;
        }

        Debug.Log($"[PAUSE] Game is now {(_isPaused ? "PAUSED (Cursor Unlocked)" : "ACTIVE (Cursor Locked)")}");
    }
}
