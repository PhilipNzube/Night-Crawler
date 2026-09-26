using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// SOLID — SRP: Manages customizable keyboard, mouse, and gamepad bindings
/// with persistent PlayerPrefs storage, interactive rebinding, and friendly display formatting.
/// </summary>
public class KeybindingManager : MonoBehaviour
{
    public static KeybindingManager Instance { get; private set; }

    public static event Action OnBindingsChanged;

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

    public void InitializeDefaults()
    {
        if (actions == null || actions.Count == 0)
        {
            actions = new List<ActionBinding>()
            {
                new ActionBinding
                {
                    actionId = "Interact",
                    displayName = "Interact / Possess",
                    tacticalDescription = "Open bulkheads, collect extraction batteries, access maintenance terminals, or initiate host possession.",
                    defaultKey = Key.E,
                    defaultGamepadControl = "buttonWest" // X / Square
                },
                new ActionBinding
                {
                    actionId = "Sprint",
                    displayName = "Sprint / Rush",
                    tacticalDescription = "Accelerate traversal across open mining caverns. Depletes stamina; increases footstep acoustic profile.",
                    defaultKey = Key.LeftShift,
                    defaultGamepadControl = "leftStickPress" // L3
                },
                new ActionBinding
                {
                    actionId = "Jump",
                    displayName = "Jump / Vault",
                    tacticalDescription = "Leap over collapsed mine rails, low pipes, and treacherous rock fissures.",
                    defaultKey = Key.Space,
                    defaultGamepadControl = "buttonSouth" // A / Cross
                },
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
                    displayName = "Resist Exorcism (QTE)",
                    tacticalDescription = "Fight back against demonic host intrusion and reclaim somatic motor control during holy possession struggles.",
                    defaultKey = Key.F,
                    defaultGamepadControl = "buttonSouth" // A / Cross
                },
                new ActionBinding
                {
                    actionId = "Pause",
                    displayName = "Tactical Menu / Pause",
                    tacticalDescription = "Access environmental diagnostics, audio acoustic mixers, and system configuration.",
                    defaultKey = Key.Escape,
                    defaultGamepadControl = "start"
                }
            };
        }

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
        var act = actions.Find(a => a.actionId == actionId);
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
                    // Cancel rebinding
                    break;
                }

                foreach (var control in Keyboard.current.allControls)
                {
                    if (control is KeyControl keyControl && keyControl.wasPressedThisFrame)
                    {
                        act.currentKey = keyControl.keyCode;
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
        var act = actions.Find(a => a.actionId == actionId);
        if (act == null) yield break;

        onWaiting?.Invoke("<color=#F1C40F>[PRESS BUTTON...]</color>");
        yield return null;

        bool bound = false;
        while (!bound)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                // Cancel
                break;
            }

            if (Gamepad.current != null)
            {
                foreach (var control in Gamepad.current.allControls)
                {
                    if (control is ButtonControl btnControl && btnControl.wasPressedThisFrame)
                    {
                        act.currentGamepadControl = btnControl.name;
                        bound = true;
                        break;
                    }
                }
            }

            yield return null;
        }

        SaveBindings();
        onComplete?.Invoke(FormatGamepadName(act.currentGamepadControl));
        _rebindCoroutine = null;
    }

    // =========================================================================
    //  Formatting Helpers
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

    public static string FormatGamepadName(string controlName)
    {
        if (string.IsNullOrEmpty(controlName)) return "[UNBOUND]";

        switch (controlName.ToLower())
        {
            case "buttonsouth": return "[A / ✕]";
            case "buttonnorth": return "[Y / △]";
            case "buttonwest": return "[X / ▢]";
            case "buttoneast": return "[B / ◯]";
            case "leftshoulder": return "[LB / L1]";
            case "rightshoulder": return "[RB / R1]";
            case "lefttrigger": return "[LT / L2]";
            case "righttrigger": return "[RT / R2]";
            case "leftstickpress": return "[L3 / L-STICK]";
            case "rightstickpress": return "[R3 / R-STICK]";
            case "dpadup": return "[D-PAD UP]";
            case "dpaddown": return "[D-PAD DOWN]";
            case "dpadleft": return "[D-PAD LEFT]";
            case "dpadright": return "[D-PAD RIGHT]";
            case "start": return "[START / MENU]";
            case "select": return "[BACK / VIEW]";
            default: return $"[{controlName.ToUpper()}]";
        }
    }

    public static Key GetActionKey(string actionId, Key defaultKey)
    {
        if (Instance == null) return defaultKey;
        var act = Instance.actions.Find(a => a.actionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return act != null ? act.currentKey : defaultKey;
    }
}
