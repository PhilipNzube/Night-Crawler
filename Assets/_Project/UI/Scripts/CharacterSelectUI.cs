using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages Character Selection UI view inside the InvestigatorFlow panel.
/// Now features Naruto/FighterZ-style Slot Card selection (horizontal card row + featured 3D model).
///
/// Fully integrated with:
///   • GirlRevealManager (post-reveal routing)
///   • CharacterSceneController (white room environment setup & camera)
///   • CharacterSelectManager (server RPC sync & role selection)
///   • SquadLineupDisplay (transitions to Squad Screen on confirm)
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
    public TextMeshProUGUI vengefulSpiritText;

    [Header("Investigator View")]
    public GameObject investigatorPanel;

    // =========================================================================
    //  Inspector — Featured 3D Model Stage
    // =========================================================================

    [Header("3D Featured Model Stage")]
    [Tooltip("Transform pivot in the scene where the featured 3D character model spawns. " +
             "Position this centered in front of the character select camera.")]
    public Transform modelPreviewPivot;

    [Tooltip("Duration in seconds to animate model swap (fade/scale).")]
    public float modelSwapDuration = 0.35f;

    // =========================================================================
    //  Inspector — Naruto-Style 2D Slot Card Row
    // =========================================================================

    [Header("Naruto-Style Slot Cards (2D Card Row)")]
    [Tooltip("Parent Transform under which character slot cards are spawned. " +
             "Use a UI GameObject with a Horizontal Layout Group component.")]
    public Transform slotCardContainer;

    [Tooltip("Prefab for a single slot card (UI Button with Image child for icon and TMP_Text child for name).")]
    public GameObject slotCardPrefab;

    [Tooltip("Color applied to the frame/border of the selected slot card.")]
    public Color selectedCardHighlightColor = new Color(1f, 0.85f, 0.2f);

    [Tooltip("Color applied to unselected slot cards.")]
    public Color unselectedCardColor = Color.white;

    // =========================================================================
    //  Inspector — Info & Stats Panel
    // =========================================================================

    [Header("Side Details Panel")]
    public TextMeshProUGUI detailsTitleText;
    public TextMeshProUGUI detailsDescriptionText;
    public TextMeshProUGUI detailsAbilitiesText;
    public Image detailsIconImage;
    [Tooltip("Optional parent root for character details/abilities panel to hide on confirm.")]
    public GameObject sideDetailsPanel;

    [Header("Stats Bars (Optional)")]
    public Slider speedBar;
    public Slider strengthBar;
    public Slider stealthBar;

    // =========================================================================
    //  Inspector — Navigation & Action Buttons
    // =========================================================================

    [Header("Navigation Buttons")]
    public Button arrowLeft;
    public Button arrowRight;

    [Header("Action Buttons")]
    public Button confirmButton;

    // =========================================================================
    //  Inspector — Character Data (ScriptableObjects & Inline List)
    // =========================================================================

    [Header("Character Roster (ScriptableObjects — Recommended)")]
    [Tooltip("Drag your CharacterDefinitionSO assets here. " +
             "Create them via: Right-click → Create → Night Crawler → Character Definition.")]
    public List<CharacterDefinitionSO> characterDefinitions = new List<CharacterDefinitionSO>();

    [Header("Inline Character Data (Fallback / Inspector Editable)")]
    [Tooltip("Used if characterDefinitions SO list above is empty.")]
    public List<InvestigatorCharacterData> characterDataList = new List<InvestigatorCharacterData>();

    [Header("Player Status Panel (Ready / Waiting)")]
    [Tooltip("Root panel shown after the player confirms selection. Shows every player's name and ready status.")]
    public GameObject playerStatusPanel;
    [Tooltip("Vertical container inside playerStatusPanel where per-player rows are spawned.")]
    public Transform playerStatusContainer;
    [Tooltip("Prefab for a single status row: must have two TMP_Text children — [0]=name, [1]=status.")]
    public GameObject playerStatusRowPrefab;
    [Tooltip("Text shown in the status row when waiting / not ready.")]
    public string statusWaitingText = "NOT READY";
    [Tooltip("Text shown in the status row when ready.")]
    public string statusReadyText = "READY";

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
    private Coroutine                 _swapCoroutine;
    private bool                      _localConfirmed         = false; // waiting for everyone else
    private readonly List<GameObject> _statusRows             = new List<GameObject>();

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    void Start()
    {
        EnsureDefaultCharacterData();

        if (characterSelectPanel != null && !characterSelectPanel.activeInHierarchy) return;

        InitialSetup();
    }

    void OnEnable()
    {
        _localConfirmed = false;
        if (playerStatusPanel != null) playerStatusPanel.SetActive(false);

        // Reset visibility of selection controls and abilities
        if (slotCardContainer != null) slotCardContainer.gameObject.SetActive(true);
        if (arrowLeft  != null)        arrowLeft.gameObject.SetActive(true);
        if (arrowRight != null)        arrowRight.gameObject.SetActive(true);
        if (confirmButton != null)     confirmButton.gameObject.SetActive(true);
        if (detailsAbilitiesText != null)   detailsAbilitiesText.gameObject.SetActive(true);
        if (detailsDescriptionText != null) detailsDescriptionText.gameObject.SetActive(true);
        if (sideDetailsPanel != null)       sideDetailsPanel.SetActive(true);

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
            PlayerReadyTracker.Instance.OnReadyStatesUpdated += HandleReadyStatesUpdated;
    }

    void OnDisable()
    {
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();

        // Unsubscribe to avoid stale callbacks
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;
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

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmSelection);

        if (arrowLeft  != null) arrowLeft.onClick.AddListener(SelectPrevious);
        if (arrowRight != null) arrowRight.onClick.AddListener(SelectNext);

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
    //  Public API & Slot Navigation
    // =========================================================================

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
        if (characterDefinitions != null && characterDefinitions.Count > 0)
            return characterDefinitions.Count;
        return characterDataList != null ? characterDataList.Count : 0;
    }

    public void SelectProfession(int index)
    {
        int count = GetTotalCharacterCount();
        if (count == 0) return;
        _selectedIndex = Mathf.Clamp(index, 0, count - 1);

        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);
        PersistentCharacterSelection.SetIsVengefulSpirit(false);

        // 1. Check ScriptableObjects list first
        if (characterDefinitions != null && _selectedIndex < characterDefinitions.Count && characterDefinitions[_selectedIndex] != null)
        {
            CharacterDefinitionSO so = characterDefinitions[_selectedIndex];

            if (detailsTitleText       != null) detailsTitleText.text       = so.characterName;
            if (detailsDescriptionText != null) detailsDescriptionText.text = so.description;
            if (detailsAbilitiesText   != null) detailsAbilitiesText.text   = so.abilityDescriptions;

            if (detailsIconImage != null)
            {
                detailsIconImage.sprite  = so.portrait;
                detailsIconImage.enabled = (so.portrait != null);
            }

            if (speedBar    != null) speedBar.value    = so.speed / 10f;
            if (strengthBar != null) strengthBar.value = so.strength / 10f;
            if (stealthBar  != null) stealthBar.value  = so.stealth / 10f;

            if (so.characterPrefab != null)
                SwapFeaturedModel(so.characterPrefab);
        }
        else
        {
            // 2. Fallback to inline characterDataList
            InvestigatorCharacterData data = GetCharacterData(_selectedIndex);
            if (data != null)
            {
                if (detailsTitleText       != null) detailsTitleText.text       = data.characterName;
                if (detailsDescriptionText != null) detailsDescriptionText.text = data.description;
                if (detailsAbilitiesText   != null) detailsAbilitiesText.text   = data.specialAbilities;

                if (detailsIconImage != null)
                {
                    detailsIconImage.sprite  = data.characterIcon;
                    detailsIconImage.enabled = (data.characterIcon != null);
                }

                if (speedBar    != null) speedBar.value    = 0.7f;
                if (strengthBar != null) strengthBar.value = 0.6f;
                if (stealthBar  != null) stealthBar.value  = 0.5f;

                if (data.characterPrefab != null)
                    SwapFeaturedModel(data.characterPrefab);
            }
        }

        UpdateSlotCardHighlights();

        SyncSelectionToServer(_selectedIndex);

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.ResetIdleTimer();
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
        if (characterDataList != null && index >= 0 && index < characterDataList.Count)
            return characterDataList[index];

        if (CharacterSelectManager.Instance != null &&
            CharacterSelectManager.Instance.availableCharacters != null &&
            index >= 0 && index < CharacterSelectManager.Instance.availableCharacters.Count)
        {
            return CharacterSelectManager.Instance.availableCharacters[index];
        }

        return null;
    }

    // =========================================================================
    //  Naruto-Style Slot Cards Building
    // =========================================================================

    private void BuildSlotCards()
    {
        if (slotCardContainer == null || slotCardPrefab == null) return;

        foreach (Transform child in slotCardContainer)
            Destroy(child.gameObject);

        _slotCardButtons.Clear();
        _slotCardFrames.Clear();
        _slotCards.Clear();

        bool useSO = characterDefinitions != null && characterDefinitions.Count > 0;
        int count = GetTotalCharacterCount();

        for (int i = 0; i < count; i++)
        {
            int capturedIndex = i;
            string charName = "";
            Sprite portrait = null;

            if (useSO && i < characterDefinitions.Count && characterDefinitions[i] != null)
            {
                charName = characterDefinitions[i].characterName;
                portrait = characterDefinitions[i].portrait;
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

            // Set icon sprite
            Image portraitImg = card.GetComponentInChildren<Image>();
            if (portraitImg != null && portrait != null)
                portraitImg.sprite = portrait;

            // Set name label
            TMP_Text nameLabel = card.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null)
                nameLabel.text = charName;

            // Wire button click
            Button btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => SelectProfession(capturedIndex));
                _slotCardButtons.Add(btn);
                _slotCardFrames.Add(btn.GetComponent<Image>());
            }

            // Register the CharacterSlotCard component (adds hover/select animations)
            CharacterSlotCard slotCard = card.GetComponent<CharacterSlotCard>();
            _slotCards.Add(slotCard); // null-safe — UpdateSlotCardHighlights checks for null
        }

        UpdateSlotCardHighlights();
    }

    private void UpdateSlotCardHighlights()
    {
        // CharacterSlotCard (animated version)
        for (int i = 0; i < _slotCards.Count; i++)
        {
            if (_slotCards[i] != null)
                _slotCards[i].SetSelected(i == _selectedIndex);
        }

        // Fallback: raw Image tinting for cards that don't have CharacterSlotCard attached
        for (int i = 0; i < _slotCardFrames.Count; i++)
        {
            if (i < _slotCards.Count && _slotCards[i] != null) continue; // already handled above
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

        // Disable player control scripts on the preview instance
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
    //  Role Handling & Confirmation
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
            if (investigatorPanel != null)     investigatorPanel.SetActive(false);

            if (vengefulSpiritText != null)
            {
                vengefulSpiritText.text = "YOU ARE THE VENGEFUL SPIRIT\n\n" +
                    "Seep into the shadows, manipulate lights, whisper lies, and turn the investigators against each other.";
            }

            if (GameManager.Instance != null && GameManager.Instance.girlPrefab != null)
            {
                SwapFeaturedModel(GameManager.Instance.girlPrefab);
            }
        }
        else
        {
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(false);
            if (investigatorPanel != null)     investigatorPanel.SetActive(true);
        }
    }

    private void OnConfirmSelection()
    {
        if (_localConfirmed) return; // don't double-confirm
        _localConfirmed = true;

        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);

        if (!_isVengefulSpirit && CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(_selectedIndex);
        }

        // Keep the featured 3D character model VISIBLE on stage (do not destroy it here).
        // It stays on display while waiting for others, matching the girl's screen behavior.

        // Hide selection controls & ready text — player has locked in
        if (slotCardContainer != null) slotCardContainer.gameObject.SetActive(false);
        if (arrowLeft  != null)        arrowLeft.gameObject.SetActive(false);
        if (arrowRight != null)        arrowRight.gameObject.SetActive(false);
        if (confirmButton != null)     confirmButton.gameObject.SetActive(false);

        // Disable character abilities and details text/panel
        if (detailsAbilitiesText != null)   detailsAbilitiesText.gameObject.SetActive(false);
        if (detailsDescriptionText != null) detailsDescriptionText.gameObject.SetActive(false);
        if (sideDetailsPanel != null)       sideDetailsPanel.SetActive(false);

        // Notify the ready tracker (server will tell everyone when all are done)
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.ReportInvestigatorConfirmed(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);

        // Show the waiting-for-others panel immediately
        if (playerStatusPanel != null)
            playerStatusPanel.SetActive(true);

        // If forceInvestigatorMode is active or tracker is null, auto-continue straight to squad screen
        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        if (forceInvestigator || PlayerReadyTracker.Instance == null)
            GoToSquadScreen();
    }

    // Called by PlayerReadyTracker when the ready snapshot changes
    private void HandleReadyStatesUpdated(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        RefreshStatusRows(snapshot);

        // Only transition once this local player has confirmed too
        if (!_localConfirmed) return;

        bool forceInvestigator = GirlRevealManager.Instance != null && GirlRevealManager.Instance.forceInvestigatorMode;
        bool allReady = true;
        foreach (var kvp in snapshot)
            if (!kvp.Value.ready) { allReady = false; break; }

        if (allReady || forceInvestigator)
            GoToSquadScreen();
    }

    private void RefreshStatusRows(Dictionary<ulong, (string name, bool ready)> snapshot)
    {
        if (playerStatusContainer == null || playerStatusRowPrefab == null) return;

        // Destroy old rows
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
        // Unsubscribe before switching
        if (PlayerReadyTracker.Instance != null)
            PlayerReadyTracker.Instance.OnReadyStatesUpdated -= HandleReadyStatesUpdated;

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
