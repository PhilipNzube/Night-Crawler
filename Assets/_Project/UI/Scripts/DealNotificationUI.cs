using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using NightCrawler.Economy;
using NightCrawler.UI;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Displays an incoming dark deal proposal to an Investigator using Heat UI Modal.
/// Shows exact deal name, terms/conditions, reward, time limit, and penalty.
/// Provides Accept [Y] / Decline [N] button actions and hotkeys.
/// </summary>
public class DealNotificationUI : MonoBehaviour
{
    private static DealNotificationUI _instance;
    public static DealNotificationUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Heat UI Modal Window")]
    [Tooltip("The ModalWindowManager on DealNotificationPromptModal.")]
    public ModalWindowManager heatModalWindow;

    [Tooltip("CanvasGroup controlling prompt visibility and input interception.")]
    public CanvasGroup canvasGroup;

    [Header("Text Display Elements")]
    [Tooltip("Title text in modal header.")]
    public TextMeshProUGUI headerTitleText;
    [Tooltip("DealNameText inside main content.")]
    public TextMeshProUGUI dealNameText;
    [Tooltip("RewardText inside main content.")]
    public TextMeshProUGUI rewardText;
    [Tooltip("Description/Terms text inside main content.")]
    public TextMeshProUGUI termsDescriptionText;

    [Header("Action Buttons")]
    [Tooltip("ButtonManager for accepting the deal.")]
    public ButtonManager acceptButton;
    [Tooltip("ButtonManager for declining the deal.")]
    public ButtonManager declineButton;

    [Header("Auto-Timeout")]
    public float timeoutSeconds = 15f;
    private float _timer;
    private ulong _currentGirlSenderId;
    private bool _grantWeapon;
    private bool _isActive = false;
    public bool IsOpen => _isActive;
    private bool _isOutcomeMode = false;

    private int _currentTimeLimitSeconds = 120;
    private int _currentPenaltyCredits = 15;
    private int _currentRewardCredits = 30;
    private string _currentTitle = "DARK DEAL";
    private string _currentTerms = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookSceneLoaded()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
        {
            EnsureActiveAndHidden();
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureActiveAndHidden()
    {
        var found = FindFirstObjectByType<DealNotificationUI>(FindObjectsInactive.Include);
        if (found != null)
        {
            _instance = found;
            if (found.heatModalWindow != null)
            {
                SanitizeModal(found.heatModalWindow);
                found.heatModalWindow.StopAllCoroutines();
                found.heatModalWindow.isOn = false;
            }
            found.SetVisible(false, modifyCursor: false);
            Debug.Log($"[DealNotificationUI] Discovered and initialized prompt: {found.gameObject.name}");
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        if (heatModalWindow == null)
        {
            heatModalWindow = GetComponent<ModalWindowManager>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);
        }

        SanitizeModal(heatModalWindow);

        if (acceptButton != null)
        {
            acceptButton.onClick.RemoveListener(OnAcceptClicked);
            acceptButton.onClick.AddListener(OnAcceptClicked);
        }

        if (declineButton != null)
        {
            declineButton.onClick.RemoveListener(OnDeclineClicked);
            declineButton.onClick.AddListener(OnDeclineClicked);
        }

        // Also bind heatModalWindow's own confirm and cancel buttons if present
        if (heatModalWindow != null)
        {
            if (heatModalWindow.confirmButton != null)
            {
                heatModalWindow.confirmButton.onClick.RemoveListener(OnAcceptClicked);
                heatModalWindow.confirmButton.onClick.AddListener(OnAcceptClicked);
            }
            if (heatModalWindow.cancelButton != null)
            {
                heatModalWindow.cancelButton.onClick.RemoveListener(OnDeclineClicked);
                heatModalWindow.cancelButton.onClick.AddListener(OnDeclineClicked);
            }
        }

        // Also support standard UnityEngine.UI.Button if present in children
        var standardButtons = GetComponentsInChildren<UnityEngine.UI.Button>(true);
        foreach (var btn in standardButtons)
        {
            string n = btn.gameObject.name.ToLower();
            if (n.Contains("accept") || n.Contains("confirm") || n.Contains("yes"))
            {
                btn.onClick.RemoveListener(OnAcceptClicked);
                btn.onClick.AddListener(OnAcceptClicked);
            }
            else if (n.Contains("decline") || n.Contains("cancel") || n.Contains("no"))
            {
                btn.onClick.RemoveListener(OnDeclineClicked);
                btn.onClick.AddListener(OnDeclineClicked);
            }
        }

        SetVisible(false, modifyCursor: false);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public bool IsActive => _isActive;

    private Coroutine _ensureVisibleRoutine;

    private void SetVisible(bool visible, bool modifyCursor = true)
    {
        _isActive = visible;

        if (_ensureVisibleRoutine != null)
        {
            StopCoroutine(_ensureVisibleRoutine);
            _ensureVisibleRoutine = null;
        }

        if (visible)
        {
            // Activate any inactive parents up the chain up to Canvas/Root
            Transform cur = transform.parent;
            while (cur != null)
            {
                if (!cur.gameObject.activeSelf)
                {
                    cur.gameObject.SetActive(true);
                }
                cur = cur.parent;
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (heatModalWindow != null)
            {
                heatModalWindow.StopAllCoroutines();
                heatModalWindow.isOn = true;
                heatModalWindow.gameObject.SetActive(true);
                heatModalWindow.transform.localScale = Vector3.one;
            }

            // Disable Animator on root modal so its clips cannot zero CanvasGroup.alpha
            var anim = GetComponent<Animator>();
            if (anim != null) anim.enabled = false;

            ForceModalAlphaOpaque();

            // Ensure EventSystem.current is valid before input touches it
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (es != null) UnityEngine.EventSystems.EventSystem.current = es;
            }

            transform.localScale = Vector3.one;

            // Ensure parent Canvas is enabled
            var parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && !parentCanvas.enabled) parentCanvas.enabled = true;

            // Continuously force alpha = 1 across upcoming frames
            _ensureVisibleRoutine = StartCoroutine(EnsureModalVisibleRoutine());

            if (modifyCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SetPlayerLookInputs(false);
            }
        }
        else
        {
            if (heatModalWindow != null)
            {
                heatModalWindow.StopAllCoroutines();
                heatModalWindow.isOn = false;
            }

            var childCanvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            foreach (var cg in childCanvasGroups)
            {
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                }
            }

            gameObject.SetActive(false);

            // If possessed, mirror dismissal to possessing Girl
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.SpawnManager != null)
            {
                var localObj = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localObj != null && localObj.TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value)
                {
                    pNet.RequestMirrorModalDismissedServerRpc(2);
                }
            }

