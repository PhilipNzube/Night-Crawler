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

        // NOTE: ClientNetworkTransform must be added to the prefab manually in the Editor,
        // NOT dynamically here. Adding it at runtime in Awake() before network spawn causes
        // ThirdPersonController to fight it for rotation authority, breaking camera movement.

        // If this is an investigator using ThirdPersonController, attach the motor adapter to fix ground/jump
        if (controller != null && !TryGetComponent<InvestigatorMotorAdapter>(out _))
        {
            gameObject.AddComponent<InvestigatorMotorAdapter>();
        }
    }

    public override void OnNetworkSpawn()
    {
        ResolveComponents();

        Debug.Log($"[NetworkPlayer] Spawned | Owner: {OwnerClientId} | IsOwner: {IsOwner} | IsClient: {IsClient}");

        if (IsOwner)
        {
            Debug.Log($"[NetworkPlayer] Local player ownership confirmed for {gameObject.name}");

            // --- CAMERA SETUP ---
            // Re-search in case Awake ran before camera child was ready
            if (virtualCamera == null)
                virtualCamera = GetComponentInChildren<CinemachineCamera>(true);

            if (virtualCamera != null)
            {
                virtualCamera.Priority = 100;
                virtualCamera.gameObject.SetActive(true);
                virtualCamera.enabled = true;

                // Only set Follow/LookAt if not already wired in the prefab
                if (virtualCamera.Follow == null)
                {
                    Transform target = (controller != null && controller.CinemachineCameraTarget != null)
                        ? controller.CinemachineCameraTarget.transform
                        : (transform.Find("PlayerCameraRoot") ?? transform);
                    virtualCamera.Follow = target;
                    virtualCamera.LookAt = target;
                }
            }

            // --- CONTROLLERS & INPUTS ---
            if (controller != null) controller.enabled = true;
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

            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.enabled = true;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

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
}