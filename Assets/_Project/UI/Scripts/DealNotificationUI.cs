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
    public static DealNotificationUI Instance { get; private set; }

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
        if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void SetVisible(bool visible)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }

        if (panel != null && panel != gameObject)
        {
            panel.SetActive(visible);
        }

        if (visible)
        {
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

        SetVisible(true);
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

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.RespondToDeal(_currentGirlSenderId, false, _grantWeapon);
        }

        SetVisible(false);
    }
}
