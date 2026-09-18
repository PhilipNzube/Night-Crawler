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
        [Header("Configuration")]
        [Tooltip("Select whether this panel upgrades Investigator stats or Girl stats.")]
        public UpgradeViewMode viewMode = UpgradeViewMode.InvestigatorStats;

        [Header("Root & Controls")]
        [Tooltip("The root modal panel GameObject. Can be toggled open/closed.")]
        public GameObject panelRoot;

        [Tooltip("Michsky Heat / Dark Modal Window Manager")]
        public ModalWindowManager heatModalWindow;

        [Tooltip("Button used to open this upgrade panel.")]
        public Button openButton;
        [Tooltip("Michsky Heat / Dark Button to open this panel.")]
        public ButtonManager heatOpenButton;

        [Tooltip("Button used to close this upgrade panel.")]
        public Button closeButton;
        [Tooltip("Michsky Heat / Dark Button to close this panel.")]
        public ButtonManager heatCloseButton;

        [Tooltip("Header title text.")]
        public TextMeshProUGUI titleText;

        [Tooltip("Displays current credit balance.")]
        public TextMeshProUGUI balanceText;

        [Header("Item Container")]
        [Tooltip("Vertical container where upgrade rows are placed.")]
        public Transform itemsContainer;

        [Tooltip("Optional custom prefab for upgrade rows. If null, procedural cards are generated.")]
        public GameObject upgradeRowPrefab;

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
            MichskyUIBridge.BindButton(openButton, heatOpenButton, OpenPanel);
            MichskyUIBridge.BindButton(closeButton, heatCloseButton, ClosePanel);

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

            BuildStatRows(balance);
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

            if (CloudCharacterSaveManager.Instance.TryPurchaseUpgrade(stat))
            {
                int currentLevel = CloudCharacterSaveManager.Instance.GetUpgradeLevel(stat);
                Debug.Log($"[LobbyUpgradeUI] Upgraded {stat} to Level {currentLevel} for {cost} {CurrencyConfig.CurrencyName}!");
                RefreshUI();
            }
        }
    }
}
