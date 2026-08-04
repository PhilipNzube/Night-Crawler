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
    private int _selectedIndex = 0;
    private bool _isVengefulSpirit = false;
    private GameObject _currentPreviewInstance;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Start()
    {
        if (confirmButton != null)
            confirmButton.onClick.AddListener(OnConfirmSelection);

        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged += OnRoleSelectionChanged;
        }

        CheckLocalRole();
        SelectProfession(0);
    }

    void OnDestroy()
    {
        if (CharacterSelectManager.Instance != null)
        {
            CharacterSelectManager.Instance.roleSelectionDone.OnValueChanged -= OnRoleSelectionChanged;
        }
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

            if (detailsTitleText != null) detailsTitleText.text = data.characterName;
            if (detailsDescriptionText != null) detailsDescriptionText.text = data.description;
            if (detailsAbilitiesText != null) detailsAbilitiesText.text = data.specialAbilities;

            if (detailsIconImage != null)
            {
                detailsIconImage.sprite = data.characterIcon;
                detailsIconImage.enabled = (data.characterIcon != null);
            }

            // Spawn 3D character preview model if assigned
            UpdateModelPreview(data.characterPrefab);
        }
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
            _currentPreviewInstance = Instantiate(prefabToSpawn, modelPreviewPivot.position, modelPreviewPivot.rotation, modelPreviewPivot);

            // Disable player control scripts on the preview instance (e.g. CharacterController, NetworkBehaviour, inputs)
            // so the preview model stays standing cleanly in place
            MonoBehaviour[] scripts = _currentPreviewInstance.GetComponentsInChildren<MonoBehaviour>();
            foreach (var script in scripts)
            {
                if (!(script is Animator))
                    script.enabled = false;
            }
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
