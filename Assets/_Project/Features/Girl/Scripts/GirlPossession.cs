using UnityEngine;
using System.Collections;
using Unity.Netcode;
using Unity.Cinemachine;
using StarterAssets;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Manages the Girl's possession ability, 5-minute total possession time pool,
/// target takeover, and forced ejection / time deduction when exorcised by the Cursed Priest.
/// </summary>
public class GirlPossession : NetworkBehaviour
{
    [Header("Data (ScriptableObject)")]
    public EntityStats stats;
    public CinemachineCamera vcam;

    [Header("Possession Time Pool (5 Minutes Total)")]
    [Tooltip("Total pool of possession time in seconds across the match (default 300s = 5 minutes).")]
    public float maxPossessionTimePool = 300f;

    [Tooltip("Time deducted from her possession pool when exorcised by the Cursed Priest.")]
    public float exorcismPenaltySeconds = 30f;

    [Header("Network State")]
    public NetworkVariable<float> remainingPossessionTime = new NetworkVariable<float>(
        300f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isPossessing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("References")]
    private GirlMaterialController _matCtrl;
    private GirlStealth _stealthLogic;
    private ThirdPersonController _starterAssets;
    private CharacterController _controller;
    private Transform _girlCameraRoot;
    private IPossessable _currentTarget;

    public float RemainingPool => remainingPossessionTime.Value;
    public bool IsCurrentlyPossessing => isPossessing.Value;

