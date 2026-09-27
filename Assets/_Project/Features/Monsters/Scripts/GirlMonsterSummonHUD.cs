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
        [Tooltip("Animator on Monsters child driving Heat SubPanel In/Out animations.")]
        public Animator monstersAnimator;

        [System.Serializable]
        public class MonsterCardBinding
        {
            public MonsterDefinitionSO monsterDefinition;
            public Michsky.UI.Heat.ShopButtonManager heatShopCard;
            [Tooltip("Heat UI ButtonManager for selecting this monster (e.g. Select button on the card).")]
            public Michsky.UI.Heat.ButtonManager selectButtonManager;

            [Header("Lock State (Berserker)")]
            [Tooltip("A dark overlay GameObject that covers the card when it is locked. Drag the lock-overlay panel here.")]
            public GameObject lockedOverlay;
            [Tooltip("TMP Text inside the lockedOverlay that displays the unlock requirement, e.g. 'Reach Level 5 to unlock'.")]
            public TMPro.TextMeshProUGUI lockedLabel;
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

        [Header("Monster Spectate Hotkey & Button (Manual Inspector Wiring)")]
        [Tooltip("Root GameObject for the MonsterSpectateHotkey.")]
        public GameObject monsterSpectateHotkey;
        [Tooltip("Michsky Heat HotkeyEvent component on MonsterSpectateHotkey.")]
        public Michsky.UI.Heat.HotkeyEvent monsterSpectateHotkeyEvent;
        [Tooltip("Optional standard Button component to spectate monsters.")]
        public Button monsterSpectateButton;
        [Tooltip("Optional Michsky ButtonManager component to spectate monsters.")]
        public Michsky.UI.Heat.ButtonManager heatMonsterSpectateButton;
        [Tooltip("Hotkey to trigger spectating monsters while HUD is open (default: Space).")]
        public Key spectateKey = Key.Space;

        [Header("Progression & Inspector Testing")]
        [Tooltip("If checked, immediately unlocks the Berserker regardless of level (for quick testing in the editor).")]
        public bool debugForceUnlockBerserker = false;

        [Tooltip("Dead Summon Charges upgrade level required to unlock the first Berserker (default Level 3 / Mid-level).")]
        public int berserkerUnlockLevel = 3;

        [Tooltip("Dead Summon Charges upgrade level required to unlock the second Berserker slot (default Level 5 / Final level).")]
        public int berserkerSecondUnlockLevel = 5;

        [Tooltip("Override summon upgrade level for testing (-1 uses real Cloud/Economy save level, 0 to 5 forces specific level).")]
        [Range(-1, 5)]
        public int debugOverrideSummonLevel = -1;

        [Header("Hotkeys")]
        [Tooltip("Hotkey to toggle this summon menu when playing as the Girl (default [X]).")]
        public Key toggleKey = Key.X;

        private int _totalCharges = 2;
        private int _remainingCharges = 2;
        private int _undeadSummoned = 0;
        private int _berserkerSummoned = 0;
        private int _selectedMonsterIndex = 0;
        private bool _isOpen = false;
        public bool IsOpen => _isOpen;
        public int TotalSummoned => _undeadSummoned + _berserkerSummoned;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (selectionConfirmationModal == null && confirmationModal != null)
            {
                selectionConfirmationModal = confirmationModal.GetComponent<SelectionConfirmationModal>();
            }

            // Close confirmation modals if open by default
            if (selectionConfirmationModal != null)
            {
                selectionConfirmationModal.CloseWindow();
            }
            else if (confirmationModal != null)
            {
                confirmationModal.CloseWindow();
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
            if (monsterSpectateButton != null)
            {
                monsterSpectateButton.onClick.RemoveListener(OnSpectateMonstersClicked);
                monsterSpectateButton.onClick.AddListener(OnSpectateMonstersClicked);
            }
            if (heatMonsterSpectateButton != null)
            {
                heatMonsterSpectateButton.onClick.RemoveListener(OnSpectateMonstersClicked);
                heatMonsterSpectateButton.onClick.AddListener(OnSpectateMonstersClicked);
            }
            if (monsterSpectateHotkey != null)
            {
                var btn = monsterSpectateHotkey.GetComponentInChildren<Button>(true);
                if (btn != null && btn != monsterSpectateButton)
                {
                    btn.onClick.RemoveListener(OnSpectateMonstersClicked);
                    btn.onClick.AddListener(OnSpectateMonstersClicked);
                }
                var heatBtn = monsterSpectateHotkey.GetComponentInChildren<Michsky.UI.Heat.ButtonManager>(true);
                if (heatBtn != null && heatBtn != heatMonsterSpectateButton)
                {
                    heatBtn.onClick.RemoveListener(OnSpectateMonstersClicked);
                    heatBtn.onClick.AddListener(OnSpectateMonstersClicked);
                }
            }

            if (monstersAnimator == null)
            {
                monstersAnimator = GetComponentInChildren<Animator>(true);
            }

            // Ensure all children are active so Heat UI components & layout don't reset
            foreach (Transform child in transform)
            {
                if (child != null && child.gameObject != gameObject)
                {
                    child.gameObject.SetActive(true);
                }
            }

            // Start hidden via CanvasGroup — exact mechanic of GirlDealUI
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            if (monstersAnimator != null)
            {
                monstersAnimator.Play("Panel Instant Out");
            }
        }

        private void Start()
        {
            InitializeSummonCharges();
            InitCardBindings();
            ApplyLockStates();

            // Ensure summon panel starts hidden via CanvasGroup & Instant Out
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            if (monstersAnimator != null)
            {
                monstersAnimator.Play("Panel Instant Out");
            }
        }

        private void InitializeSummonCharges()
        {
            int maxUndead = GetMaxUndeadSlots();
            int maxBerserker = GetMaxBerserkerSlots();
            _totalCharges = maxUndead + maxBerserker;
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

            if (SpectatorController.IsAnySpectating)
            {
                if (_isOpen) CloseHUD();
                return;
            }

            bool triggerSummon = KeybindingManager.IsActionTriggered("Summon")
                || (KeybindingManager.Instance == null && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame);

            if (triggerSummon && IsLocalPlayerGirl())
            {
                if (!_isOpen)
                {
                    if (GirlDealUI.IsAnyPanelOrModalOpen()) return;
                    OpenHUD();
                }
                else
                {
                    CloseHUD();
                }
            }
            else if (_isOpen)
            {
                if (KeybindingManager.IsActionTriggered("SpectateExit") || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                {
                    CloseHUD();
                }
                else if ((Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current[spectateKey].wasPressedThisFrame))
                         || KeybindingManager.IsActionTriggered("SpectateMonsters"))
                {
                    OnSpectateMonstersClicked();
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
            if (_isOpen) CloseHUD();
            else
            {
                if (GirlDealUI.IsAnyPanelOrModalOpen()) return;
                OpenHUD();
            }
        }

        public void OpenHUD()
        {
            _isOpen = true;

            // Re-evaluate lock states every open (player may have levelled up mid-match)
            ApplyLockStates();

            // 1. Reveal Summon HUD via CanvasGroup
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            // 2. Play 'Panel In' on the Animator (SubPanel.controller) so cards animate in smoothly
            if (monstersAnimator != null)
            {
                monstersAnimator.Play("Panel In");
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            UpdateChargesDisplay();
            SelectCard(_selectedMonsterIndex);
        }

        public void CloseHUD()
        {
            _isOpen = false;

            // Close sub-modals if open
            if (selectionConfirmationModal != null) selectionConfirmationModal.CloseWindow();
            else if (confirmationModal != null) confirmationModal.CloseWindow();

            // Animate cards out
            if (monstersAnimator != null)
            {
                monstersAnimator.Play("Panel Instant Out");
            }

            // Hide Summon HUD via CanvasGroup
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void SetVisible(bool visible)
        {
            if (visible) OpenHUD();
            else CloseHUD();
        }

        /// <summary>
        /// Gets the effective summon upgrade level, taking into account any debug inspector override.
        /// </summary>
        public int GetEffectiveSummonLevel()
        {
            if (debugOverrideSummonLevel >= 0)
            {
                return debugOverrideSummonLevel;
            }
            if (CloudCharacterSaveManager.Instance != null)
            {
                return CloudCharacterSaveManager.Instance.GetUpgradeLevel(UpgradeStatType.DeadSummonCharges);
            }
            return 0;
        }

        /// <summary>
        /// True if the Berserker is unlocked (either forced via inspector toggle or meets unlock level).
        /// </summary>
        public bool IsBerserkerUnlocked()
        {
            if (debugForceUnlockBerserker) return true;
            return GetEffectiveSummonLevel() >= berserkerUnlockLevel;
        }

        /// <summary>
        /// Berserker capacity progression:
        /// - Locked: 0 slots
        /// - Mid-level (Level 3+): 1 slot
        /// - Final level (Level 5 / Max): 2 slots
        /// </summary>
        public int GetMaxBerserkerSlots()
        {
            int lvl = GetEffectiveSummonLevel();
            if (debugForceUnlockBerserker && lvl < berserkerUnlockLevel)
            {
                return 1;
            }
            if (lvl >= berserkerSecondUnlockLevel)
            {
                return 2;
            }
            if (lvl >= berserkerUnlockLevel)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// Undead capacity progression (scales smoothly across all levels):
        /// - Level 0: 2 Undead
        /// - Level 1: 3 Undead
        /// - Level 2: 4 Undead
        /// - Level 3 (Mid-level milestone): 4 Undead (+1 Berserker = 5 total)
        /// - Level 4: 5 Undead (+1 Berserker = 6 total)
        /// - Level 5 (Final / Apex milestone): 6 Undead (+2 Berserkers = 8 total)
        /// </summary>
        public int GetMaxUndeadSlots()
        {
            int lvl = GetEffectiveSummonLevel();
            if (lvl >= 5) return 6;
            if (lvl >= 3) return 1 + lvl; // Level 3 -> 4, Level 4 -> 5
            return 2 + lvl;               // Level 0 -> 2, Level 1 -> 3, Level 2 -> 4
        }

        private void UpdateChargesDisplay()
        {
            int maxUndead = GetMaxUndeadSlots();
            int remainingUndead = Mathf.Max(0, maxUndead - _undeadSummoned);

            int maxBerserker = GetMaxBerserkerSlots();
            int remainingBerserker = Mathf.Max(0, maxBerserker - _berserkerSummoned);

            _remainingCharges = remainingUndead + remainingBerserker;
            _totalCharges = maxUndead + maxBerserker;

            // Sync MonsterSpectateHotkey visibility with whether active monsters exist in scene
            bool hasMonsters = SpectatorController.HasActiveMonstersInScene();
            if (monsterSpectateHotkey != null)
            {
                monsterSpectateHotkey.SetActive(hasMonsters);
            }
            if (monsterSpectateButton != null && monsterSpectateButton.gameObject != monsterSpectateHotkey)
            {
                monsterSpectateButton.gameObject.SetActive(hasMonsters);
            }
            if (heatMonsterSpectateButton != null && heatMonsterSpectateButton.gameObject != monsterSpectateHotkey)
            {
                heatMonsterSpectateButton.gameObject.SetActive(hasMonsters);
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
                    // Never assign an icon to the select button — intentionally left icon-free.
                    binding.selectButtonManager.onClick.RemoveAllListeners();
                    binding.selectButtonManager.onClick.AddListener(() => OnCardSelectClicked(index));
                }
            }
        }


        /// <summary>
        /// Shows or hides the locked overlay and toggles the Heat UI ShopButtonManager
        /// Purchased/Default state on each card based on Dead Summon Charges upgrade level.
        /// Call this at start and every time the HUD opens.
        /// </summary>
        private void ApplyLockStates()
        {
            if (cardBindings == null) return;

            bool berserkerUnlocked = IsBerserkerUnlocked();

            for (int i = 0; i < cardBindings.Count; i++)
            {
                var binding = cardBindings[i];
                if (binding == null) continue;

                bool isBerserker = IsSelectedMonsterBerserker(i);
                bool isLocked = isBerserker && !berserkerUnlocked;

                // 1. Native Heat UI ShopButtonManager state:
                // When locked, switch to State.Purchased which displays purchasedButton and purchasedIndicator.
                // When unlocked, switch to State.Default which displays the normal purchase/select button.
                if (binding.heatShopCard != null)
                {
                    binding.heatShopCard.SetState(isLocked ? Michsky.UI.Heat.ShopButtonManager.State.Purchased : Michsky.UI.Heat.ShopButtonManager.State.Default);

                    if (isLocked)
                    {
                        if (binding.heatShopCard.purchasedButton != null)
                        {
                            binding.heatShopCard.purchasedButton.SetText("LOCKED");
                            binding.heatShopCard.purchasedButton.Interactable(false);
                        }
                    }
                    else
                    {
                        if (binding.heatShopCard.purchaseButton != null)
                        {
                            binding.heatShopCard.purchaseButton.Interactable(true);
                        }
                    }

                    // Subtle dimming on the card body CanvasGroup when locked
                    var cg = binding.heatShopCard.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        cg.alpha = isLocked ? 0.6f : 1f;
                    }
                }

                // 2. Custom Atmospheric Locked Overlay (covers the card or portrait)
                if (binding.lockedOverlay != null)
                {
                    binding.lockedOverlay.SetActive(isLocked);
                }

                if (binding.lockedLabel != null)
                {
                    binding.lockedLabel.text = isLocked
                        ? $"Reach Dead Summon Charges Level {berserkerUnlockLevel} to unlock"
                        : string.Empty;
                }

                // 3. Fallback select button
                if (binding.selectButtonManager != null)
                {
                    binding.selectButtonManager.Interactable(!isLocked);
                }
            }
        }

        public void SelectCard(int index)
        {
            _selectedMonsterIndex = index;
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

            var spec = SpectatorController.MonsterInstance != null
                ? SpectatorController.MonsterInstance
                : SpectatorController.Instance;

            if (spec != null)
            {
                spec.TryOpenMonsterSpectator();
            }
        }

        public void OnCardSelectClicked(int index)
        {
            _selectedMonsterIndex = index;
            SelectCard(index);

            bool isBerserker = IsSelectedMonsterBerserker(index);

            if (isBerserker)
            {
                bool isUnlocked = IsBerserkerUnlocked();
                if (!isUnlocked)
                {
                    if (NotificationManager.Instance != null)
                    {
                        NotificationManager.Instance.ShowNotification($"The Berserker is locked! Requires Dead Summon Charges Level {berserkerUnlockLevel}.", 3.5f);
                    }
                    return;
                }

                int maxBerserker = GetMaxBerserkerSlots();
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
                int maxUndead = GetMaxUndeadSlots();
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

        /// <summary>
        /// Returns the MonsterDefinitionSO associated with the card binding index.
        /// </summary>
        public MonsterDefinitionSO GetMonsterDefinition(int index)
        {
            if (cardBindings != null && index >= 0 && index < cardBindings.Count && cardBindings[index] != null)
            {
                return cardBindings[index].monsterDefinition;
            }
            return null;
        }

        private bool IsSelectedMonsterBerserker(int index)
        {
            var def = GetMonsterDefinition(index);
            if (def != null && !string.IsNullOrEmpty(def.monsterName))
            {
                return def.monsterName.ToLower().Contains("berserker");
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
            bool isBerserker = IsSelectedMonsterBerserker(_selectedMonsterIndex);

            if (isBerserker)
            {
                int maxBerserker = GetMaxBerserkerSlots();
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
                int maxUndead = GetMaxUndeadSlots();
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

            var spawnMgr = DeadSpawnManager.Instance != null ? DeadSpawnManager.Instance : FindFirstObjectByType<DeadSpawnManager>();
            if (spawnMgr != null && NetworkManager.Singleton != null)
            {
                spawnMgr.RequestSpawnMonsterServerRpc(_selectedMonsterIndex, NetworkManager.Singleton.LocalClientId);
            }

            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.ShowNotification("Awakening monster in the deep tunnels...", 3f);
            }

            if (selectionConfirmationModal != null) selectionConfirmationModal.CloseWindow();
            else if (confirmationModal != null) confirmationModal.CloseWindow();
            CloseHUD();

            StartCoroutine(SpectateSpawnedMonsterRoutine());
        }

        private System.Collections.IEnumerator SpectateSpawnedMonsterRoutine()
        {
            // Close HUD first so the UI doesn't block the view
            CloseHUD();

            // Wait briefly for the monster to be spawned and replicated over the network
            float timeout = 3.0f;
            float elapsed = 0f;
            MonsterController targetMonster = null;
            TargetHealth targetHealth = null;

            while (elapsed < timeout)
            {
                var monsters = FindObjectsByType<MonsterController>(FindObjectsSortMode.None);
                if (monsters != null && monsters.Length > 0)
                {
                    for (int i = monsters.Length - 1; i >= 0; i--)
                    {
                        var m = monsters[i];
                        if (m != null && m.gameObject != null)
                        {
                            if (m.TryGetComponent<TargetHealth>(out var th) && !th.isCorpse.Value && th.CurrentHealth > 0)
                            {
                                targetMonster = m;
                                targetHealth = th;
                                break;
                            }
                            else if (m.TryGetComponent<HealthSystem>(out var hs) && !hs.IsDead)
                            {
                                targetMonster = m;
                                targetHealth = m.GetComponentInChildren<TargetHealth>();
                                break;
                            }
                        }
                    }
                }

                if (targetMonster != null) break;

                var ais = FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
                if (ais != null && ais.Length > 0)
                {
                    for (int i = ais.Length - 1; i >= 0; i--)
                    {
                        var ai = ais[i];
                        if (ai != null && ai.gameObject != null && ai.currentState != MonsterAI.AIState.Dead)
                        {
                            targetHealth = ai.GetComponent<TargetHealth>() ?? ai.GetComponentInChildren<TargetHealth>();
                            if (targetHealth != null && !targetHealth.isCorpse.Value && targetHealth.CurrentHealth > 0)
                            {
                                break;
                            }
                        }
                    }
                }

                if (targetHealth != null) break;

                yield return new WaitForSeconds(0.15f);
                elapsed += 0.15f;
            }

            var spec = SpectatorController.MonsterInstance != null 
                ? SpectatorController.MonsterInstance 
                : SpectatorController.Instance;

            if (spec != null)
            {
                spec.TryOpenMonsterSpectator(null, targetHealth);
            }
            else if (monsterSpawnVirtualCamera != null && targetMonster != null)
            {
                Transform camTarget = targetMonster.GetCameraTarget() ?? targetMonster.transform;
                monsterSpawnVirtualCamera.gameObject.SetActive(true);
                monsterSpawnVirtualCamera.enabled = true;
                monsterSpawnVirtualCamera.Follow = camTarget;
                monsterSpawnVirtualCamera.LookAt = camTarget;
                monsterSpawnVirtualCamera.Priority = 99999;
            }
        }
    }
}
