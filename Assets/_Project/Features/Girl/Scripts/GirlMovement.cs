using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using StarterAssets;

/// <summary>
/// Controls the Girl's movement, rotation, and ghost pass-through capabilities.
/// Ignores collisions with other players so she can walk straight through them,
/// while maintaining normal physics collisions with walls and environment geometry.
/// </summary>
public class GirlMovement : NetworkBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public Animator animator;
    public EntityStats stats; 

    private Vector3 _velocity;
    private readonly int _speedHash = Animator.StringToHash("Speed");
    private float _lastAnimSpeed = -1f;
    private int _upperBodyLayer = -2;
    private readonly int _hasWeaponHash = Animator.StringToHash("HasWeapon");
    private readonly int _weaponIdHash   = Animator.StringToHash("WeaponID");
    private readonly int _comboStepHash  = Animator.StringToHash("ComboStep");

    private void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        SanitizeGirlAnimator();
    }

    /// <summary>
    /// Enforces that the Girl NEVER takes any weapon or combat pose.
    /// Clamps UpperBody_Combat layer weight to 0 and forces HasWeapon = false.
    /// Runs on all machines so anyone who looks at the Girl sees her natural spirit form.
    /// </summary>
    public void SanitizeGirlAnimator()
    {
        if (animator == null) return;

        if (_upperBodyLayer == -2)
        {
            _upperBodyLayer = animator.GetLayerIndex("UpperBody_Combat");
        }

        if (_upperBodyLayer >= 0 && animator.GetLayerWeight(_upperBodyLayer) > 0f)
        {
            animator.SetLayerWeight(_upperBodyLayer, 0f);
        }

        if (animator.GetBool(_hasWeaponHash) || animator.GetInteger(_weaponIdHash) != -1)
        {
            animator.SetBool(_hasWeaponHash, false);
            animator.SetInteger(_weaponIdHash, -1);
            animator.SetInteger(_comboStepHash, 0);
            animator.CrossFadeInFixedTime("Idle Walk Run Blend", 0.05f, 0);
        }
    }

    private bool _isCurrentlyVisible = false;

    public override void OnNetworkSpawn()
    {
        UpdateCollisionState(false);
    }

    /// <summary>
    /// Updates collision mode based on visibility.
    /// When visible (real girl mode), she cannot pass through players or objects.
    /// When invisible (spirit mode), she passes through players while still colliding with walls/geometry.
    /// </summary>
    public void UpdateCollisionState(bool isVisible)
    {
        _isCurrentlyVisible = isVisible;
        ApplyPlayerPassThrough(!isVisible);
    }

    /// <summary>
    /// Configures the Girl's CharacterController collision pairing.
    /// - If enablePassThrough is true (spirit mode): ignores collisions with players.
    /// - If enablePassThrough is false (visible mode): restores full solid collisions with players and objects!
    /// </summary>
    public void ApplyPlayerPassThrough(bool enablePassThrough)
    {
        if (controller == null) return;

        var allPlayers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        foreach (var otherCc in allPlayers)
        {
            if (otherCc != controller)
            {
                Physics.IgnoreCollision(controller, otherCc, enablePassThrough);
            }
        }
    }

    private void Start()
    {
        SanitizeGirlAnimator();

        // Synchronize entity stats to ThirdPersonController if present
        if (TryGetComponent<ThirdPersonController>(out var tpc) && stats != null)
        {
            tpc.MoveSpeed = stats.walkSpeed;
            tpc.SprintSpeed = stats.runSpeed;
        }
    }

    private void LateUpdate()
    {
        // Enforce unarmed spirit pose on all machines every frame
        SanitizeGirlAnimator();
    }

    void Update()
    {
        // Periodic safeguard to ensure newly joined or spawned players respect current collision state
        if (Time.frameCount % 60 == 0)
        {
            ApplyPlayerPassThrough(!_isCurrentlyVisible);
        }

        // CORE NETWORK RULE: Ensure only the owner moves their own character
        if (!IsOwner || PauseManager.IsGamePaused) return;

        // If controller is missing or disabled (e.g. while possessing an investigator), suspend movement!
        if (controller == null || !controller.enabled) return;

        // If possessing an investigator, the Girl's body stays frozen in place
        if (TryGetComponent<GirlPossession>(out var girlPoss) && girlPoss.isPossessing.Value) return;

        // If ThirdPersonController is present and enabled, it manages 3rd-person movement,
        // gravity, and camera-relative character rotation.
        // Bypassing HandleRotation and HandleMovement prevents the Girl from spinning in place when orbiting the camera!
        if (TryGetComponent<ThirdPersonController>(out var tpc) && tpc.enabled)
        {
            return;
        }

        HandleRotation();
        HandleMovement();
    }

    private void HandleRotation()
    {
        if (Mouse.current == null || stats == null) return;

        float mouseX = Mouse.current.delta.x.ReadValue() * stats.lookSensitivity * GameSettingsManager.MouseSens;
        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleMovement()
    {
        if (Keyboard.current == null || stats == null || controller == null || !controller.enabled) return;

        // Input gathering
        float x = (Keyboard.current.dKey.isPressed ? 1 : 0) - (Keyboard.current.aKey.isPressed ? 1 : 0);
        float z = (Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0);
        
        Vector3 move = (transform.right * x + transform.forward * z).normalized;
        bool isRunning = Keyboard.current.leftShiftKey.isPressed;
        float currentSpeed = isRunning ? stats.runSpeed : stats.walkSpeed;

        // Physical movement
        controller.Move(move * currentSpeed * Time.deltaTime);

        // --- OPTIMIZED: Only update animator if speed changed significantly ---
        if (animator != null)
        {
            float targetAnimSpeed = move.magnitude * (isRunning ? 1f : 0.5f);
            if (Mathf.Abs(targetAnimSpeed - _lastAnimSpeed) > 0.05f)
            {
                animator.SetFloat(_speedHash, targetAnimSpeed, 0.1f, Time.deltaTime);
                _lastAnimSpeed = targetAnimSpeed;
            }
        }

        ApplyGravity();
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && _velocity.y < 0) 
        {
            _velocity.y = -2f;
        }
        
        _velocity.y += -9.81f * Time.deltaTime;
        controller.Move(_velocity * Time.deltaTime);
    }
}