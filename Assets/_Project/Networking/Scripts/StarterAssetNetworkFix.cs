using Unity.Netcode;
using UnityEngine;
using StarterAssets;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

/// <summary>
/// Legacy network fix helper. All core ownership, camera, and input management
/// are centralized in NetworkPlayer.cs.
/// </summary>
public class StarterAssetNetworkFix : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        // If NetworkPlayer is present on this GameObject, it already handles complete network setup
        if (TryGetComponent<NetworkPlayer>(out _))
        {
            return;
        }

        if (IsOwner)
        {
            Debug.Log($"[StarterAssetNetworkFix] Owner Client {OwnerClientId}: Controlling {gameObject.name}");
        }
        else
        {
            if (TryGetComponent<PlayerInput>(out var input)) input.enabled = false;
            if (TryGetComponent<ThirdPersonController>(out var controller)) controller.enabled = false;

            var childVCams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var vCam in childVCams)
            {
                vCam.Priority = -1000;
                vCam.Follow = null;
                vCam.LookAt = null;
                vCam.enabled = false;
                vCam.gameObject.SetActive(false);
                Destroy(vCam.gameObject);
            }

            var childCameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in childCameras)
            {
                cam.enabled = false;
                cam.gameObject.SetActive(false);
                Destroy(cam.gameObject);
            }

            var listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;
        }
    }
}