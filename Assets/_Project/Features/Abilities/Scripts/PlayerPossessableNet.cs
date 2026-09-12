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

    private HealthSystem _healthSystem;
    private TargetHealth _targetHealth;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _thirdPersonController = GetComponent<ThirdPersonController>();
        _combatNet = GetComponent<InvestigatorCombatNet>();
        _healthSystem = GetComponent<HealthSystem>();
        _targetHealth = GetComponent<TargetHealth>();
    }

    public override void OnNetworkSpawn()
    {
        isPossessed.OnValueChanged += HandlePossessionChanged;
        if (IsServer && originalOwnerClientId.Value == ulong.MaxValue)
        {
            originalOwnerClientId.Value = OwnerClientId;
        }

        if (_healthSystem != null)
        {
            _healthSystem.OnDied += HandlePossessedTargetDied;
        }
    }

    public override void OnNetworkDespawn()
    {
        isPossessed.OnValueChanged -= HandlePossessionChanged;
        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandlePossessedTargetDied;
        }
    }

    private Coroutine _priestRejectionCoroutine;

    private void HandlePossessionChanged(bool previous, bool current)
    {
        if (!current)
        {
            // Safeguard: whenever isPossessed becomes false, any non-girl client MUST hide the blackout overlay
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                bool isGirl = localObj != null && (localObj.GetComponent<GirlPossession>() != null || localObj.name.ToLower().Contains("girl"));
                if (!isGirl && PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(false);
                }
            }
        }
    }

    public void HandlePossessedTargetDied()
    {
        if (isPossessed.Value)
        {
            if (IsServer)
            {
                StartCoroutine(PossessedTargetDeathRoutine());
            }
        }
    }

    private System.Collections.IEnumerator PossessedTargetDeathRoutine()
    {
        // Allow death animation and ragdoll to play out (~1.8s)
        yield return new WaitForSeconds(1.8f);

        if (isPossessed.Value)
        {
            Debug.Log("[PlayerPossessableNet] Possessed target died -> Returning Girl to spirit form.");
            if (_activeGirlRef != null)
            {
                _activeGirlRef.ForceEjectOnTargetDeath();
            }
            else
            {
                ulong girlId = possessingClientId.Value;
                if (girlId != ulong.MaxValue)
                {
                    foreach (var gp in FindObjectsByType<GirlPossession>(FindObjectsSortMode.None))
                    {
                        if (gp.OwnerClientId == girlId)
                        {
                            gp.ForceEjectOnTargetDeath();
                            break;
                        }
                    }
                }
            }

            Release();
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
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Rpc(SendTo.Server)]
    private void RejectPossessionServerRpc()
    {
        ulong victimId = originalOwnerClientId.Value;
        ulong girlClientId = possessingClientId.Value;

        string victimName = GirlRevealManager.GetRegisteredPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = gameObject.name.Replace("(Clone)", "").Trim();

        float penalty = _activeGirlRef != null ? _activeGirlRef.exorcismPenaltySeconds : 30f;

        isPossessed.Value = false;
        possessingClientId.Value = 0;

        if (_activeGirlRef != null)
        {
            _activeGirlRef.ForceEjectByExorcism();
        }

        NotifyPriestRejectionEndedClientRpc(victimId, girlClientId, victimName, penalty);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPriestRejectionEndedClientRpc(ulong victimClientId, ulong girlClientId, string victimName, float penalty)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (localId == victimClientId)
        {
            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
            }

            if (_thirdPersonController != null) _thirdPersonController.enabled = true;
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

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("<color=#FFD700>|</color> [EXORCISED] You purged the spirit & rejected possession!", 3f);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (localId == girlClientId)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"<color=#FFD700>|</color> [EXORCISED] {victimName} purged your spirit & rejected possession! Lost {penalty:0}s possession time!", 4f);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void NotifyPossessionAcceptedServerRpc()
    {
        ulong victimId = originalOwnerClientId.Value;
        ulong girlClientId = possessingClientId.Value;

        string victimName = GirlRevealManager.GetRegisteredPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = gameObject.name.Replace("(Clone)", "").Trim();

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.OwnerClientId != girlClientId)
        {
            netObj.ChangeOwnership(girlClientId);
        }

        NotifyPossessionConfirmedClientRpc(victimId, girlClientId, victimName);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPossessionConfirmedClientRpc(ulong victimClientId, ulong girlClientId, string victimName)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        // ONLY the Girl sees that she took possession of the victim!
        if (localId == girlClientId)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"<color=#B388FF>|</color> [POSSESSED] You took possession of {victimName}!", 3f);
            }

            // Immediately switch Girl's HUD to the possessed target!
            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.BindToPossessedTarget(gameObject);
            }
        }
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        if (isPossessed.Value)
        {
            // Girl took over control: dynamically wire up StarterAssetsInputs to local input
            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
            }

            if (TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi))
            {
                pi.enabled = true;
            }

            if (TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = true;
                cc.detectCollisions = true;
            }

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

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Switch Player HUD to show the possessed investigator's health, ammo, weapons, etc.
            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.BindToPossessedTarget(gameObject);
            }
        }
        else
        {
            // Victim regained ownership: hide blackout overlay and re-enable local controls
            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
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

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.RestoreGirlHUD();
            }
        }
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();

        bool isVictim = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == originalOwnerClientId.Value;

        if (isPossessed.Value)
        {
            // Victim lost control to Girl
            if (isVictim)
            {
                if (_thirdPersonController != null) _thirdPersonController.enabled = false;
                if (_combatNet != null) _combatNet.enabled = false;
                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(true);
                }
            }
        }
        else
        {
            // Restored to victim
            if (isVictim)
            {
                if (TryGetComponent<NetworkPlayer>(out var np))
                {
                    np.SetupOwnerInput();
                }

                if (_thirdPersonController != null) _thirdPersonController.enabled = true;
                if (_combatNet != null) _combatNet.enabled = true;
                if (PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(false);
                }

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
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
            if (originalOwnerClientId.Value == ulong.MaxValue || originalOwnerClientId.Value == girl.OwnerClientId)
            {
                if (OwnerClientId != girl.OwnerClientId)
                {
                    originalOwnerClientId.Value = OwnerClientId;
                }
            }

            ulong victimId = originalOwnerClientId.Value;
            if (victimId == ulong.MaxValue || victimId == girl.OwnerClientId)
            {
                victimId = (OwnerClientId != girl.OwnerClientId) ? OwnerClientId : 0;
            }

            isPossessed.Value = true;
            possessingClientId.Value = girl.OwnerClientId;

            bool isPriest = (TryGetComponent<PriestExorcismNet>(out var priest) && priest.isUnlocked) 
                         || gameObject.name.ToLower().Contains("priest");

            if (isPriest)
            {
                float penalty = girl != null ? girl.exorcismPenaltySeconds : 30f;
                // Priest resistance window: ownership stays with Priest during resistance
                NotifyPriestRejectionWindowClientRpc(victimId, girl.OwnerClientId, 4.0f, penalty);
            }
            else
            {
                var netObj = GetComponent<NetworkObject>();
                if (netObj != null && netObj.OwnerClientId != girl.OwnerClientId)
                {
                    netObj.ChangeOwnership(girl.OwnerClientId);
                }

                NotifyVictimPossessedClientRpc(victimId);
                NotifyPossessionAcceptedServerRpc();
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPriestRejectionWindowClientRpc(ulong victimClientId, ulong girlClientId, float duration, float penalty)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (localId == victimClientId)
        {
            // Lock Priest inputs
            if (_thirdPersonController != null) _thirdPersonController.enabled = false;
            if (_combatNet != null) _combatNet.enabled = false;

            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(true);
            }

            if (_priestRejectionCoroutine != null) StopCoroutine(_priestRejectionCoroutine);
            _priestRejectionCoroutine = StartCoroutine(PriestRejectionWindowRoutine(duration));
        }
        else if (localId == girlClientId)
        {
            string victimName = GirlRevealManager.GetRegisteredPlayerName(victimClientId);
            if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(victimClientId);
            if (string.IsNullOrEmpty(victimName)) victimName = "Cursed Priest";

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification(
                    $"<color=#FFD700>|</color> [RESISTING] {victimName} is attempting Exorcism! ({duration:0}s) Rejection penalty: -{penalty:0}s! Press [E] to cancel.", duration);
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyVictimPossessedClientRpc(ulong victimClientId)
    {
        if (NetworkManager.Singleton == null) return;
        // The Girl must NEVER receive victim blackout!
        if (NetworkManager.Singleton.LocalClientId != victimClientId) return;

        // Lock victim inputs
        if (_thirdPersonController != null) _thirdPersonController.enabled = false;
        if (_combatNet != null) _combatNet.enabled = false;

        if (PossessionBlackoutOverlay.Instance != null)
        {
            PossessionBlackoutOverlay.Instance.SetBlackout(true);
        }
    }

    public void Release()
    {
        if (IsServer)
        {
            ulong victimId = originalOwnerClientId.Value;
            if (victimId == ulong.MaxValue || victimId == possessingClientId.Value)
            {
                victimId = (OwnerClientId != possessingClientId.Value) ? OwnerClientId : 0;
            }

            isPossessed.Value = false;
            possessingClientId.Value = ulong.MaxValue;

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && victimId != ulong.MaxValue && netObj.OwnerClientId != victimId)
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
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        bool isVictim = (localId == victimClientId) || (localId == originalOwnerClientId.Value);
        if (isVictim)
        {
            if (_priestRejectionCoroutine != null)
            {
                StopCoroutine(_priestRejectionCoroutine);
                _priestRejectionCoroutine = null;
            }

            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
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

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
        Transform result = null;

        if (cameraTarget != null)
        {
            result = cameraTarget;
        }
        else if (TryGetComponent<StarterAssets.ThirdPersonController>(out var tpc) && tpc.CinemachineCameraTarget != null)
        {
            result = tpc.CinemachineCameraTarget.transform;
        }
        else
        {
            foreach (var c in GetComponentsInChildren<Transform>(true))
            {
                if (c.name == "PlayerCameraRoot" || c.name == "CinemachineCameraTarget")
                {
                    result = c;
                    break;
                }
            }
        }

        if (result == null && TryGetComponent<Animator>(out var anim) && anim.isHuman)
        {
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) result = head;
            else
            {
                Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null) result = chest;
            }
        }

        if (result == null) result = transform;

        // Auto-fix if at character feet: elevate to standard eye/shoulder height (1.375m)
        if (result != transform && result.localPosition.y < 0.5f)
        {
            result.localPosition = new Vector3(result.localPosition.x, 1.375f, result.localPosition.z);
        }

        return result;
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
