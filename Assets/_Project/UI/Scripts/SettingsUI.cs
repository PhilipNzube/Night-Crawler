using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SOLID — SRP: Manages the Settings UI panel (Player Name, Audio Volume).
/// Usable in both the Lobby and the In-Game Pause overlay.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    [Header("Panel Root")]
    [Tooltip("The root settings GameObject panel.")]
    public GameObject settingsPanel;

    [Header("Player Profile")]
    [Tooltip("InputField where players can type their name.")]
    public TMP_InputField nameInputField;

    [Header("Audio Settings")]
    [Tooltip("Slider for Master Audio volume.")]
    public Slider masterVolumeSlider;

    [Tooltip("Slider for Music Volume.")]
    public Slider musicVolumeSlider;

    [Header("Buttons")]
    [Tooltip("Button to close the settings panel.")]
    public Button closeButton;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HideSettings);

        if (nameInputField != null)
            nameInputField.onEndEdit.AddListener(OnNameInputEndEdit);

        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
    }

    void OnEnable()
    {
        RefreshUIValues();
    }

    // =========================================================================
    //  Public API
    // =========================================================================
    public void ShowSettings()
    {
        RefreshUIValues();
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void HideSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    // =========================================================================
    //  Helpers & Handlers
    // =========================================================================
    private void RefreshUIValues()
    {
        if (nameInputField != null)
            nameInputField.text = PlayerNameManager.GetPlayerName();

        if (masterVolumeSlider != null)
            masterVolumeSlider.value = AudioListener.volume;

        if (musicVolumeSlider != null && GameMusicManager.Instance != null)
            musicVolumeSlider.value = GameMusicManager.Instance.bgMaxVolume;
    }

    private void OnNameInputEndEdit(string newName)
    {
        PlayerNameManager.SetPlayerName(newName);
    }

    private void OnMasterVolumeChanged(float val)
    {
        AudioListener.volume = Mathf.Clamp01(val);
    }

    private void OnMusicVolumeChanged(float val)
    {
        if (GameMusicManager.Instance != null)
            GameMusicManager.Instance.bgMaxVolume = Mathf.Clamp01(val);
    }
}
