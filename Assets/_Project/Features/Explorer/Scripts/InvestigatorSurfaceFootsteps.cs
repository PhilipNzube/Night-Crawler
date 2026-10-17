using UnityEngine;
using StarterAssets;
using Unity.Netcode;

/// <summary>
/// Controls the Investigator's footstep audio with dynamic surface detection (Mine Ground vs Rail/Metal),
/// a reliable movement cadence timer (identical to GirlMovement), and full network synchronization.
/// Replicates footsteps to all players across the network so investigators can hear each other.
/// </summary>
[DisallowMultipleComponent]
public class InvestigatorSurfaceFootsteps : NetworkBehaviour
{
    public enum SurfaceType
    {
        MineGround,
        RailMetal
    }

    [Header("References")]
    [Tooltip("Drag the ThirdPersonController component from this GameObject here.")]
    public ThirdPersonController controller;

    [Tooltip("Optional AudioSource. If assigned, footsteps play through this source with custom 3D curves. If unassigned, plays via PlayClipAtPoint.")]
    public AudioSource footstepSource;

    [Tooltip("Optional CharacterController reference. If empty, will auto-detect from this GameObject.")]
    public CharacterController characterController;

    [Header("Audio Banks")]
    [Tooltip("Clips played when walking on cave dirt, rock, or stone ground.")]
    public AudioClip[] mineFootstepClips;

    [Tooltip("Clips played when walking on minecart rails, iron tracks, or metallic surfaces.")]
    public AudioClip[] railFootstepClips;

    [Tooltip("Volume for footstep playback. Values above 1.0 provide extra audio boost for quiet clips.")]
    [Range(0f, 2.5f)] public float footstepVolume = 0.9f;

    [Header("Cadence Settings (Movement Timer)")]
    [Tooltip("Cadence/interval between footsteps while walking (seconds).")]
    public float walkStepInterval = 0.44f;

    [Tooltip("Cadence/interval between footsteps while running/sprinting (seconds).")]
    public float runStepInterval = 0.28f;

    [Header("Surface Detection")]
    [Tooltip("Raycast distance downwards to check the ground surface.")]
    public float surfaceCheckDistance = 1.5f;

    [Tooltip("Layers to consider when raycasting for ground surfaces.")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("Tag that identifies metal or rail surfaces.")]
    public string metalTag = "Metal";

    [Tooltip("Keywords matched against hit GameObject or material names to identify rails/metal.")]
    public string[] railKeywords = new string[] { "rail", "metal", "track", "cart", "iron" };

    [Header("Debug / Status")]
    [SerializeField] private SurfaceType _currentSurface = SurfaceType.MineGround;
    public SurfaceType CurrentSurface => _currentSurface;

