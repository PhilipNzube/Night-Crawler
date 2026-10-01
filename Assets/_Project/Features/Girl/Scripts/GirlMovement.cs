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

    [Header("Audio (Footsteps)")]
    [Tooltip("AudioSource used to play footstep sounds. Drag the AudioSource component here.")]
    public AudioSource footstepSource;

    [Tooltip("Barefoot audio clips played when walking on cave dirt, rock, or stone ground.")]
    public AudioClip[] mineFootstepClips;

    [Tooltip("Audio clips played when stepping on minecart rails, iron tracks, or metallic surfaces.")]
    public AudioClip[] railFootstepClips;

    [Tooltip("Fallback footstep clips if mine or rail specific clips are unassigned.")]
    public AudioClip[] footstepClips;

    [Tooltip("Volume of footstep audio playback.")]
    [Range(0f, 1f)] public float footstepVolume = 0.4f;

    [Tooltip("Cadence/interval between footsteps while walking (seconds).")]
    public float walkStepInterval = 0.45f;

    [Tooltip("Cadence/interval between footsteps while running (seconds).")]
    public float runStepInterval = 0.3f;

    [Header("Surface Detection")]
    [Tooltip("Raycast distance downwards to check the ground surface.")]
    public float surfaceCheckDistance = 1.5f;

    [Tooltip("Layers to consider when raycasting for ground surfaces.")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("Tag that identifies metal or rail surfaces.")]
    public string metalTag = "Metal";

    [Tooltip("Keywords matched against hit GameObject or material names to identify rails/metal.")]
    public string[] railKeywords = new string[] { "rail", "metal", "track", "cart", "iron" };

    private float _stepTimer = 0f;

    private Vector3 _velocity;
    private readonly int _speedHash = Animator.StringToHash("Speed");
    private float _lastAnimSpeed = -1f;
    private int _upperBodyLayer = -2;
    private readonly int _hasWeaponHash  = Animator.StringToHash("HasWeapon");
    private readonly int _weaponIdHash   = Animator.StringToHash("WeaponID");
    private readonly int _comboStepHash  = Animator.StringToHash("ComboStep");
    private readonly int _fidgetHash     = Animator.StringToHash("IdleFidget");

    private void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        SanitizeGirlAnimator();
    }

    /// <summary>
    /// Enforces that the Girl NEVER takes any weapon, combat pose, or fidget looking animation.
    /// Clamps UpperBody_Combat layer weight to 0, forces HasWeapon = false, and blocks IdleFidget.
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

        // Enforce that the Girl NEVER plays the investigator IdleFidget animation
        animator.ResetTrigger(_fidgetHash);
        var curState = animator.GetCurrentAnimatorStateInfo(0);
        var nextState = animator.GetNextAnimatorStateInfo(0);
        if (curState.IsName("Idle_Looking") || nextState.IsName("Idle_Looking"))
        {
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
        if (!IsOwner || PauseManager.IsGamePaused || SpectatorController.IsAnySpectating) return;

        // Monster Command Triggers (Go Hunt / To My Side)
        HandleMonsterCommands();

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

        // Footstep audio cadence (plays while moving on ground)
        if (move.sqrMagnitude > 0.01f && controller.isGrounded)
        {
            _stepTimer -= Time.deltaTime;
            if (_stepTimer <= 0f)
            {
                PlayFootstep();
                _stepTimer = isRunning ? runStepInterval : walkStepInterval;
            }
        }
        else
        {
            _stepTimer = 0f;
        }

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

    /// <summary>
    /// Triggered by animation events (if on the walk/run animation) or by cadence timer above.
    /// </summary>
    public void OnFootstep(AnimationEvent animationEvent)
    {
        PlayFootstep();
    }

    public void PlayFootstep()
    {
        bool isRailOrMetal = CheckIfOnRailOrMetal();
        AudioClip[] targetClips = null;

        if (isRailOrMetal && railFootstepClips != null && railFootstepClips.Length > 0)
        {
            targetClips = railFootstepClips;
        }
        else if (mineFootstepClips != null && mineFootstepClips.Length > 0)
        {
            targetClips = mineFootstepClips;
        }
        else
        {
            targetClips = footstepClips;
        }

        if (targetClips == null || targetClips.Length == 0) return;

        int idx = Random.Range(0, targetClips.Length);
        AudioClip clip = targetClips[idx];
        if (clip == null) return;

        if (footstepSource != null)
        {
            footstepSource.PlayOneShot(clip, footstepVolume);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, footstepVolume);
        }
    }

    private bool CheckIfOnRailOrMetal()
    {
        Vector3 rayStart = transform.position + Vector3.up * 0.2f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, surfaceCheckDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            // 1. Tag check
            if (!string.IsNullOrEmpty(metalTag) && (hit.collider.CompareTag(metalTag) || hit.collider.CompareTag("Rail")))
                return true;

            // 2. Object name check
            string objName = hit.collider.gameObject.name.ToLower();
            if (MatchesRailKeyword(objName))
                return true;

            // 3. Parent name check
            if (hit.collider.transform.parent != null)
            {
                string parentName = hit.collider.transform.parent.name.ToLower();
                if (MatchesRailKeyword(parentName))
                    return true;
            }

            // 4. PhysicMaterial check
            if (hit.collider.sharedMaterial != null)
            {
                string matName = hit.collider.sharedMaterial.name.ToLower();
                if (MatchesRailKeyword(matName))
                    return true;
            }
        }
        return false;
    }

    private bool MatchesRailKeyword(string str)
    {
        if (string.IsNullOrEmpty(str) || railKeywords == null) return false;
        for (int i = 0; i < railKeywords.Length; i++)
        {
            string kw = railKeywords[i];
            if (!string.IsNullOrEmpty(kw) && str.Contains(kw.ToLower()))
                return true;
        }
        return false;
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

    private void HandleMonsterCommands()
    {
        if (GirlDealUI.IsAnyPanelOrModalOpen() || SpectatorController.IsAnySpectating) return;

        bool huntPressed = KeybindingManager.IsActionTriggered("CommandHunt")
            || (Keyboard.current != null && Keyboard.current.digit3Key.wasPressedThisFrame);

        bool recallPressed = KeybindingManager.IsActionTriggered("CommandRecall")
            || (Keyboard.current != null && Keyboard.current.digit4Key.wasPressedThisFrame);

        if (!huntPressed && !recallPressed) return;

        // If no active monsters exist, notify the Girl immediately
        if (!SpectatorController.HasActiveMonstersInScene())
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("No creatures in the mine to command!", 2.5f);
            }
            return;
        }

        if (huntPressed)
        {
            if (NightCrawler.Monsters.DeadSpawnManager.Instance != null && NetworkManager.Singleton != null)
            {
                NightCrawler.Monsters.DeadSpawnManager.Instance.CommandAllMonstersServerRpc(0, NetworkManager.Singleton.LocalClientId);
            }
        }
        else if (recallPressed)
        {
            if (NightCrawler.Monsters.DeadSpawnManager.Instance != null && NetworkManager.Singleton != null)
            {
                NightCrawler.Monsters.DeadSpawnManager.Instance.CommandAllMonstersServerRpc(1, NetworkManager.Singleton.LocalClientId);
            }
        }
    }
}