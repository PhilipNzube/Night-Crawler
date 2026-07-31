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

    [Header("References")]
    public Transform monsterCameraTarget; 
    
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
        _animator = GetComponentInChildren<Animator>();
    }

    public void Possess(GirlPossession girl)
    {
        _isPossessed = true;
        _girlRef = girl;

        // Turn off AI logic if it exists
        MonsterAI ai = GetComponent<MonsterAI>();
        if (ai != null) ai.isBeingPossessed = true;

        // Initialize camera rotation to match monster's current rotation
        _yaw = transform.rotation.eulerAngles.y;
        _pitch = 0;
    }

    public Transform GetCameraTarget() => monsterCameraTarget;

    void Update()
    {
        if (!_isPossessed) return;

        // Release monster (E Key)
        if (Keyboard.current.eKey.wasPressedThisFrame) { Release(); return; }

        HandleAttack();
        HandleMovement();
    }

    void LateUpdate()
    {
        if (!_isPossessed) return;

        // Camera Look Logic (Mouse)
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        _yaw += mouseDelta.x * (stats.lookSensitivity * 0.1f);
        _pitch -= mouseDelta.y * (stats.lookSensitivity * 0.1f);
        _pitch = Mathf.Clamp(_pitch, -30f, 60f);

        // Rotate the camera target anchor
        monsterCameraTarget.rotation = Quaternion.Euler(_pitch, _yaw, 0.0f);
    }

    private void HandleMovement()
    {
        // Get Input from WASD
        float x = (Keyboard.current.dKey.isPressed ? 1 : 0) - (Keyboard.current.aKey.isPressed ? 1 : 0);
        float z = (Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0);
        Vector3 inputDir = new Vector3(x, 0, z).normalized;

        if (inputDir.magnitude > 0.1f)
        {
            // Calculate target rotation relative to the camera yaw
            float targetRotation = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + _yaw;
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetRotation, ref _rotationVelocity, stats.rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);

            Vector3 moveDir = Quaternion.Euler(0.0f, targetRotation, 0.0f) * Vector3.forward;
            _controller.Move(moveDir * (stats.walkSpeed * Time.deltaTime));
        }

        // Apply basic gravity
        _controller.Move(new Vector3(0, -9.81f * Time.deltaTime, 0));

        // Update Animator speed
        if (_animator) _animator.SetFloat("Speed", inputDir.magnitude * stats.walkSpeed);
    }

    private void HandleAttack()
    {
        if (_attackTimer > 0) _attackTimer -= Time.deltaTime;

        // Left Mouse Click triggers attack
        if (Mouse.current.leftButton.wasPressedThisFrame && _attackTimer <= 0)
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