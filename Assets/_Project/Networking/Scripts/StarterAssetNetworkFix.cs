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

            // 3. Destroy remote cameras inside this prefab clone so they cannot hijack Cinemachine
            var childVCams = GetComponentsInChildren<Unity.Cinemachine.CinemachineCamera>(true);
            foreach (var vCam in childVCams)
            {
                Destroy(vCam.gameObject);
            }

            var childCameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in childCameras)
            {
                Destroy(cam.gameObject);
            }
            
            // 4. Disable AudioListener (prevents "Multiple AudioListeners" warning)
            var listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;
        }
    }
}