using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Presenter for the Settings UI panel (Audio, Video/Display, Graphics, Controls, Player Profile).
/// Adapted to seamlessly support SlimUI's UI prefab elements (highlight lines, toggle texts, audio sliders) 
/// as well as standard Unity UI controls without touching any SlimUI code files.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    [Header("Panel Roots")]
    [Tooltip("The root settings GameObject panel.")]
    public GameObject settingsPanel;

    [Header("SlimUI Tab Panels & Highlights")]
    public GameObject panelGame;
    public GameObject panelVideo;
    public GameObject panelControls;
    public GameObject panelKeyBindings;

    public GameObject lineGame;
    public GameObject lineVideo;
    public GameObject lineControls;
    public GameObject lineKeyBindings;

    [Header("SlimUI Tab Buttons (Optional)")]
    public Button gameTabButton;
    public Button videoTabButton;
    public Button controlsTabButton;
    public Button keyBindingsTabButton;

    [Header("Player Profile")]
    public TMP_InputField nameInputField;

    [Header("Audio Settings & Sliders")]
    public Slider masterVolumeSlider;
    public TMP_Text masterVolumeText;

    public Slider musicVolumeSlider;
    public TMP_Text musicVolumeText;

    public Slider sfxVolumeSlider;
    public TMP_Text sfxVolumeText;

    [Header("SlimUI Video Settings (Texts & Lines)")]
    public TMP_Text fullscreentext;
    public TMP_Text vsynctext;
    public GameObject shadowofftextLINE;
    public GameObject shadowlowtextLINE;
    public GameObject shadowhightextLINE;

    public GameObject aaofftextLINE;
    public GameObject aa2xtextLINE;
    public GameObject aa4xtextLINE;
    public GameObject aa8xtextLINE;

    public GameObject texturelowtextLINE;
    public GameObject texturemedtextLINE;
    public GameObject texturehightextLINE;

    [Header("SlimUI Controls (Texts & Sliders)")]
    public TMP_Text invertmousetext;
    public Slider sensitivityXSlider;
    public Slider sensitivityYSlider;
    public TMP_Text sensitivityText;

    [Header("Standard UI Controls (Dropdown Fallbacks)")]
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown displayModeDropdown;
    public TMP_Dropdown vsyncDropdown;
    public TMP_Dropdown targetFpsDropdown;
    public TMP_Dropdown qualityPresetDropdown;
    public TMP_Dropdown shadowsDropdown;
    public TMP_Dropdown antiAliasingDropdown;
    public TMP_Dropdown textureQualityDropdown;
    public TMP_Dropdown anisotropicDropdown;

    [Header("Action Buttons")]
    public Button applyButton;
    public Button defaultButton;
    public Button closeButton;

    // Helper flag for PauseManager
    public bool IsSettingsOpen => settingsPanel != null && settingsPanel.activeSelf;

    private List<Resolution> _filteredResolutions = new List<Resolution>();

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================
    private void Awake()
    {
        BindUIEvents();
    }

    private void OnEnable()
    {
        PopulateResolutionDropdown();
        PopulateQualityDropdown();
        RefreshUIValues();
        SelectTab(0); // Default to Video tab
    }

    // =========================================================================
    //  Public API — Tab Navigation (SlimUI Compatible)
    // =========================================================================
    public void ShowSettings()
    {
        PopulateResolutionDropdown();
        PopulateQualityDropdown();
        RefreshUIValues();
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void HideSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void GamePanel()     => SelectTab(0);
    public void VideoPanel()    => SelectTab(1);
    public void ControlsPanel() => SelectTab(2);
    public void KeyBindingsPanel() => SelectTab(3);

    public void SelectTab(int tabIndex)
    {
        // Panels
        if (panelGame)        panelGame.SetActive(tabIndex == 0);
        if (panelVideo)       panelVideo.SetActive(tabIndex == 1);
        if (panelControls)    panelControls.SetActive(tabIndex == 2);
        if (panelKeyBindings) panelKeyBindings.SetActive(tabIndex == 3);

        // Highlight lines
        if (lineGame)        lineGame.SetActive(tabIndex == 0);
        if (lineVideo)       lineVideo.SetActive(tabIndex == 1);
        if (lineControls)    lineControls.SetActive(tabIndex == 2);
        if (lineKeyBindings) lineKeyBindings.SetActive(tabIndex == 3);
    }

    // =========================================================================
    //  SlimUI Quick Toggles & Line Setters (Callable from SlimUI UI Buttons)
    // =========================================================================
    public void FullScreen()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm == null) return;

        gsm.displayMode = (gsm.displayMode == 0) ? 2 : 0; // Toggle between Fullscreen Windowed & Windowed
        gsm.ApplySettings();
        gsm.SaveSettings();

        UpdateFullscreenUI();
    }

    public void vsync()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm == null) return;

        gsm.vSync = (gsm.vSync == 0) ? 1 : 0;
        gsm.ApplySettings();
        gsm.SaveSettings();

        UpdateVSyncUI();
    }

    public void InvertMouse()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm == null) return;

        gsm.invertYAxis = !gsm.invertYAxis;
        gsm.SaveSettings();

        UpdateInvertMouseUI();
    }

    public void ShadowsOff()  => SetShadowLevel(0);
    public void ShadowsLow()  => SetShadowLevel(1);
    public void ShadowsHigh() => SetShadowLevel(2);

    private void SetShadowLevel(int level)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null)
        {
            gsm.shadowQuality = level;
            gsm.ApplySettings();
            gsm.SaveSettings();
        }
        UpdateShadowUI(level);
    }

    public void TexturesLow()  => SetTextureLevel(2); // Quarter
    public void TexturesMed()  => SetTextureLevel(1); // Half
    public void TexturesHigh() => SetTextureLevel(0); // Full

    private void SetTextureLevel(int level)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null)
        {
            gsm.textureQuality = level;
            gsm.ApplySettings();
            gsm.SaveSettings();
        }
        UpdateTextureUI(level);
    }

    public void AAOff() => SetAALevel(0);
    public void AA2x()  => SetAALevel(2);
    public void AA4x()  => SetAALevel(4);
    public void AA8x()  => SetAALevel(8);

    private void SetAALevel(int aaValue)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null)
        {
            gsm.antiAliasing = aaValue;
            gsm.ApplySettings();
            gsm.SaveSettings();
        }
        UpdateAntiAliasingUI(aaValue);
    }

    // =========================================================================
    //  UI Bindings & Setup
    // =========================================================================
    private void BindUIEvents()
    {
        // Tab Buttons
        if (gameTabButton != null)        gameTabButton.onClick.AddListener(GamePanel);
        if (videoTabButton != null)       videoTabButton.onClick.AddListener(VideoPanel);
        if (controlsTabButton != null)    controlsTabButton.onClick.AddListener(ControlsPanel);
        if (keyBindingsTabButton != null) keyBindingsTabButton.onClick.AddListener(KeyBindingsPanel);

        // Action Buttons
        if (applyButton != null)   applyButton.onClick.AddListener(OnApplyPressed);
        if (defaultButton != null) defaultButton.onClick.AddListener(OnResetDefaultsPressed);
        if (closeButton != null)   closeButton.onClick.AddListener(HideSettings);

        // Player Name
        if (nameInputField != null)
            nameInputField.onEndEdit.AddListener(OnNameInputEndEdit);

        // Audio Sliders (live update)
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(v => {
                if (masterVolumeText != null) masterVolumeText.text = $"{Mathf.RoundToInt(v * 100)}%";
                if (GameSettingsManager.Instance != null) GameSettingsManager.Instance.masterVolume = v;
            });
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(v => {
                if (musicVolumeText != null) musicVolumeText.text = $"{Mathf.RoundToInt(v * 100)}%";
                if (GameSettingsManager.Instance != null) GameSettingsManager.Instance.musicVolume = v;
            });
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(v => {
                if (sfxVolumeText != null) sfxVolumeText.text = $"{Mathf.RoundToInt(v * 100)}%";
                if (GameSettingsManager.Instance != null) GameSettingsManager.Instance.sfxVolume = v;
            });
        }

        // Sensitivity Sliders
        if (sensitivityXSlider != null)
        {
            sensitivityXSlider.onValueChanged.AddListener(v => {
                if (sensitivityText != null) sensitivityText.text = $"{v:F1}x";
                if (GameSettingsManager.Instance != null) GameSettingsManager.Instance.mouseSensitivity = v;
            });
        }

        if (sensitivityYSlider != null && sensitivityXSlider == null)
        {
            sensitivityYSlider.onValueChanged.AddListener(v => {
                if (sensitivityText != null) sensitivityText.text = $"{v:F1}x";
                if (GameSettingsManager.Instance != null) GameSettingsManager.Instance.mouseSensitivity = v;
            });
        }
    }

    private void PopulateResolutionDropdown()
    {
        if (resolutionDropdown == null) return;

        resolutionDropdown.ClearOptions();
        _filteredResolutions.Clear();

        Resolution[] allResolutions = Screen.resolutions;
        List<string> options = new List<string>();
        int currentResIndex = 0;

        int currentW = Screen.width;
        int currentH = Screen.height;

        if (GameSettingsManager.Instance != null)
        {
            currentW = GameSettingsManager.Instance.resolutionWidth;
            currentH = GameSettingsManager.Instance.resolutionHeight;
        }

        HashSet<string> addedStr = new HashSet<string>();

        for (int i = 0; i < allResolutions.Length; i++)
        {
            Resolution res = allResolutions[i];
            int refresh = (int)res.refreshRateRatio.value > 0 ? (int)res.refreshRateRatio.value : 60;
            string optionStr = $"{res.width} x {res.height} @ {refresh}Hz";

            if (!addedStr.Contains(optionStr))
            {
                addedStr.Add(optionStr);
                _filteredResolutions.Add(res);
                options.Add(optionStr);

                if (res.width == currentW && res.height == currentH)
                {
                    currentResIndex = options.Count - 1;
                }
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentResIndex;
        resolutionDropdown.RefreshShownValue();
    }

    private void PopulateQualityDropdown()
    {
        if (qualityPresetDropdown == null) return;

        qualityPresetDropdown.ClearOptions();
        List<string> options = new List<string>(QualitySettings.names);
        qualityPresetDropdown.AddOptions(options);

        if (GameSettingsManager.Instance != null)
        {
            qualityPresetDropdown.value = GameSettingsManager.Instance.qualityLevel;
        }
        qualityPresetDropdown.RefreshShownValue();
    }

    public void RefreshUIValues()
    {
        // Player Profile Name
        if (nameInputField != null)
            nameInputField.text = PlayerNameManager.GetPlayerName();

        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm == null) return;

        // Audio Sliders
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = gsm.masterVolume;
            if (masterVolumeText != null) masterVolumeText.text = $"{Mathf.RoundToInt(gsm.masterVolume * 100)}%";
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = gsm.musicVolume;
            if (musicVolumeText != null) musicVolumeText.text = $"{Mathf.RoundToInt(gsm.musicVolume * 100)}%";
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = gsm.sfxVolume;
            if (sfxVolumeText != null) sfxVolumeText.text = $"{Mathf.RoundToInt(gsm.sfxVolume * 100)}%";
        }

        // Sensitivity
        if (sensitivityXSlider != null)
        {
            sensitivityXSlider.value = gsm.mouseSensitivity;
            if (sensitivityText != null) sensitivityText.text = $"{gsm.mouseSensitivity:F1}x";
        }
        if (sensitivityYSlider != null && sensitivityXSlider == null)
        {
            sensitivityYSlider.value = gsm.mouseSensitivity;
            if (sensitivityText != null) sensitivityText.text = $"{gsm.mouseSensitivity:F1}x";
        }

        // Update SlimUI Specific UI Indicators
        UpdateFullscreenUI();
        UpdateVSyncUI();
        UpdateInvertMouseUI();
        UpdateShadowUI(gsm.shadowQuality);
        UpdateTextureUI(gsm.textureQuality);
        UpdateAntiAliasingUI(gsm.antiAliasing);

        // Standard Dropdowns
        if (displayModeDropdown != null) displayModeDropdown.value = gsm.displayMode;
        if (vsyncDropdown != null)       vsyncDropdown.value       = gsm.vSync;
        if (qualityPresetDropdown != null) qualityPresetDropdown.value = gsm.qualityLevel;
        if (shadowsDropdown != null)        shadowsDropdown.value       = gsm.shadowQuality;
        if (textureQualityDropdown != null) textureQualityDropdown.value = gsm.textureQuality;
    }

    // =========================================================================
    //  UI Visual State Helpers (SlimUI Compatible)
    // =========================================================================
    private void UpdateFullscreenUI()
    {
        if (fullscreentext != null)
        {
            bool isFull = Screen.fullScreen || (GameSettingsManager.Instance != null && GameSettingsManager.Instance.displayMode != 2);
            fullscreentext.text = isFull ? "on" : "off";
        }
    }

    private void UpdateVSyncUI()
    {
        if (vsynctext != null)
        {
            bool isVsync = QualitySettings.vSyncCount > 0 || (GameSettingsManager.Instance != null && GameSettingsManager.Instance.vSync == 1);
            vsynctext.text = isVsync ? "on" : "off";
        }
    }

    private void UpdateInvertMouseUI()
    {
        if (invertmousetext != null)
        {
            bool inv = GameSettingsManager.Instance != null && GameSettingsManager.Instance.invertYAxis;
            invertmousetext.text = inv ? "on" : "off";
        }
    }

    private void UpdateShadowUI(int level)
    {
        if (shadowofftextLINE)  shadowofftextLINE.SetActive(level == 0);
        if (shadowlowtextLINE)  shadowlowtextLINE.SetActive(level == 1);
        if (shadowhightextLINE) shadowhightextLINE.SetActive(level == 2);
    }

    private void UpdateTextureUI(int level)
    {
        // 0 = High (Full), 1 = Med (Half), 2 = Low (Quarter)
        if (texturelowtextLINE)  texturelowtextLINE.SetActive(level == 2);
        if (texturemedtextLINE)  texturemedtextLINE.SetActive(level == 1);
        if (texturehightextLINE) texturehightextLINE.SetActive(level == 0);
    }

    private void UpdateAntiAliasingUI(int aaValue)
    {
        if (aaofftextLINE) aaofftextLINE.SetActive(aaValue == 0);
        if (aa2xtextLINE)  aa2xtextLINE.SetActive(aaValue == 2);
        if (aa4xtextLINE)  aa4xtextLINE.SetActive(aaValue == 4);
        if (aa8xtextLINE)  aa8xtextLINE.SetActive(aaValue == 8);
    }

    // =========================================================================
    //  Event Handlers
    // =========================================================================
    private void OnNameInputEndEdit(string newName)
    {
        PlayerNameManager.SetPlayerName(newName);
    }

    private void OnApplyPressed()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm == null) return;

        // Save & Apply globally
        gsm.SaveSettings();
        gsm.ApplySettings();

        Debug.Log("[SettingsUI] Settings applied and saved successfully.");
    }

    private void OnResetDefaultsPressed()
    {
        if (GameSettingsManager.Instance != null)
        {
            GameSettingsManager.Instance.ResetToDefaults();
            RefreshUIValues();
            PopulateResolutionDropdown();
            PopulateQualityDropdown();
        }
    }
}
