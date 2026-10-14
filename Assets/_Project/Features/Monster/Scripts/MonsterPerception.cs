using UnityEngine;
using NightCrawler.Monsters;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// Evaluates sensory awareness (vision, line of sight, hearing, and target evaluation)
    /// for monster NPCs based on the clean perception architecture in EnemyNPC_AI.
    /// Follows the Single Responsibility Principle (SRP).
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterPerception : MonoBehaviour
    {
        [Header("Vision Settings")]
        [Tooltip("Standard vision range when hunting or patrolling.")]
        public float viewRadius = 22f;

        [Range(0f, 360f)]
        [Tooltip("Horizontal field-of-view angle in degrees.")]
        public float viewAngle = 130f;

        [Header("Search & Alert Vision")]
        [Tooltip("Expanded vision radius when searching near last known target location.")]
        public float searchViewRadius = 35f;

        [Range(0f, 360f)]
        [Tooltip("Expanded vision angle when actively searching.")]
        public float searchViewAngle = 160f;

        [Tooltip("Vision radius when on high alert after hearing suspicious noise.")]
        public float alertViewRadius = 30f;

        [Header("Hearing Settings")]
        [Tooltip("Radius within which investigator footsteps or weapon swings are heard immediately.")]
        public float hearingRadius = 8.5f;

        [Header("Detection Masks")]
        [Tooltip("Layer mask representing living player investigators.")]
        public LayerMask targetMask;

        [Tooltip("Layer mask representing solid cave walls, props, and doors that block vision.")]
        public LayerMask obstructionMask;

        [Header("Targeting Priorities")]
        [Tooltip("How much closer (in metres) another investigator must be to trigger an aggro switch from current target.")]
        public float targetSwitchThreshold = 4.0f;

        [Tooltip("Height offset above root to cast vision rays from (e.g. eyes level).")]
        public float eyeHeight = 1.6f;

        [Tooltip("Height offset on target to check line-of-sight against (e.g. chest level).")]
        public float targetChestHeight = 1.0f;

        // Noise detection memory
        private Vector3 _lastKnownNoisePosition;
        private bool _hasHeardNoise = false;
        private float _noiseMemoryTimer = 0f;

        // Search buffer for non-allocating sphere checks
        private static readonly Collider[] _targetBuffer = new Collider[32];

        public bool HasHeardNoise => _hasHeardNoise;
        public Vector3 LastKnownNoisePosition => _lastKnownNoisePosition;

        private void Awake()
        {
            // Auto-configure obstruction mask if not explicitly set in the Inspector
            if (obstructionMask.value == 0)
            {
                int excluded = LayerMask.GetMask("Ignore Raycast", "UI", "Monster", "Player", "Explorer", "Minimap", "Fog");
                if (excluded != 0)
                {
                    obstructionMask = ~excluded;
                }
                else
                {
                    obstructionMask = LayerMask.GetMask("Default", "Environment", "Terrain", "Obstacle");
                }
            }
        }

        private void Update()
        {
            if (_hasHeardNoise)
            {
                _noiseMemoryTimer -= Time.deltaTime;
                if (_noiseMemoryTimer <= 0f)
                {
                    _hasHeardNoise = false;
                }
            }
        }

        /// <summary>
        /// Checks if the target is within view radius, inside the field-of-view cone,
        /// and not blocked by solid terrain or cave walls.
        /// </summary>
        public bool CanSeeTarget(Transform target, bool useSearchVision = false)
        {
            if (target == null || IsTargetInvalidOrDead(target)) return false;

            float currentRadius = useSearchVision ? searchViewRadius : viewRadius;
            float currentAngle = useSearchVision ? searchViewAngle : viewAngle;

            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 targetPos = target.position + Vector3.up * targetChestHeight;
            Vector3 dirToTarget = targetPos - eyePos;
            float distToTarget = dirToTarget.magnitude;

            if (distToTarget > currentRadius) return false;

            // Check field of view angle
            dirToTarget.Normalize();
            float angleToTarget = Vector3.Angle(transform.forward, dirToTarget);
            if (angleToTarget > currentAngle * 0.5f) return false;

            // Check obstruction raycast
            if (Physics.Raycast(eyePos, dirToTarget, distToTarget, obstructionMask))
            {
                return false; // Solid obstacle between eye and target
            }

            return true;
        }

        /// <summary>
        /// Pure line-of-sight check without field-of-view angle restrictions (e.g. for close combat or chasing).
        /// </summary>
        public bool HasLineOfSight(Transform target)
        {
            if (target == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 targetPos = target.position + Vector3.up * targetChestHeight;
            Vector3 dirToTarget = targetPos - eyePos;
            float distToTarget = dirToTarget.magnitude;

            if (distToTarget <= 0.05f) return true;

            return !Physics.Raycast(eyePos, dirToTarget.normalized, distToTarget, obstructionMask);
        }

        /// <summary>
        /// Checks if the target is within hearing radius and not completely muffled.
        /// </summary>
        public bool CanHearTarget(Transform target)
        {
            if (target == null || IsTargetInvalidOrDead(target)) return false;

            float dist = Vector3.Distance(transform.position, target.position);
            return dist <= hearingRadius;
        }

        /// <summary>
        /// Broadcast notification when a loud sound occurs nearby (gunfire, explosion, voice chat, sprint steps).
        /// </summary>
        public void OnNoiseHeard(Vector3 noiseSource, float noiseRadius = 15f)
        {
            float dist = Vector3.Distance(transform.position, noiseSource);
            if (dist <= noiseRadius + hearingRadius)
            {
                _lastKnownNoisePosition = noiseSource;
                _hasHeardNoise = true;
                _noiseMemoryTimer = 12f;
            }
        }

        /// <summary>
        /// Global noise broadcast method (called when a player speaks loudly, fires a gun, or makes noise).
        /// Notifies all active monsters within acoustic range.
        /// </summary>
        public static void EmitNoise(Vector3 noiseSource, float noiseRadius)
        {
            for (int i = 0; i < MonsterAI.ActiveMonsters.Count; i++)
            {
                var monster = MonsterAI.ActiveMonsters[i];
                if (monster != null && monster.perception != null && monster.currentState != MonsterAI.AIState.Dead)
                {
                    monster.perception.OnNoiseHeard(noiseSource, noiseRadius);
                }
            }
        }

        public void ClearNoise()
        {
            _hasHeardNoise = false;
        }

        /// <summary>
        /// Evaluates all potential player targets in the area, filtering dead players, corpses, and the Girl.
        /// Applies hysteresis: keeps current target unless an alternative valid target is significantly closer.
        /// </summary>
        public Transform FindBestTarget(Transform currentTarget, float maxSearchDistance = -1f)
        {
            float searchDist = maxSearchDistance > 0 ? maxSearchDistance : Mathf.Max(viewRadius, 25f);
            int count = Physics.OverlapSphereNonAlloc(transform.position, searchDist, _targetBuffer);

            Transform bestCandidate = null;
            float bestDistance = Mathf.Infinity;

            // Current target distance baseline
            float currentDist = Mathf.Infinity;
            bool currentValid = currentTarget != null && !IsTargetInvalidOrDead(currentTarget);
            if (currentValid)
            {
                currentDist = Vector3.Distance(transform.position, currentTarget.position);
            }

            for (int i = 0; i < count; i++)
            {
                Collider col = _targetBuffer[i];
                if (col == null || col.gameObject == gameObject) continue;

                // Only consider players / investigators
                if (!col.CompareTag("Player") && (col.transform.root == null || !col.transform.root.CompareTag("Player")))
                {
                    continue;
                }

                Transform rootTransform = col.transform.root != null ? col.transform.root : col.transform;
                if (IsTargetInvalidOrDead(rootTransform)) continue;

                float dist = Vector3.Distance(transform.position, rootTransform.position);

                // Check Line of Sight preference
                bool visible = HasLineOfSight(rootTransform);

                // Give visible targets high scoring priority
                float effectiveDist = visible ? dist : (dist + 8f);

                if (effectiveDist < bestDistance)
                {
                    bestDistance = effectiveDist;
                    bestCandidate = rootTransform;
                }
            }

            // Hysteresis: Only switch from current target if the new candidate is significantly closer
            if (currentValid && bestCandidate != null && bestCandidate != currentTarget)
            {
                float newDist = Vector3.Distance(transform.position, bestCandidate.position);
                if (newDist + targetSwitchThreshold < currentDist)
                {
                    return bestCandidate;
                }
                return currentTarget;
            }

            return bestCandidate != null ? bestCandidate : (currentValid ? currentTarget : null);
        }

        /// <summary>
        /// Determines whether a target is dead, invalid, a corpse, or the Girl summoner.
        /// </summary>
        public bool IsTargetInvalidOrDead(Transform t)
        {
            if (t == null) return true;

            // Never target the Girl / summoner
            if (MonsterAI.IsGirl(t)) return true;

            // Check if target is a corpse or has zero health
            if (t.TryGetComponent<TargetHealth>(out var targetHealth))
            {
                if (targetHealth.isCorpse.Value || targetHealth.CurrentHealth <= 0f) return true;
            }
            else if (t.GetComponentInParent<TargetHealth>() is { } parentHealth)
            {
                if (parentHealth.isCorpse.Value || parentHealth.CurrentHealth <= 0f) return true;
            }

            // Check HealthSystem
            if (t.TryGetComponent<HealthSystem>(out var healthSystem))
            {
                if (healthSystem.IsDead || healthSystem.CurrentHealth <= 0f) return true;
            }
            else if (t.GetComponentInParent<HealthSystem>() is { } parentHs)
            {
                if (parentHs.IsDead || parentHs.CurrentHealth <= 0f) return true;
            }

            // Check if target is untagged or inactive
            if (!t.gameObject.activeInHierarchy || !t.CompareTag("Player"))
            {
                if (t.root == null || !t.root.CompareTag("Player")) return true;
            }

            return false;
        }

        public Vector3 DirFromAngle(float angleInDegrees)
        {
            return Quaternion.Euler(0, angleInDegrees, 0) * transform.forward;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, hearingRadius);

            Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
            Vector3 viewAngleA = DirFromAngle(-viewAngle / 2f);
            Vector3 viewAngleB = DirFromAngle(viewAngle / 2f);

            Gizmos.color = Color.red;
            Gizmos.DrawLine(eyePos, eyePos + viewAngleA * viewRadius);
            Gizmos.DrawLine(eyePos, eyePos + viewAngleB * viewRadius);
        }
    }
}
