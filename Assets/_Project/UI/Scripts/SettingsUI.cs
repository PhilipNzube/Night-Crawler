using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// SOLID — SRP: Settings UI Presenter, fully adapted to SlimUI Modern Menu 1.
///
/// ───────────────────────────────────────────────────────────────────────────
/// DESIGN PRINCIPLE
/// ───────────────────────────────────────────────────────────────────────────
/// Every field in this script maps 1-to-1 to a real object that EXISTS in
/// the SlimUI Canvas_DefaultTemplate1 prefab hierarchy. No phantom fields.
///
/// HOW SLIMUI SETTINGS WORKS (read before wiring):
///   SlimUI's UISettingsManager stores references to GameObjects (not Slider
///   components directly). Each slider is a child GameObject with a Slider
///   component on it. The text labels are child TMP_Text GameObjects.
///   All toggle states are shown/hidden via active GameObjects called "LINE"
///   indicators (e.g. shadowofftextLINE, texturelowtextLINE).
///
/// WHAT SLIMUI HAS (and what to drag here):
///   Slider-type settings:
///     • musicSlider         — the Slider on the Music Volume slider GameObject
///     • sensitivityXSlider  — the Slider on the X Sensitivity slider GameObject
///     • sensitivityYSlider  — the Slider on the Y Sensitivity slider GameObject
///     • mouseSmoothSlider   — the Slider on the Mouse Smoothing slider GameObject
///
///   Toggle-type settings (on/off text labels):
///     • fullscreentext      — TMP_Text child of the fullscreen toggle button
///     • vsynctext           — TMP_Text child of the vsync toggle button
///     • invertmousetext     — TMP_Text child of the invert mouse toggle
///     • motionblurtext      — TMP_Text child of motion blur toggle
///     • ambientocclusiontext — TMP_Text child of AO toggle
///
///   Line indicators (active = selected, inactive = not selected):
///     • shadowofftextLINE / shadowlowtextLINE / shadowhightextLINE
///     • aaofftextLINE / aa2xtextLINE / aa4xtextLINE / aa8xtextLINE
///     • texturelowtextLINE / texturemedtextLINE / texturehightextLINE
///
///   Tab panels + highlights (from UIMenuManager):
///     • PanelGame / PanelVideo / PanelControls / PanelKeyBindings
///     • lineGame / lineVideo / lineControls / lineKeyBindings
///
///   Navigation:
///     • slimUIAnimator      — the Animator on the SlimUI root (drives "Animate" float)
///     • slimUIMenuManager   — UIMenuManager component on the SlimUI root
///       (used to call ReturnMenu() and Position1() for the back button)
///
/// WHAT SLIMUI DOES NOT HAVE (removed from this script):
///   - masterVolumeSlider (no master volume slider in SlimUI — use AudioListener)
///   - sfxVolumeSlider (no SFX volume slider in SlimUI — add one manually if needed)
///   - resolutionDropdown, displayModeDropdown, vsyncDropdown, targetFpsDropdown,
///     qualityPresetDropdown, shadowsDropdown, antiAliasingDropdown,
///     textureQualityDropdown, anisotropicDropdown (none of these exist in SlimUI)
///   - nameInputField (no player profile name field in SlimUI)
///   - applyButton / defaultButton (no apply/reset buttons in SlimUI)
///
/// ───────────────────────────────────────────────────────────────────────────
/// INSTRUCTIONS FOR THINGS YOU NEED TO ADD IN THE INSPECTOR (read these)
/// ───────────────────────────────────────────────────────────────────────────
/// ► sfxVolumeSlider: SlimUI does NOT ship with an SFX slider. If you want one:
///   1. Duplicate the musicSlider row inside SlimUI's Game or Controls panel.
///   2. Label it "SFX Volume".
///   3. Drag the Slider component of that new row into the sfxVolumeSlider field.
///   (If you skip this, leave the field empty — nothing will break.)
///
/// ► masterVolumeSlider: Same as above. SlimUI has no master volume slider.
///   If you want one, add it the same way and drag into masterVolumeSlider.
///   (If you skip this, AudioListener.volume is still controlled via GameSettingsManager.)
/// </summary>
public class SettingsUI : MonoBehaviour
{
    // =========================================================================
    //  Settings Panel Root
    // =========================================================================

