using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using StarterAssets;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class NetworkPlayer : NetworkBehaviour
{
    public CinemachineCamera virtualCamera;
    public ThirdPersonController controller;
    public StarterAssetsInputs inputs;
    public PlayerInput playerInput;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[NetworkPlayer] Spawned | Owner: {OwnerClientId} | IsOwner: {IsOwner} | IsClient: {IsClient}");

        if (IsOwner)
        {
            Debug.Log("[NetworkPlayer] This is MY player");

            if (virtualCamera != null)
            {
                virtualCamera.Priority = 100;
                virtualCamera.gameObject.SetActive(true);
            }

            if (controller != null) controller.enabled = true;

            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (playerInput != null)
            {
                playerInput.enabled = true;
                playerInput.ActivateInput();
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // --- FORCE ENABLE AUDIO LISTENER FOR LOCAL PLAYER ---
            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null) 
            {
                listener.enabled = true;
                Debug.Log("[AUDIO] Local AudioListener is now ACTIVE for Owner.");
            }
        }
        else
        {
            Debug.Log("[NetworkPlayer] Remote player detected");

            if (virtualCamera != null)
            {
                virtualCamera.Priority = 0;
                virtualCamera.gameObject.SetActive(false);
            }

            if (controller != null) controller.enabled = false;
            if (inputs != null) inputs.enabled = false;
            if (playerInput != null) playerInput.enabled = false;

            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.enabled = false;
            }

            // --- SMOOTH SHADOWS: ENABLE INTERPOLATION FOR REMOTE PLAYERS ---
            if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out Unity.Netcode.Components.NetworkTransform nt))
            {
                nt.Interpolate = true;
            }

            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }
}