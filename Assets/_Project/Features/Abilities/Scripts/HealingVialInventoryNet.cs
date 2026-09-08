using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages healing vials carried by a player, healing interactions with teammates or self,
/// and optional animation triggers for healing.
/// Medic starts with 4 vials (configurable). Other players start with 0 unless looted.
/// </summary>
public class HealingVialInventoryNet : NetworkBehaviour
{
    [Header("Vials Settings")]
    [Tooltip("Initial number of vials this character spawns with. Field Medic defaults to 4; others default to 0.")]
    public int initialVials = 0;

    [Tooltip("Amount of HP restored per vial consumed.")]
    public float healAmount = 50f;

    [Tooltip("Maximum interaction range to heal a teammate.")]
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

    private Animator _animator;
    private HealthSystem _healthSystem;
    private Transform _cameraTransform;

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

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }
    }

    public override void OnNetworkSpawn()
    {
        currentVials.OnValueChanged += HandleVialsChanged;

        if (IsServer)
        {
            // Auto-detect if this character is the Field Medic
            int startCount = initialVials;
            if (gameObject.name.ToLower().Contains("medic") || startCount > 0)
            {
                startCount = Mathf.Max(4, startCount);
            }
            currentVials.Value = startCount;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentVials.OnValueChanged -= HandleVialsChanged;
    }

    private void HandleVialsChanged(int previous, int current)
    {
        OnVialsChanged?.Invoke(current);
    }

    private void Update()
    {
        if (!IsOwner || _healthSystem == null || _healthSystem.IsDead) return;

        if (_cameraTransform == null && Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }

        // Heal teammate with E key (when aiming at them) or self-heal with H key
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                TryInteractHeal();
            }
            else if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                TrySelfHeal();
            }
        }
    }

    /// <summary>
    /// Attempts to heal an aiming teammate within healRange, or self if no teammate is targeted.
    /// </summary>
    public void TryInteractHeal()
    {
        if (currentVials.Value <= 0) return;

        Transform cam = _cameraTransform != null ? _cameraTransform : transform;
        Ray ray = new Ray(cam.position, cam.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, healRange, playerLayer))
        {
            if (hit.collider.gameObject == gameObject) return;

            HealthSystem targetHealth = hit.collider.GetComponentInParent<HealthSystem>();
            if (targetHealth != null && !targetHealth.IsDead && targetHealth.CurrentHealth < targetHealth.MaxHealth)
            {
                NetworkObject targetNetObj = targetHealth.GetComponent<NetworkObject>();
                if (targetNetObj != null)
                {
                    PerformHealServerRpc(targetNetObj.NetworkObjectId);
                    PlayHealEffects();
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Consumes a vial to heal self.
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
    private void PerformHealServerRpc(ulong targetNetId)
    {
        if (currentVials.Value <= 0) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var targetObj))
        {
            HealthSystem targetHp = targetObj.GetComponent<HealthSystem>();
            if (targetHp != null && !targetHp.IsDead)
            {
                currentVials.Value--;
                targetHp.Heal(healAmount);
                NotifyHealPerformedClientRpc();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyHealPerformedClientRpc()
    {
        PlayHealEffects();
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
            _audioSource.PlayOneShot(healSound);
        }
    }

    /// <summary>
    /// Adds vials (e.g. from looting a corpse).
    /// </summary>
    public void AddVialsServer(int count)
    {
        if (IsServer)
        {
            currentVials.Value += count;
        }
    }
}