            if (modifyCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                SetPlayerLookInputs(true);
            }
        }
    }

    private void ForceModalAlphaOpaque()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = GetComponentInChildren<CanvasGroup>(true);
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        var childCanvasGroups = GetComponentsInChildren<CanvasGroup>(true);
        foreach (var cg in childCanvasGroups)
        {
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        Transform contentT = transform.Find("Content");
        if (contentT != null)
        {
            contentT.localScale = Vector3.one;
        }
    }

    private System.Collections.IEnumerator EnsureModalVisibleRoutine()
    {
        for (int i = 0; i < 20; i++)
        {
            if (!_isActive) yield break;
            ForceModalAlphaOpaque();
            yield return null;
        }
        _ensureVisibleRoutine = null;
    }

    private void SetPlayerLookInputs(bool allowLookAndLock)
    {
        Unity.Netcode.NetworkObject localObj = null;
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            if (Unity.Netcode.NetworkManager.Singleton.SpawnManager != null)
            {
                localObj = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            }
            if (localObj == null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
            {
                localObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
            }
        }

        if (localObj != null)
        {
            var inputs = localObj.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorLocked = allowLookAndLock;
                inputs.cursorInputForLook = allowLookAndLock;
            }
        }
    }

    public void Show(ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimitSeconds = 120, int penaltyCredits = 15)
    {
        DisplayDealOffer(senderId, title, terms, reward, grantWeapon, timeLimitSeconds, penaltyCredits);
    }

    public void DisplayDealOffer(ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimitSeconds = 120, int penaltyCredits = 15)
    {
        // If local player is dead, reject/ignore immediately
        Unity.Netcode.NetworkObject localObj = null;
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            if (Unity.Netcode.NetworkManager.Singleton.SpawnManager != null)
            {
                localObj = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            }
            if (localObj == null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
            {
                localObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
            }
        }

        if (localObj != null)
        {
            if ((localObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) ||
                (localObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead))
            {
                Debug.Log("[DealNotificationUI] Local player is dead; suppressing deal offer display.");
                SetVisible(false, modifyCursor: true);
                return;
            }
        }

        _currentGirlSenderId = senderId;
        _grantWeapon = grantWeapon;
        _currentTimeLimitSeconds = timeLimitSeconds;
        _currentPenaltyCredits = penaltyCredits;
        _currentTitle = title;
        _currentTerms = terms;
        _timer = timeoutSeconds;
        _isOutcomeMode = false;

        // Restore normal accept/decline buttons for proposal mode
        if (declineButton != null) declineButton.gameObject.SetActive(true);
        if (heatModalWindow != null && heatModalWindow.cancelButton != null) heatModalWindow.cancelButton.gameObject.SetActive(true);
        if (acceptButton != null) acceptButton.buttonText = "ACCEPT";
        if (heatModalWindow != null && heatModalWindow.confirmButton != null) heatModalWindow.confirmButton.buttonText = "ACCEPT";

        int parsedReward = 30;
        if (!string.IsNullOrEmpty(reward))
        {
            var parts = reward.Split(' ');
            if (parts.Length > 0 && int.TryParse(parts[0], out int val))
            {
                parsedReward = val;
            }
        }
        _currentRewardCredits = parsedReward;

        string cleanTitle = !string.IsNullOrWhiteSpace(title) && !title.Equals("select", System.StringComparison.OrdinalIgnoreCase)
            ? title.ToUpper()
            : "DARK DEAL";

        // Filter out any penalty text so the receiver never sees the penalty
        string cleanTerms = terms ?? "";
        int penaltyIdx = cleanTerms.IndexOf("Penalty", System.StringComparison.OrdinalIgnoreCase);
        if (penaltyIdx >= 0)
        {
            cleanTerms = cleanTerms.Substring(0, penaltyIdx).TrimEnd();
        }

        // Only show the deal name and the reward offered
        if (dealNameText != null) dealNameText.text = cleanTitle;
        if (rewardText != null)
        {
            rewardText.text = _currentRewardCredits > 0 ? $"{_currentRewardCredits}" : reward;
        }

        if (termsDescriptionText != null)
        {
            termsDescriptionText.text = cleanTerms;
        }

        if (headerTitleText != null && (heatModalWindow == null || headerTitleText != heatModalWindow.windowDescription))
        {
            headerTitleText.text = cleanTitle;
        }

        if (heatModalWindow != null)
        {
            heatModalWindow.titleText = cleanTitle;
            heatModalWindow.descriptionText = cleanTerms;
            heatModalWindow.useLocalization = false;
            heatModalWindow.titleKey = string.Empty;
            heatModalWindow.descriptionKey = string.Empty;
            if (heatModalWindow.windowTitle != null) heatModalWindow.windowTitle.text = cleanTitle;
            if (heatModalWindow.windowDescription != null) heatModalWindow.windowDescription.text = cleanTerms;
            try { heatModalWindow.UpdateUI(); } catch { }
        }

        gameObject.SetActive(true);
        SetVisible(true, modifyCursor: true);

        // If possessed, mirror deal offer to possessing Girl
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.SpawnManager != null)
        {
            var myNetObj = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (myNetObj != null && myNetObj.TryGetComponent<PlayerPossessableNet>(out var pNet) && pNet.isPossessed.Value)
            {
                pNet.RequestMirrorDealOfferServerRpc(senderId, cleanTitle, cleanTerms, reward, grantWeapon, timeLimitSeconds, penaltyCredits);
            }
        }

        Debug.Log($"[DealNotificationUI] Displaying deal '{cleanTitle}' (reward={reward}) from {senderId} to local player! (grantWeapon={grantWeapon})");
    }

    private void Update()
    {
        if (!_isActive || PauseManager.IsGamePaused) return;

        // Continuous enforcement: guarantee modal is never made invisible by background animators or state changes
        ForceModalAlphaOpaque();

        // Maintain cursor free
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        SetPlayerLookInputs(false);

        // Hotkeys [Y] Accept / [N] Decline, or [Space / Enter / Esc] for Outcome modal
        if (Keyboard.current != null)
        {
            if (_isOutcomeMode)
            {
                if (Keyboard.current.enterKey.wasPressedThisFrame ||
                    Keyboard.current.spaceKey.wasPressedThisFrame ||
                    Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    OnAcceptClicked();
                    return;
                }
                return;
            }

            if (Keyboard.current.yKey.wasPressedThisFrame)
            {
                OnAcceptClicked();
                return;
            }
            if (Keyboard.current.nKey.wasPressedThisFrame)
            {
                OnDeclineClicked();
                return;
            }
        }
    }

    private void LateUpdate()
    {
        if (!_isActive) return;

        // Guarantee root modal stays active
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        // Guarantee canvas alpha is 100% opaque after any Animators run
        ForceModalAlphaOpaque();

        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        if (!Cursor.visible)
        {
            Cursor.visible = true;
        }
    }

    public void OnAcceptClicked()
    {
        if (!_isActive) return;

        if (_isOutcomeMode)
        {
            _isOutcomeMode = false;
            _isActive = false;
            SetVisible(false, modifyCursor: true);
            return;
        }

        _isActive = false;

        Debug.Log($"[DealNotificationUI] Local player ACCEPTED deal from Girl {_currentGirlSenderId} (grantWeapon={_grantWeapon})");

        // Immediately grant and equip weapon on the local investigator character if requested
        if (_grantWeapon)
        {
            Unity.Netcode.NetworkObject localObj = null;
            if (Unity.Netcode.NetworkManager.Singleton != null)
            {
                if (Unity.Netcode.NetworkManager.Singleton.SpawnManager != null)
                {
                    localObj = Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                }
                if (localObj == null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
                {
                    localObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
                }
            }

            if (localObj != null)
            {
                var combat = localObj.GetComponent<InvestigatorCombatNet>();
                if (combat != null)
                {
                    combat.GrantMeleeWeapon(true);
                }

                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification("Deal Sealed: Weapon granted!", 4.5f);
                }
            }
        }

        // Start active deal timer HUD
        if (ActiveDealMissionHUD.Instance != null)
        {
            ActiveDealMissionHUD.Instance.StartMission(_currentTitle, _currentTerms, _currentTimeLimitSeconds, _currentPenaltyCredits, _currentRewardCredits, _currentGirlSenderId);
        }

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.RespondToDeal(_currentGirlSenderId, true, _grantWeapon);
        }

        SetVisible(false, modifyCursor: true);
    }

    public void OnDeclineClicked()
    {
        if (!_isActive) return;
        _isActive = false;

        Debug.Log($"[DealNotificationUI] Local player DECLINED deal from Girl {_currentGirlSenderId}");
        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.RespondToDeal(_currentGirlSenderId, false, _grantWeapon);
        }

        SetVisible(false, modifyCursor: true);
    }

    public void Hide()
    {
        _isActive = false;
        _isOutcomeMode = false;
        SetVisible(false, modifyCursor: false);
    }

    public void ShowPactOutcomeModal(bool success, string pactTitle, int rewardCredits, int penaltyCredits)
    {
        if (success)
        {
            if (DealCompletionModalUI.Instance != null)
            {
                DealCompletionModalUI.Instance.Show(pactTitle, rewardCredits);
            }
        }
        else
        {
            if (DealFailureModalUI.Instance != null)
            {
                DealFailureModalUI.Instance.Show(pactTitle, penaltyCredits);
            }
        }
    }

    private static void SanitizeModal(ModalWindowManager modal)
    {
        if (modal == null) return;
        modal.useLocalization = false;
        modal.titleKey = string.Empty;
        modal.descriptionKey = string.Empty;
        modal.closeOnCancel = false;
        modal.closeOnConfirm = false;
        modal.startBehaviour = ModalWindowManager.StartBehaviour.Disable;
        modal.onConfirm.RemoveAllListeners();
        modal.onCancel.RemoveAllListeners();
        modal.onOpen.RemoveAllListeners();
        modal.onClose.RemoveAllListeners();

        var locObj = modal.GetComponent("LocalizedObject") as Behaviour;
        if (locObj != null)
        {
            locObj.enabled = false;
        }

        var exitComp = modal.GetComponent("ExitGame");
        if (exitComp != null)
        {
            Destroy(exitComp);
        }
    }
}
