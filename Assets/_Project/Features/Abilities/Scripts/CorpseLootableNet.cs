using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using StarterAssets;

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

    [Tooltip("Interaction hotkey to trigger looting (default [E]).")]
    public Key lootKey = Key.E;

    [Tooltip("Layer mask for player corpses.")]
    public LayerMask corpseLayer;

    [Header("Loot Animation Options (Inspector Toggle)")]
    [Tooltip("If true, triggers a picking/looting animation on the character before transferring items. If false, looting is instant.")]
    public bool playLootAnimation = true;

    [Tooltip("The trigger parameter name in the looter's Animator (e.g. 'Pickup', 'Loot', 'Planting').")]
    public string lootAnimationTrigger = "Loot";

    [Tooltip("Base duration in seconds of the looting animation before items transfer.")]
    public float lootDuration = 1.2f;

    [Tooltip("Speed multiplier for the animation playback / wait time. Increase (e.g. 1.5 - 2.0) to speed up slow Mixamo clips!")]
    public float animationSpeedMultiplier = 1.5f;

    [Tooltip("If true, freezes character movement input while the looting animation is playing.")]
    public bool freezeMovementWhileLooting = true;

    [Header("Dead Player Accessories (Visual Inheritance)")]
    [Tooltip("Enable to attach visual cosmetic accessories of the dead player onto the looter.")]
    public bool attachDeadAccessories = false;

    [Tooltip("Optional prefab accessories to mount on looter bone slots (e.g. helmet, gas mask, pickaxe on back, relic pouch).")]
    public GameObject[] accessoryPrefabs;

    [Tooltip("Bone names to mount accessories on in order (e.g. 'Head', 'Spine2', 'Hips', 'RightHand'). If empty, defaults to looter root/chest.")]
    public string[] accessoryMountBones;

    public static event System.Action<GameObject, GameObject> OnCorpseLootedWithAccessories;

    private bool _isLootingInProgress = false;

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

    // Persistent inherited ability flags (transfers down the chain of deaths)
    private bool _hasInheritedWeapon = false;
    private bool _hasInheritedExorcism = false;
    private bool _hasInheritedHazardMask = false;
    private bool _hasInheritedMinimap = false;

    public void InheritWeapon() => _hasInheritedWeapon = true;
    public void InheritExorcism() => _hasInheritedExorcism = true;
    public void InheritHazardMask() => _hasInheritedHazardMask = true;
    public void InheritMinimapGear() => _hasInheritedMinimap = true;

    public bool HasInheritedWeapon => _hasInheritedWeapon;
    public bool HasInheritedExorcism => _hasInheritedExorcism;
    public bool HasInheritedHazardMask => _hasInheritedHazardMask;
    public bool HasInheritedMinimap => _hasInheritedMinimap;

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

    /// <summary>
    /// Returns the exact physical position of the corpse, tracking the ragdoll hips if active.
    /// </summary>
    public Vector3 GetCorpsePosition()
    {
        if (TryGetComponent<NetworkRagdollController>(out var ragdoll))
        {
            if (ragdoll.ragdollRoot != null) return ragdoll.ragdollRoot.position;
        }
        var rb = GetComponentInChildren<Rigidbody>();
        if (rb != null && rb.gameObject != gameObject) return rb.position;
        return transform.position;
    }

    public void HandleDeath()
    {
        if (!IsServer) return;

        string charName = gameObject.name.ToLower();

        // 1. Vials: Strictly the exact number of vials currently carried in inventory.
        // Non-medics drop 0 vials unless they looted them prior to death.
        // Medic drops whatever remains of their 4 starting vials (exhaustible down to 0).
        int remaining = _vialInventory != null ? _vialInventory.VialCount : 0;
        lootableVials.Value = Mathf.Max(0, remaining);

        // 2. Weapon: Miner or investigator who carries/unlocked/inherited a weapon
        bool isMiner = charName.Contains("miner") || charName.Contains("worker") || (_combatNet != null && _combatNet.IsMiner);
        if (!isMiner && CharacterSelectManager.Instance != null)
        {
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[idx];
                if (data != null && data.profession == InvestigatorProfession.MineWorker) isMiner = true;
            }
        }
        hasWeaponLoot.Value = isMiner || (_combatNet != null && (_combatNet.HasWeapon || _combatNet.hasUnlockedWeapon)) || _hasInheritedWeapon;

        // 3. Exorcism Relic: Cursed Priest or investigator who unlocked/inherited Holy Relic
        bool isPriest = charName.Contains("priest") || (_priestExorcism != null && _priestExorcism.isUnlocked);
        if (!isPriest && CharacterSelectManager.Instance != null)
        {
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[idx];
                if (data != null && data.profession == InvestigatorProfession.CursedPriest) isPriest = true;
            }
        }
        hasExorcismRelic.Value = isPriest || _hasInheritedExorcism;

        // 4. Hazard Filter: Hazard Specialist or investigator who inherited Gas Mask
        bool isHazard = charName.Contains("hazard") || charName.Contains("protector") || (_suffocationNet != null && _suffocationNet.IsHazardSpecialist);
        if (!isHazard && CharacterSelectManager.Instance != null)
        {
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[idx];
                if (data != null && data.profession == InvestigatorProfession.HazardSpecialist) isHazard = true;
            }
        }
        hasHazardFilter.Value = isHazard || _hasInheritedHazardMask;

        // 5. Minimap Gear: Explorer / Adventurer or investigator who inherited Minimap gear
        bool isAdventurer = charName.Contains("adventure") || charName.Contains("explorer");
        if (!isAdventurer && CharacterSelectManager.Instance != null)
        {
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[idx];
                if (data != null && data.profession == InvestigatorProfession.Explorer) isAdventurer = true;
            }
        }
        hasMinimapGear.Value = isAdventurer || _hasInheritedMinimap;

        isLooted.Value = !HasLoot;

        Debug.Log($"[CorpseLootableNet] '{name}' died. Loot available: Vials={lootableVials.Value}, Weapon={hasWeaponLoot.Value}, Exorcism={hasExorcismRelic.Value}, Hazard={hasHazardFilter.Value}, Minimap={hasMinimapGear.Value}");
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

        // Proximity check (measured to the physical ragdoll bone position if fallen)
        float dist = Vector3.Distance(activePlayer.transform.position, GetCorpsePosition());
        if (dist <= interactionDistance)
        {
            if (Keyboard.current != null && Keyboard.current[lootKey].wasPressedThisFrame)
            {
                var netObj = activePlayer.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    StartLooting(activePlayer, netObj);
                }
            }
        }
    }

    private void StartLooting(GameObject activePlayer, NetworkObject looterNetObj)
    {
        if (_isLootingInProgress || !HasLoot) return;

        if (playLootAnimation)
        {
            StartCoroutine(LootAnimationRoutine(activePlayer, looterNetObj));
        }
        else
        {
            RequestLootServerRpc(looterNetObj.NetworkObjectId);
        }
    }

    private IEnumerator LootAnimationRoutine(GameObject activePlayer, NetworkObject looterNetObj)
    {
        _isLootingInProgress = true;

        // 1. Temporarily freeze movement if requested
        StarterAssetsInputs inputs = activePlayer.GetComponent<StarterAssetsInputs>();
        ThirdPersonController controller = activePlayer.GetComponent<ThirdPersonController>();

        if (freezeMovementWhileLooting)
        {
            if (inputs != null)
            {
                inputs.move = Vector2.zero;
                inputs.sprint = false;
            }
            if (controller != null)
            {
                controller.enabled = false;
            }
        }

        // 2. Play Loot Animation on Animator
        var anim = activePlayer.GetComponentInChildren<Animator>();
        if (anim != null && !string.IsNullOrEmpty(lootAnimationTrigger))
        {
            bool hasParam = false;
            foreach (var p in anim.parameters)
            {
                if (p.name == lootAnimationTrigger)
                {
                    hasParam = true;
                    break;
                }
            }

            if (hasParam)
            {
                anim.SetTrigger(lootAnimationTrigger);
            }
            else
            {
                Debug.LogWarning($"[CorpseLootableNet] Animator does not have parameter '{lootAnimationTrigger}'. Create a Trigger parameter named '{lootAnimationTrigger}' in your Animator Controller.");
            }
        }

        // 3. Wait for the looting action to finish (scaled by speed multiplier)
        float waitTime = Mathf.Max(0.2f, lootDuration / Mathf.Max(0.1f, animationSpeedMultiplier));
        yield return new WaitForSeconds(waitTime);

        // 4. Send RPC to server to grant items & abilities
        RequestLootServerRpc(looterNetObj.NetworkObjectId);

        // 5. Restore movement
        if (controller != null)
        {
            controller.enabled = true;
        }

        _isLootingInProgress = false;
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

            var looterCorpse = looterObj.GetComponent<CorpseLootableNet>();

            // 1. Transfer Vials
            var looterVials = looterObj.GetComponent<HealingVialInventoryNet>();
            if (vialsGained > 0)
            {
                if (looterVials == null)
                {
                    looterVials = looterObj.gameObject.AddComponent<HealingVialInventoryNet>();
                }
                looterVials.AddVialsServer(vialsGained);
            }

            // 2. Transfer Weapon
            var looterCombat = looterObj.GetComponent<InvestigatorCombatNet>();
            if (hasWeaponLoot.Value)
            {
                if (looterCombat != null && !looterCombat.HasWeapon)
                {
                    looterCombat.GrantMeleeWeapon();
                }
                if (looterCorpse != null)
                {
                    looterCorpse.InheritWeapon();
                }
                weaponGained = true;
            }

            // 3. Inherit Exorcism Ability (Cursed Priest)
            var looterPriest = looterObj.GetComponent<PriestExorcismNet>();
            if (hasExorcismRelic.Value)
            {
                if (looterPriest == null)
                {
                    looterPriest = looterObj.gameObject.AddComponent<PriestExorcismNet>();
                }
                looterPriest.InheritExorcismAbility();
                if (looterCorpse != null)
                {
                    looterCorpse.InheritExorcism();
                }
                exorcismGained = true;
            }

            // 4. Inherit Hazard Filter (Hazard Specialist)
            var looterSuffocation = looterObj.GetComponent<SuffocationSystemNet>();
            if (hasHazardFilter.Value)
            {
                if (looterSuffocation != null)
                {
                    looterSuffocation.InheritHazardFilter();
                }
                if (looterCorpse != null)
                {
                    looterCorpse.InheritHazardMask();
                }
                hazardGained = true;
            }

            // 5. Inherit Minimap (Explorer / Adventurer)
            if (hasMinimapGear.Value)
            {
                if (looterCorpse != null)
                {
                    looterCorpse.InheritMinimapGear();
                }
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
            var activePlayer = GetActiveControlledCharacter();
            if (activePlayer != null && activePlayer.TryGetComponent<CorpseLootableNet>(out var localCorpse))
            {
                if (weapon) localCorpse.InheritWeapon();
                if (exorcism) localCorpse.InheritExorcism();
                if (hazard) localCorpse.InheritHazardMask();
                if (minimap) localCorpse.InheritMinimapGear();
            }

            List<string> gained = new List<string>();
            if (vials > 0) gained.Add($"{vials} Healing Vial{(vials > 1 ? "s" : "")}");
            if (weapon) gained.Add("Melee Pickaxe");
            if (exorcism) gained.Add("Exorcism Rite ([R]) & Possession Resistance");
            if (hazard) gained.Add("Gas Mask (Doubled Health & Lifespan)");
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

            // Visual dead accessories attachment
            if (activePlayer != null)
            {
                if (attachDeadAccessories)
                {
                    MountAccessoriesOnCharacter(activePlayer);
                }
                OnCorpseLootedWithAccessories?.Invoke(activePlayer, gameObject);
            }
        }
    }

    private void MountAccessoriesOnCharacter(GameObject looter)
    {
        if (accessoryPrefabs == null || accessoryPrefabs.Length == 0) return;

        for (int i = 0; i < accessoryPrefabs.Length; i++)
        {
            var prefab = accessoryPrefabs[i];
            if (prefab == null) continue;

            Transform targetParent = looter.transform;
            if (accessoryMountBones != null && i < accessoryMountBones.Length && !string.IsNullOrEmpty(accessoryMountBones[i]))
            {
                var foundBone = FindChildRecursive(looter.transform, accessoryMountBones[i]);
                if (foundBone != null) targetParent = foundBone;
            }

            var spawned = Instantiate(prefab, targetParent);
            spawned.transform.localPosition = Vector3.zero;
            spawned.transform.localRotation = Quaternion.identity;
            Debug.Log($"[CorpseLootableNet] Attached accessory '{prefab.name}' to '{targetParent.name}' on {looter.name}");
        }
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase)) return parent;
        foreach (Transform child in parent)
        {
            var found = FindChildRecursive(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Builds a short human-readable summary of what loot is currently available on this corpse.
    /// </summary>
    public string GetLootDescription()
    {
        List<string> items = new List<string>();
        if (lootableVials.Value > 0) items.Add($"{lootableVials.Value} Vial{(lootableVials.Value > 1 ? "s" : "")}");
        if (hasWeaponLoot.Value) items.Add("Pickaxe");
        if (hasExorcismRelic.Value) items.Add("Holy Relic");
        if (hasHazardFilter.Value) items.Add("Gas Mask");
        if (hasMinimapGear.Value) items.Add("Minimap");

        return items.Count > 0 ? string.Join(", ", items) : "Empty";
    }
}
