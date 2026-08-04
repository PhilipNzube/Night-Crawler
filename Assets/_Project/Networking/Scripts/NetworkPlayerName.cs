using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// SOLID — SRP: Synchronizes player profile names across Netcode for GameObjects.
/// </summary>
public class NetworkPlayerName : NetworkBehaviour
{
    [Header("Network State")]
    public NetworkVariable<FixedString64Bytes> playerName = new NetworkVariable<FixedString64Bytes>(
        "Investigator",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            string localName = PlayerNameManager.GetPlayerName();
            SetPlayerNameServerRpc(localName);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SetPlayerNameServerRpc(string nameToSet)
    {
        if (string.IsNullOrWhiteSpace(nameToSet)) return;
        playerName.Value = new FixedString64Bytes(nameToSet);
    }
}
