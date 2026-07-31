using UnityEngine;

[CreateAssetMenu(fileName = "NewEntityStats", menuName = "Stats/EntityStats")]
public class EntityStats : ScriptableObject
{
    [Header("General")]
    public float maxHealth = 100f;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 5f;
    public float lookSensitivity = 1.0f;
    public float cameraDistance = 4.0f; // Default distance for this monster
    public float rotationSmoothTime = 0.12f;

    [Header("Girl Abilities")]
    public float invisDuration = 5f;
    public float invisCooldown = 10f;
    public float stealthAlpha = 0.2f;
    public float fearIncreaseRate = 0.02f;
    public float invisFearMultiplier = 2.5f;
    public float possessionRange = 6f;
    public LayerMask possessionTargetLayer;

    [Header("Combat")]
    public float damageAmount = 20f;
    public LayerMask attackTargetLayer;
    public float attackCooldown = 1.5f;
    public float damageRadius = 2.5f;
    public float attackRange = 2.0f;
}