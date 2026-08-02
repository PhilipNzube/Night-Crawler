using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Handles only the Girl / Demon's monster command input.
///
/// Monster spawning has been disabled, so CommandAllMonsters() no longer
/// exists on GameManager. This script is kept intact so the keybindings
/// remain in place and can be re-wired when a MonsterSpawner is introduced.
/// Commands are silently no-ops when no monsters are active.
/// </summary>
public class GirlCommandNet : NetworkBehaviour
{
    void Update()
    {
        if (!IsOwner) return;

        // Command: HUNT (Seek and Destroy)
        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            RequestCommandServerRpc(0); // 0 = Hunt
            Debug.Log("[COMMAND] MONSTERS: GO HUNT!");
        }

        // Command: RECALL (Come to Me)
        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            RequestCommandServerRpc(1); // 1 = Follow
            Debug.Log("[COMMAND] MONSTERS: TO MY SIDE!");
        }
    }

    // Updated to the modern Netcode for GameObjects RPC attribute — RequireOwnership is deprecated.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestCommandServerRpc(int commandIndex)
    {
        // No-op: monster spawning is currently disabled.
        // When a MonsterSpawner is added, call its CommandAllMonsters() here.
        Debug.Log($"[GirlCommandNet] Command {commandIndex} received on server — no monsters active.");
    }
}
