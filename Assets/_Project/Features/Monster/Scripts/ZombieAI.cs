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

    [Header("Zombie Posture Transitions")]
    public float crawlSpeed = 6.2f;
    public float standRunSpeed = 4.8f;
    public float standTransitionDistance = 6.5f;
    public float crawlTransitionDistance = 7.5f;
    public float standUpDuration = 1.15f;

    private Coroutine _standUpRoutine;
    private int _currentLocomotionAnimHash = 0;

    protected override void Awake()
    {
        monsterType = MonsterType.Zombie;
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
        monsterType = MonsterType.Zombie;
        currentPosture = ZombiePosture.Standing;
        screamDuration = 2.4f;
        screamAudioDelay = 0.25f;

        // Posture transition thresholds:
        // Crawling sprint is reserved for long-distance pursuit or recalling (> 7.5m)
        // Standing sprint is used for close pursuit or approaching leader (<= 6.5m)
        crawlTransitionDistance = 7.5f;
        standTransitionDistance = 6.5f;

        if (locomotion != null)
        {
            locomotion.chaseSpeed = crawlSpeed;
            locomotion.walkSpeed = 1.5f;
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
            EvaluatePosture(dist);
        }
        // 2. Following / called by the Girl (Command.Follow)
        else if (currentState == AIState.Running && currentCommand == Command.Follow)
        {
            Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
            if (leader != null)
            {
                float distToLeader = Vector3.Distance(transform.position, leader.position);
                EvaluatePosture(distToLeader);
            }
        }
        // 3. Roaming, patrolling, or searching
        else
        {
            // When roaming, searching, or idling, MUST BE STANDING
            if (currentPosture == ZombiePosture.Crawling && _standUpRoutine == null)
            {
                StartStandUp();
                return;
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
                // When called/recalled from far away (> 6.5m), immediately drop into high-speed all-fours crawl sprint!
                if (dist > standTransitionDistance)
                {
                    DropToCrawl();
                }
            }
        }
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
    /// Drives the Zombie Animator state machine cleanly.
    /// </summary>
    protected override void UpdateLocomotionAnimation()
    {
        if (_animator == null || locomotion == null) return;

        // Do not override special action states
        if (currentState == AIState.Dead || 
            currentState == AIState.SpawningScream || 
            currentState == AIState.Attacking || 
            currentPosture == ZombiePosture.StandingUp || 
            _hitStaggerTimer > 0f)
        {
            _currentLocomotionAnimHash = 0;
            return;
        }

        float currentSpeed = (_agent != null && _agent.isOnNavMesh) 
            ? _agent.velocity.magnitude 
            : (Time.deltaTime > 0f ? (transform.position - _lastPosition).magnitude / Time.deltaTime : 0f);

        bool isStopped = (_agent != null && _agent.isOnNavMesh) ? _agent.isStopped : false;
        bool isMoving = currentSpeed > 0.15f && !isStopped;

        _animator.SetFloat(_speedHash, currentSpeed);

        if (currentPosture == ZombiePosture.Crawling)
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

            bool shouldRun = false;

            if (currentState == AIState.Running)
            {
                if (target != null)
                {
                    // Chasing an enemy player close-up
                    shouldRun = true;
                }
                else if (currentCommand == Command.Follow)
                {
                    Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
                    if (leader != null)
                    {
                        float distToLeader = Vector3.Distance(transform.position, leader.position);
                        // When closing distance to girl (> 4.5m), sprint upright!
                        // When closer (<= 4.5m), walk while patrolling her perimeter!
                        shouldRun = distToLeader > 4.5f;
                    }
                }
            }

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

    protected override void ExecuteAttack(Transform attackTarget)
    {
        if (combat == null || combat.IsOnCooldown) return;

        currentState = AIState.Attacking;
        _currentLocomotionAnimHash = 0;
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
                SwitchState(target != null ? AIState.Running : AIState.Patrol);
                if (locomotion != null) locomotion.Resume(target != null ? runSpeed : walkSpeed);
            }
            _attackCoroutine = null;
            _currentLocomotionAnimHash = 0;
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
        _currentLocomotionAnimHash = 0;
        if (_animator != null)
        {
            _animator.CrossFade(_stateZombieNeck, 0.12f);
        }
    }

    public override void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;

        _hitStaggerTimer = 0.40f;
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
        _currentLocomotionAnimHash = 0;
        if (_animator != null)
        {
            _animator.SetTrigger(_hitReactionHash);
            _animator.CrossFade(_stateZombieHit, 0.08f);
        }
    }
}
