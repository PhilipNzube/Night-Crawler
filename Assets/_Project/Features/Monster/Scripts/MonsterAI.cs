using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;
using NightCrawler.Monsters;

/// <summary>
/// Pro-grade Server-authoritative Monster AI for Night Crawler.
/// Handles:
/// - Distinct Zombie vs Berserker behaviors, animations, and damage scaling.
/// - Terrifying 3D spatial spawn screams/roars before rushing players.
/// - Unstoppable running pursuit (no walking).
/// - Dynamic target switching (distance delta, damage retaliation, corpse filtering).
/// - Precise attack hitbox activation and IDamageReceiver axe hit detection.
/// - Overhead Heat UI health bar notifications.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterAI : NetworkBehaviour, IDamageReceiver
{
    public enum MonsterType
    {
        Zombie,     // Fast crawling run, heavy neck bite, scary screech
        Berserker   // Massive roar, mutant run, devastating claw swipe
    }

    public enum AIState
    {
        Falling,
        SpawningScream,
        Running,
        Attacking,
        Dead
    }

    public enum Command { Hunt, Follow }

    public enum ZombiePosture : byte
    {
        Crawling = 0,
        StandingUp = 1,
        Standing = 2
    }

    [Header("Monster Identity")]
    public MonsterType monsterType = MonsterType.Zombie;
    public Command currentCommand = Command.Hunt;
    public bool isBeingPossessed = false;

    [Header("Scriptable Data")]
    public EntityStats stats;

    [Header("Zombie Locomotion & Postures")]
    [Tooltip("Current posture of the Zombie (Crawling on all fours vs Standing upright).")]
    public ZombiePosture currentPosture = ZombiePosture.Crawling;

    [Tooltip("Distance to target at which a crawling zombie initiates standing up.")]
    public float standTransitionDistance = 8.5f;

    [Tooltip("Distance beyond which a standing zombie drops back down to a crawl (hysteresis).")]
    public float crawlTransitionDistance = 16.0f;

    [Tooltip("Duration in seconds of the Zombie Stand Up animation.")]
    public float standUpDuration = 1.25f;

    [Tooltip("Duration in seconds of the Zombie Hit Reaction stagger.")]
    public float hitReactionDuration = 0.5f;

    [Tooltip("Walking speed in standing posture (used when following Girl or patrolling near her).")]
    public float walkSpeed = 2.6f;

    [Tooltip("Standing run speed while pursuing targets or catching up to the Girl.")]
    public float standRunSpeed = 5.6f;

    [Tooltip("Crawl run speed while sprinting on all fours across long distances.")]
    public float crawlSpeed = 6.8f;

    [Header("Movement & Pursuit (Running Only)")]
    [Tooltip("Movement speed while sprinting at targets.")]
    public float runSpeed = 6.5f;
    [Tooltip("Angular rotation speed when turning towards players.")]
    public float turnSpeed = 12f;
    [Tooltip("Acceleration for instantaneous, aggressive chasing.")]
    public float runAcceleration = 18f;

    [Header("Combat & Damage")]
    [Tooltip("Distance at which the monster initiates its attack.")]
    public float attackRange = 2.0f;
    [Tooltip("Damage dealt per attack hit. Berserkers hit significantly harder than zombies.")]
    public float attackDamage = 35f;
    [Tooltip("Cooldown between successive attacks.")]
    public float attackCooldown = 1.8f;
    [Tooltip("Physical limb hitbox component (e.g. Jaw for Zombie, Right Hand for Berserker).")]
    public MonsterAttackHitbox attackHitbox;

    [Header("Spawn Scream / Roar (3D Audio)")]
    [Tooltip("Play the signature roar/scream when spawning before chasing.")]
    public bool playScreamOnSpawn = true;
    [Tooltip("Duration of the roar state where the monster screams at players before running.")]
    public float screamDuration = 2.4f;
    [Tooltip("Audio clip for the scream/roar.")]
    public AudioClip spawnScreamClip;
    [Tooltip("3D AudioSource configured for scary spatial audio.")]
    public AudioSource audioSource;

    [Header("Targeting & Threat Aggro")]
    [Tooltip("Current target being pursued.")]
    public Transform target;
    [Tooltip("Maximum detection radius for finding investigators.")]
    public float searchRadius = 60f;
    [Tooltip("If another living player gets this much closer than the current target, switch aggro!")]
    public float targetSwitchThreshold = 5f;
    [Tooltip("Switch aggro to an investigator who strikes the monster with an axe.")]
    public bool switchTargetOnDamaged = true;

    [Header("UI Health Bar")]
    [Tooltip("WorldSpace billboard Heat UI health bar mounted above the monster's head.")]
    public MonsterHealthBar healthBar;

    [Header("Runtime State")]
    public AIState currentState = AIState.Falling;

    public NetworkVariable<ZombiePosture> netPosture = new NetworkVariable<ZombiePosture>(
        ZombiePosture.Crawling,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Component references
    private NavMeshAgent _agent;
    private CharacterController _characterController;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private TargetHealth _targetHealth;

    // Animation parameter hashes
    private readonly int _speedHash       = Animator.StringToHash("Speed");
    private readonly int _isRunningHash   = Animator.StringToHash("IsRunning");
    private readonly int _attackHash      = Animator.StringToHash("Attack");
    private readonly int _screamHash      = Animator.StringToHash("Scream");
    private readonly int _roarHash        = Animator.StringToHash("Roar");
    private readonly int _dieHash         = Animator.StringToHash("Die");
    private readonly int _hitHash         = Animator.StringToHash("Hit");
    private readonly int _hitReactionHash = Animator.StringToHash("HitReaction");
    private readonly int _standUpHash     = Animator.StringToHash("StandUp");
    private readonly int _isStandingHash  = Animator.StringToHash("IsStanding");
    private readonly int _isCrawlingHash  = Animator.StringToHash("IsCrawling");
    private readonly int _isWalkingHash   = Animator.StringToHash("IsWalking");
    private readonly HashSet<int> _animParams = new HashSet<int>();

    // Direct Animator state hashes
    private readonly int _stateHitReaction  = Animator.StringToHash("Zombie Reaction Hit");
    private readonly int _stateStandUp      = Animator.StringToHash("Zombie Stand Up");
    private readonly int _stateRun          = Animator.StringToHash("Zombie Run");
    private readonly int _stateWalk         = Animator.StringToHash("Zombie Walk");
    private readonly int _stateIdle         = Animator.StringToHash("Zombie Idle");
    private readonly int _stateRunningCrawl = Animator.StringToHash("Running Crawl");
    private readonly int _stateZombieNeck   = Animator.StringToHash("Zombie Neck");
    private readonly int _stateZombieAttack = Animator.StringToHash("Zombie Attack");

    // Internal state timers & caches
    private float _attackTimer;
    private float _retargetTimer;
    private float _hitStaggerTimer;
    private float _hitCooldownTimer;
    private Coroutine _standUpCoroutine;
    private bool _hasLanded = false;
    private bool _hasScreamed = false;
    private static readonly Collider[] _searchBuffer = new Collider[20];

    // Roam & Guard state
    private Transform _commandLeader;
    private float _roamTimer = 0f;
    private float _roamWaitTimer = 0f;
    private bool _isRoamWaiting = false;
    private Vector3 _roamDestination;

    // Close-guard wandering state near the Girl
    private bool _isGuardIdling = true;
    private float _guardIdleTimer = 2.0f;
    private float _guardWalkTimer = 0f;
    private Vector3 _guardWanderDestination;

    private bool HasAuthority => (NetworkObject != null && NetworkObject.IsSpawned) ? IsServer : true;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _targetHealth = GetComponent<TargetHealth>();
        if (_targetHealth == null)
        {
            _targetHealth = GetComponentInChildren<TargetHealth>(true);
        }
        if (_targetHealth == null)
        {
            _targetHealth = gameObject.AddComponent<TargetHealth>();
            _targetHealth.baseMaxHealth = stats != null && stats.maxHealth > 0 ? stats.maxHealth : 100f;
        }

        if (healthBar == null)
        {
            healthBar = GetComponentInChildren<MonsterHealthBar>(true);
        }

        if (attackHitbox == null)
        {
            attackHitbox = GetComponentInChildren<MonsterAttackHitbox>(true);
        }

        CacheAudioSource();
        CacheAnimatorParameters();
        ConfigureMonsterDefaults();
        SetRagdollState(false);

        if (_agent != null)
        {
            _agent.enabled = false;
            _agent.speed = runSpeed;
            _agent.acceleration = runAcceleration;
            _agent.stoppingDistance = Mathf.Max(0.5f, attackRange * 0.8f);
            _agent.autoBraking = true;
        }
    }

    private void CacheAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Configure 3D Spatial Audio for maximum terror
        audioSource.spatialBlend = 1.0f; // 100% 3D sound
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = 6.0f;
        audioSource.maxDistance = 65.0f;
        audioSource.dopplerLevel = 0.0f;
        audioSource.playOnAwake = false;
    }

    private void ConfigureMonsterDefaults()
    {
        if (monsterType == MonsterType.Berserker)
        {
            if (runSpeed < 7.0f) runSpeed = 7.5f;
            if (attackDamage < 50f) attackDamage = 65f;
            if (screamDuration <= 0f) screamDuration = 2.6f;
        }
        else
        {
            // Zombie defaults
            if (crawlSpeed <= 0f) crawlSpeed = 6.8f;
            if (standRunSpeed <= 0f) standRunSpeed = 5.6f;
            if (walkSpeed <= 0f) walkSpeed = 2.6f;
            if (runSpeed < 5.0f) runSpeed = standRunSpeed;
            if (attackDamage < 25f) attackDamage = 35f;
            if (screamDuration <= 0f) screamDuration = 2.2f;
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (_targetHealth != null)
        {
            _targetHealth.currentHealth.OnValueChanged += HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged += HandlePostureChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_targetHealth != null)
        {
            _targetHealth.currentHealth.OnValueChanged -= HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged -= HandlePostureChanged;
    }

    private void HandlePostureChanged(ZombiePosture prev, ZombiePosture curr)
    {
        currentPosture = curr;
        if (IsServer) return; // Server already drives transitions directly

        if (curr == ZombiePosture.StandingUp)
        {
            SafeSetTrigger(_standUpHash);
            SafeCrossFade(_stateStandUp, "Zombie Stand Up", 0.12f);
        }
        else if (curr == ZombiePosture.Standing)
        {
            SafeSetBool(_isStandingHash, true);
            SafeSetBool(_isCrawlingHash, false);
        }
        else if (curr == ZombiePosture.Crawling)
        {
            SafeSetBool(_isStandingHash, false);
            SafeSetBool(_isCrawlingHash, true);
            SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
        }
    }

    private void HandleTargetHealthChanged(float prev, float curr)
    {
        if (curr < prev)
        {
            if (healthBar != null)
            {
                healthBar.OnDamaged(curr, _targetHealth.MaxHealth);
            }
        }

        if (curr <= 0f && currentState != AIState.Dead)
        {
            Die();
        }
    }

    private void Update()
    {
        if (!HasAuthority || currentState == AIState.Dead) return;

        // 1. Initial fall / landing
        if (!_hasLanded)
        {
            HandleFalling();
            return;
        }

        // 2. Possession override
        if (isBeingPossessed)
        {
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            return;
        }

        if (_hitCooldownTimer > 0f) _hitCooldownTimer -= Time.deltaTime;

        // Hit stagger: pause movement and attack updates briefly on impact
        if (_hitStaggerTimer > 0f)
        {
            _hitStaggerTimer -= Time.deltaTime;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
            }
            return;
        }

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // 3. State Machine
        switch (currentState)
        {
            case AIState.SpawningScream:
                // Stay stationary while roaring/screaming
                if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                RotateTowardsTarget(target);
                break;

            case AIState.Running:
                HandlePursuitAndTargeting();
                break;

            case AIState.Attacking:
                RotateTowardsTarget(target);
                break;
        }
    }

    private void HandleFalling()
    {
        if (_characterController != null)
        {
            _characterController.Move(Vector3.down * 9.81f * Time.deltaTime);
            if (_characterController.isGrounded)
            {
                OnLanded();
            }
        }
        else
        {
            OnLanded();
        }
    }

    private void OnLanded()
    {
        _hasLanded = true;
        if (_agent != null) _agent.enabled = true;

        if (playScreamOnSpawn && !_hasScreamed)
        {
            StartCoroutine(SpawnScreamRoutine());
        }
        else
        {
            currentState = AIState.Running;
        }
    }

    /// <summary>
    /// Executes the terrifying spawn scream/roar.
    /// Rotates towards the nearest investigator and broadcasts 3D audio.
    /// </summary>
    private IEnumerator SpawnScreamRoutine()
    {
        _hasScreamed = true;
        currentState = AIState.SpawningScream;

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
        }

        // Find initial closest player to look at while screaming
        target = FindBestTarget();

        // 3D Audio Scream
        Play3DScream();

        // Animation Scream Trigger
        TriggerScreamAnimation();

        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);

        yield return new WaitForSeconds(screamDuration);

        // Transition directly to relentless pursuit
        currentState = AIState.Running;
        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = false;
        }
    }

    private void Play3DScream()
    {
        if (spawnScreamClip != null && audioSource != null)
        {
            audioSource.pitch = monsterType == MonsterType.Berserker ? Random.Range(0.85f, 0.95f) : Random.Range(1.05f, 1.20f);
            audioSource.PlayOneShot(spawnScreamClip);
        }

        // Broadcast to clients via RPC if networked
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySpawnScreamClientRpc();
        }
    }

    [ClientRpc]
    private void PlaySpawnScreamClientRpc()
    {
        if (IsServer) return; // Already played locally

        if (audioSource != null && spawnScreamClip != null)
        {
            audioSource.pitch = monsterType == MonsterType.Berserker ? 0.90f : 1.15f;
            audioSource.PlayOneShot(spawnScreamClip);
        }

        TriggerScreamAnimation();
    }

    private void TriggerScreamAnimation()
    {
        if (monsterType == MonsterType.Berserker)
        {
            SafeSetTrigger(_roarHash);
        }
        else
        {
            SafeSetTrigger(_screamHash);
        }
    }

    /// <summary>
    /// Updates command directives issued by the Vengeful Spirit (Hunt or Follow/Guard).
    /// </summary>
    public void SetCommand(Command cmd, Transform girlTransform)
    {
        currentCommand = cmd;
        if (girlTransform != null)
        {
            _commandLeader = girlTransform;
        }
        else if (GameManager.Instance != null && GameManager.Instance.GirlTransform != null)
        {
            _commandLeader = GameManager.Instance.GirlTransform;
        }

        _roamWaitTimer = 0f;
        _isRoamWaiting = false;
        _roamTimer = 0f;

        if (cmd == Command.Hunt)
        {
            target = EvaluateBestTarget(null);
        }
    }

    private void HandlePursuitAndTargeting()
    {
        if (_agent == null || !_agent.isOnNavMesh || !_agent.isActiveAndEnabled) return;

        // 1. RECALL / GUARD MODE: Stand guard by the Girl
        if (currentCommand == Command.Follow)
        {
            HandleGuardingBehavior();
            return;
        }

        // 2. HUNT MODE: Seek and destroy human investigators or roam cavern
        HandleHuntingBehavior();
    }

    private void HandleGuardingBehavior()
    {
        Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
        if (leader == null)
        {
            ExecuteRoam(walkSpeed);
            return;
        }

        if (monsterType == MonsterType.Zombie)
        {
            HandleZombieGuardingBehavior(leader);
            return;
        }

        // --- Berserker Guard Behavior ---
        float angleOffset = (GetInstanceID() % 6) * 60f * Mathf.Deg2Rad;
        Vector3 guardOffset = new Vector3(Mathf.Sin(angleOffset), 0f, Mathf.Cos(angleOffset)) * 2.2f;
        Vector3 targetSpot = leader.position + guardOffset;

        float distToGirl = Vector3.Distance(transform.position, leader.position);
        float distToSpot = Vector3.Distance(transform.position, targetSpot);

        Transform threat = FindBestTarget();
        if (threat != null)
        {
            float distToThreat = Vector3.Distance(transform.position, threat.position);
            float girlToThreat = Vector3.Distance(leader.position, threat.position);

            if (distToThreat <= attackRange)
            {
                _agent.isStopped = true;
                SafeSetFloat(_speedHash, 0f);
                SafeSetBool(_isRunningHash, false);
                RotateTowardsTarget(threat);
                if (_attackTimer <= 0f)
                {
                    StartCoroutine(PerformAttackRoutine());
                }
                return;
            }
            else if (girlToThreat <= 4.5f || distToThreat <= 3.5f)
            {
                _agent.isStopped = false;
                _agent.speed = runSpeed;
                _agent.SetDestination(threat.position);
                SafeSetFloat(_speedHash, runSpeed);
                SafeSetBool(_isRunningHash, true);
                return;
            }
        }

        if (distToSpot <= 1.2f || (distToGirl <= 2.2f && _agent.velocity.magnitude < 0.2f))
        {
            _agent.isStopped = true;
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);

            Vector3 lookDir = leader.forward;
            lookDir.y = 0f;
            if (lookDir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);
            }
        }
        else
        {
            _agent.isStopped = false;
            if (distToGirl > 5.5f)
            {
                _agent.speed = runSpeed;
                SafeSetFloat(_speedHash, runSpeed);
                SafeSetBool(_isRunningHash, true);
            }
            else
            {
                float trotSpeed = 3.2f;
                _agent.speed = trotSpeed;
                SafeSetFloat(_speedHash, trotSpeed);
                SafeSetBool(_isRunningHash, false);
            }

            _agent.SetDestination(targetSpot);
        }
    }

    /// <summary>
    /// Handles Zombie Guarding behavior:
    /// - Close to Girl (<=2.5m): Stands up, walks randomly, stops, idles, walks again.
    /// - Moving with Girl (2.5m - 6m): Stands up and walks alongside her.
    /// - Far from Girl (6m - 16m): Stands up and runs.
    /// - Quite Far from Girl (>16m): Crawls rapidly on all fours to catch up.
    /// - Threat approaching: Crawls if far, stands up when close and attacks!
    /// </summary>
    private void HandleZombieGuardingBehavior(Transform leader)
    {
        float distToGirl = Vector3.Distance(transform.position, leader.position);

        // 1. Check defensive threat near the Girl or Zombie
        Transform threat = FindBestTarget();
        if (threat != null)
        {
            float distToThreat = Vector3.Distance(transform.position, threat.position);
            float girlToThreat = Vector3.Distance(leader.position, threat.position);

            if (girlToThreat <= 14f || distToThreat <= 12f)
            {
                _isGuardIdling = false;
                _guardIdleTimer = 0f;
                HandleZombieThreatEngagement(threat, distToThreat);
                return;
            }
        }

        // 2. No threat: follow / guard the Girl with tiered locomotion
        if (currentPosture == ZombiePosture.StandingUp)
        {
            RotateTowardsTarget(leader);
            return;
        }

        // TIER 1: Quite Far (> crawlTransitionDistance, ~16m) -> Drop to all fours and crawl sprint
        if (distToGirl > crawlTransitionDistance)
        {
            _isGuardIdling = false;
            _guardIdleTimer = 0f;

            DropToCrawl();
            _agent.isStopped = false;
            _agent.speed = crawlSpeed;
            _agent.SetDestination(leader.position);
            PlayZombieCrawlingLocomotion();
            return;
        }

        // TIER 2: Too Far (6m - 16m) -> Stand up and run standing
        if (distToGirl > 6.0f)
        {
            _isGuardIdling = false;
            _guardIdleTimer = 0f;

            if (currentPosture == ZombiePosture.Crawling)
            {
                StartStandUp();
                return;
            }

            _agent.isStopped = false;
            _agent.speed = standRunSpeed;
            _agent.SetDestination(leader.position);
            PlayZombieStandingRunLocomotion();
            return;
        }

        // TIER 3: Moving with her (2.5m - 6.0m) -> Stand up and walk with her
        if (distToGirl > 2.5f)
        {
            _isGuardIdling = false;
            _guardIdleTimer = 0f;

            if (currentPosture == ZombiePosture.Crawling)
            {
                StartStandUp();
                return;
            }

            _agent.isStopped = false;
            _agent.speed = walkSpeed;
            _agent.SetDestination(leader.position);
            PlayZombieStandingWalkLocomotion();
            return;
        }

        // TIER 4: Close with the Girl (<= 2.5m) -> Stand up, wander randomly, stop, idle, repeat
        if (currentPosture == ZombiePosture.Crawling)
        {
            StartStandUp();
            return;
        }

        HandleZombieCloseGuardRoutine(leader);
    }

    private void HandleZombieCloseGuardRoutine(Transform leader)
    {
        if (_isGuardIdling)
        {
            _agent.isStopped = true;
            PlayZombieIdleLocomotion();

            Vector3 lookDir = leader.forward;
            lookDir.y = 0f;
            if (lookDir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 3f);
            }

            _guardIdleTimer -= Time.deltaTime;
            if (_guardIdleTimer <= 0f)
            {
                _isGuardIdling = false;
                PickNewGuardWanderSpot(leader);
            }
        }
        else
        {
            _guardWalkTimer -= Time.deltaTime;
            bool arrived = !_agent.pathPending && (_agent.remainingDistance <= 0.6f || (_agent.remainingDistance == 0f && !_agent.hasPath));

            if (arrived || _guardWalkTimer <= 0f)
            {
                _isGuardIdling = true;
                _guardIdleTimer = Random.Range(2.5f, 5.0f);
                _agent.isStopped = true;
                PlayZombieIdleLocomotion();
            }
            else
            {
                _agent.isStopped = false;
                _agent.speed = walkSpeed * 0.85f;
                PlayZombieStandingWalkLocomotion();
            }
        }
    }

    private void PickNewGuardWanderSpot(Transform leader)
    {
        _guardWalkTimer = Random.Range(3.0f, 6.0f);

        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float randomDist = Random.Range(1.3f, 2.7f);
        Vector3 offset = new Vector3(Mathf.Sin(randomAngle), 0f, Mathf.Cos(randomAngle)) * randomDist;
        Vector3 candidateSpot = leader.position + offset;

        if (NavMesh.SamplePosition(candidateSpot, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
        {
            _guardWanderDestination = hit.position;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_guardWanderDestination);
            }
        }
    }

    private void HandleZombieThreatEngagement(Transform threat, float distToThreat)
    {
        if (distToThreat <= attackRange)
        {
            if (currentPosture == ZombiePosture.Crawling)
            {
                StartStandUp();
                return;
            }

            if (currentPosture == ZombiePosture.StandingUp)
            {
                RotateTowardsTarget(threat);
                return;
            }

            _agent.isStopped = true;
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);
            RotateTowardsTarget(threat);

            if (_attackTimer <= 0f)
            {
                StartCoroutine(PerformAttackRoutine());
            }
            return;
        }

        if (currentPosture == ZombiePosture.Crawling)
        {
            if (distToThreat <= standTransitionDistance)
            {
                StartStandUp();
                return;
            }

            _agent.isStopped = false;
            _agent.speed = crawlSpeed;
            _agent.SetDestination(threat.position);
            PlayZombieCrawlingLocomotion();
        }
        else if (currentPosture == ZombiePosture.StandingUp)
        {
            RotateTowardsTarget(threat);
        }
        else // Standing
        {
            // HYSTERESIS: Do not drop to crawl unless threat escapes quite far
            if (distToThreat > crawlTransitionDistance)
            {
                DropToCrawl();
                _agent.isStopped = false;
                _agent.speed = crawlSpeed;
                _agent.SetDestination(threat.position);
                PlayZombieCrawlingLocomotion();
            }
            else
            {
                _agent.isStopped = false;
                _agent.speed = standRunSpeed;
                _agent.SetDestination(threat.position);
                PlayZombieStandingRunLocomotion();
            }
        }
    }

    private void HandleHuntingBehavior()
    {
        // Periodic smart retargeting tick (every 0.4s)
        _retargetTimer -= Time.deltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = 0.4f;
            target = EvaluateBestTarget(target);
        }

        if (target != null && !IsTargetInvalidOrDead(target))
        {
            float distance = Vector3.Distance(transform.position, target.position);

            if (monsterType == MonsterType.Zombie)
            {
                HandleZombieThreatEngagement(target, distance);
                return;
            }

            // Berserker hunting pursuit
            if (distance <= attackRange)
            {
                _agent.isStopped = true;
                SafeSetFloat(_speedHash, 0f);
                SafeSetBool(_isRunningHash, false);
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

                SafeSetFloat(_speedHash, runSpeed);
                SafeSetBool(_isRunningHash, true);
            }
            return;
        }

        // When no human targets exist or are in range, execute NavMesh roaming!
        target = null;
        ExecuteRoam(walkSpeed);
    }

    private void ExecuteRoam(float patrolSpeed)
    {
        if (monsterType == MonsterType.Zombie && currentPosture == ZombiePosture.StandingUp)
        {
            return;
        }

        if (_isRoamWaiting)
        {
            _agent.isStopped = true;
            if (monsterType == MonsterType.Zombie)
            {
                PlayZombieIdleLocomotion();
            }
            else
            {
                SafeSetFloat(_speedHash, 0f);
                SafeSetBool(_isRunningHash, false);
            }

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
            _roamWaitTimer = Random.Range(2.0f, 4.0f);
            _agent.isStopped = true;
            if (monsterType == MonsterType.Zombie)
            {
                PlayZombieIdleLocomotion();
            }
            else
            {
                SafeSetFloat(_speedHash, 0f);
                SafeSetBool(_isRunningHash, false);
            }
            return;
        }

        _agent.isStopped = false;
        _agent.speed = patrolSpeed;
        if (monsterType == MonsterType.Zombie)
        {
            PlayZombieStandingWalkLocomotion();
        }
        else
        {
            SafeSetFloat(_speedHash, patrolSpeed);
            SafeSetBool(_isRunningHash, false);
        }
    }

    private void PickNewRoamDestination()
    {
        _roamTimer = Random.Range(10f, 16f);
        Vector3 randomDirection = Random.insideUnitSphere * 22f + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit navHit, 15f, NavMesh.AllAreas))
        {
            _roamDestination = navHit.position;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_roamDestination);
            }
        }
    }

    // =========================================================================
    //  Zombie Posture & Locomotion Animation Helpers
    // =========================================================================

    public void StartStandUp()
    {
        if (currentPosture != ZombiePosture.Crawling) return;
        if (_standUpCoroutine != null) StopCoroutine(_standUpCoroutine);
        _standUpCoroutine = StartCoroutine(StandUpRoutine());
    }

    private IEnumerator StandUpRoutine()
    {
        currentPosture = ZombiePosture.StandingUp;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netPosture.Value = ZombiePosture.StandingUp;
        }

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
        }

        SafeSetTrigger(_standUpHash);
        SafeCrossFade(_stateStandUp, "Zombie Stand Up", 0.12f);

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayStandUpClientRpc();
        }

        yield return new WaitForSeconds(standUpDuration);

        currentPosture = ZombiePosture.Standing;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netPosture.Value = ZombiePosture.Standing;
        }

        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;
        }

        _standUpCoroutine = null;
    }

    public void DropToCrawl()
    {
        if (currentPosture == ZombiePosture.Crawling) return;
        if (_standUpCoroutine != null)
        {
            StopCoroutine(_standUpCoroutine);
            _standUpCoroutine = null;
        }

        currentPosture = ZombiePosture.Crawling;
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            netPosture.Value = ZombiePosture.Crawling;
        }

        SafeSetBool(_isStandingHash, false);
        SafeSetBool(_isCrawlingHash, true);
        SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
    }

    [ClientRpc]
    private void PlayStandUpClientRpc()
    {
        if (IsServer) return;
        SafeSetTrigger(_standUpHash);
        SafeCrossFade(_stateStandUp, "Zombie Stand Up", 0.12f);
    }

    private void PlayZombieCrawlingLocomotion()
    {
        SafeSetFloat(_speedHash, crawlSpeed);
        SafeSetBool(_isRunningHash, true);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, false);
        SafeSetBool(_isCrawlingHash, true);
        SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
    }

    private void PlayZombieStandingRunLocomotion()
    {
        SafeSetFloat(_speedHash, standRunSpeed);
        SafeSetBool(_isRunningHash, true);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateRun, "Zombie Run", 0.2f);
    }

    private void PlayZombieStandingWalkLocomotion()
    {
        SafeSetFloat(_speedHash, walkSpeed);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, true);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateWalk, "Zombie Walk", 0.2f);
    }

    private void PlayZombieIdleLocomotion()
    {
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateIdle, "Zombie Idle", 0.25f);
    }

    private IEnumerator PerformAttackRoutine()
    {
        currentState = AIState.Attacking;
        _attackTimer = attackCooldown;

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
        }

        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetTrigger(_attackHash);

        if (monsterType == MonsterType.Zombie)
        {
            SafeCrossFade(_stateZombieNeck, "Zombie Neck", 0.12f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayAttackClientRpc();
        }

        // Enable attack hitbox for the damage window
        if (attackHitbox != null)
        {
            attackHitbox.EnableDamage(attackDamage);
        }
        else
        {
            // Fallback sphere hit check if no physical hitbox component assigned
            CheckDirectAttackHit();
        }

        // Approximate animation strike follow-through duration
        yield return new WaitForSeconds(0.85f);

        if (attackHitbox != null)
        {
            attackHitbox.DisableDamage();
        }

        yield return new WaitForSeconds(0.35f);

        // Resume pursuit if still alive
        if (currentState != AIState.Dead)
        {
            currentState = AIState.Running;
            if (_agent != null && _agent.enabled)
            {
                _agent.isStopped = false;
            }
        }
    }

    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        if (IsServer) return;
        SafeSetTrigger(_attackHash);
        if (monsterType == MonsterType.Zombie)
        {
            SafeCrossFade(_stateZombieNeck, "Zombie Neck", 0.12f);
        }
    }

    private void CheckDirectAttackHit()
    {
        Vector3 strikeOrigin = transform.position + transform.forward * (attackRange * 0.7f) + Vector3.up * 1.0f;
        Collider[] hits = Physics.OverlapSphere(strikeOrigin, 1.4f);

        foreach (var hit in hits)
        {
            if (hit == null || hit.gameObject == gameObject) continue;
            if (IsMonster(hit.gameObject)) continue;
            if (IsGirl(hit.transform.root != null ? hit.transform.root : hit.transform)) continue;

            if (hit.TryGetComponent<IDamageReceiver>(out var receiver) || hit.GetComponentInParent<IDamageReceiver>() is { } pReceiver)
            {
                var r = receiver != null ? receiver : hit.GetComponentInParent<IDamageReceiver>();
                r?.TakeDamage(attackDamage, false);

                if (hit.TryGetComponent<InvestigatorCombatNet>(out var combat) || hit.GetComponentInParent<InvestigatorCombatNet>() is { } pCombat)
                {
                    var c = combat != null ? combat : hit.GetComponentInParent<InvestigatorCombatNet>();
                    c?.PlayHitReaction(Random.Range(0, 2));
                }
                break;
            }
        }
    }

    private static bool IsGirl(Transform t)
    {
        if (t == null) return false;
        if (GameManager.Instance != null && GameManager.Instance.GirlTransform == t) return true;
        if (t.GetComponentInChildren<GirlStealth>() != null || t.GetComponentInParent<GirlStealth>() != null) return true;
        if (t.GetComponentInChildren<NightCrawler.Characters.Girl.GirlPossession>() != null || t.GetComponentInParent<NightCrawler.Characters.Girl.GirlPossession>() != null) return true;
        string n = t.name.ToLower();
        return n.Contains("girl") || (n.Contains("demon") && !n.Contains("monster") && !n.Contains("creep"));
    }

    private static bool IsMonster(GameObject go)
    {
        if (go == null) return false;
        if (go.GetComponentInParent<MonsterAI>() != null || go.GetComponentInParent<MonsterController>() != null) return true;
        string n = go.name.ToLower();
        return n.Contains("monster") || n.Contains("creep") || (n.Contains("demon") && !n.Contains("girl"));
    }

    private void RotateTowardsTarget(Transform t)
    {
        if (t == null) return;
        Vector3 dir = (t.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * turnSpeed);
        }
    }

    /// <summary>
    /// Evaluates if the monster should switch targets.
    /// If an alternative player is significantly closer, aggro switches seamlessly!
    /// </summary>
    private Transform EvaluateBestTarget(Transform current)
    {
        Transform best = FindBestTarget();
        if (best == null) return null;
        if (current == null) return best;

        // If current target died, immediately switch
        if (IsTargetInvalidOrDead(current))
        {
            return best;
        }

        float currentDist = Vector3.Distance(transform.position, current.position);
        float bestDist = Vector3.Distance(transform.position, best.position);

        // Switch only if new target is closer by targetSwitchThreshold to prevent target jitter
        if (bestDist < currentDist - targetSwitchThreshold)
        {
            return best;
        }

        return current;
    }

    private Transform FindBestTarget()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, _searchBuffer);
        Transform closest = null;
        float minDistance = Mathf.Infinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _searchBuffer[i];
            if (col == null || col.gameObject == gameObject) continue;
            if (IsMonster(col.gameObject)) continue;

            // Only target Players
            if (col.CompareTag("Player") || (col.transform.root != null && col.transform.root.CompareTag("Player")))
            {
                Transform candidate = col.transform.root != null ? col.transform.root : col.transform;

                if (IsTargetInvalidOrDead(candidate)) continue;

                float dist = Vector3.Distance(transform.position, candidate.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = candidate;
                }
            }
        }

        return closest;
    }

    private bool IsTargetInvalidOrDead(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return true;

        // Ignore dead corpses
        if (t.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0f))
        {
            return true;
        }
        if (t.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead)
        {
            return true;
        }

        // Monsters must NEVER target their master / summoner (the Girl)
        if (IsGirl(t))
        {
            return true;
        }

        return false;
    }

    // =========================================================================
    //  IDamageReceiver implementation (Receives Axe strikes from Players)
    // =========================================================================

    public void TakeDamage(float amount, bool isSoulAttack = false)
    {
        if (!HasAuthority || currentState == AIState.Dead) return;

        // If TargetHealth is present on the monster, forward damage to it
        if (_targetHealth != null)
        {
            _targetHealth.TakeDamage(amount, isSoulAttack);
        }
        else
        {
            // Direct local fallback if TargetHealth is missing
            if (healthBar != null)
            {
                healthBar.OnDamaged(Mathf.Max(0f, 100f - amount), 100f);
            }
        }

        // Hit reaction animation & stagger
        if (currentState != AIState.Dead)
        {
            PlayHitReaction();
        }

        // Retaliation aggro: If attacked by someone other than current target, switch aggro!
        if (switchTargetOnDamaged)
        {
            Transform attacker = FindClosestAttacker();
            if (attacker != null)
            {
                target = attacker;
            }
        }
    }

    public void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;
        if (_hitCooldownTimer > 0f) return;

        _hitCooldownTimer = 0.25f;
        _hitStaggerTimer = hitReactionDuration;

        if (monsterType == MonsterType.Zombie)
        {
            SafeSetTrigger(_hitHash);
            SafeSetTrigger(_hitReactionHash);
            SafeCrossFade(_stateHitReaction, "Zombie Reaction Hit", 0.08f, true);
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
        if (monsterType == MonsterType.Zombie)
        {
            SafeSetTrigger(_hitHash);
            SafeSetTrigger(_hitReactionHash);
            SafeCrossFade(_stateHitReaction, "Zombie Reaction Hit", 0.08f, true);
        }
    }

    private Transform FindClosestAttacker()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, 12f, _searchBuffer);
        Transform closest = null;
        float minDist = Mathf.Infinity;

        for (int i = 0; i < count; i++)
        {
            Collider c = _searchBuffer[i];
            if (c == null || c.gameObject == gameObject) continue;

            if (c.CompareTag("Player") || (c.transform.root != null && c.transform.root.CompareTag("Player")))
            {
                Transform t = c.transform.root != null ? c.transform.root : c.transform;
                if (!IsTargetInvalidOrDead(t))
                {
                    float d = Vector3.Distance(transform.position, t.position);
                    if (d < minDist)
                    {
                        minDist = d;
                        closest = t;
                    }
                }
            }
        }

        return closest;
    }

    private void Die()
    {
        currentState = AIState.Dead;

        if (_standUpCoroutine != null)
        {
            StopCoroutine(_standUpCoroutine);
            _standUpCoroutine = null;
        }

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
            _agent.enabled = false;
        }

        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        if (attackHitbox != null)
        {
            attackHitbox.DisableDamage();
        }

        if (healthBar != null)
        {
            healthBar.gameObject.SetActive(false);
        }

        TriggerRagdollPhysics();

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayDeathClientRpc();
        }
    }

    private void SetRagdollState(bool active)
    {
        var rbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs)
        {
            if (rb.gameObject == gameObject) continue;
            rb.isKinematic = !active;
            rb.detectCollisions = true;
        }

        var cols = GetComponentsInChildren<Collider>(true);
        foreach (var col in cols)
        {
            if (col.gameObject == gameObject || col.isTrigger) continue;
            col.enabled = true;
        }
    }

    private void TriggerRagdollPhysics()
    {
        // 1. Check for NetworkRagdollController
        if (TryGetComponent<NetworkRagdollController>(out var ragdoll))
        {
            ragdoll.TriggerRagdollDeath();
            return;
        }

        // 2. Disable animator, agent, and root character controller so ragdoll falls freely
        if (_animator != null) _animator.enabled = false;
        if (_agent != null) _agent.enabled = false;
        if (_characterController != null) _characterController.enabled = false;

        // 3. Activate bone Rigidbodies
        var rbs = GetComponentsInChildren<Rigidbody>(true);
        bool foundBones = false;
        foreach (var rb in rbs)
        {
            if (rb.gameObject == gameObject) continue;
            foundBones = true;
            rb.isKinematic = false;
            rb.detectCollisions = true;
        }

        if (foundBones)
        {
            var cols = GetComponentsInChildren<Collider>(true);
            foreach (var col in cols)
            {
                if (col.gameObject != gameObject && !col.isTrigger) col.enabled = true;
            }
        }
        else
        {
            // Fallback to animation if ragdoll components aren't set up yet
            SafeSetTrigger(_dieHash);
        }
    }

    [ClientRpc]
    private void PlayDeathClientRpc()
    {
        if (IsServer) return;
        TriggerRagdollPhysics();
    }

    // =========================================================================
    //  Animator Safety Helpers
    // =========================================================================

    private void CacheAnimatorParameters()
    {
        _animParams.Clear();
        if (_animator == null) _animator = GetComponentInChildren<Animator>();

        if (_animator != null && _animator.parameterCount > 0)
        {
            foreach (var p in _animator.parameters)
            {
                _animParams.Add(p.nameHash);
            }
        }
    }

    private void SafeSetFloat(int hash, float value)
    {
        if (_animator != null && _animParams.Contains(hash))
        {
            _animator.SetFloat(hash, value);
        }
    }

    private void SafeSetBool(int hash, bool value)
    {
        if (_animator != null && _animParams.Contains(hash))
        {
            _animator.SetBool(hash, value);
        }
    }

    private void SafeSetTrigger(int hash)
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_animParams.Contains(hash))
        {
            if (_networkAnimator != null)
            {
                _networkAnimator.SetTrigger(hash);
            }
            else if (_animator != null)
            {
                _animator.SetTrigger(hash);
            }
        }
    }

    private void SafeCrossFade(int stateHash, string stateName, float transitionDuration = 0.15f, bool force = false)
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_animator != null && _animator.isActiveAndEnabled)
        {
            if (_animator.HasState(0, stateHash))
            {
                if (force)
                {
                    _animator.CrossFadeInFixedTime(stateHash, transitionDuration, 0, 0f);
                    return;
                }

                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                var nextStateInfo = _animator.GetNextAnimatorStateInfo(0);

                if (stateInfo.shortNameHash != stateHash && nextStateInfo.shortNameHash != stateHash)
                {
                    _animator.CrossFadeInFixedTime(stateHash, transitionDuration);
                }
            }
        }
    }
}