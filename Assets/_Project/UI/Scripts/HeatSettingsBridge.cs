using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
/// Interacts with Heat UI solely through its public components and reflection for private serialized fields.
/// 
/// Manual & Auto-Wiring:
/// - All UI controls, resolution selectors, controller icons, and preview fields are exposed directly in the Inspector.
/// - Right-click the component and select "Auto-Wire All Fields" or "Auto-Populate Controller Icons" anytime!
/// </summary>
public class HeatSettingsBridge : MonoBehaviour
{
    public static HeatSettingsBridge Instance { get; private set; }

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

    [System.Serializable]
    public class XboxIconSet
    {
        [Header("Face Buttons")]
        [Tooltip("Xbox A button icon.")]
        public Sprite buttonA;
        [Tooltip("Xbox B button icon.")]
        public Sprite buttonB;
        [Tooltip("Xbox X button icon.")]
        public Sprite buttonX;
        [Tooltip("Xbox Y button icon.")]
        public Sprite buttonY;

        [Header("Bumpers & Triggers")]
        [Tooltip("Xbox Left Bumper (LB) icon.")]
        public Sprite lb;
        [Tooltip("Xbox Right Bumper (RB) icon.")]
        public Sprite rb;
        [Tooltip("Xbox Left Trigger (LT) icon.")]
        public Sprite lt;
        [Tooltip("Xbox Right Trigger (RT) icon.")]
        public Sprite rt;

        [Header("Thumbsticks & Stick Press")]
        [Tooltip("Xbox Left Stick Click (L3 / Left Stick Press) icon.")]
        public Sprite leftStickPress;
        [Tooltip("Xbox Right Stick Click (R3 / Right Stick Press) icon.")]
        public Sprite rightStickPress;
        [Tooltip("Xbox Left Stick icon.")]
        public Sprite leftStick;
        [Tooltip("Xbox Right Stick icon.")]
        public Sprite rightStick;

        [Header("Directional Pad (D-Pad)")]
        [Tooltip("Xbox D-Pad Up icon.")]
        public Sprite dpadUp;
        [Tooltip("Xbox D-Pad Down icon.")]
        public Sprite dpadDown;
        [Tooltip("Xbox D-Pad Left icon.")]
        public Sprite dpadLeft;
        [Tooltip("Xbox D-Pad Right icon.")]
        public Sprite dpadRight;
        [Tooltip("Xbox D-Pad (Full) icon.")]
        public Sprite dpad;

        [Header("Menu & System Buttons")]
        [Tooltip("Xbox Menu button (Start / Hamburger) icon.")]
        public Sprite menu;
        [Tooltip("Xbox View button (Back / Select / Two Squares) icon.")]
        public Sprite view;
        [Tooltip("Xbox Share button icon.")]
        public Sprite share;
    }

    [System.Serializable]
    public class DualSenseIconSet
    {
        [Header("Face Buttons")]
        [Tooltip("DualSense Cross (✕) button icon.")]
        public Sprite cross;
        [Tooltip("DualSense Circle (◯) button icon.")]
        public Sprite circle;
        [Tooltip("DualSense Square (▢) button icon.")]
        public Sprite square;
        [Tooltip("DualSense Triangle (△) button icon.")]
        public Sprite triangle;

        [Header("Bumpers & Triggers")]
        [Tooltip("DualSense L1 Bumper icon.")]
        public Sprite l1;
        [Tooltip("DualSense R1 Bumper icon.")]
        public Sprite r1;
        [Tooltip("DualSense L2 Trigger icon.")]
        public Sprite l2;
        [Tooltip("DualSense R2 Trigger icon.")]
        public Sprite r2;

        [Header("Thumbsticks & Stick Press")]
        [Tooltip("DualSense Left Stick Click (L3 / Left Stick Press) icon.")]
        public Sprite leftStickPress;
        [Tooltip("DualSense Right Stick Click (R3 / Right Stick Press) icon.")]
        public Sprite rightStickPress;
        [Tooltip("DualSense Left Stick icon.")]
        public Sprite leftStick;
        [Tooltip("DualSense Right Stick icon.")]
        public Sprite rightStick;

        [Header("Directional Pad (D-Pad)")]
        [Tooltip("DualSense D-Pad Up icon.")]
        public Sprite dpadUp;
        [Tooltip("DualSense D-Pad Down icon.")]
        public Sprite dpadDown;
        [Tooltip("DualSense D-Pad Left icon.")]
        public Sprite dpadLeft;
        [Tooltip("DualSense D-Pad Right icon.")]
        public Sprite dpadRight;
        [Tooltip("DualSense D-Pad (Full) icon.")]
        public Sprite dpad;