    void Awake()
    {
        _matCtrl = GetComponent<GirlMaterialController>();
        _stealthLogic = GetComponent<GirlStealth>();
        _starterAssets = GetComponent<ThirdPersonController>();
        _controller = GetComponent<CharacterController>();
        _girlCameraRoot = transform.Find("PlayerCameraRoot");
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            remainingPossessionTime.Value = maxPossessionTimePool;
        }
    }

    void Update()
    {
        // Server drains possession pool while possessing
        if (IsServer && isPossessing.Value)
        {
            remainingPossessionTime.Value = Mathf.Max(0f, remainingPossessionTime.Value - Time.deltaTime);

            if (remainingPossessionTime.Value <= 0f)
            {
                // Time pool depleted -> auto eject
                EjectFromCurrentTarget();
            }
        }

        // ONLY the person controlling the Girl can trigger or release possession
        if (!IsOwner || PauseManager.IsGamePaused) return;

        bool ePressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                     || Input.GetKeyDown(KeyCode.E);

        if (ePressed)
        {
            if (!isPossessing.Value)
            {
                if (remainingPossessionTime.Value > 0f)
                {
                    TryPossess();
                }
                else
                {
                    if (NotificationManager.Instance != null)
                        NotificationManager.Instance.ShowNotification("Possession time bank depleted!", 2f);
                }
            }
            else
            {
                // Voluntarily release
                RequestReleaseServerRpc();
            }
        }
    }

    private void TryPossess()
    {
        Vector3 searchCenter = transform.position + Vector3.up;
        float range = stats != null ? stats.possessionRange : 5f;
        LayerMask mask = stats != null ? stats.possessionTargetLayer : ~0;
        Collider[] hits = Physics.OverlapSphere(searchCenter, range, mask);

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            NetworkObject targetNetObj = hit.GetComponentInParent<NetworkObject>();
            IPossessable target = hit.GetComponentInParent<IPossessable>();

            if (target != null && targetNetObj != null)
            {
                RequestPossessionServerRpc(targetNetObj.NetworkObjectId);
                StartCoroutine(PossessSequence(target));
                return;
            }
        }

        // If no target found, give feedback so the player knows to get closer
        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification("Get closer to an Investigator to Possess [E]!", 1.5f);
        }
    }

    /// <summary>
    /// Initiates possession of a specific investigator selected from the UI.
    /// </summary>
    public void PossessTargetByClientId(ulong targetClientId)
    {
        if (!IsOwner || remainingPossessionTime.Value <= 0f) return;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(targetClientId, out var client) &&
            client.PlayerObject != null)
        {
            var target = client.PlayerObject.GetComponent<IPossessable>();
            if (target != null)
            {
                RequestPossessionServerRpc(client.PlayerObject.NetworkObjectId);
                StartCoroutine(PossessSequence(target));
            }
        }
    }

    public void RequestRelease()
    {
        if (!IsOwner || !isPossessing.Value) return;
        RequestReleaseServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestPossessionServerRpc(ulong targetNetId)
    {
        isPossessing.Value = true;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var targetNetObj))
        {
            var possessable = targetNetObj.GetComponent<IPossessable>();
            if (possessable != null)
            {
                _currentTarget = possessable;
                possessable.Possess(this);
            }
        }
        NotifyPossessionClientRpc(targetNetId);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPossessionClientRpc(ulong targetNetId)
    {
        if (_matCtrl != null)
        {
            _matCtrl.RefreshVisualState();
        }
        else
        {
            ToggleRenderers(false);
        }
    }

    private Transform _swoopAnchor;

    private Transform GetSwoopAnchor()
    {
        if (_swoopAnchor == null)
        {
            var go = new GameObject("PossessionSwoopAnchor");
            _swoopAnchor = go.transform;
        }
        return _swoopAnchor;
    }

    private IEnumerator CloseTargetCinematicSwoop(Transform targetCamera, float duration = 0.45f)
    {
        if (vcam == null || targetCamera == null) yield break;

        Transform anchor = GetSwoopAnchor();

        // Start position: Close behind over-the-shoulder of the victim (1.6m back, 0.45m up)
        Vector3 backOffset = (targetCamera.forward * -1.6f) + (targetCamera.right * 0.35f) + (Vector3.up * 0.45f);
        Vector3 startPos = targetCamera.position + backOffset;
        Quaternion startRot = Quaternion.LookRotation(targetCamera.position - startPos, Vector3.up);

        anchor.position = startPos;
        anchor.rotation = startRot;

        // Immediately jump camera right behind the victim's shoulder (never clips through outside geometry)
        vcam.Follow = anchor;
        vcam.LookAt = anchor;
        vcam.OnTargetObjectWarped(anchor, Vector3.zero);

        float initialFOV = vcam.Lens.FieldOfView;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Smooth cubic ease-in-out curve
            float ease = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

            // Follow target camera dynamically even if player is moving
            Vector3 currentTargetPos = targetCamera.position;
            Vector3 dynamicStartPos = currentTargetPos + (targetCamera.forward * -1.6f) + (targetCamera.right * 0.35f) + (Vector3.up * 0.45f);

            anchor.position = Vector3.Lerp(dynamicStartPos, currentTargetPos, ease);
            anchor.rotation = Quaternion.Slerp(startRot, targetCamera.rotation, ease);

            // Subtle dramatic spirit FOV rush: dips by 5 degrees at peak, then settles back smoothly
            float fovOffset = Mathf.Sin(t * Mathf.PI) * 6f;
            var lens = vcam.Lens;
            lens.FieldOfView = initialFOV - fovOffset;
            vcam.Lens = lens;

            yield return null;
        }

        // Restore exact lens and bind directly to targetCamera
        var finalLens = vcam.Lens;
        finalLens.FieldOfView = initialFOV;
        vcam.Lens = finalLens;

        vcam.Follow = targetCamera;
        vcam.LookAt = targetCamera;
        vcam.OnTargetObjectWarped(targetCamera, Vector3.zero);
    }

    private IEnumerator PossessSequence(IPossessable target)
    {
        _currentTarget = target;
        if (_matCtrl != null)
        {
            _matCtrl.RefreshVisualState();
        }
        else
        {
            ToggleRenderers(false);
        }

        Transform targetCamera = target.GetCameraTarget();

        // Keep Girl in her current position! Freeze her local movement controls while possessing
        if (_controller != null) _controller.enabled = false;
        if (_starterAssets != null) _starterAssets.enabled = false;
        if (TryGetComponent<GirlMovement>(out var gm)) gm.enabled = false;
        if (TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi)) pi.enabled = false;

        // Ensure vcam is dynamically resolved if not wired in Inspector
        if (vcam == null)
        {
            var netPlayer = GetComponent<NetworkPlayer>();
            if (netPlayer != null && netPlayer.virtualCamera != null)
            {
                vcam = netPlayer.virtualCamera as CinemachineCamera;
            }
            if (vcam == null)
            {
                vcam = FindFirstObjectByType<CinemachineCamera>();
            }
        }

        // Play cool Cinemachine spirit swoop right at the target into position
        if (vcam != null && targetCamera != null)
        {
            yield return StartCoroutine(CloseTargetCinematicSwoop(targetCamera, 0.45f));
        }

        // Inform target (Server transfers ownership so Girl can move the victim character directly)
        target.Possess(this);

        // Show active on-display HUD for the Girl
        string victimName = "Investigator";
        var comp = target as Component;
        if (comp != null)
        {
            if (comp.TryGetComponent<NetworkPlayerName>(out var netName) && 
                !string.IsNullOrEmpty(netName.playerName.Value.ToString()) && 
                !netName.playerName.Value.ToString().StartsWith("Player "))
            {
                victimName = netName.playerName.Value.ToString();
            }
            else
            {
                var no = comp.GetComponent<NetworkObject>();
                if (no != null)
                {
                    victimName = GirlRevealManager.GetRegisteredPlayerName(no.OwnerClientId);
                    if (string.IsNullOrEmpty(victimName) || victimName.StartsWith("Player "))
                    {
                        victimName = PlayerNameManager.GetPlayerName(no.OwnerClientId);
                    }
                }

                if (string.IsNullOrEmpty(victimName) || victimName.StartsWith("Player "))
                {
                    victimName = comp.gameObject.name.Replace("(Clone)", "").Trim();
                }
            }
        }

        if (PossessionActiveHUD.Instance != null)
        {
            PossessionActiveHUD.Instance.Show(victimName, this);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestReleaseServerRpc()
    {
        if (_currentTarget != null)
        {
            _currentTarget.Release();
        }
        EjectFromCurrentTarget();
    }

    /// <summary>
    /// Forces the Girl out of the possessed body and penalizes her remaining possession pool.
    /// Called when the Cursed Priest casts Exorcism!
    /// </summary>
    public void ForceEjectByExorcism()
    {
        if (IsServer)
        {
            remainingPossessionTime.Value = Mathf.Max(0f, remainingPossessionTime.Value - exorcismPenaltySeconds);
            Debug.Log($"[GirlPossession] Exorcised by Priest! Deducted {exorcismPenaltySeconds}s. Remaining: {remainingPossessionTime.Value}s");
            EjectFromCurrentTarget();
            NotifyExorcisedClientRpc(exorcismPenaltySeconds);
        }
        else
        {
            ForceEjectServerRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void ForceEjectServerRpc()
    {
        ForceEjectByExorcism();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyExorcisedClientRpc(float penalty)
    {
        if (IsOwner && NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification($"<color=#FFD700>|</color> [EXORCISED] Exorcised by Priest! Lost {penalty:0}s possession time!", 4f);
        }
    }

    /// <summary>
    /// Smoothly forces the Girl out of a possessed body when the target dies.
    /// Does NOT deduct an exorcism penalty.
    /// </summary>
    public void ForceEjectOnTargetDeath()
    {
        if (IsServer)
        {
            EjectFromCurrentTarget();
        }
        else
        {
            ForceEjectOnTargetDeathServerRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void ForceEjectOnTargetDeathServerRpc()
    {
        EjectFromCurrentTarget();
    }

    private void EjectFromCurrentTarget()
    {
        if (!isPossessing.Value) return;
        isPossessing.Value = false;

        if (_currentTarget != null)
        {
            _currentTarget.Release();
            _currentTarget = null;
        }

        ReturnToSpiritFormClientRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReturnToSpiritFormClientRpc()
    {
        if (PossessionActiveHUD.Instance != null)
        {
            PossessionActiveHUD.Instance.Hide();
        }

        // Clean up puppet controls on the Girl's client
        if (_currentTarget != null)
        {
            if (_currentTarget is Component comp && comp.TryGetComponent<PlayerPossessableNet>(out var pnet))
            {
                pnet.TeardownGirlControl();
            }
        }

        // Restore correct visibility: preserves player's chosen manifestation state
        if (_matCtrl != null)
        {
            _matCtrl.RefreshVisualState();
        }
        else
        {
            ToggleRenderers(IsOwner);
        }

        if (IsOwner)
        {
            // The Girl is alive — ensure 'YOU DIED' is never shown on the Girl's screen
            if (DeathUI.Instance != null && DeathUI.Instance.deathCanvasGroup != null)
            {
                DeathUI.Instance.deathCanvasGroup.alpha = 0f;
                DeathUI.Instance.deathCanvasGroup.blocksRaycasts = false;
                DeathUI.Instance.deathCanvasGroup.interactable = false;
            }

            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
            }
            else if (TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi))
            {
                pi.enabled = true;
            }

            if (vcam == null)
            {
                var netPlayer = GetComponent<NetworkPlayer>();
                if (netPlayer != null && netPlayer.virtualCamera != null)
                {
                    vcam = netPlayer.virtualCamera as CinemachineCamera;
                }
                if (vcam == null)
                {
                    vcam = FindFirstObjectByType<CinemachineCamera>();
                }
            }
            if (_girlCameraRoot == null)
            {
                _girlCameraRoot = transform.Find("PlayerCameraRoot") ?? transform;
            }

            // Restore Cinemachine Virtual Camera back to the Girl's camera root
            if (vcam != null && _girlCameraRoot != null)
            {
                vcam.Follow = _girlCameraRoot;
                vcam.LookAt = _girlCameraRoot;
                vcam.OnTargetObjectWarped(_girlCameraRoot, Vector3.zero);
            }

            // Re-enable Girl controls with synchronized camera rotation
            if (_starterAssets != null)
            {
                _starterAssets.ResetTargetRotation(transform.eulerAngles.y, 0f);
                _starterAssets.enabled = true;
            }
            if (_controller != null) _controller.enabled = true;
            if (TryGetComponent<GirlMovement>(out var gm)) gm.enabled = true;

            var inputs = GetComponent<StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.move = Vector2.zero;
                inputs.look = Vector2.zero;
                inputs.jump = false;
                inputs.sprint = false;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.RestoreGirlHUD();
            }
        }
        else
        {
            if (_controller != null) _controller.enabled = true;
            if (TryGetComponent<GirlMovement>(out var gm)) gm.enabled = true;
        }
    }

    public void ReturnFromMonster(Vector3 monsterPosition)
    {
        ReturnToSpiritFormClientRpc();
    }

    private void ToggleRenderers(bool isVisible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = isVisible;
    }
}