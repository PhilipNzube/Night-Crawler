using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class GirlMovement : NetworkBehaviour
{
    [Header("References")]
    public CharacterController controller;
    public Animator animator;
    public EntityStats stats; 

    private Vector3 _velocity;
    private readonly int _speedHash = Animator.StringToHash("Speed");
    private float _lastAnimSpeed = -1f;

    void Update()
    {
        // CORE NETWORK RULE: Ensure only the owner moves their own character
        if (!IsOwner) return;

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
        if (Keyboard.current == null || stats == null) return;

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