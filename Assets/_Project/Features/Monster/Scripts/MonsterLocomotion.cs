using UnityEngine;
using UnityEngine.AI;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Handles NavMesh movement, path smoothing, rotation, obstacle avoidance,
    /// and locomotion animation driving for Monster NPCs.
    /// Follows the Single Responsibility Principle (SRP) and incorporates EnemyNPC_AI navigation logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterLocomotion : MonoBehaviour
    {
        [Header("Components")]
        [Tooltip("NavMeshAgent driving physical movement.")]
        public NavMeshAgent agent;

        [Tooltip("Animator driving monster locomotion clips.")]
        public Animator animator;

        [Header("Speed Settings")]
        [Tooltip("Walking speed (e.g. while roaming or calmly guarding).")]
        public float walkSpeed = 1.6f;

        [Tooltip("Chasing/sprinting speed while aggressively hunting players.")]
        public float chaseSpeed = 4.2f;

        [Tooltip("Turning rotation speed (degrees per second).")]
        public float angularSpeed = 480f;

        [Tooltip("NavMesh acceleration for responsive movement.")]
        public float acceleration = 12f;

        [Tooltip("Distance threshold to trigger arrival or stopping.")]
        public float stoppingDistance = 1.8f;

        [Header("Obstacle Repositioning")]
        [Tooltip("Distance ahead to check for raycast obstructions blocking movement.")]
        public float obstacleCheckDistance = 2.2f;

        [Tooltip("Layer mask representing solid walls and environment props.")]
        public LayerMask obstacleMask;

        [Header("Physical Body Dimensions & Posture Squeeze")]
        [Tooltip("Standard standing dimensions.")]
        public float standingHeight = 1.85f;
        public float standingRadius = 0.5f;

        [Tooltip("Compact crawling dimensions (allows squeezing under low overhangs and through small spaces).")]
        public float crawlingHeight = 0.65f;
        public float crawlingRadius = 0.5f;

        private MonsterAI _monsterAI;
        private CharacterController _characterController;
        private float _defaultAgentHeight;
        private float _defaultAgentRadius;
        private float _defaultControllerHeight;
        private float _defaultControllerRadius;
        private Vector3 _defaultControllerCenter;
        private bool _isCrawling = false;

        public float CurrentBodyHeight => (agent != null && agent.enabled) ? agent.height : (_isCrawling ? crawlingHeight : standingHeight);
        public float CurrentBodyRadius => (agent != null && agent.enabled) ? agent.radius : (_isCrawling ? crawlingRadius : standingRadius);
        public bool IsCrawling => _isCrawling;

        // Destination smoothing threshold (from EnemyNPC_AI)
        private const float MinDestinationDelta = 0.45f;
        private Vector3 _currentDestination = Vector3.positiveInfinity;
        private float _repositionCooldownTimer = 0f;
        private float _repositionDurationTimer = 0f;
        private float _stuckTimer = 0f;
        private NavMeshPath _repositionPath;

        // Physical displacement tracking (detects collisions/pushes against obstacles)
        private Vector3 _lastPhysicalPosition;
        private float _physicalPositionCheckTimer = 0f;
        private float _consecutiveStuckDuration = 0f;

        public bool CanReposition => _repositionCooldownTimer <= 0f && !IsRepositioning;
        public bool IsRepositioning => _repositionDurationTimer > 0f;

        private void Awake()
        {
            _repositionPath = new NavMeshPath();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _characterController = GetComponent<CharacterController>();
            if (_monsterAI == null) _monsterAI = GetComponent<MonsterAI>();

            if (obstacleMask.value == 0)
            {
                obstacleMask = LayerMask.GetMask("Default");
            }

            if (agent != null)
            {
                agent.speed = chaseSpeed;
                agent.angularSpeed = angularSpeed;
                agent.acceleration = acceleration;
                agent.stoppingDistance = stoppingDistance;
                agent.autoBraking = true;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

                // Stagger avoidance priority (range 35 - 64, where lower numbers have higher priority).
                // Assigning unique priorities eliminates mutual shoving wars when multiple monsters run together,
                // ensuring one monster smoothly yields and routes around the other instead of wedging against walls.
                agent.avoidancePriority = 35 + (Mathf.Abs(gameObject.GetInstanceID()) % 30);

                _defaultAgentHeight = agent.height > 0 ? agent.height : standingHeight;
                _defaultAgentRadius = agent.radius > 0 ? agent.radius : standingRadius;
                standingHeight = _defaultAgentHeight;
                standingRadius = Mathf.Max(0.5f, _defaultAgentRadius);
                crawlingRadius = Mathf.Max(0.5f, crawlingRadius);
                agent.radius = standingRadius;
            }

            if (_characterController != null)
            {
                _defaultControllerHeight = _characterController.height > 0 ? _characterController.height : standingHeight;
                _defaultControllerRadius = _characterController.radius > 0 ? _characterController.radius : standingRadius;
                _defaultControllerCenter = _characterController.center;
                _characterController.radius = Mathf.Max(0.45f, _defaultControllerRadius);
            }

            _lastPhysicalPosition = transform.position;
        }

        /// <summary>
        /// Dynamically scales NavMeshAgent and CharacterController dimensions based on posture.
        /// Squeezing under low overhangs is determined by height (crawlingHeight = 0.65f),
        /// while agent.radius is safely kept at 0.5m so obstacle avoidance between same-kind monsters is never broken.
        /// </summary>
        public void SetCrawlPosture(bool crawling)
        {
            _isCrawling = crawling;

            float targetHeight = crawling ? crawlingHeight : standingHeight;
            float targetRadius = crawling ? crawlingRadius : standingRadius;

            if (agent != null && agent.enabled)
            {
                agent.height = targetHeight;
                agent.radius = Mathf.Max(0.5f, targetRadius);
            }

            if (_characterController != null)
            {
                _characterController.height = targetHeight;
                _characterController.radius = Mathf.Max(0.45f, targetRadius);
                _characterController.center = crawling 
                    ? new Vector3(0f, targetHeight * 0.5f, 0f) 
                    : (_defaultControllerCenter.sqrMagnitude > 0 ? _defaultControllerCenter : new Vector3(0f, targetHeight * 0.5f, 0f));
            }
        }

        /// <summary>
        /// Checks if an obstacle ahead blocks standing height but has clear opening at crawling height.
        /// </summary>
        public bool HasLowOverheadObstacleAhead(float checkDistance = 2.5f)
        {
            Vector3 forward = transform.forward;
            Vector3 lowOrigin = transform.position + Vector3.up * 0.35f;
            Vector3 highOrigin = transform.position + Vector3.up * 1.35f;

            bool highBlocked = Physics.Raycast(highOrigin, forward, checkDistance, obstacleMask, QueryTriggerInteraction.Ignore);
            bool lowBlocked = Physics.Raycast(lowOrigin, forward, checkDistance, obstacleMask, QueryTriggerInteraction.Ignore);

            return highBlocked && !lowBlocked;
        }

        private void Start()
        {
            EnsureGrounded();
            _lastPhysicalPosition = transform.position;
        }

        /// <summary>
        /// Warps the NavMeshAgent firmly onto the floor surface if it spawned or hovered in mid-air.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public void EnsureGrounded()
        {
            if (agent == null || !agent.enabled) return;

            Vector3 currentPos = transform.position;
            if (NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 6.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        private void Update()
        {
            TrackPhysicalMovement();
            ResolveSameKindAvoidance();
            ResolveQueenClearance();

            if (_repositionDurationTimer > 0f)
            {
                _repositionDurationTimer -= Time.deltaTime;
                if (_repositionDurationTimer <= 0f)
                {
                    _currentDestination = Vector3.positiveInfinity;
                }
            }

            if (_repositionCooldownTimer > 0f)
            {
                _repositionCooldownTimer -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Prevents monsters of the same kind (Zombie to Zombie, Berserker to Berserker)
        /// from overlapping or passing through each other.
        /// Applies mutual lateral steering and physical NavMesh separation forces.
        /// </summary>
        public void ResolveSameKindAvoidance()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (_monsterAI == null) _monsterAI = GetComponent<MonsterAI>();
            if (_monsterAI == null || _monsterAI.currentState == MonsterAI.AIState.Dead) return;

            var activeMonsters = MonsterAI.ActiveMonsters;
            if (activeMonsters == null || activeMonsters.Count <= 1) return;

            // Clearance bubble: at least 1.15m for zombies, 1.45m for berserkers
            float minClearance = _monsterAI.monsterType == MonsterAI.MonsterType.Berserker ? 1.45f : 1.15f;
            float minClearanceSqr = minClearance * minClearance;

            Vector3 myPos = transform.position;
            Vector3 totalPush = Vector3.zero;
            int pushCount = 0;

            for (int i = 0; i < activeMonsters.Count; i++)
            {
                var other = activeMonsters[i];
                if (other == null || other == _monsterAI || other.currentState == MonsterAI.AIState.Dead) continue;
                // Only monsters of the same kind avoid each other
                if (other.monsterType != _monsterAI.monsterType) continue;

                Vector3 otherPos = other.transform.position;
                Vector3 diff = myPos - otherPos;
                diff.y = 0f;

                float sqrDist = diff.sqrMagnitude;
                if (sqrDist < minClearanceSqr)
                {
                    float dist = Mathf.Sqrt(sqrDist);
                    Vector3 pushDir;
                    if (dist > 0.001f)
                    {
                        pushDir = diff / dist;
                    }
                    else
                    {
                        // Deterministic angle if exactly overlapping
                        float angle = (gameObject.GetInstanceID() % 360) * Mathf.Deg2Rad;
                        pushDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    }

                    float penetration = minClearance - dist;
                    totalPush += pushDir * penetration;
                    pushCount++;
                }
            }

            if (pushCount > 0)
            {
                Vector3 avgPush = totalPush / pushCount;
                // Apply soft physical separation displacement along NavMesh
                Vector3 moveDelta = avgPush * (3.0f * Time.deltaTime);
                agent.Move(moveDelta);

                // If moving along a path, also apply lateral steering to smoothly route around peer
                if (agent.hasPath && !agent.isStopped && agent.velocity.sqrMagnitude > 0.1f)
                {
                    Vector3 lateral = Vector3.Cross(Vector3.up, agent.velocity.normalized);
                    if (Vector3.Dot(lateral, avgPush) < 0f) lateral = -lateral;
                    agent.Move(lateral * (1.5f * Time.deltaTime));
                }
            }
        }

        /// <summary>
        /// Ensures monsters always treat the Girl as their Queen when guarding or following:
        /// 1. Never stops or clips inside her (keeps at least 2.4m clearance).
        /// 2. If standing in her forward path, smartly turns forward, moves to clear her corridor,
        ///    and turns back to face her once clear (never walks backwards).
        /// Suppressed completely during active Hunt commands or player pursuit.
        /// </summary>
        public void ResolveQueenClearance()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (_monsterAI == null) _monsterAI = GetComponent<MonsterAI>();
            if (_monsterAI == null || _monsterAI.currentState == MonsterAI.AIState.Dead) return;

            // When actively hunting investigators, NEVER divert to clear or face the Queen!
            if (_monsterAI.currentCommand == MonsterAI.Command.Hunt || (_monsterAI.target != null && !MonsterAI.IsGirl(_monsterAI.target)))
            {
                return;
            }

            Transform queen = _monsterAI.CommandLeader;
            if (queen == null && GameManager.Instance != null && _monsterAI.currentCommand == MonsterAI.Command.Follow)
            {
                queen = GameManager.Instance.GirlTransform;
            }
            if (queen == null) return;

            Vector3 myPos = transform.position;
            Vector3 queenPos = queen.position;
            Vector3 diff = myPos - queenPos;
            diff.y = 0f;

            float dist = diff.magnitude;
            float clearanceRadius = 2.4f;

            Vector3 queenForward = queen.forward;
            queenForward.y = 0f;
            bool isInFrontOfQueen = false;
            Vector3 clearDir = diff.sqrMagnitude > 0.05f ? diff.normalized : transform.forward;

            if (queenForward.sqrMagnitude > 0.05f && dist < 3.5f && dist > 0.05f)
            {
                float dot = Vector3.Dot(queenForward.normalized, diff.normalized);
                if (dot > 0.25f)
                {
                    isInFrontOfQueen = true;
                    // Lateral clearance perpendicular to Queen's forward direction
                    Vector3 lateral = Vector3.Cross(Vector3.up, queenForward.normalized);
                    if (Vector3.Dot(lateral, diff) < 0f) lateral = -lateral;
                    // Combine lateral sidestep with moving away
                    clearDir = (lateral * 1.6f + diff.normalized).normalized;
                }
            }

            // If too close or blocking her path, turn in the movement direction and move forward!
            if (dist < clearanceRadius || isInFrontOfQueen)
            {
                // Turn to face the direction of clearing movement (moves forward, never backwards!)
                RotateTowards(myPos + clearDir, angularSpeed * 1.8f);

                // Move forward along clearing direction
                float moveSpeed = (dist < clearanceRadius) ? (clearanceRadius - dist) * 3.5f + 1.5f : 2.5f;
                agent.Move(clearDir * (moveSpeed * Time.deltaTime));
            }
            else if (dist < 3.8f && _monsterAI.currentCommand == MonsterAI.Command.Follow && (!agent.hasPath || agent.isStopped || agent.velocity.sqrMagnitude < 0.1f))
            {
                // Cleared the corridor and stopped: turn back and face the Queen respectfully!
                RotateTowards(queenPos, angularSpeed * 1.2f);
            }
        }

        /// <summary>
        /// Deprecated: Target clearance is managed by NavMeshAgent.stoppingDistance and EnemyNPC_AI gap closing.
        /// Preserved as safe no-op.
        /// </summary>
        public void ResolveTargetClearance()
        {
            // No-op: Monsters hold their ground in striking range and close gaps smoothly without physical pushback fighting.
        }

        /// <summary>
        /// Samples real world transform displacement every 0.25s.
        /// Catches physical wedges where CharacterControllers or obstacle colliders prevent movement
        /// even while simulated agent.velocity reports forward motion.
        /// </summary>
        private void TrackPhysicalMovement()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            _physicalPositionCheckTimer += Time.deltaTime;
            if (_physicalPositionCheckTimer >= 0.25f)
            {
                float movedDist = Vector3.Distance(transform.position, _lastPhysicalPosition);
                _lastPhysicalPosition = transform.position;
                _physicalPositionCheckTimer = 0f;

                // If instructed to move towards a distant destination, but actual displacement is pinned:
                bool isTryingToMove = agent.hasPath && !agent.isStopped && agent.remainingDistance > (agent.stoppingDistance + 0.25f);
                if (isTryingToMove && movedDist < 0.08f)
                {
                    _consecutiveStuckDuration += 0.25f;
                }
                else
                {
                    _consecutiveStuckDuration = Mathf.Max(0f, _consecutiveStuckDuration - 0.25f);
                }
            }
        }

        /// <summary>
        /// Sets the agent destination only when the target position has moved significantly.
        /// Does not overwrite active obstacle repositioning detours.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public void SetDestinationSmooth(Vector3 targetPosition)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (IsRepositioning) return; // Allow active wall-avoidance maneuver to complete

            Vector3 validPos = targetPosition;
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                validPos = hit.position;
            }

            if (Vector3.Distance(_currentDestination, validPos) > MinDestinationDelta || !agent.hasPath)
            {
                agent.isStopped = false;
                agent.SetDestination(validPos);
                _currentDestination = validPos;
            }
        }

        /// <summary>
        /// Forces an immediate destination update regardless of distance delta.
        /// </summary>
        public void SetDestinationDirect(Vector3 targetPosition)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            _repositionDurationTimer = 0f;
            agent.isStopped = false;
            agent.SetDestination(targetPosition);
            _currentDestination = targetPosition;
        }

        /// <summary>
        /// Probes for a passable opening or gap that fits the monster's current body dimensions
        /// (standing or crawling) when obstacles lie in the path.
        /// Prevents monsters from wandering in circles when an opening exists that fits their bodies.
        /// </summary>
        public bool TryFindPassableSpace(Vector3 preferredDirection, float maxDistance, out Vector3 passablePoint)
        {
            passablePoint = Vector3.zero;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

            float bodyHeight = CurrentBodyHeight;
            float bodyRadius = CurrentBodyRadius;

            // Define vertical clearance capsule points
            Vector3 bottom = transform.position + Vector3.up * Mathf.Clamp(bodyRadius, 0.15f, 0.4f);
            Vector3 top = transform.position + Vector3.up * Mathf.Max(bottom.y + 0.1f, bodyHeight - bodyRadius);

            // Probe forward angles first, testing small angular deviations through/around the obstacle
            float[] probeAngles = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f };
            Vector3 forwardBase = preferredDirection.sqrMagnitude > 0.01f ? preferredDirection.normalized : transform.forward;
            forwardBase.y = 0f;

            foreach (float angle in probeAngles)
            {
                Vector3 probeDir = Quaternion.Euler(0f, angle, 0f) * forwardBase;

                // Cast capsule with monster's body dimensions (with a small 0.05m clearance tolerance)
                float castRadius = Mathf.Max(0.18f, bodyRadius * 0.88f);
                bool hitObstacle = Physics.CapsuleCast(
                    bottom,
                    top,
                    castRadius,
                    probeDir,
                    out RaycastHit hit,
                    maxDistance,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore
                );

                if (!hitObstacle)
                {
                    // Full clear corridor in this direction! Sample NavMesh point ahead
                    Vector3 candidatePos = transform.position + probeDir * Mathf.Min(maxDistance, 3.5f);
                    if (NavMesh.SamplePosition(candidatePos, out NavMeshHit navHit, 1.6f, NavMesh.AllAreas))
                    {
                        if (agent.CalculatePath(navHit.position, _repositionPath) && _repositionPath.status == NavMeshPathStatus.PathComplete)
                        {
                            passablePoint = navHit.position;
                            return true;
                        }
                    }
                }
                else if (hit.distance > 0.7f)
                {
                    // Obstacle is hit further ahead. Check if a passage exists around its side edges (the gap/opening)
                    Vector3 tangent = Vector3.Cross(hit.normal, Vector3.up).normalized;
                    Vector3[] edgeOffsets = {
                        hit.point + tangent * (bodyRadius * 2.2f),
                        hit.point - tangent * (bodyRadius * 2.2f)
                    };

                    foreach (var edgePoint in edgeOffsets)
                    {
                        if (NavMesh.SamplePosition(edgePoint, out NavMeshHit edgeNavHit, 1.8f, NavMesh.AllAreas))
                        {
                            // Verify body fits at edge point without colliding with obstacle
                            Vector3 edgeBottom = edgeNavHit.position + Vector3.up * castRadius;
                            Vector3 edgeTop = edgeNavHit.position + Vector3.up * Mathf.Max(edgeBottom.y + 0.1f, bodyHeight - castRadius);
                            if (!Physics.CheckCapsule(edgeBottom, edgeTop, castRadius, obstacleMask, QueryTriggerInteraction.Ignore))
                            {
                                if (agent.CalculatePath(edgeNavHit.position, _repositionPath) && _repositionPath.status == NavMeshPathStatus.PathComplete)
                                {
                                    passablePoint = edgeNavHit.position;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the agent's path is blocked by tunnel geometry, props, or unreachable points.
        /// Includes physical stuck detection when moving into solid cave walls or wedged by other monsters.
        /// Automatically identifies passable openings that fit the body rather than declaring blockage.
        /// (Adopted from EnemyNPC_AI AIController).
        /// </summary>
        public bool IsPathBlocked(Transform ignoredTarget = null)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
            if (agent.pathPending) return false;

            // Only truly invalid NavMesh paths indicate impossible routing
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                return true;
            }

            if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance)
            {
                _stuckTimer = 0f;
                return false;
            }

            // Physical stuck detection: agent is instructed to move but physical velocity or actual displacement is pinned
            bool isTryingToMove = !agent.isStopped && agent.remainingDistance > agent.stoppingDistance;
            if (isTryingToMove && (agent.velocity.sqrMagnitude < 0.05f || _consecutiveStuckDuration >= 0.5f))
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 0.4f || _consecutiveStuckDuration >= 0.5f)
                {
                    return true;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }

            // Check immediate forward obstacle clearance using current body dimensions
            float bodyHeight = CurrentBodyHeight;
            float bodyRadius = CurrentBodyRadius;
            Vector3 origin = transform.position + Vector3.up * Mathf.Clamp(bodyHeight * 0.5f, 0.25f, 0.85f);
            Vector3 direction = agent.steeringTarget - transform.position;
            direction.y = 0;

            // Check immediate proximity (0.95m) so openings/gaps are not falsely aborted from 2+ meters away
            float checkDist = Mathf.Min(obstacleCheckDistance, 0.95f);

            if (direction.sqrMagnitude > 0.05f)
            {
                float castRadius = Mathf.Clamp(bodyRadius * 0.85f, 0.2f, 0.4f);
                if (Physics.SphereCast(origin, castRadius, direction.normalized, out RaycastHit hit, checkDist, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    // Never collide against self or the intended attack target
                    if (hit.collider.transform.root == transform.root) return false;
                    if (ignoredTarget != null && hit.collider.transform.root == ignoredTarget.root) return false;

                    // If an obstacle is detected in immediate proximity, check if there's a passable space fitting the body
                    if (TryFindPassableSpace(direction.normalized, 3.5f, out Vector3 passablePoint))
                    {
                        // Found a space that fits! Route directly through it instead of blocking!
                        agent.isStopped = false;
                        agent.SetDestination(passablePoint);
                        _currentDestination = passablePoint;
                        _repositionDurationTimer = 1.2f;
                        _stuckTimer = 0f;
                        _consecutiveStuckDuration = 0f;
                        return false; // Not blocked anymore! We are actively passing through the space!
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Intelligently steers away from obstacles by prioritizing forward gaps and spaces that fit the body,
        /// falling back to structured flanking and rearward escape vectors.
        /// Unwedges monsters if pushed into walls or obstacles by other monsters.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public bool AttemptReposition(float range = 4.0f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh || _repositionCooldownTimer > 0f) return false;

            // Anchor on valid NavMesh close to monster
            Vector3 originPos = transform.position;
            if (NavMesh.SamplePosition(originPos, out NavMeshHit originHit, 3.0f, NavMesh.AllAreas))
            {
                originPos = originHit.position;
            }

            // Emergency unwedge: If physically pinned for over 1.8s, snap/warp to nearest valid NavMesh floor
            if (_consecutiveStuckDuration >= 1.8f)
            {
                agent.Warp(originPos);
                _consecutiveStuckDuration = 0f;
                _stuckTimer = 0f;
            }

            // 1. First Priority: Check if there's a forward/flanking opening that fits the body!
            Vector3 preferredDir = transform.forward;
            if (_monsterAI != null && _monsterAI.currentCommand == MonsterAI.Command.Follow && _monsterAI.CommandLeader != null)
            {
                preferredDir = (_monsterAI.CommandLeader.position - transform.position).normalized;
            }
            else if (agent.hasPath && (agent.destination - transform.position).sqrMagnitude > 0.1f) 
            {
                preferredDir = (agent.destination - transform.position).normalized;
            }
            preferredDir.y = 0;

            if (TryFindPassableSpace(preferredDir, range, out Vector3 passableSpace))
            {
                agent.isStopped = false;
                agent.SetDestination(passableSpace);
                _currentDestination = passableSpace;
                _repositionDurationTimer = 1.4f;
                _repositionCooldownTimer = 0.5f;
                _stuckTimer = 0f;
                _consecutiveStuckDuration = 0f;
                return true;
            }

            // 2. Second Priority: Structured angle offsets starting with small deviations (forward flanks first)
            float[] angleOffsets = { 18f, -18f, 35f, -35f, 55f, -55f, 75f, -75f, 95f, -95f, 135f, -135f, 180f };
            float[] testRadii = { range, range * 0.65f, range * 1.35f };

            Vector3 baseForward = transform.forward.sqrMagnitude > 0.01f ? transform.forward : Vector3.forward;

            foreach (float radius in testRadii)
            {
                foreach (float angle in angleOffsets)
                {
                    Vector3 escapeDir = Quaternion.Euler(0f, angle, 0f) * baseForward;
                    Vector3 candidatePos = originPos + escapeDir.normalized * radius;

                    if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    {
                        // Verify complete path without wall penetration
                        if (agent.CalculatePath(hit.position, _repositionPath) && _repositionPath.status == NavMeshPathStatus.PathComplete)
                        {
                            agent.isStopped = false;
                            agent.SetDestination(hit.position);
                            _currentDestination = hit.position;
                            _repositionDurationTimer = 1.4f;
                            _repositionCooldownTimer = 0.8f;
                            _stuckTimer = 0f;
                            _consecutiveStuckDuration = 0f;
                            return true;
                        }
                    }
                }
            }

            // 3. Fallback: Quick random radial sample
            for (int i = 0; i < 4; i++)
            {
                Vector3 randomDir = Random.insideUnitSphere * range;
                randomDir.y = 0;
                Vector3 candidatePos = originPos + randomDir;

                if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    if (agent.CalculatePath(hit.position, _repositionPath) && _repositionPath.status == NavMeshPathStatus.PathComplete)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(hit.position);
                        _currentDestination = hit.position;
                        _repositionDurationTimer = 1.4f;
                        _repositionCooldownTimer = 0.8f;
                        _stuckTimer = 0f;
                        _consecutiveStuckDuration = 0f;
                        return true;
                    }
                }
            }

            // If completely blocked, set a brief retry cooldown (0.25s) so we don't lock out for seconds
            _repositionCooldownTimer = 0.25f;
            return false;
        }

        /// <summary>
        /// Smoothly rotates the monster's body towards a world position while staying upright.
        /// </summary>
        public void RotateTowards(Vector3 targetPosition, float rotationSpeed = -1f)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f) return;

            float rotSpd = rotationSpeed > 0 ? rotationSpeed : angularSpeed;
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotSpd * Time.deltaTime);
        }

        /// <summary>
        /// Halts all locomotion immediately.
        /// </summary>
        public void Stop()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            _currentDestination = Vector3.positiveInfinity;
        }

        /// <summary>
        /// Resumes movement with the designated speed.
        /// </summary>
        public void Resume(float speed = -1f)
        {
            if (agent == null || !agent.enabled) return;

            if (!agent.isOnNavMesh)
            {
                EnsureGrounded();
            }

            if (agent.isOnNavMesh)
            {
                if (speed > 0f) agent.speed = speed;
                agent.isStopped = false;
            }
        }

        public void SetSpeed(float speed)
        {
            if (agent != null && agent.enabled)
            {
                agent.speed = speed;
            }
        }

        /// <summary>
        /// Synchronizes the Animator parameters with the agent's real physical velocity.
        /// </summary>
        public void UpdateAnimation(int speedHash, int isRunningHash)
        {
            if (animator == null || agent == null || !agent.enabled) return;

            float currentSpeed = agent.velocity.magnitude;
            animator.SetFloat(speedHash, currentSpeed);

            if (isRunningHash != 0)
            {
                animator.SetBool(isRunningHash, currentSpeed > (walkSpeed * 1.25f));
            }
        }

        public void ResetPath()
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }
            _currentDestination = Vector3.positiveInfinity;
        }
    }
}
