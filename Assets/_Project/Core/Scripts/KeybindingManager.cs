using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Supported action types available for keybinding configuration and UI mapping.
/// </summary>
public enum KeybindingActionType
{
    Sprint,
    Jump,
    Manifest,
    Exorcism,
    Heal,
    Resist,
    Deal,
    ExitPossession,
    Loot,
    SpectateCycle,
    SpectateExit,
    Summon,
    Possession,
    SpectatePrev,
    SpectateNext,
    SpectateViewMode,
    SpectateCursor,
    SpectateCategory,
    CommandHunt,
    CommandRecall,
    Attack,
    Reload,
    Holster,
    EquipWeapon1,
    EquipWeapon2,
    VoiceChat,
    ShadowTeleport,
    SpectatePlayer,
    SpectateMonster
}

/// <summary>
/// Execution and role context for game actions.
/// Used to separate mutually exclusive control schemas (e.g. Explorer vs. Spirit vs. Spectator)
/// while guaranteeing global actions (Sprint, Jump, Voice) never collide with any role.
/// </summary>
public enum ActionContext
{
    Global,     // Universal controls across all roles (Sprint, Jump, Voice Chat)
    Explorer,   // Living human investigator actions (Attack, Reload, Holster, Heal, Loot, etc.)
    Spirit,     // The Vengeful Spirit entity powers (Manifest, Teleport, Summon, Possession, Deal, etc.)
    Spectator   // Dead spectator camera and target switching controls
}

/// <summary>
/// SOLID — SRP: Manages customizable keyboard, mouse, and gamepad bindings
/// with persistent PlayerPrefs storage, interactive rebinding (including universal mouse buttons),
/// dynamic prompt formatting, context-aware duplicate detection, and live device detection.
/// </summary>
public class KeybindingManager : MonoBehaviour
{
    public static KeybindingManager Instance { get; private set; }

    public static event Action OnBindingsChanged;
    public static event Action<bool> OnDeviceTypeChanged; // bool isGamepad

    public const int CURRENT_BINDING_VERSION = 3;

    /// <summary>Keys the game uses for fixed functions (movement / pause / UI confirm). They can never be bound to an action.</summary>
    public static readonly Key[] ReservedKeys = { Key.W, Key.A, Key.S, Key.D, Key.Escape, Key.Enter, Key.NumpadEnter };

    /// <summary>Gamepad controls reserved for fixed functions (pause menu).</summary>
    public static readonly string[] ReservedGamepadControls = { "start" };

    // HotkeyEvents that mirror an action binding (re-pointed whenever bindings change)
    private static readonly Dictionary<Michsky.UI.Heat.HotkeyEvent, string> _boundHotkeys = new Dictionary<Michsky.UI.Heat.HotkeyEvent, string>();

    [System.Serializable]
    public class ActionBinding
    {
        public string actionId;
        public string displayName;
        [TextArea(2, 4)]
        public string tacticalDescription;
        public ActionContext context;
        public Key defaultKey = Key.None;
        [Tooltip("-1 for Keyboard Key. 0: LMB, 1: RMB, 2: MMB, 3: MB4 (Forward), 4: MB5 (Back)")]
        public int defaultMouseButton = -1;
        public string defaultGamepadControl; // e.g. "buttonSouth", "buttonWest", etc.

        // Runtime State
        [HideInInspector] public Key currentKey;
        [HideInInspector] public int currentMouseButton = -1;
        [HideInInspector] public string currentGamepadControl;
    }

    [Header("Registered Game Actions")]
    public List<ActionBinding> actions = new List<ActionBinding>();

    private Coroutine _rebindCoroutine;
    public bool IsRebinding => _rebindCoroutine != null;

    private static bool _lastUsedDeviceIsGamepad = false;

    // Sprite resolver delegate provided by HeatSettingsBridge or custom icon providers
    public static Func<string, Sprite> CustomGamepadSpriteResolver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("[KeybindingManager]");
            Instance = go.AddComponent<KeybindingManager>();
            DontDestroyOnLoad(go);
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeDefaults();
            LoadBindings();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (IsRebinding) return;

