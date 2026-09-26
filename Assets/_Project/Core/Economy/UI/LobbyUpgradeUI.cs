using System;
using System.Collections.Generic;
using UnityEngine;
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
    /// Cleaned: All useless/legacy inspector fields and alternative buttons removed.
    /// </summary>
    public class LobbyUpgradeUI : MonoBehaviour
    {
        [Header("1. Mode and Root Window")]
        [Tooltip("Select whether this panel upgrades Investigator stats or Girl stats.")]
        public UpgradeViewMode viewMode = UpgradeViewMode.InvestigatorStats;

        [Tooltip("The root panel GameObject (defaults to this GameObject).")]
        public GameObject panelRoot;

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

            [Tooltip("Heat UI ProgressBar visualizing tier level.")]
            public ProgressBar progressBar;
        }

        [Header("3. Stat Cards (Manual Wiring)")]
        [Tooltip("List of stat cards manually assigned in the hierarchy.")]
        public List<ManualStatItem> manualStatItems = new List<ManualStatItem>();

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
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshUI();
        }

        public void ClosePanel()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void RefreshUI()
        {
            int balance = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : 60;

            if (manualStatItems != null && manualStatItems.Count > 0)
            {
                RefreshManualStatItems(balance);
            }

            LobbyUI.Instance?.UpdateProfileUI();
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

                float progressFraction = currentLevel / 5f;
                if (item.progressBar != null)
                {
                    MichskyUIBridge.SetProgress(item.progressBar, progressFraction);
                }

                if (item.heatShopButton != null)
                {
                    item.heatShopButton.buttonTitle = statTitle;
                    item.heatShopButton.buttonDescription = isMaxLevel ? "Maximum rank achieved" : statEffect;
                    item.heatShopButton.priceText = isMaxLevel ? "MAX" : $"{cost}";
                    item.heatShopButton.isInteractable = !isMaxLevel;
                    item.heatShopButton.UpdateUI();

                    ButtonManager pBtn = item.heatShopButton.purchaseButton;
                    GameObject pInd = item.heatShopButton.purchasedIndicator != null ? item.heatShopButton.purchasedIndicator.gameObject : null;

                    var capturedStat = item.statType;
                    var capturedCost = cost;

                    // Directly wire the child purchase button
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
            }
        }

        private void OnUpgradeClicked(UpgradeStatType stat, int cost)
        {
            if (CloudCharacterSaveManager.Instance == null) return;
            int currentLevel = CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat);
            if (currentLevel >= 5) return;

            string statTitle = UpgradeStatFormulas.GetStatDisplayName(stat);
            string nextEffect = UpgradeStatFormulas.GetStatEffectDescription(stat, currentLevel + 1);

            int balance = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : 0;
            bool canAfford = balance >= cost;

            // If a confirmation modal window is assigned, open it with details!
            if (purchaseConfirmModal != null)
            {
                purchaseConfirmModal.titleText = $"UPGRADE {statTitle.ToUpper()}";
                purchaseConfirmModal.descriptionText = canAfford
                    ? $"Upgrade to Level {currentLevel + 1} for {cost} {CurrencyConfig.CurrencySymbol}?\n\n<b>Next Tier:</b> {nextEffect}"
                    : $"Requires {cost} {CurrencyConfig.CurrencySymbol} (You have {balance} {CurrencyConfig.CurrencySymbol}).\n\n<b>Next Tier:</b> {nextEffect}";
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
                if (canAfford)
                {
                    purchaseConfirmModal.onConfirm.AddListener(() => doPurchase());
                }

                if (purchaseConfirmModal.confirmButton != null)
                {
                    purchaseConfirmModal.confirmButton.isInteractable = canAfford;
                    purchaseConfirmModal.confirmButton.buttonText = canAfford ? "Confirm" : "Not Enough";
                    purchaseConfirmModal.confirmButton.UpdateUI();
                    purchaseConfirmModal.confirmButton.onClick.RemoveAllListeners();
                    if (canAfford)
                    {
                        purchaseConfirmModal.confirmButton.onClick.AddListener(() => doPurchase());
                    }
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
