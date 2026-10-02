using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// Dedicated AI controller for the subterranean Zombie minion.
/// Features:
/// - 4-tiered dynamic locomotion: Crawling sprint when far, standing sprint when close,
///   walking when moving with the Girl, and wandering/idling guard routines when close.
/// - Terrifying screech spawn scream.
/// - Seamless stand-up and crawl transitions with network synchronization.
/// - Dynamic turning animations (Turn Right clip with automated Animator Mirroring for Turn Left).
/// - Vicious bite and claw melee attack hitbox.
/// </summary>
public class ZombieAI : MonsterAI
{
    protected override void Awake()
    {
        monsterType = MonsterType.Zombie;
        base.Awake();
    }

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Zombie;
        crawlSpeed = 2.8f;
        standRunSpeed = 2.6f;
        walkSpeed = 0.95f;
        runSpeed = crawlSpeed;
        runAcceleration = 8.0f;
        if (attackDamage < 25f) attackDamage = 35f;
        if (attackRange <= 0f) attackRange = 2.0f;
        if (screamDuration <= 0f) screamDuration = 2.2f;

        // Apply ScriptableObject stats override if present
        if (stats != null)
        {
            if (stats.runSpeed > 0)
            {
                runSpeed = stats.runSpeed;
                standRunSpeed = stats.runSpeed;
            }
            else if (stats.walkSpeed > 0)
            {
                walkSpeed = stats.walkSpeed;
            }

            if (stats.damageAmount > 0) attackDamage = stats.damageAmount;
            if (stats.attackRange > 0) attackRange = stats.attackRange;
            if (stats.attackCooldown > 0) attackCooldown = stats.attackCooldown;
        }
    }

    protected override void TriggerScreamAnimation()
    {
        SafeSetTrigger(_screamHash);
    }
}
