using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dedicated Berserker minion AI.
/// Extends MonsterAI following the Open/Closed Principle (OCP).
/// Features:
/// - Signature Mutant Roar sequence with 3D audio sync.
/// - Relentless standing pursuit (never crawls; always standing sprint).
/// - Heavy damage strikes (65 base damage claw swipe).
/// - Full synchronization with BerserkerAnim states:
///   * Mutant Roaring
///   * Mutant Run
///   * Mutant Walk
///   * Berserker Idle
///   * Mutant Swiping
///   * Mutant Reaction Hit
/// </summary>
public class BerserkerAI : MonsterAI
{
    [Header("Berserker Roar Settings")]
    [Tooltip("If true, plays a double roar animation sequence to match two-roar audio.")]
    public bool doubleRoarSequence = false;

    [Tooltip("Optional custom audio clip for the second roar.")]
    public AudioClip secondRoarClip;

    [Tooltip("Delay in seconds before roar audio starts, matching animation wind-up apex.")]
    public float roarAudioDelay = 0.35f;

    [Tooltip("Delay in seconds before triggering the second roar.")]
    public float secondRoarDelay = 2.6f;

    [Tooltip("Total duration of the roar sequence.")]
    public float totalRoarDuration = 5.2f;

    // Animator State Hashes
    protected readonly int _stateMutantRoar    = Animator.StringToHash("Mutant Roaring");
    protected readonly int _stateMutantRun     = Animator.StringToHash("Mutant Run");
    protected readonly int _stateMutantWalk    = Animator.StringToHash("Mutant Walk");
    protected readonly int _stateMutantSwiping = Animator.StringToHash("Mutant Swiping");
    protected readonly int _stateMutantHit     = Animator.StringToHash("Mutant Reaction Hit");
    protected readonly int _stateBerserkerIdle = Animator.StringToHash("Berserker Idle");

    protected override void Awake()
    {
        monsterType = MonsterType.Berserker;
        currentPosture = ZombiePosture.Standing;
        base.Awake();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            netPosture.Value = ZombiePosture.Standing;
        }
    }

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Berserker;
        currentPosture = ZombiePosture.Standing;
        screamDuration = totalRoarDuration;

        if (locomotion != null)
        {
            locomotion.chaseSpeed = 4.5f;
            locomotion.walkSpeed = 1.8f;
            locomotion.stoppingDistance = 1.8f;
        }

        if (combat != null)
        {
            combat.attackDamage = 65f;
            combat.attackRange = 2.2f;
            combat.attackCooldown = 1.8f;
        }

        if (stats != null)
        {
            if (stats.runSpeed > 0 && locomotion != null) locomotion.chaseSpeed = stats.runSpeed;
            if (stats.walkSpeed > 0 && locomotion != null) locomotion.walkSpeed = stats.walkSpeed;
            if (stats.damageAmount > 0 && combat != null) combat.attackDamage = stats.damageAmount;
            if (stats.attackRange > 0 && combat != null) combat.attackRange = stats.attackRange;
            if (stats.attackCooldown > 0 && combat != null) combat.attackCooldown = stats.attackCooldown;
        }
    }

    protected override IEnumerator SpawnScreamRoutine()
    {
        _hasScreamed = true;
        currentState = AIState.SpawningScream;
        if (locomotion != null) locomotion.Stop();

        target = perception.FindBestTarget(null);
        if (target != null && locomotion != null)
        {
            locomotion.RotateTowards(target.position, angularSpeed * 2f);
        }

        // 1. Trigger First Roar
        TriggerScreamAnimation();

        float delay = Mathf.Clamp(roarAudioDelay, 0.1f, 1.0f);
        yield return new WaitForSeconds(delay);

        Play3DScream();

        if (doubleRoarSequence)
        {
            float waitBeforeSecond = Mathf.Max(0.5f, secondRoarDelay - delay);
            yield return new WaitForSeconds(waitBeforeSecond);

            TriggerSecondRoar();
            yield return new WaitForSeconds(delay);
            PlaySecondRoarAudio();

            float remaining = Mathf.Max(0.5f, totalRoarDuration - secondRoarDelay - delay);
            yield return new WaitForSeconds(remaining);
        }
        else
        {
            float clipLength = (spawnScreamClip != null && spawnScreamClip.length > 0f) ? spawnScreamClip.length : screamDuration;
            yield return new WaitForSeconds(clipLength);
        }

        SwitchState(target != null ? AIState.Running : AIState.Patrol);
        if (locomotion != null) locomotion.Resume(target != null ? runSpeed : walkSpeed);
    }

    protected override void TriggerScreamAnimation()
    {
        if (_animator != null)
        {
            _animator.SetTrigger(_roarHash);
            _animator.CrossFade(_stateMutantRoar, 0.15f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerRoarClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerRoarClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_roarHash);
            _animator.CrossFade(_stateMutantRoar, 0.15f);
        }
    }

    private void TriggerSecondRoar()
    {
        if (_animator != null)
        {
            _animator.SetTrigger(_roarHash);
            _animator.CrossFade(_stateMutantRoar, 0.12f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerSecondRoarClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerSecondRoarClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_roarHash);
            _animator.CrossFade(_stateMutantRoar, 0.12f);
        }
    }

    private void PlaySecondRoarAudio()
    {
        AudioClip clip = secondRoarClip != null ? secondRoarClip : spawnScreamClip;
        if (clip != null && audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySecondRoarAudioClientRpc();
        }
    }

    [ClientRpc]
    private void PlaySecondRoarAudioClientRpc()
    {
        if (IsServer) return;
        AudioClip clip = secondRoarClip != null ? secondRoarClip : spawnScreamClip;
        if (clip != null && audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }
    }

    protected override void ExecuteAttack(Transform attackTarget)
    {
        if (combat == null || combat.IsOnCooldown) return;

        currentState = AIState.Attacking;
        if (locomotion != null)
        {
            locomotion.Stop();
            locomotion.RotateTowards(attackTarget.position, angularSpeed * 2f);
        }

        if (_animator != null)
        {
            _animator.CrossFade(_stateMutantSwiping, 0.12f);
        }

        combat.StartAttack("Attack", () =>
        {
            if (currentState != AIState.Dead)
            {
                SwitchState(target != null ? AIState.Running : AIState.Patrol);
                if (locomotion != null) locomotion.Resume(target != null ? runSpeed : walkSpeed);
            }
            _attackCoroutine = null;
        });

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayBerserkerAttackClientRpc();
        }
    }

    [ClientRpc]
    private void PlayBerserkerAttackClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.CrossFade(_stateMutantSwiping, 0.12f);
        }
    }

    public override void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;

        _hitStaggerTimer = 0.45f;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateMutantHit, 0.08f);
        }

        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayBerserkerHitClientRpc();
        }
    }

    [ClientRpc]
    private void PlayBerserkerHitClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateMutantHit, 0.08f);
        }
    }
}
