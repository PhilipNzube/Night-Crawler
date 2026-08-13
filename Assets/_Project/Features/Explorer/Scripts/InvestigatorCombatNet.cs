using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// SOLID — SRP: Manages combat, weapon switching (Melee/Ranged), ammo, and network sync
/// for ANY Investigator character (Explorer, Mine Worker, Hazard Specialist, Cursed Priest, etc.).
///
/// Follows SOLID principles:
///   • SRP: Manages investigator player weapon combat and network synchronization.
///   • OCP: Weapon stats are ScriptableObjects (WeaponStats) — add new weapons without changing logic.
///   • DIP: Interacts with health via IDamageReceiver interface.
/// </summary>
public class InvestigatorCombatNet : NetworkBehaviour
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
        
        _audioSource.playOnAwake = false;
        _audioSource.minDistance = 5f;
        _audioSource.maxDistance = 65f;
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.volume = 1f;
    }

    public override void OnNetworkSpawn()
    {
        _audioSource.spatialBlend = IsOwner ? 0.2f : float.MaxValue;

        if (IsOwner)
        {
            SwitchWeapon(0); // Start with Axe
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // Weapon switching (1 = Axe, 2 = Gun)
        if (Keyboard.current.digit1Key.wasPressedThisFrame) SwitchWeapon(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SwitchWeapon(1);

        // Attack (Left Click)
        if (Mouse.current.leftButton.wasPressedThisFrame && _attackTimer <= 0 && !_isReloading)
        {
            PerformAttack();
        }

        // Reload (R - Gun only)
        if (Keyboard.current.rKey.wasPressedThisFrame && currentWeaponIndex.Value == 1 && !_isReloading)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    public void SwitchWeapon(int index)
    {
        currentWeaponIndex.Value = index;
        
        if (axeVisual != null) axeVisual.SetActive(index == 0);
        if (gunVisual != null) gunVisual.SetActive(index == 1);

        if (_animator != null)
        {
            _animator.SetInteger(_weaponIdHash, index);
            if (_networkAnimator != null)
                _networkAnimator.SetTrigger(_switchWeaponHash);
        }

        if (index == 1 && gunStats != null)
        {
            currentAmmo.Value = gunStats.maxAmmo;
        }
    }

    private void PerformAttack()
    {
        WeaponStats activeStats = currentWeaponIndex.Value == 0 ? axeStats : gunStats;
        if (activeStats == null) return;

        if (currentWeaponIndex.Value == 1 && currentAmmo.Value <= 0)
        {
            if (gunStats != null && gunStats.emptySound != null)
                _audioSource.PlayOneShot(gunStats.emptySound);
            return;
        }

        _attackTimer = activeStats.fireRate;

        if (_networkAnimator != null)
            _networkAnimator.SetTrigger(_attackHash);

        if (activeStats.fireSound != null)
            _audioSource.PlayOneShot(activeStats.fireSound);

        if (currentWeaponIndex.Value == 0)
        {
            PerformMeleeHit(activeStats);
        }
        else
        {
            PerformRangedShot(activeStats);
        }
    }

    private void PerformMeleeHit(WeaponStats stats)
    {
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Collider[] hits = Physics.OverlapSphere(origin + transform.forward * stats.range, stats.meleeRadius);

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            if (hit.TryGetComponent<IDamageReceiver>(out var receiver))
            {
                receiver.TakeDamage(stats.damage);
            }
        }
    }

    private void PerformRangedShot(WeaponStats stats)
    {
        currentAmmo.Value--;

        Vector3 rayOrigin = shootPoint != null ? shootPoint.position : transform.position + Vector3.up * 1.5f;
        Vector3 rayDir = transform.forward;

        if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, stats.range))
        {
            if (hit.collider.TryGetComponent<IDamageReceiver>(out var receiver))
            {
                receiver.TakeDamage(stats.damage);
            }
        }
    }

    private IEnumerator ReloadRoutine()
    {
        if (gunStats == null) yield break;

        _isReloading = true;

        if (_networkAnimator != null)
            _networkAnimator.SetTrigger(_reloadHash);

        if (gunStats.reloadSound != null)
            _audioSource.PlayOneShot(gunStats.reloadSound);

        yield return new WaitForSeconds(gunStats.reloadTime);

        currentAmmo.Value = gunStats.maxAmmo;
        _isReloading = false;
    }
}
