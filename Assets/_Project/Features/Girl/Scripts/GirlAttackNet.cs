using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class GirlAttackNet : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;

    [Header("References")]
    public Transform leftHandTransform;
    public Transform rightHandTransform;
    
    private float _attackTimer;
    private NetworkAnimator _networkAnimator;
    private readonly int _attackHash = Animator.StringToHash("Attack");

    void Awake()
    {
        _networkAnimator = GetComponent<NetworkAnimator>();
        // Cache once at start
        AutoFindHands();
    }

    private void OnValidate()
    {
        // This runs automatically in the Editor!
        AutoFindHands();
    }

    private void AutoFindHands()
    {
        if (leftHandTransform == null) leftHandTransform = FindDeepChild(transform, "ik_hand_l");
        if (rightHandTransform == null) rightHandTransform = FindDeepChild(transform, "ik_hand_r");
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // Left Click to Attack
        if (Mouse.current.leftButton.wasPressedThisFrame && _attackTimer <= 0)
        {
            // Trigger animation locally (OwnerNetworkAnimator will sync it!)
            if (_networkAnimator != null)
            {
                _networkAnimator.SetTrigger(_attackHash);
            }

            _attackTimer = stats.attackCooldown;
        }
    }

    // THIS IS CALLED BY YOUR ANIMATION EVENT
    // Use parameter: 0 = Left Hand, 1 = Right Hand, 2 = BOTH
    public void OnAttackSwipe(int handIndex)
    {
        // 1. LOCAL VISUALS: Everyone spawns their own crescent exactly on their own event frame
        SpawnAttackVfxLocal(handIndex);

        // 2. SERVER DAMAGE: Only the Server performs the actual hit detection
        if (IsServer)
        {
            PerformHitCheck();
        }
    }

    private void SpawnAttackVfxLocal(int handIndex)
    {
        if (handIndex == 0 || handIndex == 2) SpawnCrescent(leftHandTransform);
        if (handIndex == 1 || handIndex == 2) SpawnCrescent(rightHandTransform);
    }

    private void SpawnCrescent(Transform hand)
    {
        Vector3 spawnPos = (hand != null) ? hand.position : transform.position + transform.forward + Vector3.up * 1.5f;
        
        // --- OPTIMIZED: Using EffectPool instead of new GameObject ---
        if (EffectPool.Instance != null)
        {
            EffectPool.Instance.Get("BloodCrescent", spawnPos, transform.rotation);
        }
        else
        {
            GameObject vfx = new GameObject("BloodCrescent_VFX");
            vfx.transform.position = spawnPos;
            vfx.transform.rotation = transform.rotation;
            vfx.AddComponent<GirlBloodCrescentFX>();
        }
    }

    private void PerformHitCheck()
    {
        // Scan a sphere in front of the Demon for players/monsters
        // We move the sphere slightly higher and further out to match the visual crescent
        Vector3 spherePos = transform.position + transform.forward * stats.attackRange + Vector3.up * 1.2f;
        Collider[] hits = Physics.OverlapSphere(spherePos, stats.damageRadius, stats.attackTargetLayer);

        bool hitContact = false;

        foreach (var hit in hits)
        {
            // Don't hit yourself!
            if (hit.gameObject == gameObject) continue;

            if (hit.TryGetComponent<TargetHealth>(out TargetHealth health))
            {
                health.TakeDamage(stats.damageAmount, true); // The Girl does SOUL damage
                hitContact = true;
                Debug.Log($"[SERVER] Demon hit {hit.name} for {stats.damageAmount} SOUL damage!");
            }
        }

        if (hitContact)
        {
            NotifyHitClientRpc();
        }
    }

    [ClientRpc]
    private void NotifyHitClientRpc()
    {
        if (IsOwner)
        {
            Debug.Log("<color=red>[COMBAT] DIRECT HIT REGISTERED!</color>");
            // TIP: You can trigger a Camera Shake or a 'Hit Marker' sound here
        }
    }

    // Visualize the attack range in the editor
    private void OnDrawGizmosSelected()
    {
        if (stats == null) return;
        Gizmos.color = Color.red;
        Vector3 spherePos = transform.position + transform.forward * stats.attackRange + Vector3.up * 1.5f;
        Gizmos.DrawWireSphere(spherePos, stats.damageRadius);
    }
}
