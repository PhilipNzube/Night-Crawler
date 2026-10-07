using UnityEngine;
using StarterAssets;

/// <summary>
/// Dynamically updates the Investigator's footstep audio clips based on the surface beneath their feet.
/// Switches between mine ground (dirt/rock/stone) and minecart tracks/metal (rail/iron).
/// 
/// Works in TWO modes:
/// 1. Built-in Mode (Default): If footstepSource is left empty, it feeds clips directly into
///    ThirdPersonController.FootstepAudioClips and lets Unity play them via PlayClipAtPoint (no AudioSource required!).
/// 2. Dedicated AudioSource Mode: If you assign an AudioSource to footstepSource, it plays footsteps
///    directly through that AudioSource with full 3D spatial settings, and silences ThirdPersonController
///    so footsteps never double-play.
/// </summary>
[DisallowMultipleComponent]
public class InvestigatorSurfaceFootsteps : MonoBehaviour
{
    public enum SurfaceType
    {
        MineGround,
        RailMetal
    }

    [Header("References")]
    [Tooltip("Drag the ThirdPersonController component from this GameObject here.")]
    public ThirdPersonController controller;

    [Tooltip("Optional AudioSource. If assigned, footsteps play through this source with your custom 3D curves. If left empty, ThirdPersonController plays them automatically.")]
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
    }

    private void Start()
    {
        if (footstepSource != null && controller != null)
        {
            // If using a dedicated AudioSource, silence ThirdPersonController's built-in PlayClipAtPoint
            // so footsteps never double-play!
            controller.FootstepAudioClips = new AudioClip[0];
            controller.FootstepAudioVolume = 0f;
        }

        UpdateSurfaceAndClips(force: true);
    }

    private void Update()
    {
        UpdateSurfaceAndClips(force: false);
    }

    /// <summary>
    /// Checks ground beneath player and updates clips accordingly.
    /// </summary>
    public void UpdateSurfaceAndClips(bool force = false)
    {
        if (controller == null) return;

        // Only check when grounded or forced
        if (!force && !controller.Grounded) return;

        SurfaceType detectedSurface = DetectSurfaceUnderfoot();
        if (detectedSurface != _currentSurface || force)
        {
            _currentSurface = detectedSurface;
            ApplyClipsForCurrentSurface();
        }
    }

    /// <summary>
    /// Animation event receiver. Triggered automatically by Walk/Run animation events.
    /// </summary>
    public void OnFootstep(AnimationEvent animationEvent)
    {
        if (!enabled) return;
        if (animationEvent != null && animationEvent.animatorClipInfo.weight <= 0.5f) return;

        // If using dedicated AudioSource, play the sound here
        if (footstepSource != null)
        {
            PlayFootstepClip();
        }
    }

    private void PlayFootstepClip()
    {
        AudioClip[] targetClips = (_currentSurface == SurfaceType.RailMetal && railFootstepClips != null && railFootstepClips.Length > 0)
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
            AudioSource.PlayClipAtPoint(clip, transform.position, effectiveVol);
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

    private void ApplyClipsForCurrentSurface()
    {
        // In built-in mode (no dedicated AudioSource), feed clips directly into ThirdPersonController
        if (footstepSource == null && controller != null)
        {
            controller.FootstepAudioVolume = Mathf.Clamp01(footstepVolume);
            if (_currentSurface == SurfaceType.RailMetal && railFootstepClips != null && railFootstepClips.Length > 0)
            {
                controller.FootstepAudioClips = railFootstepClips;
            }
            else if (mineFootstepClips != null && mineFootstepClips.Length > 0)
            {
                controller.FootstepAudioClips = mineFootstepClips;
            }
        }
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
