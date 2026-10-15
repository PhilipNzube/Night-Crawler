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

    [Header("Exorcism & Fire VFX (Girl / Special Death)")]
    [Tooltip("Optional ParticleSystem already placed on the character (e.g. on hips or chest) to activate on death/exorcism.")]
    public ParticleSystem exorcismParticleSystem;

    [Tooltip("Optional fire/exorcism prefab to instantiate upon death.")]
    public GameObject exorcismFirePrefab;

    [Tooltip("Optional transform point to attach or spawn the fire effect. If unassigned, uses ragdollRoot or character root.")]
    public Transform exorcismFireAttachPoint;

    [Tooltip("If true, attaches the instantiated fire prefab to the attach point so it moves as the body collapses.")]
    public bool attachFireToBody = true;

    [Header("Exorcism Fire Orientation & Placement")]
    [Tooltip("If true, keeps the fire emitter oriented upright in world space as the ragdoll collapses, so flames always burn upward into the air (away from the floor) instead of rotating with the falling body.")]
    public bool keepFireUprightOnDeath = true;

    [Tooltip("Vertical height offset in meters above the hips/torso to ensure the flame emits cleanly from the upper surface of the fallen body.")]
    public float fireVerticalOffset = 0.15f;

    [Tooltip("Optional rotation Euler angle offset for fire alignment if your VFX asset uses forward (+Z) instead of up (+Y) as its flame axis.")]
    public Vector3 fireRotationOffset = Vector3.zero;

    [Tooltip("If true, automatically hides the fire particle system while the character is alive so it only bursts into flame upon death.")]
    public bool hideFireWhileAlive = true;

    private Quaternion _standingFireWorldRotation = Quaternion.identity;
    private bool _hasStandingFireRotation = false;
    private Transform _activeFireTransform;

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

        // Cache the upright standing fire orientation and guarantee it is turned OFF while alive
        if (exorcismParticleSystem != null)
        {
            _standingFireWorldRotation = exorcismParticleSystem.transform.rotation;
            _hasStandingFireRotation = true;

            exorcismParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!_isRagdollActive)
            {
                exorcismParticleSystem.gameObject.SetActive(false);
            }
        }
    }

    private void Start()
    {
        if (exorcismParticleSystem != null && !_isRagdollActive)
        {
            exorcismParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            exorcismParticleSystem.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Enforce that the exorcism fire remains completely deactivated while the character is alive
        if (!_isRagdollActive && exorcismParticleSystem != null && (exorcismParticleSystem.gameObject.activeSelf || exorcismParticleSystem.isPlaying))
        {
            exorcismParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            exorcismParticleSystem.gameObject.SetActive(false);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (exorcismParticleSystem != null && !_isRagdollActive)
        {
            exorcismParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            exorcismParticleSystem.gameObject.SetActive(false);
        }

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

        if (IsServer)
        {
            TriggerRagdollDeathClientRpc();
        }
        else
        {
            ExecuteRagdollDeath();
        }
    }

    [ClientRpc]
    private void TriggerRagdollDeathClientRpc()
    {
        ExecuteRagdollDeath();
    }

    private void ExecuteRagdollDeath()
    {
        if (_isRagdollActive) return;

        SetRagdollActive(true);

        // Apply slight impulse to hips so the body collapses naturally
        if (_ragdollRigidbodies.Count > 0 && _ragdollRigidbodies[0] != null)
        {
            Vector3 force = (-transform.forward * 0.5f + Vector3.up * 0.2f) * deathImpulse;
            _ragdollRigidbodies[0].AddForce(force, ForceMode.Impulse);
        }

        // If this character is the Girl, ensure her mesh is fully materialized & visible so her fall is seen
        if (TryGetComponent<GirlMaterialController>(out var girlMat))
        {
            girlMat.ForceManifestedOnDeath();
        }

        // Disable Girl movement script so ragdoll physics take over
        if (TryGetComponent<GirlMovement>(out var girlMove))
        {
            girlMove.enabled = false;
        }

        // Play attached ParticleSystem fire effect if assigned
        if (exorcismParticleSystem != null)
        {
            if (!_hasStandingFireRotation)
            {
                _standingFireWorldRotation = exorcismParticleSystem.transform.rotation;
                _hasStandingFireRotation = true;
            }

            _activeFireTransform = exorcismParticleSystem.transform;
            exorcismParticleSystem.gameObject.SetActive(true);

            // Set all child ParticleSystems to World Simulation Space so flames/smoke drift upward naturally
            var allPs = exorcismParticleSystem.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in allPs)
            {
                if (ps == null) continue;
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }

            exorcismParticleSystem.Clear(true);
            exorcismParticleSystem.Play(true);
        }

        // Spawn fire prefab if assigned
        if (exorcismFirePrefab != null)
        {
            Transform spawnPoint = exorcismFireAttachPoint != null ? exorcismFireAttachPoint : (ragdollRoot != null ? ragdollRoot : transform);
            GameObject fireObj = Instantiate(exorcismFirePrefab, spawnPoint.position, spawnPoint.rotation);
            _activeFireTransform = fireObj.transform;
            if (attachFireToBody)
            {
                fireObj.transform.SetParent(spawnPoint);
            }
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
        if (_cameraTarget == null)
        {
            _cameraTarget = transform.Find("PlayerCameraRoot");
            if (_cameraTarget == null) _cameraTarget = transform;
        }

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

        // Engage spectator mode after death orbit completes (fail-safe for investigators).
        // The Girl never enters spectator mode.
        bool isGirl = GetComponent<GirlStealth>() != null || GetComponent<GirlMaterialController>() != null || (transform.name.ToLower().Contains("girl") || transform.name.ToLower().Contains("wraith"));
        if (!isGirl && SpectatorController.Instance != null && !SpectatorController.Instance.IsSpectating)
        {
            if (DeathUI.Instance != null && DeathUI.Instance.HasAliveSurvivorsToSpectate())
            {
                SpectatorController.Instance.StartSpectating();
            }
        }
    }

    private void LateUpdate()
    {
        if (!_isRagdollActive || _activeFireTransform == null || !keepFireUprightOnDeath) return;

        // Find core body bone (Hips/Spine)
        Transform focusBone = exorcismFireAttachPoint != null 
            ? exorcismFireAttachPoint 
            : ((ragdollRoot != null) ? ragdollRoot : (_ragdollRigidbodies.Count > 0 ? _ragdollRigidbodies[0].transform : transform));

        if (focusBone != null)
        {
            // Position fire at the core bone + upward offset so it sits cleanly on the upper surface of the fallen body
            _activeFireTransform.position = focusBone.position + Vector3.up * fireVerticalOffset;

            // Keep fire rotation locked upright in world space so flames always billow upward towards the sky
            if (_hasStandingFireRotation)
            {
                _activeFireTransform.rotation = Quaternion.Euler(
                    _standingFireWorldRotation.eulerAngles.x + fireRotationOffset.x,
                    _standingFireWorldRotation.eulerAngles.y + fireRotationOffset.y,
                    _standingFireWorldRotation.eulerAngles.z + fireRotationOffset.z
                );
            }
            else
            {
                _activeFireTransform.rotation = Quaternion.Euler(fireRotationOffset);
            }
        }
    }

    public void ResetRagdoll()
    {
        if (_deathOrbitCoroutine != null)
        {
            StopCoroutine(_deathOrbitCoroutine);
            _deathOrbitCoroutine = null;
        }

        if (exorcismParticleSystem != null)
        {
            exorcismParticleSystem.Stop(true);
            if (hideFireWhileAlive)
            {
                exorcismParticleSystem.gameObject.SetActive(false);
            }
        }
        _activeFireTransform = null;

        SetRagdollActive(false);

        // Reset camera target local position
        if (IsOwner && _cameraTarget != null)
        {
            _cameraTarget.localPosition = new Vector3(0f, 1.375f, 0f);
        }
    }
}
