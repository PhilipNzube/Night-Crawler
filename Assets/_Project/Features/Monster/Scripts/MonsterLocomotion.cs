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

        // Destination smoothing threshold (from EnemyNPC_AI)
        private const float MinDestinationDelta = 0.45f;
        private Vector3 _currentDestination = Vector3.positiveInfinity;
        private float _repositionCooldownTimer = 0f;
        private float _repositionDurationTimer = 0f;
        private float _stuckTimer = 0f;
        private NavMeshPath _repositionPath;

        public bool CanReposition => _repositionCooldownTimer <= 0f && !IsRepositioning;
        public bool IsRepositioning => _repositionDurationTimer > 0f;

        private void Awake()
        {
            _repositionPath = new NavMeshPath();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

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
            }
        }

        private void Start()
        {
            EnsureGrounded();
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
        /// Sets the agent destination only when the target position has moved significantly.
        /// Does not overwrite active obstacle repositioning detours.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public void SetDestinationSmooth(Vector3 targetPosition)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (IsRepositioning) return; // Allow active wall-avoidance maneuver to complete

            if (Vector3.Distance(_currentDestination, targetPosition) > MinDestinationDelta || !agent.hasPath)
            {
                agent.isStopped = false;
                agent.SetDestination(targetPosition);
                _currentDestination = targetPosition;
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
        /// Checks if the agent's path is blocked by tunnel geometry, props, or unreachable points.
        /// Includes physical stuck detection when moving into solid cave walls.
        /// (Adopted from EnemyNPC_AI AIController).
        /// </summary>
        public bool IsPathBlocked(Transform ignoredTarget = null)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
            if (agent.pathPending) return false;

            // Incomplete or invalid NavMesh path means a wall blocks the route
            if (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                return true;
            }

            if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance)
            {
                _stuckTimer = 0f;
                return false;
            }

            // Stuck detection: agent is instructed to move but physical velocity is pinned against a wall
            if (!agent.isStopped && agent.velocity.sqrMagnitude < 0.05f)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 0.45f)
                {
                    return true;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }

            // SphereCast forward along the steering target direction to check for physical wall/obstacle collisions
            Vector3 origin = transform.position + Vector3.up * 0.85f;
            Vector3 direction = agent.steeringTarget - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.05f)
            {
                if (Physics.SphereCast(origin, 0.35f, direction.normalized, out RaycastHit hit, obstacleCheckDistance, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    // Never collide against self or the intended attack target
                    if (hit.collider.transform.root == transform.root) return false;
                    if (ignoredTarget != null && hit.collider.transform.root == ignoredTarget.root) return false;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Intelligently steers away from walls by testing flanking and rearward escape vectors.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public bool AttemptReposition(float range = 4.0f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh || _repositionCooldownTimer > 0f) return false;

            _repositionCooldownTimer = 1.8f; // Cooldown prevents erratic rapid-fire repathing

            Vector3[] escapeVectors = {
                transform.right * range,
                -transform.right * range,
                (-transform.forward + transform.right).normalized * range,
                (-transform.forward - transform.right).normalized * range,
                -transform.forward * range
            };

            foreach (var escape in escapeVectors)
            {
                Vector3 candidate = transform.position + escape;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    // Confirm line on NavMesh does not cross a wall
                    if (!NavMesh.Raycast(transform.position, hit.position, out _, NavMesh.AllAreas))
                    {
                        if (agent.CalculatePath(hit.position, _repositionPath) && _repositionPath.status == NavMeshPathStatus.PathComplete)
                        {
                            agent.isStopped = false;
                            agent.SetDestination(hit.position);
                            _currentDestination = hit.position;
                            _repositionDurationTimer = 1.25f;
                            _stuckTimer = 0f;
                            return true;
                        }
                    }
                }
            }

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
