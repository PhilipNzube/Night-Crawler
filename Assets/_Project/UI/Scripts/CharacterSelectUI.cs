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

    [Header("3D Model Preview")]
    [Tooltip("Transform pivot in the scene where 3D character models spawn for preview.")]
    public Transform modelPreviewPivot;

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
        // If the panel is disabled at start (normal case — GirlRevealManager
        // enables it after the reveal), skip initial setup. OnEnable will
        // handle setup when the panel is first shown.
        if (characterSelectPanel != null && !characterSelectPanel.activeInHierarchy) return;

        InitialSetup();
    }

    void OnEnable()
    {
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

        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged += OnRoleSelectionChanged;
        }

        CheckLocalRole();
        SelectProfession(0);
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void SelectProfession(int index)
    {
        _selectedIndex = index;

        if (CharacterSelectManager.Instance == null) return;
        var chars = CharacterSelectManager.Instance.availableCharacters;

        if (chars != null && index >= 0 && index < chars.Count)
        {
            var data = chars[index];

            if (detailsTitleText       != null) detailsTitleText.text       = data.characterName;
            if (detailsDescriptionText != null) detailsDescriptionText.text = data.description;
            if (detailsAbilitiesText   != null) detailsAbilitiesText.text   = data.specialAbilities;

            if (detailsIconImage != null)
            {
                detailsIconImage.sprite  = data.characterIcon;
                detailsIconImage.enabled = (data.characterIcon != null);
            }

            // Spawn 3D character preview model if assigned
            UpdateModelPreview(data.characterPrefab);
        }

        // Reset idle gesture timer — user is actively browsing characters
        if (CharacterSceneController.Instance != null)
            CharacterSceneController.Instance.ResetIdleTimer();
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
                vengefulSpiritText.text = "YOU ARE THE VENGEFUL SPIRIT 💀\n\n" +
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
        if (!_isVengefulSpirit && CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.RequestSelectCharacterServerRpc(_selectedIndex);
        }

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
}
