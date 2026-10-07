using UnityEngine;
using Unity.Netcode;
using StarterAssets;
using UnityEngine.InputSystem;

/// <summary>
/// SOLID — SRP: Enables any player investigator to be possessed by the Girl.
/// Synchronizes possession status across the network. When possessed, victim screen
/// blacks out with "Let me take the wheel for a sec" and local controls are locked.
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

    [Header("Active Deal Mirroring (Synced to Possessing Girl)")]
    public NetworkVariable<bool> hasActiveDeal = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<Unity.Collections.FixedString64Bytes> activeDealTitle = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<Unity.Collections.FixedString128Bytes> activeDealTerms = new NetworkVariable<Unity.Collections.FixedString128Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> activeDealDuration = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> activeDealTimeRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> activeDealReward = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> activeDealPenalty = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Open Modals State (Synced to Possessing Girl)")]
    public NetworkVariable<bool> isDealPromptOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> dealPromptSenderId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString64Bytes> dealPromptTitle = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString128Bytes> dealPromptTerms = new NetworkVariable<Unity.Collections.FixedString128Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString32Bytes> dealPromptReward = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> dealPromptGrantWeapon = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> dealPromptTimeLimit = new NetworkVariable<int>(
        120, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> dealPromptPenalty = new NetworkVariable<int>(
        15, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isCompletionModalOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString64Bytes> completionDealTitle = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> completionReward = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> completionPenalty = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> completionRewardLeft = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString128Bytes> completionDesc = new NetworkVariable<Unity.Collections.FixedString128Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isFailureModalOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString64Bytes> failureDealTitle = new NetworkVariable<Unity.Collections.FixedString64Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> failurePenalty = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<Unity.Collections.FixedString128Bytes> failureDesc = new NetworkVariable<Unity.Collections.FixedString128Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Priest Possession Struggle Settings")]
    [Tooltip("Maximum duration in seconds for the Priest's button-mash struggle QTE.")]
    public float priestResistWindowDuration = 5.0f;

    [Tooltip("Key required by the Priest to mash and resist possession. Dynamically reflected on the HUD.")]
    public Key resistKey = Key.F;

    [Tooltip("Fallback KeyCode for legacy input system.")]
    public KeyCode fallbackResistKeyCode = KeyCode.F;

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
        hasActiveDeal.OnValueChanged += HandleActiveDealChanged;
        isDealPromptOpen.OnValueChanged += HandleDealPromptChanged;
        isCompletionModalOpen.OnValueChanged += HandleCompletionModalChanged;
        isFailureModalOpen.OnValueChanged += HandleFailureModalChanged;

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
        hasActiveDeal.OnValueChanged -= HandleActiveDealChanged;
        isDealPromptOpen.OnValueChanged -= HandleDealPromptChanged;
        isCompletionModalOpen.OnValueChanged -= HandleCompletionModalChanged;
        isFailureModalOpen.OnValueChanged -= HandleFailureModalChanged;

        if (_healthSystem != null)
        {
            _healthSystem.OnDied -= HandlePossessedTargetDied;
        }
    }

    private Coroutine _priestRejectionCoroutine;
    private Coroutine _priestServerTimerCoroutine;

    public static PlayerPossessableNet GetPossessedByLocalClient()
    {
        if (NetworkManager.Singleton == null) return null;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        var all = Object.FindObjectsByType<PlayerPossessableNet>(FindObjectsSortMode.None);
        foreach (var p in all)
        {
            if (p != null && p.isPossessed.Value && p.possessingClientId.Value == localId)
            {
                return p;
            }
        }
        return null;
    }

    private void HandlePossessionChanged(bool previous, bool current)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        bool isPossessingGirl = current && (possessingClientId.Value == localId);

        if (isPossessingGirl)
        {
            // If the possessed investigator currently has DealNotificationPromptModal open, mirror it!
            if (isDealPromptOpen.Value && DealNotificationUI.Instance != null)
            {
                DealNotificationUI.Instance.DisplayDealOffer(
                    dealPromptSenderId.Value,
                    dealPromptTitle.Value.ToString(),
                    dealPromptTerms.Value.ToString(),
                    dealPromptReward.Value.ToString(),
                    dealPromptGrantWeapon.Value,
                    dealPromptTimeLimit.Value,
                    dealPromptPenalty.Value
                );
            }

            // If the possessed investigator currently has DealCompletionModalUI open, mirror it!
            if (isCompletionModalOpen.Value && NightCrawler.UI.DealCompletionModalUI.Instance != null)
            {
                NightCrawler.UI.DealCompletionModalUI.Instance.Show(
                    completionDealTitle.Value.ToString(),
                    completionReward.Value,
                    completionPenalty.Value,
                    completionRewardLeft.Value,
                    completionDesc.Value.ToString()
                );
            }

            // If the possessed investigator currently has DealFailureModalUI open, mirror it!
            if (isFailureModalOpen.Value && NightCrawler.UI.DealFailureModalUI.Instance != null)
            {
                NightCrawler.UI.DealFailureModalUI.Instance.Show(
                    failureDealTitle.Value.ToString(),
                    failurePenalty.Value,
                    failureDesc.Value.ToString()
                );
            }

            // If the possessed investigator has an active deal, mirror the ActiveDealMissionHUD!
            if (hasActiveDeal.Value && NightCrawler.UI.ActiveDealMissionHUD.Instance != null)
            {
                NightCrawler.UI.ActiveDealMissionHUD.Instance.StartPossessedMirror(
                    activeDealTitle.Value.ToString(),
                    activeDealTerms.Value.ToString(),
                    activeDealDuration.Value,
                    activeDealPenalty.Value,
                    activeDealReward.Value,
                    activeDealTimeRemaining.Value
                );
            }
        }
        else if (!current)
        {
            // Possession ended: dismiss mirrored modals on the Girl's screen
            bool localIsGirl = NetworkManager.Singleton.LocalClient != null && 
                               NetworkManager.Singleton.LocalClient.PlayerObject != null && 
                               (NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<GirlPossession>() != null || 
                                NetworkManager.Singleton.LocalClient.PlayerObject.name.ToLower().Contains("girl"));

            if (possessingClientId.Value == localId || localIsGirl)
            {
                if (DealNotificationUI.Instance != null) DealNotificationUI.Instance.Hide();
                if (NightCrawler.UI.DealCompletionModalUI.Instance != null) NightCrawler.UI.DealCompletionModalUI.Instance.Hide();
                if (NightCrawler.UI.DealFailureModalUI.Instance != null) NightCrawler.UI.DealFailureModalUI.Instance.Hide();
                if (NightCrawler.UI.ActiveDealMissionHUD.Instance != null && NightCrawler.UI.ActiveDealMissionHUD.Instance.IsMirroredPossession)
                {
                    NightCrawler.UI.ActiveDealMissionHUD.Instance.Hide();
                }
            }

            // Safeguard: whenever isPossessed becomes false, any non-girl client MUST hide the blackout overlay
            if (NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (!localIsGirl && PossessionBlackoutOverlay.Instance != null)
                {
                    PossessionBlackoutOverlay.Instance.SetBlackout(false);
                }
            }
        }
    }

    private void HandleDealPromptChanged(bool previous, bool current)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (current && DealNotificationUI.Instance != null)
            {
                DealNotificationUI.Instance.DisplayDealOffer(
                    dealPromptSenderId.Value,
                    dealPromptTitle.Value.ToString(),
                    dealPromptTerms.Value.ToString(),
                    dealPromptReward.Value.ToString(),
                    dealPromptGrantWeapon.Value,
                    dealPromptTimeLimit.Value,
                    dealPromptPenalty.Value
                );
            }
            else if (!current && DealNotificationUI.Instance != null)
            {
                DealNotificationUI.Instance.Hide();
            }
        }
    }

    private void HandleCompletionModalChanged(bool previous, bool current)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (current && NightCrawler.UI.DealCompletionModalUI.Instance != null)
            {
                NightCrawler.UI.DealCompletionModalUI.Instance.Show(
                    completionDealTitle.Value.ToString(),
                    completionReward.Value,
                    completionPenalty.Value,
                    completionRewardLeft.Value,
                    completionDesc.Value.ToString()
                );
            }
            else if (!current && NightCrawler.UI.DealCompletionModalUI.Instance != null)
            {
                NightCrawler.UI.DealCompletionModalUI.Instance.Hide();
            }
        }
    }

    private void HandleFailureModalChanged(bool previous, bool current)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (current && NightCrawler.UI.DealFailureModalUI.Instance != null)
            {
                NightCrawler.UI.DealFailureModalUI.Instance.Show(
                    failureDealTitle.Value.ToString(),
                    failurePenalty.Value,
                    failureDesc.Value.ToString()
                );
            }
            else if (!current && NightCrawler.UI.DealFailureModalUI.Instance != null)
            {
                NightCrawler.UI.DealFailureModalUI.Instance.Hide();
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
        bool rejected = false;

        if (PossessionBlackoutOverlay.Instance != null)
        {
            yield return PossessionBlackoutOverlay.Instance.RunPriestStruggleRoutine(
                windowDuration,
                () => isPossessed.Value,
                (won) => rejected = won,
                resistKey
            );
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < windowDuration && isPossessed.Value)
            {
                elapsed += Time.deltaTime;

                if (Keyboard.current != null)
                {
                    var keyControl = Keyboard.current[resistKey];
                    bool keyMashed = (keyControl != null && keyControl.wasPressedThisFrame) ||
                                     Keyboard.current.spaceKey.wasPressedThisFrame;

                    if (keyMashed)
                    {
                        rejected = true;
                        break;
                    }
                }
                else if (Input.GetKeyDown(fallbackResistKeyCode) || Input.GetKeyDown(KeyCode.Space))
                {
                    rejected = true;
                    break;
                }

                yield return null;
            }
        }

        if (rejected)
        {
            Debug.Log("[PlayerPossessableNet] Priest successfully rejected possession via struggle QTE!");
            RejectPossessionServerRpc();
        }
        else if (isPossessed.Value)
        {
            Debug.Log("[PlayerPossessableNet] Priest did not break free in time. Possession confirmed.");
            NotifyPossessionAcceptedServerRpc();
        }

        _priestRejectionCoroutine = null;
    }

    public NetworkVariable<ulong> originalOwnerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Rpc(SendTo.Server)]
    private void RejectPossessionServerRpc()
    {
        if (_priestServerTimerCoroutine != null)
        {
            StopCoroutine(_priestServerTimerCoroutine);
            _priestServerTimerCoroutine = null;
        }

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
            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(false);
            }

            if (IsTargetDead())
            {
                EnforceDeadState();
                return;
            }

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

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("You purged the spirit & rejected possession!", 3f);
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (localId == girlClientId)
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"{victimName} purged your spirit & rejected possession! Lost {penalty:0}s possession time!", 4f);
            }
        }
    }

    private System.Collections.IEnumerator PriestRejectionServerTimerRoutine(ulong victimId, ulong girlClientId, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && isPossessed.Value && possessingClientId.Value == girlClientId)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (isPossessed.Value && possessingClientId.Value == girlClientId)
        {
            ConfirmPossessionOnServer(victimId, girlClientId);
        }
        _priestServerTimerCoroutine = null;
    }

    private void ConfirmPossessionOnServer(ulong victimId, ulong girlClientId)
    {
        if (!IsServer) return;

        string victimName = GirlRevealManager.GetRegisteredPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = PlayerNameManager.GetPlayerName(victimId);
        if (string.IsNullOrEmpty(victimName)) victimName = gameObject.name.Replace("(Clone)", "").Trim();

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.OwnerClientId != girlClientId)
        {
            netObj.ChangeOwnership(girlClientId);
        }

        NotifyVictimPossessedClientRpc(victimId);
        NotifyPossessionConfirmedClientRpc(victimId, girlClientId, victimName);
    }

    [Rpc(SendTo.Server)]
    private void NotifyPossessionAcceptedServerRpc()
    {
        if (_priestServerTimerCoroutine != null)
        {
            StopCoroutine(_priestServerTimerCoroutine);
            _priestServerTimerCoroutine = null;
        }
        ulong victimId = originalOwnerClientId.Value;
        ulong girlClientId = possessingClientId.Value;
        ConfirmPossessionOnServer(victimId, girlClientId);
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
                NotificationManager.Instance.ShowNotification($"You took possession of {victimName}!", 3f);
            }

            // Immediately switch Girl's HUD to the possessed target!
            if (PlayerHUD.Instance != null)
            {
                PlayerHUD.Instance.BindToPossessedTarget(gameObject);
            }
        }
    }

    private bool IsTargetDead()
    {
        var targetTh = _targetHealth != null ? _targetHealth : GetComponent<TargetHealth>();
        var targetHs = _healthSystem != null ? _healthSystem : GetComponent<HealthSystem>();

        bool isThAlive = targetTh != null && !targetTh.isCorpse.Value && targetTh.CurrentHealth > 0f;
        bool isHsAlive = targetHs != null && !targetHs.IsDead && targetHs.CurrentHealth > 0f;

        if (isThAlive || isHsAlive) return false;
        return true;
    }

    private void EnforceDeadState()
    {
        // The Girl player must NEVER execute victim death logic or see 'YOU DIED'!
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObj != null)
            {
                if (localPlayerObj.GetComponent<GirlPossession>() != null || localPlayerObj.name.ToLower().Contains("girl"))
                {
                    return;
                }

                // If local player is playing their own character and is ALIVE, do not show death screen!
                if (localPlayerObj.gameObject != gameObject)
                {
                    if (localPlayerObj.TryGetComponent<TargetHealth>(out var myTh) && !myTh.isCorpse.Value && myTh.CurrentHealth > 0)
                    {
                        return;
                    }
                    if (localPlayerObj.TryGetComponent<HealthSystem>(out var myHs) && !myHs.IsDead && myHs.CurrentHealth > 0)
                    {
                        return;
                    }
                }
            }
        }

        if (TryGetComponent<NetworkPlayer>(out var np))
        {
            np.TeardownInputActions();
        }

        if (_thirdPersonController != null) _thirdPersonController.enabled = false;
        if (_combatNet != null) _combatNet.enabled = false;

        var inputs = GetComponent<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.move = Vector2.zero;
            inputs.look = Vector2.zero;
            inputs.cursorInputForLook = false;
            inputs.enabled = false;
        }

        if (PossessionBlackoutOverlay.Instance != null)
        {
            PossessionBlackoutOverlay.Instance.SetBlackout(false);
        }

        if (DeathUI.Instance != null)
        {
            DeathUI.Instance.ShowDeathScreen("YOU DIED", "Your soul has fallen. Allies can still loot your body.");
        }

        if (PlayerHUD.Instance != null)
        {
            PlayerHUD.Instance.HandleLocalPlayerDied();
        }
    }

    /// <summary>
    /// Completely strips input listening, movement, and camera control from the puppet on the Girl's machine.
    /// Prevents ghost input handling when possessing targets multiple times.
    /// </summary>
    public void TeardownGirlControl()
    {
        if (TryGetComponent<NetworkPlayer>(out var np))
        {
            np.TeardownInputActions();
        }

        if (_thirdPersonController != null)
        {
            _thirdPersonController.enabled = false;
        }

        if (_combatNet != null)
        {
            _combatNet.enabled = false;
        }

        var inputs = GetComponent<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.move = Vector2.zero;
            inputs.look = Vector2.zero;
            inputs.enabled = false;
        }

        if (TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi))
        {
            pi.enabled = false;
        }
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        if (isPossessed.Value)
        {
            // Girl took over control: dynamically wire up StarterAssetsInputs to local input
            bool inputBound = false;
            if (TryGetComponent<NetworkPlayer>(out var np))
            {
                np.SetupOwnerInput();
                inputBound = true;
            }

            if (TryGetComponent<UnityEngine.InputSystem.PlayerInput>(out var pi))
            {
                pi.enabled = true;
                if (!inputBound && TryGetComponent<StarterAssets.StarterAssetsInputs>(out var sai))
                {
                    pi.notificationBehavior = UnityEngine.InputSystem.PlayerNotifications.InvokeCSharpEvents;
                    pi.defaultActionMap = "Player";
                    var playerMap = pi.actions?.FindActionMap("Player");
                    if (playerMap != null && !playerMap.enabled) playerMap.Enable();

                    var moveAct = pi.actions?.FindAction("Move");
                    if (moveAct != null)
                    {
                        moveAct.performed += ctx => sai.MoveInput(ctx.ReadValue<Vector2>());
                        moveAct.canceled += ctx => sai.MoveInput(Vector2.zero);
                        moveAct.Enable();
                    }
                    var lookAct = pi.actions?.FindAction("Look");
                    if (lookAct != null)
                    {
                        lookAct.performed += ctx => sai.LookInput(ctx.ReadValue<Vector2>());
                        lookAct.canceled += ctx => sai.LookInput(Vector2.zero);
                        lookAct.Enable();
                    }
                    var jumpAct = pi.actions?.FindAction("Jump");
                    if (jumpAct != null)
                    {
                        jumpAct.performed += ctx => sai.JumpInput(true);
                        jumpAct.canceled += ctx => sai.JumpInput(false);
                        jumpAct.Enable();
                    }
                    var sprintAct = pi.actions?.FindAction("Sprint");
                    if (sprintAct != null)
                    {
                        sprintAct.performed += ctx => sai.SprintInput(ctx.ReadValueAsButton());
                        sprintAct.canceled += ctx => sai.SprintInput(false);
                        sprintAct.Enable();
                    }
                    pi.ActivateInput();
                }
            }

            if (TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = true;
                cc.detectCollisions = true;
            }

            if (_thirdPersonController != null)
            {
                _thirdPersonController.enabled = true;
                Transform camTarget = GetCameraTarget();
                if (camTarget != null)
                {
                    _thirdPersonController.ResetTargetRotation(camTarget.eulerAngles.y, camTarget.eulerAngles.x);
                }
                else if (Camera.main != null)
                {
                    _thirdPersonController.ResetTargetRotation(Camera.main.transform.eulerAngles.y, Camera.main.transform.eulerAngles.x);
                }

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
            // The Girl should never execute victim ownership return logic
            bool localIsGirl = NetworkManager.Singleton != null && 
                               NetworkManager.Singleton.LocalClient != null && 
                               NetworkManager.Singleton.LocalClient.PlayerObject != null && 
                               (NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<GirlPossession>() != null || 
                                NetworkManager.Singleton.LocalClient.PlayerObject.name.ToLower().Contains("girl"));
            if (localIsGirl) return;

            // Victim regained ownership
            if (IsTargetDead())
            {
                EnforceDeadState();
                return;
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

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();

        bool isGirl = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == possessingClientId.Value;
        bool isVictim = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == originalOwnerClientId.Value;

        if (isGirl)
        {
            TeardownGirlControl();
        }

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
                if (IsTargetDead())
                {
                    EnforceDeadState();
                    return;
                }

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
                float duration = priestResistWindowDuration > 0f ? priestResistWindowDuration : 5.0f;
                if (_priestServerTimerCoroutine != null) StopCoroutine(_priestServerTimerCoroutine);
                _priestServerTimerCoroutine = StartCoroutine(PriestRejectionServerTimerRoutine(victimId, girl.OwnerClientId, duration));
                // Priest resistance window: ownership stays with Priest during resistance
                NotifyPriestRejectionWindowClientRpc(victimId, girl.OwnerClientId, duration, penalty);
            }
            else
            {
                ConfirmPossessionOnServer(victimId, girl.OwnerClientId);
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
                    $"{victimName} is attempting Exorcism! ({duration:0}s) Rejection penalty: -{penalty:0}s! Press E to cancel.", duration);
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
        if (_priestServerTimerCoroutine != null)
        {
            StopCoroutine(_priestServerTimerCoroutine);
            _priestServerTimerCoroutine = null;
        }

        TeardownGirlControl();
        if (IsServer)
        {
            ulong victimId = originalOwnerClientId.Value;
            ulong girlId = (_activeGirlRef != null) ? _activeGirlRef.OwnerClientId : possessingClientId.Value;

            isPossessed.Value = false;
            possessingClientId.Value = ulong.MaxValue;

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && victimId != ulong.MaxValue && victimId != girlId && netObj.OwnerClientId != victimId)
            {
                netObj.ChangeOwnership(victimId);
            }

            ulong rpcVictimId = (victimId != girlId && victimId != ulong.MaxValue) ? victimId : ulong.MaxValue;
            NotifyPossessionEndedClientRpc(rpcVictimId);
        }

        _activeGirlRef = null;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPossessionEndedClientRpc(ulong victimClientId)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        bool localIsGirl = NetworkManager.Singleton.LocalClient != null && 
                           NetworkManager.Singleton.LocalClient.PlayerObject != null && 
                           (NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<GirlPossession>() != null || 
                            NetworkManager.Singleton.LocalClient.PlayerObject.name.ToLower().Contains("girl"));

        // Clean up puppet controls on the Girl's client
        if (localId == possessingClientId.Value || localIsGirl)
        {
            TeardownGirlControl();
        }

        // The Girl is NEVER the victim — exit immediately
        if (localIsGirl) return;

        bool isVictim = (victimClientId != ulong.MaxValue && localId == victimClientId) || 
                        (isPossessed.Value && originalOwnerClientId.Value != ulong.MaxValue && localId == originalOwnerClientId.Value);

        // Living player safeguard: if local player has their own active living player object, they are not the victim
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var myObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (myObj != null && myObj.gameObject != gameObject)
            {
                if (myObj.TryGetComponent<TargetHealth>(out var myTh) && !myTh.isCorpse.Value && myTh.CurrentHealth > 0)
                {
                    isVictim = false;
                }
            }
        }
        if (isVictim)
        {
            if (_priestRejectionCoroutine != null)
            {
                StopCoroutine(_priestRejectionCoroutine);
                _priestRejectionCoroutine = null;
            }

            if (PossessionBlackoutOverlay.Instance != null)
            {
                PossessionBlackoutOverlay.Instance.SetBlackout(false);
            }

            if (IsTargetDead())
            {
                EnforceDeadState();
                return;
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
        TeardownGirlControl();
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

    private void HandleActiveDealChanged(bool previous, bool current)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        bool isPossessingGirl = isPossessed.Value && possessingClientId.Value == localId;

        if (isPossessingGirl)
        {
            if (current)
            {
                if (NightCrawler.UI.ActiveDealMissionHUD.Instance != null)
                {
                    NightCrawler.UI.ActiveDealMissionHUD.Instance.StartPossessedMirror(
                        activeDealTitle.Value.ToString(),
                        activeDealTerms.Value.ToString(),
                        activeDealDuration.Value,
                        activeDealPenalty.Value,
                        activeDealReward.Value,
                        activeDealTimeRemaining.Value
                    );
                }
            }
            else
            {
                if (NightCrawler.UI.ActiveDealMissionHUD.Instance != null && NightCrawler.UI.ActiveDealMissionHUD.Instance.IsMirroredPossession)
                {
                    NightCrawler.UI.ActiveDealMissionHUD.Instance.Hide();
                }
            }
        }
    }

    private void Update()
    {
        if (IsServer && hasActiveDeal.Value)
        {
            if (activeDealTimeRemaining.Value > 0f)
            {
                activeDealTimeRemaining.Value = Mathf.Max(0f, activeDealTimeRemaining.Value - Time.deltaTime);
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void SetActiveDealServerRpc(string title, string terms, float duration, int reward, int penalty)
    {
        hasActiveDeal.Value = true;
        activeDealTitle.Value = !string.IsNullOrEmpty(title) ? title : "DARK DEAL";
        activeDealTerms.Value = !string.IsNullOrEmpty(terms) ? terms : "";
        activeDealDuration.Value = duration;
        activeDealTimeRemaining.Value = duration;
        activeDealReward.Value = reward;
        activeDealPenalty.Value = penalty;
    }

    [Rpc(SendTo.Server)]
    public void ClearActiveDealServerRpc()
    {
        hasActiveDeal.Value = false;
        activeDealTitle.Value = default;
        activeDealTerms.Value = default;
        activeDealDuration.Value = 0f;
        activeDealTimeRemaining.Value = 0f;
        activeDealReward.Value = 0;
        activeDealPenalty.Value = 0;
    }

    [Rpc(SendTo.Server)]
    public void RequestMirrorDealCompletionServerRpc(string title, int reward, int penalty, int currentStake, string desc)
    {
        MirrorDealCompletionToPossessorClientRpc(title, reward, penalty, currentStake, desc);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void MirrorDealCompletionToPossessorClientRpc(string title, int reward, int penalty, int currentStake, string desc)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (NightCrawler.UI.DealCompletionModalUI.Instance != null)
            {
                NightCrawler.UI.DealCompletionModalUI.Instance.Show(title, reward, penalty, currentStake, desc);
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void RequestMirrorDealFailureServerRpc(string title, int penalty, string desc)
    {
        MirrorDealFailureToPossessorClientRpc(title, penalty, desc);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void MirrorDealFailureToPossessorClientRpc(string title, int penalty, string desc)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (NightCrawler.UI.DealFailureModalUI.Instance != null)
            {
                NightCrawler.UI.DealFailureModalUI.Instance.Show(title, penalty, desc);
            }
        }
    }

    [Rpc(SendTo.Server)]
    public void RequestMirrorDealOfferServerRpc(ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimit, int penalty)
    {
        MirrorDealOfferToPossessorClientRpc(senderId, title, terms, reward, grantWeapon, timeLimit, penalty);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void MirrorDealOfferToPossessorClientRpc(ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimit, int penalty)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (DealNotificationUI.Instance != null)
            {
                DealNotificationUI.Instance.DisplayDealOffer(senderId, title, terms, reward, grantWeapon, timeLimit, penalty);
            }
        }
    }

    public static PlayerPossessableNet GetLocalOrPossessed()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (localObj != null && localObj.TryGetComponent<PlayerPossessableNet>(out var pNet))
            {
                return pNet;
            }
        }
        return GetPossessedByLocalClient();
    }

    [Rpc(SendTo.Server)]
    public void DismissModalBidirectionalServerRpc(int modalType)
    {
        if (modalType == 0) isCompletionModalOpen.Value = false;
        else if (modalType == 1) isFailureModalOpen.Value = false;
        else if (modalType == 2) isDealPromptOpen.Value = false;

        DismissModalBidirectionalClientRpc(modalType);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void DismissModalBidirectionalClientRpc(int modalType)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        bool isVictim = (NetworkObject != null && NetworkObject.OwnerClientId == localId) || (originalOwnerClientId.Value == localId);
        bool isPossessor = isPossessed.Value && (possessingClientId.Value == localId);

        if (isVictim || isPossessor)
        {
            if (modalType == 0 && NightCrawler.UI.DealCompletionModalUI.Instance != null)
                NightCrawler.UI.DealCompletionModalUI.Instance.HideLocalOnly();
            else if (modalType == 1 && NightCrawler.UI.DealFailureModalUI.Instance != null)
                NightCrawler.UI.DealFailureModalUI.Instance.HideLocalOnly();
            else if (modalType == 2 && DealNotificationUI.Instance != null)
                DealNotificationUI.Instance.HideLocalOnly();
        }
    }

    [Rpc(SendTo.Server)]
    public void RequestMirrorModalDismissedServerRpc(int modalType)
    {
        DismissModalBidirectionalServerRpc(modalType);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void MirrorModalDismissedToPossessorClientRpc(int modalType)
    {
        if (NetworkManager.Singleton == null) return;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        if (isPossessed.Value && possessingClientId.Value == localId)
        {
            if (modalType == 0 && NightCrawler.UI.DealCompletionModalUI.Instance != null)
                NightCrawler.UI.DealCompletionModalUI.Instance.HideLocalOnly();
            else if (modalType == 1 && NightCrawler.UI.DealFailureModalUI.Instance != null)
                NightCrawler.UI.DealFailureModalUI.Instance.HideLocalOnly();
            else if (modalType == 2 && DealNotificationUI.Instance != null)
                DealNotificationUI.Instance.HideLocalOnly();
        }
    }

    [Rpc(SendTo.Server)]
    public void SetDealPromptStateServerRpc(bool isOpen, ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimit, int penalty)
    {
        isDealPromptOpen.Value = isOpen;
        dealPromptSenderId.Value = senderId;
        dealPromptTitle.Value = !string.IsNullOrEmpty(title) ? title : "";
        dealPromptTerms.Value = !string.IsNullOrEmpty(terms) ? terms : "";
        dealPromptReward.Value = !string.IsNullOrEmpty(reward) ? reward : "";
        dealPromptGrantWeapon.Value = grantWeapon;
        dealPromptTimeLimit.Value = timeLimit;
        dealPromptPenalty.Value = penalty;
    }

    [Rpc(SendTo.Server)]
    public void SetCompletionModalStateServerRpc(bool isOpen, string title, int reward, int penalty, int rewardLeft, string desc)
    {
        isCompletionModalOpen.Value = isOpen;
        completionDealTitle.Value = !string.IsNullOrEmpty(title) ? title : "";
        completionReward.Value = reward;
        completionPenalty.Value = penalty;
        completionRewardLeft.Value = rewardLeft;
        completionDesc.Value = !string.IsNullOrEmpty(desc) ? desc : "";
    }

    [Rpc(SendTo.Server)]
    public void SetFailureModalStateServerRpc(bool isOpen, string title, int penalty, string desc)
    {
        isFailureModalOpen.Value = isOpen;
        failureDealTitle.Value = !string.IsNullOrEmpty(title) ? title : "";
        failurePenalty.Value = penalty;
        failureDesc.Value = !string.IsNullOrEmpty(desc) ? desc : "";
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
