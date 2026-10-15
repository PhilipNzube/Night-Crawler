using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using NightCrawler.Economy;

/// <summary>
/// SOLID — SRP: Manages medical syringes / auto-injectors carried by a player, healing interactions with teammates or self,
/// and optional animation triggers for healing.
/// Medic starts with 4 syringes (configurable). Other players start with 0 unless looted.
/// Note: Syringes are injected directly through combat suits/respirator masks to stabilize trauma.
/// </summary>
public class HealingVialInventoryNet : NetworkBehaviour
{
    [Header("Syringe / Injector Settings")]
    [Tooltip("Initial number of medical syringes this character spawns with. Field Medic defaults to 4; others default to 0.")]
    public int initialVials = 0;
    public int initialSyringes { get => initialVials; set => initialVials = value; }

    [Tooltip("Amount of HP restored per syringe injected.")]
    public float healAmount = 50f;

    [Tooltip("Maximum interaction range to inject a teammate.")]
    public float healRange = 2.5f;

    [Tooltip("Layer mask for player detection.")]
    public LayerMask playerLayer;

    [Header("Animation & Visuals")]
    [Tooltip("Optional Animator trigger name to fire when playing a healing animation.")]
    public string healAnimationTrigger = "Heal";

    [Tooltip("Optional direct AnimationClip reference for reference or custom playable graphs.")]
    public AnimationClip healAnimationClip;

    [Header("Audio (Optional)")]
    public AudioClip healSound;
    private AudioSource _audioSource;

    [Header("Network State")]
    public NetworkVariable<int> currentVials = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action<int> OnVialsChanged;
    public event Action<int> OnSyringesChanged;

    private Animator _animator;
    private HealthSystem _healthSystem;
    private Transform _cameraTransform;

