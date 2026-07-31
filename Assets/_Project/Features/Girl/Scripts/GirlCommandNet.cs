using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class GirlCommandNet : NetworkBehaviour
{
    void Update()
    {
        if (!IsOwner) return;

        // Command: HUNT (Seek and Destroy)
        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            RequestCommandServerRpc(MonsterAI.Command.Hunt);
            Debug.Log("[COMMAND] MONSTERS: GO HUNT!");
            // TIP: You can play a Demon Whistle here for audio feedback
        }

        // Command: RECALL (Come to Me)
        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            RequestCommandServerRpc(MonsterAI.Command.Follow);
            Debug.Log("[COMMAND] MONSTERS: TO MY SIDE!");
            // TIP: You can play a Demon Laugh here for audio feedback
        }
    }

    [ServerRpc]
    private void RequestCommandServerRpc(MonsterAI.Command command)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CommandAllMonsters(command);
        }
    }
}
