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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
        if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);

        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

        if (panel != null) panel.SetActive(true);
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

        if (panel != null) panel.SetActive(false);
    }

    public void OnDeclineClicked()
    {
        if (!_isActive) return;
        _isActive = false;

        if (DealSystemNet.Instance != null)
        {
            DealSystemNet.Instance.RespondToDeal(_currentGirlSenderId, false, _grantWeapon);
        }

        if (panel != null) panel.SetActive(false);
    }
}
