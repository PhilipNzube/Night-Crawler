using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using NightCrawler.Monsters;
using SuperHorizon.EnemyNPCai;

/// <summary>
/// Server-authoritative Monster AI for Night Crawler.
/// Re-architected following SOLID principles and adopting the proven, robust architecture of EnemyNPC_AI.
/// 
/// Coordinates:
/// - MonsterPerception (FOV cone, Line-of-Sight, Hearing, Target selection)
/// - MonsterLocomotion (NavMeshAgent navigation, SetDestinationSmooth, Obstacle blockage & Repositioning)
/// - MonsterCombat (Attack timing, Hitbox synchronization, Vocalizations)
/// - Network synchronization (Server-authoritative AI, NetworkTransform, ClientRpc animation & audio triggers)
/// - Vengeful Spirit Commands (Hunt, Follow/Guard, Roam)
/// - Vengeful Spirit Possession (IPossessable / MonsterController)
/// - Damage reception (IDamageReceiver, TargetHealth, EnemyHealthBar)
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterAI : NetworkBehaviour, IDamageReceiver
{
    public enum MonsterType
    {
        Zombie,     // Crawling & standing sprint, neck bite, screech
        Berserker   // Massive roar, mutant run, devastating claw swipe
    }

    public enum AIState
    {
        Falling,
        SpawningScream,
        Patrol,      // Roaming and patrolling cavern (EnemyNPC_AI)
        Running,     // Chasing / pursuing target or sprinting to leader
        Attacking,
        Dead,
        Idle,
        Search       // Searching last known target position (EnemyNPC_AI)
    }

    public enum Command { Hunt, Follow, Roam }

    public enum ZombiePosture : byte
    {
        Crawling = 0,
        StandingUp = 1,
        Standing = 2
    }

    // =========================================================================
    //  Inspector Fields & Configuration
    // =========================================================================

    [Header("Monster Identity")]
    public MonsterType monsterType = MonsterType.Zombie;
    public Command currentCommand = Command.Roam;
    public bool isBeingPossessed = false;

    [Header("Scriptable Data")]
    public EntityStats stats;

    [Header("Modular Subsystems (SOLID)")]
    [Tooltip("Sensory awareness component (Vision cone, LoS, Hearing).")]
    public MonsterPerception perception;

    [Tooltip("Movement & navigation component (NavMeshAgent, smooth paths, repositioning).")]
    public MonsterLocomotion locomotion;

    [Tooltip("Melee combat component (Attack range, cooldown, hitbox activation).")]
    public MonsterCombat combat;

    [Header("Monster Posture")]
    public ZombiePosture currentPosture = ZombiePosture.Standing;

    [Header("Target & Search Settings")]
    public Transform target;
    public float searchDuration = 8.0f;

    [Header("Spawn Scream / Roar (3D Audio)")]
    public bool playScreamOnSpawn = true;
    public float screamDuration = 2.6f;
    public float screamAudioDelay = 0.35f;
    public AudioClip spawnScreamClip;
    public AudioSource audioSource;
    public AudioClip hurtSoundClip;

    [Header("EnemyNPC_AI Health Bar Integration")]
    [Tooltip("EnemyHealthBar component from the EnemyNPC_AI package. Drag and wire in Inspector.")]
    public EnemyHealthBar enemyNPCHealthBar;

    [Tooltip("World offset above monster head for EnemyHealthBar.")]
    public Vector3 enemyHealthBarOffset = new Vector3(0, 2.2f, 0);

    [Header("Health & UI (Legacy Fallback)")]
    public MonsterHealthBar healthBar;
    public TargetHealth targetHealth;

    [Header("Hit Stagger & Stun Settings")]
    [Tooltip("Duration in seconds the monster remains stunned/immobile upon taking damage from axe or weapons.")]
    public float hitReactionStunDuration = 2.5f;

    [Header("Network Synchronization")]
    public NetworkVariable<AIState> netAIState = new NetworkVariable<AIState>(
        AIState.SpawningScream,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> netMoveSpeed = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Quaternion> netRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Tooltip("Optional NetworkTransform component on this monster for real-time position/rotation sync.")]
    public Unity.Netcode.Components.NetworkTransform networkTransform;

    [Header("Corpse Dissolve (Death Cleanup)")]
    [Tooltip("If true, the dead monster corpse will dissolve away after death using the Wraith dissolve effect.")]
    public bool dissolveOnDeath = true;

    [Tooltip("Delay in seconds after death before the corpse starts dissolving (e.g. 5s - 15s for quick cleanup, or 60s - 180s for a few minutes).")]
    [Range(1f, 600f)]
    public float dissolveDelayAfterDeath = 10f;

    [Tooltip("Duration of the dissolve effect animation in seconds.")]
    [Range(0.5f, 15f)]
    public float dissolveDuration = 3.5f;

    [Tooltip("ModelDissolveController attached to this monster. Wire in Inspector or will be added automatically.")]
    public ModelDissolveController dissolveController;

    private bool _hasDied = false;

    [Header("Runtime State")]
    public AIState currentState = AIState.SpawningScream;

    [Header("Close Melee Closeness & Recoil")]
    [Tooltip("Delay in seconds before attack audio plays, synchronizing with the strike animation.")]
    public float attackAudioDelay = 0.15f;
    protected Coroutine _attackAudioCoroutine;

    /// <summary>
    /// Intimate striking distance where claws, jaw, or weapons make physical contact with the victim.
    /// </summary>
    public virtual float CloseAttackContactDistance => (monsterType == MonsterType.Berserker) ? 1.15f : 0.72f;

    /// <summary>
    /// Distance threshold required to initiate an attack. Matches combat striking range.
    /// If distance is larger, monster continues running to close the gap (EnemyNPC_AI).
    /// </summary>
    public virtual float MaxAttackInitiationDistance => combat != null ? combat.attackRange : 1.35f;

    // Single source of truth delegates to subsystems (eliminates duplicate Inspector fields)
    public float attackRange => combat != null ? combat.attackRange : 2.2f;
    public float attackDamage => combat != null ? combat.attackDamage : 35f;
    public float attackCooldown => combat != null ? combat.attackCooldown : 1.8f;
    public MonsterAttackHitbox attackHitbox => combat != null ? combat.attackHitbox : null;
    public AudioClip attackSoundClip => combat != null ? combat.attackSoundClip : null;

    public float runSpeed => locomotion != null ? locomotion.chaseSpeed : 4.2f;
    public float walkSpeed => locomotion != null ? locomotion.walkSpeed : 1.4f;
    public float angularSpeed => locomotion != null ? locomotion.angularSpeed : 480f;

    public NetworkVariable<ZombiePosture> netPosture = new NetworkVariable<ZombiePosture>(
        ZombiePosture.Standing,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Active Monsters Registry
    public static readonly List<MonsterAI> ActiveMonsters = new List<MonsterAI>();

    // Internal State
    protected NavMeshAgent _agent;
    protected Animator _animator;
    protected bool _hasScreamed = false;
    protected bool _hasLanded = true;
    protected float _searchTimer = 0f;
    protected Vector3 _lastKnownTargetPosition;
    protected bool _hasLastKnownPosition = false;
    protected float _hitStaggerTimer = 0f;
    protected float _retargetTimer = 0f;
    protected float _recallGraceTimer = 0f;
    protected bool _isWaitingAtRoamWaypoint = false;
    protected float _roamWaitTimer = 0f;
    protected Vector3 _lastPosition;
    protected NavMeshPath _roamTestPath;
    protected Transform _commandLeader;
    public Transform CommandLeader => (currentCommand == Command.Follow) ? (_commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null)) : null;
    protected Coroutine _spawnScreamCoroutine;
    protected Coroutine _attackCoroutine;

    // Animation Hashes
    protected readonly int _speedHash       = Animator.StringToHash("Speed");
    protected readonly int _isRunningHash   = Animator.StringToHash("IsRunning");
    protected readonly int _attackHash      = Animator.StringToHash("Attack");
    protected readonly int _screamHash      = Animator.StringToHash("Scream");
    protected readonly int _roarHash        = Animator.StringToHash("Roar");
    protected readonly int _dieHash         = Animator.StringToHash("Die");
    protected readonly int _hitHash         = Animator.StringToHash("Hit");
    protected readonly int _hitReactionHash = Animator.StringToHash("HitReaction");
    protected readonly int _standUpHash     = Animator.StringToHash("StandUp");
    protected readonly int _isStandingHash  = Animator.StringToHash("IsStanding");
    protected readonly int _isCrawlingHash  = Animator.StringToHash("IsCrawling");
    protected readonly int _isWalkingHash   = Animator.StringToHash("IsWalking");

    protected new bool HasAuthority => (NetworkObject != null && NetworkObject.IsSpawned) ? IsServer : true;

    // =========================================================================
    //  Initialization & Setup
    // =========================================================================

    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();

        // Wire or add modular subsystems
        if (perception == null) perception = GetComponent<MonsterPerception>();
        if (locomotion == null) locomotion = GetComponent<MonsterLocomotion>();
        if (combat == null) combat = GetComponent<MonsterCombat>();
        if (targetHealth == null) targetHealth = GetComponent<TargetHealth>();
        if (targetHealth == null) targetHealth = GetComponentInChildren<TargetHealth>(true);

        if (healthBar == null) healthBar = GetComponentInChildren<MonsterHealthBar>(true);
        if (enemyNPCHealthBar == null) enemyNPCHealthBar = GetComponentInChildren<EnemyHealthBar>(true);
        if (combat != null && combat.attackHitbox == null) combat.attackHitbox = GetComponentInChildren<MonsterAttackHitbox>(true);
        if (networkTransform == null) networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

        _lastPosition = transform.position;
        _roamTestPath = new NavMeshPath();
        ConfigureAudio();
        ConfigureMonsterDefaults();
        SyncSubsystemParameters();

        // Enforce default command is Roam at spawn: monsters never automatically start chasing the girl!
        currentCommand = Command.Roam;
        target = null;
        _hasLastKnownPosition = false;

        if (enemyNPCHealthBar != null)
        {
            if (healthBar != null)
            {
                // Custom Heat UI healthbar is attached; suppress EnemyNPC_AI bar so it never conflicts
                enemyNPCHealthBar.gameObject.SetActive(false);
            }
            else
            {
                enemyNPCHealthBar.target = transform;
                enemyNPCHealthBar.offset = enemyHealthBarOffset;
            }
        }

        if (!ActiveMonsters.Contains(this))
        {
            ActiveMonsters.Add(this);
        }

        currentState = playScreamOnSpawn ? AIState.SpawningScream : AIState.Patrol;
    }

    protected virtual void Start()
    {
        if (playScreamOnSpawn && !_hasScreamed)
        {
            _spawnScreamCoroutine = StartCoroutine(SpawnScreamRoutine());
        }
        else
        {
            SwitchState(AIState.Patrol);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!ActiveMonsters.Contains(this))
        {
            ActiveMonsters.Add(this);
        }

        if (targetHealth != null)
        {
            targetHealth.currentHealth.OnValueChanged += HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged += HandlePostureNetworkChanged;
        netAIState.OnValueChanged += HandleNetAIStateChanged;

        if (IsServer)
        {
            currentCommand = Command.Roam;
            target = null;
            _hasLastKnownPosition = false;
            netAIState.Value = currentState;
            netPosition.Value = transform.position;
            netRotation.Value = transform.rotation;
        }
        else
        {
            // On client devices: disable local NavMeshAgent & CharacterController
            // so they do NOT fight the server's synchronized transform position!
            if (_agent != null)
            {
                _agent.enabled = false;
            }
            if (TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = false;
            }
        }

        // Initialize EnemyNPC_AI Health Bar if wired
        if (enemyNPCHealthBar != null)
        {
            enemyNPCHealthBar.target = transform;
            enemyNPCHealthBar.offset = enemyHealthBarOffset;
            float max = targetHealth != null ? targetHealth.MaxHealth : 100f;
            float cur = targetHealth != null ? targetHealth.CurrentHealth : 100f;
            enemyNPCHealthBar.SetHealth(max > 0 ? cur / max : 1f);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        ActiveMonsters.Remove(this);

        if (targetHealth != null)
        {
            targetHealth.currentHealth.OnValueChanged -= HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged -= HandlePostureNetworkChanged;
        netAIState.OnValueChanged -= HandleNetAIStateChanged;
    }

    private void HandleNetAIStateChanged(AIState prev, AIState curr)
    {
        currentState = curr;
    }

    public override void OnDestroy()
    {
        ActiveMonsters.Remove(this);
        base.OnDestroy();
    }

    private void ConfigureAudio()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.spatialBlend = 1.0f; // 3D sound
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 5.0f;
        audioSource.maxDistance = 50.0f;
        audioSource.playOnAwake = false;
        audioSource.volume = GameSettingsManager.SFXVolume;
    }

    protected virtual void ConfigureMonsterDefaults()
    {
        if (monsterType == MonsterType.Berserker)
        {
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
            screamDuration = 2.6f;
            currentPosture = ZombiePosture.Standing;
        }
        else
        {
            attackAudioDelay = 0.15f;
            if (locomotion != null)
            {
                locomotion.chaseSpeed = 3.8f;
                locomotion.walkSpeed = 0.55f;
                locomotion.stoppingDistance = 0.65f;
                if (locomotion.agent != null) locomotion.agent.stoppingDistance = 0.65f;
            }
            if (combat != null)
            {
                combat.attackDamage = 35f;
                combat.attackRange = 0.88f;
                combat.attackCooldown = 1.6f;
            }
            screamDuration = 2.4f;
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

    protected void SyncSubsystemParameters()
    {
        if (combat != null && combat.audioSource == null)
        {
            combat.audioSource = audioSource;
        }

        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(currentPosture == ZombiePosture.Crawling);
        }
    }

    // =========================================================================
    //  State Machine & Brain Loop (Adopted from EnemyNPC_AI AIController)
    // =========================================================================

    private void Update()
    {
        if (isBeingPossessed || currentState == AIState.Dead)
        {
            if (locomotion != null) locomotion.Stop();
            return;
        }

        if (HasAuthority)
        {
            // Hit stagger pause (Stun for hitReactionStunDuration seconds)
            if (_hitStaggerTimer > 0f)
            {
                _hitStaggerTimer -= Time.deltaTime;
                if (locomotion != null) locomotion.Stop();
                if (_hitStaggerTimer <= 0f)
                {
                    Debug.Log($"<color=yellow>[MonsterAI / Zombie]</color> {gameObject.name} recovered from hit stun! Resuming pursuit and attack.");
                    if (target != null && !perception.IsTargetInvalidOrDead(target))
                    {
                        SwitchState(AIState.Running);
                        if (locomotion != null) locomotion.Resume(runSpeed);
                    }
                    else
                    {
                        SwitchState(AIState.Patrol);
                    }
                }
                return;
            }

            UpdateBrain();

            // Replicate server transform and velocity to network variables
            netPosition.Value = transform.position;
            netRotation.Value = transform.rotation;
            float curSpeed = (_agent != null && _agent.isOnNavMesh) ? _agent.velocity.magnitude : 0f;
            netMoveSpeed.Value = curSpeed;
        }
        else
        {
            // On clients without NetworkTransform, smoothly interpolate to server position & rotation
            if (networkTransform == null)
            {
                if (netPosition.Value != Vector3.zero)
                {
                    transform.position = Vector3.Lerp(transform.position, netPosition.Value, Time.deltaTime * 18f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, netRotation.Value, Time.deltaTime * 18f);
                }
            }
        }

        UpdateLocomotionAnimation();
        _lastPosition = transform.position;
    }

    protected virtual void UpdateLocomotionAnimation()
    {
        if (locomotion != null)
        {
            locomotion.UpdateAnimation(_speedHash, _isRunningHash);
        }
    }

    protected virtual void UpdateBrain()
    {
        switch (currentState)
        {
            case AIState.Idle:
                if (locomotion != null) locomotion.Stop();
                if (perception != null && perception.HasHeardNoise)
                {
                    _lastKnownTargetPosition = perception.LastKnownNoisePosition;
                    _hasLastKnownPosition = true;
                    perception.ClearNoise();
                    SwitchState(AIState.Search);
                }
                break;

            case AIState.SpawningScream:
                // Stay stationary while roaring/screaming
                if (locomotion != null) locomotion.Stop();
                if (target != null && locomotion != null)
                {
                    locomotion.RotateTowards(target.position, angularSpeed);
                }
                break;

            case AIState.Patrol:
                HandlePatrolBehavior();
                break;

            case AIState.Running:
                HandlePursuitAndTargeting();
                break;

            case AIState.Attacking:
                if (target != null && locomotion != null)
                {
                    locomotion.RotateTowards(target.position, angularSpeed * 1.5f);
                }
                break;

            case AIState.Search:
                HandleSearchBehavior();
                break;

            case AIState.Falling:
                SwitchState(AIState.Patrol);
                break;
        }
    }

    // =========================================================================
    //  Patrol, Pursuit, Targeting & Commands (EnemyNPC_AI Integration)
    // =========================================================================

    /// <summary>
    /// Roams/patrols the cavern calmly when no target is present.
    /// (EnemyNPC_AI AIState.Patrol).
    protected virtual void HandlePatrolBehavior()
    {
        // 1. Follow command overrides roam
        if (currentCommand == Command.Follow)
        {
            SwitchState(AIState.Running);
            return;
        }

        // 1b. Hunt command overrides roam: relentlessly hunt investigators across the entire mine!
        if (currentCommand == Command.Hunt)
        {
            Transform huntTarget = FindGlobalInvestigatorTarget();
            if (huntTarget != null)
            {
                target = huntTarget;
                _hasLastKnownPosition = true;
                _lastKnownTargetPosition = target.position;
                SwitchState(AIState.Running);
                if (locomotion != null)
                {
                    locomotion.Resume(runSpeed);
                    locomotion.SetDestinationSmooth(target.position);
                }
                return;
            }
        }

        // 2. Scan for players/threats (EnemyNPC_AI) - strictly ignore the Girl!
        Transform candidate = perception != null ? perception.FindBestTarget(null) : null;
        if (candidate != null && !IsGirl(candidate) && perception.CanSeeTarget(candidate, true))
        {
            target = candidate;
            SwitchState(AIState.Running);
            return;
        }

        // 3. Check for loud sounds
        if (perception != null && perception.HasHeardNoise)
        {
            _lastKnownTargetPosition = perception.LastKnownNoisePosition;
            _hasLastKnownPosition = true;
            perception.ClearNoise();
            SwitchState(AIState.Search);
            return;
        }

        // 4. Roam cavern smoothly with waypoint pauses
        ExecuteRoam(walkSpeed);
    }

    protected virtual void HandlePursuitAndTargeting()
    {
        // 1. Follow / Guard Command
        if (currentCommand == Command.Follow)
        {
            HandleGuardingBehavior();
            return;
        }

        // 2. Hunt or Roam Command
        HandleHuntingBehavior();
    }

    protected virtual void HandleHuntingBehavior()
    {
        // 1. Under Hunt command, continuously acquire/track the closest living investigator globally
        if (currentCommand == Command.Hunt)
        {
            if (target == null || perception.IsTargetInvalidOrDead(target) || IsGirl(target))
            {
                target = FindGlobalInvestigatorTarget();
            }
        }
        else
        {
            // Periodically re-evaluate best target within local perception
            _retargetTimer -= Time.deltaTime;
            if (_retargetTimer <= 0f)
            {
                _retargetTimer = 0.4f;
                Transform candidate = perception != null ? perception.FindBestTarget(target) : null;
                if (candidate != null && !IsGirl(candidate))
                {
                    target = candidate;
                }
            }
        }

        // Validate target - strictly ensure it is NOT the Girl!
        if (target != null && !IsGirl(target) && !perception.IsTargetInvalidOrDead(target))
        {
            bool canSee = perception.CanSeeTarget(target);
            float dist = Vector3.Distance(transform.position, target.position);

            if (canSee)
            {
                _hasLastKnownPosition = true;
                _lastKnownTargetPosition = target.position;
            }

            float strikeRange = combat != null ? combat.attackRange : 1.35f;

            // In range for attack?
            // If in close striking range and have line of sight:
            if (dist <= strikeRange && perception.HasLineOfSight(target))
            {
                if (combat != null && !combat.IsOnCooldown)
                {
                    ExecuteAttack(target);
                    return;
                }
                else
                {
                    // In striking range but on cooldown: hold ground and face the target directly
                    if (locomotion != null)
                    {
                        locomotion.Stop();
                        locomotion.RotateTowards(target.position, angularSpeed * 1.5f);
                    }
                    return;
                }
            }

            // If actively executing an obstacle reposition escape detour:
            if (locomotion != null && locomotion.IsRepositioning)
            {
                locomotion.Resume(runSpeed);
                if (locomotion.agent != null && locomotion.agent.hasPath)
                {
                    locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
                }
                return;
            }

            // Path blockage check (from EnemyNPC_AI AIController)
            if (locomotion != null && locomotion.IsPathBlocked(target))
            {
                if (locomotion.AttemptReposition())
                {
                    locomotion.Resume(runSpeed);
                    if (locomotion.agent != null && locomotion.agent.hasPath)
                    {
                        locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
                    }
                    return;
                }
            }

            // EnemyNPC_AI Navigation: If the enemy is a bit far from the stop and attack distance,
            // move directly towards target.position to close that gap and attack!
            if (locomotion != null)
            {
                locomotion.Resume(runSpeed);
                locomotion.SetDestinationSmooth(target.position);

                // EnemyNPC_AI steering alignment:
                if (canSee && dist <= strikeRange * 2.5f)
                {
                    locomotion.RotateTowards(target.position, angularSpeed);
                }
                else if (locomotion.agent != null && locomotion.agent.hasPath)
                {
                    locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
                }
            }
            return;
        }

        // Check if loud noise was heard (player talking loudly, shouting, or running)
        if (perception != null && perception.HasHeardNoise)
        {
            _lastKnownTargetPosition = perception.LastKnownNoisePosition;
            _hasLastKnownPosition = true;
            perception.ClearNoise();
            SwitchState(AIState.Search);
            return;
        }

        // Target lost or dead -> Switch to search or patrol (EnemyNPC_AI)
        target = null;
        if (currentCommand == Command.Hunt)
        {
            // If commanded to Hunt, re-scan globally for any other living investigator!
            Transform nextTarget = FindGlobalInvestigatorTarget();
            if (nextTarget != null)
            {
                target = nextTarget;
                _hasLastKnownPosition = true;
                _lastKnownTargetPosition = target.position;
                if (locomotion != null)
                {
                    locomotion.Resume(runSpeed);
                    locomotion.SetDestinationSmooth(target.position);
                }
                return;
            }
        }

        if (_hasLastKnownPosition)
        {
            SwitchState(AIState.Search);
        }
        else
        {
            SwitchState(AIState.Patrol);
        }
    }

    protected virtual void HandleSearchBehavior()
    {
        _searchTimer -= Time.deltaTime;

        // If actively executing an obstacle reposition escape detour:
        if (locomotion != null && locomotion.IsRepositioning)
        {
            locomotion.Resume(walkSpeed);
            if (locomotion.agent != null && locomotion.agent.hasPath)
            {
                locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
            }
            return;
        }

        // Path blockage check during search (from EnemyNPC_AI AIController)
        if (locomotion != null && locomotion.IsPathBlocked())
        {
            if (locomotion.AttemptReposition())
            {
                locomotion.Resume(walkSpeed);
                if (locomotion.agent != null && locomotion.agent.hasPath)
                {
                    locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
                }
                return;
            }
        }

        // Check if noise heard during search (from EnemyNPC_AI AIController)
        if (perception.HasHeardNoise)
        {
            locomotion.SetDestinationSmooth(perception.LastKnownNoisePosition);
            perception.ClearNoise();
        }
        else if (_agent != null && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
        {
            // Sample nearby point around last known position with wall validation
            Vector3 roamOffset = Random.insideUnitSphere * 6f;
            roamOffset.y = 0;
            Vector3 candidatePoint = _lastKnownTargetPosition + roamOffset;
            if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                if (!NavMesh.Raycast(transform.position, hit.position, out _, NavMesh.AllAreas))
                {
                    locomotion.SetDestinationSmooth(hit.position);
                }
            }
        }

        // Look for targets while searching
        if (currentCommand == Command.Hunt)
        {
            Transform huntTarget = FindGlobalInvestigatorTarget();
            if (huntTarget != null)
            {
                target = huntTarget;
                _hasLastKnownPosition = true;
                _lastKnownTargetPosition = target.position;
                SwitchState(AIState.Running);
                if (locomotion != null)
                {
                    locomotion.Resume(runSpeed);
                    locomotion.SetDestinationSmooth(GetSurroundingDestination(target, CloseAttackContactDistance));
                }
                return;
            }
        }

        Transform candidate = perception != null ? perception.FindBestTarget(null, perception.searchViewRadius) : null;
        if (candidate != null && !IsGirl(candidate) && perception.CanSeeTarget(candidate, true))
        {
            target = candidate;
            SwitchState(AIState.Running);
            return;
        }

        if (_searchTimer <= 0f)
        {
            _hasLastKnownPosition = false;
            SwitchState(AIState.Patrol);
        }
    }

    protected virtual void HandleGuardingBehavior()
    {
        Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
        if (leader == null)
        {
            SwitchState(AIState.Patrol);
            return;
        }

        // When called back by the Girl, totally forget about the hunt and return immediately to her side!
        target = null;
        _hasLastKnownPosition = false;

        // If actively executing an obstacle reposition escape detour:
        if (locomotion != null && locomotion.IsRepositioning)
        {
            locomotion.Resume(runSpeed);
            if (locomotion.agent != null && locomotion.agent.hasPath)
            {
                locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
            }
            return;
        }

        // Path blockage check while returning to leader
        if (locomotion != null && locomotion.IsPathBlocked())
        {
            if (locomotion.AttemptReposition())
            {
                locomotion.Resume(runSpeed);
                if (locomotion.agent != null && locomotion.agent.hasPath)
                {
                    locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
                }
                return;
            }
        }

        float distToLeader = Vector3.Distance(transform.position, leader.position);
        float stopDist = Mathf.Max(locomotion != null ? locomotion.stoppingDistance : 2.6f, 2.6f);

        // EnemyNPC_AI Follow architecture:
        // Direct corridor pathfinding straight to leader.position along the NavMesh without wall collisions!
        if (distToLeader > stopDist + 2.0f)
        {
            locomotion.Resume(runSpeed);
            locomotion.SetDestinationSmooth(GetSurroundingDestination(leader, stopDist + 1.2f));
            if (locomotion.agent != null && locomotion.agent.hasPath)
            {
                locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
            }
        }
        else if (distToLeader > stopDist)
        {
            locomotion.Resume(walkSpeed);
            locomotion.SetDestinationSmooth(GetSurroundingDestination(leader, stopDist));
            if (locomotion.agent != null && locomotion.agent.hasPath)
            {
                locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
            }
        }
        else if (distToLeader < 2.2f)
        {
            // Too close! Turn and move forward to yield spot, never walking backward!
            Vector3 push = (transform.position - leader.position);
            push.y = 0f;
            Vector3 escapeDir = push.sqrMagnitude > 0.05f ? push.normalized : transform.forward;
            Vector3 yieldSpot = leader.position + escapeDir * 2.8f;
            if (NavMesh.SamplePosition(yieldSpot, out NavMeshHit yHit, 2.5f, NavMesh.AllAreas))
            {
                locomotion.Resume(walkSpeed * 0.85f);
                locomotion.SetDestinationSmooth(yHit.position);
                // Turn in movement direction so it moves forward, never walking backward!
                if (locomotion.agent != null && locomotion.agent.hasPath)
                {
                    locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed * 1.5f);
                }
                else
                {
                    locomotion.RotateTowards(yHit.position, angularSpeed * 1.5f);
                }
            }
        }
        else
        {
            // Safe distance cleared: stop and turn back to face the Queen!
            locomotion.Stop();
            locomotion.RotateTowards(leader.position, angularSpeed * 1.2f);
        }
    }

    /// <summary>
    /// Computes a distinct flanking / surrounding destination around a shared target or leader
    /// for monsters of the same kind so they surround targets rather than stacking into the same coordinate.
    /// Also ensures a solo monster maintains striking/following clearance rather than stopping inside targets.
    /// </summary>
    public Vector3 GetSurroundingDestination(Transform targetTransform, float radius = 1.8f)
    {
        if (targetTransform == null) return transform.position;

        bool isFollowingLeader = (currentCommand == Command.Follow || targetTransform == _commandLeader || (GameManager.Instance != null && targetTransform == GameManager.Instance.GirlTransform));
        float effectiveRadius = isFollowingLeader ? Mathf.Clamp(radius, 2.5f, 3.8f) : Mathf.Clamp(radius, 0.55f, 2.8f);

        int sameKindIndex = 0;
        int totalSameKind = 0;

        for (int i = 0; i < ActiveMonsters.Count; i++)
        {
            var m = ActiveMonsters[i];
            if (m != null && m.monsterType == this.monsterType && m.currentState != AIState.Dead)
            {
                if (m.target == targetTransform || (currentCommand == Command.Follow && m.currentCommand == Command.Follow))
                {
                    if (m == this) sameKindIndex = totalSameKind;
                    totalSameKind++;
                }
            }
        }

        if (totalSameKind <= 1)
        {
            // Even a solo monster maintains an offset so it never stops inside the target or Queen!
            Vector3 dir = transform.position - targetTransform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.05f) dir = -targetTransform.forward;
            Vector3 singleOffset = dir.normalized * effectiveRadius;
            Vector3 singleCandidate = targetTransform.position + singleOffset;
            if (NavMesh.SamplePosition(singleCandidate, out NavMeshHit singleHit, 3.0f, NavMesh.AllAreas))
            {
                return singleHit.position;
            }
            return targetTransform.position + singleOffset;
        }

        // Distribute surrounding slots evenly on an arc/circle around the target
        float angleStep = 360f / totalSameKind;
        float angle = sameKindIndex * angleStep;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * effectiveRadius);
        Vector3 candidate = targetTransform.position + offset;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return candidate;
    }

    protected virtual void ExecuteRoam(float speed)
    {
        if (_agent == null || !_agent.isOnNavMesh) return;

        // Wall collision / stuck avoidance while roaming
        if (!_isWaitingAtRoamWaypoint && locomotion != null && locomotion.IsPathBlocked())
        {
            if (locomotion.CanReposition && locomotion.AttemptReposition())
            {
                return;
            }
            else
            {
                PickNewRoamDestination(speed);
                return;
            }
        }

        if (locomotion != null && locomotion.IsRepositioning)
        {
            if (locomotion.agent != null && locomotion.agent.hasPath)
            {
                locomotion.RotateTowards(locomotion.agent.steeringTarget, angularSpeed);
            }
            return;
        }

        if (!_agent.pathPending && (!_agent.hasPath || _agent.remainingDistance <= _agent.stoppingDistance + 0.3f))
        {
            if (!_isWaitingAtRoamWaypoint)
            {
                // Arrived at roam waypoint! Pause calmly (from EnemyNPC_AI AIController)
                _isWaitingAtRoamWaypoint = true;
                _roamWaitTimer = Random.Range(2.5f, 4.5f);
                if (locomotion != null) locomotion.Stop();
            }
            else
            {
                _roamWaitTimer -= Time.deltaTime;
                if (locomotion != null) locomotion.Stop();

                if (_roamWaitTimer <= 0f)
                {
                    _isWaitingAtRoamWaypoint = false;
                    PickNewRoamDestination(speed);
                }
            }
        }
    }

    /// <summary>
    /// Intelligently picks a forward/flanking walkable corridor point.
    /// Confirms the candidate does not penetrate cave walls with NavMesh.Raycast and PathComplete.
    /// (Adopted from EnemyNPC_AI AIController).
    /// </summary>
    protected virtual void PickNewRoamDestination(float speed)
    {
        if (_agent == null || !_agent.isOnNavMesh) return;

        float[] angleOffsets = { 0f, 35f, -35f, 65f, -65f, 95f, -95f, 180f };
        for (int i = 0; i < angleOffsets.Length; i++)
        {
            int randIdx = Random.Range(i, angleOffsets.Length);
            float temp = angleOffsets[i];
            angleOffsets[i] = angleOffsets[randIdx];
            angleOffsets[randIdx] = temp;
        }

        Vector3 baseForward = transform.forward.sqrMagnitude > 0.01f ? transform.forward : Vector3.forward;
        float roamDist = Random.Range(6.5f, 11.5f);
        if (_roamTestPath == null) _roamTestPath = new NavMeshPath();

        foreach (float angle in angleOffsets)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * baseForward;
            Vector3 candidatePos = transform.position + dir * roamDist;

            if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                // Verify line on NavMesh does not cross a wall
                if (!NavMesh.Raycast(transform.position, hit.position, out _, NavMesh.AllAreas))
                {
                    if (_agent.CalculatePath(hit.position, _roamTestPath) && _roamTestPath.status == NavMeshPathStatus.PathComplete)
                    {
                        if (locomotion != null)
                        {
                            locomotion.Resume(speed);
                            locomotion.SetDestinationDirect(hit.position);
                        }
                        return;
                    }
                }
            }
        }

        // Fallback: smaller radius close sample if inside an alcove
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 fallbackOffset = Random.insideUnitSphere * 4.5f;
            fallbackOffset.y = 0;
            Vector3 fallbackCandidate = transform.position + fallbackOffset;
            if (NavMesh.SamplePosition(fallbackCandidate, out NavMeshHit fallbackHit, 3.0f, NavMesh.AllAreas))
            {
                if (_agent.CalculatePath(fallbackHit.position, _roamTestPath) && _roamTestPath.status == NavMeshPathStatus.PathComplete)
                {
                    if (locomotion != null)
                    {
                        locomotion.Resume(speed);
                        locomotion.SetDestinationDirect(fallbackHit.position);
                    }
                    return;
                }
            }
        }
    }

    public virtual void SetCommand(Command cmd, Transform girlTransform)
    {
        currentCommand = cmd;
        if (girlTransform != null) _commandLeader = girlTransform;
        else if (GameManager.Instance != null) _commandLeader = GameManager.Instance.GirlTransform;

        if (cmd == Command.Follow)
        {
            // When recalled, totally forget about the hunt and return immediately to the Girl!
            target = null;
            _hasLastKnownPosition = false;
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }
            if (combat != null) combat.CancelAttack();
            _recallGraceTimer = 0f;

            SwitchState(AIState.Running);
            if (locomotion != null && _commandLeader != null)
            {
                locomotion.Resume(runSpeed);
                locomotion.SetDestinationSmooth(GetSurroundingDestination(_commandLeader, 2.5f));
            }
        }
        else if (cmd == Command.Hunt)
        {
            _commandLeader = null;
            _recallGraceTimer = 0f;

            // Stop any active spawn scream or roar coroutine immediately so monster can attack
            if (_spawnScreamCoroutine != null)
            {
                StopCoroutine(_spawnScreamCoroutine);
                _spawnScreamCoroutine = null;
            }

            target = FindGlobalInvestigatorTarget();
            if (target != null)
            {
                _hasLastKnownPosition = true;
                _lastKnownTargetPosition = target.position;
                SwitchState(AIState.Running);
                if (locomotion != null)
                {
                    locomotion.Resume(runSpeed);
                    locomotion.SetDestinationSmooth(target.position);
                }
            }
            else
            {
                SwitchState(AIState.Patrol);
            }
        }
        else if (cmd == Command.Roam)
        {
            _commandLeader = null;
            _recallGraceTimer = 0f;
            target = null;
            _hasLastKnownPosition = false;
            SwitchState(AIState.Patrol);
        }
    }

    public void SwitchState(AIState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netAIState.Value = newState;
        }

        _isWaitingAtRoamWaypoint = false;
        _roamWaitTimer = 0f;

        switch (newState)
        {
            case AIState.Patrol:
                target = null;
                if (locomotion != null) locomotion.Resume(walkSpeed);
                break;
            case AIState.Search:
                _searchTimer = searchDuration;
                if (locomotion != null) locomotion.Resume(walkSpeed);
                break;
            case AIState.Running:
                if (locomotion != null) locomotion.Resume(runSpeed);
                break;
            case AIState.Idle:
                if (locomotion != null) locomotion.Stop();
                break;
        }
    }

    // =========================================================================
    //  Combat Execution & Attacks
    // =========================================================================

    protected virtual void ExecuteAttack(Transform attackTarget)
    {
        if (combat == null || combat.IsOnCooldown) return;

        currentState = AIState.Attacking;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netAIState.Value = AIState.Attacking;
        }
        locomotion.Stop();
        locomotion.RotateTowards(attackTarget.position, angularSpeed * 2f);

        // Enforce physical striking clearance so the monster never penetrates or clips inside the investigator
        if (attackTarget != null && locomotion != null && locomotion.agent != null && locomotion.agent.enabled)
        {
            Vector3 diff = transform.position - attackTarget.position;
            diff.y = 0f;
            float dist = diff.magnitude;
            if (dist < 1.35f && dist > 0.01f)
            {
                Vector3 push = (diff / dist) * (1.35f - dist);
                locomotion.agent.Move(push);
            }
        }

        string trigger = (monsterType == MonsterType.Berserker) ? "Attack" : "Attack";
        combat.StartAttack(trigger, () =>
        {
            if (currentState != AIState.Dead)
            {
                currentState = AIState.Running;
                if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
                {
                    netAIState.Value = AIState.Running;
                }
                locomotion.Resume(runSpeed);
            }
            _attackCoroutine = null;
        });

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayAttackClientRpc();
        }
    }

    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        if (IsServer) return;
        if (_animator != null) _animator.SetTrigger(_attackHash);
        if (attackSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSoundClip, GameSettingsManager.SFXVolume);
        }
    }

    // =========================================================================
    //  Spawn Scream / Roar (3D Audio)
    // =========================================================================

    protected virtual IEnumerator SpawnScreamRoutine()
    {
        _hasScreamed = true;
        currentState = AIState.SpawningScream;
        if (locomotion != null) locomotion.Stop();
        target = null; // Do not acquire any target on spawn

        TriggerScreamAnimation();

        float audioDelay = Mathf.Clamp(screamAudioDelay, 0.1f, screamDuration * 0.5f);
        yield return new WaitForSeconds(audioDelay);

        Play3DScream();

        float remaining = Mathf.Max(0.5f, screamDuration - audioDelay);
        yield return new WaitForSeconds(remaining);

        // A newly spawned monster always begins in calm Patrol/Roam mode.
        // It strictly ignores previous commands and only hunts if commanded AFTER spawning.
        target = null;
        currentCommand = Command.Roam;
        SwitchState(AIState.Patrol);
        if (locomotion != null) locomotion.Resume(walkSpeed);

        _spawnScreamCoroutine = null;
    }

    protected virtual void TriggerScreamAnimation()
    {
        string trigger = (monsterType == MonsterType.Berserker) ? "Roar" : "Scream";
        if (_animator != null) _animator.SetTrigger(trigger);

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySpawnScreamAnimClientRpc();
        }
    }

    [ClientRpc]
    private void PlaySpawnScreamAnimClientRpc()
    {
        if (IsServer) return;
        string trigger = (monsterType == MonsterType.Berserker) ? "Roar" : "Scream";
        if (_animator != null) _animator.SetTrigger(trigger);
    }

    protected virtual void Play3DScream()
    {
        if (spawnScreamClip != null && audioSource != null)
        {
            audioSource.clip = spawnScreamClip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySpawnScreamAudioClientRpc();
        }
    }

    [ClientRpc]
    private void PlaySpawnScreamAudioClientRpc()
    {
        if (IsServer) return;
        if (spawnScreamClip != null && audioSource != null)
        {
            audioSource.clip = spawnScreamClip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }
    }

    // =========================================================================
    //  Attack Audio & Animation Recoil Synchronization
    // =========================================================================

    public void StartSynchronizedAttackAudio(float delay = 0.15f)
    {
        // Attack swing audio silenced per design (physical impact audio handled on hitbox contact)
    }

    public void Play3DAttackAudio()
    {
        // Attack swing audio silenced per design
    }

    [ClientRpc]
    private void PlayAttackAudioClientRpc()
    {
        // Attack swing audio silenced per design
    }

    public void Stop3DAttackAudio()
    {
        // Safe no-op
    }

    [ClientRpc]
    private void StopAttackAudioClientRpc()
    {
        // Safe no-op
    }

    /// <summary>
    /// Deprecated: Recoil step-back removed per design. Monsters hold their ground in striking range.
    /// </summary>
    protected virtual IEnumerator AttackRecoilAndResumeRoutine(float pushDistance = 0.38f, float duration = 0.25f)
    {
        yield break;
    }

    // =========================================================================
    //  Damage Intake, Hit Reactions & Death
    // =========================================================================

    public void TakeDamage(float amount, bool isSoulAttack = false)
    {
        if (currentState == AIState.Dead) return;

        // Remote client forwarding: allow investigators to hit monsters even without network authority
        if (!IsServer)
        {
            Debug.Log($"<color=orange>[MonsterAI / TakeDamage]</color> Client forwarding {amount} damage to server for {gameObject.name}");
            TakeDamageServerRpc(amount, isSoulAttack);
            return;
        }

        if (targetHealth != null)
        {
            targetHealth.TakeDamage(amount, isSoulAttack);
        }

        float currentHp = targetHealth != null ? targetHealth.CurrentHealth : 0f;
        float maxHp = targetHealth != null ? targetHealth.MaxHealth : 100f;

        Debug.Log($"<color=red>[MonsterAI / Zombie Hit Detected]</color> {gameObject.name} took {amount:F1} damage! Current HP: {currentHp:F1}/{maxHp:F1}. Stunned for {hitReactionStunDuration:F1}s.");

        // 1. Update custom Heat UI healthbar (primary)
        if (healthBar != null)
        {
            healthBar.OnDamaged(currentHp, maxHp);
        }
        // 2. Fallback to EnemyNPC_AI healthbar only if no custom Heat UI bar is attached
        else if (enemyNPCHealthBar != null)
        {
            enemyNPCHealthBar.gameObject.SetActive(true);
            enemyNPCHealthBar.SetHealth(maxHp > 0.001f ? currentHp / maxHp : 0f);
        }

        PlayHitReaction();

        // Aggro Retaliation: switch to attacker (ignoring the Girl)
        Transform attacker = perception != null ? perception.FindBestTarget(null, 15f) : null;
        if (attacker != null && !IsGirl(attacker))
        {
            target = attacker;
            SwitchState(AIState.Running);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TakeDamageServerRpc(float amount, bool isSoulAttack = false)
    {
        TakeDamage(amount, isSoulAttack);
    }

    public virtual void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;

        Stop3DAttackAudio();
        _hitStaggerTimer = hitReactionStunDuration;
        if (locomotion != null) locomotion.Stop();
        if (combat != null) combat.CancelAttack();
        if (_attackCoroutine != null)
        {
            StopCoroutine(_attackCoroutine);
            _attackCoroutine = null;
        }

        if (_animator != null) _animator.SetTrigger(_hitReactionHash);

        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayHitReactionClientRpc();
        }
    }

    [ClientRpc]
    private void PlayHitReactionClientRpc()
    {
        if (IsServer) return;
        Stop3DAttackAudio();
        _hitStaggerTimer = hitReactionStunDuration;
        if (locomotion != null) locomotion.Stop();
        if (combat != null) combat.CancelAttack();
        if (_attackCoroutine != null)
        {
            StopCoroutine(_attackCoroutine);
            _attackCoroutine = null;
        }

        if (_animator != null) _animator.SetTrigger(_hitReactionHash);
        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
        }
    }

    private void HandleTargetHealthChanged(float prev, float curr)
    {
        float maxHp = targetHealth != null ? targetHealth.MaxHealth : 100f;

        // 1. Update custom Heat UI healthbar (primary)
        if (healthBar != null)
        {
            healthBar.OnDamaged(curr, maxHp);
        }
        // 2. Fallback to EnemyNPC_AI healthbar only if custom bar is absent
        else if (enemyNPCHealthBar != null)
        {
            enemyNPCHealthBar.gameObject.SetActive(true);
            enemyNPCHealthBar.SetHealth(maxHp > 0.001f ? curr / maxHp : 0f);
        }

        if (curr <= 0f && currentState != AIState.Dead)
        {
            Die();
        }
    }

    protected virtual void Die()
    {
        if (_hasDied) return;
        _hasDied = true;

        currentState = AIState.Dead;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netAIState.Value = AIState.Dead;
        }
        ActiveMonsters.Remove(this);

        if (locomotion != null) locomotion.Stop();
        if (_agent != null) _agent.enabled = false;
        if (combat != null) combat.CancelAttack();
        Stop3DAttackAudio();
        if (healthBar != null) healthBar.gameObject.SetActive(false);
        if (enemyNPCHealthBar != null) enemyNPCHealthBar.gameObject.SetActive(false);

        Debug.Log($"<color=red>[MonsterAI / Zombie Slain]</color> {gameObject.name} has died.");

        TriggerRagdollPhysics();

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayDeathClientRpc();
        }

        if (dissolveOnDeath)
        {
            StartCoroutine(CorpseDissolveRoutine());
        }
    }

    private void TriggerRagdollPhysics()
    {
        if (TryGetComponent<NetworkRagdollController>(out var ragdoll))
        {
            ragdoll.TriggerRagdollDeath();
            return;
        }

        if (_animator != null) _animator.enabled = false;

        var rbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            if (rb.gameObject == gameObject) continue;
            rb.isKinematic = false;
            rb.detectCollisions = true;
        }
    }

    [ClientRpc]
    private void PlayDeathClientRpc()
    {
        if (IsServer) return;
        Die();
    }

    private System.Collections.IEnumerator CorpseDissolveRoutine()
    {
        // 1. Wait configured delay after death (seconds to minutes)
        yield return new WaitForSeconds(dissolveDelayAfterDeath);

        // 2. Resolve dissolve controller
        if (dissolveController == null)
        {
            dissolveController = GetComponent<ModelDissolveController>();
            if (dissolveController == null)
            {
                dissolveController = gameObject.AddComponent<ModelDissolveController>();
            }
        }

        // 3. Prepare dissolve materials with preserved monster textures
        Material matTemplate = dissolveController.dissolveMaterial;
        if (matTemplate != null)
        {
            dissolveController.ApplyDissolveMaterialTemplate(matTemplate);
        }
        else
        {
            dissolveController.CollectRenderers();
        }

        // Freeze ragdoll bodies and disable colliders so the dissolving corpse stays in place without physics jitter
        var rbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            if (rb != null) rb.isKinematic = true;
        }

        var allColliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in allColliders)
        {
            if (c != null) c.enabled = false;
        }

        // 4. Start transition from 0 (solid) to 1 (dissolved)
        dissolveController.SetDissolveImmediate(0f);
        dissolveController.StartTransition(1f, dissolveDuration);

        yield return new WaitForSeconds(dissolveDuration + 0.3f);

        // 5. Despawn on server (syncs across all connected clients) or destroy if offline
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else if (NetworkObject == null || !NetworkObject.IsSpawned)
        {
            Destroy(gameObject);
        }
    }

    protected virtual void HandlePostureNetworkChanged(ZombiePosture prev, ZombiePosture curr)
    {
        currentPosture = curr;
        if (locomotion != null)
        {
            locomotion.SetCrawlPosture(curr == ZombiePosture.Crawling);
        }
    }

    // =========================================================================
    //  Compatibility & Helper Methods
    // =========================================================================

    public static bool IsGirl(Transform t)
    {
        if (t == null) return false;
        Transform root = t.root != null ? t.root : t;

        // 1. Direct Transform match from GameManager
        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.GirlTransform == t || GameManager.Instance.GirlTransform == root) return true;
            if (GameManager.Instance.girlClientId != null && GameManager.Instance.girlClientId.Value != 999 && GameManager.Instance.girlClientId.Value != ulong.MaxValue)
            {
                if (t.TryGetComponent<NetworkObject>(out var no) && no.OwnerClientId == GameManager.Instance.girlClientId.Value) return true;
                if (root.TryGetComponent<NetworkObject>(out var rno) && rno.OwnerClientId == GameManager.Instance.girlClientId.Value) return true;
            }
        }

        // 2. Client ID match from CharacterSelectManager or GirlRevealManager
        if (CharacterSelectManager.SavedVengefulSpiritClientId != 999 && CharacterSelectManager.SavedVengefulSpiritClientId != ulong.MaxValue)
        {
            if (t.TryGetComponent<NetworkObject>(out var no) && no.OwnerClientId == CharacterSelectManager.SavedVengefulSpiritClientId) return true;
            if (root.TryGetComponent<NetworkObject>(out var rno) && rno.OwnerClientId == CharacterSelectManager.SavedVengefulSpiritClientId) return true;
        }
        if (GirlRevealManager.SavedGirlClientId != 999 && GirlRevealManager.SavedGirlClientId != ulong.MaxValue)
        {
            if (t.TryGetComponent<NetworkObject>(out var no) && no.OwnerClientId == GirlRevealManager.SavedGirlClientId) return true;
            if (root.TryGetComponent<NetworkObject>(out var rno) && rno.OwnerClientId == GirlRevealManager.SavedGirlClientId) return true;
        }

        // 3. Component signatures
        if (t.GetComponentInChildren<GirlStealth>() != null || t.GetComponentInParent<GirlStealth>() != null || root.GetComponentInChildren<GirlStealth>() != null) return true;
        if (t.GetComponentInChildren<GirlPossession>() != null || t.GetComponentInParent<GirlPossession>() != null || root.GetComponentInChildren<GirlPossession>() != null) return true;
        if (t.GetComponentInChildren<GirlMaterialController>() != null || t.GetComponentInParent<GirlMaterialController>() != null || root.GetComponentInChildren<GirlMaterialController>() != null) return true;
        if (t.GetComponentInChildren<GirlMovement>() != null || t.GetComponentInParent<GirlMovement>() != null || root.GetComponentInChildren<GirlMovement>() != null) return true;
        if (t.GetComponentInChildren<GirlCommandNet>() != null || t.GetComponentInParent<GirlCommandNet>() != null || root.GetComponentInChildren<GirlCommandNet>() != null) return true;
        if (t.GetComponentInChildren<GirlPuppetNet>() != null || t.GetComponentInParent<GirlPuppetNet>() != null || root.GetComponentInChildren<GirlPuppetNet>() != null) return true;
        if (t.GetComponentInChildren<GirlSenseNet>() != null || t.GetComponentInParent<GirlSenseNet>() != null || root.GetComponentInChildren<GirlSenseNet>() != null) return true;
        if (t.GetComponentInChildren<GirlAttackNet>() != null || t.GetComponentInParent<GirlAttackNet>() != null || root.GetComponentInChildren<GirlAttackNet>() != null) return true;
        if (t.GetComponentInChildren<GirlShadowTeleportNet>() != null || t.GetComponentInParent<GirlShadowTeleportNet>() != null || root.GetComponentInChildren<GirlShadowTeleportNet>() != null) return true;
        if (t.GetComponentInChildren<GirlStateController>() != null || t.GetComponentInParent<GirlStateController>() != null || root.GetComponentInChildren<GirlStateController>() != null) return true;

        var pNet = t.GetComponentInChildren<PlayerPossessableNet>();
        if (pNet == null) pNet = t.GetComponentInParent<PlayerPossessableNet>();
        if (pNet == null) pNet = root.GetComponentInChildren<PlayerPossessableNet>();
        if (pNet != null && pNet.isPossessed.Value) return true;

        // 4. Name signatures on both t and its root
        string n = t.name.ToLower();
        string rn = root.name.ToLower();
        return n.Contains("girl") || (n.Contains("demon") && !n.Contains("monster") && !n.Contains("creep")) || n.Contains("vengeful") || n.Contains("wraith")
            || rn.Contains("girl") || (rn.Contains("demon") && !rn.Contains("monster") && !rn.Contains("creep")) || rn.Contains("vengeful") || rn.Contains("wraith");
    }

    public Vector3 FindUnobstructedPerimeterSpot(Transform leader, float preferredDist = 4.2f, float minClearance = 2.5f)
    {
        if (leader == null) return transform.position;

        float[] candidateAngles = { 180f, 145f, -145f, 115f, -115f, 85f, -85f, 0f };
        Vector3 leaderPos = leader.position;
        Vector3 leaderFwd = leader.forward.sqrMagnitude > 0.01f ? leader.forward : Vector3.forward;

        foreach (float angle in candidateAngles)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * leaderFwd;
            Vector3 testPoint = leaderPos + dir * preferredDist;

            if (NavMesh.SamplePosition(testPoint, out NavMeshHit navHit, 2.5f, NavMesh.AllAreas))
            {
                if (!Physics.Linecast(leaderPos + Vector3.up * 1f, navHit.position + Vector3.up * 1f, perception.obstructionMask))
                {
                    return navHit.position;
                }
            }
        }

        return leaderPos + leaderFwd * preferredDist;
    }

    /// <summary>
    /// Searches the entire mine for the closest living human investigator, regardless of distance.
    /// Excludes the Girl, dead players, and corpses.
    /// (Adopts global targeting for the Vengeful Spirit's Hunt command).
    /// </summary>
    public Transform FindGlobalInvestigatorTarget()
    {
        Transform bestTarget = null;
        float bestDist = Mathf.Infinity;
        Vector3 myPos = transform.position;

        // 1. Check GameManager tracked alive explorers
        if (GameManager.Instance != null && GameManager.Instance.AliveExplorers != null)
        {
            var aliveList = GameManager.Instance.AliveExplorers;
            for (int i = 0; i < aliveList.Count; i++)
            {
                var netObj = aliveList[i];
                if (netObj == null || !netObj.gameObject.activeInHierarchy) continue;
                Transform t = netObj.transform;
                if (IsGirl(t) || IsTargetInvalidOrDead(t)) continue;

                float dist = Vector3.Distance(myPos, t.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = t;
                }
            }
            if (bestTarget != null) return bestTarget;
        }

        // 2. Check NetworkManager SpawnManager spawned objects
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects != null)
        {
            foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
            {
                if (netObj == null || !netObj.IsPlayerObject || !netObj.gameObject.activeInHierarchy) continue;
                Transform t = netObj.transform;
                if (IsGirl(t) || IsTargetInvalidOrDead(t)) continue;

                float dist = Vector3.Distance(myPos, t.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = t;
                }
            }
            if (bestTarget != null) return bestTarget;
        }

        // 3. Fallback: Tag check across active scene objects
        var players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p == null || !p.activeInHierarchy) continue;
            Transform root = p.transform.root != null ? p.transform.root : p.transform;
            if (IsGirl(root) || IsTargetInvalidOrDead(root)) continue;

            float dist = Vector3.Distance(myPos, root.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = root;
            }
        }

        return bestTarget;
    }

    public Transform FindBestTarget() => perception != null ? perception.FindBestTarget(target) : target;
    public Transform EvaluateBestTarget(Transform current) => perception != null ? perception.FindBestTarget(current) : current;
    public bool HasLineOfSight(Transform t) => perception != null ? perception.HasLineOfSight(t) : true;
    public bool IsTargetInvalidOrDead(Transform t) => perception != null ? perception.IsTargetInvalidOrDead(t) : (t == null);
    public void RotateTowardsTarget(Transform t) { if (t != null && locomotion != null) locomotion.RotateTowards(t.position, angularSpeed); }
}