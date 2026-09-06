using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using StarterAssets;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class NetworkPlayer : NetworkBehaviour
{
    public CinemachineCamera virtualCamera;
    public ThirdPersonController controller;
    public StarterAssetsInputs inputs;
    public PlayerInput playerInput;
    public GirlMovement girlMovement;

    // Tracks whether we already cleared the jump input for the current jump arc
    private bool _jumpInputCleared = false;

    void LateUpdate()
    {
        // The root bug: ThirdPersonController.Update() runs JumpAndGravity() BEFORE GroundedCheck().
        // The frame a jump fires, Grounded is still last-frame's true, the input isn't cleared,
        // and the next frame jumps again. We detect the jump via velocity.y spiking above the
        // minimum jump threshold (~6 m/s for JumpHeight=1.2, Gravity=-15) and clear input once.
        // This works even when moving, where prevVelocityY can be slightly positive from steps.
        if (IsOwner && inputs != null && controller != null)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                // sqrt(JumpHeight * -2 * Gravity) = sqrt(1.2*30) = ~6 m/s.
                // 4 m/s is safely above any slope/step climbing but below a real jump.
                const float jumpVelocityThreshold = 4f;
                if (cc.velocity.y > jumpVelocityThreshold)
                {
                    if (!_jumpInputCleared)
                    {
                        inputs.jump = false;
                        _jumpInputCleared = true;
                    }
                }
                else
                {
                    // Reset once the character has landed / velocity returned to normal
                    _jumpInputCleared = false;
                }
            }
        }
    }

    void Awake()
    {
        ResolveComponents();
    }

    private void ResolveComponents()
    {
        if (virtualCamera == null) virtualCamera = GetComponentInChildren<CinemachineCamera>(true);
        if (controller == null)    controller = GetComponent<ThirdPersonController>();
        if (inputs == null)        inputs = GetComponent<StarterAssetsInputs>();
        if (playerInput == null)   playerInput = GetComponent<PlayerInput>();
        if (girlMovement == null)  girlMovement = GetComponent<GirlMovement>();

        // NOTE: ClientNetworkTransform must be added to the prefab manually in the Editor.
    }

    public override void OnNetworkSpawn()
    {
        ResolveComponents();

        Debug.Log($"[NetworkPlayer] Spawned | Owner: {OwnerClientId} | IsOwner: {IsOwner} | IsClient: {IsClient}");

        if (IsOwner)
        {
            Debug.Log($"[NetworkPlayer] Local player ownership confirmed for {gameObject.name}");

            // --- RESOLVE CAMERA TARGET ---
            Transform target = (controller != null && controller.CinemachineCameraTarget != null)
                ? controller.CinemachineCameraTarget.transform
                : (transform.Find("PlayerCameraRoot") ?? transform);

            // --- CAMERA SETUP FOR OWNER ---
            // Find the character's existing child PlayerFollowCamera
            if (virtualCamera == null)
            {
                Transform camChild = transform.Find("PlayerFollowCamera");
                if (camChild != null)
                    virtualCamera = camChild.GetComponent<CinemachineCamera>();
            }

            if (virtualCamera == null)
                virtualCamera = GetComponentInChildren<CinemachineCamera>(true);

            if (virtualCamera != null)
            {
                // Unparent from character body so WASD turning does not spin the camera
                virtualCamera.transform.SetParent(null);

                virtualCamera.Priority = 100;
                virtualCamera.Follow = target;
                virtualCamera.gameObject.SetActive(true);
                virtualCamera.enabled = true;
            }

            // --- CONTROLLERS & INPUTS ---
            if (girlMovement != null) girlMovement.enabled = true;

            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (playerInput != null)
            {
                playerInput.enabled = true;
                playerInput.ActivateInput();
            }

            // Clear any stale jump input that the Input System may have flushed on activation
            // ThirdPersonController only clears _input.jump when NOT grounded, so a stale
            // "pressed" state can cause infinite jumping immediately on spawn.
            if (inputs != null) inputs.jump = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Ground Snap & Physics Warmup routine (prevents falling through floor on spawn)
            StartCoroutine(GroundSnapAndPhysicsWarmup());

            // --- AUDIO LISTENER ---
            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null)
            {
                listener.enabled = true;
                Debug.Log("[AUDIO] Local AudioListener is now ACTIVE for Owner.");
            }
        }
        else
        {
            Debug.Log($"[NetworkPlayer] Remote player detected: {gameObject.name} (ClientId: {OwnerClientId})");

            // --- DESTROY REMOTE CAMERAS TO PREVENT HIJACK ---
            // Clones never need cameras. Destroying their camera GameObjects permanently eliminates
            // any possibility of CinemachineBrain following or blending to a remote character clone.
            var childVCams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var vCam in childVCams)
            {
                Destroy(vCam.gameObject);
            }

            var childCameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in childCameras)
            {
                Destroy(cam.gameObject);
            }

            // --- REMOTE PHYSICS & COLLIDERS ---
            // KEEP CharacterController ENABLED so remote clones keep their physics collider!
            // This prevents them from falling through floors and allows weapon raycasts to hit them.
            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.enabled = true;
            }

            // Disable player input and local movement logic on clones
            if (controller != null)   controller.enabled = false;
            if (girlMovement != null) girlMovement.enabled = false;
            if (inputs != null)       inputs.enabled = false;
            if (playerInput != null)  playerInput.enabled = false;

            // --- INTERPOLATION FOR REMOTE PLAYERS ---
            if (TryGetComponent<NetworkTransform>(out NetworkTransform nt))
            {
                nt.Interpolate = true;
            }

            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            if (listener != null) listener.enabled = false;
        }
    }

    private System.Collections.IEnumerator GroundSnapAndPhysicsWarmup()
    {
        CharacterController cc = GetComponent<CharacterController>();

        // Snap to ground on Default layer only (layer 0), avoiding self, triggers, or props
        int groundMask = 1 << 0;
        if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform) && hit.transform != transform)
            {
                if (cc != null) cc.enabled = false;
                transform.position = hit.point + Vector3.up * 0.05f;
                if (cc != null) cc.enabled = true;
            }
        }

        if (IsOwner)
        {
            if (cc != null) cc.enabled = true;
            if (controller != null)
            {
                controller.enabled = true;
                controller.Grounded = true;
            }
        }

        yield return null;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (IsOwner && virtualCamera != null && virtualCamera.transform.parent == null)
        {
            Destroy(virtualCamera.gameObject);
        }
    }
}