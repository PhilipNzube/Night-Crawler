using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages lootable items and role abilities on a dead player's corpse across the network.
/// When an Investigator dies, surviving teammates can loot their corpse and INHERIT their abilities:
///   - Medic: Remaining Healing Vials + ability to heal.
///   - Miner: Melee Pickaxe weapon.
///   - Cursed Priest: Holy Relic (unlocks Exorcism rite [R] to banish the Girl).
///   - Hazard Specialist: Gas Mask / Respirator Filter (doubles looter's lifespan against poison air).
///   - Explorer / Adventurer: Cartographer's Compass (unlocks the Minimap).
/// </summary>
public class CorpseLootableNet : NetworkBehaviour
{
    [Header("Loot Settings")]
    [Tooltip("Maximum interaction distance to loot the body.")]
    public float interactionDistance = 2.5f;

    [Tooltip("Layer mask for player corpses.")]
    public LayerMask corpseLayer;

    [Header("Network State (Items & Inherited Abilities)")]
    public NetworkVariable<int> lootableVials = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> hasWeaponLoot = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> hasExorcismRelic = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> hasHazardFilter = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> hasMinimapGear = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isLooted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private HealthSystem _healthSystem;
    private HealingVialInventoryNet _vialInventory;
    private InvestigatorCombatNet _combatNet;
    private PriestExorcismNet _priestExorcism;
    private SuffocationSystemNet _suffocationNet;

    public bool HasLoot => !isLooted.Value && (lootableVials.Value > 0 || hasWeaponLoot.Value 
                           || hasExorcismRelic.Value || hasHazardFilter.Value || hasMinimapGear.Value);

    public int RemainingVials => lootableVials.Value;

    private void Awake()
    {
        _healthSystem = GetComponent<HealthSystem>();
        _vialInventory = GetComponent<HealingVialInventoryNet>();
        _combatNet = GetComponent<InvestigatorCombatNet>();
        _priestExorcism = GetComponent<PriestExorcismNet>();
        _suffocationNet = GetComponent<SuffocationSystemNet>();
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

    public void HandleDeath()
    {
        if (!IsServer) return;

        string charName = gameObject.name.ToLower();

        // 1. Vials
        int remaining = _vialInventory != null ? _vialInventory.VialCount : 0;
        lootableVials.Value = remaining;

        // 2. Weapon (Miner or armed investigator)
        hasWeaponLoot.Value = (_combatNet != null && _combatNet.HasWeapon) || charName.Contains("miner");

        // 3. Exorcism Relic (Cursed Priest)
        hasExorcismRelic.Value = (_priestExorcism != null && _priestExorcism.isUnlocked) || charName.Contains("priest");

        // 4. Hazard Filter (Hazard Specialist)
        hasHazardFilter.Value = (_suffocationNet != null && _suffocationNet.IsHazardSpecialist) || charName.Contains("hazard") || charName.Contains("protector");

        // 5. Minimap Gear (Explorer / Adventurer)
        hasMinimapGear.Value = charName.Contains("adventure") || charName.Contains("explorer");

        isLooted.Value = !HasLoot;

        Debug.Log($"[CorpseLootableNet] '{name}' died. Loot available: Vials={remaining}, Weapon={hasWeaponLoot.Value}, Exorcism={hasExorcismRelic.Value}, Hazard={hasHazardFilter.Value}, Minimap={hasMinimapGear.Value}");
    }

    private GameObject GetActiveControlledCharacter()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return null;
        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer == null) return null;