        [Header("Menu & Special")]
        [Tooltip("DualSense Options button (Start / Menu) icon.")]
        public Sprite options;
        [Tooltip("DualSense Create button (Select / Share) icon.")]
        public Sprite create;
        [Tooltip("DualSense Touchpad Press icon.")]
        public Sprite touchpadPress;
    }

    // =========================================================================
    //  Inspector Fields (Manual Drag & Drop)
    // =========================================================================
    [Header("Description Area (Preview Card)")]
    [Tooltip("Drag the SettingsDescriptionManager component here (found in Settings/Content/Description Area).")]
    public SettingsDescriptionManager descriptionManager;

    [Header("General Tab Controls")]
    [Tooltip("Switch for Performance & Ping Overlay.")]
    public SwitchManager perfOverlaySwitch;

    [Tooltip("Switch for Camera Shockwave & Shake.")]
    public SwitchManager cameraShakeSwitch;

    [Tooltip("Horizontal Selector for Struggle Resist Mode.")]
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

    [System.Serializable]
    public class KeybindingRowItem
    {
        [Tooltip("The game action this UI row represents.")]
        public KeybindingActionType action;

        [Tooltip("The UI row Transform under Key Bindings (e.g. Settings Item (Binding)).")]
        public Transform row;
    }

    [Header("Keybinding Rows (Named Action Dropdowns)")]
    [Tooltip("Explicitly configured keybinding rows with named action dropdowns. Add or remove items to customize or exclude specific bindings.")]
    public List<KeybindingRowItem> configuredBindings = new List<KeybindingRowItem>();

    [Header("Controller Icon Packs (Xbox & DualSense)")]
    [Tooltip("Official Heat UI sprite icons for Xbox controllers with exact button names.")]
    public XboxIconSet xboxIcons = new XboxIconSet();

    [Tooltip("Official Heat UI sprite icons for Sony DualSense controllers with exact button names.")]
    public DualSenseIconSet dualSenseIcons = new DualSenseIconSet();

    [Header("Heat UI Preset Manager (Optional)")]
    [Tooltip("Reference to Heat UI's _Preset Manager asset for direct preset lookups.")]
    public ControllerPresetManager presetManager;

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
    [Tooltip("Dropdown for Screen Resolution (Heat UI Dropdown).")]
    public Michsky.UI.Heat.Dropdown resolutionDropdown;

    [Tooltip("Alternative Horizontal Selector for Screen Resolution if preferred.")]
    public HorizontalSelector resolutionSelector;

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
    private List<Resolution> _availableResolutions = new List<Resolution>();

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

    private void Awake()
    {
        Instance = this;
        KeybindingManager.CustomGamepadSpriteResolver = GetGamepadSprite;
        CleanupOldDescriptions();
    }

    private void Start()
    {
        CleanupOldDescriptions();
        InitializeBridge();
        StartCoroutine(DelayedDescriptionSync());
    }

    private void OnEnable()
    {
        KeybindingManager.OnBindingsChanged += SyncKeybindingButtons;
        KeybindingManager.OnDeviceTypeChanged += OnDeviceTypeChanged;
    }

    private void OnDisable()
    {
        KeybindingManager.OnBindingsChanged -= SyncKeybindingButtons;
        KeybindingManager.OnDeviceTypeChanged -= OnDeviceTypeChanged;
    }

    private void OnDeviceTypeChanged(bool isGamepad)
    {
        SyncKeybindingButtons();
    }

    private IEnumerator DelayedDescriptionSync()
    {
        // Wait 1 frame so Michsky Heat UI's SettingsDescription.Start() finishes registering
        yield return null;
        ApplyAllDescriptions();
        SyncKeybindingButtons();
    }

    private void Reset()
    {
        PopulateDefaultDescriptions();
#if UNITY_EDITOR
        AutoPopulateControllerIcons();
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CleanupOldDescriptions();

        if (xboxIcons.buttonA == null || dualSenseIcons.cross == null)
        {
            AutoPopulateControllerIcons();
        }
    }
#endif

