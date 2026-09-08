using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using StarterAssets;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages humanoid ragdoll physics on character death across the network.
/// Automatically collects all bone Rigidbodies & Colliders, keeps them kinematic while alive,
/// and activates ragdoll physics seamlessly upon HealthSystem.OnDied.
/// </summary>
public class NetworkRagdollController : NetworkBehaviour
{
    [Header("Ragdoll Settings")]
    [Tooltip("Root bone of the character (e.g. Hips/Pelvis). If null, finds child rigidbodies automatically.")]
    public Transform ragdollRoot;

    [Tooltip("Optional upward/forward impulse to add on death.")]
    public float deathImpulse = 2.5f;

    private readonly List<Rigidbody> _ragdollRigidbodies = new List<Rigidbody>();
    private readonly List<Collider> _ragdollColliders = new List<Collider>();

    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private CharacterController _characterController;
    private HealthSystem _healthSystem;

    private bool _isRagdollActive = false;

    private ThirdPersonController _thirdPersonController;
    private PlayerInput _playerInput;
    private StarterAssetsInputs _starterInputs;
    private Coroutine _deathOrbitCoroutine;
    private Transform _cameraTarget;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _characterController = GetComponent<CharacterController>();
        _healthSystem = GetComponent<HealthSystem>();
        _thirdPersonController = GetComponent<ThirdPersonController>();
        _playerInput = GetComponent<PlayerInput>();
        _starterInputs = GetComponent<StarterAssetsInputs>();

        // Find camera target (PlayerCameraRoot or controller's CinemachineCameraTarget)
        if (_thirdPersonController != null && _thirdPersonController.CinemachineCameraTarget != null)
        {
            _cameraTarget = _thirdPersonController.CinemachineCameraTarget.transform;
        }
        else
        {
            _cameraTarget = transform.Find("PlayerCameraRoot");
        }

        CollectRagdollComponents();
        SetRagdollActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied += TriggerRagdollDeath;
            _healthSystem.OnRevived += ResetRagdoll;

            // If already dead when spawned
            if (_healthSystem.IsDead)
            {
                TriggerRagdollDeath();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= TriggerRagdollDeath;
            _healthSystem.OnRevived -= ResetRagdoll;
        }

        if (_deathOrbitCoroutine != null)
        {
            StopCoroutine(_deathOrbitCoroutine);
            _deathOrbitCoroutine = null;
        }
    }

    /// <summary>
    /// Finds all child rigidbodies and colliders that make up the humanoid ragdoll.
    /// </summary>
    public void CollectRagdollComponents()
    {
        _ragdollRigidbodies.Clear();
        _ragdollColliders.Clear();

        Transform root = ragdollRoot != null ? ragdollRoot : transform;
        var rbs = root.GetComponentsInChildren<Rigidbody>(true);
        var cols = root.GetComponentsInChildren<Collider>(true);

        foreach (var rb in rbs)
        {
            // Do not include the main character root rigidbody if one exists
            if (rb.gameObject == gameObject) continue;
            _ragdollRigidbodies.Add(rb);
        }

        foreach (var col in cols)
        {
            // Exclude main CharacterController or trigger colliders on root
            if (col.gameObject == gameObject || col is CharacterController) continue;
            _ragdollColliders.Add(col);
        }
    }

    /// <summary>
    /// Enables or disables ragdoll bone physics.
    /// </summary>
    public void SetRagdollActive(bool active)
    {
        _isRagdollActive = active;

        // 1. Toggle main movement controller
        if (_characterController != null)
        {
            _characterController.enabled = !active;
        }

        // 2. Toggle animators
        if (_animator != null)
        {
            _animator.enabled = !active;
        }

        // 3. Toggle bone colliders and rigidbodies
        foreach (var col in _ragdollColliders)
        {
            if (col != null) col.enabled = active;
        }

        foreach (var rb in _ragdollRigidbodies)
        {
            if (rb != null)
            {
                rb.isKinematic = !active;
                rb.detectCollisions = active;
            }
        }

        // 4. On Death: Completely disable input & movement scripts so the dead player cannot
        // move the character or orbit the camera manually, and prevent CharacterController.Move on inactive controller error.
        if (IsOwner)
        {
            if (_thirdPersonController != null) _thirdPersonController.enabled = !active;
            if (_playerInput != null)           _playerInput.enabled = !active;
            if (_starterInputs != null)
            {
                _starterInputs.move = Vector2.zero;
                _starterInputs.look = Vector2.zero;
                _starterInputs.jump = false;
                _starterInputs.sprint = false;
                _starterInputs.enabled = !active;
            }
        }
    }

    public void TriggerRagdollDeath()
    {
        if (_isRagdollActive) return;

        SetRagdollActive(true);

        // Apply slight impulse to hips so the body collapses naturally
        if (_ragdollRigidbodies.Count > 0 && _ragdollRigidbodies[0] != null)
        {
            Vector3 force = (-transform.forward * 0.5f + Vector3.up * 0.2f) * deathImpulse;
            _ragdollRigidbodies[0].AddForce(force, ForceMode.Impulse);
        }

        // Launch cinematic death camera orbit for the local player
        if (IsOwner)
        {
            if (_deathOrbitCoroutine != null) StopCoroutine(_deathOrbitCoroutine);
            _deathOrbitCoroutine = StartCoroutine(CinematicDeathCameraOrbitRoutine());
        }
    }

    /// <summary>
    /// Smoothly revolves the camera around the fallen body, tilts downward, and stops.
    /// </summary>
    private System.Collections.IEnumerator CinematicDeathCameraOrbitRoutine()
    {
        if (_cameraTarget == null) yield break;

        // Give the ragdoll ~0.2s to start collapsing to the ground
        yield return new WaitForSeconds(0.2f);

        float currentYaw = _cameraTarget.rotation.eulerAngles.y;
        float currentPitch = _cameraTarget.rotation.eulerAngles.x;
        // Normalize pitch to -180..180
        if (currentPitch > 180f) currentPitch -= 360f;

        float targetPitch = 30f; // Look down gently at the fallen body
        float totalOrbitDegrees = 200f; // Orbit around the body
        float duration = 4.0f; // Revolves slowly over 4 seconds, then stops
        float elapsed = 0f;

        // Position camera target over hips if available
        Transform focusBone = (ragdollRoot != null) ? ragdollRoot : (_ragdollRigidbodies.Count > 0 ? _ragdollRigidbodies[0].transform : null);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Smooth ease-out curve
            float ease = Mathf.Sin(t * Mathf.PI * 0.5f);

            float pitch = Mathf.Lerp(currentPitch, targetPitch, ease);
            float yaw = currentYaw + (totalOrbitDegrees * ease);

            _cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (focusBone != null)
            {
                // Smoothly follow fallen hips center
                _cameraTarget.position = Vector3.Lerp(_cameraTarget.position, focusBone.position + Vector3.up * 0.4f, Time.deltaTime * 5f);
            }

            yield return null;
        }

        // Final resting angle locked in place
        _cameraTarget.rotation = Quaternion.Euler(targetPitch, currentYaw + totalOrbitDegrees, 0f);
        _deathOrbitCoroutine = null;
    }

    public void ResetRagdoll()
    {
        if (_deathOrbitCoroutine != null)
        {
            StopCoroutine(_deathOrbitCoroutine);
            _deathOrbitCoroutine = null;
        }

        SetRagdollActive(false);

        // Reset camera target local position
        if (IsOwner && _cameraTarget != null)
        {
            _cameraTarget.localPosition = new Vector3(0f, 1.375f, 0f);
        }
    }
}
