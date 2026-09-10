using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

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

    private void SetVisible(bool visible)
    {
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
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void DisplayDealOffer(ulong senderId, string title, string terms, string reward, bool grantWeapon)
    {
        _currentGirlSenderId = senderId;
        _grantWeapon = grantWeapon;
        _timer = timeoutSeconds;
        _isActive = true;

        if (titleText != null) titleText.text = title;
        if (termsText != null) termsText.text = terms;
        if (rewardText != null) rewardText.text = $"REWARD: {reward}";

        // Crucial: Make sure the GameObject itself is ACTIVE in the hierarchy!
        gameObject.SetActive(true);
        if (panel != null) panel.SetActive(true);

        SetVisible(true);
        Debug.Log($"[DealNotificationUI] Displaying deal '{title}' from {senderId} to local player!");
    }

    private void Update()
    {
        if (!_isActive) return;

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

        Debug.Log($"[DealNotificationUI] Local player ACCEPTED deal from Girl {_currentGirlSenderId}");
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