    [Header("Settings Panel Root")]
    [Tooltip("The root GameObject of the settings panel shown/hidden as a whole. " +
             "In SlimUI, this is the same as 'mainMenu' or the settings sub-panel root " +
             "that becomes active when settings are open.")]
    public GameObject settingsPanel;

    // =========================================================================
    //  Back Navigation — handled via PauseUI (no SlimUI reference needed here)
    // =========================================================================
    // PauseUI.CloseSettings() already owns the SlimUI animator, firstMenu,
    // and swoosh SFX. SettingsUI just calls it — zero SlimUI coupling.

    // =========================================================================
    //  SlimUI Tab Panels & Highlights (same names as in UIMenuManager)
    // =========================================================================

    [Header("SlimUI Tab Panels — drag from the SlimUI hierarchy")]
    [Tooltip("PanelGame — the GAME settings tab panel GameObject.")]
    public GameObject PanelGame;

    [Tooltip("PanelVideo — the VIDEO settings tab panel GameObject.")]
    public GameObject PanelVideo;

    [Tooltip("PanelControls — the CONTROLS settings tab panel GameObject.")]
    public GameObject PanelControls;

    [Tooltip("PanelKeyBindings — the KEY BINDINGS tab panel GameObject.")]
    public GameObject PanelKeyBindings;

    [Header("SlimUI Key Bindings Sub-Panels")]
    public GameObject PanelMovement;
    public GameObject PanelCombat;
    public GameObject PanelGeneral;

    [Header("SlimUI Tab Highlight Lines")]
    [Tooltip("lineGame — the active indicator line under the GAME tab.")]
    public GameObject lineGame;

    [Tooltip("lineVideo — the active indicator line under the VIDEO tab.")]
    public GameObject lineVideo;

    [Tooltip("lineControls — the active indicator line under the CONTROLS tab.")]
    public GameObject lineControls;

    [Tooltip("lineKeyBindings — the active indicator line under KEY BINDINGS tab.")]
    public GameObject lineKeyBindings;

    [Header("SlimUI Key Bindings Sub-Tab Highlight Lines")]
    public GameObject lineMovement;
    public GameObject lineCombat;
    public GameObject lineGeneral;

    // =========================================================================
    //  SlimUI Audio Sliders (the ones that ACTUALLY EXIST in SlimUI)
    // =========================================================================

    [Header("Audio Sliders — from SlimUI prefab")]
    [Tooltip("Drag the Slider COMPONENT (not the GameObject) of the Music slider row here. " +
             "This is the child of musicSlider GameObject inside UISettingsManager.")]
    public Slider musicSlider;

    [Tooltip("► YOU NEED TO ADD THIS YOURSELF — SlimUI has no SFX slider by default.\n" +
             "Duplicate the musicSlider row, label it 'SFX Volume', drag its Slider here.\n" +
             "Leave empty if you haven't added it yet — nothing will break.")]
    public Slider sfxVolumeSlider;

    [Tooltip("► YOU NEED TO ADD THIS YOURSELF — SlimUI has no master volume slider.\n" +
             "Duplicate the musicSlider row, label it 'Master Volume', drag its Slider here.\n" +
             "Leave empty if you haven't added it yet.")]
    public Slider masterVolumeSlider;

    // =========================================================================
    //  SlimUI Controls Sliders
    // =========================================================================

    [Header("Controls Sliders — from SlimUI prefab")]
    [Tooltip("Drag the Slider component of the X Sensitivity slider row.")]
    public Slider sensitivityXSlider;

    [Tooltip("Drag the Slider component of the Y Sensitivity slider row.")]
    public Slider sensitivityYSlider;

    [Tooltip("Drag the Slider component of the Mouse Smoothing slider row.")]
    public Slider mouseSmoothSlider;

    // =========================================================================
    //  SlimUI Video Toggle Texts (TMP_Text components on toggle labels)
    // =========================================================================

