using UnityEngine;
using TMPro;

/// <summary>
/// SOLID — SRP: Renders full-screen blackout and the chilling message
/// "Let me take the wheel for a sec☠️" when the player's character is possessed by the Girl.
/// </summary>
public class PossessionBlackoutOverlay : MonoBehaviour
{
    private static PossessionBlackoutOverlay _instance;
    public static PossessionBlackoutOverlay Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<PossessionBlackoutOverlay>(FindObjectsInactive.Include);
            }
            if (_instance != null && !_instance.gameObject.activeInHierarchy)
            {
                _instance.gameObject.SetActive(true);
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("UI References")]
    public CanvasGroup blackoutCanvasGroup;
    public TMP_Text possessMessageText;
    public TMP_Text subtitleText;
    public TMP_Text timerText;

    [Header("Message")]
    public string defaultMessage = "Let me take the wheel for a sec...";
    public string defaultSubtitle = "The Vengeful Spirit has taken control of your body...";

    [Header("Priest Possession Rejection Prompt")]
    public TMP_Text rejectPromptText;

    private float _possessionStartTime;
    private bool _isBlackoutActive;
    private float _rejectionWindowEndTime;
    private bool _isRejectionActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        SetBlackout(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_isBlackoutActive)
        {
            float elapsed = Time.time - _possessionStartTime;
            if (timerText != null)
            {
                timerText.text = $"Possessed: {elapsed:F1}s";
            }

            if (_isRejectionActive)
            {
                float remaining = Mathf.Max(0f, _rejectionWindowEndTime - Time.time);
                if (remaining > 0f)
                {
                    string promptStr = $"<color=#FFD700>|</color> [PRIEST WARD] Press [R] to PURGE SPIRIT & REJECT ({remaining:F1}s)";
                    if (rejectPromptText != null)
                    {
                        rejectPromptText.text = promptStr;
                    }
                    else if (subtitleText != null)
                    {
                        subtitleText.text = promptStr;
                    }
                    else if (possessMessageText != null)
                    {
                        possessMessageText.text = defaultMessage + "\n\n" + promptStr;
                    }
                }
                else
                {
                    HideRejectionPrompt();
                }
            }
        }
    }

    public void ShowRejectionPrompt(float durationSeconds)
    {
        _isRejectionActive = true;
        _rejectionWindowEndTime = Time.time + durationSeconds;
        string promptStr = $"<color=#FFD700>|</color> [PRIEST WARD] Press [R] to PURGE SPIRIT & REJECT ({durationSeconds:F1}s)";
        if (rejectPromptText != null)
        {
            rejectPromptText.gameObject.SetActive(true);
            rejectPromptText.text = promptStr;
        }
        else if (subtitleText != null)
        {
            subtitleText.text = promptStr;
        }
        else if (possessMessageText != null)
        {
            possessMessageText.text = defaultMessage + "\n\n" + promptStr;
        }
    }

    public void HideRejectionPrompt()
    {
        _isRejectionActive = false;
        if (rejectPromptText != null)
        {
            rejectPromptText.gameObject.SetActive(false);
        }
        else if (subtitleText != null)
        {
            subtitleText.text = defaultSubtitle;
        }
        else if (possessMessageText != null)
        {
            possessMessageText.text = defaultMessage;
        }
    }

    public void SetBlackout(bool active, string customMessage = null)
    {
        if (active)
        {
            // The Girl player must NEVER have a blackout screen!
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.LocalClient != null)
            {
                var localObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localObj != null && (localObj.GetComponent<GirlPossession>() != null || localObj.name.ToLower().Contains("girl")))
                {
                    return;
                }
            }
        }

        _isBlackoutActive = active;

        if (active)
        {
            _possessionStartTime = Time.time;
        }
        else
        {
            HideRejectionPrompt();
        }

        if (blackoutCanvasGroup == null)
            blackoutCanvasGroup = GetComponent<CanvasGroup>();

        if (blackoutCanvasGroup != null)
        {
            blackoutCanvasGroup.alpha = active ? 1f : 0f;
            blackoutCanvasGroup.blocksRaycasts = active;
            blackoutCanvasGroup.interactable = active;
        }

        if (possessMessageText != null)
        {
            possessMessageText.text = !string.IsNullOrEmpty(customMessage) ? customMessage : defaultMessage;
            possessMessageText.gameObject.SetActive(active);
        }

        if (subtitleText != null)
        {
            subtitleText.text = defaultSubtitle;
            subtitleText.gameObject.SetActive(active);
        }

        if (timerText != null)
        {
            timerText.gameObject.SetActive(active);
        }

        // If no CanvasGroup exists, fallback to GameObject active state
        if (blackoutCanvasGroup == null)
        {
            gameObject.SetActive(active);
        }
    }
}
