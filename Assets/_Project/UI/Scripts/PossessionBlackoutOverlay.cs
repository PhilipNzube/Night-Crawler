using UnityEngine;
using TMPro;

/// <summary>
/// SOLID — SRP: Renders full-screen blackout and the chilling message
/// "Let me take the wheel for a sec☠️" when the player's character is possessed by the Girl.
/// </summary>
public class PossessionBlackoutOverlay : MonoBehaviour
{
    public static PossessionBlackoutOverlay Instance { get; private set; }

    [Header("UI References")]
    public CanvasGroup blackoutCanvasGroup;
    public TMP_Text possessMessageText;

    [Header("Message")]
    public string defaultMessage = "Let me take the wheel for a sec☠️";

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

    public void SetBlackout(bool active, string customMessage = null)
    {
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

        gameObject.SetActive(active);
    }
}