    public void CleanupOldDescriptions()
    {
        if (settingDescriptions == null) return;

        bool changed = false;

        // 1. Migrate legacy elements to their active canonical names if not already present
        for (int i = 0; i < settingDescriptions.Count; i++)
        {
            var e = settingDescriptions[i];
            if (e == null) continue;

            if (e.elementName.Equals("Enable Subtitles", StringComparison.OrdinalIgnoreCase))
            {
                if (!settingDescriptions.Exists(x => x != null && x.elementName.Equals("Camera Shake", StringComparison.OrdinalIgnoreCase)))
                {
                    e.elementName = "Camera Shake";
                    e.displayTitle = "Camera Shockwave & Shake";
                    e.description = "Simulates visceral head trauma, demonic screams, seismic tremors, and proximity blast waves. Disable to lock camera shudder for motion sensitivity.";
                }
                else
                {
                    e.elementName = string.Empty; // mark for removal
                }
                changed = true;
            }
            else if (e.elementName.Equals("Subtitle Scale", StringComparison.OrdinalIgnoreCase))
            {
                if (!settingDescriptions.Exists(x => x != null && x.elementName.Equals("Struggle Mode", StringComparison.OrdinalIgnoreCase)))
                {
                    e.elementName = "Struggle Mode";
                    e.displayTitle = "Possession Resist Mode";
                    e.description = "Defines how you fight demonic host intrusions. Select 'Rapid Mash' for raw survival intensity, or 'Hold Key' for steady resistance without repetitive strain.";
                }
                else
                {
                    e.elementName = string.Empty; // mark for removal
                }
                changed = true;
            }
        }

        // 2. Remove all legacy / obsolete entries
        int removedCount = settingDescriptions.RemoveAll(e =>
            e == null ||
            string.IsNullOrEmpty(e.elementName) ||
            e.elementName.Equals("Enable Subtitles", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Subtitle Scale", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Language", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Enable Hints", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Show Network & FPS", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Interact", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Interaction", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Interact / Possess", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Pause", StringComparison.OrdinalIgnoreCase) ||
            e.elementName.Equals("Tactical Menu / Pause", StringComparison.OrdinalIgnoreCase));

        if (removedCount > 0)
        {
            changed = true;
        }

#if UNITY_EDITOR
        if (changed && !Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }


#if UNITY_EDITOR
    [ContextMenu("Auto-Populate Controller Icons")]
    public void AutoPopulateControllerIcons()
    {
        string xboxPath = "Assets/ThirdParty/Heat - Complete Modern UI/Textures/Controllers/Xbox/";
        string dsPath = "Assets/ThirdParty/Heat - Complete Modern UI/Textures/Controllers/DualSense/";

        // 1. Xbox Icons (Exact Button Names)
        xboxIcons.buttonA = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox A.png");
        xboxIcons.buttonB = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox B.png");
        xboxIcons.buttonX = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox X.png");
        xboxIcons.buttonY = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Y.png");
        xboxIcons.lb = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox LB.png");
        xboxIcons.rb = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox RB.png");
        xboxIcons.lt = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox LT.png");
        xboxIcons.rt = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox RT.png");
        xboxIcons.leftStickPress = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Left Stick Press.png");
        xboxIcons.rightStickPress = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Right Stick Press.png");
        xboxIcons.leftStick = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Left Stick.png");
        xboxIcons.rightStick = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Right Stick.png");
        xboxIcons.dpadUp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Dpad Up.png");
        xboxIcons.dpadDown = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Dpad Down.png");
        xboxIcons.dpadLeft = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Dpad Left.png");
        xboxIcons.dpadRight = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Dpad Right.png");
        xboxIcons.dpad = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Dpad.png");
        xboxIcons.menu = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Menu.png");
        xboxIcons.view = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox View.png");
        xboxIcons.share = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(xboxPath + "Xbox Share.png");

        // 2. DualSense Icons (Exact Button Names)
        dualSenseIcons.cross = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Cross.png");
        dualSenseIcons.circle = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Circle.png");
        dualSenseIcons.square = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Square.png");
        dualSenseIcons.triangle = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Triangle.png");
        dualSenseIcons.l1 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense L1.png");
        dualSenseIcons.r1 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense R1.png");
        dualSenseIcons.l2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense L2.png");
        dualSenseIcons.r2 = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense R2.png");
        dualSenseIcons.leftStickPress = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Left Stick Press.png");
        dualSenseIcons.rightStickPress = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Right Stick Press.png");
        dualSenseIcons.leftStick = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Left Stick.png");
        dualSenseIcons.rightStick = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Right Stick.png");
        dualSenseIcons.dpadUp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Dpad Up.png");
        dualSenseIcons.dpadDown = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Dpad Down.png");
        dualSenseIcons.dpadLeft = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Dpad Left.png");
        dualSenseIcons.dpadRight = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Dpad Right.png");
        dualSenseIcons.dpad = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSesne Dpad.png");
        dualSenseIcons.options = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Options.png");
        dualSenseIcons.create = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Create.png");
        dualSenseIcons.touchpadPress = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dsPath + "DualSense Touchpad Press.png");

        // 3. Preset Manager (optional Heat UI preset reference)
        if (presetManager == null)
        {
            presetManager = UnityEditor.AssetDatabase.LoadAssetAtPath<ControllerPresetManager>("Assets/ThirdParty/Heat - Complete Modern UI/Presets/Controllers/_Preset Manager.asset");
        }

        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

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
                elementName = "Camera Shake",
                displayTitle = "Camera Shockwave & Shake",
                description = "Simulates visceral head trauma, demonic screams, seismic tremors, and proximity blast waves. Disable to lock camera shudder for motion sensitivity.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Struggle Mode",
                displayTitle = "Possession Resist Mode",
                description = "Defines how you fight demonic host intrusions. Select 'Rapid Mash' for raw survival intensity, or 'Hold Key' for steady resistance without repetitive strain.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "UI Scale",
                displayTitle = "Interface Scaling",
                description = "Adjust the overall display scale of the HUD, biometric vitals, and inventory readouts for optimal legibility at high resolutions.",
                coverImage = null
            },

