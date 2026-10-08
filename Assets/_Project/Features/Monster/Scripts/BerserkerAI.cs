using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// Dedicated AI controller for the subterranean Berserker beast.
/// Features:
/// - Explicit recognition and synchronization for ALL Berserker Animator states:
///   * "Mutant Roaring" (Spawn & roar trigger)
///   * "Mutant Run" (High-speed pursuit & sprint)
///   * "Mutant Walk" (Guarding patrol & trot)
///   * "Berserker Idle" (Stationary combat stance)
///   * "Mutant Swiping" (Devastating claw strike)
///   * "Mutant Reaction Hit" (Hit stagger)
///   * "Turn Left" and "Turn Right" (AI steering animations)
/// - Relentless standing pursuit: NEVER crawls. Keeps running standing across all distances,
///   even when close to the target or master.
/// - Heavy combat stats: devastating 65 damage claw strikes with extended range.
/// - Aggressive guard perimeter defending the Vengeful Spirit.
/// </summary>
public class BerserkerAI : MonsterAI
{
    // Direct Animator State Hashes matching BerserkerAnim.controller
    protected readonly int _stateMutantRoar    = Animator.StringToHash("Mutant Roaring");
    protected readonly int _stateMutantRun     = Animator.StringToHash("Mutant Run");
    protected readonly int _stateMutantWalk    = Animator.StringToHash("Mutant Walk");
    protected readonly int _stateBerserkerIdle = Animator.StringToHash("Berserker Idle");
    protected readonly int _stateMutantSwiping = Animator.StringToHash("Mutant Swiping");
    protected readonly int _stateMutantHit     = Animator.StringToHash("Mutant Reaction Hit");

