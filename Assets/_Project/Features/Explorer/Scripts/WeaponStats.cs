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
    [Range(0f, 1f)]
    public float fireSoundVolume = 0.5f; // Volume scale for attack/swing sound
    public AudioClip impactSound; // Played when an attack connects with a target (flesh cut, impact thud)
    [Range(0f, 1f)]
    public float impactSoundVolume = 0.8f; // Volume scale for hit impact sound
    public AudioClip reloadSound;
    public AudioClip emptySound;
}
