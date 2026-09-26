using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Michsky.UI.Heat;

/// <summary>
/// SOLID — Bridge & Facade Pattern:
/// Unifies Michsky Heat UI Settings Panels across both LobbyScene and GameScene
/// with GameSettingsManager, KeybindingManager, and SettingsDescriptionManager.
/// 
/// ZERO MODIFICATIONS TO THIRD-PARTY HEAT UI CODE:
/// Interacts with Heat UI solely through its public components and events.
/// 
/// Manual & Auto-Wiring:
/// - All UI controls and preview fields are exposed directly in the Inspector for manual drag-and-drop.
/// - Right-click the component and select "Auto-Wire All Fields" to automatically populate all references!
/// </summary>
public class HeatSettingsBridge : MonoBehaviour
{
    [System.Serializable]
    public class SettingDescriptionEntry
    {
        [Tooltip("Identifier matching the setting element name.")]
        public string elementName;

        [Tooltip("The title displayed on the element row AND in the side preview Description Area.")]
        public string displayTitle;

        [TextArea(2, 6)]
        [Tooltip("The atmospheric description displayed in the side preview card when hovered or selected.")]
        public string description;

        [Tooltip("Optional custom cover image displayed in the side preview card.")]
        public Sprite coverImage;
    }

    // =========================================================================
    //  Inspector Fields (Manual Drag & Drop)
    // =========================================================================
    [Header("Description Area (Preview Card)")]
    [Tooltip("Drag the SettingsDescriptionManager component here (found in Settings/Content/Description Area).")]
    public SettingsDescriptionManager descriptionManager;

    [Header("General Tab Controls")]
    [Tooltip("Switch for Performance & Ping Overlay (previously Enable Hints).")]
    public SwitchManager perfOverlaySwitch;

    [Tooltip("Switch for Camera Shockwave & Shake (previously Enable Subtitles).")]
    public SwitchManager cameraShakeSwitch;

    [Tooltip("Horizontal Selector for Struggle Resist Mode (previously Subtitle Scale).")]
    public HorizontalSelector struggleModeSelector;

    [Tooltip("Horizontal Selector for UI Interface Scaling.")]
    public HorizontalSelector uiScaleSelector;

    [Header("Controls Tab Controls")]
    [Tooltip("Slider for Camera Look Sensitivity.")]
    public Slider lookSensitivitySlider;

    [Tooltip("Switch for Invert Y Pitch Axis (Reverse Look).")]
    public SwitchManager invertYSwitch;

    [Tooltip("Horizontal Selector for Sprint Mode (Hold vs Toggle).")]
    public HorizontalSelector sprintModeSelector;

    [Tooltip("The 8 keybinding row transforms under Key Bindings in the Controls panel.")]
    public List<Transform> keybindingRows = new List<Transform>();

    [Header("Audio Tab Controls")]
    [Tooltip("Slider for Master Audio Volume.")]
    public Slider masterVolumeSlider;

    [Tooltip("Slider for Atmospheric Music Volume.")]
    public Slider musicVolumeSlider;

    [Tooltip("Slider for Environmental SFX Volume.")]
    public Slider sfxVolumeSlider;

    [Tooltip("Slider for UI & Tactical Radio Volume.")]
    public Slider uiVolumeSlider;

    [Header("Visuals Tab Controls")]
    [Tooltip("Horizontal Selector for Window Mode (Borderless, Fullscreen, Windowed).")]
    public HorizontalSelector windowModeSelector;

    [Tooltip("Horizontal Selector for Frame Rate Ceiling (30, 60, 120, 144, 240, Unlimited).")]
    public HorizontalSelector frameRateSelector;

    [Tooltip("Switch for Vertical Sync (VSync).")]
    public SwitchManager vSyncSwitch;

    [Tooltip("Horizontal Selector for Texture Quality (Full, Half, Quarter, Eighth).")]
    public HorizontalSelector textureQualitySelector;