    protected override void Awake()
    {
        monsterType = MonsterType.Berserker;
        currentPosture = ZombiePosture.Standing;
        base.Awake();

        // Enforce standing posture for Berserker: crawling is strictly excluded
        currentPosture = ZombiePosture.Standing;
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            netPosture.Value = ZombiePosture.Standing;
        }
    }

    [Header("Roar Synchronization (Double Roar)")]
    [Tooltip("If true, the Berserker plays the roar animation twice to rhyme with the double-roar audio clip. If false, cuts the audio after the first roar.")]
    public bool doubleRoarSequence = true;
    [Tooltip("Optional custom audio clip for the second roar. If left empty, re-plays spawnScreamClip.")]
    public AudioClip secondRoarClip;
    [Tooltip("Delay in seconds before roar audio starts, allowing the animation wind-up to reach the open-mouth roar apex.")]
    public float roarAudioDelay = 0.35f;
    [Tooltip("Time in seconds before triggering the second roar animation to rhyme with the second roar in the audio.")]
    public float secondRoarDelay = 2.6f;
    [Tooltip("Total duration of the entire double-roar spawn sequence before running.")]
    public float totalRoarDuration = 5.4f;

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Berserker;
        runSpeed = 3.4f;
        standRunSpeed = 3.4f;
        walkSpeed = 1.4f;
        runAcceleration = 8.5f;
        if (attackDamage < 50f) attackDamage = 65f;
        if (attackRange < 2.0f) attackRange = 2.2f;

        if (doubleRoarSequence)
        {
            screamDuration = totalRoarDuration;
        }
        else if (screamDuration <= 0f)
        {
            screamDuration = 2.6f;
        }

        // ScriptableObject stats override if present
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

    // =========================================================================
    //  Animation Drivers (Recognizes all States & Parameters in BerserkerAnim)
    // =========================================================================

    public void PlayBerserkerRunLocomotion(float speed)
    {
        SafeSetFloat(_speedHash, speed);
        SafeSetBool(_isRunningHash, true);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateMutantRun, "Mutant Run", 0.2f);
    }

    public void PlayBerserkerWalkLocomotion(float speed)
    {
        SafeSetFloat(_speedHash, speed);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, true);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateMutantWalk, "Mutant Walk", 0.2f);
    }

    public void PlayBerserkerIdleLocomotion()
    {
        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateBerserkerIdle, "Berserker Idle", 0.25f);
    }

    protected override void PlayPatrolWalkLocomotion(float speed)
    {
        PlayBerserkerWalkLocomotion(speed);
    }

    protected override void PlayPatrolIdleLocomotion()
    {
        PlayBerserkerIdleLocomotion();
    }

    protected override IEnumerator SpawnScreamRoutine()
    {
        _hasScreamed = true;
        currentState = AIState.SpawningScream;

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
        }

        // Find initial closest player to look at while roaring
        target = FindBestTarget();
        if (target != null)
        {
            RotateTowardsTarget(target);
        }

        // 1. Trigger first roar animation FIRST
        TriggerScreamAnimation();

        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);

        // 2. Wait for first roar animation wind-up to reach the open-mouth apex
        float delay = Mathf.Clamp(roarAudioDelay, 0.1f, 1.0f);
        yield return new WaitForSeconds(delay);

        // 3. Play first 3D roar audio precisely at the apex
        Play3DScream();

        if (doubleRoarSequence)
        {
            // Wait for first roar to finish before second roar
            float waitBeforeSecond = Mathf.Max(0.5f, secondRoarDelay - delay);
            yield return new WaitForSeconds(waitBeforeSecond);

            // Re-aim at target if moved
            target = FindBestTarget();
            if (target != null)
            {
                RotateTowardsTarget(target);
            }

            // 4. Trigger second roar animation
            TriggerSecondRoar();

            // 5. Wait for second roar animation wind-up to reach the open-mouth apex
            yield return new WaitForSeconds(delay);

            // 6. Play second roar audio precisely at the second roar animation apex!
            PlaySecondRoarAudio();

            float remaining = Mathf.Max(0.5f, totalRoarDuration - secondRoarDelay - delay);
            yield return new WaitForSeconds(remaining);
        }
        else
        {
            // Single roar mode: wait for first roar to finish, then cleanly cut audio
            float singleDuration = Mathf.Max(0.5f, (secondRoarDelay > 0f ? secondRoarDelay : screamDuration) - delay);
            yield return new WaitForSeconds(singleDuration);

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        // Transition directly to relentless pursuit
        currentState = AIState.Running;
        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = false;
        }
    }

    protected override void TriggerScreamAnimation()
    {
        SafeSetTrigger(_roarHash);
        SafeCrossFade(_stateMutantRoar, "Mutant Roaring", 0.15f, true);
    }

    private void TriggerSecondRoar()
    {
        SafeSetTrigger(_roarHash);
        SafeCrossFade(_stateMutantRoar, "Mutant Roaring", 0.12f, true);

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySecondRoarClientRpc();
        }
    }

    [ClientRpc]
    private void PlaySecondRoarClientRpc()
    {
        if (IsServer) return;
        SafeSetTrigger(_roarHash);
        SafeCrossFade(_stateMutantRoar, "Mutant Roaring", 0.12f, true);
    }

    protected virtual void PlaySecondRoarAudio()
    {
        AudioClip clip = secondRoarClip != null ? secondRoarClip : spawnScreamClip;
        if (clip != null && audioSource != null)
        {
            audioSource.clip = clip;
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
        if (audioSource != null && clip != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    public override void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;
        if (_hitCooldownTimer > 0f) return;

        _hitCooldownTimer = 0.25f;
        _hitStaggerTimer = hitReactionDuration;

        SafeSetTrigger(_hitReactionHash);
        SafeCrossFade(_stateMutantHit, "Mutant Reaction Hit", 0.08f, true);

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayBerserkerHitReactionClientRpc();
        }
    }

    [ClientRpc]
    private void PlayBerserkerHitReactionClientRpc()
    {
        if (IsServer) return;
        SafeSetTrigger(_hitReactionHash);
        SafeCrossFade(_stateMutantHit, "Mutant Reaction Hit", 0.08f, true);
    }

    // =========================================================================
    //  Berserker Behavior & Pursuit (Pure Standing — No Crawl)
    // =========================================================================

    /// <summary>
    /// Berserker Hunting Pursuit:
    /// Charges straight at investigator targets at maximum running speed.
    /// Pure standing sprint — no crawling transitions under any circumstances.
    /// </summary>
    protected override void HandleHuntingBehavior()
    {
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.4f;
            target = EvaluateBestTarget(target);
        }

        if (target != null && !IsTargetInvalidOrDead(target))
        {
            float distance = Vector3.Distance(transform.position, target.position);

            if (distance <= attackRange)
            {
                _agent.isStopped = true;
                PlayBerserkerIdleLocomotion();
                RotateTowardsTarget(target);

                if (_attackTimer <= 0f)
                {
                    StartCoroutine(PerformAttackRoutine());
                }
            }
            else
            {
                _agent.isStopped = false;
                _agent.speed = runSpeed;
                _agent.SetDestination(target.position);
                PlayBerserkerRunLocomotion(runSpeed);
            }
            return;
        }

        // When no living investigators are within range, execute subterranean roaming
        target = null;
        ExecuteRoam(walkSpeed);
    }

    /// <summary>
    /// Berserker Guarding Behavior:
    /// Maintains an aggressive combat perimeter around the Girl in an alert standing stance.
    /// Immediately intercepts approaching investigators and runs alongside her without crawling.
    /// </summary>
    protected override void HandleGuardingBehavior()
    {
        Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
        if (leader == null)
        {
            ExecuteRoam(walkSpeed);
            return;
        }

        float angleOffset = (GetInstanceID() % 6) * 60f * Mathf.Deg2Rad;
        Vector3 guardOffset = new Vector3(Mathf.Sin(angleOffset), 0f, Mathf.Cos(angleOffset)) * 2.4f;
        Vector3 targetSpot = leader.position + guardOffset;

        float distToGirl = Vector3.Distance(transform.position, leader.position);
        float distToSpot = Vector3.Distance(transform.position, targetSpot);

        // 1. Defend against approaching investigators
        Transform threat = FindBestTarget();
        if (threat != null)
        {
            float distToThreat = Vector3.Distance(transform.position, threat.position);
            float girlToThreat = Vector3.Distance(leader.position, threat.position);

            if (distToThreat <= attackRange)
            {
                _agent.isStopped = true;
                PlayBerserkerIdleLocomotion();
                RotateTowardsTarget(threat);
                if (_attackTimer <= 0f)
                {
                    StartCoroutine(PerformAttackRoutine());
                }
                return;
            }
            else if (girlToThreat <= 14f || distToThreat <= 12f)
            {
                _agent.isStopped = false;
                _agent.speed = runSpeed;
                _agent.SetDestination(threat.position);
                PlayBerserkerRunLocomotion(runSpeed);
                return;
            }
        }

        // 2. Follow / Guard the Girl in upright posture
        if (distToSpot <= 1.2f || (distToGirl <= 2.2f && _agent.velocity.magnitude < 0.2f))
        {
            _agent.isStopped = true;
            PlayBerserkerIdleLocomotion();

            Vector3 lookDir = leader.forward;
            lookDir.y = 0f;
            if (lookDir != Vector3.zero)
            {
                float angle = Vector3.Angle(transform.forward, lookDir);
                if (angle > turnAngleThreshold)
                {
                    UpdateTurningAnimation(lookDir);
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 120f * Time.deltaTime);
                }
                else
                {
                    if (_turnActiveTimer <= 0f) StopTurningAnimation();
                }
            }
            else
            {
                if (_turnActiveTimer <= 0f) StopTurningAnimation();
            }
        }
        else
        {
            _agent.isStopped = false;
            if (distToGirl > 5.5f)
            {
                _agent.speed = runSpeed;
                PlayBerserkerRunLocomotion(runSpeed);
            }
            else
            {
                _agent.speed = walkSpeed;
                PlayBerserkerWalkLocomotion(walkSpeed);
            }

            _agent.SetDestination(targetSpot);
        }
    }

    protected override void ExecuteRoam(float patrolSpeed)
    {
        if (_isRoamWaiting)
        {
            _agent.isStopped = true;
            PlayBerserkerIdleLocomotion();

            _roamWaitTimer -= Time.deltaTime;
            if (_roamWaitTimer <= 0f)
            {
                _isRoamWaiting = false;
                PickNewRoamDestination();
            }
            return;
        }

        _roamTimer -= Time.deltaTime;
        bool arrived = !_agent.pathPending && (_agent.remainingDistance <= 1.2f || (_agent.remainingDistance == 0f && !_agent.hasPath));
        if (arrived || _roamTimer <= 0f)
        {
            _isRoamWaiting = true;
            _roamWaitTimer = Random.Range(2.0f, 5.0f);
            PlayBerserkerIdleLocomotion();
            return;
        }

        _agent.isStopped = false;
        _agent.speed = patrolSpeed;
        PlayBerserkerWalkLocomotion(patrolSpeed);
        if (!_agent.hasPath && _roamDestination != Vector3.zero)
        {
            _agent.SetDestination(_roamDestination);
        }
    }
}
