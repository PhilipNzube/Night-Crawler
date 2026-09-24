using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using NightCrawler.Economy;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Displays match outcome and detailed financial payout breakdown.
/// Shows:
/// - Primary result message (VICTORY / DEFEAT)
/// - Staked credits returned / lost
/// - Proportional pot winnings
/// - Contribution bonuses (exorcism, heals, clues, monster kills)
/// - Penalties incurred (traitor / spirit loss)
/// - Updated total persistent credit balance
/// </summary>
public class MatchResultOverlay : MonoBehaviour
{
    public GameObject overlayPanel;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI subText;
    public TextMeshProUGUI economyBreakdownText;

    [Header("Michsky Heat / Dark UI")]
    public ModalWindowManager heatModalWindow;

    [Header("Heat UI Hierarchy (GameScene)")]
    public CanvasGroup allCanvasGroup;
    public Animator allAnimator;
    public Transform contentLayoutGroup;
    public TextMeshProUGUI summaryText;
    public Michsky.UI.Heat.HotkeyEvent endGameHotkey;
    public Michsky.UI.Heat.ButtonManager endGameButton;

    private MatchPayoutSummary? _latestPayout;

    private void Awake()
    {
        EnsureUIRuntime();

        // Wire EndGame button / hotkey
        if (endGameHotkey != null)
        {
            endGameHotkey.onHotkeyPress.RemoveListener(OnEndGameClicked);
            endGameHotkey.onHotkeyPress.AddListener(OnEndGameClicked);
        }
        if (endGameButton != null)
        {
            endGameButton.onClick.RemoveListener(OnEndGameClicked);
            endGameButton.onClick.AddListener(OnEndGameClicked);
        }

        // Force hide immediately on spawn/load
        if (overlayPanel != null) overlayPanel.SetActive(false);
        if (allCanvasGroup != null)
        {
            allCanvasGroup.alpha = 0f;
            allCanvasGroup.interactable = false;
            allCanvasGroup.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        // Re-lock cursor whenever a new match/scene begins
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        // Setup initial UI states
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gameEnded.OnValueChanged += OnGameEnded;
        }

        MatchEconomyManager.OnLocalPayoutReceived += HandlePayoutReceived;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gameEnded.OnValueChanged -= OnGameEnded;
        }

        MatchEconomyManager.OnLocalPayoutReceived -= HandlePayoutReceived;

