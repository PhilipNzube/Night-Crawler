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
    ShadowTeleport
}

/// <summary>
/// SOLID — SRP: Manages customizable keyboard, mouse, and gamepad bindings
/// with persistent PlayerPrefs storage, interactive rebinding, dynamic prompt formatting,
/// and live device detection (Keyboard/Mouse vs. Gamepad).
/// </summary>
public class KeybindingManager : MonoBehaviour
{
    public static KeybindingManager Instance { get; private set; }

    public static event Action OnBindingsChanged;
    public static event Action<bool> OnDeviceTypeChanged; // bool isGamepad

    [System.Serializable]
    public class ActionBinding
    {
        public string actionId;
        public string displayName;
        [TextArea(2, 4)]
        public string tacticalDescription;
        public Key defaultKey;
        public string defaultGamepadControl; // e.g. "buttonSouth", "buttonWest", etc.

        // Runtime State
        [HideInInspector] public Key currentKey;
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
                                           Mouse.current.middleButton.wasPressedThisFrame))
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

            new ActionBinding
            {
                actionId = "Sprint",
                displayName = "Sprint / Tactical Rush",
                tacticalDescription = "Accelerate traversal across open mining caverns. Depletes stamina; increases acoustic profile.",
                defaultKey = Key.LeftShift,
                defaultGamepadControl = "leftStickPress" // L3
            },
            new ActionBinding
            {
                actionId = "Jump",
                displayName = "Jump / Vault Obstacles",
                tacticalDescription = "Leap over collapsed mine rails, low pipes, and treacherous rock fissures.",
                defaultKey = Key.Space,
                defaultGamepadControl = "buttonSouth" // A / Cross
            },

            // --- Tactical Powers & Combat ---
            new ActionBinding
            {
                actionId = "Manifest",
                displayName = "Physical Manifestation",
                tacticalDescription = "Shift from incorporeal spirit form into physical reality to execute lethal strikes and hunt explorers. Consumes manifestation charges.",
                defaultKey = Key.T,
                defaultGamepadControl = "leftTrigger" // LT / L2
            },
            new ActionBinding
            {
                actionId = "Exorcism",
                displayName = "Holy Exorcism Rite",
                tacticalDescription = "Channel sanctified rite against possessed companions to purge the invading demon and heavily drain her possession reserves.",
                defaultKey = Key.R,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },
            new ActionBinding
            {
                actionId = "Heal",
                displayName = "Administer Medical Vial",
                tacticalDescription = "Inject coagulant from your medical vial supply to rapidly stabilize critical trauma and restore vital health points.",
                defaultKey = Key.H,
                defaultGamepadControl = "dpadDown"
            },
            new ActionBinding
            {
                actionId = "Resist",
                displayName = "Resist Possession",
                tacticalDescription = "Fight back against demonic host intrusion and reclaim somatic motor control during struggle.",
                defaultKey = Key.F,
                defaultGamepadControl = "buttonSouth" // A / Cross
            },

            // --- Deals & Possession Controls ---
            new ActionBinding
            {
                actionId = "Deal",
                displayName = "Deal Activations",
                tacticalDescription = "Initiate and negotiate forbidden subterranean deals and demonic blood pacts with the Vengeful Spirit.",
                defaultKey = Key.B,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },
            new ActionBinding
            {
                actionId = "ExitPossession",
                displayName = "Exit Possession",
                tacticalDescription = "Voluntarily terminate host possession to preserve ethereal essence and revert to incorporeal ghost form.",
                defaultKey = Key.E,
                defaultGamepadControl = "buttonWest" // X / Square
            },
            new ActionBinding
            {
                actionId = "Loot",
                displayName = "Loot Bodies & Containers",
                tacticalDescription = "Scavenge fallen explorer corpses, extraction batteries, supply crates, and medical vials.",
                defaultKey = Key.E,
                defaultGamepadControl = "buttonWest" // X / Square
            },

            // --- Spectating & Navigation ---
            new ActionBinding
            {
                actionId = "SpectateCycle",
                displayName = "Spectate Cycle Target",
                tacticalDescription = "Cycle camera view between surviving explorers or stalking demonic entities during spectator observation.",
                defaultKey = Key.Tab,
                defaultGamepadControl = "rightShoulder" // RB / R1
            },
            new ActionBinding
            {
                actionId = "SpectatePrev",
                displayName = "Spectate Previous Target",
                tacticalDescription = "Cycle camera to the previous living survivor or summoned monster.",
                defaultKey = Key.A,
                defaultGamepadControl = "leftShoulder"
            },
            new ActionBinding
            {
                actionId = "SpectateNext",
                displayName = "Spectate Next Target",
                tacticalDescription = "Cycle camera to the next living survivor or summoned monster.",
                defaultKey = Key.D,
                defaultGamepadControl = "rightShoulder"
            },
            new ActionBinding
            {
                actionId = "SpectateViewMode",
                displayName = "Spectate View Mode",
                tacticalDescription = "Toggle between tactical free orbit mouse look and follow over-the-shoulder camera.",
                defaultKey = Key.Space,
                defaultGamepadControl = "buttonSouth"
            },
            new ActionBinding
            {
                actionId = "SpectateCursor",
                displayName = "Spectate Cursor Lock",
                tacticalDescription = "Lock or release mouse cursor while spectating.",
                defaultKey = Key.LeftAlt,
                defaultGamepadControl = "rightStickPress"
            },
            new ActionBinding
            {
                actionId = "SpectateCategory",
                displayName = "Spectate Switch Category",
                tacticalDescription = "Switch spectator observation between living human survivors and summoned monsters.",
                defaultKey = Key.Tab,
                defaultGamepadControl = "buttonNorth" // Y / Triangle
            },
            new ActionBinding
            {
                actionId = "SpectateExit",
                displayName = "Exit Spectator Mode",
                tacticalDescription = "Exit spectator observation mode and return to your player view.",
                defaultKey = Key.C,
                defaultGamepadControl = "select" // View / Share
            },
            new ActionBinding
            {
                actionId = "CommandHunt",
                displayName = "Command: Go Hunt",
                tacticalDescription = "Command all summoned monsters to roam and hunt down surviving investigators.",
                defaultKey = Key.Digit3,
                defaultGamepadControl = "dpadLeft"
            },
            new ActionBinding
            {
                actionId = "CommandRecall",
                displayName = "Command: To My Side",
                tacticalDescription = "Command all summoned monsters to return and stand guard beside the Vengeful Spirit.",
                defaultKey = Key.Digit4,
                defaultGamepadControl = "dpadRight"
            },
            new ActionBinding
            {
                actionId = "Summon",
                displayName = "Monster Summon Rite",
                tacticalDescription = "Open the necrotic summon rites menu to raise Undead and Berserker abominations in the subterranean mines.",
                defaultKey = Key.X,
                defaultGamepadControl = "dpadUp"
            },
            new ActionBinding
            {
                actionId = "Possession",
                displayName = "Possession Selection Menu",
                tacticalDescription = "Open the possession target selection modal to choose an investigator host to inhabit.",
                defaultKey = Key.P,
                defaultGamepadControl = "dpadDown"
            },
            new ActionBinding
            {
                actionId = "Attack",
                displayName = "Attack / Fire Weapon",
                tacticalDescription = "Execute tactical melee strikes with pickaxe or fire equipped firearms.",
                defaultKey = Key.None,
                defaultGamepadControl = "rightTrigger"
            },
            new ActionBinding
            {
                actionId = "Reload",
                displayName = "Reload Firearm",
                tacticalDescription = "Chamber ammunition into your equipped firearm.",
                defaultKey = Key.R,
                defaultGamepadControl = "buttonWest"
            },
            new ActionBinding
            {
                actionId = "Holster",
                displayName = "Holster / Stow Weapon",
                tacticalDescription = "Conceal equipped weaponry to maintain low acoustic and visual profile.",
                defaultKey = Key.X,
                defaultGamepadControl = "dpadDown"
            },
            new ActionBinding
            {
                actionId = "EquipWeapon1",
                displayName = "Equip Primary Weapon",
                tacticalDescription = "Draw primary tactical pickaxe or melee tool.",
                defaultKey = Key.Digit1,
                defaultGamepadControl = "dpadLeft"
            },
            new ActionBinding
            {
                actionId = "EquipWeapon2",
                displayName = "Equip Secondary Weapon",
                tacticalDescription = "Draw secondary firearm if acquired from fallen miners or supply caches.",
                defaultKey = Key.Digit2,
                defaultGamepadControl = "dpadRight"
            },
            new ActionBinding
            {
                actionId = "VoiceChat",
                displayName = "Push-to-Talk (Radio)",
                tacticalDescription = "Transmit tactical radio communication across the subterranean network.",
                defaultKey = Key.V,
                defaultGamepadControl = "leftStickPress"
            },
            new ActionBinding
            {
                actionId = "ShadowTeleport",
                displayName = "Shadow Teleport",
                tacticalDescription = "Vanish and remanifest instantaneously behind unsuspecting explorers.",
                defaultKey = Key.F,
                defaultGamepadControl = "buttonEast"
            }
        };

        actions = new List<ActionBinding>(defaultList);

        foreach (var act in actions)
        {
            act.currentKey = act.defaultKey;
            act.currentGamepadControl = act.defaultGamepadControl;
        }
    }

    public void LoadBindings()
    {
        foreach (var act in actions)
        {
            string keyStr = PlayerPrefs.GetString($"NC_Bind_Key_{act.actionId}", act.defaultKey.ToString());
            if (Enum.TryParse<Key>(keyStr, out Key loadedKey))
            {
                act.currentKey = loadedKey;
            }
            else
            {
                act.currentKey = act.defaultKey;
            }

            act.currentGamepadControl = PlayerPrefs.GetString($"NC_Bind_Pad_{act.actionId}", act.defaultGamepadControl);
        }
    }

    public void SaveBindings()
    {
        foreach (var act in actions)
        {
            PlayerPrefs.SetString($"NC_Bind_Key_{act.actionId}", act.currentKey.ToString());
            PlayerPrefs.SetString($"NC_Bind_Pad_{act.actionId}", act.currentGamepadControl);
        }
        PlayerPrefs.Save();
        ApplyToAllPlayerInputs();
        OnBindingsChanged?.Invoke();
    }

    public void ResetAllBindings()
    {
        foreach (var act in actions)
        {
            act.currentKey = act.defaultKey;
            act.currentGamepadControl = act.defaultGamepadControl;
        }
        SaveBindings();
    }

    // =========================================================================
    //  Interactive Rebinding
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

    private IEnumerator RebindKeyboardRoutine(string actionId, Action<string> onWaiting, Action<string> onComplete)
    {
        var act = actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) yield break;

        onWaiting?.Invoke("<color=#F1C40F>[PRESS ANY KEY...]</color>");
        yield return null;

        bool bound = false;
        while (!bound)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    break;
                }

                foreach (var control in Keyboard.current.allControls)
                {
                    if (control is KeyControl keyControl && keyControl.wasPressedThisFrame)
                    {
                        Key newKey = keyControl.keyCode;
                        if (newKey == Key.None) break;

                        // Check for duplicate key conflict
                        ActionBinding conflict = actions.Find(other => other != act && other.currentKey == newKey && other.currentKey != Key.None);
                        if (conflict != null)
                        {
                            string keyName = FormatKeyName(newKey);
                            ShowConflictError("Key Conflict", $"Key '{keyName}' is already mapped to '{conflict.displayName}'!\nPlease choose an unassigned key or rebind that command first.");
                            // Revert button text and cancel without saving changes
                            onComplete?.Invoke(FormatKeyName(act.currentKey));
                            _rebindCoroutine = null;
                            yield break;
                        }

                        act.currentKey = newKey;
                        bound = true;
                        break;
                    }
                }
            }

            yield return null;
        }

        SaveBindings();
        onComplete?.Invoke(FormatKeyName(act.currentKey));
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

                        // Check for duplicate gamepad button conflict
                        ActionBinding conflict = actions.Find(other => other != act && !string.IsNullOrEmpty(other.currentGamepadControl) && other.currentGamepadControl.Equals(newControl, StringComparison.OrdinalIgnoreCase));
                        if (conflict != null)
                        {
                            string btnName = FormatGamepadName(newControl, IsPlayStationActive());
                            ShowConflictError("Button Conflict", $"Button '{btnName}' is already mapped to '{conflict.displayName}'!\nPlease choose an unassigned button or rebind that command first.");
                            // Revert button visual and cancel without saving changes
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
            NotificationManager.Instance.ShowNotification($"{title}: {description}", 4.5f);
        }
    }

    public static void ApplyToPlayerInput(PlayerInput playerInput)
    {
        if (playerInput == null || playerInput.actions == null || Instance == null) return;

        var jumpAct = playerInput.actions.FindAction("Jump");
        if (jumpAct != null)
        {
            var jumpBinding = Instance.actions.Find(a => a.actionId.Equals("Jump", StringComparison.OrdinalIgnoreCase));
            if (jumpBinding != null && jumpBinding.currentKey != Key.None)
            {
                string kPath = GetInputControlPath(jumpBinding.currentKey);
                ApplyBindingOverrideToGroup(jumpAct, "KeyboardMouse", kPath);
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
            if (sprintBinding != null && sprintBinding.currentKey != Key.None)
            {
                string kPath = GetInputControlPath(sprintBinding.currentKey);
                ApplyBindingOverrideToGroup(sprintAct, "KeyboardMouse", kPath);
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

    // =========================================================================
    //  Formatting & Lookup Helpers
    // =========================================================================
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
            default: return $"[{key.ToString().ToUpper()}]";
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
            default: return key.ToString().ToUpper();
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
        return act != null ? FormatKeyRaw(act.currentKey) : defaultFallback;
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
            : FormatKeyName(act.currentKey);

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
    public static bool IsActionTriggered(string actionId)
    {
        if (Instance == null) return false;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (act == null) return false;

        // Check Mouse for Attack action or if left click pressed
        if (actionId.Equals("Attack", StringComparison.OrdinalIgnoreCase))
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetGamepadActive(false);
                return true;
            }
        }

        // Check Keyboard
        if (Keyboard.current != null && act.currentKey != Key.None && Keyboard.current[act.currentKey].wasPressedThisFrame)
        {
            SetGamepadActive(false);
            return true;
        }

        // Check Gamepad
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

        // Check Mouse for Attack action
        if (actionId.Equals("Attack", StringComparison.OrdinalIgnoreCase))
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                return true;
            }
        }

        // Check Keyboard
        if (Keyboard.current != null && act.currentKey != Key.None && Keyboard.current[act.currentKey].isPressed)
        {
            return true;
        }

        // Check Gamepad
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