    [Tooltip("Horizontal Selector for Anisotropic Filtering (Disabled, Per Texture, Forced On).")]
    public HorizontalSelector anisotropicSelector;

    [Header("Inspector Editable Descriptions & Preview Images")]
    [Tooltip("Modify any setting's Title, Description, and Cover Image here. Changes apply immediately to both the row text and the side preview card!")]
    public List<SettingDescriptionEntry> settingDescriptions = new List<SettingDescriptionEntry>();

    private bool _isInitializing = false;

    // =========================================================================
    //  Auto Scene Discovery & Attachment Fallback
    // =========================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachToSettingsPanels()
    {
        var allSettings = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.name == "Settings" && go.scene.isLoaded && go.transform.Find("Content") != null);

        foreach (var settingsRoot in allSettings)
        {
            if (settingsRoot.GetComponent<HeatSettingsBridge>() == null)
            {
                settingsRoot.AddComponent<HeatSettingsBridge>();
            }
        }
    }

    private void Reset()
    {
        AutoWireAllFields();
        PopulateDefaultDescriptions();
    }

    private void Awake()
    {
        AutoWireAllFields();

        if (settingDescriptions == null || settingDescriptions.Count == 0)
        {
            PopulateDefaultDescriptions();
        }

        InitializeBridge();
    }

    private void OnEnable()
    {
        SyncUIToCurrentSettings();
        ApplyAllDescriptions();
        SyncKeybindingButtons();
    }

    // =========================================================================
    //  Context Menu: Auto-Wire All Inspector Fields
    // =========================================================================
    [ContextMenu("Auto-Wire All Fields")]
    public void AutoWireAllFields()
    {
        if (descriptionManager == null)
        {
            descriptionManager = GetComponentInChildren<SettingsDescriptionManager>(true) ?? FindFirstObjectByType<SettingsDescriptionManager>();
        }

        // General
        Transform genPanel = FindPanelRecursive(transform, "General");
        if (genPanel != null)
        {
            if (perfOverlaySwitch == null)
            {
                var t = FindChildRecursive(genPanel, "Show FPS & Ping") ?? FindChildRecursive(genPanel, "Enable Hints");
                if (t != null) perfOverlaySwitch = t.GetComponentInChildren<SwitchManager>(true);
            }

            if (cameraShakeSwitch == null)
            {
                var t = FindChildRecursive(genPanel, "Enable Subtitles") ?? FindChildRecursive(genPanel, "Camera Strain & Shake");
                if (t != null) cameraShakeSwitch = t.GetComponentInChildren<SwitchManager>(true);
            }

            if (struggleModeSelector == null)
            {
                var t = FindChildRecursive(genPanel, "Subtitle Scale") ?? FindChildRecursive(genPanel, "Struggle QTE Mode");
                if (t != null) struggleModeSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }

            if (uiScaleSelector == null)
            {
                var t = FindChildRecursive(genPanel, "UI Scale");
                if (t != null) uiScaleSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }
        }

        // Controls
        Transform ctrlPanel = FindPanelRecursive(transform, "Controls");
        if (ctrlPanel != null)
        {
            if (lookSensitivitySlider == null)
            {
                var t = FindChildRecursive(ctrlPanel, "Camera Sensitivity");
                if (t != null) lookSensitivitySlider = t.GetComponentInChildren<Slider>(true);
            }

            if (invertYSwitch == null)
            {
                var t = FindChildRecursive(ctrlPanel, "Reverse Look");
                if (t != null) invertYSwitch = t.GetComponentInChildren<SwitchManager>(true);
            }

            if (sprintModeSelector == null)
            {
                var t = FindChildRecursive(ctrlPanel, "Sprint Mode");
                if (t != null) sprintModeSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }

            if (keybindingRows == null || keybindingRows.Count == 0)
            {
                keybindingRows = new List<Transform>();
                foreach (Transform child in ctrlPanel.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.StartsWith("Settings Item (Binding)"))
                    {
                        keybindingRows.Add(child);
                    }
                }
            }
        }

        // Audio
        Transform audPanel = FindPanelRecursive(transform, "Audio");
        if (audPanel != null)
        {
            if (masterVolumeSlider == null)
            {
                var t = FindChildRecursive(audPanel, "Master Volume");
                if (t != null) masterVolumeSlider = t.GetComponentInChildren<Slider>(true);
            }

            if (musicVolumeSlider == null)
            {
                var t = FindChildRecursive(audPanel, "Music Volume");
                if (t != null) musicVolumeSlider = t.GetComponentInChildren<Slider>(true);
            }

            if (sfxVolumeSlider == null)
            {
                var t = FindChildRecursive(audPanel, "SFX Volume");
                if (t != null) sfxVolumeSlider = t.GetComponentInChildren<Slider>(true);
            }

            if (uiVolumeSlider == null)
            {
                var t = FindChildRecursive(audPanel, "UI Volume");
                if (t != null) uiVolumeSlider = t.GetComponentInChildren<Slider>(true);
            }
        }

        // Visuals
        Transform visPanel = FindPanelRecursive(transform, "Visuals");
        if (visPanel != null)
        {
            if (windowModeSelector == null)
            {
                var t = FindChildRecursive(visPanel, "Window Mode");
                if (t != null) windowModeSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }

            if (frameRateSelector == null)
            {
                var t = FindChildRecursive(visPanel, "Frame Rate");
                if (t != null) frameRateSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }

            if (vSyncSwitch == null)
            {
                var t = FindChildRecursive(visPanel, "VSync");
                if (t != null) vSyncSwitch = t.GetComponentInChildren<SwitchManager>(true);
            }

            if (textureQualitySelector == null)
            {
                var t = FindChildRecursive(visPanel, "Texture Quality");
                if (t != null) textureQualitySelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }

            if (anisotropicSelector == null)
            {
                var t = FindChildRecursive(visPanel, "Anisotropic Filtering");
                if (t != null) anisotropicSelector = t.GetComponentInChildren<HorizontalSelector>(true);
            }
        }
    }

    // =========================================================================
    //  Default Subterranean-Horror Description Presets
    // =========================================================================
    public void PopulateDefaultDescriptions()
    {
        settingDescriptions = new List<SettingDescriptionEntry>()
        {
            // --- GENERAL TAB ---
            new SettingDescriptionEntry
            {
                elementName = "Show FPS & Ping",
                displayTitle = "Performance & Ping Overlay",
                description = "Renders real-time frame rates and server round-trip latency (RTT) in the HUD. Essential for monitoring subterranean connection stability and stutter.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Enable Subtitles",
                displayTitle = "Camera Shockwave & Shake",
                description = "Simulates visceral head trauma, demonic screams, seismic tremors, and proximity blast waves. Disable to lock camera shudder for motion sensitivity.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Subtitle Scale",
                displayTitle = "Possession Resist Mode",
                description = "Defines how you fight demonic exorcism attempts. Select 'Rapid Mash' for raw survival intensity, or 'Hold Key' for steady resistance without repetitive strain.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Language",
                displayTitle = "Audio & Text Language",
                description = "Select the operational language for tactical communications, extraction terminal interfaces, and subtitles.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "UI Scale",
                displayTitle = "Interface Scaling",
                description = "Adjust the overall display scale of the HUD, biometric vitals, and inventory readouts for optimal legibility at high resolutions.",
                coverImage = null
            },

            // --- CONTROLS TAB ---
            new SettingDescriptionEntry
            {
                elementName = "Camera Sensitivity",
                displayTitle = "Look Sensitivity",
                description = "Controls turning and aiming responsiveness in the depths. Higher values allow rapid threat acquisition when stalked from behind.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Reverse Look",
                displayTitle = "Invert Pitch Axis",
                description = "Inverts vertical pitch controls for flight-sim instincts. Pushing forward pitches the camera downward.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Sprint Mode",
                displayTitle = "Sprint Activation",
                description = "Choose whether sustaining your sprint requires continuously holding the sprint key or toggling it with a single tap.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Key Bindings",
                displayTitle = "Tactical Key Bindings",
                description = "Rebind controls for Keyboard, Mouse, and Gamepad controllers. Click any slot to assign a new key or controller button.",
                coverImage = null
            },

            // --- AUDIO TAB ---
            new SettingDescriptionEntry
            {
                elementName = "Master Volume",
                displayTitle = "Master Acoustics",
                description = "Controls master audio gain for all acoustic output. Keep balanced to ensure distant entity footsteps and warning claxons remain discernible.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Music Volume",
                displayTitle = "Atmospheric Score",
                description = "Adjusts volume for ambient tension drones, ritualistic chanting, and dynamic confrontation music.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "SFX Volume",
                displayTitle = "Environmental SFX",
                description = "Controls the loudness of weapon discharges, metallic vent squeaks, beast snarls, and mechanical mine shafts.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "UI Volume",
                displayTitle = "Tactical Radio & UI",
                description = "Regulates volume for menu feedback, notification clicks, terminal chimes, and tactical radio prompts.",
                coverImage = null
            },

            // --- VISUALS TAB ---
            new SettingDescriptionEntry
            {
                elementName = "Window Mode",
                displayTitle = "Display Mode",
                description = "Select your display presentation. Exclusive Fullscreen yields minimal input latency; Borderless permits seamless multi-monitor navigation.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Resolution",
                displayTitle = "Display Resolution",
                description = "Adjust the native screen pixel grid. Higher resolutions maximize subterranean clarity and edge contrast against lurking silhouettes.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Frame Rate",
                displayTitle = "Frame Rate Ceiling",
                description = "Caps maximum rendering frequency to prevent GPU overheating during extended mining excursions or reduce frame time variance.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "VSync",
                displayTitle = "Vertical Sync (VSync)",
                description = "Locks rendering to the monitor refresh cycle to eliminate screen tearing during frantic camera turns. Adds minor input lag.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Texture Quality",
                displayTitle = "Texture Fidelity",
                description = "Dictates rock surface, rust, and monster skin texture resolution. Higher quality requires greater VRAM allocation.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Anisotropic Filtering",
                displayTitle = "Surface Angle Filtering",
                description = "Enhances texture sharpness along tunnel floors and walls receding into the distance at sharp oblique viewing angles.",
                coverImage = null
            }
        };
    }

    // =========================================================================
    //  Bridge Setup & Binding
    // =========================================================================
    public void InitializeBridge()
    {
        _isInitializing = true;

        SetupGeneralTab();
        SetupControlsTab();
        SetupAudioTab();
        SetupVisualsTab();

        _isInitializing = false;
        SyncUIToCurrentSettings();
        ApplyAllDescriptions();
        SyncKeybindingButtons();
    }

    // =========================================================================
    //  1. General Tab Setup
    // =========================================================================
    private void SetupGeneralTab()
    {
        // 1. Performance Overlay
        if (perfOverlaySwitch != null)
        {
            var entry = GetDescriptionEntry("Show FPS & Ping", "Performance & Ping Overlay");
            ConfigureSwitchComponent(perfOverlaySwitch, entry, GameSettingsManager.ShowPerfOverlay, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.showPerformanceOverlay = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        // 2. Camera Shake
        if (cameraShakeSwitch != null)
        {
            var entry = GetDescriptionEntry("Enable Subtitles", "Camera Shockwave & Shake");
            ConfigureSwitchComponent(cameraShakeSwitch, entry, GameSettingsManager.CameraShakeActive, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.cameraShakeEnabled = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        // 3. Struggle QTE Mode
        if (struggleModeSelector != null)
        {
            var entry = GetDescriptionEntry("Subtitle Scale", "Possession Resist Mode");
            ConfigureSelectorComponent(struggleModeSelector, entry,
                options: new string[] { "Rapid Mash", "Hold Key" },
                initialIndex: GameSettingsManager.StruggleHoldActive ? 1 : 0,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.struggleQTEHoldMode = (index == 1);
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }

        // 4. UI Scale
        if (uiScaleSelector != null)
        {
            var entry = GetDescriptionEntry("UI Scale", "Interface Scaling");
            ConfigureSelectorComponent(uiScaleSelector, entry,
                options: new string[] { "0.25x", "0.5x", "1.0x", "1.5x", "2.0x" },
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.uiScaleIndex : 2,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.uiScaleIndex = index;
                    GameSettingsManager.Instance.SaveSettings();
                });
        }
    }

    // =========================================================================
    //  2. Controls Tab Setup
    // =========================================================================
    private void SetupControlsTab()
    {
        // 1. Look Sensitivity
        if (lookSensitivitySlider != null)
        {
            var entry = GetDescriptionEntry("Camera Sensitivity", "Look Sensitivity");
            ConfigureSliderComponent(lookSensitivitySlider, entry, GameSettingsManager.MouseSens, 0.1f, 3.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.mouseSensitivity = val;
                GameSettingsManager.Instance.SaveSettings();
            });
        }

        // 2. Invert Pitch Axis
        if (invertYSwitch != null)
        {
            var entry = GetDescriptionEntry("Reverse Look", "Invert Pitch Axis");
            ConfigureSwitchComponent(invertYSwitch, entry, GameSettingsManager.InvertY, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.invertYAxis = val;
                GameSettingsManager.Instance.SaveSettings();
            });
        }

        // 3. Sprint Mode
        if (sprintModeSelector != null)
        {
            var entry = GetDescriptionEntry("Sprint Mode", "Sprint Activation");
            ConfigureSelectorComponent(sprintModeSelector, entry,
                options: new string[] { "Hold to Sprint", "Toggle Sprint" },
                initialIndex: GameSettingsManager.SprintToggle ? 1 : 0,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.sprintToggleMode = (index == 1);
                    GameSettingsManager.Instance.SaveSettings();
                });
        }

        // 4. Pro Keybindings
        SetupKeybindingRows();
    }

    // =========================================================================
    //  Pro Keybinding Setup for Controls Tab
    // =========================================================================
    private void SetupKeybindingRows()
    {
        if (KeybindingManager.Instance == null) return;

        var actions = KeybindingManager.Instance.actions;
        for (int i = 0; i < keybindingRows.Count && i < actions.Count; i++)
        {
            Transform row = keybindingRows[i];
            if (row == null) continue;

            var actionData = actions[i];

            // 1. Action Label Text
            SetRowTitleText(row, actionData.displayName);

            // 2. Description for Preview Area
            var entry = new SettingDescriptionEntry
            {
                elementName = actionData.displayName,
                displayTitle = actionData.displayName,
                description = actionData.tacticalDescription,
                coverImage = null
            };
            AttachHoverPreview(row, entry);

            // 3. Binding 1 (Keyboard / Mouse Button)
            Transform b1 = row.Find("Binding 1");
            if (b1 != null)
            {
                var bm1 = b1.GetComponent<ButtonManager>();
                if (bm1 != null)
                {
                    bm1.SetText(KeybindingManager.FormatKeyName(actionData.currentKey));
                    bm1.onClick.RemoveAllListeners();
                    string actId = actionData.actionId;
                    bm1.onClick.AddListener(() =>
                    {
                        KeybindingManager.Instance.StartRebindKeyboard(actId,
                            waitingStr => bm1.SetText(waitingStr),
                            finishedStr => bm1.SetText(finishedStr));
                    });
                }
            }

            // 4. Binding 2 (Gamepad Button)
            Transform b2 = row.Find("Binding 2");
            if (b2 != null)
            {
                var bm2 = b2.GetComponent<ButtonManager>();
                if (bm2 != null)
                {
                    bm2.SetText(KeybindingManager.FormatGamepadName(actionData.currentGamepadControl));
                    bm2.onClick.RemoveAllListeners();
                    string actId = actionData.actionId;
                    bm2.onClick.AddListener(() =>
                    {
                        KeybindingManager.Instance.StartRebindGamepad(actId,
                            waitingStr => bm2.SetText(waitingStr),
                            finishedStr => bm2.SetText(finishedStr));
                    });
                }
            }
        }
    }

    public void SyncKeybindingButtons()
    {
        if (KeybindingManager.Instance == null) return;

        var actions = KeybindingManager.Instance.actions;
        for (int i = 0; i < keybindingRows.Count && i < actions.Count; i++)
        {
            Transform row = keybindingRows[i];
            if (row == null) continue;

            var actionData = actions[i];

            Transform b1 = row.Find("Binding 1");
            if (b1 != null)
            {
                var bm1 = b1.GetComponent<ButtonManager>();
                if (bm1 != null) bm1.SetText(KeybindingManager.FormatKeyName(actionData.currentKey));
            }

            Transform b2 = row.Find("Binding 2");
            if (b2 != null)
            {
                var bm2 = b2.GetComponent<ButtonManager>();
                if (bm2 != null) bm2.SetText(KeybindingManager.FormatGamepadName(actionData.currentGamepadControl));
            }
        }
    }

    // =========================================================================
    //  3. Audio Tab Setup
    // =========================================================================
    private void SetupAudioTab()
    {
        if (masterVolumeSlider != null)
        {
            var entry = GetDescriptionEntry("Master Volume", "Master Acoustics");
            ConfigureSliderComponent(masterVolumeSlider, entry, GameSettingsManager.Instance != null ? GameSettingsManager.Instance.masterVolume : 1.0f, 0f, 1.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.masterVolume = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        if (musicVolumeSlider != null)
        {
            var entry = GetDescriptionEntry("Music Volume", "Atmospheric Score");
            ConfigureSliderComponent(musicVolumeSlider, entry, GameSettingsManager.Instance != null ? GameSettingsManager.Instance.musicVolume : 0.75f, 0f, 1.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.musicVolume = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        if (sfxVolumeSlider != null)
        {
            var entry = GetDescriptionEntry("SFX Volume", "Environmental SFX");
            ConfigureSliderComponent(sfxVolumeSlider, entry, GameSettingsManager.SFXVolume, 0f, 1.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.sfxVolume = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        if (uiVolumeSlider != null)
        {
            var entry = GetDescriptionEntry("UI Volume", "Tactical Radio & UI");
            ConfigureSliderComponent(uiVolumeSlider, entry, GameSettingsManager.UIVolumeVal, 0f, 1.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.uiVolume = val;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }
    }

    // =========================================================================
    //  4. Visuals Tab Setup
    // =========================================================================
    private void SetupVisualsTab()
    {
        if (windowModeSelector != null)
        {
            var entry = GetDescriptionEntry("Window Mode", "Display Mode");
            ConfigureSelectorComponent(windowModeSelector, entry,
                options: new string[] { "Borderless", "Fullscreen", "Windowed" },
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.displayMode : 0,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.displayMode = index;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }

        if (vSyncSwitch != null)
        {
            var entry = GetDescriptionEntry("VSync", "Vertical Sync (VSync)");
            ConfigureSwitchComponent(vSyncSwitch, entry, GameSettingsManager.Instance != null && GameSettingsManager.Instance.vSync == 1, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.vSync = val ? 1 : 0;
                GameSettingsManager.Instance.SaveSettings();
                GameSettingsManager.Instance.ApplySettings();
            });
        }

        if (frameRateSelector != null)
        {
            string[] fpsOptions = new string[] { "30 FPS", "60 FPS", "120 FPS", "144 FPS", "240 FPS", "Unlimited" };
            int[] fpsValues = new int[] { 30, 60, 120, 144, 240, -1 };

            int curFps = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.targetFPS : -1;
            int initialIdx = System.Array.IndexOf(fpsValues, curFps);
            if (initialIdx < 0) initialIdx = 5;

            var entry = GetDescriptionEntry("Frame Rate", "Frame Rate Ceiling");
            ConfigureSelectorComponent(frameRateSelector, entry,
                options: fpsOptions,
                initialIndex: initialIdx,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    int chosenFps = (index >= 0 && index < fpsValues.Length) ? fpsValues[index] : -1;
                    GameSettingsManager.Instance.targetFPS = chosenFps;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }

        if (textureQualitySelector != null)
        {
            var entry = GetDescriptionEntry("Texture Quality", "Texture Fidelity");
            ConfigureSelectorComponent(textureQualitySelector, entry,
                options: new string[] { "Full Res", "Half Res", "Quarter Res", "Eighth Res" },
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.textureQuality : 0,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.textureQuality = index;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }

        if (anisotropicSelector != null)
        {
            var entry = GetDescriptionEntry("Anisotropic Filtering", "Surface Angle Filtering");
            ConfigureSelectorComponent(anisotropicSelector, entry,
                options: new string[] { "Disabled", "Per Texture", "Forced On" },
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.anisotropicFiltering : 2,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.anisotropicFiltering = index;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }
    }

    // =========================================================================
    //  Description Application & Preview Card Sync
    // =========================================================================
    public void ApplyAllDescriptions()
    {
        if (settingDescriptions == null) return;

        foreach (var entry in settingDescriptions)
        {
            Transform target = FindChildRecursive(transform, entry.elementName);
            if (target != null)
            {
                SetRowTitleText(target, entry.displayTitle);
                AttachHoverPreview(target, entry);
            }
        }
    }

    private SettingDescriptionEntry GetDescriptionEntry(string key, string fallbackTitle)
    {
        var found = settingDescriptions.Find(e => e.elementName.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                                 e.displayTitle.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                                 e.displayTitle.Equals(fallbackTitle, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;

        return new SettingDescriptionEntry
        {
            elementName = key,
            displayTitle = fallbackTitle,
            description = string.Empty,
            coverImage = null
        };
    }

    // =========================================================================
    //  UI Synchronizer
    // =========================================================================
    public void SyncUIToCurrentSettings()
    {
        if (GameSettingsManager.Instance == null) return;
        _isInitializing = true;

        if (perfOverlaySwitch != null) perfOverlaySwitch.isOn = GameSettingsManager.ShowPerfOverlay;
        if (cameraShakeSwitch != null) cameraShakeSwitch.isOn = GameSettingsManager.CameraShakeActive;
        if (invertYSwitch != null) invertYSwitch.isOn = GameSettingsManager.InvertY;
        if (vSyncSwitch != null) vSyncSwitch.isOn = GameSettingsManager.Instance.vSync == 1;

        if (lookSensitivitySlider != null) lookSensitivitySlider.value = GameSettingsManager.MouseSens;
        if (masterVolumeSlider != null) masterVolumeSlider.value = GameSettingsManager.Instance.masterVolume;
        if (musicVolumeSlider != null) musicVolumeSlider.value = GameSettingsManager.Instance.musicVolume;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = GameSettingsManager.SFXVolume;
        if (uiVolumeSlider != null) uiVolumeSlider.value = GameSettingsManager.UIVolumeVal;

        if (struggleModeSelector != null) SetSelectorIndex(struggleModeSelector, GameSettingsManager.StruggleHoldActive ? 1 : 0);
        if (sprintModeSelector != null) SetSelectorIndex(sprintModeSelector, GameSettingsManager.SprintToggle ? 1 : 0);
        if (uiScaleSelector != null) SetSelectorIndex(uiScaleSelector, GameSettingsManager.Instance.uiScaleIndex);
        if (windowModeSelector != null) SetSelectorIndex(windowModeSelector, GameSettingsManager.Instance.displayMode);
        if (textureQualitySelector != null) SetSelectorIndex(textureQualitySelector, GameSettingsManager.Instance.textureQuality);
        if (anisotropicSelector != null) SetSelectorIndex(anisotropicSelector, GameSettingsManager.Instance.anisotropicFiltering);

        _isInitializing = false;
    }

    // =========================================================================
    //  Helper Methods for Control Configuration
    // =========================================================================
    private void ConfigureSwitchComponent(SwitchManager sw, SettingDescriptionEntry entry, bool initialValue, Action<bool> onChanged)
    {
        SetRowTitleText(sw.transform, entry.displayTitle);
        AttachHoverPreview(sw.transform, entry);

        sw.saveValue = false;
        sw.isOn = initialValue;

        sw.onValueChanged.RemoveAllListeners();
        sw.onValueChanged.AddListener(val => onChanged?.Invoke(val));
    }

    private void ConfigureSliderComponent(Slider s, SettingDescriptionEntry entry, float initialValue, float minVal, float maxVal, Action<float> onChanged)
    {
        SetRowTitleText(s.transform, entry.displayTitle);
        AttachHoverPreview(s.transform, entry);

        s.minValue = minVal;
        s.maxValue = maxVal;
        s.value = initialValue;

        s.onValueChanged.RemoveAllListeners();
        s.onValueChanged.AddListener(val => onChanged?.Invoke(val));

        SliderManager sm = s.GetComponent<SliderManager>() ?? s.GetComponentInParent<SliderManager>();
        if (sm != null) sm.saveValue = false;
    }

    private void ConfigureSelectorComponent(HorizontalSelector hs, SettingDescriptionEntry entry, string[] options, int initialIndex, Action<int> onChanged)
    {
        SetRowTitleText(hs.transform, entry.displayTitle);
        AttachHoverPreview(hs.transform, entry);

        hs.saveSelected = false;
        hs.useLocalization = false;

        hs.items.Clear();
        for (int i = 0; i < options.Length; i++)
        {
            var item = new HorizontalSelector.Item();
            item.itemTitle = options[i];
            item.localizationKey = string.Empty;
            hs.items.Add(item);
        }

        int safeIdx = Mathf.Clamp(initialIndex, 0, hs.items.Count - 1);
        hs.index = safeIdx;
        hs.defaultIndex = safeIdx;
        hs.UpdateUI();

        hs.onValueChanged.RemoveAllListeners();
        hs.onValueChanged.AddListener(val => onChanged?.Invoke(val));
    }

    private void SetRowTitleText(Transform root, string title)
    {
        Transform textChild = root.Find("Text");
        if (textChild != null)
        {
            var tmp = textChild.GetComponent<TextMeshProUGUI>();
            if (tmp != null) tmp.text = title;
        }
        else
        {
            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps.Length > 0 && tmps[0] != null) tmps[0].text = title;
        }
    }

    private void AttachHoverPreview(Transform root, SettingDescriptionEntry entry)
    {
        var element = root.GetComponentInChildren<SettingsElement>(true);
        if (element != null)
        {
            element.onHover.RemoveAllListeners();
            element.onHover.AddListener(() =>
            {
                if (descriptionManager != null)
                {
                    descriptionManager.UpdateUI(entry.displayTitle, entry.description, entry.coverImage);
                }
            });
        }
    }

    private void SetSelectorIndex(HorizontalSelector hs, int index)
    {
        if (hs == null || hs.items.Count == 0) return;
        int safeIdx = Mathf.Clamp(index, 0, hs.items.Count - 1);
        hs.index = safeIdx;
        hs.defaultIndex = safeIdx;
        hs.UpdateUI();
    }

    private Transform FindPanelRecursive(Transform root, string panelName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(panelName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }
}
