using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dedicated Zombie minion AI.
/// Extends MonsterAI following the Open/Closed Principle (OCP).
/// Features:
/// - 4-tiered dynamic locomotion: Crawling sprint when far, standing sprint when close,
///   walking when patrolling/guarding, and stationary idling.
/// - Terrifying screech spawn scream.
/// - Seamless stand-up and crawl transitions with network synchronization.
/// - Vicious bite and claw melee attack hitbox (Zombie Neck).
/// - Full synchronization with Zombie Animator states:
///   * Zombie Scream
///   * Running Crawl
///   * Zombie Run
///   * Zombie Walk
///   * Zombie Idle
///   * Zombie Stand Up
///   * Zombie Neck
///   * Zombie Reaction Hit
/// </summary>
public class ZombieAI : MonsterAI
{
    protected readonly int _stateZombieScream  = Animator.StringToHash("Zombie Scream");
    protected readonly int _stateRunningCrawl  = Animator.StringToHash("Running Crawl");
    protected readonly int _stateZombieRun     = Animator.StringToHash("Zombie Run");
    protected readonly int _stateZombieWalk    = Animator.StringToHash("Zombie Walk");
    protected readonly int _stateZombieIdle    = Animator.StringToHash("Zombie Idle");
    protected readonly int _stateZombieStandUp = Animator.StringToHash("Zombie Stand Up");
    protected readonly int _stateZombieNeck    = Animator.StringToHash("Zombie Neck");
    protected readonly int _stateZombieHit     = Animator.StringToHash("Zombie Reaction Hit");

    private Coroutine _standUpRoutine;

    protected override void Awake()
    {
        monsterType = MonsterType.Zombie;
        base.Awake();
    }

    [Header("Zombie Posture Transitions")]
    public float crawlSpeed = 3.8f;
    public float standRunSpeed = 3.0f;
    public float standTransitionDistance = 8.0f;
    public float crawlTransitionDistance = 15.0f;
    public float standUpDuration = 1.25f;

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Zombie;
        screamDuration = 2.4f;
        screamAudioDelay = 0.25f;

        if (locomotion != null)
        {
            locomotion.chaseSpeed = crawlSpeed;
            locomotion.walkSpeed = 0.55f;
            locomotion.stoppingDistance = 1.5f;
        }

        if (combat != null)
        {
            combat.attackDamage = 35f;
            combat.attackRange = 2.0f;
            combat.attackCooldown = 1.6f;
        }

        if (stats != null)
        {
            if (stats.runSpeed > 0)
            {
                crawlSpeed = stats.runSpeed;
                standRunSpeed = stats.runSpeed;
                if (locomotion != null) locomotion.chaseSpeed = stats.runSpeed;
            }
            if (stats.walkSpeed > 0 && locomotion != null) locomotion.walkSpeed = stats.walkSpeed;
            if (stats.damageAmount > 0 && combat != null) combat.attackDamage = stats.damageAmount;
            if (stats.attackRange > 0 && combat != null) combat.attackRange = stats.attackRange;
            if (stats.attackCooldown > 0 && combat != null) combat.attackCooldown = stats.attackCooldown;
        }
    }

    protected override void UpdateBrain()
    {
        // Don't update locomotion while standing up
        if (currentPosture == ZombiePosture.StandingUp)
        {
            if (locomotion != null) locomotion.Stop();
            return;
        }

        // Posture evaluation when chasing
        if (currentState == AIState.Running && target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            EvaluatePosture(dist);
        }

        base.UpdateBrain();
    }

    private void EvaluatePosture(float distanceToTarget)
    {
        if (!HasAuthority || _standUpRoutine != null) return;

        if (currentPosture == ZombiePosture.Crawling && distanceToTarget <= standTransitionDistance)
        {
            StartStandUp();
        }
        else if (currentPosture == ZombiePosture.Standing && distanceToTarget > crawlTransitionDistance)
        {
            DropToCrawl();
        }
    }

    private void StartStandUp()
    {
        if (_standUpRoutine != null) return;
        _standUpRoutine = StartCoroutine(StandUpRoutine());
    }

    private IEnumerator StandUpRoutine()
    {
        currentPosture = ZombiePosture.StandingUp;
        if (IsServer) netPosture.Value = ZombiePosture.StandingUp;

        if (locomotion != null) locomotion.Stop();

        if (_animator != null)
        {
            _animator.SetTrigger(_standUpHash);
            _animator.CrossFade(_stateZombieStandUp, 0.12f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerStandUpClientRpc();
        }

        yield return new WaitForSeconds(standUpDuration);

        currentPosture = ZombiePosture.Standing;
        if (IsServer) netPosture.Value = ZombiePosture.Standing;

        if (_animator != null)
        {
            _animator.SetBool(_isStandingHash, true);
            _animator.SetBool(_isCrawlingHash, false);
        }

        if (locomotion != null)
        {
            locomotion.chaseSpeed = standRunSpeed;
            locomotion.Resume(standRunSpeed);
        }

        _standUpRoutine = null;
    }

    [ClientRpc]
    private void TriggerStandUpClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_standUpHash);
            _animator.CrossFade(_stateZombieStandUp, 0.12f);
        }
    }

    private void DropToCrawl()
    {
        currentPosture = ZombiePosture.Crawling;
        if (IsServer) netPosture.Value = ZombiePosture.Crawling;

        if (_animator != null)
        {
            _animator.SetBool(_isStandingHash, false);
            _animator.SetBool(_isCrawlingHash, true);
            _animator.CrossFade(_stateRunningCrawl, 0.2f);
        }

        if (locomotion != null)
        {
            locomotion.chaseSpeed = crawlSpeed;
            locomotion.Resume(crawlSpeed);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerDropToCrawlClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerDropToCrawlClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetBool(_isStandingHash, false);
            _animator.SetBool(_isCrawlingHash, true);
            _animator.CrossFade(_stateRunningCrawl, 0.2f);
        }
    }

    protected override void TriggerScreamAnimation()
    {
        if (_animator != null)
        {
            _animator.SetTrigger(_screamHash);
            _animator.CrossFade(_stateZombieScream, 0.15f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            TriggerZombieScreamClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerZombieScreamClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_screamHash);
            _animator.CrossFade(_stateZombieScream, 0.15f);
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
            _animator.CrossFade(_stateZombieNeck, 0.12f);
        }

        combat.StartAttack("Attack", () =>
        {
            if (currentState != AIState.Dead)
            {
                currentState = AIState.Running;
                if (locomotion != null) locomotion.Resume(runSpeed);
            }
            _attackCoroutine = null;
        });

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayZombieAttackClientRpc();
        }
    }

    [ClientRpc]
    private void PlayZombieAttackClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.CrossFade(_stateZombieNeck, 0.12f);
        }
    }

    public override void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;

        _hitStaggerTimer = 0.40f;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateZombieHit, 0.08f);
        }

        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayZombieHitClientRpc();
        }
    }

    [ClientRpc]
    private void PlayZombieHitClientRpc()
    {
        if (IsServer) return;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateZombieHit, 0.08f);
        }
    }
}
