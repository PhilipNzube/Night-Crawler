using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using NightCrawler.Monsters;

/// <summary>
/// SOLID — SRP: Handles monster command inputs (Go Hunt / To My Side) for the Vengeful Spirit.
/// Routes commands authority to DeadSpawnManager.
/// </summary>
public class GirlCommandNet : NetworkBehaviour
{
    void Update()
    {
        if (!IsOwner || PauseManager.IsGamePaused || GirlDealUI.IsAnyPanelOrModalOpen()) return;

        // Command: HUNT (Seek and Destroy)
        bool huntPressed = KeybindingManager.IsActionTriggered("CommandHunt")
            || (Keyboard.current != null && Keyboard.current.digit3Key.wasPressedThisFrame);

        // Command: RECALL (Come to Me)
        bool recallPressed = KeybindingManager.IsActionTriggered("CommandRecall")
            || (Keyboard.current != null && Keyboard.current.digit4Key.wasPressedThisFrame);

        if (huntPressed)
        {
            RequestCommandServerRpc(0); // 0 = Hunt
        }
        else if (recallPressed)
        {
            RequestCommandServerRpc(1); // 1 = Follow
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestCommandServerRpc(int commandIndex)
    {
        if (DeadSpawnManager.Instance != null)
        {
            DeadSpawnManager.Instance.CommandAllMonstersServerRpc(commandIndex, OwnerClientId);
        }
        else
        {
            Debug.LogWarning("[GirlCommandNet] DeadSpawnManager.Instance is null on server!");
        }
    }
}

