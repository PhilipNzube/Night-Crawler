using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;
using NightCrawler.UI;

namespace NightCrawler.Economy.UI
{
    /// <summary>
    /// Supported view categories for the Lobby Upgrade UI.
    /// </summary>
    public enum UpgradeViewMode
    {
        InvestigatorStats,
        GirlStats
    }

    /// <summary>
    /// SOLID — SRP: Manages the Persistent Upgrades shop in the Lobby Scene (Shop tab).
    /// Used on both the Investigator and Vengeful Spirit stat sections in LobbyCanvas.
    /// </summary>
    public class LobbyUpgradeUI : MonoBehaviour
    {
        [Header("1. Mode & Root Window")]
        [Tooltip("Select whether this panel upgrades Investigator stats or Girl stats.")]
        public UpgradeViewMode viewMode = UpgradeViewMode.InvestigatorStats;

        [Tooltip("The root panel GameObject (defaults to this GameObject).")]
        public GameObject panelRoot;

        [Tooltip("Michsky Heat Modal Window Manager on this window (optional).")]
        public ModalWindowManager heatModalWindow;

        [Header("2. Confirmation Modal (Michsky Heat UI)")]
        [Tooltip("The Modal Window that pops up when tapping a stat to confirm the purchase (e.g. Purchase Window).")]
        public ModalWindowManager purchaseConfirmModal;

        [System.Serializable]
        public class ManualStatItem
        {
            [Tooltip("The stat this card upgrades.")]
            public UpgradeStatType statType;
            [Tooltip("The Heat Shop Button card for this stat.")]
            public ShopButtonManager heatShopButton;
            [Tooltip("The actual clickable Purchase button inside the stat card (Content/Buttons/Purchase).")]
            public ButtonManager purchaseButton;
            [Tooltip("Indicator shown when the stat reaches max level (Content/Buttons/Purchased).")]
            public GameObject purchasedIndicator;
            [Tooltip("Alternative: Standard Heat Button.")]
            public ButtonManager heatButton;
            [Tooltip("Alternative: Heat Box Button.")]
            public BoxButtonManager heatBoxButton;
            [Tooltip("Optional text displaying tier (e.g. 'Lv. 2/5').")]
            public TextMeshProUGUI levelText;
            [Tooltip("Optional text displaying effect or cost.")]
            public TextMeshProUGUI effectText;
            [Tooltip("Optional: Heat UI ProgressBar visualizing tier level.")]
            public ProgressBar progressBar;
            [Tooltip("Optional: Standard Unity UI Slider visualizing tier level.")]
            public Slider sliderBar;
            [Tooltip("Optional: Standard Unity UI filled Image visualizing tier level.")]
            public Image fillImageBar;
        }

        [Header("3. Stat Cards (Auto-Discovered or Manual)")]
        [Tooltip("List of stat cards manually placed in the hierarchy. If empty, cards are auto-discovered from children!")]
        public List<ManualStatItem> manualStatItems = new List<ManualStatItem>();

        [Header("4. Displays & Currency")]
        [Tooltip("Header title text.")]
        public TextMeshProUGUI titleText;
        [Tooltip("Displays current credit / cinder balance.")]
        public TextMeshProUGUI balanceText;

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
        }

        private void Start()
        {
            CloudCharacterSaveManager.OnCreditsChanged += HandleCreditsChanged;
            CloudCharacterSaveManager.OnProfileLoaded += HandleProfileLoaded;
        }

        private void OnDestroy()
        {
            CloudCharacterSaveManager.OnCreditsChanged -= HandleCreditsChanged;
            CloudCharacterSaveManager.OnProfileLoaded -= HandleProfileLoaded;
        }

        private void OnEnable()
        {
            RefreshUI();
        }

        private void HandleCreditsChanged(int newBalance)
        {
            RefreshUI();
        }

        private void HandleProfileLoaded(PlayerProfileData profile)
        {
            RefreshUI();
        }

        public void OpenPanel()
        {
            if (heatModalWindow != null) heatModalWindow.OpenWindow();
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshUI();
        }