    public int SyringeCount => currentVials.Value;
    public int VialCount => currentVials.Value;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _healthSystem = GetComponent<HealthSystem>();
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 1f;
        }
        _audioSource.volume = GameSettingsManager.SFXVolume;

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        GameSettingsManager.OnSFXVolumeChanged += HandleSFXVolumeChanged;
        if (_audioSource != null) _audioSource.volume = GameSettingsManager.SFXVolume;
    }

    private void OnDisable()
    {
        GameSettingsManager.OnSFXVolumeChanged -= HandleSFXVolumeChanged;
    }

    private void HandleSFXVolumeChanged(float vol)
    {
        if (_audioSource != null) _audioSource.volume = vol;
    }

    [Header("Capacity Settings")]
    [Tooltip("Maximum syringes a non-Medic character can hold in inventory.")]
    public int nonMedicMaxCapacity = 2;

    public bool IsMedicCharacter()
    {
        if (gameObject.name.ToLower().Contains("medic")) return true;
        if (CharacterSelectManager.Instance != null)
        {
            int idx = CharacterSelectManager.Instance.GetSelectedCharacterIndex(OwnerClientId);
            if (idx >= 0 && CharacterSelectManager.Instance.availableCharacters != null && idx < CharacterSelectManager.Instance.availableCharacters.Count)
            {
                var data = CharacterSelectManager.Instance.availableCharacters[idx];
                if (data != null && data.profession == InvestigatorProfession.FieldMedic)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public int MaxCapacity
    {
        get
        {
            int syringeCountLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.SyringeCount) : 0;
            if (IsMedicCharacter())
            {
                return UpgradeStatFormulas.GetStartingSyringeCount(syringeCountLvl);
            }
            return nonMedicMaxCapacity;
        }
    }

    public override void OnNetworkSpawn()
    {
        currentVials.OnValueChanged += HandleVialsChanged;

        if (IsServer)
        {
            bool isMedic = IsMedicCharacter();

            // Only Medic starts with syringes. Scale starting capacity by persistent SyringeCountLevel.
            int syringeCountLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.SyringeCount) : 0;
            int startingSyringes = UpgradeStatFormulas.GetStartingSyringeCount(syringeCountLvl);
            currentVials.Value = isMedic ? startingSyringes : 0;
            Debug.Log($"[HealingVialInventoryNet] '{gameObject.name}' spawned with {currentVials.Value} syringes (isMedic: {isMedic}, MaxCapacity: {MaxCapacity}, SyringeCountLevel: {syringeCountLvl}).");
        }
    }

    public override void OnNetworkDespawn()
    {
        currentVials.OnValueChanged -= HandleVialsChanged;
    }

    private void HandleVialsChanged(int previous, int current)
    {
        OnVialsChanged?.Invoke(current);
        OnSyringesChanged?.Invoke(current);
    }

    private void Update()
    {
        if (!IsOwner || _healthSystem == null || _healthSystem.IsDead || PauseManager.IsGamePaused || SpectatorController.IsAnySpectating) return;

        if (_cameraTransform == null && Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }

        // Send syringe to teammate with Interact key (when aiming at them) or self-inject with Heal key
        bool interactPressed = KeybindingManager.IsActionTriggered("Interact") || (KeybindingManager.Instance == null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);
        bool healPressed = KeybindingManager.IsActionTriggered("Heal") || (KeybindingManager.Instance == null && Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame);

        if (interactPressed)
        {
            TryInteractHeal();
        }
        else if (healPressed)
        {
            TrySelfHeal();
        }
    }

    /// <summary>
    /// Attempts to send an auto-injector syringe to a targeted teammate within healRange.
    /// </summary>
    public void TryInteractHeal()
    {
        if (currentVials.Value <= 0) return;

        Transform cam = _cameraTransform != null ? _cameraTransform : transform;
        Ray ray = new Ray(cam.position, cam.forward);

        // Fallback mask: if playerLayer is not configured in Inspector, hit anything except UI and Ignore Raycast
        LayerMask mask = playerLayer.value != 0 ? playerLayer : ~LayerMask.GetMask("Ignore Raycast", "UI");

        if (Physics.Raycast(ray, out RaycastHit hit, healRange, mask))
        {
            if (hit.collider.gameObject != gameObject)
            {
                HealingVialInventoryNet targetInv = hit.collider.GetComponentInParent<HealingVialInventoryNet>();
                if (targetInv != null)
                {
                    TrySendVialToTeammate(targetInv);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Transfers a medical syringe to a target teammate's inventory.
    /// The recipient decides when to inject it and is not healed immediately.
    /// If the recipient's stats cannot carry more syringes, sending fails and a warning notification is displayed.
    /// </summary>
    public bool TrySendVialToTeammate(HealingVialInventoryNet recipient)
    {
        if (currentVials.Value <= 0)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("You have no medical syringes left to give!", 2.5f);
            }
            return false;
        }

        if (recipient == null || recipient == this) return false;

        var recHealth = recipient.GetComponent<HealthSystem>();
        if (recHealth != null && recHealth.IsDead) return false;

        // Check recipient capacity against their stats
        if (recipient.SyringeCount >= recipient.MaxCapacity)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"Teammate cannot carry any more syringes! (Full: {recipient.SyringeCount}/{recipient.MaxCapacity})", 3f);
            }
            return false;
        }

        NetworkObject recNetObj = recipient.GetComponent<NetworkObject>();
        if (recNetObj != null)
        {
            SendVialTransferServerRpc(recNetObj.NetworkObjectId);
            PlayHealEffects();
            return true;
        }

        return false;
    }

    public bool TrySendSyringeToTeammate(HealingVialInventoryNet recipient) => TrySendVialToTeammate(recipient);

    /// <summary>
    /// Administers/sends a medical syringe directly to a designated target teammate.
    /// </summary>
    public bool TryHealTarget(HealthSystem targetHealth)
    {
        if (targetHealth == null) return false;
        HealingVialInventoryNet targetInv = targetHealth.GetComponent<HealingVialInventoryNet>();
        if (targetInv != null)
        {
            return TrySendVialToTeammate(targetInv);
        }
        return false;
    }

    /// <summary>
    /// Injects an auto-injector medical syringe to heal self.
    /// </summary>
    public void TrySelfHeal()
    {
        if (currentVials.Value <= 0) return;
        if (_healthSystem == null || _healthSystem.IsDead) return;
        if (_healthSystem.CurrentHealth >= _healthSystem.MaxHealth) return;

        NetworkObject myNetObj = GetComponent<NetworkObject>();
        if (myNetObj != null)
        {
            PerformHealServerRpc(myNetObj.NetworkObjectId);
            PlayHealEffects();
        }
    }

    [Rpc(SendTo.Server)]
    private void SendVialTransferServerRpc(ulong recipientNetId)
    {
        if (currentVials.Value <= 0) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(recipientNetId, out var targetObj))
        {
            HealingVialInventoryNet recipientInv = targetObj.GetComponent<HealingVialInventoryNet>();
            if (recipientInv != null && recipientInv.SyringeCount < recipientInv.MaxCapacity)
            {
                currentVials.Value--;
                recipientInv.currentVials.Value++;

                NotifyVialTransferClientRpc(OwnerClientId, targetObj.OwnerClientId);

                if (MatchEconomyManager.Instance != null)
                {
                    MatchEconomyManager.Instance.LogHeal(OwnerClientId);
                }
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyVialTransferClientRpc(ulong senderClientId, ulong recipientClientId)
    {
        PlayHealEffects();

        ulong myClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        if (myClientId == recipientClientId)
        {
            if (NotificationManager.Instance != null)
            {
                string healKey = KeybindingManager.GetBoundKeyString("Heal", "H");
                NotificationManager.Instance.ShowNotification(
                    "MEDICAL SYRINGE RECEIVED",
                    $"Auto-injector supplies delivered by teammate. Press [{healKey}] to inject coagulant.",
                    new Color(0.15f, 0.95f, 0.6f, 1f),
                    4.5f
                );
            }
        }
        else if (myClientId == senderClientId)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(
                    "MEDICAL SYRINGE DISPATCHED",
                    "Medical syringe successfully transferred to operative in range.",
                    new Color(0.2f, 0.85f, 1f, 1f),
                    4.0f
                );
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void PerformHealServerRpc(ulong targetNetId)
    {
        if (currentVials.Value <= 0) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var targetObj))
        {
            HealthSystem targetHp = targetObj.GetComponent<HealthSystem>();
            if (targetHp != null && !targetHp.IsDead)
            {
                currentVials.Value--;

                int healLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.SyringeHealingPower) : 0;
                float finalHeal = UpgradeStatFormulas.GetSyringeHealAmount(healLvl);

                targetHp.Heal(finalHeal);
                NotifyHealPerformedClientRpc(targetNetId, finalHeal);

                // Log contribution for Medic
                if (MatchEconomyManager.Instance != null)
                {
                    MatchEconomyManager.Instance.LogHeal(OwnerClientId);
                }
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyHealPerformedClientRpc(ulong recipientNetId, float amountHealed)
    {
        PlayHealEffects();

        // If this client owns the recipient character, display restorative health message
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(recipientNetId, out var obj) &&
            obj.IsOwner)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowHealthRestored($"Medical syringe injected (+{amountHealed:F0} HP)!", amountHealed);
            }
        }
    }

    private void PlayHealEffects()
    {
        if (_animator != null && !string.IsNullOrEmpty(healAnimationTrigger))
        {
            // Check if parameter exists in animator to avoid exceptions
            foreach (var p in _animator.parameters)
            {
                if (p.name == healAnimationTrigger)
                {
                    _animator.SetTrigger(healAnimationTrigger);
                    break;
                }
            }
        }

        if (_audioSource != null && healSound != null)
        {
            _audioSource.PlayOneShot(healSound, GameSettingsManager.SFXVolume);
        }
    }

    /// <summary>
    /// Adds syringes (e.g. from looting a corpse).
    /// </summary>
    public void AddVialsServer(int count)
    {
        if (IsServer)
        {
            currentVials.Value += count;
        }
    }

    public void AddSyringesServer(int count) => AddVialsServer(count);
}
