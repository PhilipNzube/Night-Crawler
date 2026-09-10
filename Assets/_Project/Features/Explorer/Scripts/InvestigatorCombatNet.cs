using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages combat, weapon switching (Melee/Ranged), ammo, and network sync
/// for ANY Investigator character (Explorer, Mine Worker, Hazard Specialist, Cursed Priest, etc.).
///
/// Cleanly verifies Animator parameters before setting triggers so missing controller parameters
/// never crash network spawning or freeze character animations.
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

    private readonly int _weaponIdHash     = Animator.StringToHash("WeaponID");
    private readonly int _switchWeaponHash = Animator.StringToHash("SwitchWeapon");
    private readonly int _attackHash       = Animator.StringToHash("Attack");
    private readonly int _reloadHash       = Animator.StringToHash("Reload");

    private float _attackTimer;
    private bool _isReloading;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private AudioSource _audioSource;
    private HashSet<int> _animatorParameterHashes = new HashSet<int>();

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

        CacheAnimatorParameters();
        AutoBindWeaponVisuals();
    }

    private void AutoBindWeaponVisuals()
    {
        if (axeVisual == null)
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t != null && t != transform && (t.name.ToLower().Contains("axe") || t.name.ToLower().Contains("pickaxe")))
                {
                    axeVisual = t.gameObject;
                    break;
                }
            }

            if (axeVisual == null && _animator != null && _animator.isHuman)
            {
                var rHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (rHand != null)
                {
                    var handChildren = rHand.GetComponentsInChildren<Transform>(true);
                    foreach (var c in handChildren)
                    {
                        if (c != rHand && c.GetComponent<Renderer>() != null)
                        {
                            axeVisual = c.gameObject;
                            break;
                        }
                    }

                    // If character has no axe model attached (e.g. Medic), instantiate default axe_Bloody
                    if (axeVisual == null)
                    {
                        var axePrefab = Resources.Load<GameObject>("axe_Bloody");
                        if (axePrefab != null)
                        {
                            axeVisual = Instantiate(axePrefab, rHand);
                            axeVisual.name = "axe_Bloody";
                            axeVisual.transform.localPosition = new Vector3(0.208f, -0.03f, 0.146f);
                            axeVisual.transform.localRotation = Quaternion.Euler(3.902f, -85f, 4.435f);
                            axeVisual.transform.localScale = Vector3.one;
                            Debug.Log($"[InvestigatorCombatNet] Instantiated default axe_Bloody for {gameObject.name}");
                        }
                    }
                }
            }
        }

        if (axeStats == null)
        {
            var stats = Resources.FindObjectsOfTypeAll<WeaponStats>();
            foreach (var s in stats)
            {
                if (s != null && (s.name.ToLower().Contains("axe") || s.name.ToLower().Contains("pick") || !s.isRanged))
                {
                    axeStats = s;
                    break;
                }
            }
            if (axeStats == null)
            {
                axeStats = ScriptableObject.CreateInstance<WeaponStats>();
                axeStats.weaponName = "Heavy Pickaxe";
                axeStats.isRanged = false;
                axeStats.damage = 35f;
                axeStats.fireRate = 0.8f;
                axeStats.meleeRadius = 2.5f;
            }
        }
    }

    [Header("Starting Weapon")]
    [Tooltip("If true, starts with melee weapon in hand. By default, only the Miner starts armed.")]
    public bool startArmed = false;

    [Header("Weapon Inventory")]
    [Tooltip("Tracks whether this investigator currently carries/unlocked a weapon.")]
    public bool hasUnlockedWeapon = false;

    public bool IsMiner => startArmed || gameObject.name.ToLower().Contains("miner") || gameObject.name.ToLower().Contains("worker");

    public bool HasWeapon => currentWeaponIndex.Value >= 0;

    public override void OnNetworkSpawn()
    {
        _audioSource.spatialBlend = IsOwner ? 0.2f : float.MaxValue;
        currentWeaponIndex.OnValueChanged += HandleWeaponIndexChanged;

        if (IsOwner)
        {
            if (IsMiner)
            {
                hasUnlockedWeapon = true;
                SwitchWeapon(0); // Miner always starts with pickaxe
            }
            else
            {
                hasUnlockedWeapon = false;
                SwitchWeapon(-1); // Other investigators start unarmed
            }
        }
        else
        {
            ApplyWeaponVisuals(currentWeaponIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        currentWeaponIndex.OnValueChanged -= HandleWeaponIndexChanged;
    }

    private void HandleWeaponIndexChanged(int prev, int current)
    {
        ApplyWeaponVisuals(current);
    }

    private void ApplyWeaponVisuals(int index)
    {
        if (axeVisual != null) axeVisual.SetActive(index == 0);
        if (gunVisual != null) gunVisual.SetActive(index == 1);
    }

    /// <summary>
    /// Grants and equips the melee axe weapon (e.g. from accepting a deal with the Girl or looting).
    /// Safe to invoke from server (routes via ClientRpc) or directly on the owner client.
    /// </summary>
    public void GrantMeleeWeapon()
    {
        hasUnlockedWeapon = true;
        if (IsServer && !IsOwner)
        {
            GrantMeleeWeaponClientRpc();
        }
        else if (IsOwner)
        {
            SwitchWeapon(0);
        }
    }

    [ClientRpc]
    public void GrantMeleeWeaponClientRpc()
    {
        hasUnlockedWeapon = true;
        if (IsOwner)
        {
            SwitchWeapon(0);
        }
    }

    /// <summary>
    /// Holsters or draws the melee weapon. The Miner is NOT allowed to hide their pickaxe.
    /// </summary>
    public void ToggleWeaponHide()
    {
        if (IsMiner) return; // Miner cannot hide weapons
        if (!hasUnlockedWeapon) return;

        if (currentWeaponIndex.Value >= 0)
        {
            SwitchWeapon(-1); // Holster / hide weapon
        }
        else
        {
            SwitchWeapon(0);  // Draw / unhide weapon
        }
    }

    void Update()
    {
        if (!IsOwner || PauseManager.IsGamePaused) return;

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // Weapon hide/holster toggle [X] — only non-miners can hide
        if (Keyboard.current != null && Keyboard.current.xKey != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            ToggleWeaponHide();
        }

        // Weapon draw/toggle [1]
        if (Keyboard.current != null && Keyboard.current.digit1Key != null && Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            if (currentWeaponIndex.Value == 0 && !IsMiner)
            {
                SwitchWeapon(-1); // Holster if already equipped
            }
            else if (hasUnlockedWeapon)
            {
                SwitchWeapon(0);  // Draw axe
            }
        }

        // Weapon draw/switch [2] (Gun)
        if (Keyboard.current != null && Keyboard.current.digit2Key != null && Keyboard.current.digit2Key.wasPressedThisFrame && HasWeapon)
        {
            SwitchWeapon(1);
        }

        // Attack (Left Click) — suppressed if clicking over UI or if weapon is hidden
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && _attackTimer <= 0 && !_isReloading)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            if (DealNotificationUI.Instance != null && DealNotificationUI.Instance.IsActive)
            {
                return;
            }

            if (HasWeapon && currentWeaponIndex.Value >= 0)
            {
                PerformAttack();
            }
        }

        // Reload (R - Gun only)
        if (Keyboard.current != null && Keyboard.current.rKey != null && Keyboard.current.rKey.wasPressedThisFrame && currentWeaponIndex.Value == 1 && !_isReloading)
        {
            StartCoroutine(ReloadRoutine());
        }
    }

    public void SwitchWeapon(int index)
    {
        currentWeaponIndex.Value = index;
        ApplyWeaponVisuals(index);

        if (index >= 0)
        {
            SafeSetInteger(_weaponIdHash, index);
            SafeSetTrigger(_switchWeaponHash);
        }
        else
        {
            SafeSetInteger(_weaponIdHash, -1);
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

        SafeSetTrigger(_attackHash);

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

        SafeSetTrigger(_reloadHash);

        if (gunStats.reloadSound != null)
            _audioSource.PlayOneShot(gunStats.reloadSound);

        yield return new WaitForSeconds(gunStats.reloadTime);

        currentAmmo.Value = gunStats.maxAmmo;
        _isReloading = false;
    }

    // =========================================================================
    //  Animator Safety Helpers
    // =========================================================================

    private void CacheAnimatorParameters()
    {
        _animatorParameterHashes.Clear();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        if (_animator != null && _animator.parameterCount > 0)
        {
            foreach (var param in _animator.parameters)
            {
                _animatorParameterHashes.Add(param.nameHash);
            }
        }
    }

    private void SafeSetInteger(int hash, int value)
    {
        if (_animator != null && _animatorParameterHashes.Contains(hash))
        {
            _animator.SetInteger(hash, value);
        }
    }

    private void SafeSetTrigger(int hash)
    {
        if (_animatorParameterHashes.Contains(hash))
        {
            if (_networkAnimator != null)
            {
                _networkAnimator.SetTrigger(hash);
            }
            else if (_animator != null)
            {
                _animator.SetTrigger(hash);
            }
        }
    }
}