    private StarterAssetsInputs _inputs;
    private TargetHealth _targetHealth;
    private HealthSystem _healthSystem;
    private float _stepTimer = 0f;
    private float _lastStepTime = 0f;
    private Vector3 _lastPosition;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<ThirdPersonController>();
        }
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
        if (_inputs == null)
        {
            _inputs = GetComponent<StarterAssetsInputs>();
        }
        _targetHealth = GetComponent<TargetHealth>();
        _healthSystem = GetComponent<HealthSystem>();
        _lastPosition = transform.position;
    }

    private void Start()
    {
        // Silence ThirdPersonController's built-in un-networked footstep player
        // so our synchronized cadence and surface audio plays cleanly without double-triggering.
        if (controller != null)
        {
            controller.FootstepAudioClips = new AudioClip[0];
            controller.FootstepAudioVolume = 0f;
        }

        UpdateSurfaceAndClips(force: true);
    }

    private void Update()
    {
        // Check ground surface periodically
        UpdateSurfaceAndClips(force: false);

        // Footstep cadence runs authoritatively on the local owning player
        if (!IsOwner) return;

        // Dead investigators don't make footstep sounds
        if (_targetHealth != null && (_targetHealth.isCorpse.Value || _targetHealth.CurrentHealth <= 0)) return;
        if (_healthSystem != null && _healthSystem.IsDead) return;

        bool isGrounded = (characterController != null && characterController.isGrounded)
                       || (controller != null && controller.Grounded);

        float horizontalSpeed = characterController != null
            ? new Vector3(characterController.velocity.x, 0f, characterController.velocity.z).magnitude
            : 0f;

        // Position delta fallback if characterController.velocity is 0 on certain frames
        float posDeltaSpeed = (new Vector3(transform.position.x - _lastPosition.x, 0f, transform.position.z - _lastPosition.z).magnitude) / Mathf.Max(Time.deltaTime, 0.001f);
        _lastPosition = transform.position;

        float effectiveSpeed = Mathf.Max(horizontalSpeed, posDeltaSpeed);
        bool hasMoveInput = _inputs != null && _inputs.move.sqrMagnitude > 0.01f;
        bool isMoving = isGrounded && (effectiveSpeed > 0.18f || (hasMoveInput && effectiveSpeed > 0.05f));

        if (isMoving)
        {
            bool isRunning = (_inputs != null && _inputs.sprint) || effectiveSpeed > 3.2f;
            _stepTimer -= Time.deltaTime;
            if (_stepTimer <= 0f)
            {
                TriggerFootstep();
                _stepTimer = isRunning ? runStepInterval : walkStepInterval;
            }
        }
        else
        {
            _stepTimer = 0f;
        }
    }

    /// <summary>
    /// Checks ground beneath player and updates current surface enum.
    /// </summary>
    public void UpdateSurfaceAndClips(bool force = false)
    {
        bool isGrounded = (characterController != null && characterController.isGrounded)
                       || (controller != null && controller.Grounded);

        if (!force && !isGrounded) return;

        SurfaceType detectedSurface = DetectSurfaceUnderfoot();
        if (detectedSurface != _currentSurface || force)
        {
            _currentSurface = detectedSurface;
        }
    }

    /// <summary>
    /// Animation event receiver. Triggered by Walk/Run animation events if present on animation clips.
    /// Uses a small time-guard so it cooperates smoothly with the cadence timer without double-playing.
    /// </summary>
    public void OnFootstep(AnimationEvent animationEvent)
    {
        if (!enabled) return;
        if (!IsOwner) return;
        if (animationEvent != null && animationEvent.animatorClipInfo.weight <= 0.5f) return;

        if (Time.time - _lastStepTime >= 0.16f)
        {
            TriggerFootstep();
        }
    }

    private void TriggerFootstep()
    {
        _lastStepTime = Time.time;
        bool isRailOrMetal = (_currentSurface == SurfaceType.RailMetal);

        // 1. Play locally on owner immediately with zero latency
        PlayFootstepAudio(isRailOrMetal, transform.position);

        // 2. Replicate across network to all remote clients
        if (IsSpawned)
        {
            PlayFootstepServerRpc(isRailOrMetal, transform.position);
        }
    }

    [ServerRpc]
    private void PlayFootstepServerRpc(bool isRailOrMetal, Vector3 position)
    {
        PlayFootstepClientRpc(isRailOrMetal, position);
    }

    [ClientRpc]
    private void PlayFootstepClientRpc(bool isRailOrMetal, Vector3 position)
    {
        // Owner already played locally
        if (IsOwner) return;

        PlayFootstepAudio(isRailOrMetal, position);
    }

    public void PlayFootstepAudio(bool isRailOrMetal, Vector3 position)
    {
        AudioClip[] targetClips = (isRailOrMetal && railFootstepClips != null && railFootstepClips.Length > 0)
            ? railFootstepClips
            : mineFootstepClips;

        if (targetClips == null || targetClips.Length == 0) return;

        int idx = Random.Range(0, targetClips.Length);
        AudioClip clip = targetClips[idx];
        if (clip == null) return;

        float effectiveVol = footstepVolume * GameSettingsManager.SFXVolume;
        if (footstepSource != null)
        {
            footstepSource.PlayOneShot(clip, effectiveVol);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, position, effectiveVol);
        }
    }

    private SurfaceType DetectSurfaceUnderfoot()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, surfaceCheckDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            // 1. GameObject name check (e.g. 'Rail (1)', 'Minecart', 'IronBar')
            string objName = hit.collider.gameObject.name.ToLower();
            if (MatchesKeyword(objName))
            {
                return SurfaceType.RailMetal;
            }

            // 2. Parent GameObject name check
            if (hit.collider.transform.parent != null)
            {
                string parentName = hit.collider.transform.parent.name.ToLower();
                if (MatchesKeyword(parentName))
                {
                    return SurfaceType.RailMetal;
                }
            }

            // 3. PhysicMaterial check
            if (hit.collider.sharedMaterial != null)
            {
                string matName = hit.collider.sharedMaterial.name.ToLower();
                if (MatchesKeyword(matName))
                {
                    return SurfaceType.RailMetal;
                }
            }

            // 4. Safe Tag check (surrounded in try-catch so undefined tags never throw exceptions)
            if (SafeCompareTag(hit.collider, metalTag) || SafeCompareTag(hit.collider, "Rail"))
            {
                return SurfaceType.RailMetal;
            }
        }

        return SurfaceType.MineGround;
    }

    private bool MatchesKeyword(string str)
    {
        if (string.IsNullOrEmpty(str) || railKeywords == null) return false;
        for (int i = 0; i < railKeywords.Length; i++)
        {
            string kw = railKeywords[i];
            if (!string.IsNullOrEmpty(kw) && str.Contains(kw.ToLower()))
            {
                return true;
            }
        }
        return false;
    }

    private bool SafeCompareTag(Component comp, string tag)
    {
        if (comp == null || string.IsNullOrEmpty(tag)) return false;
        try
        {
            return string.Equals(comp.gameObject.tag, tag, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
