using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Manages Character Selection UI view, secret Vengeful Spirit role notification,
/// and Investigator profession side-panel details.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [Header("Root Panel")]
    public GameObject characterSelectPanel;

    [Header("Vengeful Spirit Secret View")]
    public GameObject vengefulSpiritPanel;
    public TextMeshProUGUI vengefulSpiritText;

    [Header("Investigator View")]
    public GameObject investigatorPanel;

    [Header("Side Details Panel")]
    public TextMeshProUGUI detailsTitleText;
    public TextMeshProUGUI detailsDescriptionText;
    public TextMeshProUGUI detailsAbilitiesText;
    public Image detailsIconImage;

    [Header("3D Model Preview (Single Slot — used if no Carousel)")]
    [Tooltip("Transform pivot in the scene where a single 3D character model spawns for preview. " +
             "Only used when Carousel is not assigned.")]
    public Transform modelPreviewPivot;

    [Header("Character Carousel (3D Ring — recommended)")]
    [Tooltip("Drag the CharacterCarousel component here. " +
             "When assigned, the carousel handles all character model display. " +
             "modelPreviewPivot is ignored.")]
    public CharacterCarousel carousel;

    [Header("Character Data (Inspector Editable)")]
    [Tooltip("List of character definitions exposed in the Inspector. " +
             "You can edit names, descriptions, abilities, icons, and prefabs directly here. " +
             "Add or remove entries to add/remove characters.")]
    public List<InvestigatorCharacterData> characterDataList = new List<InvestigatorCharacterData>();

    [Header("2D Image Selection (Optional Thumbnail Buttons)")]
    [Tooltip("List of 2D Image buttons for selecting characters via UI icons. " +
             "Clicking button [i] selects character index [i] and rotates the 3D carousel to it.")]
    public List<Button> thumbnailButtons = new List<Button>();

    [Header("Buttons")]
    public Button confirmButton;

    // -------------------------------------------------------------------------
    //  Private State
    // -------------------------------------------------------------------------
    private int         _selectedIndex             = 0;
    private bool        _isVengefulSpirit          = false;
    private GameObject  _currentPreviewInstance;
    private bool        _initialized               = false; // prevents double setup

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
        EnsureDefaultCharacterData();

        // If the panel is disabled at start (normal case — GirlRevealManager
        // enables it after the reveal), skip initial setup. OnEnable will
        // handle setup when the panel is first shown.
        if (characterSelectPanel != null && !characterSelectPanel.activeInHierarchy) return;

        InitialSetup();
    }

    void OnEnable()
    {
        EnsureDefaultCharacterData();

        // Activate the white room environment whenever this panel is shown
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.EnableCharacterSelectEnvironment();

        // If Start() skipped setup (panel was inactive), run it now
        if (!_initialized)
            InitialSetup();
    }

    void OnDisable()
    {
        // Deactivate the white room when the panel is hidden or destroyed
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.DisableCharacterSelectEnvironment();
    }

    void OnDestroy()
    {
        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged -= OnRoleSelectionChanged;
        }
    }

    private void InitialSetup()
    {
        if (_initialized) return;
        _initialized = true;

        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmSelection);

        // Wire 2D image thumbnail buttons
        WireThumbnailButtons();

        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged += OnRoleSelectionChanged;
        }

        CheckLocalRole();

        // Restore last selected character index
        int savedIndex = PersistentCharacterSelection.GetSelectedCharacterIndex();
        SelectProfession(savedIndex);
    }

    private void WireThumbnailButtons()
    {
        if (thumbnailButtons == null) return;
        for (int i = 0; i < thumbnailButtons.Count; i++)
        {
            int index = i;
            if (thumbnailButtons[i] != null)
            {
                thumbnailButtons[i].onClick.RemoveAllListeners();
                thumbnailButtons[i].onClick.AddListener(() => OnThumbnailClicked(index));
            }
        }
    }

    private void OnThumbnailClicked(int index)
    {
        SelectProfession(index);
        if (carousel != null)
            carousel.ScrollToIndex(index);
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void SelectProfession(int index)
    {
        _selectedIndex = index;
        EnsureDefaultCharacterData();

        // Save persistent selection
        PersistentCharacterSelection.SetSelectedCharacterIndex(index);

        InvestigatorCharacterData data = GetCharacterData(index);
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

            // Only spawn a single preview model if there is no carousel.
            // When the carousel is active, it handles all models itself.
            if (carousel == null && data.characterPrefab != null)
                UpdateModelPreview(data.characterPrefab);
        }

        // Reset idle gesture timer
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.ResetIdleTimer();
    }

    /// <summary>
    /// Gets character data by index from characterDataList (Inspector) or CharacterSelectManager.
    /// </summary>
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
    //  3D Preview Spawning
    // =========================================================================
    private void UpdateModelPreview(GameObject prefabToSpawn)
    {
        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }

        if (modelPreviewPivot != null && prefabToSpawn != null)
        {
            _currentPreviewInstance = Instantiate(
                prefabToSpawn, modelPreviewPivot.position,
                modelPreviewPivot.rotation, modelPreviewPivot);

            // Disable player control scripts on the preview instance so the
            // preview model stands cleanly in place
            foreach (var script in _currentPreviewInstance.GetComponentsInChildren<MonoBehaviour>())
            {
                if (script is CharacterAnimationController) continue;
                script.enabled = false;
            }

            // Attach animation controller and start idle
            CharacterAnimationController animCtrl =
                _currentPreviewInstance.GetComponent<CharacterAnimationController>();
            if (animCtrl == null)
                animCtrl = _currentPreviewInstance.AddComponent<CharacterAnimationController>();

            // Character type will be set per-selection; default to Adventurer idle
            animCtrl.characterType = CharacterAnimationController.CharacterType.Adventurer;

            // Inform CharacterSceneController so it can manage the gesture delay timer
            if (CharacterSceneController.Instance != null)
                CharacterSceneController.Instance.NotifyPreviewModelChanged(animCtrl);
        }
        else
        {
            // No model — clear the gesture timer reference
            if (CharacterSceneController.Instance != null)
                CharacterSceneController.Instance.NotifyPreviewModelChanged(null);
        }
    }

    // =========================================================================
    //  Helpers & Handlers
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
            if (investigatorPanel != null) investigatorPanel.SetActive(false);

            if (vengefulSpiritText != null)
            {
                vengefulSpiritText.text = "YOU ARE THE VENGEFUL SPIRIT\n\n" +
                    "Seep into the shadows, manipulate lights, whisper lies, and turn the investigators against each other.";
            }

            // If Vengeful Spirit has a prefab configured on GameManager, preview it
            if (GameManager.Instance != null && GameManager.Instance.girlPrefab != null)
            {
                UpdateModelPreview(GameManager.Instance.girlPrefab);
            }
        }
        else
        {
            if (vengefulSpiritPanel != null) vengefulSpiritPanel.SetActive(false);
            if (investigatorPanel != null) investigatorPanel.SetActive(true);
        }
    }

    private void OnConfirmSelection()
    {
        // If carousel is active, use its focused index as the confirmed selection
        int confirmedIndex = carousel != null ? carousel.GetFocusedIndex() : _selectedIndex;

        // Save selection persistently across scenes
        PersistentCharacterSelection.SetSelectedCharacterIndex(confirmedIndex);

        if (!_isVengefulSpirit && CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(confirmedIndex);
        }

        // Clean up single preview model (if used — no-op when carousel is active)
        if (_currentPreviewInstance != null)
        {
            Destroy(_currentPreviewInstance);
            _currentPreviewInstance = null;
        }

        if (characterSelectPanel != null)
            characterSelectPanel.SetActive(false);

        // Show Call of Duty style squad lineup showcase
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
                specialAbilities = "• Occult Sensing\n• Ward Placement\n• Presence Detection"
            },
            new InvestigatorCharacterData
            {
                characterName = "Field Medic",
                profession = InvestigatorProfession.FieldMedic,
                description = "Examines injuries, heals teammates, and determines if deaths were caused by accidents or violence.",
                specialAbilities = "• First Aid Healing\n• Autopsy Examination\n• Revive Assistance"
            }
        };
    }
}

