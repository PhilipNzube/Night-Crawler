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
                if (_instance != null && !_instance.gameObject.activeInHierarchy)
                {
                    _instance.gameObject.SetActive(true);
                }
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
    public string defaultMessage = "Let me take the wheel for a sec☠️";
    public string defaultSubtitle = "The Vengeful Spirit has taken control of your body...";

    private float _possessionStartTime;
    private bool _isBlackoutActive;

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
        }
    }

    public void SetBlackout(bool active, string customMessage = null)
    {
        _isBlackoutActive = active;

        if (active)
        {
            _possessionStartTime = Time.time;
        }

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

        gameObject.SetActive(active);
    }
}
