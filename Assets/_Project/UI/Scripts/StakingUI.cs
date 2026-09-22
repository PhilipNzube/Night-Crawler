using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NightCrawler.Economy;
using Michsky.UI.Heat;

namespace NightCrawler.UI
{
    /// <summary>
    /// SOLID — SRP: Staking UI modal shown at match start.
    /// Allows players to select and submit their credit stake into the pot.
    /// Enforces minimum stake (2 credits) and hard cap (max 60% balance, no all-in).
    /// Includes runtime auto-creation fallback if not baked into the scene canvas!
    /// </summary>
    public class StakingUI : MonoBehaviour
    {
        public static StakingUI Instance { get; private set; }

        [Header("UI Panels & Controls")]
        public GameObject stakingModalPanel;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI balanceText;
        public TextMeshProUGUI stakeValueText;
        public TextMeshProUGUI countdownText;
        public Slider stakeSlider;
        public Button confirmStakeButton;
        public TextMeshProUGUI confirmButtonText;

        [Header("Michsky Heat / Dark UI")]
        public ModalWindowManager heatStakingModal;
        public SliderManager heatStakeSlider;
        public ButtonManager heatConfirmStakeButton;
        public BoxButtonManager heatBoxConfirmStakeButton;
        public GameObject heatConfirmStakeButtonObject;

        private bool _hasConfirmed = false;
        private int _selectedStake = CurrencyConfig.MinimumStake;
        private int _maxAllowedStake = CurrencyConfig.MinimumStake;
        private int _currentBalance = 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureUIRuntime();
        }