        if (endGameHotkey != null)
        {
            endGameHotkey.onHotkeyPress.RemoveListener(OnEndGameClicked);
        }
        if (endGameButton != null)
        {
            endGameButton.onClick.RemoveListener(OnEndGameClicked);
        }
    }

    public void OnEndGameClicked()
    {
        Debug.Log("[MatchResultOverlay] EndGame triggered. Returning to Lobby...");
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
        }

        if (LoadingScreen.Instance != null)
        {
            LoadingScreen.Instance.LoadScene("LobbyScene");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }
    }

    private void HandlePayoutReceived(MatchPayoutSummary summary)
    {
        _latestPayout = summary;
        UpdateEconomyText();
    }

    private void OnGameEnded(bool previousValue, bool newValue)
    {
        if (newValue) 
        {
            ShowResult("MATCH OVER"); // Local safety trigger
        }
    }

    public void ShowResultDirectly(string msg)
    {
        Debug.Log($"[UI] Setting Match Result Text: {msg}");
        ShowResult(msg);
    }

    private void ShowResult(string msg)
    {
        Debug.Log($"[UI-OVERLAY] ShowResult called with: {msg}");
        
        if (overlayPanel == null)
        {
            EnsureUIRuntime();
            if (overlayPanel == null) return;
        }

        overlayPanel.SetActive(true);

        // Unlock cursor for match end
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            var inputs = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<StarterAssets.StarterAssetsInputs>();
            if (inputs != null) 
            {
                inputs.cursorLocked = false;
                inputs.cursorInputForLook = false;
            }
        }

        // Primary message
        if (resultText != null)
        {
            resultText.text = msg;
        }

        // Outcome subtext
        string outcome = "GAME OVER";
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            bool isGirl = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.name.Contains("Girl");
            bool demonWon = msg.Contains("Vengeful Spirit Wins") || msg.Contains("Demon Wins") || msg.Contains("No Survivors");
            bool explorersWon = msg.Contains("Investigators Win") || msg.Contains("Explorers Win") || msg.Contains("Survived") || msg.Contains("Slain");

            if ((isGirl && demonWon) || (!isGirl && explorersWon)) outcome = "VICTORY";
            else if ((isGirl && explorersWon) || (!isGirl && demonWon)) outcome = "DEFEAT";
        }

        if (subText != null)
        {
            subText.text = $"{outcome} • Returning to Lobby...";
        }

        if (summaryText != null)
        {
            summaryText.text = $"{outcome}\n<size=75%>{msg}</size>";
        }

        if (allAnimator != null)
        {
            allAnimator.enabled = true;
            allAnimator.Rebind();
            allAnimator.Play("In");
        }
        else if (allCanvasGroup != null)
        {
            allCanvasGroup.alpha = 1f;
            allCanvasGroup.interactable = true;
            allCanvasGroup.blocksRaycasts = true;
        }

        if (heatModalWindow != null)
        {
            heatModalWindow.titleText = msg;
            heatModalWindow.descriptionText = subText != null ? subText.text : outcome;
            heatModalWindow.UpdateUI();
            heatModalWindow.OpenWindow();
        }

        UpdateEconomyText();

        StartCoroutine(ResultsPulse());
    }

    private void UpdateEconomyText()
    {
        if (_latestPayout.HasValue)
        {
            var p = _latestPayout.Value;
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("<b>— MATCH SETTLEMENT —</b>");

            if (p.won)
            {
                sb.AppendLine($"Stake Returned: +{p.stakedAmount} {CurrencyConfig.CurrencySymbol}");
                if (p.potWinnings > 0)
                    sb.AppendLine($"Pot Share: +{p.potWinnings} {CurrencyConfig.CurrencySymbol}");
                if (p.contributionBonus > 0)
                    sb.AppendLine($"Contribution Bonus: +{p.contributionBonus} {CurrencyConfig.CurrencySymbol} ({p.bonusDetails})");

                sb.AppendLine($"<b>Net Payout: +{p.netPayout} {CurrencyConfig.CurrencyName}</b>");
            }
            else
            {
                sb.AppendLine($"Stake Forfeited: -{p.stakedAmount} {CurrencyConfig.CurrencySymbol}");
                if (p.penaltyDeduction > 0)
                    sb.AppendLine($"Penalty: -{p.penaltyDeduction} {CurrencyConfig.CurrencySymbol} ({p.bonusDetails})");

                sb.AppendLine($"<b>Net Loss: {p.netPayout} {CurrencyConfig.CurrencyName}</b>");
            }

            sb.AppendLine($"<size=14>Current Balance: <b>{p.newBalance} {CurrencyConfig.CurrencyName}</b></size>");

            if (economyBreakdownText != null) economyBreakdownText.text = sb.ToString();

            if (heatModalWindow != null)
            {
                heatModalWindow.descriptionText = $"{subText?.text}\n\n{sb.ToString()}";
                heatModalWindow.UpdateUI();
            }
        }
        else
        {
            if (economyBreakdownText != null) economyBreakdownText.text = "Calculating match earnings...";
        }
    }

    private IEnumerator ResultsPulse()
    {
        float t = 0;
        while (t < 1.0f)
        {
            t += Time.deltaTime;
            if (overlayPanel != null)
            {
                overlayPanel.transform.localScale = Vector3.one * (1.0f + Mathf.PingPong(t * 2, 0.05f));
            }
            yield return null;
        }
    }

    private void EnsureUIRuntime()
    {
        if (overlayPanel != null || allCanvasGroup != null || allAnimator != null) return;

        Canvas targetCanvas = GetComponentInParent<Canvas>();
        if (targetCanvas == null) targetCanvas = FindFirstObjectByType<Canvas>();
        if (targetCanvas == null) return;

        var panel = new GameObject("MatchResultPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(targetCanvas.transform, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(620, 440);

        var img = panel.GetComponent<Image>();
        img.color = new Color(0.04f, 0.05f, 0.07f, 0.95f);

        // Result Text
        var resObj = new GameObject("ResultText", typeof(RectTransform), typeof(TextMeshProUGUI));
        resObj.transform.SetParent(panel.transform, false);
        var resRt = resObj.GetComponent<RectTransform>();
        resRt.anchoredPosition = new Vector2(0, 160);
        resRt.sizeDelta = new Vector2(580, 50);
        resultText = resObj.GetComponent<TextMeshProUGUI>();
        resultText.fontSize = 28;
        resultText.fontStyle = FontStyles.Bold;
        resultText.alignment = TextAlignmentOptions.Center;
        resultText.color = new Color(1f, 0.85f, 0.3f, 1f);

        // Sub Text
        var subObj = new GameObject("SubText", typeof(RectTransform), typeof(TextMeshProUGUI));
        subObj.transform.SetParent(panel.transform, false);
        var subRt = subObj.GetComponent<RectTransform>();
        subRt.anchoredPosition = new Vector2(0, 115);
        subRt.sizeDelta = new Vector2(580, 35);
        subText = subObj.GetComponent<TextMeshProUGUI>();
        subText.fontSize = 18;
        subText.alignment = TextAlignmentOptions.Center;
        subText.color = new Color(0.85f, 0.9f, 0.95f, 1f);

        // Economy Breakdown Text
        var ecoObj = new GameObject("EconomyBreakdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
        ecoObj.transform.SetParent(panel.transform, false);
        var ecoRt = ecoObj.GetComponent<RectTransform>();
        ecoRt.anchoredPosition = new Vector2(0, -20);
        ecoRt.sizeDelta = new Vector2(540, 210);
        economyBreakdownText = ecoObj.GetComponent<TextMeshProUGUI>();
        economyBreakdownText.fontSize = 16;
        economyBreakdownText.lineSpacing = 15;
        economyBreakdownText.alignment = TextAlignmentOptions.Center;
        economyBreakdownText.color = Color.white;

        overlayPanel = panel;
        overlayPanel.SetActive(false);
    }
}
