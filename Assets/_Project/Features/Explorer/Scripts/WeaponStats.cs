using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponStats", menuName = "Stats/WeaponStats")]
public class WeaponStats : ScriptableObject
{
    [Header("General")]
    public string weaponName = "New Weapon";
    public float damage = 20f;
    public float fireRate = 0.5f; // Time between attacks
    public LayerMask targetLayer;

    [Header("Ranged Settings")]
    public bool isRanged = true;
    public float range = 50f;
    public int maxAmmo = 12;
    public float reloadTime = 2f;
    
    [Header("Melee Settings")]
    public float meleeRadius = 2.5f;

    [Header("VFX & SFX")]
    public GameObject muzzleFlashPrefab;
    public GameObject impactVFX;
    public AudioClip fireSound;
    public AudioClip reloadSound;
    public AudioClip emptySound;
}
