using UnityEngine;
using UnityEngine.AI;

public class MonsterAI : MonoBehaviour
{
    public Transform target; 
    public bool isBeingPossessed = false;

    public enum Command { Hunt, Follow }
    [Header("Behavior")]
    public Command currentCommand = Command.Hunt;
    
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;

    private float _attackTimer;

    private NavMeshAgent _agent;
    private CharacterController _characterController;
    private Animator _animator;
    private bool _hasLanded = false;
    
    private readonly int _speedHash = Animator.StringToHash("Speed");
    private readonly int _attackHash = Animator.StringToHash("Attack");
    private static readonly Collider[] _hitBuffer = new Collider[10];

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();

        if (_agent != null) _agent.enabled = false;
    }

    private void OnEnable()
    {
        StartCoroutine(TargetingRoutine());
    }

    private System.Collections.Generic.IEnumerator<WaitForSeconds> TargetingRoutine()
    {
        // TARGETING TICK: Only search for targets 5 times a second (much cheaper!)
        var wait = new WaitForSeconds(0.2f);
        while (enabled)
        {
            if (_hasLanded && !isBeingPossessed && currentCommand == Command.Hunt)
            {
                target = FindClosestTarget();
            }
            yield return wait;
        }
    }

    void Update()
    {
        // 1. Initial falling logic from sky spawn
        if (!_hasLanded)
        {
            if (_characterController != null)
            {
                _characterController.Move(Vector3.down * 9.81f * Time.deltaTime);

                if (_characterController.isGrounded)
                {
                    _hasLanded = true;
                    if (_agent != null) _agent.enabled = true;
                }
            }
            else
            {
                _hasLanded = true;
                if (_agent != null) _agent.enabled = true;
            }
            return;
        }

        if (isBeingPossessed) 
        {
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            return;
        }

        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // 3. Navigation and Combat Logic
        if (_agent != null && _agent.isOnNavMesh && _agent.isActiveAndEnabled && stats != null)
        {
            if (currentCommand == Command.Follow)
            {
                if (GameManager.Instance != null && GameManager.Instance.GirlTransform != null)
                {
                    target = GameManager.Instance.GirlTransform;
                }
            }

            if (target == null) return;

            float distanceToTarget = Vector3.Distance(transform.position, target.position);

            if (distanceToTarget <= stats.attackRange)
            {
                _agent.isStopped = true;
                if (_animator != null) _animator.SetFloat(_speedHash, 0f); 
                
                if (_attackTimer <= 0)
                {
                    PerformAttack();
                }
            }
            else
            {
                _agent.isStopped = false;
                _agent.SetDestination(target.position);
                if (_animator != null) _animator.SetFloat(_speedHash, _agent.velocity.magnitude);
            }
        }
    }

    private void PerformAttack()
    {
        _attackTimer = stats.attackCooldown;
        if (_animator != null)
        {
            _animator.SetTrigger(_attackHash);
        }
        
        Vector3 direction = (target.position - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
        }
    }

    private Transform FindClosestTarget()
    {
        // --- OPTIMIZED: Use OverlapSphereNonAlloc to prevent GC Alloc ---
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, 50f, _hitBuffer, stats != null ? (int)stats.attackTargetLayer : -1);
        
        Transform closest = null;
        float minDistance = Mathf.Infinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];
            if (hit.gameObject == gameObject) continue;

            // Use Tag or basic component check (CharacterController is a good filter)
            if (hit.CompareTag("Player"))
            {
                // Verify it's not the Girl
                if (hit.GetComponent<GirlStealth>() != null) continue;

                float dist = Vector3.Distance(transform.position, hit.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = hit.transform;
                }
            }
        }
        return closest;
    }
}