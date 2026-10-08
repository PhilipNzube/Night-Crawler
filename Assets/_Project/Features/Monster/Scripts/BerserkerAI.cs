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

    [Header("Roar Synchronization")]
    [Tooltip("If true, the Berserker plays the roar animation twice to rhyme with the double-roar audio clip. If false, plays the single trimmed roar audio.")]
    public bool doubleRoarSequence = false;
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
        else
        {
            float clipLength = (spawnScreamClip != null && spawnScreamClip.length > 0f) ? spawnScreamClip.length : 3.09f;
            screamDuration = roarAudioDelay + clipLength;
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

    // =========================================================================
    //  Turning Animation Drivers (Recognizes Turn Left & Turn Right in BerserkerAnim)
    // =========================================================================

    public void PlayBerserkerTurnRight()
    {
        SafeSetTrigger(_turnRightHash);
        SafeCrossFade(_stateTurnRight, "Turn Right", 0.10f, true);
    }

    public void PlayBerserkerTurnLeft()
    {
        SafeSetTrigger(_turnLeftHash);
        SafeCrossFade(_stateTurnLeft, "Turn Left", 0.10f, true);
    }

    public override void UpdateTurningAnimation(Vector3 desiredFacingDir)
    {
        if (IsMovingLocomotion())
        {
            StopTurningAnimation(true);
            return;
        }

        desiredFacingDir.y = 0f;
        if (desiredFacingDir.sqrMagnitude < 0.01f)
        {
            StopTurningAnimation(true);
            return;
        }

        float signedAngle = Vector3.SignedAngle(transform.forward, desiredFacingDir.normalized, Vector3.up);
        SafeSetFloat(_turnAngleHash, signedAngle);

        if (signedAngle > turnAngleThreshold)
        {
            if (!_isTurningRight)
            {
                PlayBerserkerTurnRight();
            }
            _isTurningRight = true;
            _isTurningLeft = false;
            SafeSetBool(_isTurningRightHash, true);
            SafeSetBool(_isTurningLeftHash, false);
            _turnActiveTimer = Mathf.Max(_turnActiveTimer, minTurnDuration);
        }
        else if (signedAngle < -turnAngleThreshold)
        {
            if (!_isTurningLeft)
            {
                PlayBerserkerTurnLeft();
            }
            _isTurningLeft = true;
            _isTurningRight = false;
            SafeSetBool(_isTurningLeftHash, true);
            SafeSetBool(_isTurningRightHash, false);
            _turnActiveTimer = Mathf.Max(_turnActiveTimer, minTurnDuration);
        }
        else if (_turnActiveTimer <= 0f && Mathf.Abs(signedAngle) <= 15f)
        {
            StopTurningAnimation();
        }
    }

    public override void StopTurningAnimation(bool force = false)
    {
        if (!force && _turnActiveTimer > 0f) return;

        base.StopTurningAnimation(force);

        if (!IsMovingLocomotion() && currentState != AIState.Dead && currentState != AIState.Attacking)
        {
            PlayBerserkerIdleLocomotion();
        }
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
            // Single roar mode: let trimmed audio and animation play to completion naturally
            float clipLength = (spawnScreamClip != null && spawnScreamClip.length > 0f) ? spawnScreamClip.length : 3.09f;
            yield return new WaitForSeconds(clipLength);
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
    /// - Command.Roam: On its own, only engages if an investigator comes close (<=12m), and breaks off chase if they get too far (>16m).
    /// - Command.Hunt: Relentless pursuit ordered by the Girl. Chases across any distance until caught or recalled.
    /// Pure standing sprint — no crawling transitions under any circumstances.
    /// </summary>
    protected override void HandleHuntingBehavior()
    {
        // 1. ROAMING ON ITS OWN:
        if (currentCommand == Command.Roam)
        {
            if (target != null && !IsTargetInvalidOrDead(target))
            {
                float distance = Vector3.Distance(transform.position, target.position);
                bool hasLoS = HasLineOfSight(target);

                // Roam Leash: if player runs too far (>16m, or >12m without LoS), return to roaming!
                if (distance > 16.0f || (!hasLoS && distance > 12.0f))
                {
                    target = null;
                    ExecuteRoam(walkSpeed);
                    return;
                }

                if (distance <= attackRange && hasLoS)
                {
                    _agent.isStopped = true;
                    PlayBerserkerIdleLocomotion();
                    RotateTowardsTarget(target);

                    if (_attackTimer <= 0f)
                    {
                        _attackCoroutine = StartCoroutine(PerformAttackRoutine());
                    }
                }
                else
                {
                    if (CheckForwardObstacleInChase(target.position))
                    {
                        ExecuteSmartReroute();
                        return;
                    }

                    _agent.isStopped = false;
                    _agent.speed = runSpeed;
                    _agent.SetDestination(target.position);
                    PlayBerserkerRunLocomotion(runSpeed);
                }
                return;
            }

            // On its own: periodically check if an investigator comes close (<= 12m)
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = 0.5f;
                Transform candidate = EvaluateBestTarget(null);
                if (candidate != null)
                {
                    float d = Vector3.Distance(transform.position, candidate.position);
                    if (d <= 12.0f && HasLineOfSight(candidate))
                    {
                        target = candidate;
                    }
                }
            }

            if (target == null)
            {
                ExecuteRoam(walkSpeed);
                return;
            }
        }

        // 2. HUNT COMMAND ISSUED BY THE GIRL:
        // Relentless pursuit! No matter how far away an investigator is, keep chasing until caught or recalled!
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.4f;
            target = EvaluateBestTarget(target);
        }

        if (target != null && !IsTargetInvalidOrDead(target))
        {
            float distance = Vector3.Distance(transform.position, target.position);
            bool hasLoS = HasLineOfSight(target);

            if (distance <= attackRange && hasLoS)
            {
                _agent.isStopped = true;
                PlayBerserkerIdleLocomotion();
                RotateTowardsTarget(target);

                if (_attackTimer <= 0f)
                {
                    _attackCoroutine = StartCoroutine(PerformAttackRoutine());
                }
            }
            else
            {
                if (CheckForwardObstacleInChase(target.position))
                {
                    ExecuteSmartReroute();
                    return;
                }

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
    /// - When called back: Ignores investigators and runs straight to the Girl.
    /// - While guarding: Chases investigators that come close, but returns to the Girl if they run far away.
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
        Vector3 guardOffset = new Vector3(Mathf.Sin(angleOffset), 0f, Mathf.Cos(angleOffset)) * 4.2f;
        Vector3 targetSpot = leader.position + guardOffset;

        float distToGirl = Vector3.Distance(transform.position, leader.position);
        float distToSpot = Vector3.Distance(transform.position, targetSpot);

        // 1. Recall Grace & Target Defense:
        if (_recallGraceTimer > 0f)
        {
            _recallGraceTimer -= Time.deltaTime;
            target = null;
        }
        else
        {
            Transform threat = FindBestTarget();
            if (threat != null)
            {
                float distToThreat = Vector3.Distance(transform.position, threat.position);
                float girlToThreat = Vector3.Distance(leader.position, threat.position);
                bool threatLoS = HasLineOfSight(threat);

                if (target != null)
                {
                    // LEASH: if the chased player runs far away, drop chase and return to the Girl!
                    if (distToThreat > 8.5f && girlToThreat > 8.0f)
                    {
                        target = null;
                    }
                    else if (distToThreat <= attackRange && threatLoS)
                    {
                        _agent.isStopped = true;
                        PlayBerserkerIdleLocomotion();
                        RotateTowardsTarget(threat);
                        if (_attackTimer <= 0f)
                        {
                            _attackCoroutine = StartCoroutine(PerformAttackRoutine());
                        }
                        return;
                    }
                    else
                    {
                        _agent.isStopped = false;
                        _agent.speed = runSpeed;
                        _agent.SetDestination(threat.position);
                        PlayBerserkerRunLocomotion(runSpeed);
                        return;
                    }
                }
                else
                {
                    // New threat: only intercept if an investigator comes close!
                    if (girlToThreat <= 5.5f || distToThreat <= 6.0f)
                    {
                        target = threat;
                        _agent.isStopped = false;
                        _agent.speed = runSpeed;
                        _agent.SetDestination(threat.position);
                        PlayBerserkerRunLocomotion(runSpeed);
                        return;
                    }
                }
            }
            else
            {
                target = null;
            }
        }

        // 2. Personal Space Buffer with the Girl:
        // If the Girl approaches too close (< 3.2m), smoothly yield space and step back
        if (distToGirl < 3.2f)
        {
            Vector3 pushBack = (transform.position - leader.position).normalized;
            if (pushBack.sqrMagnitude < 0.01f) pushBack = -leader.forward;
            Vector3 retreatSpot = leader.position + pushBack * 4.2f;
            if (NavMesh.SamplePosition(retreatSpot, out NavMeshHit backHit, 2.5f, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.speed = walkSpeed;
                _agent.SetDestination(backHit.position);
                PlayBerserkerWalkLocomotion(walkSpeed);
                return;
            }
        }

        // 3. Follow / Guard the Girl at respectful perimeter
        if (distToSpot <= 1.5f)
        {
            _agent.isStopped = true;
            PlayBerserkerIdleLocomotion();
            // Natural stationary idle: DO NOT snap around to face the Girl!
            if (_turnActiveTimer <= 0f) StopTurningAnimation();
        }
        else
        {
            _agent.isStopped = false;
            if (distToGirl > 6.0f)
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
}
