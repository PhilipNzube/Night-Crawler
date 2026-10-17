using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Dedicated Zombie minion AI.
/// Extends MonsterAI following the Open/Closed Principle (OCP).
/// Features:
/// - 4-tiered dynamic locomotion: Crawling sprint when far (> 12m), standing sprint when close (<= 7m),
///   calm upright walking when patrolling/guarding/roaming, and stationary idling at waypoints.
/// - Terrifying screech spawn scream.
/// - Seamless stand-up and crawl transitions with network synchronization.
/// - Relentless right-hand claw swipe melee attack (Zombie Attack).
/// - Full synchronization with Zombie Animator states:
///   * Zombie Scream
///   * Running Crawl
///   * Zombie Run
///   * Zombie Walk
///   * Zombie Idle
///   * Zombie Stand Up
///   * Zombie Attack
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
    protected readonly int _stateZombieAttack = Animator.StringToHash("Zombie Attack");
    protected readonly int _stateZombieHit     = Animator.StringToHash("Zombie Reaction Hit");

    [Header("Zombie Posture Transitions")]
    public float crawlSpeed = 6.2f;
    public float standRunSpeed = 4.8f;
    public float standTransitionDistance = 14.0f;
    public float crawlTransitionDistance = 22.0f;
    public float standUpDuration = 1.15f;

    private Coroutine _standUpRoutine;
    private int _currentLocomotionAnimHash = 0;

    protected override void Awake()
    {
        monsterType = MonsterType.Zombie;
        currentPosture = ZombiePosture.Standing;
        base.Awake();
        currentCommand = Command.Roam;
        target = null;
        _hasLastKnownPosition = false;
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
        }
        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (_animator != null)
        {
            _animator.applyRootMotion = false;
        }
        if (IsServer)
        {
            netPosture.Value = ZombiePosture.Standing;
        }
    }

    protected override void ConfigureMonsterDefaults()
    {
        monsterType = MonsterType.Zombie;
        currentPosture = ZombiePosture.Standing;
        screamDuration = 2.4f;
        screamAudioDelay = 0.25f;

        // Posture transition thresholds:
        // Crawling sprint is strictly reserved for long-distance pursuit or recalling from far away (> 22m).
        // Near the Girl or in patrol mode, monsters must ALWAYS remain upright (walks or standing runs).
        crawlTransitionDistance = 22.0f;
        standTransitionDistance = 14.0f;

        strikeContactDistance = 1.15f;
        attackAudioDelay = 0.15f;

        if (locomotion != null)
        {
            locomotion.chaseSpeed = crawlSpeed;
            locomotion.walkSpeed = 1.5f;
            locomotion.stoppingDistance = 1.0f;
            if (locomotion.agent != null) locomotion.agent.stoppingDistance = 1.0f;
        }

        if (combat != null)
        {
            combat.attackDamage = 35f;
            combat.attackRange = 1.35f;
            combat.attackCooldown = 1.6f;
        }

        if (stats != null)
        {
            if (stats.runSpeed > 0)
            {
                crawlSpeed = stats.runSpeed * 1.25f;
                standRunSpeed = stats.runSpeed;
                if (locomotion != null) locomotion.chaseSpeed = crawlSpeed;
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

        // 1. Chasing an enemy player
        if (currentState == AIState.Running && target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            EvaluatePosture(dist, false);
        }
        // 2. Following / called by the Girl (Command.Follow)
        else if (currentState == AIState.Running && currentCommand == Command.Follow)
        {
            Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
            if (leader != null)
            {
                float distToLeader = Vector3.Distance(transform.position, leader.position);
                EvaluatePosture(distToLeader, true);
            }
        }
        // 3. Roaming, patrolling, or searching
        else
        {
            // When roaming, searching, patrolling, or idling, MUST BE STANDING
            // Strictly only stand up if the zombie was actually crawling
            if (currentPosture == ZombiePosture.Crawling && _standUpRoutine == null)
            {
                Vector3 headOrigin = transform.position + Vector3.up * 0.65f;
                LayerMask mask = (locomotion != null && locomotion.obstacleMask.value != 0) 
                    ? locomotion.obstacleMask 
                    : LayerMask.GetMask("Default");
                bool hasOverheadObstacle = Physics.Raycast(headOrigin, Vector3.up, 1.4f, mask, QueryTriggerInteraction.Ignore);
                if (!hasOverheadObstacle)
                {
                    StartStandUp();
                    return;
                }
            }
        }

        base.UpdateBrain();
    }

    public override void SetCommand(Command cmd, Transform girlTransform)
    {
        base.SetCommand(cmd, girlTransform);

        if (cmd == Command.Follow)
        {
            Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
            if (leader != null)
            {
                float dist = Vector3.Distance(transform.position, leader.position);
                // When called/recalled from far away across the cavern (> 22m), drop into high-speed all-fours crawl sprint!
                // If already around the girl (<= 22m), NEVER crawl — stay upright!
                if (dist > crawlTransitionDistance)
                {
                    DropToCrawl();
                }
                else if (currentPosture == ZombiePosture.Crawling)
                {
                    StartStandUp();
                }
            }
        }
        else if (cmd == Command.Hunt)
        {
            if (target != null)
            {
                float dist = Vector3.Distance(transform.position, target.position);
                // When dispatched to hunt from far away (> 16m), drop into high-speed all-fours crawl sprint!
                if (dist > 16.0f)
                {
                    DropToCrawl();
                }
            }
        }
    }

    private void EvaluatePosture(float distanceToTarget, bool isFollowingLeader)
    {
        if (!HasAuthority || _standUpRoutine != null) return;

        // When following/guarding the Girl:
        // Monsters must NEVER drop into crawl state when already around the Girl!
        // Only drop into crawl sprint if the target/leader is quite far away (> 22m),
        // and stand up early (<= 14m) so they escort/patrol upright.
        float effectiveCrawlDist = isFollowingLeader ? crawlTransitionDistance : 16.0f;
        float effectiveStandDist = isFollowingLeader ? standTransitionDistance : 8.0f;

        if (currentPosture == ZombiePosture.Crawling)
        {
            // Close enough to stand up, UNLESS under a low ceiling or tight obstacle clearance
            if (distanceToTarget <= effectiveStandDist)
            {
                Vector3 headOrigin = transform.position + Vector3.up * 0.65f;
                LayerMask mask = (locomotion != null && locomotion.obstacleMask.value != 0) 
                    ? locomotion.obstacleMask 
                    : LayerMask.GetMask("Default");
                bool hasOverheadObstacle = Physics.Raycast(headOrigin, Vector3.up, 1.4f, mask, QueryTriggerInteraction.Ignore);
                if (!hasOverheadObstacle)
                {
                    StartStandUp();
                }
            }
        }
        else if (currentPosture == ZombiePosture.Standing)
        {
            // Strictly only drop to crawl sprint during long-distance pursuit (> effectiveCrawlDist).
            // Never drop to crawl from arbitrary obstacle hits while patrolling or roaming!
            if (distanceToTarget > effectiveCrawlDist)
            {
                DropToCrawl();
            }
        }
    }

    private void StartStandUp()
    {
        // Stand up animation must NEVER play unless explicitly transitioning from an active crawl!
        if (currentPosture != ZombiePosture.Crawling || _standUpRoutine != null) return;
        _standUpRoutine = StartCoroutine(StandUpRoutine());
    }

    private IEnumerator StandUpRoutine()
    {
        currentPosture = ZombiePosture.StandingUp;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned) netPosture.Value = ZombiePosture.StandingUp;

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
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned) netPosture.Value = ZombiePosture.Standing;

        if (_animator != null)
        {
            _animator.SetBool(_isStandingHash, true);
            _animator.SetBool(_isCrawlingHash, false);
        }

        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(false);
            bool closingGapToGirl = (currentCommand == Command.Follow && _commandLeader != null && Vector3.Distance(transform.position, _commandLeader.position) > 4.5f);
            float speedToResume = (target != null || closingGapToGirl) ? standRunSpeed : walkSpeed;
            locomotion.chaseSpeed = standRunSpeed;
            locomotion.Resume(speedToResume);
        }

        _standUpRoutine = null;
        _currentLocomotionAnimHash = 0;
    }

    [ClientRpc]
    private void TriggerStandUpClientRpc()
    {
        if (IsServer) return;
        // Client safety guard: Stand up must never play unless in Crawling posture
        if (currentPosture != ZombiePosture.Crawling && netPosture.Value != ZombiePosture.Crawling) return;

        if (_animator != null)
        {
            _animator.SetTrigger(_standUpHash);
            _animator.CrossFade(_stateZombieStandUp, 0.12f);
        }
    }

    private void DropToCrawl()
    {
        currentPosture = ZombiePosture.Crawling;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned) netPosture.Value = ZombiePosture.Crawling;

        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(true);
            locomotion.chaseSpeed = crawlSpeed;
            locomotion.Resume(crawlSpeed);
        }

        if (_animator != null)
        {
            _animator.SetBool(_isStandingHash, false);
            _animator.SetBool(_isCrawlingHash, true);
            _animator.CrossFade(_stateRunningCrawl, 0.2f);
        }

        _currentLocomotionAnimHash = _stateRunningCrawl;

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

    /// <summary>
    /// Synchronizes locomotion animations across idle, walk, run, and crawl.
    /// Drives the Zombie Animator state machine cleanly across host and clients.
    /// </summary>
    protected override void UpdateLocomotionAnimation()
    {
        if (_animator == null) return;

        AIState effectiveState = HasAuthority ? currentState : netAIState.Value;
        ZombiePosture effectivePosture = HasAuthority ? currentPosture : netPosture.Value;

        // Do not override special action states
        if (effectiveState == AIState.Dead || 
            effectiveState == AIState.SpawningScream || 
            effectiveState == AIState.Attacking || 
            effectivePosture == ZombiePosture.StandingUp || 
            _hitStaggerTimer > 0f)
        {
            _currentLocomotionAnimHash = 0;
            return;
        }

        float currentSpeed = HasAuthority
            ? ((_agent != null && _agent.isOnNavMesh) ? _agent.velocity.magnitude : (Time.deltaTime > 0f ? (transform.position - _lastPosition).magnitude / Time.deltaTime : 0f))
            : netMoveSpeed.Value;

        bool isStopped = HasAuthority ? ((_agent != null && _agent.isOnNavMesh) ? _agent.isStopped : false) : (currentSpeed < 0.15f);
        bool isMoving = currentSpeed > 0.15f && !isStopped;

        _animator.SetFloat(_speedHash, currentSpeed);

        if (effectivePosture == ZombiePosture.Crawling)
        {
            _animator.SetBool(_isCrawlingHash, true);
            _animator.SetBool(_isStandingHash, false);
            _animator.SetBool(_isRunningHash, true);
            _animator.SetBool(_isWalkingHash, false);

            if (_currentLocomotionAnimHash != _stateRunningCrawl)
            {
                _animator.CrossFade(_stateRunningCrawl, 0.2f);
                _currentLocomotionAnimHash = _stateRunningCrawl;
            }
        }
        else // Standing posture (ZombiePosture.Standing)
        {
            _animator.SetBool(_isStandingHash, true);
            _animator.SetBool(_isCrawlingHash, false);

            bool shouldRun = (effectiveState == AIState.Running);

            if (isMoving)
            {
                if (shouldRun || currentSpeed > (walkSpeed * 1.35f))
                {
                    // Upright run (Zombie Run)
                    _animator.SetBool(_isRunningHash, true);
                    _animator.SetBool(_isWalkingHash, false);

                    if (_currentLocomotionAnimHash != _stateZombieRun)
                    {
                        _animator.CrossFade(_stateZombieRun, 0.2f);
                        _currentLocomotionAnimHash = _stateZombieRun;
                    }
                }
                else
                {
                    // Upright walk (Zombie Walk) - patrolling girl's perimeter or roaming cavern
                    _animator.SetBool(_isWalkingHash, true);
                    _animator.SetBool(_isRunningHash, false);

                    if (_currentLocomotionAnimHash != _stateZombieWalk)
                    {
                        _animator.CrossFade(_stateZombieWalk, 0.2f);
                        _currentLocomotionAnimHash = _stateZombieWalk;
                    }
                }
            }
            else
            {
                // Stopped / idle (Zombie Idle) - stationed next to girl or waiting at waypoint
                _animator.SetBool(_isWalkingHash, false);
                _animator.SetBool(_isRunningHash, false);

                if (_currentLocomotionAnimHash != _stateZombieIdle)
                {
                    _animator.CrossFade(_stateZombieIdle, 0.25f);
                    _currentLocomotionAnimHash = _stateZombieIdle;
                }
            }
        }
    }

    protected override void HandlePostureNetworkChanged(ZombiePosture prev, ZombiePosture curr)
    {
        base.HandlePostureNetworkChanged(prev, curr);
        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(curr == ZombiePosture.Crawling);
        }
        _currentLocomotionAnimHash = 0;
    }

    protected override void TriggerScreamAnimation()
    {
        _currentLocomotionAnimHash = 0;
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
        _currentLocomotionAnimHash = 0;
        if (_animator != null)
        {
            _animator.SetTrigger(_screamHash);
            _animator.CrossFade(_stateZombieScream, 0.15f);
        }
    }

    protected override IEnumerator SpawnScreamRoutine()
    {
        yield return base.SpawnScreamRoutine();

        // When scream ends, explicitly crossfade into upright walk/idle if posture is standing,
        // so the controller's exit-time transition into Running Crawl is completely prevented.
        if (currentPosture == ZombiePosture.Standing && _animator != null)
        {
            _animator.SetBool(_isStandingHash, true);
            _animator.SetBool(_isCrawlingHash, false);
            _animator.CrossFade(_stateZombieWalk, 0.15f);
        }
        _currentLocomotionAnimHash = 0;
    }

    [Header("Zombie Melee & Hand Swipe Alignment")]
    [Tooltip("Intimate strike distance where the zombie's right hand swipe reaches the victim.")]
    public float strikeContactDistance = 1.15f;

    [System.Obsolete("Use strikeContactDistance instead.")]
    public float biteContactDistance
    {
        get => strikeContactDistance;
        set => strikeContactDistance = value;
    }

    private Coroutine _swipeAlignmentRoutine;

    protected override void ExecuteAttack(Transform attackTarget)
    {
        if (combat == null || combat.IsOnCooldown || attackTarget == null) return;

        // If distance opened up, keep running to close the gap instead of swinging at empty air
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
        _currentLocomotionAnimHash = 0;

        if (locomotion != null)
        {
            locomotion.Stop();
        }

        // Align zombie directly towards victim on authoritative server
        AlignSwipeWithTarget(attackTarget);

        if (_animator != null)
        {
            _animator.CrossFade(_stateZombieAttack, 0.12f);
        }

        if (_swipeAlignmentRoutine != null) StopCoroutine(_swipeAlignmentRoutine);
        _swipeAlignmentRoutine = StartCoroutine(SwipeAlignmentRoutine(attackTarget));

        ulong targetNetId = 0;
        if (attackTarget.TryGetComponent<NetworkObject>(out var targetNetObj))
        {
            targetNetId = targetNetObj.NetworkObjectId;
        }
        else if (attackTarget.root != null && attackTarget.root.TryGetComponent<NetworkObject>(out var rootNetObj))
        {
            targetNetId = rootNetObj.NetworkObjectId;
        }

        combat.StartAttack("Attack", () =>
        {
            Stop3DAttackAudio();
            if (_swipeAlignmentRoutine != null)
            {
                StopCoroutine(_swipeAlignmentRoutine);
                _swipeAlignmentRoutine = null;
            }

            // No recoil push back: instantly resume running or patrolling
            if (currentState != AIState.Dead)
            {
                SwitchState(target != null ? AIState.Running : AIState.Patrol);
                if (locomotion != null) locomotion.Resume(target != null ? runSpeed : walkSpeed);

                // Enforce upright posture when attack completes so controller exit-time to Running Crawl is prevented
                if (currentPosture == ZombiePosture.Standing && _animator != null)
                {
                    _animator.SetBool(_isStandingHash, true);
                    _animator.SetBool(_isCrawlingHash, false);
                    _animator.CrossFade(target != null ? _stateZombieRun : _stateZombieWalk, 0.15f);
                }
            }
            _attackCoroutine = null;
            _currentLocomotionAnimHash = 0;
        });

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayZombieAttackClientRpc(targetNetId);
        }
    }

    private IEnumerator SwipeAlignmentRoutine(Transform victim)
    {
        float timer = 0f;
        float attackDuration = combat != null ? combat.attackCooldown : 1.6f;

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

        // 2. Step into strike contact distance (~1.15m) so the right hand swipe reaches the victim
        if (currentDist > 0.01f && Mathf.Abs(currentDist - strikeContactDistance) > 0.03f)
        {
            Vector3 desiredPos = victim.position - toVictim.normalized * strikeContactDistance;
            if (UnityEngine.AI.NavMesh.SamplePosition(desiredPos, out UnityEngine.AI.NavMeshHit hit, 0.6f, UnityEngine.AI.NavMesh.AllAreas))
            {
                desiredPos = hit.position;
            }

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                Vector3 step = (desiredPos - transform.position);
                _agent.Move(step * (Time.deltaTime * 12f));
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * 12f);
            }
        }
    }

    [ClientRpc]
    private void PlayZombieAttackClientRpc(ulong targetNetId)
    {
        if (IsServer) return;
        _currentLocomotionAnimHash = 0;

        // On non-authority clients: DO NOT manipulate transform.position or transform.rotation!
        // NetworkTransform has 100% authoritative control over position and rotation,
        // preventing the clone from jittering or flipping around when attacking.
        if (_animator != null)
        {
            _animator.CrossFade(_stateZombieAttack, 0.12f);
        }
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

        _hitStaggerTimer = hitReactionStunDuration; // Stun duration (2.5s)
        _currentLocomotionAnimHash = 0;
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

        Stop3DAttackAudio();
        if (_swipeAlignmentRoutine != null)
        {
            StopCoroutine(_swipeAlignmentRoutine);
            _swipeAlignmentRoutine = null;
        }

        if (locomotion != null) locomotion.Stop();
        _hitStaggerTimer = hitReactionStunDuration;
        _currentLocomotionAnimHash = 0;

        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateZombieHit, 0.08f);
        }

        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
        }
    }
}
