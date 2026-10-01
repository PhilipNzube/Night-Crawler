using UnityEngine;
using StarterAssets;

/// <summary>
/// Dynamically updates the Investigator's footstep audio clips based on the surface beneath their feet.
/// Switches between mine ground (dirt/rock/stone) and minecart tracks/metal (rail/iron).
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

    [Tooltip("Optional CharacterController reference. If empty, will auto-detect from this GameObject.")]
    public CharacterController characterController;

    [Header("Audio Banks")]
    [Tooltip("Clips played when walking on cave dirt, rock, or stone ground.")]
    public AudioClip[] mineFootstepClips;

    [Tooltip("Clips played when walking on minecart rails, iron tracks, or metallic surfaces.")]
    public AudioClip[] railFootstepClips;

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
        UpdateSurfaceAndClips(force: true);
    }

    private void Update()
    {
        UpdateSurfaceAndClips(force: false);
    }

    /// <summary>
    /// Checks ground beneath player and updates controller.FootstepAudioClips accordingly.
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

    private SurfaceType DetectSurfaceUnderfoot()
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, surfaceCheckDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            // 1. Tag check
            if (!string.IsNullOrEmpty(metalTag) && (hit.collider.CompareTag(metalTag) || hit.collider.CompareTag("Rail")))
            {
                return SurfaceType.RailMetal;
            }

            // 2. GameObject name check
            string objName = hit.collider.gameObject.name.ToLower();
            if (MatchesKeyword(objName))
            {
                return SurfaceType.RailMetal;
            }

            // 3. Parent GameObject name check
            if (hit.collider.transform.parent != null)
            {
                string parentName = hit.collider.transform.parent.name.ToLower();
                if (MatchesKeyword(parentName))
                {
                    return SurfaceType.RailMetal;
                }
            }

            // 4. PhysicMaterial check
            if (hit.collider.sharedMaterial != null)
            {
                string matName = hit.collider.sharedMaterial.name.ToLower();
                if (MatchesKeyword(matName))
                {
                    return SurfaceType.RailMetal;
                }
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
        if (controller == null) return;

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
