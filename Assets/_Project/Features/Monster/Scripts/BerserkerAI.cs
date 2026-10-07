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

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Berserker;
        runSpeed = 3.4f;
        standRunSpeed = 3.4f;
        walkSpeed = 1.4f;
        runAcceleration = 8.5f;
        if (attackDamage < 50f) attackDamage = 65f;
        if (attackRange < 2.0f) attackRange = 2.2f;
        if (screamDuration <= 0f) screamDuration = 2.6f;

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

    protected override void TriggerScreamAnimation()
    {
        SafeSetTrigger(_roarHash);
        SafeCrossFade(_stateMutantRoar, "Mutant Roaring", 0.15f, true);
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
