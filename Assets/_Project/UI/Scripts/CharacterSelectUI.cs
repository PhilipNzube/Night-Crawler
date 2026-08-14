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

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------

    private int                  _selectedIndex          = 0;
    private bool                 _isVengefulSpirit       = false;
    private GameObject           _currentPreviewInstance;
    private bool                 _initialized            = false;
    private readonly List<Button> _slotCardButtons       = new List<Button>();
    private readonly List<Image>  _slotCardFrames        = new List<Image>();
    private Coroutine            _swapCoroutine;

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
        EnsureDefaultCharacterData();

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.EnableCharacterSelectEnvironment();

        if (!_initialized)
            InitialSetup();

        // Refresh UI state when enabled
        SelectProfession(_selectedIndex);
    }

    void OnDisable()
    {
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();
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

        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.ResetIdleTimer();
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
        }

        UpdateSlotCardHighlights();
    }

    private void UpdateSlotCardHighlights()
    {
        for (int i = 0; i < _slotCardFrames.Count; i++)
        {
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
        if (NetworkManager.Singleton == null || CharacterSelectManager.Instance == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        ulong vengefulId = CharacterSelectManager.Instance.vengefulSpiritClientId.Value;

        _isVengefulSpirit = (localId == vengefulId);

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
        PersistentCharacterSelection.SetSelectedCharacterIndex(_selectedIndex);

        if (!_isVengefulSpirit && CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(_selectedIndex);
        }

        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }

        // Hide slot cards, arrow buttons, and confirm button explicitly
        if (slotCardContainer != null) slotCardContainer.gameObject.SetActive(false);
        if (arrowLeft  != null)        arrowLeft.gameObject.SetActive(false);
        if (arrowRight != null)        arrowRight.gameObject.SetActive(false);
        if (confirmButton != null)     confirmButton.gameObject.SetActive(false);

        if (investigatorPanel != null)
            investigatorPanel.SetActive(false);

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);

        // Transition to Squad Lineup Screen
        if (SquadLineupDisplay.Instance != null)
        {
            SquadLineupDisplay.Instance.ShowSquadLineup();
        }
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