            // --- CONTROLS TAB (8 Canonical Game Actions) ---
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
            new SettingDescriptionEntry
            {
                elementName = "Sprint / Tactical Rush",
                displayTitle = "Sprint / Tactical Rush",
                description = "Accelerate traversal across open mining caverns. Depletes stamina; increases acoustic profile.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Jump / Vault Obstacles",
                displayTitle = "Jump / Vault Obstacles",
                description = "Leap over collapsed mine rails, low pipes, and treacherous rock fissures.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Physical Manifestation",
                displayTitle = "Physical Manifestation",
                description = "Shift from incorporeal spirit form into physical reality to execute lethal strikes and hunt explorers. Consumes manifestation charges.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Holy Exorcism Rite",
                displayTitle = "Holy Exorcism Rite",
                description = "Channel sanctified rite against possessed companions to purge the invading demon and heavily drain her possession reserves.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Administer Medical Vial",
                displayTitle = "Administer Medical Vial",
                description = "Inject coagulant from your medical vial supply to rapidly stabilize critical trauma and restore vital health points.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Resist Possession",
                displayTitle = "Resist Possession",
                description = "Fight back against demonic host intrusion and reclaim somatic motor control during struggle.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Deal Activations",
                displayTitle = "Deal Activations",
                description = "Initiate and negotiate forbidden subterranean deals and demonic blood pacts with the Vengeful Spirit.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Exit Possession",
                displayTitle = "Exit Possession",
                description = "Voluntarily terminate host possession to preserve ethereal essence and revert to incorporeal ghost form.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Loot Bodies & Containers",
                displayTitle = "Loot Bodies & Containers",
                description = "Scavenge fallen explorer corpses, extraction batteries, supply crates, and medical vials.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate Cycle Target",
                displayTitle = "Spectate Cycle Target",
                description = "Cycle camera view between surviving explorers or stalking demonic entities during spectator observation.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate Previous Target",
                displayTitle = "Spectate Previous Target",
                description = "Cycle camera to the previous living survivor or summoned monster.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate Next Target",
                displayTitle = "Spectate Next Target",
                description = "Cycle camera to the next living survivor or summoned monster.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate View Mode",
                displayTitle = "Spectate View Mode",
                description = "Toggle between tactical free-orbit mouse look and follow over-the-shoulder camera.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate Cursor Lock",
                displayTitle = "Spectate Cursor Lock",
                description = "Lock or release mouse cursor while spectating.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Spectate Switch Category",
                displayTitle = "Spectate Switch Category",
                description = "Switch spectator observation between living human survivors and summoned monsters.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Exit Spectator Mode",
                displayTitle = "Exit Spectator Mode",
                description = "Exit spectator observation mode and return to player view or death screen.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Command: Go Hunt",
                displayTitle = "Command: Go Hunt",
                description = "Command all summoned monsters to roam and hunt down surviving investigators.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Command: To My Side",
                displayTitle = "Command: To My Side",
                description = "Command all summoned monsters to return and stand guard beside the Vengeful Spirit.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Monster Summon Rite",
                displayTitle = "Monster Summon Rite",
                description = "Open the necrotic summon rites menu to raise Undead and Berserker abominations in the subterranean mines.",
                coverImage = null
            },
            new SettingDescriptionEntry
            {
                elementName = "Possession Selection Menu",
                displayTitle = "Possession Selection Menu",
                description = "Open the possession target selection modal to choose an investigator host to inhabit.",
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
            var entry = GetDescriptionEntry("Camera Shake", "Camera Shockwave & Shake");
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
            var entry = GetDescriptionEntry("Struggle Mode", "Possession Resist Mode");
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
                options: new string[] { "Compact (90%)", "Standard (100%)", "Expanded (110%)", "Large (120%)" },
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.uiScaleIndex : 1,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.uiScaleIndex = index;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }
    }

    // =========================================================================
    //  2. Controls Tab Setup
    // =========================================================================
    private void SetupControlsTab()
    {
        if (lookSensitivitySlider != null)
        {
            var entry = GetDescriptionEntry("Camera Sensitivity", "Look Sensitivity");
            ConfigureSliderComponent(lookSensitivitySlider, entry, GameSettingsManager.MouseSens, 0.2f, 5.0f, val =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.mouseSensitivity = val;
                GameSettingsManager.Instance.SaveSettings();
            });
        }

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
        if (KeybindingManager.Instance == null || configuredBindings == null) return;

        var actions = KeybindingManager.Instance.actions;
        configuredBindings.RemoveAll(item => item == null || item.row == null);

        foreach (var item in configuredBindings)
        {
            string actId = item.action.ToString();
            var actionData = actions.Find(a => a.actionId.Equals(actId, StringComparison.OrdinalIgnoreCase));

            // If action was excluded in code, hide the row
            if (actionData == null)
            {
                item.row.gameObject.SetActive(false);
                continue;
            }

            item.row.gameObject.SetActive(true);

            // 1. Action Label Text
            SetRowTitleText(item.row, actionData.displayName);

            // 2. Description for Preview Area
            var entry = GetDescriptionEntry(actionData.displayName, actionData.displayName);
            if (string.IsNullOrEmpty(entry.description))
            {
                entry.description = actionData.tacticalDescription;
            }
            AttachHoverPreview(item.row, entry);

            // 3. Binding 1 (Keyboard / Mouse Button)
            Transform b1 = item.row.Find("Binding 1");
            if (b1 != null)
            {
                var bm1 = b1.GetComponent<ButtonManager>();
                if (bm1 != null)
                {
                    bm1.SetText(KeybindingManager.FormatKeyName(actionData.currentKey));
                    bm1.onClick.RemoveAllListeners();
                    string targetActId = actionData.actionId;
                    bm1.onClick.AddListener(() =>
                    {
                        KeybindingManager.Instance.StartRebindKeyboard(targetActId,
                            waitingStr => bm1.SetText(waitingStr),
                            finishedStr => bm1.SetText(finishedStr));
                    });
                }
            }

            // 4. Binding 2 (Gamepad Button with Official Heat UI Icon)
            Transform b2 = item.row.Find("Binding 2");
            if (b2 != null)
            {
                var bm2 = b2.GetComponent<ButtonManager>();
                if (bm2 != null)
                {
                    SetupGamepadBindingButton(b2, bm2, actionData);
                }
            }
        }
    }

    private void SetupGamepadBindingButton(Transform b2, ButtonManager bm2, KeybindingManager.ActionBinding actionData)
    {
        UpdateGamepadButtonVisual(bm2, actionData.currentGamepadControl);

        bm2.onClick.RemoveAllListeners();
        string actId = actionData.actionId;
        bm2.onClick.AddListener(() =>
        {
            var iconImg = EnsureGamepadIcon(bm2.transform);
            if (iconImg != null) iconImg.gameObject.SetActive(false);

            KeybindingManager.Instance.StartRebindGamepad(actId,
                waitingStr => bm2.SetText(waitingStr),
                finishedStr =>
                {
                    var updatedAct = KeybindingManager.Instance.actions.Find(a => a.actionId.Equals(actId, StringComparison.OrdinalIgnoreCase));
                    if (updatedAct != null)
                    {
                        UpdateGamepadButtonVisual(bm2, updatedAct.currentGamepadControl);
                    }
                    else
                    {
                        bm2.SetText(finishedStr);
                    }
                });
        });
    }

    private void UpdateGamepadButtonVisual(ButtonManager bm, string controlName)
    {
        if (bm == null) return;

        Image iconImg = EnsureGamepadIcon(bm.transform);
        Sprite iconSprite = GetGamepadSprite(controlName);

        if (iconSprite != null && iconImg != null)
        {
            iconImg.sprite = iconSprite;
            iconImg.gameObject.SetActive(true);
            bm.SetText(""); // Clear text so only the sharp controller icon is shown
        }
        else
        {
            if (iconImg != null) iconImg.gameObject.SetActive(false);
            bm.SetText(KeybindingManager.FormatGamepadName(controlName, KeybindingManager.IsPlayStationActive()));
        }
    }

    private Image EnsureGamepadIcon(Transform buttonRoot)
    {
        Transform existing = buttonRoot.Find("GamepadIcon");
        if (existing != null)
        {
            return existing.GetComponent<Image>();
        }

        GameObject go = new GameObject("GamepadIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(buttonRoot, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(28f, 28f);
        rt.anchoredPosition = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;

        return img;
    }

    public Sprite GetGamepadSprite(string controlName)
    {
        if (string.IsNullOrEmpty(controlName)) return null;

        bool isDualSense = KeybindingManager.IsPlayStationActive();
        string key = controlName.Trim().ToLower().Replace(" ", "").Replace("_", "").Replace("-", "").Replace("/", "");

        if (isDualSense)
        {
            if (dualSenseIcons == null) return null;
            switch (key)
            {
                // Face buttons
                case "cross":
                case "buttonsouth":
                case "select":
                case "a":
                    return dualSenseIcons.cross;

                case "circle":
                case "buttoneast":
                case "exit":
                case "goback":
                case "b":
                    return dualSenseIcons.circle;

                case "square":
                case "buttonwest":
                case "getready":
                case "x":
                    return dualSenseIcons.square;

                case "triangle":
                case "buttonnorth":
                case "y":
                    return dualSenseIcons.triangle;

                // Bumpers & Triggers
                case "l1":
                case "leftshoulder":
                case "previous":
                case "lb":
                    return dualSenseIcons.l1;

                case "r1":
                case "rightshoulder":
                case "next":
                case "fastercredits":
                case "rb":
                    return dualSenseIcons.r1;

                case "l2":
                case "lefttrigger":
                case "previousalt":
                case "lt":
                    return dualSenseIcons.l2;

                case "r2":
                case "righttrigger":
                case "nextalt":
                case "rt":
                    return dualSenseIcons.r2;

                // Sticks & Press
                case "leftstickpress":
                case "l3":
                case "ls":
                    return dualSenseIcons.leftStickPress;

                case "rightstickpress":
                case "r3":
                case "rs":
                    return dualSenseIcons.rightStickPress;

                case "leftstick":
                case "navigate":
                    return dualSenseIcons.leftStick;

                case "rightstick":
                case "scroll":
                case "scrollhorizontal":
                case "scrollvertical":
                    return dualSenseIcons.rightStick;

                // D-Pad
                case "dpadup":
                    return dualSenseIcons.dpadUp;

                case "dpaddown":
                    return dualSenseIcons.dpadDown;

                case "dpadleft":
                    return dualSenseIcons.dpadLeft;

                case "dpadright":
                    return dualSenseIcons.dpadRight;

                case "dpad":
                    return dualSenseIcons.dpad;

                // Menu & Special
                case "options":
                case "start":
                case "menu":
                    return dualSenseIcons.options;

                case "create":
                case "share":
                case "view":
                    return dualSenseIcons.create;

                case "touchpadpress":
                case "touchpad":
                    return dualSenseIcons.touchpadPress;

                default:
                    return null;
            }
        }
        else
        {
            if (xboxIcons == null) return null;
            switch (key)
            {
                // Face buttons
                case "a":
                case "buttona":
                case "buttonsouth":
                case "select":
                case "cross":
                    return xboxIcons.buttonA;

                case "b":
                case "buttonb":
                case "buttoneast":
                case "exit":
                case "goback":
                case "circle":
                    return xboxIcons.buttonB;

                case "x":
                case "buttonx":
                case "buttonwest":
                case "getready":
                case "square":
                    return xboxIcons.buttonX;

                case "y":
                case "buttony":
                case "buttonnorth":
                case "triangle":
                    return xboxIcons.buttonY;

                // Bumpers & Triggers
                case "lb":
                case "leftshoulder":
                case "previous":
                case "l1":
                    return xboxIcons.lb;

                case "rb":
                case "rightshoulder":
                case "next":
                case "fastercredits":
                case "r1":
                    return xboxIcons.rb;

                case "lt":
                case "lefttrigger":
                case "previousalt":
                case "l2":
                    return xboxIcons.lt;

                case "rt":
                case "righttrigger":
                case "nextalt":
                case "r2":
                    return xboxIcons.rt;

                // Sticks & Press
                case "leftstickpress":
                case "l3":
                case "ls":
                    return xboxIcons.leftStickPress;

                case "rightstickpress":
                case "r3":
                case "rs":
                    return xboxIcons.rightStickPress;

                case "leftstick":
                case "navigate":
                    return xboxIcons.leftStick;

                case "rightstick":
                case "scroll":
                case "scrollhorizontal":
                case "scrollvertical":
                    return xboxIcons.rightStick;

                // D-Pad
                case "dpadup":
                    return xboxIcons.dpadUp;

                case "dpaddown":
                    return xboxIcons.dpadDown;

                case "dpadleft":
                    return xboxIcons.dpadLeft;

                case "dpadright":
                    return xboxIcons.dpadRight;

                case "dpad":
                    return xboxIcons.dpad;

                // Menu & Special
                case "menu":
                case "start":
                case "options":
                    return xboxIcons.menu;

                case "view":
                case "select_alt":
                case "back":
                case "share":
                case "create":
                    return xboxIcons.view;

                case "share_button":
                    return xboxIcons.share;

                default:
                    return null;
            }
        }
    }

    public void SyncKeybindingButtons()
    {
        if (KeybindingManager.Instance == null || configuredBindings == null) return;

        var actions = KeybindingManager.Instance.actions;
        foreach (var item in configuredBindings)
        {
            if (item == null || item.row == null) continue;

            string actId = item.action.ToString();
            var actionData = actions.Find(a => a.actionId.Equals(actId, StringComparison.OrdinalIgnoreCase));
            if (actionData == null) continue;

            Transform b1 = item.row.Find("Binding 1");
            if (b1 != null)
            {
                var bm1 = b1.GetComponent<ButtonManager>();
                if (bm1 != null) bm1.SetText(KeybindingManager.FormatKeyName(actionData.currentKey));
            }

            Transform b2 = item.row.Find("Binding 2");
            if (b2 != null)
            {
                var bm2 = b2.GetComponent<ButtonManager>();
                if (bm2 != null) UpdateGamepadButtonVisual(bm2, actionData.currentGamepadControl);
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
        SetupResolutionControl();

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

            int curFps = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.targetFPS : 60;
            int curIdx = Array.IndexOf(fpsValues, curFps);
            if (curIdx < 0) curIdx = 1; // default 60 FPS

            var entry = GetDescriptionEntry("Frame Rate", "Frame Rate Ceiling");
            ConfigureSelectorComponent(frameRateSelector, entry, fpsOptions, curIdx, index =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                GameSettingsManager.Instance.targetFPS = fpsValues[index];
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
                initialIndex: GameSettingsManager.Instance != null ? GameSettingsManager.Instance.anisotropicFiltering : 1,
                onChanged: index =>
                {
                    if (_isInitializing || GameSettingsManager.Instance == null) return;
                    GameSettingsManager.Instance.anisotropicFiltering = index;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                });
        }
    }

    private void SetupResolutionControl()
    {
        _availableResolutions.Clear();
        var allRes = Screen.resolutions;
        if (allRes != null && allRes.Length > 0)
        {
            var seen = new HashSet<string>();
            foreach (var r in allRes)
            {
                string key = $"{r.width}x{r.height}";
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    _availableResolutions.Add(r);
                }
            }
        }
        else
        {
            _availableResolutions.Add(new Resolution { width = 1280, height = 720 });
            _availableResolutions.Add(new Resolution { width = 1600, height = 900 });
            _availableResolutions.Add(new Resolution { width = 1920, height = 1080 });
            _availableResolutions.Add(new Resolution { width = 2560, height = 1440 });
            _availableResolutions.Add(new Resolution { width = 3840, height = 2160 });
        }

        int curW = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.resolutionWidth : Screen.width;
        int curH = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.resolutionHeight : Screen.height;

        int curIdx = _availableResolutions.FindIndex(r => r.width == curW && r.height == curH);
        if (curIdx < 0)
        {
            curIdx = _availableResolutions.FindIndex(r => r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height);
            if (curIdx < 0) curIdx = _availableResolutions.Count - 1;
        }

        var entry = GetDescriptionEntry("Resolution", "Display Resolution");

        if (resolutionDropdown != null)
        {
            SetRowTitleText(resolutionDropdown.transform, entry.displayTitle);
            AttachHoverPreview(resolutionDropdown.transform, entry);

            resolutionDropdown.items.Clear();
            for (int i = 0; i < _availableResolutions.Count; i++)
            {
                var r = _availableResolutions[i];
                var item = new Michsky.UI.Heat.Dropdown.Item
                {
                    itemName = $"{r.width} x {r.height}",
                    localizationKey = string.Empty
                };
                resolutionDropdown.items.Add(item);
            }

            resolutionDropdown.saveSelected = false;
            resolutionDropdown.selectedItemIndex = curIdx;
            resolutionDropdown.Initialize();
            resolutionDropdown.SetDropdownIndex(curIdx);

            resolutionDropdown.onValueChanged.RemoveAllListeners();
            resolutionDropdown.onValueChanged.AddListener(index =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                if (index >= 0 && index < _availableResolutions.Count)
                {
                    var chosen = _availableResolutions[index];
                    GameSettingsManager.Instance.resolutionWidth = chosen.width;
                    GameSettingsManager.Instance.resolutionHeight = chosen.height;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                }
            });
        }

        if (resolutionSelector != null)
        {
            string[] options = _availableResolutions.Select(r => $"{r.width} x {r.height}").ToArray();
            ConfigureSelectorComponent(resolutionSelector, entry, options, curIdx, index =>
            {
                if (_isInitializing || GameSettingsManager.Instance == null) return;
                if (index >= 0 && index < _availableResolutions.Count)
                {
                    var chosen = _availableResolutions[index];
                    GameSettingsManager.Instance.resolutionWidth = chosen.width;
                    GameSettingsManager.Instance.resolutionHeight = chosen.height;
                    GameSettingsManager.Instance.SaveSettings();
                    GameSettingsManager.Instance.ApplySettings();
                }
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
        var found = settingDescriptions.Find(e => 
            e.elementName.Equals(key, StringComparison.OrdinalIgnoreCase) ||
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

        if (_availableResolutions != null && _availableResolutions.Count > 0)
        {
            int idx = _availableResolutions.FindIndex(r => r.width == GameSettingsManager.Instance.resolutionWidth && r.height == GameSettingsManager.Instance.resolutionHeight);
            if (idx >= 0)
            {
                if (resolutionDropdown != null) resolutionDropdown.SetDropdownIndex(idx);
                if (resolutionSelector != null) SetSelectorIndex(resolutionSelector, idx);
            }
        }

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
        Transform textChild = root.Find("Label") ?? root.Find("Text");
        if (textChild == null)
        {
            foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            {
                if (c.name.Equals("Label", StringComparison.OrdinalIgnoreCase) || c.name.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    if (c.parent != null && c.parent.name.Equals("Header", StringComparison.OrdinalIgnoreCase)) continue;
                    textChild = c;
                    break;
                }
            }
        }

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
        if (root == null || entry == null) return;

        // 1. Configure Michsky's SettingsDescription component on root, child, or parent
        var descComp = root.GetComponentInChildren<SettingsDescription>(true) ?? root.GetComponentInParent<SettingsDescription>();
        if (descComp != null)
        {
            var type = typeof(SettingsDescription);
            var tField = type.GetField("title", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (tField != null) tField.SetValue(descComp, entry.displayTitle);

            var dField = type.GetField("description", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (dField != null) dField.SetValue(descComp, entry.description);

            var cField = type.GetField("cover", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (cField != null && entry.coverImage != null) cField.SetValue(descComp, entry.coverImage);

            var tkField = type.GetField("titleKey", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (tkField != null) tkField.SetValue(descComp, string.Empty);

            var dkField = type.GetField("descriptionKey", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (dkField != null) dkField.SetValue(descComp, string.Empty);
        }

        // 2. Direct SettingsElement event binding on root, child, or parent
        var element = root.GetComponentInChildren<SettingsElement>(true) ?? root.GetComponentInParent<SettingsElement>();
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
            element.onLeave.RemoveAllListeners();
            element.onLeave.AddListener(() =>
            {
                if (descriptionManager != null)
                {
                    descriptionManager.SetDefault();
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