        if (localPlayer.TryGetComponent<GirlPossession>(out var possession) && possession.isPossessing.Value)
        {
            if (possession.CurrentTarget is Component comp && comp != null)
            {
                return comp.gameObject;
            }
        }
        return localPlayer.gameObject;
    }

    private void Update()
    {
        if (PauseManager.IsGamePaused) return;

        // Only alive, non-owner players can loot this corpse
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        var activePlayer = GetActiveControlledCharacter();
        if (activePlayer == null || activePlayer == gameObject) return;

        // The Girl character in her own body CANNOT loot corpses
        if (activePlayer.GetComponent<GirlPossession>() != null ||
            activePlayer.GetComponent<GirlMovement>() != null ||
            activePlayer.GetComponent<GirlStealth>() != null)
        {
            return;
        }

        var localHealth = activePlayer.GetComponent<HealthSystem>();
        if (localHealth != null && localHealth.IsDead) return;
        if (activePlayer.TryGetComponent<TargetHealth>(out var localTh) && (localTh.isCorpse.Value || localTh.CurrentHealth <= 0)) return;

        bool isCorpseDead = (_healthSystem != null && _healthSystem.IsDead) ||
                            (TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0));
        if (!isCorpseDead || !HasLoot) return;

        // Proximity check
        float dist = Vector3.Distance(activePlayer.transform.position, transform.position);
        if (dist <= interactionDistance)
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                var netObj = activePlayer.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    RequestLootServerRpc(netObj.NetworkObjectId);
                }
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestLootServerRpc(ulong looterNetId)
    {
        if (!HasLoot) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(looterNetId, out var looterObj))
        {
            // The Girl character CANNOT loot corpses
            if (looterObj.GetComponent<GirlPossession>() != null ||
                looterObj.GetComponent<GirlMovement>() != null ||
                looterObj.GetComponent<GirlStealth>() != null)
            {
                Debug.Log("[CorpseLootableNet] The Girl character cannot loot a corpse!");
                return;
            }

            int vialsGained = lootableVials.Value;
            bool weaponGained = false;
            bool exorcismGained = false;
            bool hazardGained = false;
            bool minimapGained = false;

            // 1. Transfer Vials
            var looterVials = looterObj.GetComponent<HealingVialInventoryNet>();
            if (looterVials != null && vialsGained > 0)
            {
                looterVials.AddVialsServer(vialsGained);
            }

            // 2. Transfer Weapon
            var looterCombat = looterObj.GetComponent<InvestigatorCombatNet>();
            if (looterCombat != null && hasWeaponLoot.Value && !looterCombat.HasWeapon)
            {
                looterCombat.GrantMeleeWeapon();
                weaponGained = true;
            }

            // 3. Inherit Exorcism Ability (Cursed Priest)
            var looterPriest = looterObj.GetComponent<PriestExorcismNet>();
            if (looterPriest != null && hasExorcismRelic.Value)
            {
                looterPriest.InheritExorcismAbility();
                exorcismGained = true;
            }

            // 4. Inherit Hazard Filter (Hazard Specialist)
            var looterSuffocation = looterObj.GetComponent<SuffocationSystemNet>();
            if (looterSuffocation != null && hasHazardFilter.Value)
            {
                looterSuffocation.InheritHazardFilter();
                hazardGained = true;
            }

            // 5. Inherit Minimap (Explorer / Adventurer)
            if (hasMinimapGear.Value)
            {
                minimapGained = true;
            }

            // Mark corpse as depleted
            lootableVials.Value = 0;
            hasWeaponLoot.Value = false;
            hasExorcismRelic.Value = false;
            hasHazardFilter.Value = false;
            hasMinimapGear.Value = false;
            isLooted.Value = true;

            NotifyLootSuccessClientRpc(vialsGained, weaponGained, exorcismGained, hazardGained, minimapGained, looterNetId);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyLootSuccessClientRpc(int vials, bool weapon, bool exorcism, bool hazard, bool minimap, ulong looterNetId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == looterNetId)
        {
            List<string> gained = new List<string>();
            if (vials > 0) gained.Add($"{vials} Healing Vial{(vials > 1 ? "s" : "")}");
            if (weapon) gained.Add("Melee Pickaxe");
            if (exorcism) gained.Add("Exorcism Rite ([R])");
            if (hazard) gained.Add("Gas Mask Filter (2x Lifespan)");
            if (minimap)
            {
                gained.Add("Minimap Unlocked");
                AdventurerMinimapSetup.UnlockMinimapForLocalPlayer();
            }

            string msg = gained.Count > 0 
                ? $"LOOTED CORPSE: Inherited {string.Join(", ", gained)}!"
                : "Looted corpse.";

            Debug.Log($"[CorpseLootableNet] {msg}");
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(msg, 4.5f);
            }
        }
    }

    /// <summary>
    /// Builds a short human-readable summary of what loot is currently available on this corpse.
    /// </summary>
    public string GetLootDescription()
    {
        List<string> items = new List<string>();
        if (lootableVials.Value > 0) items.Add($"{lootableVials.Value} Vials");
        if (hasWeaponLoot.Value) items.Add("Pickaxe");
        if (hasExorcismRelic.Value) items.Add("Holy Relic");
        if (hasHazardFilter.Value) items.Add("Gas Mask");
        if (hasMinimapGear.Value) items.Add("Minimap");

        return items.Count > 0 ? string.Join(", ", items) : "Empty";
    }
}
