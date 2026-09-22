using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;
using NightCrawler.Economy;

namespace NightCrawler.Monsters
{
    /// <summary>
    /// SOLID — SRP: Scrollable Card HUD for the Vengeful Spirit (Girl) to select
    /// and awaken monsters from the dead into the mine.
    /// Uses persistent upgrade stat 'DeadSummonCharges' for match capacity.
    /// </summary>
    public class GirlMonsterSummonHUD : MonoBehaviour
    {
        public static GirlMonsterSummonHUD Instance { get; private set; }

        [Header("Root & Panels")]
        public GameObject mainPanel;
        public CanvasGroup canvasGroup;

        [Header("UI Labels")]
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI chargesRemainingText;
        public TextMeshProUGUI monsterDescText;
        public TextMeshProUGUI dangerText;

        [Header("Scrollable Card Container")]
        [Tooltip("Parent transform with HorizontalLayoutGroup where monster cards are spawned.")]
        public Transform cardsContainer;
        public GameObject cardPrefab;

        [Header("Action Buttons")]
        public Button summonButton;
        public Button closeButton;

        [Header("Michsky Heat / Dark UI")]
        [Tooltip("Optional: Michsky ButtonManager to confirm summon.")]
        public Michsky.UI.Heat.ButtonManager heatSummonButton;

        [Tooltip("Optional: Michsky ButtonManager to close summon HUD.")]
        public Michsky.UI.Heat.ButtonManager heatCloseButton;

        [Tooltip("Optional: Michsky ModalWindowManager to animate the summon dialog.")]
        public Michsky.UI.Heat.ModalWindowManager heatModalWindow;

        [Header("Hotkeys")]
        [Tooltip("Hotkey to toggle this summon menu when playing as the Girl (default [X]).")]
        public Key toggleKey = Key.X;

        private int _totalCharges = 2;
        private int _remainingCharges = 2;
        private int _selectedMonsterIndex = 0;
        private bool _isOpen = false;
        private readonly List<Button> _cardButtons = new List<Button>();
        private readonly List<Image> _cardFrames = new List<Image>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            NightCrawler.UI.MichskyUIBridge.BindButton(summonButton, heatSummonButton, OnSummonClicked);
            NightCrawler.UI.MichskyUIBridge.BindButton(closeButton, heatCloseButton, CloseHUD);

            SetVisible(false);
        }

        private void Start()
        {
            InitializeSummonCharges();
            BuildMonsterCards();
        }