        public void ClosePanel()
        {
            if (heatModalWindow != null) heatModalWindow.CloseWindow();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void RefreshUI()
        {
            int balance = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : 60;

            if (balanceText != null)
            {
                balanceText.text = CurrencyConfig.FormatBalance(balance);
            }

            if (titleText != null)
            {
                titleText.text = viewMode == UpgradeViewMode.InvestigatorStats
                    ? "INVESTIGATOR UPGRADES"
                    : "VENGEFUL SPIRIT UPGRADES";
            }

            if (manualStatItems != null && manualStatItems.Count > 0)
            {
                RefreshManualStatItems(balance);
            }
        }

        private void RefreshManualStatItems(int currentBalance)
        {
            foreach (var item in manualStatItems)
            {
                if (item == null) continue;

                int currentLevel = CloudCharacterSaveManager.Instance != null
                    ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(item.statType)
                    : 0;

                bool isMaxLevel = currentLevel >= 5;
                int cost = isMaxLevel ? 0 : UpgradeStatFormulas.CalculateUpgradeCost(item.statType, currentLevel);
                bool canAfford = !isMaxLevel && (currentBalance >= cost);

                string statTitle = UpgradeStatFormulas.GetStatDisplayName(item.statType);
                string statEffect = UpgradeStatFormulas.GetStatEffectDescription(item.statType, currentLevel);

                if (item.levelText != null)
                {
                    item.levelText.text = isMaxLevel ? "MAX" : $"Lv. {currentLevel} / 5";
                }

                if (item.effectText != null)
                {
                    item.effectText.text = isMaxLevel ? "Max Level Reached" : $"{statEffect}\n<color=#F1C40F>Cost: {cost} {CurrencyConfig.CurrencySymbol}</color>";
                }

                float progressFraction = currentLevel / 5f;
                if (item.progressBar != null)
                {
                    MichskyUIBridge.SetProgress(item.progressBar, progressFraction);
                }
                if (item.sliderBar != null)
                {
                    MichskyUIBridge.SetProgress(item.sliderBar, progressFraction);
                }
                if (item.fillImageBar != null)
                {
                    MichskyUIBridge.SetProgress(item.fillImageBar, progressFraction);
                }

                if (item.heatShopButton != null)
                {
                    item.heatShopButton.buttonTitle = statTitle;
                    item.heatShopButton.buttonDescription = isMaxLevel ? "Maximum rank achieved" : statEffect;
                    item.heatShopButton.priceText = isMaxLevel ? "MAX" : $"{cost}";
                    item.heatShopButton.isInteractable = canAfford;
                    item.heatShopButton.UpdateUI();

                    ButtonManager pBtn = item.purchaseButton != null ? item.purchaseButton : item.heatShopButton.purchaseButton;
                    GameObject pInd = item.purchasedIndicator != null ? item.purchasedIndicator : (item.heatShopButton.purchasedIndicator != null ? item.heatShopButton.purchasedIndicator.gameObject : null);

                    var capturedStat = item.statType;
                    var capturedCost = cost;

                    // Directly wire the child purchase button (this is what the user clicks!)
                    if (pBtn != null)
                    {
                        pBtn.isInteractable = canAfford;
                        pBtn.buttonText = isMaxLevel ? "MAX" : "Purchase";
                        pBtn.UpdateUI();

                        pBtn.onClick.RemoveAllListeners();
                        if (!isMaxLevel)
                        {
                            pBtn.onClick.AddListener(() => OnUpgradeClicked(capturedStat, capturedCost));
                        }

                        pBtn.gameObject.SetActive(!isMaxLevel);
                    }

                    if (pInd != null)
                    {
                        pInd.SetActive(isMaxLevel);
                    }

                    item.heatShopButton.onPurchaseClick.RemoveAllListeners();
                    item.heatShopButton.onClick.RemoveAllListeners();

                    if (!isMaxLevel)
                    {
                        item.heatShopButton.onPurchaseClick.AddListener(() => OnUpgradeClicked(capturedStat, capturedCost));
                        item.heatShopButton.onClick.AddListener(() => OnUpgradeClicked(capturedStat, capturedCost));
                    }
                }

                if (item.heatButton != null)
                {
                    item.heatButton.SetText($"{statTitle} {(isMaxLevel ? "[MAX]" : $"[Lv. {currentLevel}/5 - {cost}C]")}");
                    item.heatButton.isInteractable = canAfford;
                    item.heatButton.UpdateUI();
                    item.heatButton.onClick.RemoveAllListeners();
                    if (!isMaxLevel)
                    {
                        var capturedStat = item.statType;
                        var capturedCost = cost;
                        item.heatButton.onClick.AddListener(() => OnUpgradeClicked(capturedStat, capturedCost));
                    }
                }

                if (item.heatBoxButton != null)
                {
                    item.heatBoxButton.SetText($"{statTitle} {(isMaxLevel ? "[MAX]" : $"Lv.{currentLevel}")}");
                    item.heatBoxButton.isInteractable = canAfford;
                    item.heatBoxButton.UpdateUI();
                    item.heatBoxButton.onClick.RemoveAllListeners();
                    if (!isMaxLevel)
                    {
                        var capturedStat = item.statType;
                        var capturedCost = cost;
                        item.heatBoxButton.onClick.AddListener(() => OnUpgradeClicked(capturedStat, capturedCost));
                    }
                }
            }
        }

        private void OnUpgradeClicked(UpgradeStatType stat, int cost)
        {
            if (CloudCharacterSaveManager.Instance == null) return;
            int currentLevel = CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat);
            if (currentLevel >= 5) return;

            string statTitle = UpgradeStatFormulas.GetStatDisplayName(stat);
            string nextEffect = UpgradeStatFormulas.GetStatEffectDescription(stat, currentLevel + 1);

            // If a confirmation modal window is assigned, open it with details!
            if (purchaseConfirmModal != null)
            {
                purchaseConfirmModal.titleText = $"UPGRADE {statTitle.ToUpper()}";
                purchaseConfirmModal.descriptionText = $"Upgrade to Level {currentLevel + 1} for {cost} {CurrencyConfig.CurrencySymbol}?\n\n<b>Next Tier:</b> {nextEffect}";
                purchaseConfirmModal.UpdateUI();

                Action doPurchase = () =>
                {
                    if (CloudCharacterSaveManager.Instance != null && CloudCharacterSaveManager.Instance.TryPurchaseUpgrade(stat))
                    {
                        purchaseConfirmModal.CloseWindow();
                        RefreshUI();
                    }
                };

                purchaseConfirmModal.onConfirm.RemoveAllListeners();
                purchaseConfirmModal.onConfirm.AddListener(() => doPurchase());

                if (purchaseConfirmModal.confirmButton != null)
                {
                    purchaseConfirmModal.confirmButton.onClick.RemoveAllListeners();
                    purchaseConfirmModal.confirmButton.onClick.AddListener(() => doPurchase());
                }

                Action doCancel = () =>
                {
                    purchaseConfirmModal.CloseWindow();
                };

                purchaseConfirmModal.onCancel.RemoveAllListeners();
                purchaseConfirmModal.onCancel.AddListener(() => doCancel());

                if (purchaseConfirmModal.cancelButton != null)
                {
                    purchaseConfirmModal.cancelButton.onClick.RemoveAllListeners();
                    purchaseConfirmModal.cancelButton.onClick.AddListener(() => doCancel());
                }

                purchaseConfirmModal.OpenWindow();
                return;
            }

            // Direct purchase fallback if no modal assigned
            if (CloudCharacterSaveManager.Instance.TryPurchaseUpgrade(stat))
            {
                int newLevel = CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat);
                Debug.Log($"[LobbyUpgradeUI] Upgraded {stat} to Level {newLevel} for {cost} {CurrencyConfig.CurrencyName}!");
                RefreshUI();
            }
        }
    }
}
