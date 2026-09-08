using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// SOLID — SRP: Manages humanoid ragdoll physics on character death across the network.
/// Automatically collects all bone Rigidbodies & Colliders, keeps them kinematic while alive,
/// and activates ragdoll physics seamlessly upon HealthSystem.OnDied.
/// </summary>
public class NetworkRagdollController : NetworkBehaviour
{
    [Header("Ragdoll Settings")]
    [Tooltip("Root bone of the character (e.g. Hips/Pelvis). If null, finds child rigidbodies automatically.")]
    public Transform ragdollRoot;

    [Tooltip("Optional upward/forward impulse to add on death.")]
    public float deathImpulse = 2.5f;

    private readonly List<Rigidbody> _ragdollRigidbodies = new List<Rigidbody>();
    private readonly List<Collider> _ragdollColliders = new List<Collider>();

    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private CharacterController _characterController;
    private HealthSystem _healthSystem;

    private bool _isRagdollActive = false;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _characterController = GetComponent<CharacterController>();
        _healthSystem = GetComponent<HealthSystem>();

        CollectRagdollComponents();
        SetRagdollActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied += TriggerRagdollDeath;
            _healthSystem.OnRevived += ResetRagdoll;

            // If already dead when spawned
            if (_healthSystem.IsDead)
            {
                TriggerRagdollDeath();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= TriggerRagdollDeath;
            _healthSystem.OnRevived -= ResetRagdoll;
        }
    }

    /// <summary>
    /// Finds all child rigidbodies and colliders that make up the humanoid ragdoll.
    /// </summary>
    public void CollectRagdollComponents()
    {
        _ragdollRigidbodies.Clear();
        _ragdollColliders.Clear();

        Transform root = ragdollRoot != null ? ragdollRoot : transform;
        var rbs = root.GetComponentsInChildren<Rigidbody>(true);
        var cols = root.GetComponentsInChildren<Collider>(true);

        foreach (var rb in rbs)
        {
            // Do not include the main character root rigidbody if one exists
            if (rb.gameObject == gameObject) continue;
            _ragdollRigidbodies.Add(rb);
        }

        foreach (var col in cols)
        {
            // Exclude main CharacterController or trigger colliders on root
            if (col.gameObject == gameObject || col is CharacterController) continue;
            _ragdollColliders.Add(col);
        }
    }

    /// <summary>
    /// Enables or disables ragdoll bone physics.
    /// </summary>
    public void SetRagdollActive(bool active)
    {
        _isRagdollActive = active;

        // 1. Toggle main movement controller
        if (_characterController != null)
        {
            _characterController.enabled = !active;
        }

        // 2. Toggle animators
        if (_animator != null)
        {
            _animator.enabled = !active;
        }

        // 3. Toggle bone colliders and rigidbodies
        foreach (var col in _ragdollColliders)
        {
            if (col != null) col.enabled = active;
        }

        foreach (var rb in _ragdollRigidbodies)
        {
            if (rb != null)
            {
                rb.isKinematic = !active;
                rb.detectCollisions = active;
            }
        }
    }

    public void TriggerRagdollDeath()
    {
        if (_isRagdollActive) return;

        SetRagdollActive(true);

        // Apply slight impulse to hips so the body collapses naturally
        if (_ragdollRigidbodies.Count > 0 && _ragdollRigidbodies[0] != null)
        {
            Vector3 force = (-transform.forward * 0.5f + Vector3.up * 0.2f) * deathImpulse;
            _ragdollRigidbodies[0].AddForce(force, ForceMode.Impulse);
        }
    }

    public void ResetRagdoll()
    {
        SetRagdollActive(false);
    }
}