    [Header("Video Toggle Texts — TMP_Text children of toggle buttons")]
    [Tooltip("TMP_Text showing 'on'/'off' for the Fullscreen toggle. " +
             "It's a child TMP_Text inside the fullscreen button.")]
    public TMP_Text fullscreentext;

    [Tooltip("TMP_Text showing 'on'/'off' for the VSync toggle.")]
    public TMP_Text vsynctext;

    [Tooltip("TMP_Text showing 'on'/'off' for Motion Blur toggle.")]
    public TMP_Text motionblurtext;

    [Tooltip("TMP_Text showing 'on'/'off' for Ambient Occlusion toggle.")]
    public TMP_Text ambientocclusiontext;

    [Tooltip("TMP_Text showing 'on'/'off' for Camera Effects toggle.")]
    public TMP_Text cameraeffectstext;

    // =========================================================================
    //  SlimUI Controls Toggle Text
    // =========================================================================

    [Header("Controls Toggle Text")]
    [Tooltip("TMP_Text showing 'on'/'off' for Invert Mouse Y.")]
    public TMP_Text invertmousetext;

    // =========================================================================
    //  SlimUI Shadow Line Indicators
    // =========================================================================

    [Header("Shadow Quality Lines — GameObjects active = selected")]
    [Tooltip("shadowofftextLINE — active when Shadows = Off.")]
    public GameObject shadowofftextLINE;

    [Tooltip("shadowlowtextLINE — active when Shadows = Low.")]
    public GameObject shadowlowtextLINE;

    [Tooltip("shadowhightextLINE — active when Shadows = High.")]
    public GameObject shadowhightextLINE;

    // =========================================================================
    //  SlimUI Anti-Aliasing Line Indicators
    // =========================================================================

    [Header("Anti-Aliasing Lines — GameObjects active = selected")]
    public GameObject aaofftextLINE;
    public GameObject aa2xtextLINE;
    public GameObject aa4xtextLINE;
    public GameObject aa8xtextLINE;

    // =========================================================================
    //  SlimUI Texture Quality Line Indicators
    // =========================================================================

    [Header("Texture Quality Lines — GameObjects active = selected")]
    public GameObject texturelowtextLINE;
    public GameObject texturemedtextLINE;
    public GameObject texturehightextLINE;

    // =========================================================================
    //  State flag for PauseManager
    // =========================================================================

    /// <summary>True when the settings panel is visible. Read by PauseManager for ESC navigation.</summary>
    public bool IsSettingsOpen => settingsPanel != null && settingsPanel.activeSelf;

    // Cached reference — auto-found at runtime, no Inspector drag needed
    private PauseUI _pauseUI;

    // =========================================================================
    //  Unity Lifecycle
    // =========================================================================

    private void Awake()
    {
        _pauseUI = FindFirstObjectByType<PauseUI>();
        BindSliderEvents();
    }

    private void OnEnable()
    {
        RefreshUIValues();
        SelectTab(1); // Default to VIDEO tab (matches SlimUI default)
    }

    // =========================================================================
    //  Public API — Show / Hide
    // =========================================================================

