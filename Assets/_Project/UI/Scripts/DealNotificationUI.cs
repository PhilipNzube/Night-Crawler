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

    private int _currentTimeLimitSeconds = 120;
    private int _currentPenaltyCredits = 15;
    private string _currentTitle = "DARK PACT";
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
            found.gameObject.SetActive(true);
            found.SetVisible(false);
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

        SanitizeModal(heatModalWindow);

        if (acceptButton != null)
        {
            acceptButton.onClick.RemoveAllListeners();
            acceptButton.onClick.AddListener(OnAcceptClicked);
        }

        if (declineButton != null)
        {
            declineButton.onClick.RemoveAllListeners();
            declineButton.onClick.AddListener(OnDeclineClicked);
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public bool IsActive => _isActive;

    private void SetVisible(bool visible)
    {
        _isActive = visible;

        if (heatModalWindow != null)
        {
            if (visible) heatModalWindow.OpenWindow();
            else heatModalWindow.CloseWindow();
        }

        if (visible)
        {
            gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetPlayerLookInputs(false);
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetPlayerLookInputs(true);
        }
    }

    private void SetPlayerLookInputs(bool allowLookAndLock)
    {
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClient != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var inputs = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null)
            {
                inputs.cursorLocked = allowLookAndLock;
                inputs.cursorInputForLook = allowLookAndLock;
            }
        }
    }

    public void DisplayDealOffer(ulong senderId, string title, string terms, string reward, bool grantWeapon, int timeLimitSeconds = 120, int penaltyCredits = 15)
    {
        // If local player is dead, reject/ignore immediately
        var localObj = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localObj != null)
        {
            if ((localObj.TryGetComponent<TargetHealth>(out var th) && (th.isCorpse.Value || th.CurrentHealth <= 0)) ||
                (localObj.TryGetComponent<HealthSystem>(out var hs) && hs.IsDead))
            {
                Debug.Log("[DealNotificationUI] Local player is dead; suppressing deal offer display.");
                SetVisible(false);
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

        if (headerTitleText != null) headerTitleText.text = "DEAL PROPOSAL";
        if (dealNameText != null) dealNameText.text = title.ToUpper();
        if (rewardText != null) rewardText.text = $"REWARD: {reward}";
        if (termsDescriptionText != null)
        {
            termsDescriptionText.text = $"{terms}\n\n<color=#F1C40F>⏱ Time Limit: {timeLimitSeconds}s</color>\n<color=#E74C3C>⚠ Penalty on Failure: -{penaltyCredits} {CurrencyConfig.CurrencySymbol}</color>";
        }

        if (heatModalWindow != null)
        {
            heatModalWindow.titleText = "DEAL PROPOSAL";
            heatModalWindow.descriptionText = $"{terms}\n\n⏱ Time Limit: {timeLimitSeconds}s | ⚠ Penalty: -{penaltyCredits} {CurrencyConfig.CurrencySymbol}\n\nREWARD: {reward}";
        }

        gameObject.SetActive(true);
        SetVisible(true);
        Debug.Log($"[DealNotificationUI] Displaying deal '{title}' from {senderId} to local player! (grantWeapon={grantWeapon}, time={timeLimitSeconds}s)");
    }

    private void Update()
    {
        if (!_isActive || PauseManager.IsGamePaused) return;

        // Maintain cursor free
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        SetPlayerLookInputs(false);

        // Hotkeys [Y] Accept / [N] Decline
        if (Keyboard.current != null)
        {
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

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            OnDeclineClicked();
        }
    }

    public void OnAcceptClicked()
    {
        if (!_isActive) return;
        _isActive = false;

        Debug.Log($"[DealNotificationUI] Local player ACCEPTED deal from Girl {_currentGirlSenderId} (grantWeapon={_grantWeapon})");

        // Immediately grant and equip weapon on the local investigator character if requested
        if (_grantWeapon)
        {
            if (Unity.Netcode.NetworkManager.Singleton != null &&
                Unity.Netcode.NetworkManager.Singleton.LocalClient != null &&
                Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var combat = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<InvestigatorCombatNet>();
                if (combat != null)
                {
                    combat.GrantMeleeWeapon(true);
                }

                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification("Pact Sealed: Weapon granted!", 4.5f);
                }
            }
        }

        // Start active deal timer HUD
        if (ActiveDealMissionHUD.Instance != null)
        {
            ActiveDealMissionHUD.Instance.StartMission(_currentTitle, _currentTerms, _currentTimeLimitSeconds, _currentPenaltyCredits);
        }

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.RespondToDeal(_currentGirlSenderId, true, _grantWeapon);
        }

        SetVisible(false);
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

        SetVisible(false);
    }

    private static void SanitizeModal(ModalWindowManager modal)
    {
        if (modal == null) return;
        modal.useLocalization = false;
        modal.titleKey = string.Empty;
        modal.descriptionKey = string.Empty;

        var exitComp = modal.GetComponent("ExitGame");
        if (exitComp != null)
        {
            Destroy(exitComp);
        }
    }
}
