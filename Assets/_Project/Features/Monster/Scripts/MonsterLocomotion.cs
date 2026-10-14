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

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (obstacleMask.value == 0)
            {
                obstacleMask = LayerMask.GetMask("Default", "Environment", "Terrain", "Obstacle");
            }

            if (agent != null)
            {
                agent.speed = chaseSpeed;
                agent.angularSpeed = angularSpeed;
                agent.acceleration = acceleration;
                agent.stoppingDistance = stoppingDistance;
                agent.autoBraking = true;
            }
        }

        /// <summary>
        /// Sets the agent destination only when the target position has moved significantly.
        /// This eliminates CPU-intensive path recalculation thrashing and agent stuttering.
        /// (Clean logic adopted from EnemyNPC_AI AIController).
        /// </summary>
        public void SetDestinationSmooth(Vector3 targetPosition)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            if (Vector3.Distance(_currentDestination, targetPosition) > MinDestinationDelta)
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

            agent.isStopped = false;
            agent.SetDestination(targetPosition);
            _currentDestination = targetPosition;
        }

        /// <summary>
        /// Checks if the agent's path is blocked by tunnel geometry, props, or unreachable points.
        /// (Adopted from EnemyNPC_AI AIController).
        /// </summary>
        public bool IsPathBlocked(Transform ignoredTarget = null)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;

            if (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                return true;
            }

            if (agent.remainingDistance <= agent.stoppingDistance)
            {
                return false;
            }

            // Raycast forward along the steering target direction
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            Vector3 direction = agent.steeringTarget - transform.position;
            direction.y = 0;

            if (direction.sqrMagnitude > 0.05f)
            {
                if (Physics.Raycast(origin, direction.normalized, out RaycastHit hit, obstacleCheckDistance, obstacleMask))
                {
                    if (!hit.collider.isTrigger && (ignoredTarget == null || hit.collider.transform.root != ignoredTarget.root))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Calculates a random candidate position on the nearby NavMesh to unjam the monster
        /// if blocked by walls or props. (Adopted from EnemyNPC_AI AIController).
        /// </summary>
        public void AttemptReposition(float range = 3.5f)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            Vector3 offset = Random.insideUnitSphere * range;
            offset.y = 0;
            Vector3 candidate = transform.position + offset;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, range, NavMesh.AllAreas))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
                _currentDestination = hit.position;
            }
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
            if (agent != null && agent.enabled && agent.isOnNavMesh)
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
                animator.SetBool(isRunningHash, currentSpeed > (walkSpeed * 1.1f));
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
