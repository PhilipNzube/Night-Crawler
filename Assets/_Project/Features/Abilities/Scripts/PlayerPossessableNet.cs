using UnityEngine;
using Unity.Netcode;
using StarterAssets;

/// <summary>
/// SOLID — SRP: Enables any player investigator to be possessed by the Girl.
/// Synchronizes possession status across the network. When possessed, victim screen
/// blacks out with "Let me take the wheel for a sec☠️" and local controls are locked.
/// </summary>
public class PlayerPossessableNet : NetworkBehaviour, IPossessable
{
    [Header("Camera Target")]
    public Transform cameraTarget;

    [Header("Network State")]
    public NetworkVariable<bool> isPossessed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> possessingClientId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CharacterController _characterController;
    private ThirdPersonController _thirdPersonController;
    private InvestigatorCombatNet _combatNet;
    private GirlPossession _activeGirlRef;

    public bool IsPossessed => isPossessed.Value;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _thirdPersonController = GetComponent<ThirdPersonController>();
        _combatNet = GetComponent<InvestigatorCombatNet>();
    }

    public override void OnNetworkSpawn()
    {
        isPossessed.OnValueChanged += HandlePossessionChanged;
    }

    public override void OnNetworkDespawn()
    {
        isPossessed.OnValueChanged -= HandlePossessionChanged;
    }

    private Coroutine _priestRejectionCoroutine;

    private void HandlePossessionChanged(bool previous, bool current)
    {
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 999999;
        bool isLocalVictim = (IsOwner && !isPossessed.Value) || (originalOwnerClientId.Value == localId);

        if (isLocalVictim || IsOwner)
        {
            if (current)
            {
                // Lock victim inputs
                if (_thirdPersonController != null) _thirdPersonController.enabled = false;
                if (_combatNet != null) _combatNet.enabled = false;

                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(true);
                }

                // Check if this character is a Priest with Exorcism capability
                bool isPriest = (TryGetComponent<PriestExorcismNet>(out var priest) && priest.isUnlocked) 
                             || gameObject.name.ToLower().Contains("priest");

                if (isPriest)
                {
                    if (_priestRejectionCoroutine != null) StopCoroutine(_priestRejectionCoroutine);
                    _priestRejectionCoroutine = StartCoroutine(PriestRejectionWindowRoutine(4.0f));
                }
                else
                {
                    NotifyPossessionAcceptedServerRpc();
                }
            }
            else
            {
                if (_priestRejectionCoroutine != null)
                {
                    StopCoroutine(_priestRejectionCoroutine);
                    _priestRejectionCoroutine = null;
                }

                // Restore victim inputs
                if (_thirdPersonController != null) _thirdPersonController.enabled = true;
                if (_combatNet != null) _combatNet.enabled = true;

                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(false);
                }
            }
        }
    }

    private System.Collections.IEnumerator PriestRejectionWindowRoutine(float windowDuration)
    {
        if (PossessionBlackoutOverlay.Instance != null)
        {
            PossessionBlackoutOverlay.Instance.ShowRejectionPrompt(windowDuration);
        }

        float elapsed = 0f;
        bool rejected = false;

        while (elapsed < windowDuration && isPossessed.Value)
        {
            elapsed += Time.deltaTime;

            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                bool rPressed = UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame;
                bool spacePressed = UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;

                if (rPressed || spacePressed)
                {
                    rejected = true;
                    break;
                }
            }
            else if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space))
            {
                rejected = true;
                break;
            }

            yield return null;
        }

        if (PossessionBlackoutOverlay.Instance != null)
        {
            PossessionBlackoutOverlay.Instance.HideRejectionPrompt();
        }

        if (rejected)
        {
            Debug.Log("[PlayerPossessableNet] Priest successfully rejected possession!");
            RejectPossessionServerRpc();
        }
        else if (isPossessed.Value)
        {
            Debug.Log("[PlayerPossessableNet] Priest did not reject in time. Possession confirmed.");
            NotifyPossessionAcceptedServerRpc();
        }

        _priestRejectionCoroutine = null;
    }

    public NetworkVariable<ulong> originalOwnerClientId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Rpc(SendTo.Server)]
    private void RejectPossessionServerRpc()
    {
        string victimName = GirlRevealManager.GetRegisteredPlayerName(OwnerClientId);
        if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(OwnerClientId);
        if (string.IsNullOrEmpty(victimName)) victimName = gameObject.name.Replace("(Clone)", "").Trim();

        ForceExorcise();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.BroadcastMatchEventClientRpc(
                $"<color=#FFD700>|</color> [EXORCISED] {victimName} (Priest) purged the spirit & rejected possession!",
                new Color(1f, 0.85f, 0.2f, 1f));
        }
    }

    [Rpc(SendTo.Server)]
    private void NotifyPossessionAcceptedServerRpc()
    {
        string victimName = GirlRevealManager.GetRegisteredPlayerName(OwnerClientId);
        if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(OwnerClientId);
        if (string.IsNullOrEmpty(victimName)) victimName = gameObject.name.Replace("(Clone)", "").Trim();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.BroadcastMatchEventClientRpc(
                $"<color=#B388FF>|</color> [POSSESSED] The Vengeful Spirit took possession of {victimName}!",
                new Color(0.7f, 0.4f, 1f, 1f));
        }
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        if (isPossessed.Value)
        {
            // Girl took over control
            if (_thirdPersonController != null)
            {
                _thirdPersonController.enabled = true;
                var camField = typeof(ThirdPersonController).GetField("_mainCamera",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (camField != null && Camera.main != null)
                {
                    camField.SetValue(_thirdPersonController, Camera.main.gameObject);
                }
            }
            if (_combatNet != null) _combatNet.enabled = true;

            var inputs = GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }
        }
        else
        {
            // Victim regained ownership: hide blackout overlay and re-enable local controls
            if (_thirdPersonController != null)
            {
                _thirdPersonController.enabled = true;
            }
            if (_combatNet != null) _combatNet.enabled = true;

            var inputs = GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(false);
            }
        }
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        if (isPossessed.Value)
        {
            // Victim lost control to Girl
            if (_thirdPersonController != null) _thirdPersonController.enabled = false;
            if (_combatNet != null) _combatNet.enabled = false;
            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(true);
            }
        }
        else
        {
            // Restored to victim
            if (_thirdPersonController != null) _thirdPersonController.enabled = true;
            if (_combatNet != null) _combatNet.enabled = true;
            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(false);
            }
        }
    }

    // =========================================================================
    //  IPossessable Implementation
    // =========================================================================

    public void Possess(GirlPossession girl)
    {
        _activeGirlRef = girl;
        if (IsServer)
        {
            originalOwnerClientId.Value = OwnerClientId;
            isPossessed.Value = true;
            possessingClientId.Value = girl.OwnerClientId;

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId != girl.OwnerClientId)
            {
                netObj.ChangeOwnership(girl.OwnerClientId);
            }
        }
    }

    public void Release()
    {
        if (IsServer)
        {
            ulong victimId = originalOwnerClientId.Value;
            isPossessed.Value = false;
            possessingClientId.Value = 0;

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && victimId != 0 && netObj.OwnerClientId != victimId)
            {
                netObj.ChangeOwnership(victimId);
            }

            NotifyPossessionEndedClientRpc(victimId);
        }

        _activeGirlRef = null;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPossessionEndedClientRpc(ulong victimClientId)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == victimClientId)
        {
            if (_priestRejectionCoroutine != null)
            {
                StopCoroutine(_priestRejectionCoroutine);
                _priestRejectionCoroutine = null;
            }

            if (_thirdPersonController != null)
            {
                _thirdPersonController.enabled = true;
            }
            if (_combatNet != null) _combatNet.enabled = true;

            var inputs = GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.enabled = true;
                inputs.cursorLocked = true;
                inputs.cursorInputForLook = true;
            }

            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(false);
            }
        }
    }

    public void OnPossess(ulong clientId)
    {
        if (IsServer)
        {
            originalOwnerClientId.Value = OwnerClientId;
            isPossessed.Value = true;
            possessingClientId.Value = clientId;

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.OwnerClientId != clientId)
            {
                netObj.ChangeOwnership(clientId);
            }
        }
    }

    public void OnRelease()
    {
        Release();
    }

    public Transform GetCameraTarget()
    {
        if (cameraTarget != null) return cameraTarget;
        Transform camRoot = transform.Find("PlayerCameraRoot");
        return camRoot != null ? camRoot : transform;
    }

    /// <summary>
    /// Forcibly ejects the possessing spirit (e.g. via Priest Exorcism).
    /// </summary>
    public void ForceExorcise()
    {
        if (_activeGirlRef != null)
        {
            _activeGirlRef.ForceEjectByExorcism();
        }
        else
        {
            var girl = FindFirstObjectByType<GirlPossession>();
            if (girl != null && girl.IsCurrentlyPossessing)
            {
                girl.ForceEjectByExorcism();
            }
        }
        Release();
    }
}
