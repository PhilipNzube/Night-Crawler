using UnityEngine;
using System;
using Unity.Netcode;

/// <summary>
/// SOLID — SRP: Reusable Health / Damage System for any entity (Player, NPC, Enemy, Prop).
/// Works standalone or with Netcode for GameObjects (server-authoritative damage).
///
/// Usage: Add to any GameObject that can take damage.
///   Subscribe to OnDied or OnHealthChanged for reactions (UI bars, ragdoll, respawn...).
/// </summary>
public class HealthSystem : NetworkBehaviour, IDamageReceiver
{
    [Header("Health Settings")]
    public float maxHealth = 100f;

    [Tooltip("If true, health is a NetworkVariable and synced to all clients.")]
    public bool networked = true;

    // Events — subscribe in character scripts or UI
    public event Action<float, float> OnHealthChanged; // (current, max)
    public event Action               OnDied;
    public event Action<float>        OnDamageTaken;
    public event Action               OnRevived;

    private NetworkVariable<float> _networkHealth = new(100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float _localHealth;
    private bool  _isDead;

    public float CurrentHealth => networked && IsSpawned ? _networkHealth.Value : _localHealth;
    public float MaxHealth     => maxHealth;
    public bool  IsDead        => _isDead;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    private void Awake()
    {
        _localHealth = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        _networkHealth.Value = maxHealth;
        _networkHealth.OnValueChanged += HandleNetworkHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        _networkHealth.OnValueChanged -= HandleNetworkHealthChanged;
    }

    // =========================================================================
    //  IDamageReceiver implementation
    // =========================================================================

    public void TakeDamage(float amount, bool isSoulAttack = false)
    {
        if (_isDead) return;

        // isSoulAttack can be used here to apply type-based multipliers in future
        if (networked && IsSpawned)
        {
            if (IsServer) ApplyDamageServer(amount, isSoulAttack);
            else          TakeDamageServerRpc(amount, isSoulAttack);
        }
        else
        {
            ApplyDamageLocal(amount);
        }
    }

    // =========================================================================
    //  Public API
    // =========================================================================

    public void Heal(float amount)
    {
        if (_isDead) return;

        if (networked && IsSpawned)
        {
            if (IsServer) _networkHealth.Value = Mathf.Clamp(_networkHealth.Value + amount, 0, maxHealth);
        }
        else
        {
            _localHealth = Mathf.Clamp(_localHealth + amount, 0, maxHealth);
            OnHealthChanged?.Invoke(_localHealth, maxHealth);
        }
    }

    public void Revive(float withHealth = -1f)
    {
        _isDead = false;
        float hp = withHealth > 0 ? withHealth : maxHealth;

        if (networked && IsSpawned && IsServer)
            _networkHealth.Value = hp;
        else
            _localHealth = hp;

        OnRevived?.Invoke();
        OnHealthChanged?.Invoke(hp, maxHealth);
    }

    // =========================================================================
    //  Private Helpers
    // =========================================================================

    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(float amount, bool isSoulAttack)
    {
        ApplyDamageServer(amount, isSoulAttack);
    }

    private void ApplyDamageServer(float amount, bool isSoulAttack = false)
    {
        if (_isDead) return;
        _networkHealth.Value = Mathf.Max(0f, _networkHealth.Value - amount);
        OnDamageTaken?.Invoke(amount);

        if (_networkHealth.Value <= 0f)
        {
            _isDead = true;
            OnDied?.Invoke();
        }
    }

    private void ApplyDamageLocal(float amount)
    {
        _localHealth = Mathf.Max(0f, _localHealth - amount);
        OnDamageTaken?.Invoke(amount);
        OnHealthChanged?.Invoke(_localHealth, maxHealth);

        if (_localHealth <= 0f && !_isDead)
        {
            _isDead = true;
            OnDied?.Invoke();
        }
    }

    private void HandleNetworkHealthChanged(float prev, float current)
    {
        OnHealthChanged?.Invoke(current, maxHealth);

        if (current <= 0f && !_isDead)
        {
            _isDead = true;
            OnDied?.Invoke();
        }
    }
}
