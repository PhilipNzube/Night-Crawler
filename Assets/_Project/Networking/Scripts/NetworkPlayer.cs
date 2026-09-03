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

            // --- DISABLE REMOTE CAMERAS & PREVENT HIJACK ---
            if (virtualCamera != null)
            {
                virtualCamera.Priority = 0;
                virtualCamera.enabled = false;
                virtualCamera.gameObject.SetActive(false);
            }

            var childCameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in childCameras) cam.enabled = false;

            var childVCams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var vCam in childVCams)
            {
                vCam.Priority = 0;
                vCam.enabled = false;
                vCam.gameObject.SetActive(false);
            }

            // --- DISABLE REMOTE INPUTS & PHYSICS ---
            if (controller != null) controller.enabled = false;
            if (girlMovement != null) girlMovement.enabled = false;
            if (inputs != null) inputs.enabled = false;
            if (playerInput != null) playerInput.enabled = false;

            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.enabled = false;
            }

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
        if (cc != null) cc.enabled = false;
        if (controller != null) controller.enabled = false;

        // Raycast down from above the spawn point to find exact ground surface
        if (Physics.Raycast(transform.position + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform) && hit.transform != transform)
            {
                transform.position = hit.point + Vector3.up * 0.05f;
            }
        }

        // Wait 2 fixed updates for physics colliders to initialize
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        if (IsOwner)
        {
            if (cc != null) cc.enabled = true;
            if (controller != null)
            {
                controller.enabled = true;
                controller.Grounded = true;
            }
        }
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