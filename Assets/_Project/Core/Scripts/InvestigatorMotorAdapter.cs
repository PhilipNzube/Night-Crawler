using UnityEngine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// SOLID — SRP & OCP: Non-intrusive adapter for StarterAssets' ThirdPersonController.
/// Remotely configures and safeguards ground checking, layer masks, and jump buffering
/// without modifying third-party asset code.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(ThirdPersonController))]
public class InvestigatorMotorAdapter : MonoBehaviour
{
    private ThirdPersonController _controller;
    private CharacterController   _characterController;
    private StarterAssetsInputs   _inputs;
    private LayerMask             _safeGroundLayers;

    void Awake()
    {
        _controller          = GetComponent<ThirdPersonController>();
        _characterController = GetComponent<CharacterController>();
        _inputs              = GetComponent<StarterAssetsInputs>();

        ConfigureSafeParameters();
    }

    void Start()
    {
        ConfigureSafeParameters();
    }

    void Update()
    {
        if (_controller == null || _characterController == null) return;

        // Ground detection safety:
        // Ensure that if the CharacterController is not grounded and the character is airborne,
        // ThirdPersonController doesn't falsely report grounded due to internal collider overlap.
        bool nativeGrounded = _characterController.isGrounded;

        // Check ground via Raycast downward from feet to double-check slopes / rough ground
        Vector3 feetPos = transform.position + Vector3.up * 0.15f;
        bool rayGrounded = Physics.Raycast(feetPos, Vector3.down, out RaycastHit hit, 0.35f, _safeGroundLayers, QueryTriggerInteraction.Ignore);
        if (rayGrounded && (hit.transform.IsChildOf(transform) || hit.transform == transform))
        {
            rayGrounded = false; // Ignore self-collision
        }

        bool isActuallyGrounded = nativeGrounded || rayGrounded;

        // If in mid-air moving upward, strictly prevent false grounded state
        if (!isActuallyGrounded)
        {
            _controller.Grounded = false;
        }

        // Consume jump input once applied to prevent infinite jump spam in mid-air
        if (_inputs != null && _inputs.jump && !isActuallyGrounded)
        {
            _inputs.jump = false;
        }
    }

    /// <summary>
    /// Remotely tunes ThirdPersonController parameters to prevent self-collision and infinite jump glitches.
    /// </summary>
    public void ConfigureSafeParameters()
    {
        if (_controller == null) _controller = GetComponent<ThirdPersonController>();
        if (_characterController == null) _characterController = GetComponent<CharacterController>();

        if (_controller != null)
        {
            // 1. Ensure GroundedOffset is non-negative (placing the check at the base of the capsule)
            if (_controller.GroundedOffset < 0f)
            {
                _controller.GroundedOffset = Mathf.Abs(_controller.GroundedOffset);
            }
            if (_controller.GroundedOffset <= 0.01f)
            {
                _controller.GroundedOffset = 0.14f;
            }

            // 2. Build a safe GroundLayers mask that excludes the player's own layer
            int playerLayer = gameObject.layer;
            int uiLayer = LayerMask.NameToLayer("UI");
            int ignoreRaycastLayer = 2; // Ignore Raycast

            int excludeMask = (1 << playerLayer) | (1 << uiLayer) | (1 << ignoreRaycastLayer);

            if (_controller.GroundLayers.value == 0 || (_controller.GroundLayers.value & (1 << playerLayer)) != 0)
            {
                _safeGroundLayers = ~excludeMask;
                _controller.GroundLayers = _safeGroundLayers;
            }
            else
            {
                _safeGroundLayers = _controller.GroundLayers & ~excludeMask;
                _controller.GroundLayers = _safeGroundLayers;
            }
        }
    }
}
