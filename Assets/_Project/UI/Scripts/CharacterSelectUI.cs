using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using NightCrawler.Economy;
using NightCrawler.UI;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — SRP: Manages Character Selection UI view inside the InvestigatorFlow panel.
/// Features Naruto/Apex-style Slot Card selection (horizontal card row + featured 3D model).
///
/// Flow:
///   1. Player selects operative via slot cards.
///   2. Player taps CONFIRM / READY.
///   3. Match Stake Modal opens.
///      - Confirm button remains INACTIVE until at least 2 credits are entered.
///      - If cancelled without confirming: player is NOT marked ready.
///      - Once confirmed: credits are staked, selection is locked in, and live ready status is reported.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    // =========================================================================
    //  Inspector — Root Panels
    // =========================================================================

    [Header("Root Panel")]
    public GameObject characterSelectPanel;

    [Header("Vengeful Spirit Secret View")]
    public GameObject vengefulSpiritPanel;

    [Header("Investigator View")]
    public GameObject investigatorPanel;

    // =========================================================================
    //  Inspector — Featured 3D Model Stage
    // =========================================================================

    [Header("3D Featured Model Stage")]
    [Tooltip("Transform pivot in the scene where the featured 3D character model spawns.")]
    public Transform modelPreviewPivot;

    [Tooltip("Duration in seconds to animate model swap (fade/scale).")]
    public float modelSwapDuration = 0.35f;

    // =========================================================================
    //  Inspector — 2D Slot Card Row
    // =========================================================================

    [Header("Slot Cards (2D Card Row)")]
    [Tooltip("Parent Transform under which character slot cards are spawned (Horizontal Layout Group).")]
    public Transform slotCardContainer;

    [Tooltip("Prefab for a single slot card.")]
    public GameObject slotCardPrefab;

    [Tooltip("Color applied to the frame/border of the selected slot card.")]
    public Color selectedCardHighlightColor = new Color(1f, 0.85f, 0.2f);

    [Tooltip("Color applied to unselected slot cards.")]
    public Color unselectedCardColor = Color.white;

    // =========================================================================
    //  Inspector — Side Details Panel
    // =========================================================================

    [Header("Side Details Panel")]
    public TextMeshProUGUI detailsTitleText;
    public TextMeshProUGUI detailsDescriptionText;
    public TextMeshProUGUI detailsAbilitiesText;
    public Image detailsIconImage;
    [Tooltip("Parent root for character details/abilities panel to hide on confirm.")]
    public GameObject sideDetailsPanel;

    [Header("Stat Progress Bars")]
    [Tooltip("List of progress bar items in the Details panel. Automatically shows stats relevant to the selected character and hides others.")]
    public List<StatProgressBarItem> statProgressBars = new List<StatProgressBarItem>();

    // =========================================================================
    //  Inspector — Ready / Confirm Button
    // =========================================================================

    [Header("Confirm / Ready Button")]
    [Tooltip("Michsky ButtonManager for Confirm / Ready.")]
    public ButtonManager heatConfirmButton;

    [Tooltip("Michsky BoxButtonManager for Confirm / Ready (optional).")]
    public BoxButtonManager heatBoxConfirmButton;

    // =========================================================================
    //  Inspector — Match Stake Modal (Opened on Ready)
    // =========================================================================

    [Header("Match Stake Modal (Opened on Ready)")]
    [Tooltip("Modal Window that pops up when tapping CONFIRM to enter the match stake.")]
    public ModalWindowManager matchStakeModal;

    [Tooltip("Michsky Input Field inside the modal where the player types their stake.")]
    public InputFieldManager heatStakeInputField;

    [Tooltip("Confirm button inside the stake modal. Inactive until 2+ credits entered.")]
    public ButtonManager heatStakeConfirmButton;

    [Tooltip("Box Button for confirming stake (optional).")]
    public BoxButtonManager heatBoxStakeConfirmButton;

    [Tooltip("Cancel / Close button inside the stake modal.")]
    public ButtonManager heatStakeCancelButton;

    [Tooltip("Error / Hint label inside the stake modal (e.g. 'Min 2 credits').")]
    public TextMeshProUGUI stakeErrorText;

    [Tooltip("Label inside the modal displaying player's current credit balance.")]
    public TextMeshProUGUI creditBalanceText;

    // =========================================================================
    //  Inspector — Character Data & Filters
    // =========================================================================

    [Header("Roster Filter")]
    [Tooltip("Toggle to include or exclude Hazard Specialist.")]
    public bool includeHazardSpecialist = false;

    [Header("Character Roster (ScriptableObjects)")]
    public List<CharacterDefinitionSO> characterDefinitions = new List<CharacterDefinitionSO>();

    [Header("Inline Character Data (Fallback)")]
    public List<InvestigatorCharacterData> characterDataList = new List<InvestigatorCharacterData>();

    [Header("Player Status Panel (Ready / Waiting)")]
    [Tooltip("Root panel shown after the player confirms selection. Shows every player's name and ready status.")]
    public GameObject playerStatusPanel;
    public Transform playerStatusContainer;
    public GameObject playerStatusRowPrefab;
    public string statusWaitingText = "NOT READY";
    public string statusReadyText = "READY";

    // =========================================================================
    //  Inspector — Exit Confirmation Modal & Hotkey (Manual Wiring — No Auto-Find)
    // =========================================================================

    [Header("Exit Confirmation Modal & Hotkey (Manual Assignment — No Auto-Find)")]
    [Tooltip("Explicit reference to the Exit Confirmation Modal for Character Select (e.g. ExitModal under InvestigatorFlow). No auto-finding.")]
    public ModalWindowManager exitConfirmModal;

    [Tooltip("Optional explicit reference to the Confirm button inside the exit modal.")]
    public BoxButtonManager heatBoxExitConfirmButton;

    [Tooltip("Optional standard/Heat ButtonManager for confirming exit.")]
    public ButtonManager heatExitConfirmButton;

    [Tooltip("Optional explicit reference to the Exit Hotkey indicator/button (e.g. ExitHotKey under InvestigatorFlow).")]
    public HotkeyEvent exitHotkey;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------

    private int                       _selectedIndex          = 0;
    private bool                      _isVengefulSpirit       = false;
    private GameObject                _currentPreviewInstance;
    private bool                      _initialized            = false;
    private readonly List<Button>     _slotCardButtons        = new List<Button>();
    private readonly List<Image>      _slotCardFrames         = new List<Image>();
    private readonly List<CharacterSlotCard> _slotCards       = new List<CharacterSlotCard>();
    private readonly List<BoxButtonManager> _heatBoxSlotCards  = new List<BoxButtonManager>();
    private readonly List<ButtonManager> _heatSlotButtons      = new List<ButtonManager>();
    private Coroutine                 _swapCoroutine;
    private bool                      _localConfirmed         = false;
    private readonly List<GameObject> _statusRows             = new List<GameObject>();
    private int                       _lastEscapeFrame        = -1;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Awake()
    {
        InitExitBindings();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleExitHotkey();
        }
    }

    public void InitExitBindings()
    {
        // Explicit wiring only — no auto-finding!
        if (exitConfirmModal != null)
        {
            exitConfirmModal.onConfirm.RemoveListener(ConfirmExitToHome);
            exitConfirmModal.onConfirm.AddListener(ConfirmExitToHome);
        }

        if (heatBoxExitConfirmButton != null)
        {
            MichskyUIBridge.BindButton(null, heatBoxExitConfirmButton, ConfirmExitToHome);
        }
        else if (heatExitConfirmButton != null)
        {
            MichskyUIBridge.BindButton(null, heatExitConfirmButton, ConfirmExitToHome);
        }

        if (exitHotkey != null)
        {
            exitHotkey.onHotkeyPress.RemoveListener(HandleExitHotkey);
            exitHotkey.onHotkeyPress.AddListener(HandleExitHotkey);
        }
    }

    /// <summary>
    /// Handles hotkey press (Esc or UI HotKey button).
    /// Prevents double-firing in the same frame if both HotkeyEvent and Keyboard fire.
    /// </summary>
    public void HandleExitHotkey()
    {
        if (Time.frameCount == _lastEscapeFrame) return;
        _lastEscapeFrame = Time.frameCount;

        // 1. If Exit Confirm Modal is open, cancel/close it
        if (exitConfirmModal != null && exitConfirmModal.isOn)
        {
            exitConfirmModal.CloseWindow();
            return;
        }

        // 2. If Match Stake Modal is open, close/cancel it
        if (matchStakeModal != null && matchStakeModal.isOn)
        {
            OnStakeModalCancelled();
            return;
        }

        // 3. Otherwise, prompt the exit modal if assigned
        RequestExitToHome();
    }

    public void RequestExitToHome()
    {
        if (exitConfirmModal != null)
        {
            if (!exitConfirmModal.gameObject.activeSelf)
                exitConfirmModal.gameObject.SetActive(true);
            exitConfirmModal.OpenWindow();
        }
    }

    public void ConfirmExitToHome()
    {
        if (exitConfirmModal != null)
        {
            exitConfirmModal.CloseWindow();
        }

        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.ConfirmDisconnect();
        }
        else
        {
            if (CharacterSceneController.Instance != null)
                CharacterSceneController.Instance.DisableCharacterSelectEnvironment();
            if (characterSelectPanel != null)
                characterSelectPanel.SetActive(false);
        }
    }

    void Start()
    {
        EnsureDefaultCharacterData();

        if (characterSelectPanel != null && !characterSelectPanel.activeInHierarchy) return;

        InitialSetup();
    }

    void OnEnable()
    {
        InitExitBindings();
        _localConfirmed = false;
        if (playerStatusPanel != null) playerStatusPanel.SetActive(false);

        // Reset visibility of selection controls
        if (slotCardContainer != null) slotCardContainer.gameObject.SetActive(true);
        if (heatConfirmButton != null) heatConfirmButton.gameObject.SetActive(true);
        if (heatBoxConfirmButton != null) heatBoxConfirmButton.gameObject.SetActive(true);
        if (detailsAbilitiesText != null) detailsAbilitiesText.gameObject.SetActive(true);
        if (detailsDescriptionText != null) detailsDescriptionText.gameObject.SetActive(true);
        if (sideDetailsPanel != null) sideDetailsPanel.SetActive(true);

        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        if (forceInvestigator)
        {
            PersistentCharacterSelection.SetIsVengefulSpirit(false);
        }
        else if (PersistentCharacterSelection.IsVengefulSpirit())
        {
            gameObject.SetActive(false);
            return;
        }

        EnsureDefaultCharacterData();

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.EnableCharacterSelectEnvironment();

        if (!_initialized)
            InitialSetup();

        CheckLocalRole();

        // Refresh UI state when enabled
        SelectProfession(_selectedIndex);

        // Subscribe to live ready-state updates
        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated += HandlePlayerLobbyStatesUpdated;
        }
    }

    void OnDisable()
    {
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();

        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated -= HandlePlayerLobbyStatesUpdated;
        }
    }

    void OnDestroy()
    {
        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged -= OnRoleSelectionChanged;
        }

        if (_currentPreviewInstance != null)
            Destroy(_currentPreviewInstance);
    }

    // =========================================================================
    //  Initialization
    // =========================================================================

    private void InitialSetup()
    {
        if (_initialized) return;
        _initialized = true;

        // Wire Ready / Confirm button to open the Match Stake modal
        MichskyUIBridge.BindButton(null, heatConfirmButton, OnReadyButtonClicked);
        MichskyUIBridge.BindButton(null, heatBoxConfirmButton, OnReadyButtonClicked);

        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged += OnRoleSelectionChanged;
        }

        CheckLocalRole();
        BuildSlotCards();

        int savedIndex = PersistentCharacterSelection.GetSelectedCharacterIndex();
        SelectProfession(savedIndex);
    }

    // =========================================================================
    //  Match Stake Modal Flow
    // =========================================================================

    private void OnReadyButtonClicked()
    {
        if (_localConfirmed) return;

        if (matchStakeModal != null)
        {
            OpenStakeModal();
        }
        else
        {
            // Direct fallback if no modal assigned
            FinalizeSelectionAndReady(CurrencyConfig.MinimumStake);
        }
    }

    private void EnsureStakeReferences()
    {
        if (stakeErrorText == null && matchStakeModal != null)
        {
            var texts = matchStakeModal.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var t in texts)
            {
                if (t.gameObject.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stakeErrorText = t;
                    break;
                }
            }
        }

        if (stakeErrorText != null)
        {
            stakeErrorText.richText = true;
        }
    }

    private void OpenStakeModal()
    {
        EnsureStakeReferences();

        int balance = CloudCharacterSaveManager.Instance != null
            ? CloudCharacterSaveManager.Instance.CurrentCredits
            : 50;

        if (creditBalanceText != null)
            creditBalanceText.text = CurrencyConfig.FormatBalance(balance);

        // Bind input typing validation
        MichskyUIBridge.BindInputField(null, heatStakeInputField, OnStakeInputChanged);

        // Bind confirm and cancel buttons
        MichskyUIBridge.BindButton(null, heatStakeConfirmButton, OnStakeModalConfirmed);
        if (heatBoxStakeConfirmButton != null)
            MichskyUIBridge.BindButton(null, heatBoxStakeConfirmButton, OnStakeModalConfirmed);

        if (heatStakeCancelButton != null)
            MichskyUIBridge.BindButton(null, heatStakeCancelButton, OnStakeModalCancelled);

        // Clear input text initially and run initial validation & error state
        MichskyUIBridge.SetInputText(null, heatStakeInputField, string.Empty);
        OnStakeInputChanged(string.Empty);

        matchStakeModal.OpenWindow();
    }

    private void OnStakeInputChanged(string raw)
    {
        EnsureStakeReferences();

        bool isValid = ValidateStakeInput(raw, out int stake, out string error);

        if (stakeErrorText != null)
        {
            if (isValid)
            {
                stakeErrorText.text = $"Ready to stake {CurrencyConfig.Format(stake)}";
                stakeErrorText.gameObject.SetActive(true);
            }
            else
            {
                stakeErrorText.text = error;
                stakeErrorText.gameObject.SetActive(!string.IsNullOrEmpty(error));
            }
        }

        SetStakeConfirmInteractable(isValid);
    }

    private bool ValidateStakeInput(string raw, out int stake, out string errorMessage)
    {
        stake = 0;
        errorMessage = "";

        int balance = CloudCharacterSaveManager.Instance != null
            ? CloudCharacterSaveManager.Instance.CurrentCredits
            : 50;

        if (balance < CurrencyConfig.MinimumStake)
        {
            errorMessage = $"Insufficient credits! You need at least {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol} to participate (Balance: {balance} {CurrencyConfig.CurrencySymbol}).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            errorMessage = $"Enter stake amount ({CurrencyConfig.MinimumStake} - {balance} {CurrencyConfig.CurrencySymbol}) to confirm.";
            return false;
        }

        if (!int.TryParse(raw.Trim(), out stake))
        {
            errorMessage = "Please enter a valid whole number.";
            return false;
        }

        if (stake <= 0)
        {
            errorMessage = $"Stake must be at least {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        if (stake < CurrencyConfig.MinimumStake)
        {
            errorMessage = $"Stake too low! Minimum required is {CurrencyConfig.MinimumStake} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        if (stake > balance)
        {
            errorMessage = $"Insufficient credits! You cannot stake {stake} {CurrencyConfig.CurrencySymbol} with a balance of {balance} {CurrencyConfig.CurrencySymbol}.";
            return false;
        }

        return true;
    }

    private void SetStakeConfirmInteractable(bool interactable)
    {
        if (heatStakeConfirmButton != null)
        {
            heatStakeConfirmButton.isInteractable = interactable;
            heatStakeConfirmButton.UpdateUI();
        }
        if (heatBoxStakeConfirmButton != null)
        {
            heatBoxStakeConfirmButton.isInteractable = interactable;
            heatBoxStakeConfirmButton.UpdateUI();
        }
        MichskyUIBridge.SetButtonInteractable(null, heatStakeConfirmButton, interactable);
        if (heatBoxStakeConfirmButton != null)
            MichskyUIBridge.SetButtonInteractable(null, heatBoxStakeConfirmButton, interactable);
    }

    private void OnStakeModalCancelled()
    {
        if (matchStakeModal != null)
            matchStakeModal.CloseWindow();

        // Player cancelled without inputting/confirming: DO NOT set ready!
    }

    private void OnStakeModalConfirmed()
    {
        string raw = MichskyUIBridge.GetInputText(null, heatStakeInputField);
        if (!ValidateStakeInput(raw, out int stake, out string error))
        {
            if (stakeErrorText != null)
            {
                stakeErrorText.text = error;
                stakeErrorText.gameObject.SetActive(true);
            }
            SetStakeConfirmInteractable(false);
            return;
        }

        if (matchStakeModal != null)
            matchStakeModal.CloseWindow();

        FinalizeSelectionAndReady(stake);
    }

    private void FinalizeSelectionAndReady(int stake)
    {
        if (_localConfirmed) return;

        PersistentCharacterSelection.SetSavedMatchStake(stake);
        if (CloudCharacterSaveManager.Instance != null)
        {
            CloudCharacterSaveManager.Instance.SpendCredits(stake);
        }

        _localConfirmed = true;
        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);

        if (!_isVengefulSpirit && CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(_selectedIndex);
        }

        // Hide selection controls & ready button
        if (slotCardContainer != null) slotCardContainer.gameObject.SetActive(false);
        if (heatConfirmButton != null) heatConfirmButton.gameObject.SetActive(false);
        if (heatBoxConfirmButton != null) heatBoxConfirmButton.gameObject.SetActive(false);

        // Hide character abilities and details text
        if (detailsAbilitiesText != null) detailsAbilitiesText.gameObject.SetActive(false);
        if (detailsDescriptionText != null) detailsDescriptionText.gameObject.SetActive(false);
        if (sideDetailsPanel != null) sideDetailsPanel.SetActive(false);

        if (statProgressBars != null)
        {
            foreach (var bar in statProgressBars)
            {
                if (bar != null) bar.SetVisible(false);
            }
        }

        // Notify the ready tracker
        if (PlayerReadyTracker.Instance != null)
        {
            int localLvl = CloudCharacterSaveManager.Instance != null ? CloudCharacterSaveManager.Instance.GetPlayerLevel() : 1;
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
            PlayerReadyTracker.Instance.ReportInvestigatorConfirmed(localId, localLvl, _selectedIndex);
        }

        // Show player status panel
        if (playerStatusPanel != null)
            playerStatusPanel.SetActive(true);

        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        if (forceInvestigator || PlayerReadyTracker.Instance == null)
            GoToSquadScreen();
    }

    // =========================================================================
    //  Public API & Slot Navigation (With Hazard Specialist Filter)
    // =========================================================================

    private readonly List<CharacterDefinitionSO> _filteredDefinitions = new List<CharacterDefinitionSO>();
    private readonly List<InvestigatorCharacterData> _filteredInlineData = new List<InvestigatorCharacterData>();

    private void RefreshFilteredRoster()
    {
        _filteredDefinitions.Clear();
        if (characterDefinitions != null)
        {
            foreach (var so in characterDefinitions)
            {
                if (so == null) continue;
                if (!includeHazardSpecialist && (so.profession == InvestigatorProfession.HazardSpecialist || so.characterName.ToLower().Contains("hazard")))
                    continue;
                _filteredDefinitions.Add(so);
            }
        }

        _filteredInlineData.Clear();
        var rawList = (characterDataList != null && characterDataList.Count > 0)
            ? characterDataList
            : (CharacterSelectManager.Instance != null ? CharacterSelectManager.Instance.availableCharacters : null);

        if (rawList != null)
        {
            foreach (var d in rawList)
            {
                if (d == null) continue;
                if (!includeHazardSpecialist && (d.profession == InvestigatorProfession.HazardSpecialist || d.characterName.ToLower().Contains("hazard")))
                    continue;
                _filteredInlineData.Add(d);
            }
        }
    }

    public void SelectNext()
    {
        int count = GetTotalCharacterCount();
        if (count == 0) return;
        SelectProfession((_selectedIndex + 1) % count);
    }

    public void SelectPrevious()
    {
        int count = GetTotalCharacterCount();
        if (count == 0) return;
        SelectProfession((_selectedIndex - 1 + count) % count);
    }

    public int GetTotalCharacterCount()
    {
        RefreshFilteredRoster();
        if (_filteredDefinitions.Count > 0)
            return _filteredDefinitions.Count;
        return _filteredInlineData.Count;
    }

    public void SelectProfession(int index)
    {
        RefreshFilteredRoster();
        int count = GetTotalCharacterCount();
        if (count == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, count - 1);

        string selectedCharName = string.Empty;

        // 1. Check ScriptableObjects list first
        if (_filteredDefinitions.Count > 0 && _selectedIndex < _filteredDefinitions.Count && _filteredDefinitions[_selectedIndex] != null)
        {
            CharacterDefinitionSO so = _filteredDefinitions[_selectedIndex];
            selectedCharName = so.characterName;

            if (detailsTitleText       != null) detailsTitleText.text       = so.characterName;
            if (detailsDescriptionText != null) detailsDescriptionText.text = so.description;
            if (detailsAbilitiesText   != null) detailsAbilitiesText.text   = so.abilityDescriptions;

            if (detailsIconImage != null)
            {
                detailsIconImage.sprite  = so.portrait;
                detailsIconImage.enabled = (so.portrait != null);
            }

            GameObject modelPrefab = ResolvePreviewPrefab(so);
            SwapFeaturedModel(modelPrefab);
        }
        else
        {
            // 2. Fallback to inline characterDataList
            InvestigatorCharacterData data = GetCharacterData(_selectedIndex);
            if (data != null)
            {
                selectedCharName = data.characterName;

                if (detailsTitleText       != null) detailsTitleText.text       = data.characterName;
                if (detailsDescriptionText != null) detailsDescriptionText.text = data.description;
                if (detailsAbilitiesText   != null) detailsAbilitiesText.text   = data.specialAbilities;

                if (detailsIconImage != null)
                {
                    detailsIconImage.sprite  = data.characterIcon;
                    detailsIconImage.enabled = (data.characterIcon != null);
                }

                GameObject modelPrefab = data.characterPrefab;
                if (modelPrefab == null) modelPrefab = FindFallbackPrefabByName(data.characterName);
                SwapFeaturedModel(modelPrefab);
            }
        }

        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);
        if (!string.IsNullOrEmpty(selectedCharName))
        {
            PersistentCharacterSelection.SetSelectedCharacterName(selectedCharName);
        }
        PersistentCharacterSelection.SetIsVengefulSpirit(false);

        UpdateSlotCardHighlights();

        UpdateCharacterStatProgressBars(_selectedIndex);

        SyncSelectionToServer(_selectedIndex);

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.ResetIdleTimer();
    }

    // =========================================================================
    //  Stat Progress Bars Helpers
    // =========================================================================

    public List<UpgradeStatType> GetRelevantStatsForCharacter(int index)
    {
        List<UpgradeStatType> result = new List<UpgradeStatType>();
        InvestigatorProfession profession = InvestigatorProfession.MineWorker;

        if (_filteredDefinitions.Count > 0 && index >= 0 && index < _filteredDefinitions.Count && _filteredDefinitions[index] != null)
        {
            var so = _filteredDefinitions[index];
            profession = so.profession;

            // Check character name to ensure correct profession
            string charName = (so.characterName + " " + so.name).ToLowerInvariant();
            if (charName.Contains("medic")) profession = InvestigatorProfession.FieldMedic;
            else if (charName.Contains("explorer")) profession = InvestigatorProfession.Explorer;
            else if (charName.Contains("hazard")) profession = InvestigatorProfession.HazardSpecialist;
            else if (charName.Contains("priest")) profession = InvestigatorProfession.CursedPriest;

            // Only use SO relevantStats if it contains valid, non-corrupted investigator stats
            if (so.relevantStats != null && so.relevantStats.Count > 0)
            {
                bool hasInvalidStats = false;
                foreach (var s in so.relevantStats)
                {
                    // Stat values >= 7 are Girl/Demon stats accidentally serialized on an investigator SO
                    if ((int)s >= 7) { hasInvalidStats = true; break; }
                }

                if (profession == InvestigatorProfession.FieldMedic)
                {
                    if (!so.relevantStats.Contains(UpgradeStatType.VialCount) || !so.relevantStats.Contains(UpgradeStatType.VialHealingPower))
                    {
                        hasInvalidStats = true;
                    }
                }

                if (!hasInvalidStats)
                {
                    return so.relevantStats;
                }
            }
        }
        else
        {
            var data = GetCharacterData(index);
            if (data != null)
            {
                profession = data.profession;

                if (profession == InvestigatorProfession.MineWorker && !string.IsNullOrEmpty(data.characterName))
                {
                    string charName = data.characterName.ToLowerInvariant();
                    if (charName.Contains("medic")) profession = InvestigatorProfession.FieldMedic;
                    else if (charName.Contains("explorer")) profession = InvestigatorProfession.Explorer;
                    else if (charName.Contains("hazard")) profession = InvestigatorProfession.HazardSpecialist;
                    else if (charName.Contains("priest")) profession = InvestigatorProfession.CursedPriest;
                }
            }
        }

        switch (profession)
        {
            case InvestigatorProfession.MineWorker:
                result.Add(UpgradeStatType.WeaponDamage);
                result.Add(UpgradeStatType.DamageResistance);
                break;
            case InvestigatorProfession.FieldMedic:
                result.Add(UpgradeStatType.VialCount);
                result.Add(UpgradeStatType.VialHealingPower);
                result.Add(UpgradeStatType.DamageResistance);
                break;
            case InvestigatorProfession.HazardSpecialist:
                result.Add(UpgradeStatType.MaskFilter);
                result.Add(UpgradeStatType.DamageResistance);
                break;
            case InvestigatorProfession.CursedPriest:
                result.Add(UpgradeStatType.SpiritualLevel);
                result.Add(UpgradeStatType.DamageResistance);
                break;
            case InvestigatorProfession.Explorer:
                result.Add(UpgradeStatType.MapPower);
                result.Add(UpgradeStatType.DamageResistance);
                break;
            default:
                result.Add(UpgradeStatType.DamageResistance);
                break;
        }

        return result;
    }

    private void UpdateCharacterStatProgressBars(int index)
    {
        if (statProgressBars == null || statProgressBars.Count == 0) return;

        var relevant = GetRelevantStatsForCharacter(index);

        foreach (var bar in statProgressBars)
        {
            if (bar == null) continue;

            bool isRelevant = relevant != null && relevant.Contains(bar.statType);
            bar.SetVisible(isRelevant);

            if (isRelevant)
            {
                int level = CloudCharacterSaveManager.Instance != null
                    ? CloudCharacterSaveManager.Instance.GetUpgradeLevel(bar.statType)
                    : 0;
                bar.Refresh(level);
            }
        }
    }

    private void SyncSelectionToServer(int index)
    {
        if (_isVengefulSpirit) return;

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            if (CharacterSelectManager.Instance != null)
            {
                CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(index);
            }
            CharacterSelectManager.SetChoiceStatic(NetworkManager.Singleton.LocalClientId, index);
        }
    }

    public InvestigatorCharacterData GetCharacterData(int index)
    {
        RefreshFilteredRoster();
        if (index >= 0 && index < _filteredInlineData.Count)
            return _filteredInlineData[index];

        return null;
    }

    public CharacterDefinitionSO GetCharacterDefinition(int index)
    {
        RefreshFilteredRoster();
        if (index >= 0 && index < _filteredDefinitions.Count)
            return _filteredDefinitions[index];
        return null;
    }

    private GameObject ResolvePreviewPrefab(CharacterDefinitionSO so)
    {
        if (so == null) return null;
        if (so.characterPrefab != null) return so.characterPrefab;

        // Auto-heal missing/broken prefab link by matching character name
        return FindFallbackPrefabByName(so.characterName);
    }

    private GameObject FindFallbackPrefabByName(string charName)
    {
        if (string.IsNullOrEmpty(charName)) return null;

        if (GameManager.Instance != null && GameManager.Instance.explorerPrefabs != null)
        {
            foreach (var p in GameManager.Instance.explorerPrefabs)
            {
                if (p != null && IsNameMatch(p.name, charName))
                    return p;
            }
        }

        if (characterDataList != null)
        {
            foreach (var d in characterDataList)
            {
                if (d != null && d.characterPrefab != null && IsNameMatch(d.characterName, charName))
                    return d.characterPrefab;
            }
        }

        return null;
    }

    public static bool IsNameMatch(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return false;
        string s = source.ToLowerInvariant();
        string t = target.ToLowerInvariant();

        if (s.Contains(t) || t.Contains(s)) return true;
        if ((s.Contains("adventurer") || s.Contains("explorer")) && (t.Contains("adventurer") || t.Contains("explorer"))) return true;
        if ((s.Contains("miner") || s.Contains("mine")) && (t.Contains("miner") || t.Contains("mine"))) return true;
        if ((s.Contains("medic") || s.Contains("doctor")) && (t.Contains("medic") || t.Contains("doctor"))) return true;
        if ((s.Contains("priest") || s.Contains("cursed")) && (t.Contains("priest") || t.Contains("cursed"))) return true;
        if (s.Contains("hazard") && t.Contains("hazard")) return true;

        return false;
    }

    // =========================================================================
    //  Slot Cards Building
    // =========================================================================

    private void BuildSlotCards()
    {
        if (slotCardContainer == null || slotCardPrefab == null) return;

        foreach (Transform child in slotCardContainer)
            Destroy(child.gameObject);

        _slotCardButtons.Clear();
        _slotCardFrames.Clear();
        _slotCards.Clear();
        _heatBoxSlotCards.Clear();
        _heatSlotButtons.Clear();

        RefreshFilteredRoster();
        bool useSO = _filteredDefinitions.Count > 0;
        int count = GetTotalCharacterCount();

        for (int i = 0; i < count; i++)
        {
            int capturedIndex = i;
            string charName = "";
            Sprite portrait = null;

            if (useSO && i < _filteredDefinitions.Count && _filteredDefinitions[i] != null)
            {
                charName = _filteredDefinitions[i].characterName;
                portrait = _filteredDefinitions[i].portrait;
            }
            else
            {
                InvestigatorCharacterData data = GetCharacterData(i);
                if (data != null)
                {
                    charName = data.characterName;
                    portrait = data.characterIcon;
                }
            }

            GameObject card = Instantiate(slotCardPrefab, slotCardContainer);
            card.name = $"SlotCard_{charName}";

            // Standard icon sprite fallback
            Image portraitImg = card.GetComponentInChildren<Image>();
            if (portraitImg != null && portrait != null)
                portraitImg.sprite = portrait;

            // Standard name label fallback
            TMP_Text nameLabel = card.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null)
                nameLabel.text = charName;

            // Standard Button click
            Button btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => SelectProfession(capturedIndex));
                _slotCardButtons.Add(btn);
                _slotCardFrames.Add(btn.GetComponent<Image>());
            }

            // Michsky Heat UI BoxButtonManager support
            var bbm = card.GetComponent<BoxButtonManager>();
            if (bbm != null)
            {
                bbm.SetText(charName);
                if (portrait != null) bbm.SetIcon(portrait);
                bbm.onClick.RemoveAllListeners();
                bbm.onClick.AddListener(() => SelectProfession(capturedIndex));
                _heatBoxSlotCards.Add(bbm);
            }

            // Michsky Heat UI ButtonManager support
            var bm = card.GetComponent<ButtonManager>();
            if (bm != null)
            {
                bm.SetText(charName);
                if (portrait != null) bm.SetIcon(portrait);
                bm.onClick.RemoveAllListeners();
                bm.onClick.AddListener(() => SelectProfession(capturedIndex));
                _heatSlotButtons.Add(bm);
            }

            // Register the CharacterSlotCard component
            CharacterSlotCard slotCard = card.GetComponent<CharacterSlotCard>();
            _slotCards.Add(slotCard);
        }

        UpdateSlotCardHighlights();
    }

    private void UpdateSlotCardHighlights()
    {
        for (int i = 0; i < _slotCards.Count; i++)
        {
            if (_slotCards[i] != null)
                _slotCards[i].SetSelected(i == _selectedIndex);
        }

        for (int i = 0; i < _heatBoxSlotCards.Count; i++)
        {
            if (_heatBoxSlotCards[i] != null)
            {
                bool isSelected = (i == _selectedIndex);
                string baseName = (i < _filteredDefinitions.Count && _filteredDefinitions[i] != null) 
                    ? _filteredDefinitions[i].characterName 
                    : (_slotCardButtons.Count > i ? _slotCardButtons[i].name.Replace("SlotCard_", "") : "OPERATIVE");

                _heatBoxSlotCards[i].SetText(isSelected ? $"<b>{baseName.ToUpper()}</b>" : baseName);
            }
        }

        for (int i = 0; i < _slotCardFrames.Count; i++)
        {
            if (i < _slotCards.Count && _slotCards[i] != null) continue;
            if (_slotCardFrames[i] != null)
                _slotCardFrames[i].color = (i == _selectedIndex) ? selectedCardHighlightColor : unselectedCardColor;
        }
    }

    // =========================================================================
    //  Featured 3D Model Swap
    // =========================================================================

    private void SwapFeaturedModel(GameObject prefabToSpawn)
    {
        if (_swapCoroutine != null) StopCoroutine(_swapCoroutine);
        _swapCoroutine = StartCoroutine(SwapModelRoutine(prefabToSpawn));
    }

    private IEnumerator SwapModelRoutine(GameObject prefabToSpawn)
    {
        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }

        if (modelPreviewPivot == null || prefabToSpawn == null) yield break;

        _currentPreviewInstance = Instantiate(
            prefabToSpawn, modelPreviewPivot.position,
            modelPreviewPivot.rotation, modelPreviewPivot);

        foreach (var script in _currentPreviewInstance.GetComponentsInChildren<MonoBehaviour>())
        {
            if (script is CharacterAnimationController || script is CharacterAnimationSystem) continue;
            script.enabled = false;
        }

        CharacterAnimationController animCtrl =
            _currentPreviewInstance.GetComponent<CharacterAnimationController>();
        if (animCtrl == null)
            animCtrl = _currentPreviewInstance.AddComponent<CharacterAnimationController>();

        animCtrl.characterType = CharacterAnimationController.CharacterType.Adventurer;

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.NotifyPreviewModelChanged(animCtrl);

        yield return null;
    }

    // =========================================================================
    //  Role Handling
    // =========================================================================

    private void OnRoleSelectionChanged(bool prev, bool current)
    {
        if (current) CheckLocalRole();
    }

    private void CheckLocalRole()
    {
        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        if (forceInvestigator)
        {
            _isVengefulSpirit = false;
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(false);
            if (investigatorPanel != null)   investigatorPanel.SetActive(true);
            return;
        }

        if (NetworkManager.Singleton == null || CharacterSelectManager.Instance == null)
        {
            _isVengefulSpirit = false;
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(false);
            if (investigatorPanel != null)   investigatorPanel.SetActive(true);
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        ulong vengefulId = CharacterSelectManager.Instance.vengefulSpiritClientId.Value;

        _isVengefulSpirit = (localId == vengefulId && vengefulId != 999);

        if (_isVengefulSpirit)
        {
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(true);
            if (investigatorPanel != null)   investigatorPanel.SetActive(false);

            if (GameManager.Instance != null && GameManager.Instance.girlPrefab != null)
            {
                SwapFeaturedModel(GameManager.Instance.girlPrefab);
            }
        }
        else
        {
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(false);
            if (investigatorPanel != null)   investigatorPanel.SetActive(true);
        }
    }

    // Called by PlayerReadyTracker when the ready snapshot changes
    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        RefreshStatusRows(snapshot);

        if (!_localConfirmed) return;

        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        bool allReady = true;
        foreach (var kvp in snapshot)
            if (!kvp.Value.ready) { allReady = false; break; }

        if (allReady || forceInvestigator)
            GoToSquadScreen();
    }

    private void HandlePlayerLobbyStatesUpdated(Dictionary<ulong, PlayerLobbyInfo> snapshot)
    {
        RefreshLobbyStatusRows(snapshot);

        if (!_localConfirmed) return;

        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        bool allReady = true;
        foreach (var kvp in snapshot)
            if (!kvp.Value.isReady) { allReady = false; break; }

        if (allReady || forceInvestigator)
            GoToSquadScreen();
    }

    public Sprite GetPortraitForCharacter(int characterIndex)
    {
        RefreshFilteredRoster();
        if (_filteredDefinitions != null && characterIndex >= 0 && characterIndex < _filteredDefinitions.Count && _filteredDefinitions[characterIndex] != null)
        {
            return _filteredDefinitions[characterIndex].portrait;
        }
        if (_filteredInlineData != null && characterIndex >= 0 && characterIndex < _filteredInlineData.Count && _filteredInlineData[characterIndex] != null)
        {
            return _filteredInlineData[characterIndex].characterIcon;
        }
        return null;
    }

    private void RefreshLobbyStatusRows(Dictionary<ulong, PlayerLobbyInfo> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
            if (row != null) Destroy(row);
        _statusRows.Clear();

        foreach (var kvp in snapshot)
        {
            PlayerLobbyInfo info = kvp.Value;
            GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
            _statusRows.Add(row);

            HeatPlayerStatusRow heatRow = row.GetComponent<HeatPlayerStatusRow>();
            if (heatRow != null)
            {
                Sprite portrait = info.isGirl ? null : GetPortraitForCharacter(info.characterIndex);
                string rank = CloudCharacterSaveManager.Instance != null
                    ? CloudCharacterSaveManager.Instance.GetPlayerRankTitle(info.playerLevel)
                    : "Recruit";
                heatRow.Setup(info.playerName, info.isReady, info.playerLevel, rank, portrait, info.isGirl);
            }
            else
            {
                var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (texts.Length >= 1) texts[0].text = info.playerName;
                if (texts.Length >= 2)
                {
                    texts[1].text = info.isReady ? statusReadyText : statusWaitingText;
                    texts[1].color = info.isReady
                        ? new Color(0.18f, 0.80f, 0.44f)
                        : new Color(0.91f, 0.30f, 0.24f);
                }
            }
        }
    }

    private void RefreshStatusRows(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        foreach (var row in _statusRows)
            if (row != null) Destroy(row);
        _statusRows.Clear();

        foreach (var kvp in snapshot)
        {
            GameObject row = Instantiate(playerStatusRowPrefab, playerStatusContainer);
            _statusRows.Add(row);

            var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length >= 1) texts[0].text = kvp.Value.name;
            if (texts.Length >= 2)
            {
                texts[1].text = kvp.Value.ready ? statusReadyText : statusWaitingText;
                texts[1].color = kvp.Value.ready
                    ? new Color(0.18f, 0.80f, 0.44f)  // Bright Emerald Green
                    : new Color(0.91f, 0.30f, 0.24f); // Vibrant Crimson Red
            }
        }
    }

    private void GoToSquadScreen()
    {
        if (PlayerReadyTracker.Instance != null)
        {
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
            PlayerReadyTracker.Instance.OnPlayerLobbyStatesUpdated -= HandlePlayerLobbyStatesUpdated;
        }

        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }

        if (investigatorPanel != null) investigatorPanel.SetActive(false);
        if (characterSelectPanel != null) characterSelectPanel.SetActive(false);
        if (playerStatusPanel != null) playerStatusPanel.SetActive(false);

        if (SquadLineupDisplay.Instance != null)
            SquadLineupDisplay.Instance.ShowSquadLineup();
    }

    private void EnsureDefaultCharacterData()
    {
        if (characterDataList != null && characterDataList.Count > 0) return;

        characterDataList = new List<InvestigatorCharacterData>
        {
            new InvestigatorCharacterData
            {
                characterName = "Mine Worker",
                profession = InvestigatorProfession.MineWorker,
                description = "Understands mine structures, heavy machinery, and practical underground navigation.",
                specialAbilities = "• Heavy Pickaxe Attack\n• Structural Inspection\n• Machine Repair"
            },
            new InvestigatorCharacterData
            {
                characterName = "Hazard Specialist",
                profession = InvestigatorProfession.HazardSpecialist,
                description = "Wears a heavy protective suit to handle environmental hazards and toxic gas without panic.",
                specialAbilities = "• Toxic Gas Immunity\n• Hazard Filter Deployment\n• Heavy Armor"
            },
            new InvestigatorCharacterData
            {
                characterName = "Explorer",
                profession = InvestigatorProfession.Explorer,
                description = "Experienced with subterranean mapping, rappelling, and difficult terrain.",
                specialAbilities = "• Stamina Boost\n• Terrain Traversal\n• Flare Marker"
            },
            new InvestigatorCharacterData
            {
                characterName = "Cursed Priest",
                profession = InvestigatorProfession.CursedPriest,
                description = "Supernatural specialist whose unsettling presence makes the team wonder why he joined.",
                specialAbilities = "• Ward Aura\n• Curse Detection\n• Holy Blessing"
            }
        };
    }
}