        private void InitializeSummonCharges()
        {
            int level = 0;
            if (CloudCharacterSaveManager.Instance != null)
            {
                level = CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DeadSummonCharges);
            }
            _totalCharges = UpgradeStatFormulas.GetGirlDeadSummonCharges(level);
            _remainingCharges = _totalCharges;
            UpdateChargesDisplay();
        }

        private void Update()
        {
            if (PauseManager.IsGamePaused)
            {
                if (_isOpen) CloseHUD();
                return;
            }

            if (GirlDealUI.IsAnyInputFocused()) return;

            if (Keyboard.current != null)
            {
                if (Keyboard.current[toggleKey].wasPressedThisFrame && IsLocalPlayerGirl())
                {
                    ToggleHUD();
                }
                else if (_isOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    CloseHUD();
                }
            }
        }

        private bool IsLocalPlayerGirl()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return false;
            var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj == null) return false;

            return playerObj.GetComponent<GirlPossession>() != null
                || playerObj.GetComponent<GirlStealth>() != null
                || playerObj.GetComponent<GirlMaterialController>() != null;
        }

        public void ToggleHUD()
        {
            _isOpen = !_isOpen;
            SetVisible(_isOpen);
        }

        public void CloseHUD()
        {
            _isOpen = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _isOpen = visible;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }

            if (mainPanel != null && mainPanel != gameObject)
            {
                mainPanel.SetActive(visible);
            }

            if (visible)
            {
                NightCrawler.UI.MichskyUIBridge.OpenModal(heatModalWindow);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                UpdateChargesDisplay();
                SelectCard(_selectedMonsterIndex);
            }
            else
            {
                NightCrawler.UI.MichskyUIBridge.CloseModal(heatModalWindow);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void UpdateChargesDisplay()
        {
            if (chargesRemainingText != null)
            {
                chargesRemainingText.text = $"Risen Summons Left: {_remainingCharges} / {_totalCharges}";
            }

            if (summonButton != null)
            {
                summonButton.interactable = (_remainingCharges > 0);
            }
        }

        private void BuildMonsterCards()
        {
            if (cardsContainer == null) return;

            foreach (Transform child in cardsContainer)
            {
                Destroy(child.gameObject);
            }

            _cardButtons.Clear();
            _cardFrames.Clear();

            List<MonsterDefinitionSO> monsters = DeadSpawnManager.Instance != null ? DeadSpawnManager.Instance.availableMonsters : null;

            int count = (monsters != null && monsters.Count > 0) ? monsters.Count : 3;

            for (int i = 0; i < count; i++)
            {
                int index = i;
                string mName = (monsters != null && i < monsters.Count && monsters[i] != null) ? monsters[i].monsterName : $"Monster Type {i + 1}";
                Sprite mIcon = (monsters != null && i < monsters.Count && monsters[i] != null) ? monsters[i].icon : null;

                GameObject card = null;
                if (cardPrefab != null)
                {
                    card = Instantiate(cardPrefab, cardsContainer);
                }
                else
                {
                    // Procedural card fallback
                    card = CreateProceduralCard(mName, mIcon);
                    card.transform.SetParent(cardsContainer, false);
                }

                card.name = $"MonsterCard_{mName}";

                Button btn = card.GetComponent<Button>() ?? card.GetComponentInChildren<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => SelectCard(index));
                    _cardButtons.Add(btn);
                    _cardFrames.Add(btn.GetComponent<Image>());
                }
            }

            SelectCard(0);
        }

        private GameObject CreateProceduralCard(string name, Sprite icon)
        {
            GameObject cardObj = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = cardObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(140, 180);

            var img = cardObj.GetComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);

            var vGroup = cardObj.AddComponent<VerticalLayoutGroup>();
            vGroup.childAlignment = TextAnchor.MiddleCenter;
            vGroup.padding = new RectOffset(10, 10, 12, 12);
            vGroup.spacing = 8;
            vGroup.childControlWidth = true;
            vGroup.childControlHeight = true;

            // Icon
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(cardObj.transform, false);
            var iconImg = iconObj.GetComponent<Image>();
            if (icon != null) iconImg.sprite = icon;
            iconImg.color = icon != null ? Color.white : new Color(0.8f, 0.2f, 0.2f, 0.8f);

            // Title
            GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObj.transform.SetParent(cardObj.transform, false);
            var tmp = labelObj.GetComponent<TextMeshProUGUI>();
            tmp.text = name;
            tmp.fontSize = 13;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return cardObj;
        }

        public void SelectCard(int index)
        {
            _selectedMonsterIndex = index;

            List<MonsterDefinitionSO> monsters = DeadSpawnManager.Instance != null ? DeadSpawnManager.Instance.availableMonsters : null;

            if (monsters != null && index >= 0 && index < monsters.Count && monsters[index] != null)
            {
                var def = monsters[index];
                if (monsterDescText != null) monsterDescText.text = def.description;
                if (dangerText != null) dangerText.text = $"Danger: {def.dangerRating}/5";
            }
            else
            {
                if (monsterDescText != null) monsterDescText.text = "A terrifying subterranean abomination that hunts the investigators.";
                if (dangerText != null) dangerText.text = "Danger: 3/5";
            }

            // Update card highlights
            for (int i = 0; i < _cardFrames.Count; i++)
            {
                if (_cardFrames[i] != null)
                {
                    _cardFrames[i].color = (i == _selectedMonsterIndex)
                        ? new Color(0.9f, 0.2f, 0.2f, 1f) // Crimson glow for selected
                        : new Color(0.2f, 0.2f, 0.25f, 0.9f);
                }
            }

            UpdateChargesDisplay();
        }

        private void OnSummonClicked()
        {
            if (_remainingCharges <= 0)
            {
                if (NotificationManager.Instance != null)
                    NotificationManager.Instance.ShowNotification("No risen summon charges remaining this match!", 2.5f);
                return;
            }

            _remainingCharges--;
            UpdateChargesDisplay();

            if (DeadSpawnManager.Instance != null && NetworkManager.Singleton != null)
            {
                DeadSpawnManager.Instance.RequestSpawnMonsterServerRpc(_selectedMonsterIndex, NetworkManager.Singleton.LocalClientId);
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("Awakening monster in the deep tunnels...", 3f);
            }

            CloseHUD();
        }
    }
}
