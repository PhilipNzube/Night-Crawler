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

    // Direct Input System action references to bypass broken prefab event wirings
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;

    // Tracks whether we already cleared the jump input for the current jump arc
    private bool _jumpInputCleared = false;

    void LateUpdate()
    {
        // ThirdPersonController.Update() runs JumpAndGravity() BEFORE GroundedCheck().
        // Clear jump input once velocity.y spikes above the jump threshold.
        if (IsOwner && inputs != null && controller != null)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
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
                    _jumpInputCleared = false;
                }
            }
        }
    }

    void Awake()
    {
        CleanupDuplicateMainCameraTags();
        ResolveComponents();

        // Immediately neutralize camera before CinemachineBrain can evaluate or blend to it
        if (virtualCamera != null)
        {
            virtualCamera.enabled = false;
            virtualCamera.Priority = -9999;
        }
    }

    /// <summary>
    /// Ensures only the camera with CinemachineBrain holds the "MainCamera" tag.
    /// Untags any secondary/demo cameras in the scene that lack a CinemachineBrain.
    /// </summary>
    private static void CleanupDuplicateMainCameraTags()
    {
        var mainCams = GameObject.FindGameObjectsWithTag("MainCamera");
        if (mainCams != null && mainCams.Length > 1)
        {
            foreach (var camObj in mainCams)
            {
                if (camObj != null && !camObj.TryGetComponent<CinemachineBrain>(out _))
                {
                    camObj.tag = "Untagged";
                    Debug.Log($"[NetworkPlayer] Untagged duplicate camera '{camObj.name}' to prevent Camera.main collision.");
                }
            }
        }
    }

    private void ResolveComponents()
    {
        if (virtualCamera == null) virtualCamera = GetComponentInChildren<CinemachineCamera>(true);
        if (controller == null)    controller = GetComponent<ThirdPersonController>();
        if (inputs == null)        inputs = GetComponent<StarterAssetsInputs>();
        if (playerInput == null)   playerInput = GetComponent<PlayerInput>();
        if (girlMovement == null)  girlMovement = GetComponent<GirlMovement>();
    }

    public override void OnNetworkSpawn()
    {
        ResolveComponents();
        CleanupDuplicateMainCameraTags();

        Debug.Log($"[NetworkPlayer] Spawned | Owner: {OwnerClientId} | IsOwner: {IsOwner} | IsClient: {IsClient}");

        if (IsOwner)
        {
            Debug.Log($"[NetworkPlayer] Local player ownership confirmed for {gameObject.name} (ClientId: {OwnerClientId})");

            // --- RESOLVE CAMERA TARGET ---
            Transform target = (controller != null && controller.CinemachineCameraTarget != null)
                ? controller.CinemachineCameraTarget.transform
                : (transform.Find("PlayerCameraRoot") ?? transform);

            // --- CAMERA SETUP FOR OWNER ---
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
                // Unparent and rename uniquely to prevent name collisions
                virtualCamera.transform.SetParent(null);
                virtualCamera.gameObject.name = $"PlayerFollowCamera_Local_{OwnerClientId}";

                virtualCamera.Priority = 1000;
                virtualCamera.Follow = target;
                virtualCamera.LookAt = target;
                virtualCamera.gameObject.SetActive(true);
                virtualCamera.enabled = true;

                // Force CinemachineBrain to instantly cut to the local player camera
                CinemachineBrain brain = Camera.main != null
                    ? Camera.main.GetComponent<CinemachineBrain>()
                    : FindFirstObjectByType<CinemachineBrain>();

                if (brain != null)
                {
                    brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                    brain.ActiveBlend = null;
                }
            }

            // --- ENSURE CONTROLLER HAS VALID MAIN CAMERA ---
            if (controller != null)
            {
                var camField = typeof(ThirdPersonController).GetField("_mainCamera",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (camField != null)
                {
                    GameObject currentCam = camField.GetValue(controller) as GameObject;
                    if (currentCam == null || !currentCam.TryGetComponent<CinemachineBrain>(out _))
                    {
                        var brain = FindFirstObjectByType<CinemachineBrain>();
                        if (brain != null)
                        {
                            camField.SetValue(controller, brain.gameObject);
                        }
                    }
                }
            }

            // --- CONTROLLERS & INPUTS ---
            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.detectCollisions = true;
                cc.enabled = true;
            }

            if (controller != null)   controller.enabled = true;
            if (girlMovement != null) girlMovement.enabled = true;

            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
                inputs.jump = false;
            }

            // Dynamically bind InputSystem actions to StarterAssetsInputs in C#
            // This completely bypasses the broken serialized prefab UnityEvents that pointed to Demon.prefab
            SetupOwnerInput();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

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

            // --- IMMEDIATELY NEUTRALIZE REMOTE CAMERAS ---
            // Clones must NEVER be evaluated by CinemachineBrain.
            // Deprioritize, decouple targets, disable and deactivate immediately before Destroy().
            var childVCams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var vCam in childVCams)
            {
                vCam.Priority = -9999;
                vCam.Follow = null;
                vCam.LookAt = null;
                vCam.enabled = false;
                vCam.gameObject.SetActive(false);
                Destroy(vCam.gameObject);
            }

            var childCameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in childCameras)
            {
                cam.enabled = false;
                cam.gameObject.SetActive(false);
                Destroy(cam.gameObject);
            }

            // --- REMOTE PHYSICS & SOLID COLLIDERS ---
            // Keep CharacterController enabled and keep detectCollisions = true
            // so the clone is physically solid and neither players nor objects can pass straight through it!
            if (TryGetComponent<CharacterController>(out CharacterController cc))
            {
                cc.detectCollisions = true;
                cc.enabled = true;
            }

            // Clones never run local movement updates or accept input
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

    /// <summary>
    /// Dynamically hooks PlayerInput C# action events to the local character's StarterAssetsInputs.
    /// This fixes the bug where investigator prefabs were hard-linked to Demon.prefab in UnityEvents.
    /// </summary>
    private void SetupOwnerInput()
    {
        if (playerInput == null || inputs == null) return;

        // Switch notification behavior to C# events so broken serialized UnityEvents are ignored
        playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
        playerInput.defaultActionMap = "Player";

        var playerMap = playerInput.actions?.FindActionMap("Player");
        if (playerMap != null && !playerMap.enabled)
        {
            playerMap.Enable();
        }

        TeardownInputActions();

        if (playerInput.actions != null)
        {
            _moveAction = playerInput.actions.FindAction("Move");
            if (_moveAction != null)
            {
                _moveAction.performed += OnMovePerformed;
                _moveAction.canceled += OnMoveCanceled;
                _moveAction.Enable();
            }

            _lookAction = playerInput.actions.FindAction("Look");
            if (_lookAction != null)
            {
                _lookAction.performed += OnLookPerformed;
                _lookAction.canceled += OnLookCanceled;
                _lookAction.Enable();
            }

            _jumpAction = playerInput.actions.FindAction("Jump");
            if (_jumpAction != null)
            {
                _jumpAction.performed += OnJumpPerformed;
                _jumpAction.canceled += OnJumpCanceled;
                _jumpAction.Enable();
            }

            _sprintAction = playerInput.actions.FindAction("Sprint");
            if (_sprintAction != null)
            {
                _sprintAction.performed += OnSprintPerformed;
                _sprintAction.canceled += OnSprintCanceled;
                _sprintAction.Enable();
            }
        }

        playerInput.enabled = true;
        playerInput.ActivateInput();
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.MoveInput(ctx.ReadValue<Vector2>());
    }

    private void OnMoveCanceled(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.MoveInput(Vector2.zero);
    }

    private void OnLookPerformed(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.LookInput(ctx.ReadValue<Vector2>());
    }

    private void OnLookCanceled(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.LookInput(Vector2.zero);
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.JumpInput(ctx.ReadValueAsButton());
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.JumpInput(false);
    }

    private void OnSprintPerformed(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.SprintInput(ctx.ReadValueAsButton());
    }

    private void OnSprintCanceled(InputAction.CallbackContext ctx)
    {
        if (inputs != null) inputs.SprintInput(false);
    }

    private void TeardownInputActions()
    {
        if (_moveAction != null)
        {
            _moveAction.performed -= OnMovePerformed;
            _moveAction.canceled -= OnMoveCanceled;
            _moveAction = null;
        }

        if (_lookAction != null)
        {
            _lookAction.performed -= OnLookPerformed;
            _lookAction.canceled -= OnLookCanceled;
            _lookAction = null;
        }

        if (_jumpAction != null)
        {
            _jumpAction.performed -= OnJumpPerformed;
            _jumpAction.canceled -= OnJumpCanceled;
            _jumpAction = null;
        }

        if (_sprintAction != null)
        {
            _sprintAction.performed -= OnSprintPerformed;
            _sprintAction.canceled -= OnSprintCanceled;
            _sprintAction = null;
        }
    }

    private System.Collections.IEnumerator GroundSnapAndPhysicsWarmup()
    {
        CharacterController cc = GetComponent<CharacterController>();

        int mask = ~LayerMask.GetMask("Ignore Raycast", "UI");
        if (controller != null && controller.GroundLayers.value != 0)
        {
            mask = controller.GroundLayers.value;
        }

        Vector3 origin = transform.position + Vector3.up * 2f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 25f, mask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform) && hit.transform != transform)
            {
                if (cc != null) cc.enabled = false;
                transform.position = hit.point + Vector3.up * 0.05f;
                if (cc != null) cc.enabled = true;
                Debug.Log($"[NetworkPlayer] GroundSnap succeeded for {gameObject.name} at {transform.position}");
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

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        TeardownInputActions();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        TeardownInputActions();
        if (IsOwner && virtualCamera != null && virtualCamera.transform.parent == null)
        {
            Destroy(virtualCamera.gameObject);
        }
    }
}