    public void ShowSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
        RefreshUIValues();
        SelectTab(1);
    }

    public void HideSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    // =========================================================================
    //  Back Button — returns to Pause first menu
    //  Wire the SlimUI RETURN / BACK button's OnClick to this method.
    // =========================================================================

    /// <summary>
    /// Called by the BACK / RETURN button in the settings screen.
    /// Delegates entirely to PauseUI.CloseSettings() which:
    ///   • Hides the settings panel
    ///   • Re-shows firstMenu
    ///   • Plays swoosh SFX
    ///   • Resets the SlimUI camera animator back to Position 1
    /// No SlimUI references needed here.
    /// </summary>
    public void ReturnToPauseMenu()
    {
        // Re-find if scene reloaded
        if (_pauseUI == null) _pauseUI = FindFirstObjectByType<PauseUI>();

        if (_pauseUI != null)
            _pauseUI.CloseSettings();
        else
            HideSettings(); // Fallback: just hide panel
    }

    // =========================================================================
    //  Tab Navigation (mirrors UIMenuManager tab methods exactly)
    // =========================================================================

    public void GamePanel()        => SelectTab(0);
    public void VideoPanel()       => SelectTab(1);
    public void ControlsPanel()    => SelectTab(2);
    public void KeyBindingsPanel() => MovementPanel(); // Default to Movement sub-tab

    public void MovementPanel() => SelectSubTab(0);
    public void CombatPanel()   => SelectSubTab(1);
    public void GeneralPanel()  => SelectSubTab(2);

    private void SelectTab(int index)
    {
        SetActiveIfNotNull(PanelGame,        index == 0);
        SetActiveIfNotNull(PanelVideo,       index == 1);
        SetActiveIfNotNull(PanelControls,    index == 2);
        SetActiveIfNotNull(PanelKeyBindings, index == 3);

        SetActiveIfNotNull(lineGame,        index == 0);
        SetActiveIfNotNull(lineVideo,       index == 1);
        SetActiveIfNotNull(lineControls,    index == 2);
        SetActiveIfNotNull(lineKeyBindings, index == 3);

        if (index == 3)
            MovementPanel();
        else
            DisableSubTabs();
    }

    private void SelectSubTab(int subIndex)
    {
        SetActiveIfNotNull(PanelGame,        false);
        SetActiveIfNotNull(PanelVideo,       false);
        SetActiveIfNotNull(PanelControls,    false);
        SetActiveIfNotNull(PanelKeyBindings, true);

        SetActiveIfNotNull(lineGame,        false);
        SetActiveIfNotNull(lineVideo,       false);
        SetActiveIfNotNull(lineControls,    false);
        SetActiveIfNotNull(lineKeyBindings, true);

        SetActiveIfNotNull(PanelMovement, subIndex == 0);
        SetActiveIfNotNull(PanelCombat,   subIndex == 1);
        SetActiveIfNotNull(PanelGeneral,  subIndex == 2);

        SetActiveIfNotNull(lineMovement, subIndex == 0);
        SetActiveIfNotNull(lineCombat,   subIndex == 1);
        SetActiveIfNotNull(lineGeneral,  subIndex == 2);
    }

    private void DisableSubTabs()
    {
        SetActiveIfNotNull(PanelMovement, false);
        SetActiveIfNotNull(PanelCombat,   false);
        SetActiveIfNotNull(PanelGeneral,  false);

        SetActiveIfNotNull(lineMovement, false);
        SetActiveIfNotNull(lineCombat,   false);
        SetActiveIfNotNull(lineGeneral,  false);
    }

    // =========================================================================
    //  SlimUI Toggle Buttons (wire each button's OnClick to these methods)
    //  Each toggle modifies GameSettingsManager, calls ApplySettings() LIVE,
    //  and updates the SlimUI text label.
    // =========================================================================

    public void FullScreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        SaveBool("Fullscreen", Screen.fullScreen);
        if (fullscreentext != null)
            fullscreentext.text = Screen.fullScreen ? "on" : "off";
    }

    public void vsync()
    {
        QualitySettings.vSyncCount = QualitySettings.vSyncCount == 0 ? 1 : 0;
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null) { gsm.vSync = QualitySettings.vSyncCount; gsm.SaveSettings(); }
        if (vsynctext != null) vsynctext.text = QualitySettings.vSyncCount > 0 ? "on" : "off";
    }

    public void MotionBlur()
    {
        int current = PlayerPrefs.GetInt("MotionBlur", 0);
        int next = current == 0 ? 1 : 0;
        PlayerPrefs.SetInt("MotionBlur", next);
        if (motionblurtext != null) motionblurtext.text = next == 1 ? "on" : "off";
    }

    public void AmbientOcclusion()
    {
        int current = PlayerPrefs.GetInt("AmbientOcclusion", 0);
        int next = current == 0 ? 1 : 0;
        PlayerPrefs.SetInt("AmbientOcclusion", next);
        if (ambientocclusiontext != null) ambientocclusiontext.text = next == 1 ? "on" : "off";
    }

    public void CameraEffects()
    {
        int current = PlayerPrefs.GetInt("CameraEffects", 0);
        int next = current == 0 ? 1 : 0;
        PlayerPrefs.SetInt("CameraEffects", next);
        if (cameraeffectstext != null) cameraeffectstext.text = next == 1 ? "on" : "off";
    }

    public void InvertMouse()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null) { gsm.invertYAxis = !gsm.invertYAxis; gsm.SaveSettings(); }
        if (invertmousetext != null)
            invertmousetext.text = (gsm != null && gsm.invertYAxis) ? "on" : "off";
    }

    public void ShadowsOff()  => SetShadowLevel(0);
    public void ShadowsLow()  => SetShadowLevel(1);
    public void ShadowsHigh() => SetShadowLevel(2);

    private void SetShadowLevel(int level)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null) { gsm.shadowQuality = level; gsm.ApplySettings(); gsm.SaveSettings(); }
        UpdateShadowUI(level);
    }

    public void TexturesLow()  => SetTextureLevel(2);
    public void TexturesMed()  => SetTextureLevel(1);
    public void TexturesHigh() => SetTextureLevel(0);

    private void SetTextureLevel(int mipmapLimit)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null) { gsm.textureQuality = mipmapLimit; gsm.ApplySettings(); gsm.SaveSettings(); }
        QualitySettings.globalTextureMipmapLimit = mipmapLimit;
        UpdateTextureUI(mipmapLimit);
    }

    public void AAOff() => SetAALevel(0);
    public void AA2x()  => SetAALevel(2);
    public void AA4x()  => SetAALevel(4);
    public void AA8x()  => SetAALevel(8);

    private void SetAALevel(int samples)
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;
        if (gsm != null) { gsm.antiAliasing = samples; gsm.ApplySettings(); gsm.SaveSettings(); }
        UpdateAntiAliasingUI(samples);
    }

    // =========================================================================
    //  Slider Bindings — live-apply audio and sensitivity changes
    // =========================================================================

    private void BindSliderEvents()
    {
        // Music Volume → AudioListener.volume via GameSettingsManager.musicVolume
        if (musicSlider != null)
        {
            musicSlider.onValueChanged.AddListener(v =>
            {
                GameSettingsManager gsm = GameSettingsManager.Instance;
                if (gsm == null) return;
                gsm.musicVolume = v;
                // Apply immediately so user hears the change live
                gsm.ApplySettings();
                PlayerPrefs.SetFloat("MusicVolume", v);
            });
        }

        // SFX Volume (only if you added the slider — safe to leave empty)
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(v =>
            {
                GameSettingsManager gsm = GameSettingsManager.Instance;
                if (gsm == null) return;
                gsm.sfxVolume = v;
                gsm.ApplySettings();
                PlayerPrefs.SetFloat("SFXVolume", v);
            });
        }

        // Master Volume (only if you added the slider)
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(v =>
            {
                GameSettingsManager gsm = GameSettingsManager.Instance;
                if (gsm == null) return;
                gsm.masterVolume = v;
                AudioListener.volume = v; // Apply live immediately
                PlayerPrefs.SetFloat("MasterVolume", v);
            });
        }

        // Sensitivity X
        if (sensitivityXSlider != null)
        {
            sensitivityXSlider.onValueChanged.AddListener(v =>
            {
                GameSettingsManager gsm = GameSettingsManager.Instance;
                if (gsm != null) { gsm.mouseSensitivity = v; gsm.SaveSettings(); }
                PlayerPrefs.SetFloat("XSensitivity", v);
            });
        }

        // Sensitivity Y
        if (sensitivityYSlider != null)
        {
            sensitivityYSlider.onValueChanged.AddListener(v =>
            {
                GameSettingsManager gsm = GameSettingsManager.Instance;
                if (gsm != null) { gsm.mouseSensitivity = v; gsm.SaveSettings(); }
                PlayerPrefs.SetFloat("YSensitivity", v);
            });
        }

        // Mouse Smooth
        if (mouseSmoothSlider != null)
        {
            mouseSmoothSlider.onValueChanged.AddListener(v =>
            {
                PlayerPrefs.SetFloat("MouseSmoothing", v);
            });
        }
    }

    // =========================================================================
    //  Refresh UI — reads current saved values and pushes to UI
    // =========================================================================

    public void RefreshUIValues()
    {
        GameSettingsManager gsm = GameSettingsManager.Instance;

        // Sliders
        if (musicSlider        != null) musicSlider.value        = PlayerPrefs.GetFloat("MusicVolume", gsm != null ? gsm.musicVolume : 0.8f);
        if (sfxVolumeSlider    != null) sfxVolumeSlider.value    = PlayerPrefs.GetFloat("SFXVolume",   gsm != null ? gsm.sfxVolume   : 1.0f);
        if (masterVolumeSlider != null) masterVolumeSlider.value  = gsm != null ? gsm.masterVolume : 1.0f;
        if (sensitivityXSlider != null) sensitivityXSlider.value  = PlayerPrefs.GetFloat("XSensitivity", gsm != null ? gsm.mouseSensitivity : 1f);
        if (sensitivityYSlider != null) sensitivityYSlider.value  = PlayerPrefs.GetFloat("YSensitivity", gsm != null ? gsm.mouseSensitivity : 1f);
        if (mouseSmoothSlider  != null) mouseSmoothSlider.value   = PlayerPrefs.GetFloat("MouseSmoothing", 0.5f);

        // Fullscreen
        if (fullscreentext != null)
            fullscreentext.text = Screen.fullScreen ? "on" : "off";

        // VSync
        if (vsynctext != null)
            vsynctext.text = QualitySettings.vSyncCount > 0 ? "on" : "off";

        // Motion Blur
        if (motionblurtext != null)
            motionblurtext.text = PlayerPrefs.GetInt("MotionBlur", 0) == 1 ? "on" : "off";

        // AO
        if (ambientocclusiontext != null)
            ambientocclusiontext.text = PlayerPrefs.GetInt("AmbientOcclusion", 0) == 1 ? "on" : "off";

        // Camera Effects
        if (cameraeffectstext != null)
            cameraeffectstext.text = PlayerPrefs.GetInt("CameraEffects", 1) == 1 ? "on" : "off";

        // Invert Mouse
        if (invertmousetext != null)
            invertmousetext.text = (gsm != null && gsm.invertYAxis) ? "on" : "off";

        // Lines
        int shadow  = gsm != null ? gsm.shadowQuality : 2;
        int texture = gsm != null ? gsm.textureQuality : 0;
        int aa      = gsm != null ? gsm.antiAliasing : 4;

        UpdateShadowUI(shadow);
        UpdateTextureUI(texture);
        UpdateAntiAliasingUI(aa);
    }

    // =========================================================================
    //  UI Line Helpers
    // =========================================================================

    private void UpdateShadowUI(int level)
    {
        SetActiveIfNotNull(shadowofftextLINE,  level == 0);
        SetActiveIfNotNull(shadowlowtextLINE,  level == 1);
        SetActiveIfNotNull(shadowhightextLINE, level == 2);
    }

    private void UpdateTextureUI(int mipmapLimit)
    {
        // mipmapLimit: 0 = High (Full res), 1 = Medium (Half), 2 = Low (Quarter)
        SetActiveIfNotNull(texturehightextLINE, mipmapLimit == 0);
        SetActiveIfNotNull(texturemedtextLINE,  mipmapLimit == 1);
        SetActiveIfNotNull(texturelowtextLINE,  mipmapLimit == 2);
    }

    private void UpdateAntiAliasingUI(int samples)
    {
        SetActiveIfNotNull(aaofftextLINE, samples == 0);
        SetActiveIfNotNull(aa2xtextLINE,  samples == 2);
        SetActiveIfNotNull(aa4xtextLINE,  samples == 4);
        SetActiveIfNotNull(aa8xtextLINE,  samples == 8);
    }

    private static void SetActiveIfNotNull(GameObject obj, bool active)
    {
        if (obj != null) obj.SetActive(active);
    }

    private static void SaveBool(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }
}
