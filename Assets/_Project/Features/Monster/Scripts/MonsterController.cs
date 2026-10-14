using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MonsterController : MonoBehaviour, IPossessable
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    
    [Header("Attack Settings")]
    public LayerMask explorerLayer; // Set this to the layer your Explorers are on
    private float _attackTimer;

    [Header("Camera References")]
    [Tooltip("Target anchor for camera rotation and tracking.")]
    public Transform monsterCameraTarget; 
    [Tooltip("Camera object or CinemachineCamera attached directly inside the monster prefab.")]
    public GameObject monsterFollowCamera;

    [Header("Grounding & Gravity (EnemyNPC_AI)")]
    [Tooltip("Downward gravity acceleration (adopted from EnemyNPC_AI PlayerController).")]
    public float gravity = -9.81f;
    private float _verticalVelocity = 0f;
    private bool _isGrounded = false;
    
    private CharacterController _controller;
    private Animator _animator;
    private GirlPossession _girlRef;

    private bool _isPossessed = false;
    private float _yaw;
    private float _pitch;
    private float _rotationVelocity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (_controller != null) _controller.stepOffset = 0.4f;
        _animator = GetComponentInChildren<Animator>();

        // Auto-detect monsterCameraTarget if not manually assigned
        if (monsterCameraTarget == null)
        {
            var root = transform.Find("MonsterCameraRoot");
            if (root != null)
            {
                monsterCameraTarget = root;
            }
            else
            {
                var allTransforms = GetComponentsInChildren<Transform>(true);
                foreach (var tr in allTransforms)
                {
                    if (tr != null && (tr.name.Contains("MonsterCamera") || tr.name.Contains("CameraRoot") || tr.name.Contains("CinemachineTarget")))
                    {
                        monsterCameraTarget = tr;
                        break;
                    }
                }
            }
        }

        // Auto-detect MonsterFollowCamera if not manually assigned
        if (monsterFollowCamera == null)
        {
            var cam = transform.Find("MonsterFollowCamera");
            if (cam != null)
            {
                monsterFollowCamera = cam.gameObject;
            }
            else
            {
                var allTransforms = GetComponentsInChildren<Transform>(true);
                foreach (var tr in allTransforms)
                {
                    if (tr != null && tr.name.Contains("MonsterFollowCamera"))
                    {
                        monsterFollowCamera = tr.gameObject;
                        break;
                    }
                }
            }
        }

        // Ensure monster follow camera is inactive while unpossessed
        if (monsterFollowCamera != null)
        {
            monsterFollowCamera.SetActive(false);
        }
    }

    private void Start()
    {
        // Guarantee monster begins with feet planted firmly on the floor/NavMesh (eliminates spawn floating)
        SnapToFloor();
    }

    /// <summary>
    /// Snaps the monster directly to the ground collider/NavMesh on spawn/start so it never flies in mid-air.
    /// (Adopts EnemyNPC_AI grounding and NavMesh positioning).
    /// </summary>
    public void SnapToFloor()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        LayerMask mask = ~LayerMask.GetMask("UI", "Ignore Raycast", "Player", "Monster");

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 15f, mask, QueryTriggerInteraction.Ignore))
        {
            Vector3 floorPos = hit.point;
            if (UnityEngine.AI.NavMesh.SamplePosition(floorPos, out UnityEngine.AI.NavMeshHit navHit, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                floorPos = navHit.position;
            }

            if (TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.enabled)
            {
                agent.Warp(floorPos);
            }
            else
            {
                transform.position = floorPos;
            }
        }
        else if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit navHit, 8.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            if (TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent) && agent.enabled)
            {
                agent.Warp(navHit.position);
            }
            else
            {
                transform.position = navHit.position;
            }
        }

        _verticalVelocity = -2f;
    }

    public void Possess(GirlPossession girl)
    {
        _isPossessed = true;
        _girlRef = girl;

        // Turn off AI logic if it exists
        MonsterAI ai = GetComponent<MonsterAI>();
        if (ai != null) ai.isBeingPossessed = true;

        // Activate the monster's follow camera for the possessing player
        if (monsterFollowCamera != null)
        {
            monsterFollowCamera.SetActive(true);
        }

        // Initialize camera rotation to match monster's current rotation
        _yaw = transform.rotation.eulerAngles.y;
        _pitch = 0;
    }

    public Transform GetCameraTarget()
    {
        if (monsterCameraTarget != null) return monsterCameraTarget;
        if (monsterFollowCamera != null) return monsterFollowCamera.transform;
        return transform;
    }

    void Update()
    {
        ApplyGroundingAndGravity();

        if (!_isPossessed) return;

        // Release monster (Interact key or E)
        bool releasePressed = KeybindingManager.IsActionTriggered("Interact")
            || (KeybindingManager.Instance == null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);
        if (releasePressed) { Release(); return; }

        HandleAttack();
        HandleMovement();
    }

    /// <summary>
    /// Ensures the monster is always firmly touching the floor using EnemyNPC_AI gravity and grounding logic.
    /// Works whether possessed by the Girl or moving as an autonomous AI minion.
    /// Never turns off the CharacterController.
    /// </summary>
    private void ApplyGroundingAndGravity()
    {
        if (_controller == null || !_controller.enabled) return;

        _isGrounded = _controller.isGrounded;

        // EnemyNPC_AI PlayerController grounding logic:
        // Clamps downward velocity to -2f when touching floor to prevent bouncing/floating down slopes.
        if (_isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }
        else
        {
            _verticalVelocity += gravity * Time.deltaTime;
        }

        // When unpossessed, the AI controls horizontal movement via NavMeshAgent.
        // We apply pure vertical gravity through the CharacterController so the monster
        // always falls and stays firmly touching the floor even before starting movement.
        if (!_isPossessed)
        {
            _controller.Move(new Vector3(0f, _verticalVelocity * Time.deltaTime, 0f));
        }
    }

    void LateUpdate()
    {
        if (!_isPossessed) return;

        // Camera Look Logic (Mouse)
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float sens = GameSettingsManager.MouseSens;
        float invert = GameSettingsManager.InvertY ? -1.0f : 1.0f;
        _yaw += mouseDelta.x * ((stats != null ? stats.lookSensitivity : 1.0f) * sens * 0.1f);
        _pitch -= mouseDelta.y * ((stats != null ? stats.lookSensitivity : 1.0f) * sens * 0.1f) * invert;
        _pitch = Mathf.Clamp(_pitch, -30f, 60f);

        // Rotate the camera target anchor
        if (monsterCameraTarget != null)
        {
            monsterCameraTarget.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
        }
    }

    private void HandleMovement()
    {
        // Get Input from WASD
        float x = (Keyboard.current.dKey.isPressed ? 1 : 0) - (Keyboard.current.aKey.isPressed ? 1 : 0);
        float z = (Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0);
        Vector3 inputDir = new Vector3(x, 0, z).normalized;
        Vector3 move = Vector3.zero;

        if (inputDir.magnitude > 0.1f)
        {
            // Calculate target rotation relative to the camera yaw
            float targetRotation = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + _yaw;
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref _rotationVelocity, stats != null ? stats.rotationSmoothTime : 0.12f);
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);

            Vector3 moveDir = Quaternion.Euler(0.0f, targetRotation, 0.0f) * Vector3.forward;
            float speed = stats != null ? stats.walkSpeed : 3.5f;
            move += moveDir * (speed * Time.deltaTime);
        }

        // Apply EnemyNPC_AI downward gravity displacement
        move.y += _verticalVelocity * Time.deltaTime;
        _controller.Move(move);

        // Update Animator speed
        if (_animator) _animator.SetFloat("Speed", inputDir.magnitude * (stats != null ? stats.walkSpeed : 3.5f));
    }

    private void HandleAttack()
    {
        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        if ((KeybindingManager.IsActionTriggered("Attack") || (KeybindingManager.Instance == null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)) && _attackTimer <= 0)
        {
            if (_animator != null)
            {
                _animator.SetTrigger("Attack"); 
                _attackTimer = stats.attackCooldown;
                
                // Optional: Immediate hit check (or use Animation Events for better timing)
                CheckForHit();
            }
        }
    }

    private void CheckForHit()
    {
        // Detect enemies in front of the monster
        Vector3 attackPoint = transform.position + transform.forward * 1.5f;
        Collider[] hits = Physics.OverlapSphere(attackPoint, stats.damageRadius, explorerLayer);

        foreach (var hit in hits)
        {
            Debug.Log("Monster hit explorer: " + hit.name);
            // hit.GetComponent<ExplorerHealth>()?.TakeDamage(50);
        }
    }

    public void Release()
    {
        _isPossessed = false;

        if (monsterFollowCamera != null)
        {
            monsterFollowCamera.SetActive(false);
        }

        MonsterAI ai = GetComponent<MonsterAI>();
        if (ai != null) ai.isBeingPossessed = false;
        _girlRef.ReturnFromMonster(transform.position);
    }

    public void OnPossess(ulong clientId)
    {
        // Interface requirement
    }

    public void OnRelease()
    {
        // Interface requirement
        Release();
    }

    // Visualize the attack range in the editor
    private void OnDrawGizmosSelected()
    {
        if (stats == null) return;
        
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * 1.5f, stats.damageRadius);
    }
}