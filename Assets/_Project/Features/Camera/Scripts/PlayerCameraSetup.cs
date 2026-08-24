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
            if (vcam == null) vcam = FindFirstObjectByType<CinemachineCamera>();
            if (vcam != null)
            {
                vcam.Priority = 100;
                vcam.gameObject.SetActive(true);
                if (vcam.Follow == null) vcam.Follow = transform;
                if (vcam.LookAt == null) vcam.LookAt = transform;
            }
        }
    }
}