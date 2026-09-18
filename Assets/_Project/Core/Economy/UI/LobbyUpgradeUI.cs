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
    /// SOLID — SRP: Manages the Persistent Upgrades shop in the Lobby Scene.
    /// Used on both the Character Selection Screen (Investigator upgrades)
    /// and the Girl Player Screen (Vengeful Spirit upgrades).
    /// </summary>
    public class LobbyUpgradeUI : MonoBehaviour
    {
        [Header("1. Mode & Root Window")]
        [Tooltip("Select whether this panel upgrades Investigator stats or Girl stats.")]
        public UpgradeViewMode viewMode = UpgradeViewMode.InvestigatorStats;

        [Tooltip("The root modal panel GameObject (usually this GameObject).")]
        public GameObject panelRoot;

        [Tooltip("Michsky Heat Modal Window Manager on this window.")]
        public ModalWindowManager heatModalWindow;

        [Header("2. Window Triggers (Optional)")]
        [Tooltip("Michsky Heat Button to open this panel.")]
        public ButtonManager heatOpenButton;
        [Tooltip("Michsky Heat Box Button to open this panel.")]
        public BoxButtonManager heatBoxOpenButton;
        [Tooltip("Michsky Heat Button to close this panel.")]
        public ButtonManager heatCloseButton;
        [Tooltip("Michsky Heat Box Button to close this panel.")]
        public BoxButtonManager heatBoxCloseButton;

        [Header("3. Confirmation Modal (Michsky Heat UI)")]
        [Tooltip("The Modal Window that pops up when tapping a stat to confirm the purchase.")]
        public ModalWindowManager purchaseConfirmModal;

        [System.Serializable]
        public class ManualStatItem
        {
            [Tooltip("The stat this card upgrades.")]
            public UpgradeStatType statType;
            [Tooltip("The Heat Shop Button card for this stat.")]
            public ShopButtonManager heatShopButton;
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

            // Hidden legacy fields to keep inspector clean
            [HideInInspector] public Button button;
            [HideInInspector] public GameObject buttonObject;
        }

        [Header("4. Stat Cards (Drag & Drop Hierarchy)")]
        [Tooltip("List of stat cards manually placed in the hierarchy.")]
        public List<ManualStatItem> manualStatItems = new List<ManualStatItem>();

        [Header("5. Displays & Currency (Optional)")]
        [Tooltip("Header title text.")]
        public TextMeshProUGUI titleText;
        [Tooltip("Displays current credit / cinder balance.")]
        public TextMeshProUGUI balanceText;

        // Hidden legacy fields to prevent inspector clutter while preserving backwards compatibility
        [HideInInspector] public Button openButton;
        [HideInInspector] public ShopButtonManager heatShopOpenButton;
        [HideInInspector] public GameObject heatOpenButtonObject;
        [HideInInspector] public Button closeButton;
        [HideInInspector] public GameObject heatCloseButtonObject;
        [HideInInspector] public Transform itemsContainer;
        [HideInInspector] public GameObject upgradeRowPrefab;

        private readonly List<UpgradeStatType> _investigatorStats = new List<UpgradeStatType>
        {
            UpgradeStatType.DamageResistance,
            UpgradeStatType.WeaponDamage,
            UpgradeStatType.MaskFilter,
            UpgradeStatType.SpiritualLevel,
            UpgradeStatType.MapPower,
            UpgradeStatType.VialCount,
            UpgradeStatType.VialHealingPower
        };

        private readonly List<UpgradeStatType> _girlStats = new List<UpgradeStatType>
        {
            UpgradeStatType.PossessionDuration,
            UpgradeStatType.DealCapacity,
            UpgradeStatType.VisibilityCount,
            UpgradeStatType.VisibilityDuration,
            UpgradeStatType.DeadSummonCharges
        };

        private void Awake()
        {
            MichskyUIBridge.BindAnyButton(OpenPanel, openButton, heatOpenButton, heatShopOpenButton, heatBoxOpenButton, heatOpenButtonObject);
            MichskyUIBridge.BindAnyButton(ClosePanel, closeButton, heatCloseButton, heatBoxCloseButton, heatCloseButtonObject);

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
                balanceText.text = $"Credits: {balance} {CurrencyConfig.CurrencySymbol}";
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
            else
            {
                BuildStatRows(balance);
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
                    item.heatShopButton.buttonDescription = statEffect;
                    item.heatShopButton.priceText = isMaxLevel ? "MAX" : $"{cost}";
                    item.heatShopButton.isInteractable = canAfford;
                    item.heatShopButton.UpdateUI();
                }

                if (item.heatButton != null)
                {
                    item.heatButton.SetText($"{statTitle} {(isMaxLevel ? "[MAX]" : $"[Lv. {currentLevel}/5 - {cost}C]")}");
                    item.heatButton.isInteractable = canAfford;
                    item.heatButton.UpdateUI();
                }

                if (item.heatBoxButton != null)
                {
                    item.heatBoxButton.SetText($"{statTitle} {(isMaxLevel ? "[MAX]" : $"Lv.{currentLevel}")}");
                    item.heatBoxButton.isInteractable = canAfford;
                    item.heatBoxButton.UpdateUI();
                }

                MichskyUIBridge.SetAnyButtonInteractable(canAfford, item.button, item.heatButton, item.heatBoxButton, item.buttonObject);

                // Bind click to open confirmation modal
                MichskyUIBridge.BindAnyButton(() =>
                {
                    OnUpgradeClicked(item.statType, cost);
                }, item.button, item.heatButton, item.heatBoxButton, item.heatShopButton, item.buttonObject);
            }
        }

        private void BuildStatRows(int currentBalance)
        {
            if (itemsContainer == null) return;

            // Clear old children
            foreach (Transform child in itemsContainer)
            {
                Destroy(child.gameObject);
            }

            List<UpgradeStatType> statsToDisplay = viewMode == UpgradeViewMode.InvestigatorStats
                ? _investigatorStats
                : _girlStats;

            foreach (var stat in statsToDisplay)
            {
                CreateUpgradeRow(stat, currentBalance);
            }
        }

        private void CreateUpgradeRow(UpgradeStatType stat, int currentBalance)
        {
            int currentLevel = CloudCharacterSaveManager.Instance != null
                ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat)
                : 0;

            bool isMaxLevel = currentLevel >= 5;
            int cost = isMaxLevel ? 0 : UpgradeStatFormulas.CalculateUpgradeCost(stat, currentLevel);
            bool canAfford = !isMaxLevel && (currentBalance >= cost);

            string statTitle = UpgradeStatFormulas.GetStatDisplayName(stat);
            string statEffect = UpgradeStatFormulas.GetStatEffectDescription(stat, currentLevel);

            // Create row container
            GameObject rowObj = new GameObject($"UpgradeRow_{stat}", typeof(RectTransform), typeof(Image));
            rowObj.transform.SetParent(itemsContainer, false);

            var rowRect = rowObj.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 56);

            var rowImg = rowObj.GetComponent<Image>();
            rowImg.color = new Color(0.12f, 0.12f, 0.15f, 0.95f);

            var hGroup = rowObj.AddComponent<HorizontalLayoutGroup>();
            hGroup.childForceExpandWidth = false;
            hGroup.childForceExpandHeight = true;
            hGroup.childControlWidth = true;
            hGroup.childControlHeight = true;
            hGroup.spacing = 10;
            hGroup.padding = new RectOffset(15, 15, 6, 6);

            // Left text block (Name + Level + Effect)
            GameObject textObj = new GameObject("TextInfo", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(rowObj.transform, false);
            var layoutElement = textObj.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;

            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;
            tmp.richText = true;

            string levelBadge = isMaxLevel ? "<color=#F1C40F>[MAX]</color>" : $"<color=#3498DB>[Lv. {currentLevel}/5]</color>";
            tmp.text = $"<b>{statTitle}</b> {levelBadge}\n<size=11><color=#BDC3C7>{statEffect}</color></size>";

            // Upgrade Button
            GameObject btnObj = new GameObject("Btn_Upgrade", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(rowObj.transform, false);

            var btnLayout = btnObj.AddComponent<LayoutElement>();
            btnLayout.preferredWidth = 130;

            var btnImg = btnObj.GetComponent<Image>();
            btnImg.color = isMaxLevel ? new Color(0.3f, 0.3f, 0.3f) : (canAfford ? new Color(0.18f, 0.80f, 0.44f) : new Color(0.7f, 0.2f, 0.2f));

            var btn = btnObj.GetComponent<Button>();
            btn.interactable = canAfford;

            // Button label
            GameObject btnLabelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnLabelObj.transform.SetParent(btnObj.transform, false);
            var labelRect = btnLabelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;

            var btnTmp = btnLabelObj.GetComponent<TextMeshProUGUI>();
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.fontSize = 13;
            btnTmp.fontStyle = FontStyles.Bold;
            btnTmp.color = Color.white;

            if (isMaxLevel)
            {
                btnTmp.text = "MAXED";
            }
            else
            {
                btnTmp.text = $"UPGRADE ({cost} {CurrencyConfig.CurrencySymbol})";
            }

            btn.onClick.AddListener(() =>
            {
                OnUpgradeClicked(stat, cost);
            });
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

                purchaseConfirmModal.onConfirm.RemoveAllListeners();
                purchaseConfirmModal.onConfirm.AddListener(() =>
                {
                    if (CloudCharacterSaveManager.Instance.TryPurchaseUpgrade(stat))
                    {
                        purchaseConfirmModal.CloseWindow();
                        RefreshUI();
                    }
                });

                purchaseConfirmModal.onCancel.RemoveAllListeners();
                purchaseConfirmModal.onCancel.AddListener(() =>
                {
                    purchaseConfirmModal.CloseWindow();
                });

                purchaseConfirmModal.OpenWindow();
                return;
            }

            // Direct purchase fallback
            if (CloudCharacterSaveManager.Instance.TryPurchaseUpgrade(stat))
            {
                int newLevel = CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat);
                Debug.Log($"[LobbyUpgradeUI] Upgraded {stat} to Level {newLevel} for {cost} {CurrencyConfig.CurrencyName}!");
                RefreshUI();
            }
        }
    }
}
