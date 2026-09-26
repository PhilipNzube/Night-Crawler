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
        public CanvasGroup canvasGroup;

        [System.Serializable]
        public class MonsterCardBinding
        {
            public MonsterDefinitionSO monsterDefinition;
            public Michsky.UI.Heat.ShopButtonManager heatShopCard;
            [Tooltip("Heat UI ButtonManager for selecting this monster (e.g. Select button on the card).")]
            public Michsky.UI.Heat.ButtonManager selectButtonManager;
        }

        [Header("Monster Card Bindings")]
        [Tooltip("Pre-placed or dynamically bound monster cards in the Heat UI list.")]
        public List<MonsterCardBinding> cardBindings = new List<MonsterCardBinding>();

        [Header("Selection Confirmation Modal")]
        [Tooltip("The SelectionConfirmationModal script attached to SelectionConfirmationModal GameObject (recommended).")]
        public SelectionConfirmationModal selectionConfirmationModal;

        [Tooltip("The Michsky Modal Window Manager for the confirmation dialog.")]
        public Michsky.UI.Heat.ModalWindowManager confirmationModal;

        [Tooltip("Confirm button inside the confirmation modal.")]
        public Michsky.UI.Heat.ButtonManager confirmSpawnButton;

        [Tooltip("Cancel button inside the confirmation modal.")]
        public Michsky.UI.Heat.ButtonManager cancelSpawnButton;

        [Header("Monster Spawn Camera Tracking")]
        [Tooltip("Virtual camera or Cinemachine camera used to focus on newly spawned monster.")]
        public Unity.Cinemachine.CinemachineVirtualCameraBase monsterSpawnVirtualCamera;

        [Tooltip("How long the camera focuses on the monster before returning priority (seconds).")]
        public float cameraFocusDuration = 3.5f;

        [Header("Monster Spectate Hotkey (Manual Inspector Wiring)")]
        [Tooltip("Root GameObject for the MonsterSpectateHotkey.")]
        public GameObject monsterSpectateHotkey;
        [Tooltip("Michsky Heat HotkeyEvent component on MonsterSpectateHotkey.")]
        public Michsky.UI.Heat.HotkeyEvent monsterSpectateHotkeyEvent;

        [Tooltip("Dead Summon Charges upgrade level required to unlock Berserker (default Level 3).")]
        public int berserkerUnlockLevel = 3;

        [Header("Hotkeys")]
        [Tooltip("Hotkey to toggle this summon menu when playing as the Girl (default [X]).")]
        public Key toggleKey = Key.X;

        private int _totalCharges = 2;
        private int _remainingCharges = 2;
        private int _undeadSummoned = 0;
        private int _berserkerSummoned = 0;
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

            if (selectionConfirmationModal == null && confirmationModal != null)
            {
                selectionConfirmationModal = confirmationModal.GetComponent<SelectionConfirmationModal>();
            }

            // Wire Confirmation Modal buttons
            var confirmBtn = selectionConfirmationModal != null && selectionConfirmationModal.confirmButton != null 
                ? selectionConfirmationModal.confirmButton 
                : confirmSpawnButton;
            if (confirmBtn != null)
            {
                confirmBtn.onClick.RemoveListener(OnConfirmSpawnClicked);
                confirmBtn.onClick.AddListener(OnConfirmSpawnClicked);
            }

            var cancelBtn = selectionConfirmationModal != null && selectionConfirmationModal.cancelButton != null 
                ? selectionConfirmationModal.cancelButton 
                : cancelSpawnButton;
            if (cancelBtn != null)
            {
                cancelBtn.onClick.RemoveListener(OnCancelSpawnClicked);
                cancelBtn.onClick.AddListener(OnCancelSpawnClicked);
            }

            // Wire MonsterSpectateHotkey if assigned in inspector
            if (monsterSpectateHotkeyEvent != null)
            {
                monsterSpectateHotkeyEvent.onHotkeyPress.RemoveListener(OnSpectateMonstersClicked);
                monsterSpectateHotkeyEvent.onHotkeyPress.AddListener(OnSpectateMonstersClicked);
            }

            SetVisible(false);
        }

        private void Start()
        {
            InitializeSummonCharges();
            InitCardBindings();
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

            if (visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                UpdateChargesDisplay();
                SelectCard(_selectedMonsterIndex);
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void UpdateChargesDisplay()
        {
            int summonLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DeadSummonCharges) : 0;
            int maxUndead = 2 + summonLvl;
            int remainingUndead = Mathf.Max(0, maxUndead - _undeadSummoned);

            int maxBerserker = summonLvl >= berserkerUnlockLevel ? 1 + ((summonLvl - berserkerUnlockLevel) / 2) : 0;
            int remainingBerserker = Mathf.Max(0, maxBerserker - _berserkerSummoned);

            _remainingCharges = remainingUndead + remainingBerserker;
            _totalCharges = maxUndead + maxBerserker;

            // Sync MonsterSpectateHotkey visibility with whether active monsters exist in scene
            bool hasMonsters = SpectatorController.HasActiveMonstersInScene();
            if (monsterSpectateHotkey != null)
            {
                monsterSpectateHotkey.SetActive(hasMonsters);
            }
        }

        private void InitCardBindings()
        {
            if (cardBindings == null || cardBindings.Count == 0) return;

            for (int i = 0; i < cardBindings.Count; i++)
            {
                int index = i;
                var binding = cardBindings[i];
                if (binding == null) continue;

                Sprite icon = binding.monsterDefinition != null ? binding.monsterDefinition.icon : null;
                string mName = binding.monsterDefinition != null ? binding.monsterDefinition.monsterName : "Monster";

                if (binding.heatShopCard != null)
                {
                    binding.heatShopCard.buttonTitle = mName;
                    if (icon != null)
                    {
                        binding.heatShopCard.buttonIcon = icon;
                        binding.heatShopCard.enableIcon = true;
                        if (binding.heatShopCard.iconObj != null)
                        {
                            binding.heatShopCard.iconObj.gameObject.SetActive(true);
                            binding.heatShopCard.iconObj.sprite = icon;
                        }
                    }
                    binding.heatShopCard.UpdateUI();

                    binding.heatShopCard.onClick.RemoveAllListeners();
                    binding.heatShopCard.onClick.AddListener(() => OnCardSelectClicked(index));
                }

                if (binding.selectButtonManager != null)
                {
                    if (icon != null)
                    {
                        binding.selectButtonManager.buttonIcon = icon;
                        binding.selectButtonManager.enableIcon = true;
                        binding.selectButtonManager.UpdateUI();
                    }

                    binding.selectButtonManager.onClick.RemoveAllListeners();
                    binding.selectButtonManager.onClick.AddListener(() => OnCardSelectClicked(index));
                }
            }
        }

        private void BuildMonsterCards()
        {
        }

        public void SelectCard(int index)
        {
            _selectedMonsterIndex = index;

            List<MonsterDefinitionSO> monsters = DeadSpawnManager.Instance != null ? DeadSpawnManager.Instance.availableMonsters : null;

            if (monsters != null && index >= 0 && index < monsters.Count && monsters[index] != null)
            {
                var def = monsters[index];
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

        public void OnSpectateMonstersClicked()
        {
            if (!SpectatorController.HasActiveMonstersInScene())
            {
                if (monsterSpectateHotkey != null) monsterSpectateHotkey.SetActive(false);
                if (NotificationManager.Instance != null)
                {
                    NotificationManager.Instance.ShowNotification("There are no active monsters left in the mine to spectate!", 3.5f);
                }
                return;
            }

            CloseHUD();

            var spec = FindFirstObjectByType<SpectatorController>();
            if (spec != null)
            {
                spec.TryOpenMonsterSpectator();
            }
        }

        public void OnCardSelectClicked(int index)
        {
            _selectedMonsterIndex = index;
            SelectCard(index);

            int summonLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DeadSummonCharges) : 0;
            bool isBerserker = IsSelectedMonsterBerserker(index);

            if (isBerserker)
            {
                bool isUnlocked = summonLvl >= berserkerUnlockLevel;
                if (!isUnlocked)
                {
                    if (NotificationManager.Instance != null)
                    {
                        NotificationManager.Instance.ShowNotification($"The Berserker is locked! Requires Dead Summon Charges Level {berserkerUnlockLevel}.", 3.5f);
                    }
                    return;
                }

                int maxBerserker = 1 + ((summonLvl - berserkerUnlockLevel) / 2);
                int remainingBerserker = Mathf.Max(0, maxBerserker - _berserkerSummoned);
                bool canSummon = remainingBerserker > 0;

                if (selectionConfirmationModal != null)
                {
                    selectionConfirmationModal.ShowBerserkerSlots(remainingBerserker, maxBerserker, canSummon);
                }
            }
            else
            {
                // Undead
                int maxUndead = 2 + summonLvl;
                int remainingUndead = Mathf.Max(0, maxUndead - _undeadSummoned);
                bool canSummon = remainingUndead > 0;

                if (selectionConfirmationModal != null)
                {
                    selectionConfirmationModal.ShowUndeadSlots(remainingUndead, maxUndead, canSummon);
                }
            }

            if (selectionConfirmationModal != null)
            {
                selectionConfirmationModal.OpenWindow();
            }
            else if (confirmationModal != null)
            {
                confirmationModal.OpenWindow();
            }
            else
            {
                OnConfirmSpawnClicked();
            }
        }

        private bool IsSelectedMonsterBerserker(int index)
        {
            if (cardBindings != null && index >= 0 && index < cardBindings.Count && cardBindings[index] != null)
            {
                var def = cardBindings[index].monsterDefinition;
                if (def != null && def.monsterName.ToLower().Contains("berserker")) return true;
            }
            List<MonsterDefinitionSO> monsters = DeadSpawnManager.Instance != null ? DeadSpawnManager.Instance.availableMonsters : null;
            if (monsters != null && index >= 0 && index < monsters.Count && monsters[index] != null)
            {
                if (monsters[index].monsterName.ToLower().Contains("berserker")) return true;
            }
            return index == 1; // default index 1 is Berserker
        }

        public void OnCancelSpawnClicked()
        {
            if (selectionConfirmationModal != null)
            {
                selectionConfirmationModal.CloseWindow();
            }
            else if (confirmationModal != null)
            {
                confirmationModal.CloseWindow();
            }
        }

        public void OnConfirmSpawnClicked()
        {
            int summonLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DeadSummonCharges) : 0;
            bool isBerserker = IsSelectedMonsterBerserker(_selectedMonsterIndex);

            if (isBerserker)
            {
                int maxBerserker = summonLvl >= berserkerUnlockLevel ? 1 + ((summonLvl - berserkerUnlockLevel) / 2) : 0;
                int remainingBerserker = Mathf.Max(0, maxBerserker - _berserkerSummoned);
                if (remainingBerserker <= 0)
                {
                    if (NotificationManager.Instance != null)
                        NotificationManager.Instance.ShowNotification("No Berserker summon charges remaining this match!", 2.5f);
                    if (selectionConfirmationModal != null) selectionConfirmationModal.CloseWindow();
                    else if (confirmationModal != null) confirmationModal.CloseWindow();
                    return;
                }
                _berserkerSummoned++;
            }
            else
            {
                int maxUndead = 2 + summonLvl;
                int remainingUndead = Mathf.Max(0, maxUndead - _undeadSummoned);
                if (remainingUndead <= 0)
                {
                    if (NotificationManager.Instance != null)
                        NotificationManager.Instance.ShowNotification("No Undead summon charges remaining this match!", 2.5f);
                    if (selectionConfirmationModal != null) selectionConfirmationModal.CloseWindow();
                    else if (confirmationModal != null) confirmationModal.CloseWindow();
                    return;
                }
                _undeadSummoned++;
            }

            UpdateChargesDisplay();

            if (DeadSpawnManager.Instance != null && NetworkManager.Singleton != null)
            {
                DeadSpawnManager.Instance.RequestSpawnMonsterServerRpc(_selectedMonsterIndex, NetworkManager.Singleton.LocalClientId);
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("Awakening monster in the deep tunnels...", 3f);
            }

            if (selectionConfirmationModal != null) selectionConfirmationModal.CloseWindow();
            else if (confirmationModal != null) confirmationModal.CloseWindow();
            CloseHUD();

            StartCoroutine(FocusCameraOnSpawnedMonsterRoutine());
        }

        private void OnSummonClicked()
        {
            OnCardSelectClicked(_selectedMonsterIndex);
        }

        private System.Collections.IEnumerator FocusCameraOnSpawnedMonsterRoutine()
        {
            if (monsterSpawnVirtualCamera == null) yield break;

            yield return new WaitForSeconds(0.25f);

            var monsters = FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
            if (monsters == null || monsters.Length == 0) yield break;

            MonsterController newestMonster = monsters[monsters.Length - 1];
            if (newestMonster == null) yield break;

            Transform camTarget = newestMonster.GetCameraTarget();
            if (camTarget == null)
            {
                camTarget = newestMonster.transform.Find("CameraFollowAnchor") ?? newestMonster.transform;
            }

            monsterSpawnVirtualCamera.gameObject.SetActive(true);
            monsterSpawnVirtualCamera.enabled = true;
            monsterSpawnVirtualCamera.Follow = camTarget;
            monsterSpawnVirtualCamera.LookAt = camTarget;
            monsterSpawnVirtualCamera.Priority = 99999;

            yield return new WaitForSeconds(cameraFocusDuration);

            monsterSpawnVirtualCamera.Priority = 10;
        }
    }
}
