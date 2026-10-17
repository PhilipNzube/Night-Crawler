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
        berserkerContactDistance = 1.15f;
        attackAudioDelay = 0.20f;

        if (locomotion != null)
        {
            locomotion.chaseSpeed = 4.5f;
            locomotion.walkSpeed = 1.8f;
            locomotion.stoppingDistance = 1.10f;
            if (locomotion.agent != null) locomotion.agent.stoppingDistance = 1.10f;
        }

        if (combat != null)
        {
            combat.attackDamage = 65f;
            combat.attackRange = 1.35f;
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

        Transform cand = perception != null ? perception.FindBestTarget(null) : null;
        if (cand != null && !IsGirl(cand))
        {
            target = cand;
        }
        else
        {
            target = null;
        }

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

    [Header("Berserker Melee & Swipe Alignment")]
    [Tooltip("Intimate strike distance where the claw swipe reaches the investigator.")]
    public float berserkerContactDistance = 1.15f;
    private Coroutine _swipeAlignmentRoutine;

    protected override void ExecuteAttack(Transform attackTarget)
    {
        if (combat == null || combat.IsOnCooldown || attackTarget == null) return;

        // If distance opened up, keep running to close the gap before swinging
        float dist = Vector3.Distance(transform.position, attackTarget.position);
        if (dist > MaxAttackInitiationDistance)
        {
            SwitchState(AIState.Running);
            if (locomotion != null) locomotion.Resume(runSpeed);
            return;
        }

        currentState = AIState.Attacking;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netAIState.Value = AIState.Attacking;
        }
        if (locomotion != null)
        {
            locomotion.Stop();
        }

        // Align and step into intimate swipe distance (~1.15m) on the authoritative server
        AlignSwipeWithTarget(attackTarget);

        if (_animator != null)
        {
            _animator.CrossFade(_stateMutantSwiping, 0.12f);
        }

        // Synchronize claw swipe attack audio timing with animation wind-up apex
        StartSynchronizedAttackAudio(attackAudioDelay);

        if (_swipeAlignmentRoutine != null) StopCoroutine(_swipeAlignmentRoutine);
        _swipeAlignmentRoutine = StartCoroutine(SwipeAlignmentRoutine(attackTarget));

        combat.StartAttack("Attack", () =>
        {
            Stop3DAttackAudio();
            if (_swipeAlignmentRoutine != null)
            {
                StopCoroutine(_swipeAlignmentRoutine);
                _swipeAlignmentRoutine = null;
            }

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

    private IEnumerator SwipeAlignmentRoutine(Transform victim)
    {
        float timer = 0f;
        float attackDuration = combat != null ? combat.attackCooldown : 1.8f;

        while (timer < attackDuration && currentState == AIState.Attacking && _hitStaggerTimer <= 0f)
        {
            if (victim == null || !victim.gameObject.activeInHierarchy) yield break;

            AlignSwipeWithTarget(victim);

            timer += Time.deltaTime;
            yield return null;
        }

        _swipeAlignmentRoutine = null;
    }

    private void AlignSwipeWithTarget(Transform victim)
    {
        if (victim == null) return;

        Vector3 toVictim = victim.position - transform.position;
        toVictim.y = 0f;
        float currentDist = toVictim.magnitude;

        // 1. Rotate to face victim directly
        if (toVictim.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toVictim);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 24f);
        }

        // 2. Step into intimate strike range (~1.15m)
        if (currentDist > 0.01f && Mathf.Abs(currentDist - berserkerContactDistance) > 0.04f)
        {
            Vector3 desiredPos = victim.position - toVictim.normalized * berserkerContactDistance;
            if (UnityEngine.AI.NavMesh.SamplePosition(desiredPos, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas))
            {
                desiredPos = hit.position;
            }

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                Vector3 step = (desiredPos - transform.position);
                _agent.Move(step * (Time.deltaTime * 10f));
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * 10f);
            }
        }
    }

    [ClientRpc]
    private void PlayBerserkerAttackClientRpc()
    {
        if (IsServer) return;

        // On non-authority clients: DO NOT manipulate transform.position or transform.rotation!
        // NetworkTransform has 100% authoritative control over position and rotation,
        // preventing the clone from jittering or flipping around when attacking.
        if (_animator != null)
        {
            _animator.CrossFade(_stateMutantSwiping, 0.12f);
        }

        StartSynchronizedAttackAudio(attackAudioDelay);
    }

    public override void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;

        Stop3DAttackAudio();
        if (_swipeAlignmentRoutine != null)
        {
            StopCoroutine(_swipeAlignmentRoutine);
            _swipeAlignmentRoutine = null;
        }

        if (combat != null) combat.CancelAttack();
        if (locomotion != null) locomotion.Stop();

        _hitStaggerTimer = hitReactionStunDuration;
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

        Stop3DAttackAudio();
        if (_swipeAlignmentRoutine != null)
        {
            StopCoroutine(_swipeAlignmentRoutine);
            _swipeAlignmentRoutine = null;
        }

        if (locomotion != null) locomotion.Stop();
        _hitStaggerTimer = hitReactionStunDuration;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateMutantHit, 0.08f);
        }
    }
}
