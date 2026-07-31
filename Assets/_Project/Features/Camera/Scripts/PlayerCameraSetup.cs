using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerSetup : NetworkBehaviour
{
    public GameObject cameraRoot;
    public MonoBehaviour[] scriptsToDisable;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Disable camera for other players
            if (cameraRoot != null)
                cameraRoot.SetActive(false);

            // Disable movement/input scripts
            foreach (var script in scriptsToDisable)
            {
                script.enabled = false;
            }
        }
    }
}