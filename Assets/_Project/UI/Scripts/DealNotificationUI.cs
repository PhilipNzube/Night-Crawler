using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using NightCrawler.Economy;
using NightCrawler.UI;

/// <summary>
/// SOLID — SRP: Displays an incoming dark deal proposal to an Investigator.
/// Shows exact title, terms, reward, and provides Accept [Y] / Decline [N] actions.
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

    [Header("UI References")]
    public GameObject panel;
    public TMP_Text titleText;
    public TMP_Text termsText;
    public TMP_Text rewardText;
    public Button acceptButton;
    public Button declineButton;

    [Header("Auto-Timeout")]
    public float timeoutSeconds = 15f;
    private float _timer;
    private ulong _currentGirlSenderId;
    private bool _grantWeapon;
    private bool _isActive = false;

    private CanvasGroup _canvasGroup;

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
            // Activate the GameObject in the hierarchy so Awake runs and listeners hook up,
            // but keep it visually hidden via CanvasGroup until a deal is offered!
            found.gameObject.SetActive(true);
            found.SetVisible(false);
            Debug.Log($"[DealNotificationUI] Discovered and initialized prompt: {found.gameObject.name} (activeInHierarchy={found.gameObject.activeInHierarchy})");
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

        if (_canvasGroup == null)
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
        if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);

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

        if (_canvasGroup == null)
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        if (visible)
        {
            gameObject.SetActive(true);
            if (panel != null) panel.SetActive(true);

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

    private int _currentTimeLimitSeconds = 120;
    private int _currentPenaltyCredits = 15;
    private string _currentTitle = "DARK PACT";
    private string _currentTerms = "";

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

        if (titleText != null) titleText.text = title;
        if (termsText != null)
        {
            termsText.text = $"{terms}\n\n<color=#F1C40F>⏱ Time Limit: {timeLimitSeconds}s</color>\n<color=#E74C3C>⚠ Penalty on Failure: -{penaltyCredits} {CurrencyConfig.CurrencySymbol} (Deducted from Stake)</color>";
        }
        if (rewardText != null) rewardText.text = $"REWARD: {reward}";

        // Crucial: Make sure the GameObject itself is ACTIVE in the hierarchy!
        gameObject.SetActive(true);
        if (panel != null) panel.SetActive(true);

        SetVisible(true);
        Debug.Log($"[DealNotificationUI] Displaying deal '{title}' from {senderId} to local player! (grantWeapon={grantWeapon}, time={timeLimitSeconds}s)");
    }

    private void Update()
    {
        if (!_isActive || PauseManager.IsGamePaused) return;

        // Force cursor to stay free and visible while this modal is active (prevents clicks from re-locking cursor)
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        if (!Cursor.visible)
        {
            Cursor.visible = true;
        }
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
                    NotificationManager.Instance.ShowNotification("Pact Sealed: Axe granted! (Note: pact weapons cannot harm cave monsters, only investigators)", 4.5f);
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
}
