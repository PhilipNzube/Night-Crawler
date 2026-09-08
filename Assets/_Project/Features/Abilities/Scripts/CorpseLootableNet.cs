using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages lootable items on a dead player's corpse across the network.
/// When a player (e.g. Medic) dies with items like Healing Vials, their corpse retains
/// those exact remaining items. Any surviving player can interact with the corpse to loot them.
/// </summary>
public class CorpseLootableNet : NetworkBehaviour
{
    [Header("Loot Settings")]
    [Tooltip("Maximum interaction distance to loot the body.")]
    public float interactionDistance = 2.5f;

    [Tooltip("Layer mask for player corpses.")]
    public LayerMask corpseLayer;

    [Header("Network State")]
    public NetworkVariable<int> lootableVials = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isLooted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private HealthSystem _healthSystem;
    private HealingVialInventoryNet _vialInventory;
    private bool _isLocalPlayerAimingAtThis = false;

    public bool HasLoot => !isLooted.Value && lootableVials.Value > 0;
    public int RemainingVials => lootableVials.Value;

    private void Awake()
    {
        _healthSystem = GetComponent<HealthSystem>();
        _vialInventory = GetComponent<HealingVialInventoryNet>();
    }

    public override void OnNetworkSpawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied += HandleDeath;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandleDeath;
        }
    }

    private void HandleDeath()
    {
        if (!IsServer) return;

        // Extract remaining vials from inventory
        int remaining = _vialInventory != null ? _vialInventory.VialCount : 0;
        lootableVials.Value = remaining;
        isLooted.Value = (remaining <= 0);

        Debug.Log($"[CorpseLootableNet] Player '{name}' died with {remaining} vials available to loot.");
    }

    private void Update()
    {
        // Only alive, non-owner players can loot this corpse
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null || localPlayer.gameObject == gameObject) return;

        var localHealth = localPlayer.GetComponent<HealthSystem>();
        if (localHealth != null && localHealth.IsDead) return;

        if (_healthSystem == null || !_healthSystem.IsDead || !HasLoot) return;

        // Check distance to corpse
        float dist = Vector3.Distance(localPlayer.transform.position, transform.position);
        if (dist <= interactionDistance)
        {
            // If aiming near corpse or within radius, allow looting with E
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                RequestLootServerRpc(localPlayer.NetworkObjectId);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestLootServerRpc(ulong looterNetId)
    {
        if (!HasLoot) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(looterNetId, out var looterObj))
        {
            var looterVials = looterObj.GetComponent<HealingVialInventoryNet>();
            if (looterVials != null)
            {
                int vialsToGive = lootableVials.Value;
                lootableVials.Value = 0;
                isLooted.Value = true;

                looterVials.AddVialsServer(vialsToGive);
                NotifyLootSuccessClientRpc(vialsToGive, looterNetId);
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyLootSuccessClientRpc(int count, ulong looterNetId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == looterNetId)
        {
            string msg = $"Looted {count} Healing Vial{(count > 1 ? "s" : "")} from corpse!";
            Debug.Log($"[CorpseLootableNet] {msg}");
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(msg, 3f);
            }
        }
    }
}
