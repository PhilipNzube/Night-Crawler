using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerCameraSetup : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            var vcam = GetComponentInChildren<CinemachineCamera>(true);
            if (vcam != null)
            {
                vcam.Priority = 100;
                vcam.gameObject.SetActive(true);
                vcam.enabled = true;
                if (vcam.Follow == null) vcam.Follow = transform;
                if (vcam.LookAt == null) vcam.LookAt = transform;
            }
        }
        else
        {
            var childVCams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var v in childVCams)
            {
                v.Priority = 0;
                v.enabled = false;
                v.gameObject.SetActive(false);
            }
        }
    }
}