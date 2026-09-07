using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerCameraSetup : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            var vcam = GetComponentInChildren<CinemachineVirtualCameraBase>(true);
            if (vcam != null)
            {
                vcam.Priority = 99999;
                vcam.gameObject.SetActive(true);
                vcam.enabled = true;
                if (vcam.Follow == null) vcam.Follow = transform;
                if (vcam.LookAt == null) vcam.LookAt = transform;
            }
        }
        else
        {
            var childVCams = GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            foreach (var v in childVCams)
            {
                v.Priority = -99999;
                v.enabled = false;
                v.gameObject.SetActive(false);
                Destroy(v.gameObject);
            }
        }
    }
}