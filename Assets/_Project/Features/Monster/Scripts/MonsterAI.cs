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

    [Header("Monster Identity")]
    public MonsterType monsterType = MonsterType.Zombie;
    public Command currentCommand = Command.Hunt;
    public bool isBeingPossessed = false;

    [Header("Scriptable Data")]
    public EntityStats stats;

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

    // Component references
    private NavMeshAgent _agent;
    private CharacterController _characterController;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private TargetHealth _targetHealth;

    // Animation parameter hashes
    private readonly int _speedHash      = Animator.StringToHash("Speed");
    private readonly int _isRunningHash  = Animator.StringToHash("IsRunning");
    private readonly int _attackHash     = Animator.StringToHash("Attack");
    private readonly int _screamHash     = Animator.StringToHash("Scream");
    private readonly int _roarHash       = Animator.StringToHash("Roar");
    private readonly int _dieHash        = Animator.StringToHash("Die");
    private readonly HashSet<int> _animParams = new HashSet<int>();

    // Internal state timers & caches
    private float _attackTimer;
    private float _retargetTimer;
    private bool _hasLanded = false;
    private bool _hasScreamed = false;
    private static readonly Collider[] _searchBuffer = new Collider[20];

    // Roam & Guard state
    private Transform _commandLeader;
    private float _roamTimer = 0f;
    private float _roamWaitTimer = 0f;
    private bool _isRoamWaiting = false;
    private Vector3 _roamDestination;

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
            // Zombie
            if (runSpeed < 5.0f) runSpeed = 5.5f;
            if (attackDamage < 25f) attackDamage = 35f;
            if (screamDuration <= 0f) screamDuration = 2.2f;
        }

        // ScriptableObject stats override if present
        if (stats != null)
        {
            if (stats.runSpeed > 0) runSpeed = stats.runSpeed;
            else if (stats.walkSpeed > 0) runSpeed = stats.walkSpeed;

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_targetHealth != null)
        {
            _targetHealth.currentHealth.OnValueChanged -= HandleTargetHealthChanged;
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

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // 3. State Machine
        switch (currentState)
        {
            case AIState.SpawningScream:
                // Stay stationary while roaring/screaming
                if (_agent != null && _agent.enabled) _agent.isStopped = true;
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
            ExecuteRoam(2.8f);
            return;
        }

        // Bodyguard guard position offset around the Girl
        float angleOffset = (GetInstanceID() % 6) * 60f * Mathf.Deg2Rad;
        Vector3 guardOffset = new Vector3(Mathf.Sin(angleOffset), 0f, Mathf.Cos(angleOffset)) * 2.2f;
        Vector3 targetSpot = leader.position + guardOffset;

        float distToGirl = Vector3.Distance(transform.position, leader.position);
        float distToSpot = Vector3.Distance(transform.position, targetSpot);

        // Defensive protection: if an investigator gets close (< 4m to Girl or < 3.2m to monster), strike the threat!
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

        // Move to guard position beside the Girl
        if (distToSpot <= 1.2f || (distToGirl <= 2.2f && _agent.velocity.magnitude < 0.2f))
        {
            // Reached guard position — stand vigilant beside the Girl
            _agent.isStopped = true;
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);

            // Turn outward matching Girl's forward view
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

            if (distance <= attackRange)
            {
                // Within strike range
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
                // Relentless sprint towards target
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
        ExecuteRoam(2.8f);
    }

    private void ExecuteRoam(float patrolSpeed)
    {
        if (_isRoamWaiting)
        {
            _agent.isStopped = true;
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);

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
            SafeSetFloat(_speedHash, 0f);
            SafeSetBool(_isRunningHash, false);
            return;
        }

        _agent.isStopped = false;
        _agent.speed = patrolSpeed;
        SafeSetFloat(_speedHash, patrolSpeed);
        SafeSetBool(_isRunningHash, false);
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
}