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
    [Header("Heat UI Hierarchy (GameScene)")]
    public CanvasGroup allCanvasGroup;
    public Animator allAnimator;
    public Transform contentLayoutGroup;
    public Michsky.UI.Heat.HotkeyEvent endGameHotkey;

    [Header("Heat UI Achievements")]
    [Tooltip("Prefab instantiated into contentLayoutGroup for each achievement (e.g. Heat Achievement Item prefab).")]
    public GameObject achievementItemPrefab;

    [Tooltip("GameObject (e.g. 'NoAchievementTexts') shown when there are no achievement prefabs or items, and hidden when achievements exist.")]
    public GameObject noAchievementTexts;

    [Header("Heat UI Summary Counters (Bottom)")]
    [Tooltip("The 'Summary' horizontal layout container GameObject.")]
    public Transform summaryContainer;

    [Tooltip("Displays the Total Stake Won counter (e.g. inside Summary/TotalStakeWon/Total).")]
    public TextMeshProUGUI totalStakeWonText;

    [Tooltip("Displays the initial Staked amount (e.g. inside Summary/Stake/Total).")]
    public TextMeshProUGUI stakedAmountText;

    [Tooltip("Displays the Achievements count / contribution bonus (e.g. inside Summary/Achievements/Total).")]
    public TextMeshProUGUI achievementsCountText;

    [Tooltip("Displays Net Payout total (e.g. inside Summary/NetPayout/Total).")]
    public TextMeshProUGUI netPayoutText;

    private MatchPayoutSummary? _latestPayout;
    private CanvasGroup _rootCanvasGroup;

    private void Awake()
    {
        ResolveSummaryReferences();

        // Wire EndGame hotkey
        if (endGameHotkey != null)
        {
            endGameHotkey.onHotkeyPress.RemoveListener(OnEndGameClicked);
            endGameHotkey.onHotkeyPress.AddListener(OnEndGameClicked);
        }

        // Ensure root GameObject has its own CanvasGroup to suppress the entire hierarchy including background and summary
        _rootCanvasGroup = GetComponent<CanvasGroup>();
        if (_rootCanvasGroup == null)
        {
            _rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        _rootCanvasGroup.alpha = 0f;
        _rootCanvasGroup.interactable = false;
        _rootCanvasGroup.blocksRaycasts = false;

        // Auto-resolve visual references if unlinked
        if (allCanvasGroup == null)
        {
            allCanvasGroup = GetComponentInChildren<CanvasGroup>(true);
        }
        if (allAnimator == null)
        {
            allAnimator = GetComponentInChildren<Animator>(true);
        }

        // Force hide immediately on spawn/load - prevents Animator from driving alpha back to 1 on frame 0
        if (allAnimator != null)
        {
            allAnimator.enabled = false;
        }

        if (allCanvasGroup != null)
        {
            allCanvasGroup.alpha = 0f;
            allCanvasGroup.interactable = false;
            allCanvasGroup.blocksRaycasts = false;
        }

        // Deactivate all child visual objects (Background, All, Summary, Hotkeys)
        foreach (Transform child in transform)
        {
            if (child != null && child.gameObject != gameObject)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private void Start()
    {
        // Re-lock cursor whenever a new match/scene begins
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Re-enforce hidden state on Start to prevent any frame 0 animator overrides
        if (_rootCanvasGroup != null)
        {
            _rootCanvasGroup.alpha = 0f;
            _rootCanvasGroup.interactable = false;
            _rootCanvasGroup.blocksRaycasts = false;
        }
        foreach (Transform child in transform)
        {
            if (child != null && child.gameObject != gameObject)
            {
                child.gameObject.SetActive(false);
            }
        }
        
        MatchEconomyManager.OnLocalPayoutReceived += HandlePayoutReceived;
    }

    private void OnDestroy()
    {
        MatchEconomyManager.OnLocalPayoutReceived -= HandlePayoutReceived;

        if (endGameHotkey != null)
        {
            endGameHotkey.onHotkeyPress.RemoveListener(OnEndGameClicked);
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

    public void ShowResultDirectly(string msg)
    {
        Debug.Log($"[UI] Setting Match Result Text: {msg}");
        ShowResult(msg);
    }

    private void ShowResult(string msg)
    {
        Debug.Log($"[UI-OVERLAY] ShowResult called with: {msg}");

        // Dismiss Death UI cleanly when Match Result Overlay appears
        if (DeathUI.Instance != null && DeathUI.Instance.deathCanvasGroup != null)
        {
            DeathUI.Instance.deathCanvasGroup.alpha = 0f;
            DeathUI.Instance.deathCanvasGroup.interactable = false;
            DeathUI.Instance.deathCanvasGroup.blocksRaycasts = false;
            if (DeathUI.Instance.deathPanel != null)
            {
                DeathUI.Instance.deathPanel.SetActive(false);
            }
        }
        
        if (_rootCanvasGroup != null)
        {
            _rootCanvasGroup.alpha = 1f;
            _rootCanvasGroup.interactable = true;
            _rootCanvasGroup.blocksRaycasts = true;
        }

        foreach (Transform child in transform)
        {
            if (child != null && child.gameObject != gameObject)
            {
                child.gameObject.SetActive(true);
            }
        }

        if (allCanvasGroup != null)
        {
            allCanvasGroup.alpha = 1f;
            allCanvasGroup.interactable = true;
            allCanvasGroup.blocksRaycasts = true;
        }

        if (allAnimator != null)
        {
            allAnimator.enabled = true;
            allAnimator.Rebind();
            allAnimator.Play("In");
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateEconomyText();
        RefreshAchievementsUI();
    }

    private void UpdateEconomyText()
    {
        if (_latestPayout.HasValue)
        {
            var p = _latestPayout.Value;

            // 1. Update Heat UI bottom summary counters
            if (totalStakeWonText != null)
            {
                totalStakeWonText.text = p.won ? (p.potWinnings > 0 ? $"+{p.potWinnings}" : $"{p.stakedAmount}") : "0";
            }
            if (stakedAmountText != null)
            {
                stakedAmountText.text = p.stakedAmount.ToString();
            }
            if (achievementsCountText != null)
            {
                achievementsCountText.text = p.contributionBonus > 0 ? $"+{p.contributionBonus}" : "0";
            }
            if (netPayoutText != null)
            {
                netPayoutText.text = p.netPayout >= 0 ? $"+{p.netPayout}" : p.netPayout.ToString();
            }

            // 2. Populate Heat UI achievement items into layout group
            PopulateAchievements(p);
            RefreshAchievementsUI();

            // 3. Fallback text breakdown if legacy text components are present
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
        }
        else
        {
            RefreshAchievementsUI();
        }
    }

    private void PopulateAchievements(MatchPayoutSummary p)
    {
        if (contentLayoutGroup == null)
        {
            RefreshAchievementsUI();
            return;
        }

        // Clear previously instantiated items (preserving noAchievementTexts if placed inside contentLayoutGroup)
        for (int i = contentLayoutGroup.childCount - 1; i >= 0; i--)
        {
            GameObject child = contentLayoutGroup.GetChild(i).gameObject;
            if (noAchievementTexts != null && (child == noAchievementTexts || child.transform.IsChildOf(noAchievementTexts.transform))) continue;
            Destroy(child);
        }

        if (achievementItemPrefab == null)
        {
            RefreshAchievementsUI();
            return;
        }

        // 1. Pot Share Bounty (only when won and pot was shared)
        if (p.won && p.potWinnings > 0)
        {
            SpawnAchievementEntry("High Roller Bounty", $"Secured +{p.potWinnings} {CurrencyConfig.CurrencySymbol} share from the shared match stake pot.", true);
        }

        // 2. Contribution Bonuses from bonusDetails
        if (!string.IsNullOrEmpty(p.bonusDetails))
        {
            string[] items = p.bonusDetails.Split(new[] { ',', ';', '\n', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in items)
            {
                string trimmed = item.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                string aTitle = "Field Merit";
                string aDesc = trimmed;

                if (trimmed.IndexOf("Exorcism", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Master Exorcist";
                else if (trimmed.IndexOf("Heal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Combat Medic";
                else if (trimmed.IndexOf("Clue", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Keen Investigator";
                else if (trimmed.IndexOf("Slain", System.StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("Wiped", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Apex Predator";
                else if (trimmed.IndexOf("Dark Deal", System.StringComparison.OrdinalIgnoreCase) >= 0 || trimmed.IndexOf("Fulfilled", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Dark Deal Fulfilled";
                else if (trimmed.IndexOf("Traitor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "Traitor Pact Exposed";
                else if (trimmed.IndexOf("Abandon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    aTitle = "MIA";

                SpawnAchievementEntry(aTitle, aDesc, true);
            }
        }

        RefreshAchievementsUI();
    }

    public void RefreshAchievementsUI()
    {
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(RefreshAchievementsUIRoutine());
        }
        else
        {
            ApplyAchievementsVisibility();
        }
    }

    private IEnumerator RefreshAchievementsUIRoutine()
    {
        // Wait for end of frame to ensure Animator.Rebind() or Play("In") keyframes have evaluated
        yield return new WaitForEndOfFrame();
        ApplyAchievementsVisibility();
    }

    private void ApplyAchievementsVisibility()
    {
        if (noAchievementTexts == null) return;

        int activeAchievementCount = 0;
        if (contentLayoutGroup != null && achievementItemPrefab != null)
        {
            for (int i = 0; i < contentLayoutGroup.childCount; i++)
            {
                Transform child = contentLayoutGroup.GetChild(i);
                if (child == null) continue;
                if (child.gameObject == noAchievementTexts || child.IsChildOf(noAchievementTexts.transform)) continue;
                if (child.gameObject.activeSelf)
                {
                    activeAchievementCount++;
                }
            }
        }

        bool hasAchievements = activeAchievementCount > 0;
        noAchievementTexts.SetActive(!hasAchievements);

        if (!hasAchievements)
        {
            // Ensure CanvasGroup on noAchievementTexts is fully visible
            var cg = noAchievementTexts.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            // Ensure parent hierarchy of noAchievementTexts is active
            Transform p = noAchievementTexts.transform.parent;
            while (p != null && p != transform)
            {
                if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                p = p.parent;
            }
        }
    }

    public void UpdateNoAchievementsVisibility(int achievementCount)
    {
        RefreshAchievementsUI();
    }

    private void SpawnAchievementEntry(string title, string desc, bool unlocked)
    {
        GameObject go = Instantiate(achievementItemPrefab, contentLayoutGroup);
        go.SetActive(true);

        var item = go.GetComponent<AchievementItem>();
        if (item != null)
        {
            if (item.titleObj != null) item.titleObj.text = title;
            if (item.descriptionObj != null) item.descriptionObj.text = desc;
            if (item.lockedIndicator != null) item.lockedIndicator.SetActive(!unlocked);
            if (item.unlockedIndicator != null) item.unlockedIndicator.SetActive(unlocked);
        }
        else
        {
            var tmpList = go.GetComponentsInChildren<TextMeshProUGUI>();
            if (tmpList.Length > 0 && tmpList[0] != null) tmpList[0].text = title;
            if (tmpList.Length > 1 && tmpList[1] != null) tmpList[1].text = desc;
        }
    }

    private void ResolveSummaryReferences()
    {
        if (summaryContainer == null)
        {
            Transform found = transform.Find("Summary");
            if (found != null) summaryContainer = found;
        }

        if (summaryContainer != null)
        {
            if (totalStakeWonText == null) totalStakeWonText = ResolveChildText(summaryContainer, "TotalStakeWon", "StakeWon", "Total Stake Won", "Total");
            if (stakedAmountText == null) stakedAmountText = ResolveChildText(summaryContainer, "Stake", "Staked", "StakedAmount");
            if (achievementsCountText == null) achievementsCountText = ResolveChildText(summaryContainer, "Achievements", "Achievement", "Contribution");
            if (netPayoutText == null)
            {
                netPayoutText = ResolveChildText(summaryContainer, "NetPayout", "Payout", "Net Payout", "Common");
                if (netPayoutText != null && netPayoutText.transform.parent != null)
                {
                    var header = netPayoutText.transform.parent.Find("Header")?.GetComponentInChildren<TextMeshProUGUI>();
                    if (header != null && (header.text == "Common" || header.text == "Card Title"))
                    {
                        header.text = "NET PAYOUT";
                    }
                }
            }
        }
    }

    private TextMeshProUGUI ResolveChildText(Transform parent, params string[] candidateNames)
    {
        foreach (var cName in candidateNames)
        {
            Transform found = parent.Find(cName);
            if (found == null)
            {
                foreach (Transform child in parent)
                {
                    if (child.name.Replace(" ", "").Equals(cName.Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = child;
                        break;
                    }
                }
            }

            if (found != null)
            {
                Transform total = found.Find("Total");
                if (total != null)
                {
                    var tmp = total.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) return tmp;
                }
                var pillTmp = found.GetComponentInChildren<TextMeshProUGUI>();
                if (pillTmp != null) return pillTmp;
            }
        }
        return null;
    }


}
