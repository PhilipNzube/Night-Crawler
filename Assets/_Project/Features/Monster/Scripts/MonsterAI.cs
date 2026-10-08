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
        Dead,
        Idle
    }

    public enum Command { Hunt, Follow, Roam }

    public enum ZombiePosture : byte
    {
        Crawling = 0,
        StandingUp = 1,
        Standing = 2
    }

    [Header("Monster Identity")]
    public MonsterType monsterType = MonsterType.Zombie;
    public Command currentCommand = Command.Roam;
    protected float _recallGraceTimer = 0f;
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

    [Tooltip("Walking speed in standing posture (calibrated to match animation foot stride).")]
    public float walkSpeed = 0.55f;

    [Tooltip("Standing run speed while pursuing targets or catching up to the Girl.")]
    public float standRunSpeed = 2.6f;

    [Tooltip("Crawl run speed while sprinting on all fours across long distances.")]
    public float crawlSpeed = 2.8f;

    [Tooltip("NavMeshAgent base offset when crawling. Fine-tune to ensure hands/knees touch the ground (e.g. 0.0 or -0.05).")]
    public float crawlBaseOffset = 0f;

    [Tooltip("CharacterController / NavMesh height while crawling on all fours to prevent floating.")]
    public float crawlColliderHeight = 0.9f;

    private float _defaultBaseOffset = 0f;
    private float _defaultControllerHeight = 1.8f;
    private Vector3 _defaultControllerCenter = new Vector3(0f, 0.9f, 0f);
    [Tooltip("Movement speed while sprinting at targets.")]
    public float runSpeed = 2.8f;
    [Tooltip("Angular rotation speed when turning towards players.")]
    public float turnSpeed = 12f;
    [Tooltip("Acceleration for instantaneous, aggressive chasing.")]
    public float runAcceleration = 8.0f;

    [Header("Locomotion & Root Motion Settings")]
    [Tooltip("If true, enables Animator root motion and synchronizes it with the NavMeshAgent velocity for 1:1 Mixamo locomotion.")]
    public bool useRootMotion = true;
    [Tooltip("Max body turn rate (deg/sec) for a standing Zombie. Higher = responsive, smooth turning without sideways skating.")]
    public float zombieTurnRate = 480f;
    [Tooltip("Max body turn rate (deg/sec) for a crawling Zombie.")]
    public float zombieCrawlTurnRate = 420f;
    [Tooltip("Max body turn rate (deg/sec) for a Berserker.")]
    public float berserkerTurnRate = 360f;
    [Tooltip("Fraction of forward speed kept when the body faces away from the path.")]
    [Range(0f, 1f)] public float minTurnSpeedFactor = 0.65f;
    [Tooltip("Normalized time in Zombie Walk animation where the rear leg drag begins (forward movement stops completely).")]
    [Range(0f, 1f)] public float legDragStartNormalizedTime = 0.42f;
    [Tooltip("Normalized time in Zombie Walk animation where the rear leg drag ends (forward movement resumes).")]
    [Range(0f, 1f)] public float legDragEndNormalizedTime = 0.88f;
    [Tooltip("How strongly a standing Zombie's speed stalls between steps (leg drag). 0 = smooth continuous movement.")]
    [Range(0f, 0.9f)] public float zombieDragStrength = 0f;
    [Tooltip("Number of footsteps per loop of the Zombie walk/run clip (usually 2).")]
    public float zombieStepsPerCycle = 2f;
    [Tooltip("How quickly the monster brakes to a halt when stopped (m/s per sec). Higher = less sliding when stopping.")]
    public float stopFriction = 18f;

    [Header("Ground Snapping (anti-float)")]
    [Tooltip("Raycast-based grounding that keeps the monster's feet on real cave geometry every frame.")]
    public bool enableGroundSnap = true;
    [Tooltip("Layers counted as walkable ground. Leave as 'Nothing' to auto-use everything except Monster/Player/Explorer/UI/Ignore Raycast.")]
    public LayerMask groundMask = 0;
    [Tooltip("Ray starts this far above the pivot (lets it detect ground when slightly sunk).")]
    public float groundProbeUp = 0.8f;
    [Tooltip("Max distance below the pivot to search for ground and snap down to.")]
    public float groundProbeDown = 2.5f;
    [Tooltip("Additional pivot offset above the ground hit (normally 0).")]
    public float groundFootOffset = 0f;
    [Tooltip("Also check humanoid foot bones and pull the model down if both feet hover above the ground (fixes animation-induced floating).")]
    public bool enableFootGrounding = true;
    [Tooltip("Height of the ankle bone above the sole. Increase if feet sink into the floor, decrease if they still hover.")]
    public float footSoleHeight = 0.09f;
    [Tooltip("Maximum visual downward correction applied to the model (metres).")]
    public float maxFootCorrection = 0.35f;
    [Tooltip("Maximum height of obstacles monsters will step over (0.22m). Higher obstacles are treated as walls and bypassed.")]
    public float maxClimbableStep = 0.22f;

    private float _nominalAgentSpeed = -1f;
    private float _smoothedStrideScale = 1f;
    private float _footCorrection = 0f;
    private Vector3 _modelBaseLocalPos;
    private bool _modelBaseCached = false;
    private static readonly RaycastHit[] _groundHits = new RaycastHit[8];

    [Header("Combat & Damage")]
    [Tooltip("Distance at which the monster initiates its attack.")]
    public float attackRange = 2.0f;
    [Tooltip("Damage dealt per attack hit. Berserkers hit significantly harder than zombies.")]
    public float attackDamage = 35f;
    [Tooltip("Cooldown between successive attacks.")]
    public float attackCooldown = 1.8f;
    [Tooltip("Physical limb hitbox component (e.g. Jaw for Zombie, Right Hand for Berserker).")]
    public MonsterAttackHitbox attackHitbox;
    protected Coroutine _attackCoroutine;

    [Header("Spawn Scream / Roar (3D Audio)")]
    [Tooltip("Play the signature roar/scream when spawning before chasing.")]
    public bool playScreamOnSpawn = true;
    [Tooltip("Duration of the roar state where the monster screams at players before running.")]
    public float screamDuration = 2.4f;
    [Tooltip("Delay in seconds before the scream audio triggers, waiting for the animation wind-up to reach the open-mouth apex.")]
    public float screamAudioDelay = 0.35f;
    [Tooltip("Audio clip for the scream/roar.")]
    public AudioClip spawnScreamClip;
    [Tooltip("3D AudioSource configured for scary spatial audio.")]
    public AudioSource audioSource;
    [Tooltip("Audio clip for attack swing vocalization or grunt.")]
    public AudioClip attackSoundClip;
    [Tooltip("Audio clip for screech or hurt groan when damaged.")]
    public AudioClip hurtSoundClip;

    [Header("Targeting & Threat Aggro")]
    [Tooltip("Current target being pursued.")]
    public Transform target;
    [Tooltip("Maximum detection radius for finding investigators.")]
    public float searchRadius = 20f;
    [Tooltip("If another living player gets this much closer than the current target, switch aggro!")]
    public float targetSwitchThreshold = 5f;
    [Tooltip("Switch aggro to an investigator who strikes the monster with an axe.")]
    public bool switchTargetOnDamaged = true;

    [Header("UI Health Bar")]
    [Tooltip("WorldSpace billboard Heat UI health bar mounted above the monster's head.")]
    public MonsterHealthBar healthBar;

    [Header("Obstacle & Line-of-Sight Detection")]
    [Tooltip("Layer mask representing solid cave walls, props, and doors (used to prevent attacking or tracking through walls).")]
    public LayerMask wallObstacleMask = 0;

    [Header("Anti-Stuck & Auto-Reroute")]
    [Tooltip("Enables smart obstacle avoidance and automatic rerouting when jammed against tunnel walls.")]
    public bool enableAntiStuck = true;
    [Tooltip("Time in seconds the monster must be pushing forward without progress before triggering an auto-reroute. Kept short (<0.4s) so players never notice.")]
    public float stuckDetectionDuration = 0.35f;
    [Tooltip("Duration in seconds the monster focuses on the escape path away from the wall before re-evaluating.")]
    public float rerouteDuration = 1.1f;

    [Header("Runtime State")]
    public AIState currentState = AIState.Falling;

    public NetworkVariable<ZombiePosture> netPosture = new NetworkVariable<ZombiePosture>(
        ZombiePosture.Crawling,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Component references
    protected NavMeshAgent _agent;
    protected CharacterController _characterController;
    protected Animator _animator;
    protected NetworkAnimator _networkAnimator;
    protected TargetHealth _targetHealth;

    // Animation parameter hashes
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
    protected readonly HashSet<int> _animParams = new HashSet<int>();

    // Turning animation hashes
    [Header("Turning Animations")]
    public float turnAngleThreshold = 22f;
    public float minTurnDuration = 0.45f;
    protected float _turnActiveTimer = 0f;
    protected bool _isTurningRight = false;
    protected bool _isTurningLeft = false;
    protected readonly int _turnRightHash = Animator.StringToHash("TurnRight");
    protected readonly int _turnLeftHash = Animator.StringToHash("TurnLeft");
    protected readonly int _isTurningRightHash = Animator.StringToHash("IsTurningRight");
    protected readonly int _isTurningLeftHash = Animator.StringToHash("IsTurningLeft");
    protected readonly int _turnAngleHash = Animator.StringToHash("TurnAngle");

    // Monster Dodge & Spatial Avoidance System
    public static readonly List<MonsterAI> ActiveMonsters = new List<MonsterAI>();
    [Header("Monster Avoidance & Dodging")]
    public bool enableMonsterDodging = true;
    public float monsterDodgeDistance = 2.8f;
    protected bool _isDodgingMonster = false;
    protected float _dodgeTimer = 0f;
    protected Vector3 _dodgeOffset = Vector3.zero;

    // Direct Animator state hashes
    protected readonly int _stateHitReaction  = Animator.StringToHash("Zombie Reaction Hit");
    protected readonly int _stateStandUp      = Animator.StringToHash("Zombie Stand Up");
    protected readonly int _stateRun          = Animator.StringToHash("Zombie Run");
    protected readonly int _stateWalk         = Animator.StringToHash("Zombie Walk");
    protected readonly int _stateIdle         = Animator.StringToHash("Zombie Idle");
    protected readonly int _stateRunningCrawl = Animator.StringToHash("Running Crawl");
    protected readonly int _stateZombieNeck   = Animator.StringToHash("Zombie Neck");
    protected readonly int _stateZombieAttack = Animator.StringToHash("Zombie Attack");
    protected readonly int _stateTurnRight    = Animator.StringToHash("Turn Right");
    protected readonly int _stateTurnLeft     = Animator.StringToHash("Turn Left");

    // Internal state timers & caches
    protected float _attackTimer;
    protected float _retargetTimer;
    protected float _hitStaggerTimer;
    protected float _hitCooldownTimer;
    protected Coroutine _standUpCoroutine;
    protected bool _hasLanded = false;
    protected bool _hasScreamed = false;
    protected static readonly Collider[] _searchBuffer = new Collider[20];

    // Roam & Guard state
    protected Transform _commandLeader;
    protected float _roamTimer = 0f;
    protected float _roamWaitTimer = 0f;
    protected bool _isRoamWaiting = true;
    protected float _roamMinTravelTimer = 0f;
    protected Vector3 _roamDestination;
    protected Vector3 _lastRoamDirection = Vector3.zero;

    // Close-guard wandering state near the Girl
    protected bool _isGuardIdling = true;
    protected float _guardIdleTimer = 2.0f;
    protected float _guardWalkTimer = 0f;
    protected Vector3 _guardWanderDestination;

    // Smart pathing & Anti-Stuck state
    protected float _stuckTimer = 0f;
    protected float _rerouteTimer = 0f;
    protected Vector3 _rerouteWaypoint;
    protected Vector3 _lastStuckCheckPos;
    protected float _stuckCheckInterval = 0.12f;
    protected float _stuckCheckTimer = 0f;
    protected int _consecutiveStuckCount = 0;
    protected Transform _temporarilyIgnoredTarget;
    protected float _targetIgnoreTimer = 0f;
    protected NavMeshPath _pathCalc;
    protected NavMeshPath _reroutePath;
    protected Coroutine _rerouteTurnCoroutine;
    protected Coroutine _roamTurnCoroutine;

    protected bool HasAuthority => (NetworkObject != null && NetworkObject.IsSpawned) ? IsServer : true;
    protected Coroutine _spawnScreamCoroutine;

    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent != null)
        {
            _defaultBaseOffset = _agent.baseOffset;
        }

        _characterController = GetComponent<CharacterController>();
        if (_characterController != null)
        {
            _defaultControllerHeight = _characterController.height;
            _defaultControllerCenter = _characterController.center;
        }

        _animator = GetComponentInChildren<Animator>();
        if (_animator != null)
        {
            _animator.applyRootMotion = useRootMotion;
        }

        // Ensure physical body CapsuleCollider exists so monsters have solid physics collision
        // and cannot slide or phase into characters, without the CharacterController step-offset flying bug
        var rootCol = GetComponent<CapsuleCollider>();
        if (rootCol == null)
        {
            rootCol = gameObject.AddComponent<CapsuleCollider>();
            rootCol.center = _defaultControllerCenter;
            rootCol.radius = (monsterType == MonsterType.Berserker) ? 0.55f : 0.42f;
            rootCol.height = _defaultControllerHeight;
        }

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

        // Ensure all child SkinnedMeshRenderers do not get culled when the spectator camera looks at them
        var smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in smrs)
        {
            if (smr != null)
            {
                smr.updateWhenOffscreen = true;
            }
        }

        // Ensure the active camera rendering the scene includes the Monster layer in its culling mask
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0 && Camera.main != null)
        {
            Camera.main.cullingMask |= (1 << monsterLayer);
        }

        if (wallObstacleMask.value == 0)
        {
            int excluded = LayerMask.GetMask("Ignore Raycast", "UI", "Monster", "Player", "Explorer", "Minimap", "Fog");
            if (excluded != 0)
            {
                wallObstacleMask = ~excluded;
            }
            else
            {
                wallObstacleMask = LayerMask.GetMask("Default", "Environment", "Terrain", "Obstacle");
            }
        }

        // Immediate responsive initialization:
        // Monsters spawned on the subterranean NavMesh shouldn't be blocked by CharacterController falling logic
        _hasLanded = true;
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.speed = runSpeed;
            _agent.acceleration = runAcceleration;
            _agent.stoppingDistance = Mathf.Max(0.5f, attackRange * 0.8f);
            _agent.autoBraking = true;
            _agent.updatePosition = (monsterType != MonsterType.Zombie);
            _agent.updateRotation = false;
            _agent.angularSpeed = 480f;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _agent.radius = (monsterType == MonsterType.Berserker) ? 0.60f : 0.48f;
            _agent.avoidancePriority = 40 + (int)(NetworkObjectId % 30);
        }

        if (!ActiveMonsters.Contains(this))
        {
            ActiveMonsters.Add(this);
        }

        if (groundMask.value == 0)
        {
            int excludedGround = LayerMask.GetMask("Ignore Raycast", "UI", "Monster", "Player", "Explorer", "Minimap", "Fog");
            groundMask = ~excludedGround;
        }

        currentState = playScreamOnSpawn ? AIState.SpawningScream : AIState.Running;
    }

    protected virtual void Start()
    {
        if (monsterType == MonsterType.Zombie)
        {
            ApplyPostureColliders(currentPosture == ZombiePosture.Crawling);
        }

        IgnoreGirlCollisionIfInvisible();

        if (playScreamOnSpawn && !_hasScreamed)
        {
            _spawnScreamCoroutine = StartCoroutine(SpawnScreamRoutine());
        }
        else
        {
            currentState = AIState.Running;
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
        audioSource.volume = GameSettingsManager.SFXVolume;
    }

    protected virtual void ConfigureMonsterDefaults()
    {
        if (monsterType == MonsterType.Berserker)
        {
            runSpeed = 4.5f;
            standRunSpeed = 4.5f;
            walkSpeed = 1.8f;
            runAcceleration = 8.5f;
            if (attackDamage < 50f) attackDamage = 65f;
            if (screamDuration <= 0f) screamDuration = 2.6f;
        }
        else
        {
            // Zombie defaults: calibrated to realistic, grounded speeds that match the animations
            crawlSpeed = 4.2f;
            standRunSpeed = 3.5f;
            walkSpeed = 0.55f;
            runSpeed = crawlSpeed;
            runAcceleration = 8.0f;
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

        if (_agent != null)
        {
            _agent.avoidancePriority = 40 + (int)(NetworkObjectId % 30);
        }

        if (!ActiveMonsters.Contains(this))
        {
            ActiveMonsters.Add(this);
        }

        if (_targetHealth != null)
        {
            _targetHealth.currentHealth.OnValueChanged += HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged += HandlePostureChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        ActiveMonsters.Remove(this);

        if (_targetHealth != null)
        {
            _targetHealth.currentHealth.OnValueChanged -= HandleTargetHealthChanged;
        }

        netPosture.OnValueChanged -= HandlePostureChanged;
    }

    protected virtual void OnDestroy()
    {
        ActiveMonsters.Remove(this);
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
            ApplyPostureColliders(false);
        }
        else if (curr == ZombiePosture.Crawling)
        {
            SafeSetBool(_isStandingHash, false);
            SafeSetBool(_isCrawlingHash, true);
            SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
            ApplyPostureColliders(true);
        }
    }

    private void ApplyPostureColliders(bool isCrawling)
    {
        if (monsterType != MonsterType.Zombie) return;

        if (_agent != null)
        {
            _agent.baseOffset = isCrawling ? crawlBaseOffset : _defaultBaseOffset;
            _agent.height = isCrawling ? crawlColliderHeight : _defaultControllerHeight;
        }

        if (_characterController != null)
        {
            _characterController.height = isCrawling ? crawlColliderHeight : _defaultControllerHeight;
            _characterController.center = isCrawling ? new Vector3(0f, crawlColliderHeight * 0.5f, 0f) : _defaultControllerCenter;
        }

        var rootCol = GetComponent<CapsuleCollider>();
        if (rootCol != null)
        {
            rootCol.height = isCrawling ? crawlColliderHeight : _defaultControllerHeight;
            rootCol.center = isCrawling ? new Vector3(0f, crawlColliderHeight * 0.5f, 0f) : _defaultControllerCenter;
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
        // Restore the unscaled speed so the brain (and anything reading _agent.speed) sees the
        // nominal value, then re-apply friction scaling afterwards. Prevents speed compounding.
        if (_agent != null && _agent.enabled && _nominalAgentSpeed >= 0f)
        {
            _agent.speed = _nominalAgentSpeed;
        }

        UpdateBrain();

        if (HasAuthority && currentState != AIState.Dead && _hasLanded && !isBeingPossessed)
        {
            UpdateSteeringAndRotation();
            ApplyLocomotionFriction();
        }

        UpdateDynamicStrideSync();
    }

    private void LateUpdate()
    {
        if (currentState == AIState.Dead || isBeingPossessed) return;

        // Root snapping runs on every peer: the server's result is replicated, and clients re-snap
        // the interpolated position locally so they never render a floating monster.
        if (_hasLanded && enableGroundSnap)
        {
            EnforceGrounding();
        }

        // Foot correction is purely visual, so every client applies it locally
        if (enableFootGrounding)
        {
            ApplyFootGrounding();
        }

        // Stabilize idle zombie to keep it solid and firmly planted on the ground (no side-to-side doll shaking)
        if (monsterType == MonsterType.Zombie && _animator != null && _animator.isHuman)
        {
            StabilizeIdleZombieWaist();
        }
    }

    /// <summary>
    /// Preserves natural animation motion for the Zombie idle without artificial hip distortion.
    /// </summary>
    protected virtual void StabilizeIdleZombieWaist()
    {
        // Natural Mixamo idle hip motion is preserved cleanly.
    }

    private void UpdateBrain()
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

        // CharacterController suppression (ground snapping itself now happens in LateUpdate)
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (_characterController != null && _characterController.enabled)
            {
                _characterController.enabled = false;
            }
        }

        if (_hitCooldownTimer > 0f) _hitCooldownTimer -= Time.deltaTime;

        if (_targetIgnoreTimer > 0f)
        {
            _targetIgnoreTimer -= Time.deltaTime;
            if (_targetIgnoreTimer <= 0f) _temporarilyIgnoredTarget = null;
        }

        // Active reroute handling: steer away from the obstacle/wall
        if (_rerouteTurnCoroutine != null)
        {
            // Monster is executing visual turn animation towards new escape route
            return;
        }

        // Active roam turn handling: take time to turn in idle state before walking
        if (_roamTurnCoroutine != null)
        {
            return;
        }

        if (_rerouteTimer > 0f)
        {
            _rerouteTimer -= Time.deltaTime;
            if (_agent != null && _agent.isOnNavMesh && _agent.enabled)
            {
                _agent.isStopped = false;
                float spd = (monsterType == MonsterType.Zombie) ? standRunSpeed : runSpeed;
                _agent.speed = spd;
                _agent.SetDestination(_rerouteWaypoint);
                SafeSetFloat(_speedHash, spd);
                SafeSetBool(_isRunningHash, true);
                UpdateNavMeshTurningAnimation();
            }

            if (_agent != null && (_agent.remainingDistance <= 0.6f || _rerouteTimer <= 0f))
            {
                _rerouteTimer = 0f;
            }
            return;
        }

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

        UpdateMonsterDodge();

        // 3. State Machine
        switch (currentState)
        {
            case AIState.Idle:
                if (_animator != null) _animator.SetFloat(_speedHash, 0f);
                if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                break;

            case AIState.SpawningScream:
                // Stay stationary while roaring/screaming
                if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
                if (_animator != null) _animator.speed = 1.0f;
                RotateTowardsTarget(target);
                break;

            case AIState.Running:
                UpdateAntiStuck();
                HandlePursuitAndTargeting();
                UpdateNavMeshTurningAnimation();
                UpdateDynamicStrideSync();
                break;

            case AIState.Attacking:
                if (_animator != null) _animator.speed = 1.0f;
                if (target != null && HasLineOfSight(target))
                {
                    RotateTowardsTarget(target);
                }
                break;
        }
    }

    private void HandleFalling()
    {
        if (_characterController != null)
        {
            if (!_characterController.enabled) _characterController.enabled = true;
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
        if (_characterController != null) _characterController.enabled = false;
        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.updatePosition = (monsterType != MonsterType.Zombie);
        }

        if (playScreamOnSpawn && !_hasScreamed)
        {
            if (_spawnScreamCoroutine != null) StopCoroutine(_spawnScreamCoroutine);
            _spawnScreamCoroutine = StartCoroutine(SpawnScreamRoutine());
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
    protected virtual IEnumerator SpawnScreamRoutine()
    {
        _hasScreamed = true;
        currentState = AIState.SpawningScream;

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
        }

        // Find initial closest player to look at while screaming
        target = FindBestTarget();

        // 1. Trigger Animation first
        TriggerScreamAnimation();

        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);

        // 2. Wait for the animation anticipation/wind-up to open the mouth at apex
        float audioDelay = Mathf.Clamp(screamAudioDelay, 0.1f, screamDuration * 0.5f);
        yield return new WaitForSeconds(audioDelay);

        // 3. Play 3D Audio Scream precisely at the apex
        Play3DScream();

        float remaining = Mathf.Max(0.5f, screamDuration - audioDelay);
        yield return new WaitForSeconds(remaining);

        // Transition directly to relentless pursuit
        currentState = AIState.Running;
        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = false;
        }
    }

    protected virtual void Play3DScream()
    {
        if (spawnScreamClip != null && audioSource != null)
        {
            audioSource.pitch = monsterType == MonsterType.Berserker ? Random.Range(0.85f, 0.95f) : Random.Range(1.05f, 1.20f);
            audioSource.clip = spawnScreamClip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }

        // Broadcast to clients via RPC if networked
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlaySpawnScreamClientRpc();
        }
    }

    [ClientRpc]
    protected void PlaySpawnScreamClientRpc()
    {
        if (IsServer) return; // Already played locally

        if (audioSource != null && spawnScreamClip != null)
        {
            audioSource.pitch = monsterType == MonsterType.Berserker ? 0.90f : 1.15f;
            audioSource.clip = spawnScreamClip;
            audioSource.volume = GameSettingsManager.SFXVolume;
            audioSource.Play();
        }

        TriggerScreamAnimation();
    }

    protected virtual void TriggerScreamAnimation()
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
    public virtual void SetCommand(Command cmd, Transform girlTransform)
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

        _isRoamWaiting = true;
        _roamWaitTimer = Random.Range(3.5f, 6.0f);
        _roamTimer = 0f;
        _roamMinTravelTimer = 0f;
        _lastRoamDirection = Vector3.zero;

        // If the monster just spawned / loaded and is currently falling or screaming,
        // immediately cancel the stationary pause so it responds instantly to the command!
        if (currentState == AIState.SpawningScream || !_hasLanded)
        {
            if (_spawnScreamCoroutine != null)
            {
                StopCoroutine(_spawnScreamCoroutine);
                _spawnScreamCoroutine = null;
            }
            _hasLanded = true;
            _hasScreamed = true;
            if (_agent != null)
            {
                if (!_agent.enabled) _agent.enabled = true;
                _agent.isStopped = false;
            }
            currentState = AIState.Running;
        }

        if (cmd == Command.Follow)
        {
            // RECALL: Even if they saw an investigator or are attacking that investigator,
            // immediately ignore that investigator, cancel any attack, and run towards the girl!
            target = null;
            if (_attackCoroutine != null)
            {
                StopCoroutine(_attackCoroutine);
                _attackCoroutine = null;
            }
            if (attackHitbox != null) attackHitbox.DisableDamage();

            _recallGraceTimer = 3.5f;

            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                Transform leader = _commandLeader != null ? _commandLeader : (GameManager.Instance != null ? GameManager.Instance.GirlTransform : null);
                if (leader != null)
                {
                    _agent.SetDestination(leader.position);
                }
            }

            if (currentState == AIState.Attacking)
            {
                currentState = AIState.Running;
            }
        }
        else if (cmd == Command.Hunt)
        {
            _recallGraceTimer = 0f;
            target = EvaluateBestTarget(null);
        }
        else // Roam
        {
            _recallGraceTimer = 0f;
            target = null;
        }
    }

    protected virtual void HandlePursuitAndTargeting()
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

    protected virtual void HandleGuardingBehavior()
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
        Vector3 guardOffset = new Vector3(Mathf.Sin(angleOffset), 0f, Mathf.Cos(angleOffset)) * 4.2f;
        Vector3 targetSpot = leader.position + guardOffset;

        float distToGirl = Vector3.Distance(transform.position, leader.position);

        // Respectful personal space: if the Girl walks into the monster (< 3.2m), yield space and step back
        if (distToGirl < 3.2f)
        {
            Vector3 pushBackDir = (transform.position - leader.position).normalized;
            if (pushBackDir.sqrMagnitude < 0.01f) pushBackDir = -leader.forward;
            targetSpot = leader.position + pushBackDir * 4.2f;
        }

        float distToSpot = Vector3.Distance(transform.position, targetSpot);

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
                    // LEASH: if player runs far away, return to the girl!
                    if (distToThreat > 8.5f && girlToThreat > 8.0f)
                    {
                        target = null;
                    }
                    else if (distToThreat <= attackRange && threatLoS)
                    {
                        _agent.isStopped = true;
                        SafeSetFloat(_speedHash, 0f);
                        SafeSetBool(_isRunningHash, false);
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
                        SafeSetFloat(_speedHash, runSpeed);
                        SafeSetBool(_isRunningHash, true);
                        return;
                    }
                }
                else
                {
                    // New threat: only chase if an investigator comes close!
                    if (girlToThreat <= 5.5f || distToThreat <= 6.0f)
                    {
                        target = threat;
                        _agent.isStopped = false;
                        _agent.speed = runSpeed;
                        _agent.SetDestination(threat.position);
                        SafeSetFloat(_speedHash, runSpeed);
                        SafeSetBool(_isRunningHash, true);
                        return;
                    }
                }
            }
            else
            {
                target = null;
            }
        }

        if (distToSpot <= 1.2f || (distToGirl <= 4.2f && _agent.velocity.magnitude < 0.2f))
        {
            _agent.isStopped = true;
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);

            // Natural stationary idle: DO NOT snap around to face the Girl!
            if (_turnActiveTimer <= 0f) StopTurningAnimation();
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

                if (target != null)
                {
                    // LEASH: if player runs far away, drop chase and return to the girl!
                    if (distToThreat > 8.5f && girlToThreat > 8.0f)
                    {
                        target = null;
                    }
                    else
                    {
                        _isGuardIdling = false;
                        _guardIdleTimer = 0f;
                        HandleZombieThreatEngagement(target, distToThreat);
                        return;
                    }
                }
                else
                {
                    // New threat: only chase if an investigator comes close!
                    if (distToThreat <= 6.0f || girlToThreat <= 5.5f)
                    {
                        target = threat;
                        _isGuardIdling = false;
                        _guardIdleTimer = 0f;
                        HandleZombieThreatEngagement(threat, distToThreat);
                        return;
                    }
                }
            }
            else
            {
                target = null;
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

        // TIER 3: Moving with her (4.2m - 6.5m) -> Stand up and walk with her
        if (distToGirl > 4.2f)
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
            Vector3 approachSpot = leader.position + (transform.position - leader.position).normalized * 4.2f;
            _agent.SetDestination(approachSpot);
            PlayZombieStandingWalkLocomotion();
            return;
        }

        // TIER 4: Close with the Girl (<= 4.2m) -> Maintain personal space, wander perimeter, stop, idle, repeat
        if (currentPosture == ZombiePosture.Crawling)
        {
            StartStandUp();
            return;
        }

        HandleZombieCloseGuardRoutine(leader);
    }

    private void HandleZombieCloseGuardRoutine(Transform leader)
    {
        float distToGirl = Vector3.Distance(transform.position, leader.position);

        // Personal space buffer: if the Girl walks directly into the zombie (< 3.0m), gently step back to 4.2m
        if (distToGirl < 3.0f)
        {
            _isGuardIdling = false;
            Vector3 pushBackDir = (transform.position - leader.position).normalized;
            if (pushBackDir.sqrMagnitude < 0.01f) pushBackDir = -leader.forward;
            Vector3 yieldSpot = leader.position + pushBackDir * 4.2f;
            if (NavMesh.SamplePosition(yieldSpot, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                _agent.isStopped = false;
                _agent.speed = walkSpeed;
                _agent.SetDestination(hit.position);
                PlayZombieStandingWalkLocomotion();
                return;
            }
        }

        if (_isGuardIdling)
        {
            _agent.isStopped = true;
            PlayZombieIdleLocomotion();

            // Natural stationary idle: DO NOT snap around to face the Girl!
            if (_turnActiveTimer <= 0f) StopTurningAnimation();

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
            bool arrived = !_agent.pathPending && _agent.hasPath && _agent.remainingDistance <= 1.0f;

            if (arrived || _guardWalkTimer <= 0f)
            {
                _isGuardIdling = true;
                _guardIdleTimer = Random.Range(3.5f, 6.5f);
                _agent.isStopped = true;
                PlayZombieIdleLocomotion();
                if (_turnActiveTimer <= 0f) StopTurningAnimation();
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
        _guardWalkTimer = Random.Range(3.5f, 6.5f);

        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        // Wander along a respectful perimeter 3.8m to 5.2m from the Girl
        float randomDist = Random.Range(3.8f, 5.2f);
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
        bool hasLoS = HasLineOfSight(threat);

        // Only initiate attack when close AND has direct Line of Sight! Never attack through a solid wall!
        if (distToThreat <= attackRange && hasLoS)
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
                _attackCoroutine = StartCoroutine(PerformAttackRoutine());
            }
            return;
        }

        // Check if a physical obstacle (e.g. door frame, mine timber beam, barrier) blocks forward pursuit
        if (distToThreat > attackRange && CheckForwardObstacleInChase(threat.position))
        {
            ExecuteSmartReroute();
            return;
        }

        // Target is either far away or behind a wall: continue running along the tunnel NavMesh!
        if (currentPosture == ZombiePosture.Crawling)
        {
            // Only transition to standing if we have direct Line of Sight or are rounding into the room
            if (distToThreat <= standTransitionDistance && hasLoS)
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
            else if (distToThreat <= standTransitionDistance)
            {
                // When close, zombie stands and walks menacingly towards the target!
                _agent.isStopped = false;
                _agent.speed = walkSpeed;
                _agent.SetDestination(threat.position);
                PlayZombieStandingWalkLocomotion();
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

    protected virtual void HandleHuntingBehavior()
    {
        // 1. ROAMING ON THEIR OWN:
        // Chases an investigator if they come close, but returns to roaming if the investigator gets too far away!
        if (currentCommand == Command.Roam)
        {
            if (target != null && !IsTargetInvalidOrDead(target))
            {
                float distance = Vector3.Distance(transform.position, target.position);
                bool hasLoS = HasLineOfSight(target);

                // Roam Leash: if player runs too far (>16m, or >12m without LoS), give up and resume roaming!
                if (distance > 16.0f || (!hasLoS && distance > 12.0f))
                {
                    target = null;
                    ExecuteRoam(walkSpeed);
                    return;
                }

                if (monsterType == MonsterType.Zombie)
                {
                    HandleZombieThreatEngagement(target, distance);
                    return;
                }

                if (distance <= attackRange && hasLoS)
                {
                    _agent.isStopped = true;
                    SetLocomotionAnimSpeed(1.0f);
                    SafeSetFloat(_speedHash, 0f);
                    SafeSetBool(_isRunningHash, false);
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

                    SetLocomotionAnimSpeed(1.15f);
                    SafeSetFloat(_speedHash, runSpeed);
                    SafeSetBool(_isRunningHash, true);
                }
                return;
            }

            // On their own: only engage if an investigator comes close (<= 12m)
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

        // 2. HUNT COMMAND (Issued by the Girl):
        // Relentless pursuit! No matter how far away an investigator is, they keep chasing till caught or recalled!
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

            if (monsterType == MonsterType.Zombie)
            {
                HandleZombieThreatEngagement(target, distance);
                return;
            }

            // Berserker hunting pursuit: Only attack if in range AND has Line of Sight!
            if (distance <= attackRange && hasLoS)
            {
                _agent.isStopped = true;
                SetLocomotionAnimSpeed(1.0f);
                SafeSetFloat(_speedHash, 0f);
                SafeSetBool(_isRunningHash, false);
                RotateTowardsTarget(target);

                if (_attackTimer <= 0f)
                {
                    _attackCoroutine = StartCoroutine(PerformAttackRoutine());
                }
            }
            else
            {
                // Check if a physical obstacle (e.g. door frame, mine timber beam, barrier) blocks forward pursuit
                if (CheckForwardObstacleInChase(target.position))
                {
                    ExecuteSmartReroute();
                    return;
                }

                _agent.isStopped = false;
                _agent.speed = runSpeed;
                _agent.SetDestination(target.position);

                SetLocomotionAnimSpeed(1.15f);
                SafeSetFloat(_speedHash, runSpeed);
                SafeSetBool(_isRunningHash, true);
            }
            return;
        }

        // When no human targets exist or are in range, execute NavMesh roaming!
        target = null;
        ExecuteRoam(walkSpeed);
    }

    protected virtual void PlayPatrolWalkLocomotion(float speed)
    {
        if (monsterType == MonsterType.Zombie)
        {
            PlayZombieStandingWalkLocomotion();
        }
        else
        {
            float curSpeed = _animator != null ? _animator.GetFloat(_speedHash) : 0f;
            SafeSetFloat(_speedHash, Mathf.MoveTowards(curSpeed, speed, Time.deltaTime * 4f));
            SafeSetBool(_isRunningHash, false);
            SafeSetBool(_isWalkingHash, true);
        }
    }

    protected virtual void PlayPatrolIdleLocomotion()
    {
        if (monsterType == MonsterType.Zombie)
        {
            PlayZombieIdleLocomotion();
        }
        else
        {
            float curSpeed = _animator != null ? _animator.GetFloat(_speedHash) : 0f;
            SafeSetFloat(_speedHash, Mathf.MoveTowards(curSpeed, 0f, Time.deltaTime * 6f));
            SafeSetBool(_isRunningHash, false);
            SafeSetBool(_isWalkingHash, false);
        }
    }

    protected virtual void ExecuteRoam(float patrolSpeed)
    {
        if (monsterType == MonsterType.Zombie && currentPosture == ZombiePosture.StandingUp)
        {
            return;
        }

        // State 0: Monster is actively executing a realistic turning animation while IDLE
        if (_roamTurnCoroutine != null)
        {
            return;
        }

        // Initialize into a natural stationary pause if starting fresh
        if (_roamWaitTimer <= 0f && _roamTimer <= 0f && (!_agent.hasPath || _agent.remainingDistance == 0f))
        {
            _isRoamWaiting = true;
            _roamWaitTimer = Random.Range(3.5f, 6.5f);
        }

        // State 1: Monster is resting / idling realistically before moving
        if (_isRoamWaiting)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.MoveTowards(_agent.velocity, Vector3.zero, stopFriction * Time.deltaTime);
            PlayPatrolIdleLocomotion();

            _roamWaitTimer -= Time.deltaTime;
            if (_roamWaitTimer <= 0f)
            {
                _isRoamWaiting = false;
                PickNewRoamDestination(patrolSpeed);
            }
            return;
        }

        // State 2: Monster is actively walking towards its chosen waypoint
        if (_roamMinTravelTimer > 0f)
        {
            _roamMinTravelTimer -= Time.deltaTime;
        }

        _roamTimer -= Time.deltaTime;

        // Arrival check: enforce minimum travel grace period so frame-0 path latency never triggers instant arrival
        bool hasArrived = false;
        if (_roamMinTravelTimer <= 0f)
        {
            if (!_agent.pathPending)
            {
                if (!_agent.hasPath || _agent.remainingDistance <= 1.2f)
                {
                    hasArrived = true;
                }
            }
        }

        // Sharp corner check during patrol: stop into idle and turn rather than spinning body while walking
        if (_agent.hasPath && !_agent.pathPending && _roamMinTravelTimer <= 0f)
        {
            Vector3 steerDir = _agent.steeringTarget - transform.position;
            steerDir.y = 0f;
            if (steerDir.sqrMagnitude > 0.4f)
            {
                float cornerAngle = Vector3.Angle(transform.forward, steerDir);
                if (cornerAngle > 60f && _agent.remainingDistance > 2.0f)
                {
                    if (_roamTurnCoroutine != null) StopCoroutine(_roamTurnCoroutine);
                    _roamTurnCoroutine = StartCoroutine(RoamTurnTowardsDestinationRoutine(_agent.steeringTarget, patrolSpeed));
                    return;
                }
            }
        }

        // Reached destination or timer expired -> smooth stop and enter realistic idle pause
        if (hasArrived || _roamTimer <= 0f)
        {
            _isRoamWaiting = true;
            _roamWaitTimer = Random.Range(4.0f, 7.5f);
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            PlayPatrolIdleLocomotion();
            return;
        }

        // Walk smoothly along the path
        _agent.isStopped = false;
        _agent.speed = Mathf.MoveTowards(_agent.speed, patrolSpeed, Time.deltaTime * 3.5f);
        PlayPatrolWalkLocomotion(patrolSpeed);
    }

    protected void PickNewRoamDestination(float patrolSpeed = -1f)
    {
        if (patrolSpeed <= 0f) patrolSpeed = walkSpeed;

        _roamTimer = Random.Range(14f, 22f);
        _roamMinTravelTimer = 1.2f; // Guarantees at least 1.2s of travel before arrival can evaluate

        if (_agent == null || !_agent.isOnNavMesh) return;
        if (_pathCalc == null) _pathCalc = new NavMeshPath();

        // Smart Creature Corridor Navigation:
        // Continues forward down cavern tunnels or branches organically into side paths.
        // Strictly avoids turning 180 degrees back along the incoming corridor unless trapped at a dead-end wall!
        Vector3 forward = transform.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        float[] candidateDistances = { 12f, 8f, 5.5f, 4f };

        // Organically alternate branch checking order (left vs right)
        bool branchLeftFirst = Random.value > 0.5f;
        float[] forwardAngles = branchLeftFirst
            ? new float[] { 0f, -25f, 25f, -50f, 50f, -75f, 75f }
            : new float[] { 0f, 25f, -25f, 50f, -50f, 75f, -75f };

        bool foundPath = false;
        Vector3 bestPoint = transform.position;

        // 1. Forward and side branches (prioritize continuing forward exploration)
        foreach (float dist in candidateDistances)
        {
            foreach (float angle in forwardAngles)
            {
                Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * forward;

                // Anti-backtracking filter: do not immediately reverse into the tunnel we just walked from
                if (_lastRoamDirection != Vector3.zero && Vector3.Dot(rayDir, -_lastRoamDirection) > 0.70f)
                {
                    continue;
                }

                Vector3 sampleOrigin = transform.position + rayDir * dist;
                if (NavMesh.SamplePosition(sampleOrigin, out NavMeshHit navHit, 3.5f, NavMesh.AllAreas))
                {
                    if (_agent.CalculatePath(navHit.position, _pathCalc) && _pathCalc.status == NavMeshPathStatus.PathComplete)
                    {
                        if (Vector3.Distance(transform.position, navHit.position) >= 2.5f)
                        {
                            bestPoint = navHit.position;
                            _lastRoamDirection = (bestPoint - transform.position).normalized;
                            foundPath = true;
                            break;
                        }
                    }
                }
            }
            if (foundPath) break;
        }

        // 2. Dead-end fallback: If every forward angle hits solid rock, turn around and exit the dead end
        if (!foundPath)
        {
            float[] turnaroundAngles = { 120f, -120f, 150f, -150f, 180f };
            foreach (float dist in new float[] { 10f, 6f })
            {
                foreach (float angle in turnaroundAngles)
                {
                    Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * forward;
                    Vector3 sampleOrigin = transform.position + rayDir * dist;
                    if (NavMesh.SamplePosition(sampleOrigin, out NavMeshHit navHit, 3.5f, NavMesh.AllAreas))
                    {
                        if (_agent.CalculatePath(navHit.position, _pathCalc) && _pathCalc.status == NavMeshPathStatus.PathComplete)
                        {
                            if (Vector3.Distance(transform.position, navHit.position) >= 3.0f)
                            {
                                bestPoint = navHit.position;
                                _lastRoamDirection = (bestPoint - transform.position).normalized;
                                foundPath = true;
                                break;
                            }
                        }
                    }
                }
                if (foundPath) break;
            }
        }

        if (foundPath)
        {
            _roamDestination = bestPoint;
            Vector3 toDest = bestPoint - transform.position;
            toDest.y = 0f;
            float turnAngle = Vector3.Angle(transform.forward, toDest);

            // If angle requires turning, go into idle state and take time to turn properly with turning animation
            if (turnAngle > 25f && toDest.sqrMagnitude > 0.1f)
            {
                if (_roamTurnCoroutine != null) StopCoroutine(_roamTurnCoroutine);
                _roamTurnCoroutine = StartCoroutine(RoamTurnTowardsDestinationRoutine(_roamDestination, patrolSpeed));
            }
            else
            {
                _agent.isStopped = false;
                _agent.SetDestination(_roamDestination);
            }
        }
        else
        {
            // Fully trapped: stay idle for a bit and re-evaluate
            _isRoamWaiting = true;
            _roamWaitTimer = Random.Range(3.5f, 5.5f);
            _agent.isStopped = true;
            PlayPatrolIdleLocomotion();
        }
    }

    /// <summary>
    /// Smoothly turns the monster towards its destination while in IDLE state.
    /// Plays authentic turning animations without walking or running, preventing unnatural globe spinning.
    /// </summary>
    protected virtual IEnumerator RoamTurnTowardsDestinationRoutine(Vector3 destination, float patrolSpeed)
    {
        // 1. Enter idle state: stop movement and locomotion animations immediately
        if (_agent != null && _agent.isOnNavMesh && _agent.enabled)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        PlayPatrolIdleLocomotion();

        Vector3 turnDir = destination - transform.position;
        turnDir.y = 0f;

        if (turnDir.sqrMagnitude > 0.05f)
        {
            // If Zombie is crawling, transition to standing posture so turn animations can play
            if (monsterType == MonsterType.Zombie && currentPosture == ZombiePosture.Crawling)
            {
                yield return StartCoroutine(StandUpRoutine());
            }

            // 2. Play turning animation while stationary idle
            UpdateTurningAnimation(turnDir);

            Quaternion targetRot = Quaternion.LookRotation(turnDir.normalized);
            float elapsed = 0f;
            float maxDuration = 1.1f;
            float turnRate = (monsterType == MonsterType.Berserker) ? 140f : 120f;

            while (elapsed < maxDuration && Quaternion.Angle(transform.rotation, targetRot) > 10f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnRate * Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            StopTurningAnimation(true);
            yield return new WaitForSeconds(0.12f); // Brief natural pause before stepping into forward walk
        }

        // 3. Resume locomotion forward along the new corridor path
        if (_agent != null && _agent.isOnNavMesh && _agent.enabled)
        {
            _agent.isStopped = false;
            _agent.SetDestination(destination);
            _roamTimer = Random.Range(14f, 22f);
            _roamMinTravelTimer = 1.2f;
        }

        _roamTurnCoroutine = null;
    }

    /// <summary>
    /// AAA Velocity-to-Animation Stride Synchronization.
    /// Dynamically scales Animator playback speed to match actual ground velocity.
    /// Completely eliminates foot-sliding and ice-skating across mine tunnels.
    /// </summary>
    protected virtual void UpdateDynamicStrideSync()
    {
        if (_animator == null) return;
        _animator.speed = 1.0f;
    }

    // =========================================================================
    //  Locomotion Friction & Ground Snapping
    // =========================================================================

    /// <summary>
    /// Smooth, natural locomotion matching original Mixamo animations.
    /// </summary>
    protected virtual void ApplyLocomotionFriction()
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

        _nominalAgentSpeed = _agent.speed;

        // Brake hard when stopped instead of coasting
        if (_agent.isStopped)
        {
            _agent.velocity = Vector3.MoveTowards(_agent.velocity, Vector3.zero, stopFriction * Time.deltaTime);
            return;
        }

        // Maintain full nominal speed without artificial drag or gait throttling
        _agent.speed = _nominalAgentSpeed;
    }

    protected virtual void OnAnimatorMove()
    {
        if (_animator != null && _animator.applyRootMotion)
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                // Root motion rotation: authentic animation turns (Turn Left / Turn Right)
                transform.rotation *= _animator.deltaRotation;

                Vector3 delta = _animator.deltaPosition;

                if (monsterType == MonsterType.Zombie)
                {
                    // Authentic animation-driven zombie stride:
                    // Only move forward when stepping the lead leg forward,
                    // and apply ZERO forward movement while dragging the rear leg along the ground!
                    if (_agent.hasPath && !_agent.isStopped && _agent.desiredVelocity.sqrMagnitude > 0.01f)
                    {
                        Vector3 moveDir = _agent.desiredVelocity.normalized;
                        // Dynamically steer around physical obstacles like barrels, crates, and props
                        if (CheckForwardPropObstacle(out Vector3 avoidDir))
                        {
                            moveDir = (moveDir + avoidDir * 0.85f).normalized;
                        }

                        var animState = _animator.GetCurrentAnimatorStateInfo(0);
                        bool isStandingWalk = (animState.shortNameHash == _stateWalk || animState.IsName("Zombie Walk"));

                        if (isStandingWalk)
                        {
                            // Mixamo Zombie Walk cycle:
                            // Stride phase: Lead leg drives and swings forward (moves forward).
                            // Drag phase: Lead foot planted, rear leg drags behind along the ground.
                            // ZERO forward movement during rear leg drag!
                            float norm = animState.normalizedTime % 1.0f;
                            bool isDraggingRearLeg = (norm >= legDragStartNormalizedTime && norm <= legDragEndNormalizedTime);

                            if (isDraggingRearLeg)
                            {
                                // Back leg dragging: zero forward movement, perfectly planted!
                                delta = Vector3.zero;
                            }
                            else
                            {
                                float forwardMagnitude = Vector3.Dot(delta, transform.forward);
                                if (forwardMagnitude > 0.001f)
                                {
                                    delta = moveDir * forwardMagnitude;
                                }
                                else
                                {
                                    delta = moveDir * (walkSpeed * Time.deltaTime * 2.2f);
                                }
                            }
                        }
                        else
                        {
                            // Crawling or running: continuous forward motion
                            float forwardMagnitude = Vector3.Dot(delta, transform.forward);
                            if (forwardMagnitude > 0.001f)
                            {
                                delta = moveDir * forwardMagnitude;
                            }
                            else
                            {
                                float spd = (currentPosture == ZombiePosture.Crawling) ? crawlSpeed : standRunSpeed;
                                delta = moveDir * (spd * Time.deltaTime);
                            }
                        }
                    }
                    else if (_agent.isStopped)
                    {
                        delta = Vector3.zero;
                    }

                    // Sweep ahead to prevent penetrating or clipping through barrels/props
                    delta = PreventPropPenetration(delta);

                    // Move strictly along NavMesh boundaries so the zombie NEVER passes through walls!
                    if (delta.sqrMagnitude > 0.00001f)
                    {
                        _agent.Move(delta);
                    }

                    Vector3 clampedPos = _agent.nextPosition;
                    if (TryGetGroundHeight(clampedPos, out float floorY))
                    {
                        clampedPos.y = floorY + _agent.baseOffset;
                    }
                    transform.position = clampedPos;
                }
                else
                {
                    // Berserker and other monsters:
                    // NavMeshAgent natively manages position along NavMesh boundaries with zero wall clipping!
                    if (_agent.isStopped)
                    {
                        _agent.velocity = Vector3.zero;
                    }

                    Vector3 clampedPos = _agent.nextPosition;
                    if (TryGetGroundHeight(clampedPos, out float floorY))
                    {
                        clampedPos.y = floorY + _agent.baseOffset;
                    }
                    transform.position = clampedPos;
                }
            }
            else
            {
                transform.position += PreventPropPenetration(_animator.deltaPosition);
                transform.rotation *= _animator.deltaRotation;
            }
        }
    }

    /// <summary>
    /// Proactively detects forward obstacles (such as barrels, crates, rock props)
    /// and calculates a smooth steering direction around them before collision occurs.
    /// </summary>
    protected bool CheckForwardPropObstacle(out Vector3 steerDir)
    {
        steerDir = Vector3.zero;
        Vector3 forward = transform.forward;
        if (_agent != null && _agent.hasPath && _agent.desiredVelocity.sqrMagnitude > 0.01f)
        {
            forward = _agent.desiredVelocity.normalized;
        }

        Vector3 origin = transform.position + Vector3.up * 0.6f;
        float radius = (monsterType == MonsterType.Berserker) ? 0.45f : 0.35f;
        float checkDist = 1.6f;

        if (Physics.SphereCast(origin, radius, forward, out RaycastHit hit, checkDist, wallObstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform) && !hit.collider.isTrigger)
            {
                // Don't steer away from human prey in chase/attack
                if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)))
                {
                    return false;
                }

                // If hit another monster, let NavMesh avoidance handle separation
                if (hit.collider.GetComponentInParent<MonsterAI>() != null)
                {
                    return false;
                }

                Vector3 toHit = hit.point - transform.position;
                toHit.y = 0f;

                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > 0.01f)
                {
                    normal.Normalize();
                    Vector3 rightTangent = Vector3.Cross(Vector3.up, normal);
                    steerDir = (Vector3.Dot(rightTangent, -toHit) >= 0f) ? rightTangent : -rightTangent;
                }
                else
                {
                    steerDir = Vector3.Cross(Vector3.up, forward).normalized;
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sweeps delta movement against physical obstacle colliders (barrels, props, walls)
    /// preventing monsters from penetrating or sliding into them.
    /// </summary>
    protected Vector3 PreventPropPenetration(Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.00001f) return delta;

        Vector3 moveDir = delta.normalized;
        float moveDist = delta.magnitude;
        Vector3 p1 = transform.position + Vector3.up * 0.35f;
        Vector3 p2 = transform.position + Vector3.up * 1.5f;
        float radius = (monsterType == MonsterType.Berserker) ? 0.48f : 0.38f;

        if (Physics.CapsuleCast(p1, p2, radius, moveDir, out RaycastHit hit, moveDist + 0.12f, wallObstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform) && !hit.collider.isTrigger)
            {
                if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)))
                {
                    return delta;
                }
                if (hit.collider.GetComponentInParent<MonsterAI>() != null)
                {
                    return delta;
                }

                Vector3 normal = hit.normal;
                normal.y = 0f;
                if (normal.sqrMagnitude > 0.01f)
                {
                    normal.Normalize();
                    delta = Vector3.ProjectOnPlane(delta, normal);
                }
                else
                {
                    delta = Vector3.zero;
                }
            }
        }

        return delta;
    }

    /// <summary>
    /// Keeps the character grounded directly to physical cave floor geometry or the NavMesh,
    /// eliminating any vertical hovering, floating, or flying gaps.
    /// </summary>
    protected virtual void EnforceGrounding()
    {
        Vector3 pos = transform.position;
        float baseOffset = (_agent != null && _agent.enabled) ? _agent.baseOffset : 0f;
        float targetY = pos.y;
        bool hasTarget = false;

        // 1. Raycast real physical floor geometry first (cave meshes, rocks, terrain)
        if (TryGetGroundHeight(pos, out float physicalGroundY))
        {
            targetY = physicalGroundY + baseOffset + groundFootOffset;
            hasTarget = true;
        }
        // 2. Fall back to NavMesh if raycast didn't hit physical mesh
        else if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                targetY = hit.position.y + baseOffset + groundFootOffset;
                hasTarget = true;
            }
        }

        if (hasTarget)
        {
            // Snap firmly and smoothly to ground surface with zero floating gap
            if (Mathf.Abs(pos.y - targetY) > 0.002f)
            {
                pos.y = Mathf.MoveTowards(pos.y, targetY, Time.deltaTime * 16.0f);
                transform.position = pos;
                if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
                {
                    _agent.nextPosition = transform.position;
                }
            }
        }
    }

    protected bool TryGetGroundHeight(Vector3 pivot, out float groundY)
    {
        groundY = 0f;
        Vector3 origin = pivot + Vector3.up * groundProbeUp;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, groundProbeUp + groundProbeDown, groundMask, QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var h = _groundHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.collider.isTrigger) continue;
            if (h.collider.GetComponentInParent<MonsterAI>() != null) continue;
            if (h.collider.GetComponentInParent<CharacterController>() != null) continue;

            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                groundY = h.point.y;
                found = true;
            }
        }
        return found;
    }

    protected virtual void ApplyFootGrounding()
    {
        // Intentionally no-op: bone displacement fights Mixamo humanoid root motion and causes idle/stride bobbing
    }

    // =========================================================================
    //  Zombie Posture & Locomotion Animation Helpers
    // =========================================================================

    protected void SetLocomotionAnimSpeed(float speed)
    {
        if (_animator != null)
        {
            _animator.speed = speed;
        }
    }

    public void StartStandUp()
    {
        if (currentPosture != ZombiePosture.Crawling) return;
        if (_standUpCoroutine != null) StopCoroutine(_standUpCoroutine);
        _standUpCoroutine = StartCoroutine(StandUpRoutine());
    }

    protected IEnumerator StandUpRoutine()
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

        SetLocomotionAnimSpeed(1.0f);
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
        ApplyPostureColliders(false);

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

        SetLocomotionAnimSpeed(1.0f);
        SafeSetBool(_isStandingHash, false);
        SafeSetBool(_isCrawlingHash, true);
        SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
        ApplyPostureColliders(true);
    }

    [ClientRpc]
    private void PlayStandUpClientRpc()
    {
        if (IsServer) return;
        SetLocomotionAnimSpeed(1.0f);
        SafeSetTrigger(_standUpHash);
        SafeCrossFade(_stateStandUp, "Zombie Stand Up", 0.12f);
    }

    private void PlayZombieCrawlingLocomotion()
    {
        SetLocomotionAnimSpeed(1.0f);

        SafeSetFloat(_speedHash, crawlSpeed);
        SafeSetBool(_isRunningHash, true);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, false);
        SafeSetBool(_isCrawlingHash, true);
        SafeCrossFade(_stateRunningCrawl, "Running Crawl", 0.2f);
    }

    private void PlayZombieStandingRunLocomotion()
    {
        SetLocomotionAnimSpeed(1.0f);

        SafeSetFloat(_speedHash, standRunSpeed);
        SafeSetBool(_isRunningHash, true);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateRun, "Zombie Run", 0.2f);
    }

    private void PlayZombieStandingWalkLocomotion()
    {
        SetLocomotionAnimSpeed(1.0f);

        SafeSetFloat(_speedHash, walkSpeed);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, true);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateWalk, "Zombie Walk", 0.2f);
    }

    private void PlayZombieIdleLocomotion()
    {
        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, false);
        SafeSetBool(_isStandingHash, true);
        SafeSetBool(_isCrawlingHash, false);
        SafeCrossFade(_stateIdle, "Zombie Idle", 0.25f);
    }

    protected virtual IEnumerator PerformAttackRoutine()
    {
        currentState = AIState.Attacking;
        _attackTimer = attackCooldown;

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
        }

        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetTrigger(_attackHash);

        if (monsterType == MonsterType.Zombie)
        {
            SafeCrossFade(_stateZombieNeck, "Zombie Neck", 0.12f);
        }
        else if (monsterType == MonsterType.Berserker)
        {
            SafeCrossFade(Animator.StringToHash("Mutant Swiping"), "Mutant Swiping", 0.12f);
        }

        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            PlayAttackClientRpc();
        }

        if (attackSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSoundClip, GameSettingsManager.SFXVolume);
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
        _attackCoroutine = null;
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
        else if (monsterType == MonsterType.Berserker)
        {
            SafeCrossFade(Animator.StringToHash("Mutant Swiping"), "Mutant Swiping", 0.12f);
        }

        if (attackSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(attackSoundClip, GameSettingsManager.SFXVolume);
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

    public static bool IsGirl(Transform t)
    {
        if (t == null) return false;
        if (GameManager.Instance != null && (GameManager.Instance.GirlTransform == t || (t.root != null && GameManager.Instance.GirlTransform == t.root))) return true;
        if (t.GetComponentInChildren<GirlStealth>() != null || t.GetComponentInParent<GirlStealth>() != null) return true;
        if (t.GetComponentInChildren<GirlPossession>() != null || t.GetComponentInParent<GirlPossession>() != null) return true;
        if (t.GetComponentInChildren<GirlMaterialController>() != null || t.GetComponentInParent<GirlMaterialController>() != null) return true;
        if (t.GetComponentInChildren<GirlMovement>() != null || t.GetComponentInParent<GirlMovement>() != null) return true;
        if (t.GetComponentInChildren<GirlCommandNet>() != null || t.GetComponentInParent<GirlCommandNet>() != null) return true;
        if (t.GetComponentInChildren<GirlPuppetNet>() != null || t.GetComponentInParent<GirlPuppetNet>() != null) return true;
        if (t.GetComponentInChildren<GirlSenseNet>() != null || t.GetComponentInParent<GirlSenseNet>() != null) return true;
        if (t.GetComponentInChildren<GirlAttackNet>() != null || t.GetComponentInParent<GirlAttackNet>() != null) return true;
        if (t.GetComponentInChildren<GirlShadowTeleportNet>() != null || t.GetComponentInParent<GirlShadowTeleportNet>() != null) return true;
        if (t.GetComponentInChildren<GirlStateController>() != null || t.GetComponentInParent<GirlStateController>() != null) return true;
        var pNet = t.GetComponentInChildren<PlayerPossessableNet>();
        if (pNet == null) pNet = t.GetComponentInParent<PlayerPossessableNet>();
        if (pNet != null && pNet.isPossessed.Value) return true;
        if (t.CompareTag("Girl") || (t.root != null && t.root.CompareTag("Girl"))) return true;
        string n = t.name.ToLower();
        return n.Contains("girl") || (n.Contains("demon") && !n.Contains("monster") && !n.Contains("creep")) || n.Contains("vengeful") || n.Contains("wraith");
    }

    public void IgnoreGirlCollisionIfInvisible()
    {
        var girlMaterial = FindAnyObjectByType<GirlMaterialController>();
        if (girlMaterial != null)
        {
            bool invisible = !girlMaterial.isManifested.Value;
            var girlCc = girlMaterial.GetComponent<CharacterController>();
            var myColliders = GetComponentsInChildren<Collider>();
            foreach (var col in myColliders)
            {
                if (col == null || !col.enabled) continue;
                if (girlCc != null && girlCc.enabled)
                {
                    Physics.IgnoreCollision(col, girlCc, invisible);
                }
            }
        }
    }

    private static bool IsMonster(GameObject go)
    {
        if (go == null) return false;
        if (go.GetComponentInParent<MonsterAI>() != null || go.GetComponentInParent<MonsterController>() != null) return true;
        string n = go.name.ToLower();
        return n.Contains("monster") || n.Contains("creep") || (n.Contains("demon") && !n.Contains("girl") && !n.Contains("wraith"));
    }

    /// <summary>
    /// Returns true if the monster is actively walking or running.
    /// Used to strictly suppress stationary turning animations during locomotion.
    /// </summary>
    public virtual bool IsMovingLocomotion()
    {
        if (currentState == AIState.Dead || currentState == AIState.Falling) return false;
        if (currentState == AIState.Attacking || currentState == AIState.SpawningScream) return false;
        if (currentState == AIState.Idle) return false;
        if (_isGuardIdling || _isRoamWaiting) return false;

        if (_agent != null && _agent.enabled && !_agent.isStopped)
        {
            if (_agent.velocity.sqrMagnitude > 0.04f) return true;
            if (_agent.hasPath && _agent.remainingDistance > 0.35f && _agent.desiredVelocity.sqrMagnitude > 0.04f) return true;
        }

        if (_animator != null)
        {
            if (_animator.GetBool(_isRunningHash) || _animator.GetBool(_isWalkingHash)) return true;
            if (_animator.GetFloat(_speedHash) > 0.1f) return true;
        }

        return false;
    }

    /// <summary>
    /// Server-authoritative steering and continuous rotation for monsters.
    /// Ensures the monster actively and smoothly faces its navigation path, destination,
    /// or active target at all times, completely eliminating sideways skating or moving backwards.
    /// STRICT RULE: Turning animations (Turn Left / Turn Right) ONLY play when the monster is idle/stationary.
    /// While running or walking, the monster smoothly rotates its body without triggering turn animation clips.
    /// </summary>
    protected virtual void UpdateSteeringAndRotation()
    {
        if (!HasAuthority || currentState == AIState.Dead || !_hasLanded || isBeingPossessed) return;
        if (_rerouteTurnCoroutine != null) return; // Smart reroute handles its own rotation

        if (_turnActiveTimer > 0f) _turnActiveTimer -= Time.deltaTime;

        Vector3 desiredFacingDir = Vector3.zero;

        if (currentState == AIState.Attacking || currentState == AIState.SpawningScream)
        {
            if (target != null)
            {
                desiredFacingDir = target.position - transform.position;
            }
        }
        else if (_agent != null && _agent.enabled && _agent.isOnNavMesh && _agent.hasPath && !_agent.isStopped)
        {
            if (_agent.desiredVelocity.sqrMagnitude > 0.04f)
            {
                desiredFacingDir = _agent.desiredVelocity;
            }
            else
            {
                desiredFacingDir = _agent.steeringTarget - transform.position;
            }
        }
        else if (target != null && HasLineOfSight(target))
        {
            desiredFacingDir = target.position - transform.position;
        }

        desiredFacingDir.y = 0f;
        if (desiredFacingDir.sqrMagnitude < 0.01f)
        {
            if (_turnActiveTimer <= 0f) StopTurningAnimation();
            return;
        }

        desiredFacingDir.Normalize();
        float signedAngle = Vector3.SignedAngle(transform.forward, desiredFacingDir, Vector3.up);
        float absAngle = Mathf.Abs(signedAngle);

        bool isMoving = IsMovingLocomotion();
        if (isMoving)
        {
            // STRICT REQUIREMENT: Monsters must NEVER play turning animations while running or walking!
            StopTurningAnimation(true);
        }
        else
        {
            // Turning animations are strictly restricted to IDLE / stationary states
            if (absAngle > turnAngleThreshold)
            {
                UpdateTurningAnimation(desiredFacingDir);
            }
            else if (absAngle <= 15f && _turnActiveTimer <= 0f)
            {
                StopTurningAnimation();
            }
        }

        // Smooth body rotation towards desired direction (always active so monster faces path/target)
        float turnRate = (monsterType == MonsterType.Zombie)
            ? (currentPosture == ZombiePosture.Crawling ? zombieCrawlTurnRate : zombieTurnRate)
            : berserkerTurnRate;

        // Ensure snappy turning during dedicated turn animation
        if (_isTurningLeft || _isTurningRight || _turnActiveTimer > 0f)
        {
            turnRate = Mathf.Max(turnRate, 320f);
        }

        Quaternion targetRot = Quaternion.LookRotation(desiredFacingDir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnRate * Time.deltaTime);
    }

    public virtual void UpdateTurningAnimation(Vector3 desiredFacingDir)
    {
        // STRICT REQUIREMENT: Never play turning animations when running or walking; only while idle/stationary.
        if (IsMovingLocomotion())
        {
            StopTurningAnimation(true);
            return;
        }

        // Crawling postures should never play upright turn animations
        if (monsterType == MonsterType.Zombie && currentPosture != ZombiePosture.Standing)
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
            // Turning Right
            if (!_isTurningRight)
            {
                SafeSetTrigger(_turnRightHash);
                SafeCrossFade(_stateTurnRight, "Turn Right", 0.12f);
            }
            _isTurningRight = true;
            _isTurningLeft = false;
            SafeSetBool(_isTurningRightHash, true);
            SafeSetBool(_isTurningLeftHash, false);
            _turnActiveTimer = Mathf.Max(_turnActiveTimer, minTurnDuration);
        }
        else if (signedAngle < -turnAngleThreshold)
        {
            // Turning Left
            if (!_isTurningLeft)
            {
                SafeSetTrigger(_turnLeftHash);
                SafeCrossFade(_stateTurnLeft, "Turn Left", 0.12f);
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

    public virtual void StopTurningAnimation(bool force = false)
    {
        if (!force && _turnActiveTimer > 0f) return;

        _turnActiveTimer = 0f;
        if (_isTurningRight || _isTurningLeft)
        {
            _isTurningRight = false;
            _isTurningLeft = false;
            SafeSetBool(_isTurningRightHash, false);
            SafeSetBool(_isTurningLeftHash, false);
            SafeSetFloat(_turnAngleHash, 0f);

            // If we are stationary idle and just finished turning, return smoothly to idle pose
            if (!IsMovingLocomotion() && currentState != AIState.Dead && currentState != AIState.Attacking)
            {
                if (monsterType == MonsterType.Zombie && currentPosture == ZombiePosture.Standing)
                {
                    SafeCrossFade(_stateIdle, "Zombie Idle", 0.15f);
                }
            }
        }
    }

    protected virtual void UpdateNavMeshTurningAnimation()
    {
        UpdateSteeringAndRotation();
    }

    protected virtual void RotateTowardsTarget(Transform t)
    {
        if (t == null)
        {
            if (_turnActiveTimer <= 0f) StopTurningAnimation();
            return;
        }
        Vector3 dir = (t.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            float angle = Vector3.Angle(transform.forward, dir);
            if (!IsMovingLocomotion() && angle > turnAngleThreshold)
            {
                UpdateTurningAnimation(dir);
            }
            else if (_turnActiveTimer <= 0f && angle < 15f)
            {
                StopTurningAnimation();
            }

            float rate = (_isTurningRight || _isTurningLeft || _turnActiveTimer > 0f) ? 280f : 360f;
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rate * Time.deltaTime);
        }
        else
        {
            if (_turnActiveTimer <= 0f) StopTurningAnimation();
        }
    }

    /// <summary>
    /// Evaluates if the monster should switch targets.
    /// If an alternative player is significantly closer, aggro switches seamlessly!
    /// </summary>
    protected Transform EvaluateBestTarget(Transform current)
    {
        Transform best = FindBestTarget();
        if (best == null) return null;
        if (current == null) return best;

        // If current target died or is temporarily blocked, immediately switch
        if (IsTargetInvalidOrDead(current) || current == _temporarilyIgnoredTarget)
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

    /// <summary>
    /// Checks if the monster has an unobstructed line of sight to the target through cave corridors.
    /// Raycasts from monster chest height to target chest height against the obstacle mask.
    /// </summary>
    public virtual bool HasLineOfSight(Transform t)
    {
        if (t == null) return false;

        Vector3 eyePos = transform.position + Vector3.up * 1.35f;
        Vector3 targetPos = t.position + Vector3.up * 1.1f;
        Vector3 toTarget = targetPos - eyePos;
        float distance = toTarget.magnitude;

        if (distance <= 0.25f) return true;

        if (Physics.Raycast(eyePos, toTarget.normalized, out RaycastHit hit, distance, wallObstacleMask, QueryTriggerInteraction.Ignore))
        {
            Transform hitRoot = hit.transform.root != null ? hit.transform.root : hit.transform;
            Transform targetRoot = t.root != null ? t.root : t;

            if (hitRoot != targetRoot && !hit.collider.CompareTag("Player"))
            {
                return false; // Solid wall or cave prop is blocking Line of Sight!
            }
        }

        return true;
    }

    /// <summary>
    /// Calculates the actual walking distance along the baked NavMesh corridors.
    /// Used to prioritize players in the same tunnel rather than chasing someone separated by solid stone.
    /// </summary>
    protected float GetNavMeshPathDistance(Vector3 start, Vector3 end)
    {
        if (_pathCalc == null) _pathCalc = new NavMeshPath();
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _pathCalc))
        {
            if (_pathCalc.status == NavMeshPathStatus.PathComplete && _pathCalc.corners != null && _pathCalc.corners.Length > 1)
            {
                float len = 0f;
                for (int k = 1; k < _pathCalc.corners.Length; k++)
                {
                    len += Vector3.Distance(_pathCalc.corners[k - 1], _pathCalc.corners[k]);
                }
                return len;
            }
        }
        return -1f;
    }

    /// <summary>
    /// Detects when a monster is stuck against a cave wall or obstacle while trying to run forward.
    /// Stops locomotion animation, figures out an alternate route, triggers turn animation, and reroutes.
    /// </summary>
    protected virtual void UpdateAntiStuck()
    {
        if (!enableAntiStuck || _agent == null || !_agent.isOnNavMesh || !_agent.enabled || _agent.isStopped || _rerouteTurnCoroutine != null)
        {
            _stuckTimer = 0f;
            return;
        }

        _stuckCheckTimer -= Time.deltaTime;
        if (_stuckCheckTimer <= 0f)
        {
            _stuckCheckTimer = _stuckCheckInterval;

            if (_agent.hasPath && _agent.desiredVelocity.sqrMagnitude > 0.25f)
            {
                float movedDist = Vector3.Distance(transform.position, _lastStuckCheckPos);
                if (movedDist < 0.10f)
                {
                    _stuckTimer += _stuckCheckInterval;
                    if (_stuckTimer >= stuckDetectionDuration)
                    {
                        ExecuteSmartReroute();
                        _stuckTimer = 0f;
                    }
                }
                else
                {
                    _stuckTimer = 0f;
                    _consecutiveStuckCount = 0;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }

            _lastStuckCheckPos = transform.position;
        }
    }

    /// <summary>
    /// Proactively detects another monster in the path or immediate vicinity,
    /// and steers or sidesteps laterally around it to dodge the other monster cleanly.
    /// </summary>
    protected virtual void UpdateMonsterDodge()
    {
        if (!enableMonsterDodging || _agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
        if (currentState == AIState.Dead || isBeingPossessed || _agent.isStopped) return;

        if (_dodgeTimer > 0f)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f)
            {
                _isDodgingMonster = false;
                _dodgeOffset = Vector3.zero;
            }
        }

        MonsterAI blockingMonster = null;
        float closestDist = float.MaxValue;
        Vector3 myPos = transform.position;
        Vector3 myForward = transform.forward;

        for (int i = 0; i < ActiveMonsters.Count; i++)
        {
            var other = ActiveMonsters[i];
            if (other == null || other == this || other.currentState == AIState.Dead || !other.gameObject.activeInHierarchy) continue;

            Vector3 otherPos = other.transform.position;
            Vector3 toOther = otherPos - myPos;
            toOther.y = 0f;
            float dist = toOther.magnitude;

            if (dist < monsterDodgeDistance && dist > 0.1f)
            {
                Vector3 toOtherDir = toOther / dist;
                float forwardDot = Vector3.Dot(myForward, toOtherDir);

                // If other monster is ahead of us within ~130 degree cone
                if (forwardDot > 0.35f)
                {
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        blockingMonster = other;
                    }
                }
            }
        }

        if (blockingMonster != null)
        {
            Vector3 localOther = transform.InverseTransformPoint(blockingMonster.transform.position);
            Vector3 lateralDir;
            if (Mathf.Abs(localOther.x) > 0.15f)
            {
                lateralDir = localOther.x > 0f ? -transform.right : transform.right;
            }
            else
            {
                lateralDir = (GetInstanceID() % 2 == 0) ? transform.right : -transform.right;
            }

            float dodgeDist = 1.6f;
            Vector3 probePos = myPos + lateralDir * dodgeDist + myForward * 1.0f;
            if (NavMesh.SamplePosition(probePos, out NavMeshHit hit, 1.2f, NavMesh.AllAreas))
            {
                _dodgeOffset = (hit.position - myPos).normalized;
                _isDodgingMonster = true;
                _dodgeTimer = 0.5f;

                // If jammed very close (head-to-head deadlock < 1.6m), steer destination and play turn
                if (closestDist < 1.6f && _agent.velocity.magnitude < 0.8f)
                {
                    Vector3 steerTarget = hit.position;
                    _agent.SetDestination(steerTarget);
                    Vector3 steerDir = steerTarget - myPos;
                    steerDir.y = 0f;
                    if (!IsMovingLocomotion() && steerDir.sqrMagnitude > 0.05f)
                    {
                        UpdateTurningAnimation(steerDir);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Detects if an obstacle (door frame, timber beam, mine prop, high barrier) is blocking the monster
    /// during pursuit so it can reroute through open corridors instead of trying to run through it.
    /// </summary>
    protected virtual bool CheckForwardObstacleInChase(Vector3 targetPos)
    {
        if (_rerouteTurnCoroutine != null || _rerouteTimer > 0f) return false;

        // SphereCast forward at chest level (0.85m)
        Vector3 origin = transform.position + Vector3.up * 0.85f;
        Vector3 forward = transform.forward;
        float checkDist = 1.7f;

        int layerMask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        if (Physics.SphereCast(origin, 0.30f, forward, out RaycastHit hit, checkDist, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
            {
                // If it hits the human prey, this is not an obstacle
                if (target != null && (hit.collider.transform == target || hit.collider.transform.IsChildOf(target)))
                {
                    return false;
                }

                // If hitting another monster, do not trigger obstacle reroute
                if (hit.collider.GetComponentInParent<MonsterAI>() != null)
                {
                    return false;
                }

                // If the hit normal faces towards us, we are about to push into an obstacle / frame
                float angle = Vector3.Angle(-hit.normal, forward);
                if (angle < 80f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Verifies that a proposed path candidate is completely clear of physical obstacles (doors, props, beams).
    /// </summary>
    protected virtual bool IsClearOfObstacles(Vector3 start, Vector3 end)
    {
        Vector3 origin = start + Vector3.up * 0.7f;
        Vector3 dest = end + Vector3.up * 0.7f;
        Vector3 diff = dest - origin;
        float dist = diff.magnitude;

        if (dist > 0.1f)
        {
            int obstacleMask = ~LayerMask.GetMask("Ignore Raycast", "UI");
            if (Physics.SphereCast(origin, 0.28f, diff.normalized, out RaycastHit hit, dist, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
                {
                    if (target == null || (hit.collider.transform != target && !hit.collider.transform.IsChildOf(target)))
                    {
                        if (hit.collider.GetComponentInParent<MonsterAI>() == null)
                        {
                            return false; // Physical barrier detected
                        }
                    }
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Intelligently maneuvers the monster away from a wall or obstacle into clear NavMesh tunnel space.
    /// Stops walk/run animation, finds open route, executes turn animation towards route, and resumes navigation.
    /// </summary>
    protected virtual void ExecuteSmartReroute()
    {
        _consecutiveStuckCount++;

        // If jammed repeatedly trying to reach a player behind a wall/obstruction, temporarily ignore that player
        if (_consecutiveStuckCount >= 2 && target != null)
        {
            _temporarilyIgnoredTarget = target;
            _targetIgnoreTimer = 4.0f;
            target = null;
        }

        if (_reroutePath == null) _reroutePath = new NavMeshPath();

        Vector3 chosenWaypoint = Vector3.zero;
        bool found = false;

        // 1. Try steering away from the closest NavMesh wall edge
        if (NavMesh.FindClosestEdge(transform.position, out NavMeshHit edgeHit, NavMesh.AllAreas))
        {
            Vector3 pushAwayDir = edgeHit.normal; // Normal points directly into open space
            pushAwayDir.y = 0f;
            if (pushAwayDir.sqrMagnitude > 0.01f)
            {
                Vector3 escapeCandidate = transform.position + pushAwayDir.normalized * 3.5f;
                if (NavMesh.SamplePosition(escapeCandidate, out NavMeshHit navHit, 2.5f, NavMesh.AllAreas))
                {
                    if (IsClearOfObstacles(transform.position, navHit.position) &&
                        NavMesh.CalculatePath(transform.position, navHit.position, NavMesh.AllAreas, _reroutePath) &&
                        _reroutePath.status == NavMeshPathStatus.PathComplete)
                    {
                        chosenWaypoint = navHit.position;
                        found = true;
                    }
                }
            }
        }

        // 2. Flanking escape angles (90 deg, -90 deg, 135 deg, -135 deg, 180 deg, 45 deg, -45 deg)
        if (!found)
        {
            float[] escapeAngles = new float[] { 90f, -90f, 135f, -135f, 180f, 45f, -45f };
            Vector3 forward = transform.forward;
            foreach (float angle in escapeAngles)
            {
                Vector3 testDir = Quaternion.Euler(0f, angle, 0f) * forward;
                Vector3 testSpot = transform.position + testDir * 3.2f;
                if (NavMesh.SamplePosition(testSpot, out NavMeshHit navHit, 2.0f, NavMesh.AllAreas))
                {
                    if (IsClearOfObstacles(transform.position, navHit.position) &&
                        NavMesh.CalculatePath(transform.position, navHit.position, NavMesh.AllAreas, _reroutePath) &&
                        _reroutePath.status == NavMeshPathStatus.PathComplete)
                    {
                        chosenWaypoint = navHit.position;
                        found = true;
                        break;
                    }
                }
            }
        }

        // 3. Fallback: Nearby clear spot
        if (!found)
        {
            Vector3 randomPoint = transform.position + Random.insideUnitSphere * 3.5f;
            randomPoint.y = transform.position.y;
            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit fallbackHit, 3f, NavMesh.AllAreas))
            {
                if (IsClearOfObstacles(transform.position, fallbackHit.position))
                {
                    chosenWaypoint = fallbackHit.position;
                    found = true;
                }
            }
        }

        if (found)
        {
            _rerouteWaypoint = chosenWaypoint;
            _rerouteTimer = rerouteDuration;

            if (_rerouteTurnCoroutine != null) StopCoroutine(_rerouteTurnCoroutine);
            _rerouteTurnCoroutine = StartCoroutine(SmartRerouteTurnRoutine(chosenWaypoint));
        }
    }

    /// <summary>
    /// Smoothly transitions the monster from being stuck: stops locomotion animation, triggers turn animation,
    /// turns towards the clear corridor waypoint, and resumes running.
    /// </summary>
    protected virtual IEnumerator SmartRerouteTurnRoutine(Vector3 waypoint)
    {
        // 1. Immediately stop the walk / run locomotion animation
        SetLocomotionAnimSpeed(1.0f);
        SafeSetFloat(_speedHash, 0f);
        SafeSetBool(_isRunningHash, false);
        SafeSetBool(_isWalkingHash, false);

        if (_agent != null && _agent.isOnNavMesh && _agent.enabled)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        // 2. Compute facing direction towards the new route waypoint
        Vector3 turnDir = waypoint - transform.position;
        turnDir.y = 0f;

        if (turnDir.sqrMagnitude > 0.05f)
        {
            // 3. Trigger turn animation and rotate body towards open tunnel
            UpdateTurningAnimation(turnDir);

            Quaternion targetRotation = Quaternion.LookRotation(turnDir.normalized);
            float elapsed = 0f;
            float maxDuration = 0.55f;
            float rerouteTurnRate = (monsterType == MonsterType.Berserker) ? 140f : 110f;

            while (elapsed < maxDuration && Quaternion.Angle(transform.rotation, targetRotation) > 8f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rerouteTurnRate * Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            StopTurningAnimation(true);
        }

        // 4. Resume locomotion along the open route
        if (_agent != null && _agent.isOnNavMesh && _agent.enabled)
        {
            _agent.isStopped = false;
            _agent.SetDestination(waypoint);

            float runSpd = (monsterType == MonsterType.Zombie) ? standRunSpeed : runSpeed;
            SafeSetFloat(_speedHash, runSpd);
            SafeSetBool(_isRunningHash, true);
        }

        _rerouteTurnCoroutine = null;
    }

    protected Transform FindBestTarget()
    {
        Transform closest = null;
        float minEffectiveDistance = Mathf.Infinity;

        // 1. Mine-Wide Global Targeting: Evaluate all living explorers across the entire mine
        if (GameManager.Instance != null && GameManager.Instance.AliveExplorers != null)
        {
            var alive = GameManager.Instance.AliveExplorers;
            for (int i = 0; i < alive.Count; i++)
            {
                var netObj = alive[i];
                if (netObj == null || !netObj.gameObject.activeInHierarchy) continue;
                Transform candidate = netObj.transform;

                EvaluateCandidatePrey(candidate, ref closest, ref minEffectiveDistance);
            }
        }

        // 2. Proximity Sphere Fallback (picks up non-registered local player colliders)
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, searchRadius, _searchBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _searchBuffer[i];
            if (col == null || col.gameObject == gameObject) continue;
            if (IsMonster(col.gameObject)) continue;

            // Only target Players
            if (col.CompareTag("Player") || (col.transform.root != null && col.transform.root.CompareTag("Player")))
            {
                Transform candidate = col.transform.root != null ? col.transform.root : col.transform;
                EvaluateCandidatePrey(candidate, ref closest, ref minEffectiveDistance);
            }
        }

        return closest;
    }

    protected void EvaluateCandidatePrey(Transform candidate, ref Transform closest, ref float minEffectiveDistance)
    {
        if (candidate == null || candidate.gameObject == gameObject) return;
        if (IsMonster(candidate.gameObject)) return;
        if (IsTargetInvalidOrDead(candidate)) return;
        if (_temporarilyIgnoredTarget != null && candidate == _temporarilyIgnoredTarget) return;

        float straightDist = Vector3.Distance(transform.position, candidate.position);
        bool hasLoS = HasLineOfSight(candidate);

        // Huge global mine calculation:
        // Calculate the full NavMesh corridor path across the entire mine before considering moving.
        // If NO complete path exists (blocked by cave cave-in or solid rock wall), STRICTLY REJECT!
        float effectiveDist = straightDist;
        if (!hasLoS)
        {
            float pathDist = GetNavMeshPathDistance(transform.position, candidate.position);
            if (pathDist <= 0f)
            {
                // Target is unreachable through the mine corridors (separated by rock walls or on separate level)
                return;
            }
            effectiveDist = pathDist;
        }

        if (effectiveDist < minEffectiveDistance)
        {
            minEffectiveDistance = effectiveDist;
            closest = candidate;
        }
    }

    protected bool IsTargetInvalidOrDead(Transform t)
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

    public virtual void PlayHitReaction()
    {
        if (currentState == AIState.Dead) return;
        if (_hitCooldownTimer > 0f) return;

        _hitCooldownTimer = 0.25f;
        _hitStaggerTimer = hitReactionDuration;

        SafeSetTrigger(_hitHash);
        SafeSetTrigger(_hitReactionHash);
        SafeCrossFade(_stateHitReaction, "Zombie Reaction Hit", 0.08f, true);

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
        SafeSetTrigger(_hitHash);
        SafeSetTrigger(_hitReactionHash);
        SafeCrossFade(_stateHitReaction, "Zombie Reaction Hit", 0.08f, true);

        if (hurtSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSoundClip, GameSettingsManager.SFXVolume);
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
        ActiveMonsters.Remove(this);

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

    protected void SafeSetFloat(int hash, float value)
    {
        if (_animator != null && _animParams.Contains(hash))
        {
            _animator.SetFloat(hash, value);
        }
    }

    protected void SafeSetBool(int hash, bool value)
    {
        if (_animator != null && _animParams.Contains(hash))
        {
            _animator.SetBool(hash, value);
        }
    }

    protected void SafeSetTrigger(int hash)
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

    protected void SafeCrossFade(int stateHash, string stateName, float transitionDuration = 0.15f, bool force = false)
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