        private void Start()
        {
            if (stakeSlider != null)
            {
                stakeSlider.onValueChanged.AddListener(OnSliderChanged);
            }

            if (heatStakeSlider != null)
            {
                heatStakeSlider.onValueChanged.AddListener(OnSliderChanged);
            }

            MichskyUIBridge.BindAnyButton(OnConfirmClicked, confirmStakeButton, heatConfirmStakeButton, heatBoxConfirmStakeButton, heatConfirmStakeButtonObject);

            // Hide initially until staking opens
            if (stakingModalPanel != null)
            {
                stakingModalPanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (MatchEconomyManager.Instance == null) return;

            // Check if staking opened
            if (MatchEconomyManager.Instance.isStakingActive.Value)
            {
                if (stakingModalPanel != null && !stakingModalPanel.activeSelf && !_hasConfirmed)
                {
                    OpenStakingModal();
                }

                if (countdownText != null)
                {
                    float remaining = MatchEconomyManager.Instance.stakingTimeRemaining.Value;
                    countdownText.text = $"Staking closes in: {remaining:0.0}s";
                }
            }
            else
            {
                // Staking closed
                if (stakingModalPanel != null && stakingModalPanel.activeSelf)
                {
                    stakingModalPanel.SetActive(false);
                }
            }
        }

        public void OpenStakingModal()
        {
            if (stakingModalPanel != null) stakingModalPanel.SetActive(true);
            if (heatStakingModal != null) heatStakingModal.OpenWindow();

            _currentBalance = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.CurrentCredits : 50;

            // Enforce minimum stake and max cap (60% balance)
            int minStake = CurrencyConfig.MinimumStake;
            _maxAllowedStake = Mathf.Max(minStake, Mathf.FloorToInt(_currentBalance * CurrencyConfig.MaxStakeCapPercentage));

            MichskyUIBridge.SetSliderLimits(stakeSlider, heatStakeSlider, minStake, _maxAllowedStake, true);
            MichskyUIBridge.SetSliderValue(stakeSlider, heatStakeSlider, minStake);

            _selectedStake = minStake;
            UpdateDisplay();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnSliderChanged(float value)
        {
            _selectedStake = Mathf.RoundToInt(value);
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (balanceText != null)
            {
                balanceText.text = $"Balance: {_currentBalance} {CurrencyConfig.CurrencySymbol}";
            }

            if (stakeValueText != null)
            {
                stakeValueText.text = $"{_selectedStake} {CurrencyConfig.CurrencySymbol}";
            }

            string btnText = $"CONFIRM STAKE ({_selectedStake} {CurrencyConfig.CurrencySymbol})";
            if (confirmButtonText != null)
            {
                confirmButtonText.text = btnText;
            }
            MichskyUIBridge.SetAnyButtonText(btnText, confirmStakeButton, heatConfirmStakeButton, heatBoxConfirmStakeButton, heatConfirmStakeButtonObject);
        }

        private void OnConfirmClicked()
        {
            if (_hasConfirmed) return;
            _hasConfirmed = true;

            if (MatchEconomyManager.Instance != null)
            {
                MatchEconomyManager.Instance.SubmitStakeServerRpc(_selectedStake);
            }

            MichskyUIBridge.SetAnyButtonInteractable(false, confirmStakeButton, heatConfirmStakeButton, heatBoxConfirmStakeButton, heatConfirmStakeButtonObject);
            if (stakeSlider != null) stakeSlider.interactable = false;

            if (confirmButtonText != null)
            {
                confirmButtonText.text = "STAKE LOCKED";
            }
            MichskyUIBridge.SetAnyButtonText("STAKE LOCKED", confirmStakeButton, heatConfirmStakeButton, heatBoxConfirmStakeButton, heatConfirmStakeButtonObject);

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification($"Staked {_selectedStake} {CurrencyConfig.CurrencyName} into the match pot.", 2.5f);
            }

            StartCoroutine(CloseAfterDelay(1.5f));
        }

        private IEnumerator CloseAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (heatStakingModal != null) heatStakingModal.CloseWindow();
            if (stakingModalPanel != null) stakingModalPanel.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Creates a clean procedural UI panel if none was manually assigned in the Inspector.
        /// </summary>
        private void EnsureUIRuntime()
        {
            if (stakingModalPanel != null) return;

            Canvas targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>();
            }

            if (targetCanvas == null) return;

            // Build root panel
            var panelObj = new GameObject("StakingModalPanel", typeof(RectTransform), typeof(Image));
            panelObj.transform.SetParent(targetCanvas.transform, false);

            var rt = panelObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(480, 320);

            var img = panelObj.GetComponent<Image>();
            img.color = new Color(0.06f, 0.07f, 0.09f, 0.95f);

            // Title
            var titleObj = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObj.transform.SetParent(panelObj.transform, false);
            var titleRt = titleObj.GetComponent<RectTransform>();
            titleRt.anchoredPosition = new Vector2(0, 115);
            titleRt.sizeDelta = new Vector2(440, 45);
            titleText = titleObj.GetComponent<TextMeshProUGUI>();
            titleText.text = "MATCH STAKE POT";
            titleText.fontSize = 24;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontStyle = FontStyles.Bold;
            titleText.color = new Color(1f, 0.78f, 0.25f, 1f);

            // Balance
            var balObj = new GameObject("BalanceText", typeof(RectTransform), typeof(TextMeshProUGUI));
            balObj.transform.SetParent(panelObj.transform, false);
            var balRt = balObj.GetComponent<RectTransform>();
            balRt.anchoredPosition = new Vector2(0, 70);
            balRt.sizeDelta = new Vector2(440, 30);
            balanceText = balObj.GetComponent<TextMeshProUGUI>();
            balanceText.fontSize = 16;
            balanceText.alignment = TextAlignmentOptions.Center;
            balanceText.color = new Color(0.8f, 0.85f, 0.9f, 1f);

            // Stake Value
            var valObj = new GameObject("StakeValueText", typeof(RectTransform), typeof(TextMeshProUGUI));
            valObj.transform.SetParent(panelObj.transform, false);
            var valRt = valObj.GetComponent<RectTransform>();
            valRt.anchoredPosition = new Vector2(0, 15);
            valRt.sizeDelta = new Vector2(440, 45);
            stakeValueText = valObj.GetComponent<TextMeshProUGUI>();
            stakeValueText.fontSize = 32;
            stakeValueText.alignment = TextAlignmentOptions.Center;
            stakeValueText.fontStyle = FontStyles.Bold;
            stakeValueText.color = new Color(0.95f, 0.45f, 0.15f, 1f);

            // Countdown
            var cdObj = new GameObject("CountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
            cdObj.transform.SetParent(panelObj.transform, false);
            var cdRt = cdObj.GetComponent<RectTransform>();
            cdRt.anchoredPosition = new Vector2(0, -35);
            cdRt.sizeDelta = new Vector2(440, 25);
            countdownText = cdObj.GetComponent<TextMeshProUGUI>();
            countdownText.fontSize = 14;
            countdownText.alignment = TextAlignmentOptions.Center;
            countdownText.color = new Color(0.6f, 0.65f, 0.7f, 1f);

            // Confirm Button
            var btnObj = new GameObject("ConfirmStakeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnObj.transform.SetParent(panelObj.transform, false);
            var btnRt = btnObj.GetComponent<RectTransform>();
            btnRt.anchoredPosition = new Vector2(0, -95);
            btnRt.sizeDelta = new Vector2(300, 48);

            var btnImg = btnObj.GetComponent<Image>();
            btnImg.color = new Color(0.85f, 0.35f, 0.1f, 1f);
            confirmStakeButton = btnObj.GetComponent<Button>();

            var btnTextObj = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnTextObj.transform.SetParent(btnObj.transform, false);
            var btnTextRt = btnTextObj.GetComponent<RectTransform>();
            btnTextRt.sizeDelta = btnRt.sizeDelta;
            confirmButtonText = btnTextObj.GetComponent<TextMeshProUGUI>();
            confirmButtonText.text = "CONFIRM STAKE";
            confirmButtonText.fontSize = 16;
            confirmButtonText.fontStyle = FontStyles.Bold;
            confirmButtonText.alignment = TextAlignmentOptions.Center;
            confirmButtonText.color = Color.white;

            stakingModalPanel = panelObj;
            stakingModalPanel.SetActive(false);
        }
    }
}
