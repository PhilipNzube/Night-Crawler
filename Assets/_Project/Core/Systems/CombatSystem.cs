using UnityEngine;
using System;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Modular Combat System component for any character (Explorer, Girl, Monster, NPC, AI).
/// OCP & DIP: Implements ICombatHandler abstraction. Can be added to any entity to grant combat capabilities.
/// </summary>
public interface ICombatHandler
{
    bool CanAttack { get; }
    void PerformMeleeAttack(float damage, float range, float radius, LayerMask targetLayer);
    void PerformRangedAttack(Ray fireRay, float damage, float maxDistance, LayerMask targetLayer);
    event Action OnAttackTriggered;
    event Action<float> OnDamageDealt;
}

public class CombatSystem : NetworkBehaviour, ICombatHandler
{
    [Header("Combat Settings")]
    [Tooltip("Base damage dealt by this entity.")]
    public float baseDamage = 25f;

    [Tooltip("Cooldown between attacks in seconds.")]
    public float attackCooldown = 1.0f;

    [Tooltip("Target layer mask for hit detection.")]
    public LayerMask targetLayerMask;

    [Header("Audio SFX (Optional)")]
    public AudioSource audioSource;
    public AudioClip attackSound;
    public AudioClip hitSound;

    // Events
    public event Action OnAttackTriggered;
    public event Action<float> OnDamageDealt;

    private float _attackTimer = 0f;

    public bool CanAttack => _attackTimer <= 0f;

    private void Update()
    {
        if (_attackTimer > 0f)
        {
            _attackTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Performs a sphere-overlap melee attack in front of the entity.
    /// </summary>
    public void PerformMeleeAttack(float damage, float range, float radius, LayerMask targetLayer)
    {
        if (!CanAttack) return;
        _attackTimer = attackCooldown;

        OnAttackTriggered?.Invoke();
        PlayAudio(attackSound);

        if (!IsServer && NetworkManager.Singleton != null)
        {
            PerformMeleeServerRpc(damage, range, radius, targetLayer.value);
            return;
        }

        ExecuteMeleeHitCheck(transform.position + transform.forward * range + Vector3.up * 1.0f, radius, damage, targetLayer);
    }

    [ServerRpc]
    private void PerformMeleeServerRpc(float damage, float range, float radius, int targetLayerValue)
    {
        ExecuteMeleeHitCheck(transform.position + transform.forward * range + Vector3.up * 1.0f, radius, damage, (LayerMask)targetLayerValue);
    }

    private void ExecuteMeleeHitCheck(Vector3 sphereCenter, float radius, float damage, LayerMask layer)
    {
        Collider[] hits = Physics.OverlapSphere(sphereCenter, radius, layer);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            if (hit.TryGetComponent<IDamageReceiver>(out var damageReceiver))
            {
                damageReceiver.TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
                PlayAudio(hitSound);
            }
            else if (hit.TryGetComponent<TargetHealth>(out var targetHealth))
            {
                targetHealth.TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
                PlayAudio(hitSound);
            }
        }
    }

    /// <summary>
    /// Performs a raycast ranged attack (e.g. firearm or spell beam).
    /// </summary>
    public void PerformRangedAttack(Ray fireRay, float damage, float maxDistance, LayerMask targetLayer)
    {
        if (!CanAttack) return;
        _attackTimer = attackCooldown;

        OnAttackTriggered?.Invoke();
        PlayAudio(attackSound);

        if (Physics.Raycast(fireRay, out RaycastHit hit, maxDistance, targetLayer))
        {
            if (hit.collider.TryGetComponent<IDamageReceiver>(out var damageReceiver))
            {
                damageReceiver.TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
                PlayAudio(hitSound);
            }
            else if (hit.collider.TryGetComponent<TargetHealth>(out var targetHealth))
            {
                targetHealth.TakeDamage(damage);
                OnDamageDealt?.Invoke(damage);
                PlayAudio(hitSound);
            }
        }
    }

    private void PlayAudio(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, GameSettingsManager.SFXVolume);
        }
    }
}
