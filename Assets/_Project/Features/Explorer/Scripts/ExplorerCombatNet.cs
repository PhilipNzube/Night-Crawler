using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;
using System.Collections;

public class ExplorerCombatNet : NetworkBehaviour
{
    [Header("Configuration")]
    public WeaponStats axeStats;
    public WeaponStats gunStats;
    public Transform shootPoint; // Drag the gun muzzle here
    
    [Header("Visuals")]
    public GameObject axeVisual;
    public GameObject gunVisual;

    [Header("Runtime State")]
    public NetworkVariable<int> currentWeaponIndex = new NetworkVariable<int>(0, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    public NetworkVariable<int> currentAmmo = new NetworkVariable<int>(0, 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private readonly int _weaponIdHash = Animator.StringToHash("WeaponID");
    private readonly int _switchWeaponHash = Animator.StringToHash("SwitchWeapon");
    private readonly int _attackHash = Animator.StringToHash("Attack");
    private readonly int _reloadHash = Animator.StringToHash("Reload");

    private float _attackTimer;
    private bool _isReloading;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private AudioSource _audioSource;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _audioSource = gameObject.AddComponent<AudioSource>();
        
        // --- NEW: LOUD & CLEAR AUDIO SETUP ---
        _audioSource.playOnAwake = false;
        _audioSource.minDistance = 5f;
        _audioSource.maxDistance = 65f;
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.volume = 1f;
    }

    public override void OnNetworkSpawn()
    {
        // Owner hears their own shots crisp (near 2D), others hear 3D
        _audioSource.spatialBlend = IsOwner ? 0.2f : float.MaxValue; // Use high value for max 3D

        if (IsOwner)
        {
            // Initial weapon setup
            SwitchWeapon(0); // Start with Axe
        }
    }

    void Update()
    {
        if (!IsOwner || _isReloading) return;

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // 1. Weapon Switching
        if (Keyboard.current.digit1Key.wasPressedThisFrame) SwitchWeapon(0); // Axe
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SwitchWeapon(1); // Gun

        // 2. Attack Input
        if (Mouse.current.leftButton.wasPressedThisFrame && _attackTimer <= 0)
        {
            PerformAttack();
        }

        // 3. Reload Input
        if (Keyboard.current.rKey.wasPressedThisFrame && currentWeaponIndex.Value == 1 && currentAmmo.Value < gunStats.maxAmmo)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    private void SwitchWeapon(int index)
    {
        currentWeaponIndex.Value = index;
        if (index == 1) currentAmmo.Value = gunStats.maxAmmo; // Refill on switch for now
        
        // Toggle 3D Models
        if (axeVisual != null) axeVisual.SetActive(index == 0);
        if (gunVisual != null) gunVisual.SetActive(index == 1);

        // Trigger animations
        if (_animator != null)
        {
            _animator.SetInteger(_weaponIdHash, index);
            _animator.SetTrigger(_switchWeaponHash);
        }
    }

    private void PerformAttack()
    {
        WeaponStats activeStats = (currentWeaponIndex.Value == 0) ? axeStats : gunStats;
        
        if (currentWeaponIndex.Value == 1) // GUN
        {
            if (currentAmmo.Value <= 0)
            {
                if (activeStats.emptySound != null) _audioSource.PlayOneShot(activeStats.emptySound);
                return;
            }
            
            currentAmmo.Value--;
            FireGunServerRpc();
        }
        else // AXE
        {
            SwingAxeServerRpc();
        }

        _attackTimer = activeStats.fireRate;
        
        if (_networkAnimator != null)
        {
            _networkAnimator.SetTrigger(_attackHash);
        }
    }

    [ServerRpc]
    private void FireGunServerRpc()
    {
        // 1. Raycast Hit Detection
        Ray ray = new Ray(shootPoint.position, shootPoint.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, gunStats.range, gunStats.targetLayer))
        {
            if (hit.collider.TryGetComponent<TargetHealth>(out TargetHealth health))
            {
                health.TakeDamage(gunStats.damage, false); // False = Physical
            }
            
            // Spawn Impact at hit.point
            SpawnImpactClientRpc(hit.point, hit.normal);
        }

        // 2. Muzzle Flash for everyone
        SpawnMuzzleFlashClientRpc();
    }

    [ServerRpc]
    private void SwingAxeServerRpc()
    {
        // Melee check in front
        Vector3 checkPos = transform.position + transform.forward * axeStats.range + Vector3.up;
        Collider[] hits = Physics.OverlapSphere(checkPos, axeStats.meleeRadius, axeStats.targetLayer);

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<TargetHealth>(out TargetHealth health))
            {
                health.TakeDamage(axeStats.damage, false);
            }
        }
        
        SpawnMuzzleFlashClientRpc();
    }

    [ClientRpc]
    private void SpawnMuzzleFlashClientRpc()
    {
        WeaponStats activeStats = (currentWeaponIndex.Value == 0) ? axeStats : gunStats;
        
        if (activeStats.fireSound != null) 
        {
            _audioSource.PlayOneShot(activeStats.fireSound);
        }

        // --- OPTIMIZED: Using EffectPool instead of Instantiate ---
        if (EffectPool.Instance != null && shootPoint != null)
        {
            string effectKey = (currentWeaponIndex.Value == 0) ? "AxeSwing" : "GunFlash";
            EffectPool.Instance.Get(effectKey, shootPoint.position, shootPoint.rotation);
        }
        else if (activeStats.muzzleFlashPrefab != null && shootPoint != null)
        {
            Instantiate(activeStats.muzzleFlashPrefab, shootPoint.position, shootPoint.rotation);
        }
    }

    [ClientRpc]
    private void SpawnImpactClientRpc(Vector3 pos, Vector3 normal)
    {
        if (EffectPool.Instance != null)
        {
            EffectPool.Instance.Get("GunImpact", pos, Quaternion.LookRotation(normal));
        }
        else if (gunStats.impactVFX != null)
        {
            Instantiate(gunStats.impactVFX, pos, Quaternion.LookRotation(normal));
        }
    }

    private IEnumerator ReloadRoutine()
    {
        _isReloading = true;
        if (_animator != null) _animator.SetTrigger(_reloadHash);
        if (gunStats.reloadSound != null) _audioSource.PlayOneShot(gunStats.reloadSound);
        
        yield return new WaitForSeconds(gunStats.reloadTime);
        
        currentAmmo.Value = gunStats.maxAmmo;
        _isReloading = false;
    }
}