        // Auto-detect active input device
        if (Gamepad.current != null)
        {
            foreach (var control in Gamepad.current.allControls)
            {
                if (control is ButtonControl btn && btn.wasPressedThisFrame)
                {
                    SetGamepadActive(true);
                    break;
                }
            }
        }

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            SetGamepadActive(false);
        }
        else if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame ||
                                           Mouse.current.rightButton.wasPressedThisFrame ||
                                           Mouse.current.middleButton.wasPressedThisFrame ||
                                           Mouse.current.forwardButton.wasPressedThisFrame ||
                                           Mouse.current.backButton.wasPressedThisFrame))
        {
            SetGamepadActive(false);
        }
    }

    private static void SetGamepadActive(bool isGamepad)
    {
        if (_lastUsedDeviceIsGamepad != isGamepad)
        {
            _lastUsedDeviceIsGamepad = isGamepad;
            OnDeviceTypeChanged?.Invoke(isGamepad);
        }
    }

    public static bool IsGamepadActive()
    {
        return _lastUsedDeviceIsGamepad && Gamepad.current != null;
    }

    public static bool IsPlayStationActive()
    {
        if (Gamepad.current == null) return false;
        if (Gamepad.current is UnityEngine.InputSystem.DualShock.DualShockGamepad) return true;
        string n = (Gamepad.current.displayName ?? "").ToLower();
        string l = (Gamepad.current.layout ?? "").ToLower();
        return n.Contains("dual") || n.Contains("playstation") || n.Contains("sony") || n.Contains("ps4") || n.Contains("ps5") || l.Contains("dual") || l.Contains("playstation");
    }

    public void InitializeDefaults()
    {
        var defaultList = new List<ActionBinding>()
        {
            // =========================================================================
            //  1. Global Traversal & Communication (Shared Across All Roles)
            // =========================================================================
            new ActionBinding
            {
                actionId = "Sprint",
                displayName = "Sprint / Tactical Rush",
                tacticalDescription = "Accelerate traversal across open mining caverns. Depletes stamina; increases acoustic profile.",
                context = ActionContext.Global,
                defaultKey = Key.LeftShift,
                defaultMouseButton = -1,
                defaultGamepadControl = "leftStickPress" // L3
            },
            new ActionBinding
            {
                actionId = "Jump",
                displayName = "Jump / Vault Obstacles",
                tacticalDescription = "Leap over collapsed mine rails, low pipes, and treacherous rock fissures.",
                context = ActionContext.Global,
                defaultKey = Key.Space,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonSouth" // A / Cross
            },
            new ActionBinding
            {
                actionId = "VoiceChat",
                displayName = "Push-to-Talk (Radio)",
                tacticalDescription = "Transmit tactical radio communication across the subterranean network.",
                context = ActionContext.Global,
                defaultKey = Key.V,
                defaultMouseButton = -1,
                defaultGamepadControl = "rightStickPress" // R3
            },

            // =========================================================================
            //  2. Explorer Combat & Survival (Investigator Role)
            // =========================================================================
            new ActionBinding
            {
                actionId = "Attack",
                displayName = "Attack / Fire Weapon",
                tacticalDescription = "Execute tactical melee strikes with pickaxe or fire equipped firearms.",
                context = ActionContext.Explorer,
                defaultKey = Key.None,
                defaultMouseButton = 0, // [LMB] Universal Mouse Binding
                defaultGamepadControl = "rightTrigger" // RT / R2
            },
            new ActionBinding
            {
                actionId = "Reload",
                displayName = "Reload Firearm",
                tacticalDescription = "Chamber ammunition into your equipped firearm.",
                context = ActionContext.Explorer,
                defaultKey = Key.R,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonWest" // X / Square
            },
            new ActionBinding
            {
                actionId = "Holster",
                displayName = "Holster / Stow Weapon",
                tacticalDescription = "Conceal equipped weaponry to maintain low acoustic and visual profile.",
                context = ActionContext.Explorer,
                defaultKey = Key.Z,
                defaultMouseButton = -1,
                defaultGamepadControl = "leftShoulder" // LB / L1
            },
            new ActionBinding
            {
                actionId = "EquipWeapon1",
                displayName = "Equip Primary Weapon",
                tacticalDescription = "Draw primary tactical pickaxe or melee tool.",
                context = ActionContext.Explorer,
                defaultKey = Key.Digit1,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadLeft"
            },
            new ActionBinding
            {
                actionId = "EquipWeapon2",
                displayName = "Equip Secondary Weapon",
                tacticalDescription = "Draw secondary firearm if acquired from fallen miners or supply caches.",
                context = ActionContext.Explorer,
                defaultKey = Key.Digit2,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadRight"
            },
            new ActionBinding
            {
                actionId = "Heal",
                displayName = "Administer Medical Vial",
                tacticalDescription = "Inject coagulant from your medical vial supply to rapidly stabilize critical trauma and restore vital health points.",
                context = ActionContext.Explorer,
                defaultKey = Key.H,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadUp"
            },
            new ActionBinding
            {
                actionId = "Loot",
                displayName = "Loot Bodies & Containers",
                tacticalDescription = "Scavenge fallen explorer corpses, extraction batteries, supply crates, and medical vials.",
                context = ActionContext.Explorer,
                defaultKey = Key.E,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonEast" // B / Circle
            },
            new ActionBinding
            {
                actionId = "Exorcism",
                displayName = "Holy Exorcism Rite",
                tacticalDescription = "Channel sanctified rite against possessed companions to purge the invading demon and heavily drain her possession reserves.",
                context = ActionContext.Explorer,
                defaultKey = Key.G,
                defaultMouseButton = -1,
                defaultGamepadControl = "leftTrigger" // LT / L2
            },
            new ActionBinding
            {
                actionId = "Resist",
                displayName = "Resist Possession",
                tacticalDescription = "Fight back against demonic host intrusion and reclaim somatic motor control during struggle.",
                context = ActionContext.Explorer,
                defaultKey = Key.F,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },

            // =========================================================================
            //  3. Vengeful Spirit Powers & Rites (The Girl Entity Role)
            // =========================================================================
            new ActionBinding
            {
                actionId = "Manifest",
                displayName = "Physical Manifestation",
                tacticalDescription = "Shift from incorporeal spirit form into physical reality to execute lethal strikes and hunt explorers. Consumes manifestation charges.",
                context = ActionContext.Spirit,
                defaultKey = Key.T,
                defaultMouseButton = -1,
                defaultGamepadControl = "rightShoulder" // RB / R1
            },
            new ActionBinding
            {
                actionId = "ShadowTeleport",
                displayName = "Shadow Teleport",
                tacticalDescription = "Vanish and remanifest instantaneously behind unsuspecting explorers.",
                context = ActionContext.Spirit,
                defaultKey = Key.Q,
                defaultMouseButton = -1,
                defaultGamepadControl = "leftShoulder" // LB / L1
            },
            new ActionBinding
            {
                actionId = "Summon",
                displayName = "Monster Summon Rite",
                tacticalDescription = "Open the necrotic summon rites menu to raise Undead and Berserker abominations in the subterranean mines.",
                context = ActionContext.Spirit,
                defaultKey = Key.X,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadUp"
            },
            new ActionBinding
            {
                actionId = "Possession",
                displayName = "Possession Selection Menu",
                tacticalDescription = "Open the possession target selection modal to choose an investigator host to inhabit.",
                context = ActionContext.Spirit,
                defaultKey = Key.P,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadDown"
            },
            new ActionBinding
            {
                actionId = "ExitPossession",
                displayName = "Exit Possession",
                tacticalDescription = "Voluntarily terminate host possession to preserve ethereal essence and revert to incorporeal ghost form.",
                context = ActionContext.Spirit,
                defaultKey = Key.K,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonWest" // X / Square
            },
            new ActionBinding
            {
                actionId = "Deal",
                displayName = "Deal Activations",
                tacticalDescription = "Initiate and negotiate forbidden subterranean deals and demonic blood pacts with the Vengeful Spirit.",
                context = ActionContext.Spirit,
                defaultKey = Key.B,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },
            new ActionBinding
            {
                actionId = "CommandHunt",
                displayName = "Command: Go Hunt",
                tacticalDescription = "Command all summoned monsters to roam and hunt down surviving investigators.",
                context = ActionContext.Spirit,
                defaultKey = Key.Digit3,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadLeft"
            },
            new ActionBinding
            {
                actionId = "CommandRecall",
                displayName = "Command: To My Side",
                tacticalDescription = "Command all summoned monsters to return and stand guard beside the Vengeful Spirit.",
                context = ActionContext.Spirit,
                defaultKey = Key.Digit4,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadRight"
            },

            // =========================================================================
            //  4. Spectator Observation Controls (Post-Mortem Role)
            // =========================================================================
            new ActionBinding
            {
                actionId = "SpectateCycle",
                displayName = "Spectate Cycle Target",
                tacticalDescription = "Cycle camera view between surviving explorers or stalking demonic entities during spectator observation.",
                context = ActionContext.Spectator,
                defaultKey = Key.Tab,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonWest" // X / Square
            },
            new ActionBinding
            {
                actionId = "SpectatePrev",
                displayName = "Spectate Previous Target",
                tacticalDescription = "Cycle camera to the previous living survivor or summoned monster.",
                context = ActionContext.Spectator,
                defaultKey = Key.LeftArrow,
                defaultMouseButton = -1,
                defaultGamepadControl = "leftShoulder" // LB / L1
            },
            new ActionBinding
            {
                actionId = "SpectateNext",
                displayName = "Spectate Next Target",
                tacticalDescription = "Cycle camera to the next living survivor or summoned monster.",
                context = ActionContext.Spectator,
                defaultKey = Key.RightArrow,
                defaultMouseButton = -1,
                defaultGamepadControl = "rightShoulder" // RB / R1
            },
            new ActionBinding
            {
                actionId = "SpectateViewMode",
                displayName = "Spectate View Mode",
                tacticalDescription = "Toggle between tactical free orbit mouse look and follow over-the-shoulder camera.",
                context = ActionContext.Spectator,
                defaultKey = Key.M,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadUp"
            },
            new ActionBinding
            {
                actionId = "SpectateCursor",
                displayName = "Spectate Cursor Lock",
                tacticalDescription = "Lock or release mouse cursor while spectating.",
                context = ActionContext.Spectator,
                defaultKey = Key.LeftAlt,
                defaultMouseButton = -1,
                defaultGamepadControl = "dpadDown"
            },
            new ActionBinding
            {
                actionId = "SpectateCategory",
                displayName = "Spectate Switch Category",
                tacticalDescription = "Switch spectator observation between living human survivors and summoned monsters.",
                context = ActionContext.Spectator,
                defaultKey = Key.N,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },
            new ActionBinding
            {
                actionId = "SpectatePlayer",
                displayName = "Spectate Survivors",
                tacticalDescription = "Enter spectator mode from the death screen to follow surviving teammates through the mine. This key is shown on the YOU DIED screen prompt.",
                context = ActionContext.Spectator,
                defaultKey = Key.O,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonEast" // B / Circle
            },
            new ActionBinding
            {
                actionId = "SpectateMonster",
                displayName = "Spectate Monsters",
                tacticalDescription = "Enter monster spectator mode from the Summon HUD to watch your summoned zombies and berserkers hunt. This key is shown on the Monster Summoning prompt.",
                context = ActionContext.Spirit,
                defaultKey = Key.U,
                defaultMouseButton = -1,
                defaultGamepadControl = "buttonEast" // B / Circle
            },
            new ActionBinding
            {
                actionId = "SpectateExit",
                displayName = "Exit Spectator Mode",
                tacticalDescription = "Exit spectator observation mode and return to your player view.",
                context = ActionContext.Spectator,
                defaultKey = Key.C,
                defaultMouseButton = -1,
                defaultGamepadControl = "select" // View / Share
            }
        };

        actions = new List<ActionBinding>(defaultList);

        foreach (var act in actions)
        {
            act.currentKey = act.defaultKey;
            act.currentMouseButton = act.defaultMouseButton;
            act.currentGamepadControl = act.defaultGamepadControl;
        }
    }

    public void LoadBindings()
    {
        int savedVersion = PlayerPrefs.GetInt("NC_Bind_Version", 0);
        if (savedVersion < CURRENT_BINDING_VERSION)
        {
            // Automatically migrate to new non-overlapping preset table
            ResetAllBindings();
            PlayerPrefs.SetInt("NC_Bind_Version", CURRENT_BINDING_VERSION);
            PlayerPrefs.Save();
            return;
        }

        foreach (var act in actions)
        {
            // 1. Mouse Button
            act.currentMouseButton = PlayerPrefs.GetInt($"NC_Bind_Mouse_{act.actionId}", act.defaultMouseButton);

            // 2. Keyboard Key
            string keyStr = PlayerPrefs.GetString($"NC_Bind_Key_{act.actionId}", act.defaultKey.ToString());
            if (Enum.TryParse<Key>(keyStr, out Key loadedKey))
            {
                act.currentKey = loadedKey;
            }
            else
            {
                act.currentKey = act.defaultKey;
            }

            // 3. Gamepad Control
            act.currentGamepadControl = PlayerPrefs.GetString($"NC_Bind_Pad_{act.actionId}", act.defaultGamepadControl);
        }

        if (ResolveDuplicateBindings())
        {
            SaveBindings();
        }
    }

    // =========================================================================
    //  Duplicate Resolution
    // =========================================================================
    private static readonly Key[] FallbackKeyPool =
    {
        Key.J, Key.L, Key.I, Key.Y, Key.Comma, Key.Period, Key.Slash, Key.Semicolon, Key.Quote,
        Key.LeftBracket, Key.RightBracket, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.UpArrow, Key.DownArrow,
        Key.RightAlt, Key.RightCtrl, Key.RightShift, Key.Backquote, Key.Minus, Key.Equals
    };

    private static readonly string[] FallbackPadPool =
    {
        "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "leftTrigger", "rightTrigger", "dpadUp", "dpadDown", "dpadLeft", "dpadRight",
        "leftStickPress", "rightStickPress", "select"
    };

    private static bool SameKeyboardMouseInput(ActionBinding a, ActionBinding b)
    {
        if (a.currentMouseButton >= 0 || b.currentMouseButton >= 0)
            return a.currentMouseButton >= 0 && a.currentMouseButton == b.currentMouseButton;
        return a.currentKey != Key.None && a.currentKey == b.currentKey;
    }

    private static bool SamePadInput(ActionBinding a, ActionBinding b) =>
        !string.IsNullOrEmpty(a.currentGamepadControl) &&
        string.Equals(a.currentGamepadControl, b.currentGamepadControl, StringComparison.OrdinalIgnoreCase);

    private bool IsKeyboardMouseTaken(ActionBinding self)
    {
        if (self.currentMouseButton < 0 && Array.IndexOf(ReservedKeys, self.currentKey) >= 0) return true;
        return actions.Exists(o => o != self && SameKeyboardMouseInput(self, o));
    }

    private bool IsPadTaken(ActionBinding self)
    {
        if (Array.Exists(ReservedGamepadControls, r => r.Equals(self.currentGamepadControl, StringComparison.OrdinalIgnoreCase))) return true;
        return actions.Exists(o => o != self && CanConflict(self, o) && SamePadInput(self, o));
    }

    /// <summary>
    /// Guarantees no two actions share a keyboard key or mouse button (globally), and no two actions that
    /// can be active at the same time share a gamepad button. The later duplicate is moved back to its
    /// default, or to the first free input if its default is also taken. Returns true if anything changed.
    /// </summary>
    public bool ResolveDuplicateBindings()
    {
        bool changed = false;
        foreach (var act in actions)
        {
            if (IsKeyboardMouseTaken(act))
            {
                act.currentMouseButton = act.defaultMouseButton;
                act.currentKey = act.defaultKey;
                if (IsKeyboardMouseTaken(act))
                {
                    act.currentMouseButton = -1;
                    foreach (var k in FallbackKeyPool)
                    {
                        act.currentKey = k;
                        if (!IsKeyboardMouseTaken(act)) break;
                    }
                }
                changed = true;
            }

            if (IsPadTaken(act))
            {
                act.currentGamepadControl = act.defaultGamepadControl;
                if (IsPadTaken(act))
                {
                    string free = null;
                    foreach (var p in FallbackPadPool)
                    {
                        act.currentGamepadControl = p;
                        if (!IsPadTaken(act)) { free = p; break; }
                    }
                    act.currentGamepadControl = free; // null = unbound if the pad is fully saturated for this role
                }
                changed = true;
            }
        }
        return changed;
    }

    public void SaveBindings()
    {
        foreach (var act in actions)
        {
            PlayerPrefs.SetInt($"NC_Bind_Mouse_{act.actionId}", act.currentMouseButton);
            PlayerPrefs.SetString($"NC_Bind_Key_{act.actionId}", act.currentKey.ToString());
            PlayerPrefs.SetString($"NC_Bind_Pad_{act.actionId}", act.currentGamepadControl);
        }
        PlayerPrefs.SetInt("NC_Bind_Version", CURRENT_BINDING_VERSION);
        PlayerPrefs.Save();
        ApplyToAllPlayerInputs();
        RefreshBoundHotkeys();
        OnBindingsChanged?.Invoke();
    }

    // =========================================================================
    //  Heat HotkeyEvent Mirroring
    // =========================================================================
    /// <summary>
    /// Builds a fresh InputAction containing ONLY the user's current keyboard/mouse and gamepad binding for an action.
    /// </summary>
    public static InputAction CreateInputActionFor(string actionId)
    {
        var ia = new InputAction($"NC_{actionId}", InputActionType.Button);
        if (Instance == null) return ia;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) return ia;

        if (act.currentMouseButton >= 0) ia.AddBinding(GetInputControlPathForMouse(act.currentMouseButton));
        else if (act.currentKey != Key.None) ia.AddBinding(GetInputControlPath(act.currentKey));

        if (!string.IsNullOrEmpty(act.currentGamepadControl)) ia.AddBinding($"<Gamepad>/{act.currentGamepadControl}");
        return ia;
    }

    /// <summary>
    /// Replaces a Heat HotkeyEvent's scene-authored InputAction with one that follows the given action binding.
    /// The old InputAction is disabled and disposed so its original key (e.g. Space) stops firing.
    /// The HotkeyEvent is kept in sync automatically whenever the player rebinds.
    /// </summary>
    public static void BindHotkeyEvent(Michsky.UI.Heat.HotkeyEvent hotkeyEvent, string actionId)
    {
        if (hotkeyEvent == null || string.IsNullOrEmpty(actionId)) return;
        _boundHotkeys[hotkeyEvent] = actionId;
        RepointHotkey(hotkeyEvent, actionId);
    }

    /// <summary>Stops mirroring an action on a HotkeyEvent (the current InputAction is left as is).</summary>
    public static void UnbindHotkeyEvent(Michsky.UI.Heat.HotkeyEvent hotkeyEvent)
    {
        if (hotkeyEvent != null) _boundHotkeys.Remove(hotkeyEvent);
    }

    private static void RepointHotkey(Michsky.UI.Heat.HotkeyEvent he, string actionId)
    {
        var old = he.hotkey;
        bool wasEnabled = old != null && old.enabled;
        if (old != null)
        {
            old.Disable();
            try { old.Dispose(); } catch { }
        }

        he.hotkey = CreateInputActionFor(actionId);
        if (wasEnabled) he.hotkey.Enable();
    }

    private static void RefreshBoundHotkeys()
    {
        var dead = new List<Michsky.UI.Heat.HotkeyEvent>();
        foreach (var kv in _boundHotkeys)
        {
            if (kv.Key == null) { dead.Add(kv.Key); continue; }
            RepointHotkey(kv.Key, kv.Value);
        }
        foreach (var d in dead) _boundHotkeys.Remove(d);
    }

    public void ResetAllBindings()
    {
        foreach (var act in actions)
        {
            act.currentMouseButton = act.defaultMouseButton;
            act.currentKey = act.defaultKey;
            act.currentGamepadControl = act.defaultGamepadControl;
        }
        SaveBindings();
    }

    // =========================================================================
    //  Interactive Rebinding (Universal Keyboard & Mouse + Gamepad)
    // =========================================================================
    public void StartRebindKeyboard(string actionId, Action<string> onWaiting, Action<string> onComplete)
    {
        if (_rebindCoroutine != null) StopCoroutine(_rebindCoroutine);
        _rebindCoroutine = StartCoroutine(RebindKeyboardRoutine(actionId, onWaiting, onComplete));
    }

    public void StartRebindGamepad(string actionId, Action<string> onWaiting, Action<string> onComplete)
    {
        if (_rebindCoroutine != null) StopCoroutine(_rebindCoroutine);
        _rebindCoroutine = StartCoroutine(RebindGamepadRoutine(actionId, onWaiting, onComplete));
    }

    /// <summary>
    /// Checks whether two actions can conflict.
    /// Actions in the same context conflict. Actions with Global context conflict with all actions.
    /// Mutually exclusive roles (Explorer vs Spirit vs Spectator) do not conflict.
    /// </summary>
    public static bool CanConflict(ActionBinding a, ActionBinding b)
    {
        if (a == null || b == null) return false;
        if (a.context == ActionContext.Global || b.context == ActionContext.Global) return true;
        return a.context == b.context;
    }

    private IEnumerator RebindKeyboardRoutine(string actionId, Action<string> onWaiting, Action<string> onComplete)
    {
        var act = actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) yield break;

        onWaiting?.Invoke("<color=#F1C40F>[PRESS ANY KEY OR MOUSE BUTTON...]</color>");

        // Wait until mouse button is released so the click that initiated the rebind is not registered
        yield return null;
        while (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            yield return null;
        }

        bool bound = false;
        while (!bound)
        {
            // Escape cancels rebinding
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                break;
            }

            // 1. Check Mouse Buttons (LMB = 0, RMB = 1, MMB = 2, MB4 = 3, MB5 = 4)
            int detectedMouseBtn = -1;
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame) detectedMouseBtn = 0;
                else if (Mouse.current.rightButton.wasPressedThisFrame) detectedMouseBtn = 1;
                else if (Mouse.current.middleButton.wasPressedThisFrame) detectedMouseBtn = 2;
                else if (Mouse.current.forwardButton.wasPressedThisFrame) detectedMouseBtn = 3;
                else if (Mouse.current.backButton.wasPressedThisFrame) detectedMouseBtn = 4;
            }

            if (detectedMouseBtn >= 0)
            {
                // Mouse buttons must be unique across every action (keyboard/mouse is never shared)
                ActionBinding conflict = actions.Find(other => other != act && other.currentMouseButton == detectedMouseBtn);
                if (conflict != null)
                {
                    string btnName = FormatMouseButtonName(detectedMouseBtn);
                    ShowConflictError("Binding Conflict", $"{btnName} is already assigned to {conflict.displayName}. Choose a free input or rebind that action first.");
                    onComplete?.Invoke(FormatActionInput(act));
                    _rebindCoroutine = null;
                    yield break;
                }

                act.currentMouseButton = detectedMouseBtn;
                act.currentKey = Key.None; // Clear keyboard key since mouse is mapped
                bound = true;
                break;
            }

            // 2. Check Keyboard Keys
            if (Keyboard.current != null)
            {
                foreach (var control in Keyboard.current.allControls)
                {
                    if (control is KeyControl keyControl && keyControl.wasPressedThisFrame)
                    {
                        Key newKey = keyControl.keyCode;
                        if (newKey == Key.None || newKey == Key.Escape) break;

                        if (Array.IndexOf(ReservedKeys, newKey) >= 0)
                        {
                            ShowConflictError("Reserved Key", $"{FormatKeyName(newKey)} is reserved for movement or menus and can't be assigned.");
                            onComplete?.Invoke(FormatActionInput(act));
                            _rebindCoroutine = null;
                            yield break;
                        }

                        // Keyboard keys must be unique across every action
                        ActionBinding conflict = actions.Find(other => other != act && other.currentMouseButton < 0 && other.currentKey == newKey && other.currentKey != Key.None);
                        if (conflict != null)
                        {
                            string keyName = FormatKeyName(newKey);
                            ShowConflictError("Key Conflict", $"{keyName} is already assigned to {conflict.displayName}. Choose a free key or rebind that action first.");
                            onComplete?.Invoke(FormatActionInput(act));
                            _rebindCoroutine = null;
                            yield break;
                        }

                        act.currentKey = newKey;
                        act.currentMouseButton = -1; // Clear mouse since keyboard key is mapped
                        bound = true;
                        break;
                    }
                }
            }

            yield return null;
        }

        SaveBindings();
        onComplete?.Invoke(FormatActionInput(act));
        _rebindCoroutine = null;
    }

    private IEnumerator RebindGamepadRoutine(string actionId, Action<string> onWaiting, Action<string> onComplete)
    {
        var act = actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) yield break;

        onWaiting?.Invoke("<color=#F1C40F>[PRESS BUTTON...]</color>");
        yield return null;

        bool bound = false;
        while (!bound)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                break;
            }

            if (Gamepad.current != null)
            {
                foreach (var control in Gamepad.current.allControls)
                {
                    if (control is ButtonControl btnControl && btnControl.wasPressedThisFrame)
                    {
                        string newControl = btnControl.name;
                        if (string.IsNullOrEmpty(newControl)) break;

                        if (Array.Exists(ReservedGamepadControls, r => r.Equals(newControl, StringComparison.OrdinalIgnoreCase)))
                        {
                            ShowConflictError("Reserved Button", $"{FormatGamepadName(newControl, IsPlayStationActive())} opens the pause menu and can't be assigned.");
                            onComplete?.Invoke(FormatGamepadName(act.currentGamepadControl, IsPlayStationActive()));
                            _rebindCoroutine = null;
                            yield break;
                        }

                        // Gamepad buttons must be unique among actions that can be active at the same time
                        ActionBinding conflict = actions.Find(other => other != act && CanConflict(act, other) && !string.IsNullOrEmpty(other.currentGamepadControl) && other.currentGamepadControl.Equals(newControl, StringComparison.OrdinalIgnoreCase));
                        if (conflict != null)
                        {
                            string btnName = FormatGamepadName(newControl, IsPlayStationActive());
                            ShowConflictError("Button Conflict", $"{btnName} is already assigned to {conflict.displayName}. Choose a free button or rebind that action first.");
                            onComplete?.Invoke(FormatGamepadName(act.currentGamepadControl, IsPlayStationActive()));
                            _rebindCoroutine = null;
                            yield break;
                        }

                        act.currentGamepadControl = newControl;
                        bound = true;
                        break;
                    }
                }
            }

            yield return null;
        }

        SaveBindings();
        onComplete?.Invoke(FormatGamepadName(act.currentGamepadControl, IsPlayStationActive()));
        _rebindCoroutine = null;
    }

    /// <summary>
    /// Displays a binding conflict error via the Error Modal in Pause Canvas / Settings, or via NotificationManager.
    /// </summary>
    public static void ShowConflictError(string title, string description)
    {
        if (HeatSettingsBridge.Instance != null && HeatSettingsBridge.Instance.errorModal != null)
        {
            HeatSettingsBridge.Instance.ShowErrorModal(title, description);
            return;
        }

        if (PauseUI.Instance != null && PauseUI.Instance.errorModal != null)
        {
            PauseUI.Instance.ShowErrorModal(title, description);
            return;
        }

        if (NotificationManager.Instance != null)
        {
            NotificationManager.Instance.ShowNotification(title, description, 4.5f);
        }
    }

    public static void ApplyToPlayerInput(PlayerInput playerInput)
    {
        if (playerInput == null || playerInput.actions == null || Instance == null) return;

        var jumpAct = playerInput.actions.FindAction("Jump");
        if (jumpAct != null)
        {
            var jumpBinding = Instance.actions.Find(a => a.actionId.Equals("Jump", StringComparison.OrdinalIgnoreCase));
            if (jumpBinding != null)
            {
                if (jumpBinding.currentMouseButton >= 0)
                {
                    ApplyBindingOverrideToGroup(jumpAct, "KeyboardMouse", GetInputControlPathForMouse(jumpBinding.currentMouseButton));
                }
                else if (jumpBinding.currentKey != Key.None)
                {
                    string kPath = GetInputControlPath(jumpBinding.currentKey);
                    ApplyBindingOverrideToGroup(jumpAct, "KeyboardMouse", kPath);
                }

                if (!string.IsNullOrEmpty(jumpBinding.currentGamepadControl))
                {
                    ApplyBindingOverrideToGroup(jumpAct, "Gamepad", $"<Gamepad>/{jumpBinding.currentGamepadControl}");
                }
            }
        }

        var sprintAct = playerInput.actions.FindAction("Sprint");
        if (sprintAct != null)
        {
            var sprintBinding = Instance.actions.Find(a => a.actionId.Equals("Sprint", StringComparison.OrdinalIgnoreCase));
            if (sprintBinding != null)
            {
                if (sprintBinding.currentMouseButton >= 0)
                {
                    ApplyBindingOverrideToGroup(sprintAct, "KeyboardMouse", GetInputControlPathForMouse(sprintBinding.currentMouseButton));
                }
                else if (sprintBinding.currentKey != Key.None)
                {
                    string kPath = GetInputControlPath(sprintBinding.currentKey);
                    ApplyBindingOverrideToGroup(sprintAct, "KeyboardMouse", kPath);
                }

                if (!string.IsNullOrEmpty(sprintBinding.currentGamepadControl))
                {
                    ApplyBindingOverrideToGroup(sprintAct, "Gamepad", $"<Gamepad>/{sprintBinding.currentGamepadControl}");
                }
            }
        }
    }

    public static void ApplyToAllPlayerInputs()
    {
        var playerInputs = UnityEngine.Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
        foreach (var pi in playerInputs)
        {
            ApplyToPlayerInput(pi);
        }
    }

    private static void ApplyBindingOverrideToGroup(InputAction action, string group, string newPath)
    {
        if (action == null || string.IsNullOrEmpty(newPath)) return;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (!string.IsNullOrEmpty(b.groups) && b.groups.Contains(group))
            {
                action.ApplyBindingOverride(i, newPath);
            }
        }
    }

    private static string GetInputControlPath(Key key)
    {
        if (key == Key.None) return string.Empty;
        if (Keyboard.current != null)
        {
            try
            {
                var ctrl = Keyboard.current[key];
                if (ctrl != null && !string.IsNullOrEmpty(ctrl.path)) return ctrl.path;
            }
            catch { }
        }
        string keyStr = key.ToString();
        return $"<Keyboard>/{char.ToLower(keyStr[0])}{(keyStr.Length > 1 ? keyStr.Substring(1) : string.Empty)}";
    }

    private static string GetInputControlPathForMouse(int buttonIndex)
    {
        switch (buttonIndex)
        {
            case 0: return "<Mouse>/leftButton";
            case 1: return "<Mouse>/rightButton";
            case 2: return "<Mouse>/middleButton";
            case 3: return "<Mouse>/forwardButton";
            case 4: return "<Mouse>/backButton";
            default: return "<Mouse>/press";
        }
    }

    // =========================================================================
    //  Formatting & Lookup Helpers
    // =========================================================================
    public static string FormatMouseButtonName(int mouseBtn)
    {
        switch (mouseBtn)
        {
            case 0: return "[LMB]";
            case 1: return "[RMB]";
            case 2: return "[MMB]";
            case 3: return "[MB4]";
            case 4: return "[MB5]";
            default: return $"[M{mouseBtn + 1}]";
        }
    }

    public static string FormatMouseButtonRaw(int mouseBtn)
    {
        switch (mouseBtn)
        {
            case 0: return "LMB";
            case 1: return "RMB";
            case 2: return "MMB";
            case 3: return "MB4";
            case 4: return "MB5";
            default: return $"M{mouseBtn + 1}";
        }
    }

    public static string FormatActionInput(ActionBinding act)
    {
        if (act == null) return "[UNBOUND]";
        if (act.currentMouseButton >= 0)
        {
            return FormatMouseButtonName(act.currentMouseButton);
        }
        return FormatKeyName(act.currentKey);
    }

    public static string FormatActionInputRaw(ActionBinding act)
    {
        if (act == null) return "UNBOUND";
        if (act.currentMouseButton >= 0)
        {
            return FormatMouseButtonRaw(act.currentMouseButton);
        }
        return FormatKeyRaw(act.currentKey);
    }

    public static string FormatKeyName(Key key)
    {
        switch (key)
        {
            case Key.Space: return "[SPACE]";
            case Key.LeftShift: return "[L-SHIFT]";
            case Key.RightShift: return "[R-SHIFT]";
            case Key.LeftCtrl: return "[L-CTRL]";
            case Key.RightCtrl: return "[R-CTRL]";
            case Key.LeftAlt: return "[L-ALT]";
            case Key.RightAlt: return "[R-ALT]";
            case Key.Tab: return "[TAB]";
            case Key.Enter: return "[ENTER]";
            case Key.Escape: return "[ESC]";
            case Key.Backspace: return "[BACKSPACE]";
            case Key.CapsLock: return "[CAPS]";
            case Key.LeftArrow: return "[<--]";
            case Key.RightArrow: return "[-->]";
            case Key.UpArrow: return "[UP]";
            case Key.DownArrow: return "[DOWN]";
            default:
                string s = key.ToString();
                if (s.StartsWith("Digit")) return $"[{s.Substring(5)}]";
                return $"[{s.ToUpper()}]";
        }
    }

    public static string FormatKeyRaw(Key key)
    {
        switch (key)
        {
            case Key.Space: return "SPACE";
            case Key.LeftShift: return "L-SHIFT";
            case Key.RightShift: return "R-SHIFT";
            case Key.LeftCtrl: return "L-CTRL";
            case Key.RightCtrl: return "R-CTRL";
            case Key.LeftAlt: return "L-ALT";
            case Key.RightAlt: return "R-ALT";
            case Key.Tab: return "TAB";
            case Key.Enter: return "ENTER";
            case Key.Escape: return "ESC";
            case Key.Backspace: return "BACKSPACE";
            case Key.CapsLock: return "CAPS";
            case Key.LeftArrow: return "LEFT";
            case Key.RightArrow: return "RIGHT";
            case Key.UpArrow: return "UP";
            case Key.DownArrow: return "DOWN";
            default:
                string s = key.ToString();
                if (s.StartsWith("Digit")) return s.Substring(5);
                return s.ToUpper();
        }
    }

    public static string FormatGamepadName(string controlName, bool isPlayStation = false)
    {
        if (string.IsNullOrEmpty(controlName)) return "[UNBOUND]";

        switch (controlName.ToLower())
        {
            case "buttonsouth": return isPlayStation ? "[✕]" : "[A]";
            case "buttonnorth": return isPlayStation ? "[△]" : "[Y]";
            case "buttonwest":  return isPlayStation ? "[▢]" : "[X]";
            case "buttoneast":  return isPlayStation ? "[◯]" : "[B]";
            case "leftshoulder": return isPlayStation ? "[L1]" : "[LB]";
            case "rightshoulder": return isPlayStation ? "[R1]" : "[RB]";
            case "lefttrigger": return isPlayStation ? "[L2]" : "[LT]";
            case "righttrigger": return isPlayStation ? "[R2]" : "[RT]";
            case "leftstickpress": return isPlayStation ? "[L3]" : "[L-STICK]";
            case "rightstickpress": return isPlayStation ? "[R3]" : "[R-STICK]";
            case "dpadup": return "[D-PAD UP]";
            case "dpaddown": return "[D-PAD DOWN]";
            case "dpadleft": return "[D-PAD LEFT]";
            case "dpadright": return "[D-PAD RIGHT]";
            case "start": return isPlayStation ? "[OPTIONS]" : "[MENU]";
            case "select": return isPlayStation ? "[SHARE]" : "[VIEW]";
            default: return $"[{controlName.ToUpper()}]";
        }
    }

    public static Key GetBoundKey(string actionId, Key defaultFallback = Key.None)
    {
        if (Instance == null) return defaultFallback;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return act != null ? act.currentKey : defaultFallback;
    }

    public static string GetBoundKeyString(string actionId, string defaultFallback = "E")
    {
        if (Instance == null) return defaultFallback;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return act != null ? FormatActionInputRaw(act) : defaultFallback;
    }

    public static string GetBoundGamepadControl(string actionId, string defaultFallback = "buttonSouth")
    {
        if (Instance == null) return defaultFallback;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return act != null ? act.currentGamepadControl : defaultFallback;
    }

    public static string GetBoundGamepadString(string actionId, string defaultFallback = "A")
    {
        if (Instance == null) return defaultFallback;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return act != null ? FormatGamepadName(act.currentGamepadControl, IsPlayStationActive()).Replace("[", "").Replace("]", "") : defaultFallback;
    }

    public static string GetActionPrompt(string actionId, string actionVerb = "")
    {
        if (Instance == null)
            return string.IsNullOrEmpty(actionVerb) ? "[E]" : $"{actionVerb} [E]";

        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null)
            return string.IsNullOrEmpty(actionVerb) ? "[E]" : $"{actionVerb} [E]";

        string prompt = IsGamepadActive() 
            ? FormatGamepadName(act.currentGamepadControl, IsPlayStationActive())
            : FormatActionInput(act);

        return string.IsNullOrEmpty(actionVerb) ? prompt : $"{actionVerb} {prompt}";
    }

    public static Sprite GetGamepadSprite(string actionId)
    {
        if (Instance == null) return null;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null || string.IsNullOrEmpty(act.currentGamepadControl)) return null;

        if (CustomGamepadSpriteResolver != null)
        {
            return CustomGamepadSpriteResolver.Invoke(act.currentGamepadControl);
        }

        if (HeatSettingsBridge.Instance != null)
        {
            return HeatSettingsBridge.Instance.GetGamepadSprite(act.currentGamepadControl);
        }

        return null;
    }

    // =========================================================================
    //  Input State Checking Helpers
    // =========================================================================
    public static bool IsMouseButtonTriggered(int buttonIndex)
    {
        if (Mouse.current == null) return false;
        switch (buttonIndex)
        {
            case 0: return Mouse.current.leftButton.wasPressedThisFrame;
            case 1: return Mouse.current.rightButton.wasPressedThisFrame;
            case 2: return Mouse.current.middleButton.wasPressedThisFrame;
            case 3: return Mouse.current.forwardButton.wasPressedThisFrame;
            case 4: return Mouse.current.backButton.wasPressedThisFrame;
            default: return false;
        }
    }

    public static bool IsMouseButtonHeld(int buttonIndex)
    {
        if (Mouse.current == null) return false;
        switch (buttonIndex)
        {
            case 0: return Mouse.current.leftButton.isPressed;
            case 1: return Mouse.current.rightButton.isPressed;
            case 2: return Mouse.current.middleButton.isPressed;
            case 3: return Mouse.current.forwardButton.isPressed;
            case 4: return Mouse.current.backButton.isPressed;
            default: return false;
        }
    }

    public static bool IsActionTriggered(string actionId)
    {
        if (Instance == null) return false;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) return false;

        // 1. Check Mouse if action is bound to mouse button
        if (act.currentMouseButton >= 0 && Mouse.current != null)
        {
            if (IsMouseButtonTriggered(act.currentMouseButton))
            {
                SetGamepadActive(false);
                return true;
            }
        }

        // 2. Check Keyboard if action is bound to keyboard key
        if (act.currentKey != Key.None && Keyboard.current != null)
        {
            if (Keyboard.current[act.currentKey].wasPressedThisFrame)
            {
                SetGamepadActive(false);
                return true;
            }
        }

        // 3. Check Gamepad
        if (Gamepad.current != null && IsGamepadButtonPressed(act.currentGamepadControl))
        {
            SetGamepadActive(true);
            return true;
        }

        return false;
    }

    public static bool IsActionHeld(string actionId)
    {
        if (Instance == null) return false;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) return false;

        // 1. Check Mouse if action is bound to mouse button
        if (act.currentMouseButton >= 0 && Mouse.current != null)
        {
            if (IsMouseButtonHeld(act.currentMouseButton))
            {
                return true;
            }
        }

        // 2. Check Keyboard if action is bound to keyboard key
        if (act.currentKey != Key.None && Keyboard.current != null)
        {
            if (Keyboard.current[act.currentKey].isPressed)
            {
                return true;
            }
        }

        // 3. Check Gamepad
        if (Gamepad.current != null && IsGamepadButtonHeld(act.currentGamepadControl))
        {
            return true;
        }

        return false;
    }

    public static bool IsGamepadButtonPressed(string controlName)
    {
        if (Gamepad.current == null || string.IsNullOrEmpty(controlName)) return false;
        var control = Gamepad.current.TryGetChildControl(controlName);
        if (control is ButtonControl btn) return btn.wasPressedThisFrame;
        return false;
    }

    public static bool IsGamepadButtonHeld(string controlName)
    {
        if (Gamepad.current == null || string.IsNullOrEmpty(controlName)) return false;
        var control = Gamepad.current.TryGetChildControl(controlName);
        if (control is ButtonControl btn) return btn.isPressed;
        return false;
    }
}

