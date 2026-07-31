using Unity.Netcode;
using UnityEngine;
using StarterAssets; // Requires the Starter Assets namespace
using UnityEngine.InputSystem;

public class StarterAssetNetworkFix : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // I own this player! Log it so I know which one I am.
            Debug.Log($"[Owner] I am Client {OwnerClientId}. Controlling {gameObject.name}");
        }
        else
        {
            // 1. Disable the PlayerInput so I don't move someone else's character
            if (TryGetComponent<PlayerInput>(out var input)) input.enabled = false;

            // 2. Disable the ThirdPersonController script logic
            if (TryGetComponent<ThirdPersonController>(out var controller)) controller.enabled = false;

            // 3. Find and disable the Camera and Cinemachine Virtual Camera inside this prefab
            // This stops the "Camera Hijack"
            var childCameras = GetComponentsInChildren<Camera>();
            foreach (var cam in childCameras) cam.enabled = false;

            var vCam = GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>();
            if (vCam != null) vCam.enabled = false;
            
            // 4. Disable AudioListener (prevents "Multiple AudioListeners" warning)
            var listener = GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }
}